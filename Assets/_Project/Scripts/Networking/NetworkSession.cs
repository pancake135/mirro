using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirro.Core;
using Mirro.Gameplay;
using Mirro.Themes;

namespace Mirro.Networking
{
    public enum SessionPhase
    {
        Lobby = 0,
        InGame = 1
    }

    /// <summary>
    /// 방 하나의 공유 상태(플레이어 슬롯, 준비 여부, 테마/크기, 진행 단계). 호스트가 방을 만들 때 스폰되어
    /// 로비 → 게임 씬 전환 후에도 DontDestroyOnLoad로 유지된다. 게임 시작/씬 준비 핸드셰이크도 여기서 처리한다.
    /// </summary>
    public class NetworkSession : NetworkBehaviour
    {
        public const int MaxPlayers = 4;
        // 혼자서는 이길 상대가 없으므로 2명부터 시작할 수 있다(자동 테스트만 1로 낮춘다).
        public static int MinPlayersToStart = 2;

        public static NetworkSession Instance { get; private set; }

        public readonly NetworkVariable<int> ThemeIndex = new NetworkVariable<int>();
        public readonly NetworkVariable<int> MazeSize = new NetworkVariable<int>(50);
        public readonly NetworkVariable<int> Phase = new NetworkVariable<int>((int)SessionPhase.Lobby);

        // 혼자 하기 설정. 멀티(Versus) 방에서는 기본값 그대로 쓰지 않는다.
        public readonly NetworkVariable<int> Mode = new NetworkVariable<int>((int)GameMode.Versus);
        public readonly NetworkVariable<int> BotCount = new NetworkVariable<int>(3);
        public readonly NetworkVariable<int> BotDifficulty = new NetworkVariable<int>((int)Mirro.Gameplay.BotDifficulty.Normal);
        public readonly NetworkVariable<bool> IsSolo = new NetworkVariable<bool>();

        public NetworkList<PlayerSlot> Slots;

        /// <summary>서버 전용: 모든 슬롯의 클라이언트가 게임 씬 준비를 마쳤을 때 한 번 발생한다.</summary>
        public event Action AllSceneReady;

        private readonly HashSet<ulong> _sceneReady = new HashSet<ulong>();
        private bool _allReadyRaised;

        public bool IsSceneReady(ulong clientId) => _sceneReady.Contains(clientId);

        private void Awake()
        {
            Slots = new NetworkList<PlayerSlot>();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (!IsServer) return;

            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            foreach (ulong id in NetworkManager.ConnectedClientsIds)
                AddSlot(id);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
        }

        public override void OnDestroy()
        {
            Slots?.Dispose();
            base.OnDestroy();
        }

        public SessionPhase CurrentPhase => (SessionPhase)Phase.Value;

        public GameMode CurrentMode => (GameMode)Mode.Value;

        public MazeThemeConfig Theme => ThemeLibrary.All[Mathf.Clamp(ThemeIndex.Value, 0, ThemeLibrary.All.Count - 1)];

