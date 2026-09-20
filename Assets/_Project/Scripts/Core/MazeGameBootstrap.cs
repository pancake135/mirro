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
            if (NetworkFlow.Instance.HostRoom(theme, mazeSize))
                NetworkSession.Instance.ForceInGame();
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

            var timer = System.Diagnostics.Stopwatch.StartNew();
            var cells = SpawnPlacer.Place(Maze, slots.Count);
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

            var matchGo = Instantiate(Resources.Load<GameObject>("Prefabs/MatchManager"));
            matchGo.GetComponent<NetworkObject>().Spawn(true);
            matchGo.GetComponent<MatchManager>().ServerBegin(matchPlayers);
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
