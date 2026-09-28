using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Mirro.Core;
using Mirro.Items;
using Mirro.Maze;
using Mirro.Networking;

namespace Mirro.Gameplay
{
    /// <summary>서버가 게임 시작 때 넘기는 플레이어 한 명의 정보(깃발을 어디에 세울지 포함).</summary>
    public struct MatchPlayer
    {
        public ulong clientId;
        public int colorIndex;
        public Vector3 flagPosition;
        public float flagYaw;
    }

    /// <summary>깃발 찾기 모드에서 미로에 세우는 주인 없는 깃발 하나.</summary>
    public struct TreasureFlag
    {
        public ulong id;
        public Vector3 position;
        public float yaw;
    }

    /// <summary>
    /// 한 판의 진행 상태와 규칙. 깃발 뽑기를 서버가 검증해 탈락을 결정하고, 한 명만 남으면 게임을 끝낸다.
    /// 탈락 기록/승자/시간은 모든 피어에 복제되어 HUD와 결과 화면이 그대로 읽는다.
    /// </summary>
    public class MatchManager : NetworkBehaviour
    {
        /// <summary>깃발을 뽑을 수 있는 거리(수평, m). 같은 칸 안에 있어야 한다는 조건도 함께 필요하다.</summary>
        public const float PullRange = 2.4f;

        /// <summary>깃발을 뽑는 데 E를 누르고 있어야 하는 시간(초).</summary>
        public const float PullSeconds = 1.5f;

        public const ulong NoOne = ulong.MaxValue;

        /// <summary>봇의 PlayerId는 BotIdBase + n, 주인 없는 금색 깃발은 TreasureIdBase + n.</summary>
        public const ulong BotIdBase = 1000;
        public const ulong TreasureIdBase = 2000;

        public static bool IsBotId(ulong id) => id >= BotIdBase && id < TreasureIdBase;

        public static bool IsTreasureId(ulong id) => id >= TreasureIdBase && id < NoOne;

        // 서버가 보는 플레이어 위치는 조금 늦으므로 서버 쪽 거리 판정만 여유를 둔다.
        private const float ServerRangeSlack = 1f;

        // 깃발은 시작 지점의 뒤쪽 왼쪽 모퉁이에 세운다(시작하자마자 깃발 위에 서지 않도록).
        private static readonly Vector3 FlagOffset = new Vector3(-1f, 0f, -1f);

        public static MatchManager Instance { get; private set; }

        /// <summary>탈락한 순서대로 쌓이는 기록. 마지막 생존자(승자)는 여기에 들어가지 않는다.</summary>
        public NetworkList<MatchEntry> Eliminated;

        public readonly NetworkVariable<int> Mode = new NetworkVariable<int>((int)GameMode.Versus);
        public readonly NetworkVariable<int> TotalPlayers = new NetworkVariable<int>();
        public readonly NetworkVariable<int> TotalTreasures = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Collected = new NetworkVariable<int>();
        public readonly NetworkVariable<bool> Finished = new NetworkVariable<bool>();
        public readonly NetworkVariable<ulong> WinnerId = new NetworkVariable<ulong>(NoOne);
        public readonly NetworkVariable<int> WinnerColor = new NetworkVariable<int>();

        // 서버 시계(초). 모든 피어가 같은 값으로 경기 시간을 계산한다.
        public readonly NetworkVariable<double> StartTime = new NetworkVariable<double>();
        public readonly NetworkVariable<double> EndTime = new NetworkVariable<double>();

        private readonly HashSet<ulong> _alive = new HashSet<ulong>();
        private readonly Dictionary<ulong, int> _colors = new Dictionary<ulong, int>();
        private ulong _humanId = NoOne;
        private ItemSpawner _items;

        public GameMode CurrentMode => (GameMode)Mode.Value;

        public int AliveCount => Mathf.Max(0, TotalPlayers.Value - Eliminated.Count);

        /// <summary>경기 시작부터 지금(끝났다면 끝난 순간)까지의 시간(초).</summary>
        public double Elapsed
        {
            get
            {
                double now = Finished.Value ? EndTime.Value : NetworkManager != null ? NetworkManager.ServerTime.Time : 0.0;
                return Math.Max(0.0, now - StartTime.Value);
            }
        }

