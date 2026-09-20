using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Mirro.Gameplay;
using Mirro.Maze;
using Mirro.Networking;
using Mirro.Themes;
using Mirro.UI;

namespace Mirro.Core
{
    /// <summary>
    /// Game 씬의 진입점. 모든 피어가 같은 seed로 미로를 로컬에서 생성한 뒤 서버에 "씬 준비 완료"를 알리고,
    /// 서버가 모두의 준비가 끝나면 플레이어를 스폰한다. 메뉴를 거치지 않고 Game 씬을 직접 실행하면
    /// 아래 인스펙터 값으로 호스트 방을 자동으로 열어 혼자 테스트할 수 있다.
    /// </summary>
    public class MazeGameBootstrap : MonoBehaviour
    {
        private const float SpawnTimeoutSeconds = 20f;

        [Header("Fallback (메뉴 없이 Game 씬을 직접 실행할 때만 사용)")]
        [Tooltip("50, 70, 100, 120, 150, 170, 200 중 하나")]
        public int mazeSize = 50;
        public int seed = 12345;
        public SeasonTheme fallbackSeason = SeasonTheme.Spring;

        [Header("Layout")]
        public float cellSize = 4f;

        public static MazeGameBootstrap Instance { get; private set; }

        public MazeData Maze { get; private set; }
        public float CellSize => cellSize;

        private MazeThemeConfig _theme;
        private Light _sun;
        private bool _playersSpawned;

        private void Awake()
        {
            Instance = this;
            NetworkPlayer.LocalPlayerSpawned += OnLocalPlayerSpawned;
        }

        private void OnDestroy()
        {
            NetworkPlayer.LocalPlayerSpawned -= OnLocalPlayerSpawned;
            if (Instance == this) Instance = null;
            if (NetworkSession.Instance != null)
                NetworkSession.Instance.AllSceneReady -= SpawnPlayers;
        }