        public bool TryGetSlot(ulong clientId, out PlayerSlot slot)
        {
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i].clientId == clientId)
                {
                    slot = Slots[i];
                    return true;
                }
            }
            slot = default;
            return false;
        }

        public bool CanStart
        {
            get
            {
                if (!IsServer || CurrentPhase != SessionPhase.Lobby || Slots.Count < MinPlayersToStart)
                    return false;
                for (int i = 0; i < Slots.Count; i++)
                    if (!Slots[i].ready) return false;
                return true;
            }
        }

        // ---- 서버 전용 ----

        public void Configure(int themeIndex, int size)
        {
            if (!IsServer) return;
            ThemeIndex.Value = themeIndex;
            MazeSize.Value = size;
        }

        /// <summary>메뉴를 거치지 않고 Game 씬을 직접 실행한 경우(개발용)에 로비 단계를 건너뛴다.</summary>
        public void ForceInGame()
        {
            if (IsServer) Phase.Value = (int)SessionPhase.InGame;
        }

        public bool StartGame()
        {
            if (!CanStart) return false;

            Phase.Value = (int)SessionPhase.InGame;
            _sceneReady.Clear();
            _allReadyRaised = false;
            BeginGameRpc(ThemeIndex.Value, MazeSize.Value, UnityEngine.Random.Range(1, int.MaxValue));
            return true;
        }

        /// <summary>
        /// 게임 도중이나 끝난 뒤에 같은 방의 대기실로 돌아가 다시 시작할 수 있게 한다(방장만 호출). 게임 오브젝트(플레이어,
        /// 깃발, 매치)를 먼저 치워 모든 피어에 알린 뒤 준비 상태를 초기화하고 모두를 메뉴 씬(대기실)으로 보낸다.
        /// 방에 남은 사람과 색은 그대로이고, 다음 시작 때 새 미로가 만들어진다.
        /// </summary>
        public bool ReturnToLobby()
        {
            if (!IsServer || CurrentPhase != SessionPhase.InGame) return false;

            DespawnGameObjects();

            _sceneReady.Clear();
            _allReadyRaised = false;
            for (int i = 0; i < Slots.Count; i++)
            {
                var slot = Slots[i];
                slot.ready = slot.clientId == NetworkManager.ServerClientId;
                Slots[i] = slot;
            }

            Phase.Value = (int)SessionPhase.Lobby;
            ReturnToLobbyRpc();
            return true;
        }

        /// <summary>서버 전용: 서버가 먼저 깃발/매치/플레이어(봇 포함)를 치워 모든 피어에 알린 뒤 씬을 바꾼다.</summary>
        private void DespawnGameObjects()
        {
            foreach (var flag in Flag.All.ToArray())
                if (flag.IsSpawned) flag.NetworkObject.Despawn(true);

            var match = MatchManager.Instance;
            if (match != null && match.IsSpawned) match.NetworkObject.Despawn(true);

            foreach (var player in NetworkPlayer.All.ToArray())
                if (player.IsSpawned) player.NetworkObject.Despawn(true);
        }

        /// <summary>서버 전용: 혼자 하기 방으로 설정한다(방을 만든 직후, 시작 전에 호출).</summary>
        public void ConfigureSolo(GameMode mode, int botCount, Mirro.Gameplay.BotDifficulty difficulty)
        {
            if (!IsServer) return;
            IsSolo.Value = true;
            Mode.Value = (int)mode;
            BotCount.Value = Mathf.Clamp(botCount, 1, MaxPlayers - 1);
            BotDifficulty.Value = (int)difficulty;
        }

        /// <summary>서버 전용: 준비/최소 인원 검사 없이 혼자 하기 게임을 바로 시작한다(새 seed).</summary>
        public bool StartSoloGame()
        {
            if (!IsServer || !IsSolo.Value || Slots.Count == 0) return false;

            Phase.Value = (int)SessionPhase.InGame;
            _sceneReady.Clear();
            _allReadyRaised = false;
            BeginGameRpc(ThemeIndex.Value, MazeSize.Value, UnityEngine.Random.Range(1, int.MaxValue));
            return true;
        }

        /// <summary>서버 전용: 같은 설정으로 새 미로에서 혼자 하기 게임을 처음부터 다시 시작한다.</summary>
        public bool RestartSolo()
        {
            if (!IsServer || !IsSolo.Value || CurrentPhase != SessionPhase.InGame) return false;

            DespawnGameObjects();
            return StartSoloGame();
        }

        private void AddSlot(ulong clientId)
        {
            if (CurrentPhase != SessionPhase.Lobby || Slots.Count >= MaxPlayers || TryGetSlot(clientId, out _))
                return;

            Slots.Add(new PlayerSlot
            {
                clientId = clientId,
                colorIndex = LowestFreeColor(),
                ready = clientId == NetworkManager.ServerClientId
            });
        }

        private int LowestFreeColor()
        {
            for (int color = 0; color < MaxPlayers; color++)
            {
                bool used = false;
                for (int i = 0; i < Slots.Count; i++)
                    if (Slots[i].colorIndex == color) used = true;
                if (!used) return color;
            }
            return 0;
        }

        private void OnClientConnected(ulong clientId) => AddSlot(clientId);

        private void OnClientDisconnected(ulong clientId)
        {
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i].clientId != clientId) continue;
                Slots.RemoveAt(i);
                break;
            }
            _sceneReady.Remove(clientId);
            CheckAllSceneReady();
        }

        private void CheckAllSceneReady()
        {
            if (_allReadyRaised || CurrentPhase != SessionPhase.InGame || Slots.Count == 0) return;
            for (int i = 0; i < Slots.Count; i++)
                if (!_sceneReady.Contains(Slots[i].clientId)) return;

            _allReadyRaised = true;
            AllSceneReady?.Invoke();
        }

        // ---- RPC ----

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SetReadyRpc(bool ready, RpcParams rpcParams = default)
        {
            if (CurrentPhase != SessionPhase.Lobby) return;

            ulong sender = rpcParams.Receive.SenderClientId;
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i].clientId != sender) continue;
                var slot = Slots[i];
                slot.ready = ready;
                Slots[i] = slot;
                return;
            }
        }

        [Rpc(SendTo.Everyone)]
        public void BeginGameRpc(int themeIndex, int size, int seed)
        {
            var themes = ThemeLibrary.All;
            GameSession.Begin(themes[Mathf.Clamp(themeIndex, 0, themes.Count - 1)], size, seed);
            SceneManager.LoadScene(GameSession.GameScene);
        }

        [Rpc(SendTo.Everyone)]
        public void ReturnToLobbyRpc()
        {
            GameSession.Clear();
            SceneManager.LoadScene(GameSession.MenuScene);
        }

        /// <summary>각 피어가 Game 씬 로드와 미로 생성을 마친 뒤 서버에 알린다.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SceneReadyRpc(RpcParams rpcParams = default)
        {
            _sceneReady.Add(rpcParams.Receive.SenderClientId);
            CheckAllSceneReady();
        }
    }
}