        private void Awake()
        {
            Eliminated = new NetworkList<MatchEntry>();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        public override void OnDestroy()
        {
            Eliminated?.Dispose();
            base.OnDestroy();
        }

        public bool IsEliminated(ulong playerId)
        {
            for (int i = 0; i < Eliminated.Count; i++)
                if (Eliminated[i].clientId == playerId) return true;
            return false;
        }

        /// <summary>플레이어의 시작 위치/방향으로부터 깃발을 세울 자리(바닥 높이).</summary>
        public static Vector3 FlagPositionFor(Vector3 spawnPosition, Quaternion spawnRotation)
        {
            return new Vector3(spawnPosition.x, 0f, spawnPosition.z) + spawnRotation * FlagOffset;
        }

        /// <summary>
        /// position에 선 플레이어가 깃발을 뽑을 수 있는 자리인지. 벽 너머로 손이 닿는 걸 막으려고
        /// 깃발과 같은 칸 안에 있어야 하고, 그 안에서 수평 거리가 PullRange 이내여야 한다.
        /// </summary>
        public static bool CanReach(Vector3 position, Flag flag, float slack)
        {
            var bootstrap = MazeGameBootstrap.Instance;
            if (bootstrap == null || flag == null) return false;

            Vector3 flagPosition = flag.transform.position;
            float dx = position.x - flagPosition.x;
            float dz = position.z - flagPosition.z;
            float range = PullRange + slack;
            if (dx * dx + dz * dz > range * range) return false;

            return SpawnPlacer.CellAt(position, bootstrap.CellSize) == SpawnPlacer.CellAt(flagPosition, bootstrap.CellSize);
        }

        // ---- 서버 전용 ----

        /// <summary>
        /// 경기를 시작한다. 플레이어(봇 포함)가 모두 스폰된 뒤 한 번 호출한다. Versus/Bots에서는 플레이어마다 깃발을,
        /// Treasure에서는 주인 없는 금색 깃발을 세운다. Practice에는 깃발이 없다.
        /// </summary>
        public void ServerBegin(GameMode mode, IReadOnlyList<MatchPlayer> players, IReadOnlyList<TreasureFlag> treasures)
        {
            if (!IsServer) return;

            Mode.Value = (int)mode;
            var flagPrefab = Resources.Load<GameObject>("Prefabs/Flag");
            foreach (var player in players)
            {
                _alive.Add(player.clientId);
                _colors[player.clientId] = player.colorIndex;
                if (_humanId == NoOne && !IsBotId(player.clientId)) _humanId = player.clientId;

                if (mode != GameMode.Versus && mode != GameMode.Bots) continue;
                SpawnFlag(flagPrefab, player.clientId, player.colorIndex, player.flagPosition, player.flagYaw);
            }
            foreach (var treasure in treasures)
                SpawnFlag(flagPrefab, treasure.id, 0, treasure.position, treasure.yaw);

            if (mode == GameMode.Versus || mode == GameMode.Bots)
            {
                var bootstrap = MazeGameBootstrap.Instance;
                if (bootstrap != null && bootstrap.Maze != null)
                {
                    var starts = new List<Vector2Int>();
                    foreach (var matchPlayer in players)
                        starts.Add(SpawnPlacer.CellAt(matchPlayer.flagPosition, bootstrap.CellSize));
                    _items = gameObject.AddComponent<ItemSpawner>();
                    _items.Begin(bootstrap.Maze, bootstrap.CellSize, starts);
                }
            }

            TotalPlayers.Value = players.Count;
            TotalTreasures.Value = treasures.Count;
            StartTime.Value = NetworkManager.ServerTime.Time;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            Debug.Log($"[Mirro] Match begins ({mode}) with {players.Count} player(s) and {Flag.All.Count} flag(s)");
        }

        private static void SpawnFlag(GameObject prefab, ulong id, int colorIndex, Vector3 position, float yaw)
        {
            var go = Instantiate(prefab, position, Quaternion.Euler(0f, yaw, 0f));
            go.GetComponent<Flag>().InitServer(id, colorIndex);
            go.GetComponent<NetworkObject>().Spawn(true);
        }

        /// <summary>아이템 줍기 요청을 검증하고 통과하면 슬롯에 넣는다(기존 아이템은 교체). 처리했으면 true.</summary>
        public bool ServerRequestPickup(ulong playerId, ulong pickupObjectId)
        {
            if (!IsServer || Finished.Value || _items == null || !_alive.Contains(playerId)) return false;

            var player = NetworkPlayer.Find(playerId);
            var pickup = ItemPickup.FindByObjectId(pickupObjectId);
            if (player == null || pickup == null || !pickup.IsSpawned || player.IsStunnedNow) return false;

            Vector3 delta = pickup.transform.position - player.transform.position;
            delta.y = 0f;
            if (delta.magnitude > ItemRules.PickupServerRange) return false;

            Vector3 at = pickup.transform.position;
            player.HeldItem.Value = (int)pickup.Type;
            _items.OnPickedUp(pickup);
            if (!player.IsBot) player.PickupConfirmedRpc(at);
            return true;
        }

        /// <summary>
        /// 들고 있는 아이템을 쓴다. 신의 손은 대상(살아 있는 누구든, 나 포함)을 그의 시작 지점으로 보내고, 번개는 나 외 전원을 경직시키고,
        /// 레이더는 나에게 미니맵을 띄우고, 칼은 정면 가까운 상대 한 명을 경직시킨다. 쓰면 슬롯이 빈다(신의 손은 대상이 없으면 소모하지 않음).
        /// </summary>
        public bool ServerUseItem(ulong playerId, ulong targetPlayerId)
        {
            if (!IsServer || Finished.Value || !_alive.Contains(playerId)) return false;

            var caster = NetworkPlayer.Find(playerId);
            if (caster == null || !caster.IsSpawned || caster.IsStunnedNow) return false;

            switch (caster.Held)
            {
                case ItemType.GodsHand:
                    var target = NetworkPlayer.Find(targetPlayerId);
                    if (target == null || !target.IsSpawned || !_alive.Contains(targetPlayerId)) return false;
                    target.TeleportToSpawnRpc();
                    break;
                case ItemType.Lightning:
                    foreach (var other in NetworkPlayer.All.ToArray())
                        if (other != caster && other.IsSpawned && _alive.Contains(other.PlayerId))
                            Stun(other, ItemRules.LightningStunSeconds, false);
                    break;
                case ItemType.Radar:
                    if (!caster.IsBot) caster.RadarPulseRpc();
                    break;
                case ItemType.Knife:
                    var hit = FindKnifeTarget(caster);
                    if (hit != null) Stun(hit, ItemRules.KnifeStunSeconds, true);
                    break;
                default:
                    return false;
            }

            caster.HeldItem.Value = (int)ItemType.None;
            return true;
        }

        private void Stun(NetworkPlayer player, float seconds, bool accumulate)
        {
            double now = NetworkManager.ServerTime.Time;
            double current = player.StunnedUntil.Value;
            player.StunnedUntil.Value = accumulate ? Math.Max(now, current) + seconds : Math.Max(current, now + seconds);
        }

        /// <summary>캐스터의 위치와 정면 기준 사거리/각도 안에서 벽에 가리지 않은 가장 가까운 살아 있는 상대.</summary>
        private NetworkPlayer FindKnifeTarget(NetworkPlayer caster)
        {
            Vector3 origin = caster.transform.position;
            Vector3 forward = caster.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return null;
            forward.Normalize();

            float maxDistance = ItemRules.KnifeRange + ItemRules.KnifeServerSlack;
            NetworkPlayer best = null;
            float bestDistance = maxDistance;
            foreach (var other in NetworkPlayer.All)
            {
                if (other == caster || !other.IsSpawned || !_alive.Contains(other.PlayerId)) continue;

                Vector3 delta = other.transform.position - origin;
                delta.y = 0f;
                float distance = delta.magnitude;
                if (distance > maxDistance || distance < 0.01f) continue;
                if (Vector3.Dot(forward, delta / distance) < ItemRules.KnifeCosHalfAngle) continue;
                if (distance >= bestDistance || WallBetween(origin + Vector3.up, other.transform.position + Vector3.up)) continue;

                best = other;
                bestDistance = distance;
            }
            return best;
        }

        private static bool WallBetween(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return false;

            foreach (var hit in Physics.RaycastAll(from, delta / distance, distance, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<NetworkPlayer>() == null) return true;
            return false;
        }

        /// <summary>깃발 뽑기 요청을 검증하고 통과하면 주인을 탈락시킨다(금색 깃발이면 수집). 처리했으면 true.</summary>
        public bool ServerTryPullFlag(ulong puller, ulong target)
        {
            if (!IsServer || Finished.Value) return false;
            if (IsTreasureId(target)) return TryCollectTreasure(puller, target);
            if (puller == target || !_alive.Contains(puller) || !_alive.Contains(target)) return false;

            var pullerPlayer = NetworkPlayer.Find(puller);
            var flag = Flag.FindFor(target);
            if (pullerPlayer == null || flag == null) return false;

            if (!CanReach(pullerPlayer.transform.position, flag, ServerRangeSlack))
            {
                Debug.Log($"[Mirro] Rejected flag pull: player {puller} is out of reach of player {target}'s flag");
                return false;
            }

            Eliminate(target, puller);
            return true;
        }

        private bool TryCollectTreasure(ulong puller, ulong flagId)
        {
            if (CurrentMode != GameMode.Treasure || !_alive.Contains(puller)) return false;

            var pullerPlayer = NetworkPlayer.Find(puller);
            var flag = Flag.FindFor(flagId);
            if (pullerPlayer == null || flag == null || !CanReach(pullerPlayer.transform.position, flag, ServerRangeSlack)) return false;

            flag.NetworkObject.Despawn(true);
            Collected.Value++;
            Debug.Log($"[Mirro] Treasure collected: {Collected.Value}/{TotalTreasures.Value}");
            if (Collected.Value >= TotalTreasures.Value) Finish(puller);
            return true;
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (Finished.Value || !_alive.Contains(clientId)) return;
            Eliminate(clientId, NoOne);
        }

        private void Eliminate(ulong target, ulong by)
        {
            if (NetworkManager == null || NetworkManager.ShutdownInProgress || !IsSpawned) return;
            if (!_alive.Remove(target)) return;

            var player = NetworkPlayer.Find(target);
            if (player != null && player.IsSpawned) player.IsAlive.Value = false;

            var flag = Flag.FindFor(target);
            if (flag != null && flag.NetworkObject.IsSpawned) flag.NetworkObject.Despawn(true);

            Eliminated.Add(new MatchEntry { clientId = target, colorIndex = _colors[target], eliminatedBy = by });
            Debug.Log(by == NoOne
                ? $"[Mirro] Player {target} was eliminated (left the game); {_alive.Count} left"
                : $"[Mirro] Player {target} was eliminated by player {by}; {_alive.Count} left");

            CheckForWinner();
        }

        private void CheckForWinner()
        {
            if (Finished.Value) return;

            switch (CurrentMode)
            {
                case GameMode.Practice:
                case GameMode.Treasure:
                    return;
                case GameMode.Bots:
                    // 사람이 탈락하면 남은 봇끼리 계속 싸우지 않고 바로 끝낸다(패배).
                    if (_humanId != NoOne && !_alive.Contains(_humanId))
                    {
                        Finish(NoOne);
                        return;
                    }
                    break;
            }

            if (TotalPlayers.Value < 2 || _alive.Count > 1) return;

            ulong winner = NoOne;
            foreach (ulong id in _alive) winner = id;
            Finish(winner);
        }

        private void Finish(ulong winner)
        {
            WinnerId.Value = winner;
            WinnerColor.Value = winner != NoOne && _colors.TryGetValue(winner, out int color) ? color : 0;
            EndTime.Value = NetworkManager.ServerTime.Time;
            Finished.Value = true;
            Debug.Log($"[Mirro] Match finished; winner = {(winner == NoOne ? "nobody" : "player " + winner)}");
        }
    }
}