        private IEnumerator Start()
        {
            if (!NetworkFlow.IsRunning)
                StartDevHost();

            _theme = GameSession.Theme;
            Maze = MazeGenerator.Generate(GameSession.MazeSize, GameSession.Seed);

            var builderGo = new GameObject("MazeBuilder");
            var builder = builderGo.AddComponent<MazeBuilder>();
            builder.cellSize = cellSize;
            ThemeApplier.ApplyToBuilder(_theme, builder);
            builder.Build(Maze);

            _sun = EnsureLight();
            ThemeApplier.ApplyEnvironment(_theme, null, _sun);

            float waited = 0f;
            while (NetworkSession.Instance == null && waited < 10f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            var session = NetworkSession.Instance;
            if (session == null)
            {
                Debug.LogError("[Mirro] No network session found; returning to the menu.");
                NetworkFlow.Instance.Leave(true, "방 정보를 찾지 못했어요.");
                yield break;
            }

            if (NetworkManager.Singleton.IsServer)
            {
                session.AllSceneReady += SpawnPlayers;
                StartCoroutine(SpawnAfterTimeout());
            }
            session.SceneReadyRpc();
        }

        private void StartDevHost()
        {
            var theme = ThemeLibrary.Get(fallbackSeason);
            GameSession.Begin(theme, mazeSize, seed);
            // 메뉴 없이 Game 씬을 직접 실행하면 혼자 하기 방(자유 연습)으로 연다.
            if (NetworkFlow.Instance.HostRoom(theme, mazeSize, NetworkFlow.DefaultPort, solo: true))
            {
                NetworkSession.Instance.ConfigureSolo(GameMode.Practice, 0, BotDifficulty.Normal);
                NetworkSession.Instance.ForceInGame();
            }
        }

        private IEnumerator SpawnAfterTimeout()
        {
            yield return new WaitForSecondsRealtime(SpawnTimeoutSeconds);
            if (!_playersSpawned)
            {
                Debug.LogWarning("[Mirro] Not every client reported ready in time; spawning the ready ones.");
                SpawnPlayers();
            }
        }

        private void SpawnPlayers()
        {
            if (_playersSpawned) return;
            _playersSpawned = true;

            var session = NetworkSession.Instance;
            var prefab = Resources.Load<GameObject>("Prefabs/Player");

            var slots = new List<PlayerSlot>();
            foreach (var slot in session.Slots)
                if (session.IsSceneReady(slot.clientId)) slots.Add(slot);

            var mode = session.CurrentMode;
            int botCount = mode == GameMode.Bots ? session.BotCount.Value : 0;

            var timer = System.Diagnostics.Stopwatch.StartNew();
            var cells = SpawnPlacer.Place(Maze, slots.Count + botCount);
            string spacing = cells.Length > 1
                ? $"; closest pair is {SpawnPlacer.MinPairwisePathDistance(Maze, cells)} cells apart by path"
                : string.Empty;
            Debug.Log($"[Mirro] Placed {cells.Length} spawn(s) in {timer.ElapsedMilliseconds} ms{spacing}");
            var matchPlayers = new List<MatchPlayer>();
            for (int i = 0; i < slots.Count; i++)
            {
                Vector3 spawnPos = SpawnPlacer.CellCenter(cells[i], cellSize);
                Quaternion spawnRot = SpawnPlacer.FacingOpenSide(Maze, cells[i]);
                Debug.Log($"[Mirro] Spawning player for client {slots[i].clientId} in cell {cells[i]} at {spawnPos}");

                var go = Instantiate(prefab, spawnPos, spawnRot);
                var player = go.GetComponent<NetworkPlayer>();
                player.InitServer(spawnPos, spawnRot.eulerAngles.y);
                go.GetComponent<NetworkObject>().SpawnAsPlayerObject(slots[i].clientId, true);
                player.ColorIndex.Value = slots[i].colorIndex;

                matchPlayers.Add(new MatchPlayer
                {
                    clientId = slots[i].clientId,
                    colorIndex = slots[i].colorIndex,
                    flagPosition = MatchManager.FlagPositionFor(spawnPos, spawnRot),
                    flagYaw = spawnRot.eulerAngles.y
                });
            }

            SpawnBots(prefab, slots, cells, botCount, session, matchPlayers);

            var treasures = new List<TreasureFlag>();
            if (mode == GameMode.Treasure)
            {
                var treasureCells = SpawnPlacer.PlaceTreasures(Maze, Mathf.Max(1, GameSession.MazeSize / 10), cells[0]);
                for (int i = 0; i < treasureCells.Length; i++)
                {
                    float yaw = (i % 4) * 90f;
                    Vector3 center = SpawnPlacer.CellCenter(treasureCells[i], cellSize);
                    treasures.Add(new TreasureFlag
                    {
                        id = MatchManager.TreasureIdBase + (ulong)i,
                        position = new Vector3(center.x, 0f, center.z) + Quaternion.Euler(0f, yaw, 0f) * new Vector3(-0.9f, 0f, -0.9f),
                        yaw = yaw
                    });
                }
            }

            var matchGo = Instantiate(Resources.Load<GameObject>("Prefabs/MatchManager"));
            matchGo.GetComponent<NetworkObject>().Spawn(true);
            matchGo.GetComponent<MatchManager>().ServerBegin(mode, matchPlayers, treasures);
        }

        /// <summary>봇을 사람 다음 시작 칸에 세운다. 서버가 소유하고 BotBrain이 조종하며, 색은 사람이 쓰지 않는 색을 순서대로 받는다.</summary>
        private void SpawnBots(GameObject prefab, List<PlayerSlot> slots, Vector2Int[] cells, int botCount, NetworkSession session,
            List<MatchPlayer> matchPlayers)
        {
            var usedColors = new HashSet<int>();
            foreach (var slot in slots) usedColors.Add(slot.colorIndex);

            var difficulty = (BotDifficulty)session.BotDifficulty.Value;
            var botControllers = new List<CharacterController>();
            for (int b = 0; b < botCount; b++)
            {
                int color = 0;
                while (usedColors.Contains(color)) color++;
                usedColors.Add(color);

                Vector2Int cell = cells[slots.Count + b];
                Vector3 pos = SpawnPlacer.CellCenter(cell, cellSize);
                Quaternion rot = SpawnPlacer.FacingOpenSide(Maze, cell);
                ulong botId = MatchManager.BotIdBase + (ulong)b;

                var botGo = Instantiate(prefab, pos, rot);
                var bot = botGo.GetComponent<NetworkPlayer>();
                bot.InitServer(pos, rot.eulerAngles.y);
                bot.InitBot(botId);
                botGo.GetComponent<NetworkObject>().Spawn(true);
                bot.ColorIndex.Value = color;
                botGo.AddComponent<BotBrain>().Init(bot, Maze, cellSize, difficulty, Maze.Seed + b * 7919);
                botControllers.Add(botGo.GetComponent<CharacterController>());

                matchPlayers.Add(new MatchPlayer
                {
                    clientId = botId,
                    colorIndex = color,
                    flagPosition = MatchManager.FlagPositionFor(pos, rot),
                    flagYaw = rot.eulerAngles.y
                });
                Debug.Log($"[Mirro] Spawned bot {botId} ({difficulty}) in cell {cell}");
            }

            // 복도에서 봇끼리 마주쳐 서로 막지 않도록 봇끼리는 충돌을 무시한다(사람과는 그대로 충돌한다).
            for (int i = 0; i < botControllers.Count; i++)
                for (int j = i + 1; j < botControllers.Count; j++)
                    Physics.IgnoreCollision(botControllers[i], botControllers[j]);
        }

        private void OnLocalPlayerSpawned(NetworkPlayer player)
        {
            ThemeApplier.ApplyEnvironment(_theme, player.playerCamera, _sun);

            var pauseGo = new GameObject("PauseMenu");
            pauseGo.AddComponent<PauseMenu>().Init(player.controller, _theme);

            var hudGo = new GameObject("MatchHud");
            hudGo.AddComponent<MatchHud>().Init(player, _theme);
        }

        private static Light EnsureLight()
        {
            var existing = Object.FindAnyObjectByType<Light>();
            if (existing != null) return existing;

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            return light;
        }
    }
}
