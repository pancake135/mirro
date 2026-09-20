# 솔로 플레이 모드 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 혼자서 즐기는 3가지 모드(봇과 대결 / 깃발 찾기 타임어택 / 자유 연습)를 추가한다.

**Architecture:** 솔로도 기존 호스트 구조를 그대로 쓰되 내 PC 안(127.0.0.1)에서만 연다. 봇은 플레이어 프리팹을 서버가 소유하고 `BotBrain`이 조종하며, 깃발/탈락/관전 코드는 `PlayerId`(사람=접속 번호, 봇=1000+, 금색 깃발=2000+) 기준으로 재사용한다. `MatchManager`가 `GameMode`별 승패 규칙을 적용한다.

**Tech Stack:** Unity 6000.6.2f1, C#, Netcode for GameObjects 2.13, Input System, uGUI(코드로 생성). 자동 테스트는 (1) 에디터 배치 셀프 테스트 `MazeSelfTest`, (2) 개발 빌드를 실행하는 플레이테스트 `PlaytestDriver`.

**Spec:** `docs/superpowers/specs/2026-09-20-solo-modes-design.md`

## Global Constraints

- 네임스페이스는 `Mirro.*`. UI 문구는 한국어. 프로그래머 아트(도형/색)만 쓴다. `Shader.Find`를 런타임에 쓰지 않는다(`MazeBuilder.CreateColoredMaterial` 사용).
- 봇 이동 속도 쉬움 3 / 보통 4 / 어려움 5 m/s, 출발 지연 45 / 25 / 10초, 탐색형 인지 깊이 12칸, 깃발 뽑기 `MatchManager.PullSeconds`(1.5초).
- 금색 깃발 개수 = `크기 / 10`(50→5 … 200→20). 최고 기록 키 `mirro.treasure.best.<크기>`(PlayerPrefs, 초 단위).
- 식별자: 사람 `PlayerId == OwnerClientId`, 봇 `1000 + n`, 금색 깃발 `2000 + n`, `NoOne = ulong.MaxValue`.
- 솔로 방: 수신 주소 `127.0.0.1`, 포트 7777부터 빈 UDP 포트, LAN 알림 없음, 다른 접속은 "혼자 하기 방이에요."로 거절.
- **멀티(Versus) 동작은 바뀌면 안 된다.** 각 작업의 끝에서 컴파일하고, 지정된 자동 테스트를 통과시킨 뒤 커밋+푸시한다(커밋 메시지 끝에 `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`).

## 검증 명령 (모든 작업 공통)

Unity 에디터가 실제 프로젝트를 열고 있어도 배치 모드가 돌도록 **검증용 복사본**을 쓴다. `$WSP = C:\Temp\claude\C--project-mirro\77953bc0-a754-4781-bcc3-a7324ae3ec71\scratchpad`, `$U = "C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe"`.

- **동기화(PowerShell):** `robocopy C:\project\mirro\Assets\_Project\Scripts $WSP\mirro_verify\Assets\_Project\Scripts /E /NFL /NDL /NJH /NJS /NP` (robocopy는 변경이 있으면 종료 코드 1을 돌려주므로 무시)
- **셋업(프리팹 생성, 새 네트워크 프리팹이 있을 때):** `& $U -batchmode -nographics -quit -projectPath $WSP\mirro_verify -executeMethod Mirro.EditorTools.ProjectSetup.SetupAll -logFile $WSP\setup.log`
- **셀프 테스트:** `& $U ... -executeMethod Mirro.EditorTools.MazeSelfTest.RunBatch -logFile $WSP\selftest.log` 후 `Select-String $WSP\selftest.log 'error CS','\[MirroTest\]','ALL PASSED','FAILED'`
- **빌드:** `& $U ... -executeMethod Mirro.EditorTools.BuildTools.BuildWindowsDevBatch -buildPath $WSP\build -logFile $WSP\build.log` 후 로그에서 `error CS`와 `[MirroBuild] Succeeded` 확인
- **멀티 회귀:** `cd /c/project/mirro && PLAYDIR=<폴더> PLAYERS=<2|3|4> SIZE=50 SEASON=Autumn bash $WSP/run4.sh` 후 각 `report_*.txt`의 첫 줄이 `failures=0 errors=0 warnings=0`
- **새 파일을 만들면** 복사본에서 생성된 `.meta`를 실제 프로젝트로 복사한다(스크립트 GUID 유지).

---

### Task 1: `PlayerId` 도입 (멀티 동작 변경 없음)

**Files:**
- Modify: `Assets/_Project/Scripts/Networking/NetworkPlayer.cs`, `Assets/_Project/Scripts/Gameplay/FlagPuller.cs`, `Assets/_Project/Scripts/Gameplay/MatchManager.cs`, `Assets/_Project/Scripts/UI/ResultScreen.cs`

**Interfaces:**
- Produces: `NetworkPlayer.PlayerId : ulong`, `NetworkPlayer.IsBot : bool`, `NetworkPlayer.IsLocalHuman : bool`, `NetworkPlayer.InitBot(ulong botId)`, `NetworkPlayer.Find(ulong playerId)`(이제 `PlayerId` 기준), `MatchManager.BotIdBase`/`TreasureIdBase`/`IsBotId(id)`/`IsTreasureId(id)`.

- [ ] **Step 1: `MatchManager` 상수 추가** — `NoOne` 선언 바로 아래:

```csharp
        /// <summary>봇의 PlayerId는 BotIdBase + n, 주인 없는 금색 깃발은 TreasureIdBase + n.</summary>
        public const ulong BotIdBase = 1000;
        public const ulong TreasureIdBase = 2000;

        public static bool IsBotId(ulong id) => id >= BotIdBase && id < TreasureIdBase;

        public static bool IsTreasureId(ulong id) => id >= TreasureIdBase && id < NoOne;
```

- [ ] **Step 2: `NetworkPlayer`에 식별자 추가** — 필드/프로퍼티(`_hiddenBySpectator` 근처)와 스폰 페이로드:

```csharp
        private bool _isBot;
        private ulong _botId;

        /// <summary>서버가 조종하는 봇인지(스폰 페이로드로 전달).</summary>
        public bool IsBot => _isBot;

        /// <summary>깃발/탈락 기록/관전이 플레이어를 가리키는 번호. 사람은 접속 번호, 봇은 MatchManager.BotIdBase + n.</summary>
        public ulong PlayerId => _isBot ? _botId : OwnerClientId;

        /// <summary>이 화면을 조작하는 사람의 캐릭터인지. 봇은 서버가 소유하므로 IsOwner만으로는 구별할 수 없다.</summary>
        public bool IsLocalHuman => IsOwner && !_isBot;

        /// <summary>서버 전용: Spawn 호출 전에 봇으로 지정한다.</summary>
        public void InitBot(ulong botId)
        {
            _isBot = true;
            _botId = botId;
        }
```
`OnSynchronize`에서 `serializer.SerializeValue(ref _spawnYaw);` 다음 줄에 추가:

```csharp
            serializer.SerializeValue(ref _isBot);
            serializer.SerializeValue(ref _botId);
```

- [ ] **Step 3: 조회/판정을 `PlayerId` 기준으로** — `NetworkPlayer.cs`에서
  - `Find`: `if (player.PlayerId == clientId) return player;` (매개변수 이름은 `playerId`로)
  - `PullFlagRpc`: `if (_isBot || rpcParams.Receive.SenderClientId != OwnerClientId) return; MatchManager.Instance?.ServerTryPullFlag(PlayerId, targetPlayerId);`
  - `Update`: `if (!IsSpawned || !IsLocalHuman || ...) return;`
  - `OnAliveChanged`: `if (!alive && IsLocalHuman) EnterSpectator();`
  - `EnterSpectator`의 `match.Eliminated[i].clientId == OwnerClientId` → `== PlayerId`
- `FlagPuller.cs`: `flag.PlayerId == _player.OwnerClientId` → `flag.PlayerId == _player.PlayerId`
- `ResultScreen.Show`: `Build(local.OwnerClientId, ...)` → `Build(local.PlayerId, ...)`

- [ ] **Step 4: 컴파일 + 멀티 회귀** — 동기화 → 빌드(`error CS` 없음) → 3인 플레이테스트(`PLAYDIR=t1_p3 PLAYERS=3`)와 2인(`PLAYERS=2`) 모두 `failures=0 errors=0 warnings=0`. 사람은 `PlayerId == OwnerClientId`라서 통과해야 한다.

- [ ] **Step 5: 커밋+푸시**

```bash
cd /c/project/mirro && git add -A && git commit -q -m "Introduce PlayerId to identify players independent of connection id" -m "Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>" && git push -q origin main
```

---

### Task 2: 게임 모드와 솔로 방 (네트워크)

**Files:**
- Create: `Assets/_Project/Scripts/Core/GameMode.cs`, `Assets/_Project/Scripts/Gameplay/BotSettings.cs`(난이도 enum만 먼저)
- Modify: `Assets/_Project/Scripts/Networking/NetworkSession.cs`, `NetworkFlow.cs`, `LanDiscovery.cs`, `Assets/_Project/Scripts/Core/MazeGameBootstrap.cs`

**Interfaces:**
- Produces: `enum GameMode { Versus, Bots, Treasure, Practice }`, `enum BotDifficulty { Easy, Normal, Hard }`, `NetworkSession.Mode/BotCount/BotDifficulty/IsSolo` (NetworkVariable), `NetworkSession.CurrentMode`, `ConfigureSolo(GameMode, int, BotDifficulty)`, `StartSoloGame() : bool`, `RestartSolo() : bool`, `NetworkFlow.HostRoom(theme, size, port = DefaultPort, solo = false)`.

- [ ] **Step 1: 열거형 파일 작성**

```csharp
// Core/GameMode.cs
namespace Mirro.Core
{
    /// <summary>한 판의 종류. Versus는 사람끼리(4인 멀티), 나머지는 혼자 하기 모드.</summary>
    public enum GameMode
    {
        Versus = 0,
        Bots = 1,
        Treasure = 2,
        Practice = 3
    }
}
```
```csharp
// Gameplay/BotSettings.cs (이 작업에서는 enum만, Task 3에서 BotSettings 구조체를 이 파일에 추가한다)
namespace Mirro.Gameplay
{
    public enum BotDifficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2
    }
}
```

- [ ] **Step 2: `NetworkSession`에 솔로 상태와 시작/재시작** — 변수(다른 NetworkVariable 옆)와 메서드:

```csharp
        public readonly NetworkVariable<int> Mode = new NetworkVariable<int>((int)GameMode.Versus);
        public readonly NetworkVariable<int> BotCount = new NetworkVariable<int>(3);
        public readonly NetworkVariable<int> BotDifficulty = new NetworkVariable<int>((int)Mirro.Gameplay.BotDifficulty.Normal);
        public readonly NetworkVariable<bool> IsSolo = new NetworkVariable<bool>();

        public GameMode CurrentMode => (GameMode)Mode.Value;

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
```
그리고 `ReturnToLobby`의 정리 부분을 `private void DespawnGameObjects()`로 뽑아 두 곳이 공유한다:

```csharp
        private void DespawnGameObjects()
        {
            foreach (var flag in Flag.All.ToArray())
                if (flag.IsSpawned) flag.NetworkObject.Despawn(true);

            var match = MatchManager.Instance;
            if (match != null && match.IsSpawned) match.NetworkObject.Despawn(true);

            foreach (var player in NetworkPlayer.All.ToArray())
                if (player.IsSpawned) player.NetworkObject.Despawn(true);
        }
```
(`ReturnToLobby`는 첫 줄 검사 다음에 `DespawnGameObjects();`를 호출하도록 바꾼다.)

- [ ] **Step 3: `NetworkFlow.HostRoom`에 솔로 옵션** — 시그니처와 앞부분:

```csharp
        public bool HostRoom(MazeThemeConfig theme, int size, ushort port = DefaultPort, bool solo = false)
        {
            if (!PrepareManager()) return false;

            var nm = NetworkManager.Singleton;
            var transport = nm.GetComponent<UnityTransport>();
            // 혼자 하기 방은 내 PC 안에서만 열고(방화벽 창/외부 접속 없음), 쓰던 포트를 피해 빈 포트를 고른다.
            string listen = solo ? "127.0.0.1" : ListenAddress;
            if (solo) port = FindFreeLoopbackPort(port);
            transport.SetConnectionData("127.0.0.1", port, listen);
```
맨 끝의 `LanDiscovery.EnsureExists();`는 `if (!solo)`로 감싼다. 헬퍼 추가:

```csharp
        private static ushort FindFreeLoopbackPort(ushort start)
        {
            for (ushort p = start; p < start + 10; p++)
            {
                try
                {
                    using (new UdpClient(new IPEndPoint(IPAddress.Loopback, p))) return p;
                }
                catch (SocketException)
                {
                    // 이미 쓰는 포트면 다음 포트를 시도한다.
                }
            }
            return start;
        }
```
`ApproveConnection`에서 승인 분기 앞(세션 검사 앞)에 추가:

```csharp
            if (NetworkSession.Instance != null && NetworkSession.Instance.IsSolo.Value)
            {
                response.Approved = false;
                response.Reason = "혼자 하기 방이에요.";
                return;
            }
```

- [ ] **Step 4: LAN 알림 제외** — `LanDiscovery.TryAdvertise`의 조건에 `|| session.IsSolo.Value` 추가(`session.Slots.Count >= MaxPlayers` 조건 옆).

- [ ] **Step 5: 개발용 Game 씬 직접 실행 = 자유 연습** — `MazeGameBootstrap.StartDevHost`:

```csharp
        private void StartDevHost()
        {
            var theme = ThemeLibrary.Get(fallbackSeason);
            GameSession.Begin(theme, mazeSize, seed);
            if (NetworkFlow.Instance.HostRoom(theme, mazeSize, NetworkFlow.DefaultPort, solo: true))
            {
                NetworkSession.Instance.ConfigureSolo(GameMode.Practice, 0, BotDifficulty.Normal);
                NetworkSession.Instance.ForceInGame();
            }
        }
```
(`using Mirro.Gameplay;`는 이미 있다.)

- [ ] **Step 6: 컴파일 + 멀티 회귀** — 빌드 성공, 2인 플레이테스트 `failures=0`. (멀티 방 생성 경로는 `solo=false`라 그대로여야 한다.)

- [ ] **Step 7: 커밋+푸시** — 메시지 `Add game modes and local-only solo room networking`.

---

### Task 3: 봇 경로 탐색과 계획 (TDD, 셀프 테스트)

**Files:**
- Modify: `Assets/_Project/Scripts/Gameplay/BotSettings.cs`
- Create: `Assets/_Project/Scripts/Gameplay/BotPathfinder.cs`, `Assets/_Project/Scripts/Gameplay/BotPlanner.cs`
- Modify(Test): `Assets/_Project/Scripts/Editor/MazeSelfTest.cs`

**Interfaces:**
- Produces: `BotSettings.For(BotDifficulty)`(필드 `speed`, `startDelay`, `explorer`, 상수 `AwarenessDepth = 12`), `BotPathfinder.TryFindPath(int from, Func<int,bool> isGoal, int maxDepth, DeterministicRandom rng, List<int> path) : bool`, `BotPlanner(MazeData, BotSettings, int seed)`, `BotPlanner.Plan(int cell, IReadOnlyDictionary<int,ulong> enemyFlags)`, `BotPlanner.MarkVisited(int)`, `BotPlanner.Path`, `BotPlanner.TargetFlagId`, `BotPlanner.IsChasing`.

- [ ] **Step 1: 실패하는 테스트 작성** — `MazeSelfTest.Run()`의 `allOk &= TestSpawnPlacement();` 다음 줄에 `allOk &= TestBots();`를 넣고 메서드를 추가한다(아직 클래스가 없어 컴파일 실패해야 한다):

```csharp
        private static bool TestBots()
        {
            bool allOk = true;
            var maze = MazeGenerator.Generate(50, 909);
            int cells = maze.Cells.Length;
            var finder = new BotPathfinder(maze);
            var path = new List<int>();

            // 경로 탐색: 인접한 칸으로 이어지고 길이가 BFS 최단 거리와 같다.
            int from = maze.Index(3, 4);
            int[] dist = SpawnPlacer.PathDistances(maze, new Vector2Int(3, 4));
            int far = 0;
            for (int i = 1; i < cells; i++) if (dist[i] > dist[far]) far = i;
            bool found = finder.TryFindPath(from, c => c == far, int.MaxValue, null, path);
            bool adjacent = found && path.Count == dist[far] && path[path.Count - 1] == far;
            int prev = from;
            foreach (int c in path)
            {
                int dx = Mathf.Abs(c % 50 - prev % 50), dy = Mathf.Abs(c / 50 - prev / 50);
                if (dx + dy != 1) adjacent = false;
                prev = c;
            }
            allOk &= Report(adjacent, $"[MirroTest] bots: path to the farthest cell is contiguous and shortest ({path.Count} steps)");

            bool limited = !finder.TryFindPath(from, c => c == far, 5, null, path) && path.Count == 0;
            allOk &= Report(limited, "[MirroTest] bots: max depth is respected");
            allOk &= Report(finder.TryFindPath(from, c => c == from, 0, null, path) && path.Count == 0, "[MirroTest] bots: standing on the goal gives an empty path");

            // 추격형: 가장 가까운 남의 깃발까지 최단 경로로 걸어간다.
            var flags = new Dictionary<int, ulong> { { far, 1001UL }, { maze.Index(40, 3), 1002UL }, { maze.Index(10, 45), 1003UL } };
            int nearest = int.MaxValue;
            foreach (var pair in flags) nearest = Mathf.Min(nearest, dist[pair.Key]);
            var chaser = new BotPlanner(maze, BotSettings.For(BotDifficulty.Hard), 1);
            chaser.MarkVisited(from);
            chaser.Plan(from, flags);
            allOk &= Report(chaser.IsChasing && chaser.Path.Count == nearest && flags[chaser.Path[chaser.Path.Count - 1]] == chaser.TargetFlagId,
                $"[MirroTest] bots: chaser heads for the nearest flag ({chaser.Path.Count} steps, expected {nearest})");

            // 탐색형: 멀리 있는 깃발도 결국 발견해서 쫓아간다(칸 수의 3배 안에).
            var explorer = new BotPlanner(maze, BotSettings.For(BotDifficulty.Easy), 2);
            var farFlag = new Dictionary<int, ulong> { { far, 1001UL } };
            int at = from, steps = 0;
            explorer.MarkVisited(at);
            int index = 0;
            explorer.Plan(at, farFlag);
            while (at != far && steps < cells * 3)
            {
                if (index >= explorer.Path.Count || (!explorer.IsChasing && steps % 3 == 0))
                {
                    explorer.Plan(at, farFlag);
                    index = 0;
                    if (explorer.Path.Count == 0) break;
                }
                at = explorer.Path[index++];
                explorer.MarkVisited(at);
                steps++;
            }
            allOk &= Report(at == far, $"[MirroTest] bots: explorer finds the far flag on its own ({steps} steps, {cells} cells)");
            return allOk;
        }
```

- [ ] **Step 2: 컴파일이 실패하는지 확인** — 동기화 후 셀프 테스트 실행 → 로그에 `error CS0246`(`BotPathfinder`/`BotPlanner`/`BotSettings` 없음) 확인.

- [ ] **Step 3: 구현** — `BotSettings.cs`(파일 전체):

```csharp
namespace Mirro.Gameplay
{
    public enum BotDifficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2
    }

    /// <summary>난이도별 봇 능력치. 쉬움은 탐색형(가까운 깃발만 인지), 보통/어려움은 추격형(미로를 안다).</summary>
    public readonly struct BotSettings
    {
        /// <summary>탐색형이 깃발을 알아채는 경로 거리(칸).</summary>
        public const int AwarenessDepth = 12;

        public readonly float speed;
        public readonly float startDelay;
        public readonly bool explorer;

        private BotSettings(float speed, float startDelay, bool explorer)
        {
            this.speed = speed;
            this.startDelay = startDelay;
            this.explorer = explorer;
        }

        public static BotSettings For(BotDifficulty difficulty)
        {
            switch (difficulty)
            {
                case BotDifficulty.Easy: return new BotSettings(3f, 45f, true);
                case BotDifficulty.Hard: return new BotSettings(5f, 10f, false);
                default: return new BotSettings(4f, 25f, false);
            }
        }
    }
}
```
`BotPathfinder.cs`:

```csharp
using System;
using System.Collections.Generic;
using Mirro.Core;
using Mirro.Maze;

namespace Mirro.Gameplay
{
    /// <summary>
    /// 미로 위 BFS 경로 탐색. 배열을 재사용해 호출마다 메모리를 새로 잡지 않는다(방문 표시는 호출 번호 스탬프).
    /// </summary>
    public sealed class BotPathfinder
    {
        private static readonly WallSide[] Sides = { WallSide.North, WallSide.East, WallSide.South, WallSide.West };

        private readonly MazeData _maze;
        private readonly int[] _offset;
        private readonly int[] _parent;
        private readonly int[] _depth;
        private readonly int[] _queue;
        private readonly int[] _stamp;
        private int _currentStamp;

        public BotPathfinder(MazeData maze)
        {
            _maze = maze;
            int count = maze.Cells.Length;
            _offset = new[] { maze.Width, 1, -maze.Width, -1 };
            _parent = new int[count];
            _depth = new int[count];
            _queue = new int[count];
            _stamp = new int[count];
        }

        /// <summary>
        /// from에서 isGoal을 만족하는 가장 가까운 칸까지의 경로(from 제외, 목표 포함)를 path에 담는다.
        /// maxDepth칸 안에 없으면 false. rng가 있으면 이웃 검사 순서를 돌려 동점 경로가 무작위로 갈리게 한다.
        /// </summary>
        public bool TryFindPath(int from, Func<int, bool> isGoal, int maxDepth, DeterministicRandom rng, List<int> path)
        {
            path.Clear();
            if (isGoal(from)) return true;

            _currentStamp++;
            int head = 0, tail = 0;
            _queue[tail++] = from;
            _stamp[from] = _currentStamp;
            _parent[from] = -1;
            _depth[from] = 0;
            int rotation = rng != null ? rng.NextInt(0, 4) : 0;

            while (head < tail)
            {
                int cell = _queue[head++];
                if (_depth[cell] >= maxDepth) continue;

                WallSide walls = _maze.Cells[cell];
                for (int k = 0; k < 4; k++)
                {
                    int side = (k + rotation) & 3;
                    if ((walls & Sides[side]) != 0) continue;

                    int next = cell + _offset[side];
                    if (_stamp[next] == _currentStamp) continue;

                    _stamp[next] = _currentStamp;
                    _parent[next] = cell;
                    _depth[next] = _depth[cell] + 1;
                    if (isGoal(next))
                    {
                        for (int c = next; c != from; c = _parent[c]) path.Add(c);
                        path.Reverse();
                        return true;
                    }
                    _queue[tail++] = next;
                }
            }
            return false;
        }
    }
}
```
`BotPlanner.cs`:

```csharp
using System.Collections.Generic;
using Mirro.Core;
using Mirro.Maze;

namespace Mirro.Gameplay
{
    /// <summary>
    /// 봇이 "어디로 갈지"만 정하는 순수 로직(물리/유니티 오브젝트 없음). 추격형은 가장 가까운 남의 깃발로,
    /// 탐색형은 12칸 안의 깃발이 보이면 그쪽으로, 아니면 가장 가까운 안 가본 칸으로 향한다.
    /// </summary>
    public sealed class BotPlanner
    {
        private readonly BotSettings _settings;
        private readonly BotPathfinder _finder;
        private readonly DeterministicRandom _rng;
        private readonly bool[] _visited;
        private readonly List<int> _path = new List<int>();

        public BotPlanner(MazeData maze, BotSettings settings, int seed)
        {
            _settings = settings;
            _finder = new BotPathfinder(maze);
            _rng = new DeterministicRandom(seed);
            for (int i = 0; i < 8; i++) _rng.NextFloat01();
            _visited = new bool[maze.Cells.Length];
        }

        /// <summary>현재 칸 다음부터 목표까지의 칸 목록.</summary>
        public IReadOnlyList<int> Path => _path;

        /// <summary>쫓고 있는 깃발의 주인(PlayerId). 탐색 중이면 MatchManager.NoOne.</summary>
        public ulong TargetFlagId { get; private set; } = MatchManager.NoOne;

        public bool IsChasing => TargetFlagId != MatchManager.NoOne;

        public void MarkVisited(int cell) => _visited[cell] = true;

        public void ClearTarget()
        {
            TargetFlagId = MatchManager.NoOne;
            _path.Clear();
        }

        /// <summary>enemyFlags: 칸 번호 → 그 칸에 서 있는 남의 깃발 주인.</summary>
        public void Plan(int cell, IReadOnlyDictionary<int, ulong> enemyFlags)
        {
            ClearTarget();
            _visited[cell] = true;

            if (enemyFlags.Count > 0)
            {
                int depth = _settings.explorer ? BotSettings.AwarenessDepth : int.MaxValue;
                if (TryChase(cell, enemyFlags, depth)) return;
            }

            if (_settings.explorer)
            {
                _finder.TryFindPath(cell, c => !_visited[c], int.MaxValue, _rng, _path);
                // 미로를 다 돌았는데도 아무도 못 찾았다면 마지막으로 아는 깃발을 쫓는다.
                if (_path.Count == 0 && enemyFlags.Count > 0) TryChase(cell, enemyFlags, int.MaxValue);
            }
        }

        private bool TryChase(int cell, IReadOnlyDictionary<int, ulong> enemyFlags, int depth)
        {
            if (!_finder.TryFindPath(cell, enemyFlags.ContainsKey, depth, null, _path)) return false;
            int goal = _path.Count > 0 ? _path[_path.Count - 1] : cell;
            TargetFlagId = enemyFlags[goal];
            return true;
        }
    }
}
```

- [ ] **Step 4: 셀프 테스트 통과 확인** — 동기화 → 셀프 테스트 → `[MirroTest] bots: ...` 5줄이 모두 `OK`이고 `ALL PASSED`.

- [ ] **Step 5: 커밋+푸시** — 새 `.cs.meta`를 복사본에서 실제 프로젝트로 복사한 뒤 `git add -A`. 메시지 `Add bot path finding and planning with self tests`.

---

### Task 4: 금색 깃발 배치와 기록 (TDD, 셀프 테스트)

**Files:**
- Modify: `Assets/_Project/Scripts/Maze/SpawnPlacer.cs`, `Assets/_Project/Scripts/Editor/MazeSelfTest.cs`
- Create: `Assets/_Project/Scripts/Gameplay/TreasureRecords.cs`

**Interfaces:**
- Produces: `SpawnPlacer.PlaceTreasures(MazeData maze, int count, Vector2Int avoid) : Vector2Int[]`, `TreasureRecords.Best(int size) : float`(0=기록 없음), `TreasureRecords.Submit(int size, float seconds, out float previousBest) : bool`(새 기록이면 true 후 저장).

- [ ] **Step 1: 실패하는 테스트** — `MazeSelfTest.Run()`에 `allOk &= TestTreasures();` 추가:

```csharp
        private static bool TestTreasures()
        {
            bool allOk = true;
            foreach (int size in new[] { 50, 100, 200 })
            {
                int count = size / 10;
                var maze = MazeGenerator.Generate(size, 5150 + size);
                var avoid = new Vector2Int(size / 2, size / 2);
                var cells = SpawnPlacer.PlaceTreasures(maze, count, avoid);
                var again = SpawnPlacer.PlaceTreasures(maze, count, avoid);

                bool distinct = new HashSet<Vector2Int>(cells).Count == count && cells.Length == count;
                bool deterministic = cells.Length == again.Length;
                for (int i = 0; deterministic && i < cells.Length; i++) deterministic = cells[i] == again[i];
                float minSpacing = float.MaxValue, minFromStart = float.MaxValue;
                for (int i = 0; i < cells.Length; i++)
                {
                    minFromStart = Mathf.Min(minFromStart, Vector2Int.Distance(cells[i], avoid));
                    for (int j = i + 1; j < cells.Length; j++) minSpacing = Mathf.Min(minSpacing, Vector2Int.Distance(cells[i], cells[j]));
                    if (!maze.InBounds(cells[i].x, cells[i].y)) distinct = false;
                }
                bool spread = minSpacing >= size * 0.16f * 0.8f - 0.01f && minFromStart >= size * 0.15f * 0.8f - 0.01f;
                allOk &= Report(distinct && deterministic && spread,
                    $"[MirroTest] treasures size {size}: {count} distinct, deterministic, spread (min spacing {minSpacing:F1}, from start {minFromStart:F1})");
            }

            const int probeSize = 999;
            PlayerPrefs.DeleteKey("mirro.treasure.best." + probeSize);
            bool first = TreasureRecords.Submit(probeSize, 120f, out float prev0);
            bool slower = TreasureRecords.Submit(probeSize, 150f, out float prev1);
            bool faster = TreasureRecords.Submit(probeSize, 100f, out float prev2);
            bool ok = first && prev0 == 0f && !slower && prev1 == 120f && faster && prev2 == 120f && Mathf.Approximately(TreasureRecords.Best(probeSize), 100f);
            PlayerPrefs.DeleteKey("mirro.treasure.best." + probeSize);
            allOk &= Report(ok, "[MirroTest] treasure records keep only the best time");
            return allOk;
        }
```

- [ ] **Step 2: 컴파일 실패 확인** — `PlaceTreasures`/`TreasureRecords` 없음.

- [ ] **Step 3: 구현** — `SpawnPlacer`에 상수와 메서드:

```csharp
        private const int TreasureSeedSalt = 0x2545F491;

        /// <summary>
        /// 금색 깃발 칸을 count개 고른다. 서로 미로 한 변의 16% 이상, avoid(시작 칸)에서 15% 이상 떨어지게 무작위로 고르고,
        /// 자리가 모자라면 조건을 80%씩 낮춘다. (미로, count, avoid)만으로 결정된다.
        /// </summary>
        public static Vector2Int[] PlaceTreasures(MazeData maze, int count, Vector2Int avoid)
        {
            count = Mathf.Clamp(count, 0, maze.Cells.Length - 1);
            var result = new List<Vector2Int>(count);
            var rng = new DeterministicRandom(maze.Seed ^ TreasureSeedSalt);
            for (int i = 0; i < 8; i++) rng.NextFloat01();

            float side = Mathf.Min(maze.Width, maze.Height);
            float spacing = Mathf.Max(4f, side * 0.16f);
            float fromStart = side * 0.15f;

            while (result.Count < count)
            {
                for (int attempt = 0; attempt < 400 && result.Count < count; attempt++)
                {
                    var cell = new Vector2Int(rng.NextInt(0, maze.Width), rng.NextInt(0, maze.Height));
                    if (Vector2Int.Distance(cell, avoid) < fromStart) continue;

                    bool ok = true;
                    foreach (var other in result)
                    {
                        if (Vector2Int.Distance(cell, other) >= spacing) continue;
                        ok = false;
                        break;
                    }
                    if (ok) result.Add(cell);
                }
                spacing = Mathf.Max(0.5f, spacing * 0.8f);
                fromStart *= 0.8f;
            }
            return result.ToArray();
        }
```
`TreasureRecords.cs`:

```csharp
using UnityEngine;

namespace Mirro.Gameplay
{
    /// <summary>깃발 찾기 타임어택의 크기별 최고 기록(내 PC에만 저장).</summary>
    public static class TreasureRecords
    {
        private static string Key(int size) => "mirro.treasure.best." + size;

        /// <summary>저장된 최고 기록(초). 기록이 없으면 0.</summary>
        public static float Best(int size) => PlayerPrefs.GetFloat(Key(size), 0f);

        /// <summary>기록을 제출한다. 이전 최고 기록(없으면 0)을 previousBest로 돌려주고, 새 기록이면 저장하고 true.</summary>
        public static bool Submit(int size, float seconds, out float previousBest)
        {
            previousBest = Best(size);
            bool isNewRecord = previousBest <= 0f || seconds < previousBest;
            if (isNewRecord)
            {
                PlayerPrefs.SetFloat(Key(size), seconds);
                PlayerPrefs.Save();
            }
            return isNewRecord;
        }
    }
}
```
(`SpawnPlacer.cs` 맨 위 `using System.Collections.Generic;`는 이미 있다.)

- [ ] **Step 4: 셀프 테스트 통과** — 배치 3개 크기와 기록 테스트가 `OK`, `ALL PASSED`.

- [ ] **Step 5: 커밋+푸시** — 메시지 `Add treasure flag placement and best-time records`.

---

### Task 5: 모드별 승패 규칙과 금색 깃발/연습 스폰 (서버)

**Files:**
- Modify: `Assets/_Project/Scripts/Gameplay/MatchManager.cs`, `Flag.cs`, `FlagPuller.cs`, `Assets/_Project/Scripts/UI/MatchHud.cs`(깃발 이름), `Assets/_Project/Scripts/Core/MazeGameBootstrap.cs`

**Interfaces:**
- Consumes: Task 1 상수, Task 2 `NetworkSession.CurrentMode`, Task 4 `SpawnPlacer.PlaceTreasures`.
- Produces: `struct TreasureFlag { ulong id; Vector3 position; float yaw; }`, `MatchManager.ServerBegin(GameMode mode, IReadOnlyList<MatchPlayer> players, IReadOnlyList<TreasureFlag> treasures)`, `MatchManager.Mode`/`CurrentMode`/`TotalTreasures`/`Collected`(NetworkVariable), `Flag.TreasureColor`, `Flag.DisplayName`.

- [ ] **Step 1: `Flag` 금색 표시** — 상수/프로퍼티 추가, `BuildVisual`의 색 결정 변경:

```csharp
        public static readonly Color TreasureColor = new Color(1f, 0.82f, 0.2f);

        /// <summary>안내 문구에 쓰는 깃발 이름(플레이어 색 이름, 금색 깃발은 "금색").</summary>
        public string DisplayName => MatchManager.IsTreasureId(_playerId) ? "금색" : PlayerColors.GetName(_colorIndex);
```
`BuildVisual`: `Color color = MatchManager.IsTreasureId(_playerId) ? TreasureColor : PlayerColors.Get(_colorIndex);`
`FlagPuller`/`MatchHud`에서 깃발 이름을 쓰는 곳(`PlayerColors.GetName(candidate.ColorIndex)`)을 `candidate.DisplayName`으로 바꾼다(`MatchHud.UpdatePrompt`).

- [ ] **Step 2: `MatchManager` 변수와 구조체**

```csharp
    /// <summary>깃발 찾기 모드에서 미로에 세우는 주인 없는 깃발 하나.</summary>
    public struct TreasureFlag
    {
        public ulong id;
        public Vector3 position;
        public float yaw;
    }
```
클래스 안:

```csharp
        public readonly NetworkVariable<int> Mode = new NetworkVariable<int>((int)GameMode.Versus);
        public readonly NetworkVariable<int> TotalTreasures = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Collected = new NetworkVariable<int>();

        public GameMode CurrentMode => (GameMode)Mode.Value;

        private ulong _humanId = NoOne;
```
(`using Mirro.Core;`는 이미 있다.)

- [ ] **Step 3: `ServerBegin` 교체** — 모드에 따라 깃발을 세운다:

```csharp
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

                // 깃발 뽑기 대결(Versus/Bots)에서만 플레이어마다 깃발이 있다.
                if (mode != GameMode.Versus && mode != GameMode.Bots) continue;
                SpawnFlag(flagPrefab, player.clientId, player.colorIndex, player.flagPosition, player.flagYaw);
            }
            foreach (var treasure in treasures)
                SpawnFlag(flagPrefab, treasure.id, 0, treasure.position, treasure.yaw);

            TotalPlayers.Value = players.Count;
            TotalTreasures.Value = treasures.Count;
            StartTime.Value = NetworkManager.ServerTime.Time;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            Debug.Log($"[Mirro] Match begins ({mode}) with {players.Count} player(s), {Flag.All.Count} flag(s)");
        }

        private static void SpawnFlag(GameObject prefab, ulong id, int colorIndex, Vector3 position, float yaw)
        {
            var go = Instantiate(prefab, position, Quaternion.Euler(0f, yaw, 0f));
            go.GetComponent<Flag>().InitServer(id, colorIndex);
            go.GetComponent<NetworkObject>().Spawn(true);
        }
```

- [ ] **Step 4: 뽑기 처리와 종료 규칙** — `ServerTryPullFlag` 시작부와 금색 깃발, `CheckForWinner`/`Finish` 교체:

```csharp
        public bool ServerTryPullFlag(ulong puller, ulong target)
        {
            if (!IsServer || Finished.Value) return false;
            if (IsTreasureId(target)) return TryCollectTreasure(puller, target);
            // (이하 기존 검증/탈락 코드는 그대로)
```
```csharp
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
                    if (_humanId != NoOne && !_alive.Contains(_humanId)) { Finish(NoOne); return; }
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
```
(기존 `CheckForWinner` 본문은 위 코드로 대체한다.)

- [ ] **Step 5: `MazeGameBootstrap.SpawnPlayers`에서 모드 반영** — `MatchManager.ServerBegin(matchPlayers)` 호출을 교체하고, 깃발 찾기용 목록을 만든다. 사람 스폰 루프는 그대로 두고, 루프 뒤에서(봇 스폰은 Task 6에서 이 자리에 추가):

```csharp
            var mode = session.CurrentMode;
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
```
(`using Mirro.Core;`는 같은 네임스페이스라 불필요.)

- [ ] **Step 6: 컴파일 + 멀티 회귀** — 빌드 성공, 3인 플레이테스트 `failures=0`(Versus는 `CurrentMode == Versus`로 기존 동작).

- [ ] **Step 7: 커밋+푸시** — 메시지 `Apply per-mode match rules; add treasure flags and practice spawn`.

---

### Task 6: 봇 (몸체, 두뇌, 스폰)

**Files:**
- Create: `Assets/_Project/Scripts/Gameplay/BotBrain.cs`
- Modify: `Assets/_Project/Scripts/Networking/NetworkPlayer.cs`, `Assets/_Project/Scripts/Core/MazeGameBootstrap.cs`

**Interfaces:**
- Consumes: Task 1 `InitBot/PlayerId`, Task 3 `BotPlanner/BotSettings`, Task 5 `ServerBegin`.
- Produces: `BotBrain.Init(NetworkPlayer player, MazeData maze, float cellSize, BotDifficulty difficulty, int seed)`, 테스트용 정적 값 `BotBrain.StartDelayOverride`(초, 음수면 난이도 값 사용)와 `BotBrain.SpeedMultiplier`.

- [ ] **Step 1: `NetworkPlayer` 봇 세팅** — `_bodyCollider` 타입을 `Collider`로 바꾸고, `SetupRemote`의 몸체 만들기를 메서드로 분리한다:

```csharp
        private Collider _bodyCollider;   // (기존 CapsuleCollider 필드를 Collider로)

        public override void OnNetworkSpawn()
        {
            // ... (기존 구독/ApplySpawnPose까지 그대로)
            if (_isBot)
            {
                if (IsServer) SetupBot();
                else SetupRemote();
            }
            else if (IsOwner) SetupLocal();
            else SetupRemote();
        }

        /// <summary>서버가 조종하는 봇: CharacterController로 걷고, 몸체만 눈에 보이게 만든다.</summary>
        private void SetupBot()
        {
            _bodyCollider = GetComponent<CharacterController>();
            BuildBodyVisuals();
            ApplyColor();
            RefreshBodyVisibility();
        }
```
`SetupRemote`는 콜라이더 생성 다음에 `BuildBodyVisuals(); ApplyColor(); RefreshBodyVisibility();`를 부르도록 하고, 몸체(캡슐)와 코(구슬)를 만드는 기존 코드를 `private void BuildBodyVisuals()`로 옮긴다(동작 동일).

- [ ] **Step 2: `BotBrain` 작성** (파일 전체):

```csharp
using System.Collections.Generic;
using UnityEngine;
using Mirro.Maze;
using Mirro.Networking;

namespace Mirro.Gameplay
{
    /// <summary>
    /// 서버에서 봇 하나를 조종한다. 경기 시작 후 출발 지연이 지나면 BotPlanner가 정한 경로의 칸 중심을 차례로 걸어가고,
    /// 목표 깃발 칸에 도착하면 사람과 같은 시간(PullSeconds) 서 있다가 서버 검증(ServerTryPullFlag)으로 깃발을 뽑는다.
    /// </summary>
    public class BotBrain : MonoBehaviour
    {
        /// <summary>테스트용: 0 이상이면 난이도의 출발 지연 대신 이 값(초)을 쓴다.</summary>
        public static float StartDelayOverride = -1f;

        /// <summary>테스트용: 이동 속도 배율.</summary>
        public static float SpeedMultiplier = 1f;

        private const float AwarenessInterval = 0.5f;
        private const float ArriveDistance = 0.6f;
        private const float StuckCheckSeconds = 1.5f;
        private const float StuckDistance = 0.4f;
        private const float Gravity = -20f;

        private readonly Dictionary<int, ulong> _enemyFlags = new Dictionary<int, ulong>();

        private NetworkPlayer _player;
        private CharacterController _controller;
        private MazeData _maze;
        private float _cellSize;
        private BotSettings _settings;
        private BotPlanner _planner;

        private int _pathIndex;
        private float _nextAwareness;
        private ulong _pullTarget = MatchManager.NoOne;
        private float _pullTimer;
        private float _stuckTimer;
        private Vector3 _stuckAnchor;
        private float _sidestepUntil;
        private float _sidestepSign = 1f;
        private float _verticalVelocity;

        public void Init(NetworkPlayer player, MazeData maze, float cellSize, BotDifficulty difficulty, int seed)
        {
            _player = player;
            _controller = player.GetComponent<CharacterController>();
            _maze = maze;
            _cellSize = cellSize;
            _settings = BotSettings.For(difficulty);
            _planner = new BotPlanner(maze, _settings, seed);
            _stuckAnchor = transform.position;
        }

        private int CellIndexOf(Vector3 position)
        {
            var cell = SpawnPlacer.CellAt(position, _cellSize);
            return _maze.Index(Mathf.Clamp(cell.x, 0, _maze.Width - 1), Mathf.Clamp(cell.y, 0, _maze.Height - 1));
        }

        private Vector3 CenterOf(int cellIndex)
        {
            return SpawnPlacer.CellCenter(new Vector2Int(cellIndex % _maze.Width, cellIndex / _maze.Width), _cellSize);
        }

        private void Update()
        {
            var match = MatchManager.Instance;
            if (_planner == null || match == null || match.Finished.Value || !_player.IsAlive.Value) return;

            float delay = StartDelayOverride >= 0f ? StartDelayOverride : _settings.startDelay;
            if (match.Elapsed < delay) return;

            Vector3 position = transform.position;
            int cell = CellIndexOf(position);
            _planner.MarkVisited(cell);

            if (_pullTarget != MatchManager.NoOne)
            {
                UpdatePull(match);
                return;
            }

            Replan(cell);

            var path = _planner.Path;
            if (_pathIndex >= path.Count)
            {
                // 목표 깃발 칸에 도착했다.
                if (_planner.IsChasing && Flag.FindFor(_planner.TargetFlagId) != null)
                {
                    _pullTarget = _planner.TargetFlagId;
                    _pullTimer = 0f;
                }
                return;
            }

            Vector3 goal = CenterOf(path[_pathIndex]);
            Vector3 toGoal = goal - position;
            toGoal.y = 0f;
            float distance = toGoal.magnitude;
            if (distance < ArriveDistance) { _pathIndex++; return; }

            // 순간이동 등으로 경로에서 크게 벗어났으면 새로 찾는다.
            if (distance > _cellSize * 3f) { _planner.ClearTarget(); _pathIndex = 0; return; }

            Walk(toGoal / distance);
        }

        private void Replan(int cell)
        {
            bool targetGone = _planner.IsChasing && Flag.FindFor(_planner.TargetFlagId) == null;
            bool pathDone = _pathIndex >= _planner.Path.Count && !_planner.IsChasing;
            bool looking = !_planner.IsChasing && Time.time >= _nextAwareness;
            bool cleared = _planner.Path.Count == 0 && !_planner.IsChasing;
            if (!targetGone && !pathDone && !looking && !cleared) return;

            _enemyFlags.Clear();
            foreach (var flag in Flag.All)
                if (flag.PlayerId != _player.PlayerId) _enemyFlags[CellIndexOf(flag.transform.position)] = flag.PlayerId;

            _planner.Plan(cell, _enemyFlags);
            _pathIndex = 0;
            _nextAwareness = Time.time + AwarenessInterval;
        }

        private void UpdatePull(MatchManager match)
        {
            var flag = Flag.FindFor(_pullTarget);
            if (flag == null)
            {
                _pullTarget = MatchManager.NoOne;
                _planner.ClearTarget();
                return;
            }

            Vector3 look = flag.transform.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), 360f * Time.deltaTime);

            _pullTimer += Time.deltaTime;
            if (_pullTimer < MatchManager.PullSeconds) return;

            match.ServerTryPullFlag(_player.PlayerId, _pullTarget);
            _pullTarget = MatchManager.NoOne;
            _planner.ClearTarget();
        }

        private void Walk(Vector3 direction)
        {
            // 1.5초 동안 거의 못 나아갔다면(사람에게 막힘 등) 옆으로 비켜서 돌아간다.
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer >= StuckCheckSeconds)
            {
                Vector3 moved = transform.position - _stuckAnchor;
                moved.y = 0f;
                if (moved.magnitude < StuckDistance)
                {
                    _sidestepUntil = Time.time + 1f;
                    _sidestepSign = -_sidestepSign;
                }
                _stuckAnchor = transform.position;
                _stuckTimer = 0f;
            }

            Vector3 move = direction;
            if (Time.time < _sidestepUntil) move += Vector3.Cross(Vector3.up, direction) * (0.8f * _sidestepSign);
            move.Normalize();

            _verticalVelocity = _controller.isGrounded ? -1f : _verticalVelocity + Gravity * Time.deltaTime;
            Vector3 velocity = move * (_settings.speed * SpeedMultiplier) + Vector3.up * _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 540f * Time.deltaTime);
        }
    }
}
```

- [ ] **Step 3: `MazeGameBootstrap`에서 봇 스폰** — 사람 스폰 루프 뒤, 깃발 찾기 코드 앞에 추가하고, `cells` 계산을 봇 수만큼 늘린다:

```csharp
            var session = NetworkSession.Instance;
            int botCount = session.CurrentMode == GameMode.Bots ? session.BotCount.Value : 0;
            var cells = SpawnPlacer.Place(Maze, slots.Count + botCount);
```
(기존 `var cells = SpawnPlacer.Place(Maze, slots.Count);`를 교체.) 사람 루프 뒤:

```csharp
            var usedColors = new HashSet<int>();
            foreach (var slot in slots) usedColors.Add(slot.colorIndex);
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
                botGo.AddComponent<BotBrain>().Init(bot, Maze, cellSize, (BotDifficulty)session.BotDifficulty.Value, Maze.Seed + b * 7919);
                botControllers.Add(botGo.GetComponent<CharacterController>());

                matchPlayers.Add(new MatchPlayer
                {
                    clientId = botId,
                    colorIndex = color,
                    flagPosition = MatchManager.FlagPositionFor(pos, rot),
                    flagYaw = rot.eulerAngles.y
                });
                Debug.Log($"[Mirro] Spawned bot {botId} ({(BotDifficulty)session.BotDifficulty.Value}) in cell {cell}");
            }

            // 복도에서 봇끼리 마주쳐 서로 막지 않도록 봇끼리는 충돌을 무시한다.
            for (int i = 0; i < botControllers.Count; i++)
                for (int j = i + 1; j < botControllers.Count; j++)
                    Physics.IgnoreCollision(botControllers[i], botControllers[j]);
```
(`SpawnPlayers` 안에 이미 있는 `var session = NetworkSession.Instance;` 선언은 중복되지 않게 하나로 합친다.)

- [ ] **Step 4: 컴파일 + 멀티 회귀** — 빌드 성공, 3인 플레이테스트 `failures=0`(봇 관련 코드는 `botCount == 0`이라 실행되지 않는다).

- [ ] **Step 5: 커밋+푸시** — `.meta` 복사 후 커밋. 메시지 `Add bots: server-driven players with chase/explore AI`.

---

### Task 7: HUD, 결과 화면, 일시정지 메뉴 (솔로 모드용)

**Files:**
- Modify: `Assets/_Project/Scripts/UI/MatchHud.cs`, `ResultScreen.cs`, `PauseMenu.cs`

**Interfaces:**
- Consumes: `MatchManager.CurrentMode/Collected/TotalTreasures/Elapsed`, `NetworkSession.IsSolo/RestartSolo()`, `TreasureRecords.Submit`.
- Produces(테스트가 찾는 노드 이름): HUD `MatchCanvas/AliveLabel/Text`(모드별 문구), 결과 화면 `ResultCanvas/Panel/{Title,Subtitle,Row0..Row3(Rank,Name,Note),RestartButton,MenuButton}`, 일시정지 `PauseCanvas/Panel/RestartButton`.

- [ ] **Step 1: `MatchHud` 상단 문구** — `UpdateAlive` 교체, 알약 폭을 모드에 맞춰 넓힌다:

```csharp
        private static string FormatTime(double seconds)
        {
            var span = System.TimeSpan.FromSeconds(seconds);
            return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
        }

        private void UpdateAlive()
        {
            switch (_match.CurrentMode)
            {
                case GameMode.Treasure:
                    _alive.text = $"깃발 {_match.Collected.Value} / {_match.TotalTreasures.Value} · {FormatTime(_match.Elapsed)}";
                    break;
                case GameMode.Practice:
                    _alive.text = $"자유 연습 · {FormatTime(_match.Elapsed)}";
                    break;
                default:
                    _alive.text = $"생존 {_match.AliveCount} / {_match.TotalPlayers.Value}";
                    break;
            }

            float width = _match.CurrentMode == GameMode.Versus || _match.CurrentMode == GameMode.Bots ? 340f : 560f;
            _aliveRect.sizeDelta = new Vector2(width, 72f);
        }
```
`Build()`에서 알약 rect를 필드 `_aliveRect`로 저장한다(`var aliveRt = ...` → `_aliveRect = UIFactory.NewRect(...)`). `using Mirro.Core;` 추가. 탈락 알림의 봇 이름은 `DescribeElimination`에서 `(봇)`을 붙이지 않는다(색 이름만).

- [ ] **Step 2: `ResultScreen` 모드별 구성** — `Show`에서 솔로 여부와 모드를 읽고, `Build`를 다음처럼 바꾼다.
  - 순위: 우승자가 없으면(사람 탈락으로 끝난 봇 대결) 살아 있는 플레이어를 "생존"으로 맨 위에 둔다.

```csharp
        private static List<Rank> BuildRanking(MatchManager match)
        {
            var ranking = new List<Rank>();
            if (match.WinnerId.Value != MatchManager.NoOne)
                ranking.Add(new Rank { clientId = match.WinnerId.Value, colorIndex = match.WinnerColor.Value, note = "최후의 생존자" });
            else
                foreach (var player in NetworkPlayer.All)
                    if (player.IsAlive.Value) ranking.Add(new Rank { clientId = player.PlayerId, colorIndex = player.ColorIndex.Value, note = "생존" });

            for (int i = match.Eliminated.Count - 1; i >= 0; i--)
            {
                var entry = match.Eliminated[i];
                string note = "연결이 끊겨 탈락";
                if (entry.eliminatedBy != MatchManager.NoOne)
                    note = PlayerColors.GetName(ColorOf(match, entry.eliminatedBy)) + "에게 깃발을 뽑힘";
                ranking.Add(new Rank { clientId = entry.clientId, colorIndex = entry.colorIndex, note = note });
            }
            return ranking;
        }

        private static string NameOf(Rank rank, ulong localId)
        {
            string name = PlayerColors.GetName(rank.colorIndex);
            if (rank.clientId == localId) return name + " (나)";
            return MatchManager.IsBotId(rank.clientId) ? name + " (봇)" : name;
        }
```
  - 행 이름은 `NameOf(rank, localId)`로. 제목: 내가 우승자면 "승리!", 봇 대결에서 내가 졌으면 "패배", 그 외 "게임 종료". 부제: 승리 "마지막까지 살아남았어요", 패배 "내 깃발이 뽑혔어요", 그 외 기존 문구, 뒤에 `· 경기 시간 mm분 ss초`.
  - **깃발 찾기 결과**(`match.CurrentMode == GameMode.Treasure`): 제목 "완료!"(금색), 부제 "깃발 N개를 모두 찾았어요", 순위 행 대신 두 줄 — `Row0`: Name "기록", Note "mm:ss"; `Row1`: Name "최고 기록", Note "mm:ss" (새 기록이면 Note 끝에 " · 새 기록!", 금색). 기록은 `TreasureRecords.Submit(GameSession.MazeSize, (float)match.Elapsed, out float previous)` 결과로, **`Show`에서 한 번만** 호출한다.
  - 버튼: `IsSolo`면 `RestartButton`("다시 하기") + `MenuButton`(방장 힌트 문구 없음), 멀티 방장은 기존 구성, 그 외는 기존 대기 힌트. 솔로의 다시 하기는 `NetworkSession.Instance.RestartSolo()`.

- [ ] **Step 3: `PauseMenu` 솔로** — 방장이거나 솔로일 때 `RestartButton`을 만들고, 솔로면 문구를 "다시 시작"으로, 눌렀을 때 `RestartSolo()`를 부른다:

```csharp
            bool solo = NetworkSession.Instance != null && NetworkSession.Instance.IsSolo.Value;
            _restartText = solo ? "다시 시작" : "로비로 돌아가기";
            ...
            var session = NetworkSession.Instance;
            if (session != null) { if (solo) session.RestartSolo(); else session.ReturnToLobby(); }
```
(`RestartLabel` 상수는 필드 `_restartText`로 바꾸고, `DisarmRestart`/확인 문구 처리는 그대로 쓴다. 확인 문구는 솔로에서 "한 번 더 누르세요"로 한다.)

- [ ] **Step 4: 컴파일 + 멀티 회귀** — 빌드 성공, 2인/3인 플레이테스트 `failures=0`(Versus 결과 화면/일시정지는 기존과 같은 노드/문구).

- [ ] **Step 5: 커밋+푸시** — 메시지 `Adapt HUD, result screen and pause menu to solo modes`.

---

### Task 8: 메뉴 — 혼자 하기, 모드 카드, 봇 설정

**Files:**
- Create: `Assets/_Project/Scripts/UI/SoloModeScreen.cs`, `Assets/_Project/Scripts/UI/BotOptionsScreen.cs`
- Modify: `Assets/_Project/Scripts/UI/MainMenuUI.cs`

**Interfaces:**
- Consumes: `NetworkFlow.HostRoom(..., solo: true)`, `NetworkSession.ConfigureSolo/StartSoloGame`.
- Produces(테스트가 찾는 노드 이름): `MenuCanvas/TitleScreen/SoloButton`, `SoloModeScreen/Mode_Bots|Mode_Treasure|Mode_Practice`, `BotOptionsScreen/BotCount_1|2|3`, `BotOptionsScreen/Diff_Easy|Normal|Hard`, `BotOptionsScreen/BotStartButton`, `BotOptionsScreen/Error`, 크기 화면 `StartButton`의 문구 "시작".

- [ ] **Step 1: `SoloModeScreen`** (`LobbyScreen`처럼 `MainMenuUI.NewScreen`으로 루트를 만든다):

```csharp
using System;
using UnityEngine;
using Mirro.Core;

namespace Mirro.UI
{
    /// <summary>혼자 하기: 모드 카드 3장(봇과 대결 / 깃발 찾기 / 자유 연습).</summary>
    public class SoloModeScreen
    {
        public RectTransform Root { get; }

        public SoloModeScreen(MainMenuUI menu, Action<GameMode> onPick, Action onBack)
        {
            Root = menu.NewScreen("SoloModeScreen");

            var title = UIFactory.AddLabel(Root, "Title", "혼자 하기", 90, Color.white, FontStyle.Bold);
            UIFactory.SetBox(title.rectTransform, new Vector2(0f, 420f), new Vector2(1400f, 140f));
            var subtitle = UIFactory.AddLabel(Root, "Subtitle", "어떤 방식으로 즐길까요?", 38, MainMenuUI.MutedText);
            UIFactory.SetBox(subtitle.rectTransform, new Vector2(0f, 335f), new Vector2(1000f, 60f));

            AddCard("Mode_Bots", "봇과 대결", "AI 봇과 깃발을\n뽑는 대결", new Color(0.95f, 0.55f, 0.45f), -480f, () => onPick(GameMode.Bots));
            AddCard("Mode_Treasure", "깃발 찾기", "숨겨진 금색 깃발을\n모두 찾는 타임어택", new Color(0.98f, 0.82f, 0.30f), 0f, () => onPick(GameMode.Treasure));
            AddCard("Mode_Practice", "자유 연습", "승패 없이\n미로를 탐험", new Color(0.55f, 0.78f, 0.60f), 480f, () => onPick(GameMode.Practice));

            UIFactory.AddTextButton(Root, "BackButton", "뒤로", new Vector2(-780f, -450f), new Vector2(240f, 90f),
                new Color(0.24f, 0.27f, 0.29f), Color.white, 40, () => onBack(), 36f);
        }

        private void AddCard(string name, string label, string description, Color color, float x, Action onClick)
        {
            var card = UIFactory.NewRect(name, Root);
            UIFactory.SetBox(card, new Vector2(x, -60f), new Vector2(420f, 560f));
            var image = UIFactory.AddRounded(card, color, 56f);
            UIFactory.MakeButton(card, image, () => onClick(), 1.06f);

            Color textColor = UIFactory.ContrastText(color);
            var nameLabel = UIFactory.AddLabel(card, "Name", label, 72, textColor, FontStyle.Bold);
            UIFactory.SetBox(nameLabel.rectTransform, new Vector2(0f, 90f), new Vector2(400f, 110f));
            var desc = UIFactory.AddLabel(card, "Description", description, 34, new Color(textColor.r, textColor.g, textColor.b, 0.85f));
            UIFactory.SetBox(desc.rectTransform, new Vector2(0f, -80f), new Vector2(400f, 130f));
        }
    }
}
```

- [ ] **Step 2: `BotOptionsScreen`** — 봇 수(1~3)와 난이도 선택, 시작 버튼:

```csharp
using System;
using UnityEngine;
using UnityEngine.UI;
using Mirro.Gameplay;

namespace Mirro.UI
{
    /// <summary>봇과 대결 설정: 봇 수(1~3)와 난이도.</summary>
    public class BotOptionsScreen
    {
        private static readonly Color Unselected = new Color(0.24f, 0.27f, 0.29f);
        private static readonly string[] DifficultyNames = { "쉬움", "보통", "어려움" };

        public RectTransform Root { get; }
        public int BotCount { get; private set; } = 3;
        public BotDifficulty Difficulty { get; private set; } = BotDifficulty.Normal;

        private readonly Image[] _countButtons = new Image[3];
        private readonly Image[] _difficultyButtons = new Image[3];
        private readonly Text _error;
        private Color _accent = MainMenuUI.DefaultAccent;

        public BotOptionsScreen(MainMenuUI menu, Action onStart, Action onBack)
        {
            Root = menu.NewScreen("BotOptionsScreen");

            var title = UIFactory.AddLabel(Root, "Title", "봇 설정", 90, Color.white, FontStyle.Bold);
            UIFactory.SetBox(title.rectTransform, new Vector2(0f, 400f), new Vector2(1400f, 140f));

            var countLabel = UIFactory.AddLabel(Root, "CountLabel", "봇 수", 44, MainMenuUI.MutedText, FontStyle.Bold);
            UIFactory.SetBox(countLabel.rectTransform, new Vector2(0f, 250f), new Vector2(600f, 60f));
            for (int i = 0; i < 3; i++)
            {
                int count = i + 1;
                var button = UIFactory.AddTextButton(Root, "BotCount_" + count, count + "명", new Vector2((i - 1) * 260f, 150f),
                    new Vector2(220f, 110f), Unselected, Color.white, 48, () => { BotCount = count; Refresh(); }, 40f);
                _countButtons[i] = button.image;
            }

            var difficultyLabel = UIFactory.AddLabel(Root, "DifficultyLabel", "난이도", 44, MainMenuUI.MutedText, FontStyle.Bold);
            UIFactory.SetBox(difficultyLabel.rectTransform, new Vector2(0f, 20f), new Vector2(600f, 60f));
            string[] ids = { "Easy", "Normal", "Hard" };
            for (int i = 0; i < 3; i++)
            {
                var level = (BotDifficulty)i;
                var button = UIFactory.AddTextButton(Root, "Diff_" + ids[i], DifficultyNames[i], new Vector2((i - 1) * 340f, -80f),
                    new Vector2(300f, 110f), Unselected, Color.white, 48, () => { Difficulty = level; Refresh(); }, 40f);
                _difficultyButtons[i] = button.image;
            }

            var hint = UIFactory.AddLabel(Root, "Hint", "쉬움: 봇이 가까운 깃발만 알아채요 · 보통/어려움: 봇이 길을 알고 곧장 와요", 32, MainMenuUI.MutedText);
            UIFactory.SetBox(hint.rectTransform, new Vector2(0f, -190f), new Vector2(1600f, 50f));

            UIFactory.AddTextButton(Root, "BotStartButton", "시작", new Vector2(0f, -320f), new Vector2(520f, 110f),
                MainMenuUI.DefaultAccent, Color.white, 56, () => onStart(), 44f);
            UIFactory.AddTextButton(Root, "BackButton", "뒤로", new Vector2(-780f, -450f), new Vector2(240f, 90f),
                new Color(0.24f, 0.27f, 0.29f), Color.white, 40, () => onBack(), 36f);

            _error = UIFactory.AddLabel(Root, "Error", string.Empty, 34, MainMenuUI.WarningText);
            UIFactory.SetBox(_error.rectTransform, new Vector2(0f, -420f), new Vector2(1500f, 50f));
            Refresh();
        }

        public void Show(Color accent)
        {
            _accent = accent;
            _error.text = string.Empty;
            Refresh();
        }

        public void SetError(string message) => _error.text = message;

        private void Refresh()
        {
            for (int i = 0; i < 3; i++)
            {
                _countButtons[i].color = BotCount == i + 1 ? _accent : Unselected;
                _difficultyButtons[i].color = (int)Difficulty == i ? _accent : Unselected;
            }
        }
    }
}
```
(버튼 글자색은 선택 여부와 무관하게 흰색으로 둔다 — 밝은 강조색에서 읽기 어려우면 `UIFactory.ContrastText(_accent)`로 `Label` 색도 갱신한다.)

- [ ] **Step 3: `MainMenuUI` 연결** —
  - 필드: `private SoloModeScreen _soloScreen; private BotOptionsScreen _botOptions; private GameMode? _soloMode;`
  - `Build()`의 마지막에 `_soloScreen = new SoloModeScreen(this, PickSoloMode, () => ShowTitleScreen()); _botOptions = new BotOptionsScreen(this, StartSolo, () => ShowSizeScreen(_theme));`
  - `Activate`의 화면 목록에 `_soloScreen.Root, _botOptions.Root` 추가.
  - `BuildTitleScreen`: 버튼 위치를 방 만들기 y=-20, 참가하기 y=-170, **혼자 하기 y=-320**(이름 `SoloButton`, 색 `SecondaryButton`, 글자 흰색, `ShowSoloModeScreen`), 메시지 y=-450.
  - `ShowTitleScreen`에서 `_soloMode = null;`
  - 새 메서드:

```csharp
        public void ShowSoloModeScreen()
        {
            _soloMode = null;
            _theme = null;
            _selectedSize = 0;
            SetBackground(NeutralBackground);
            Activate(_soloScreen.Root);
        }

        private void PickSoloMode(GameMode mode)
        {
            _soloMode = mode;
            _theme = null;
            _selectedSize = 0;
            SetBackground(NeutralBackground);
            Activate(_themeScreen);
        }

        private void StartSolo()
        {
            if (_soloMode == null || _theme == null || _selectedSize == 0) return;

            if (!NetworkFlow.Instance.HostRoom(_theme, _selectedSize, NetworkFlow.DefaultPort, solo: true))
            {
                string reason = NetworkFlow.Instance.FailureReason ?? "게임을 시작하지 못했어요.";
                if (_soloMode == GameMode.Bots) _botOptions.SetError(reason); else _sizeError.text = reason;
                return;
            }

            var session = NetworkSession.Instance;
            session.ConfigureSolo(_soloMode.Value, _botOptions.BotCount, _botOptions.Difficulty);
            session.StartSoloGame();
        }
```
  - `CreateRoom()` 앞머리: `if (_soloMode != null) { if (_soloMode == GameMode.Bots) ShowBotOptionsScreen(); else StartSolo(); return; }` 그리고 `ShowBotOptionsScreen() { _botOptions.Show(_theme.accentColor); SetBackgroundTint(_theme.accentColor); Activate(_botOptions.Root); }`
  - 테마 화면 "뒤로" 버튼: `() => { if (_soloMode != null) ShowSoloModeScreen(); else ShowTitleScreen(); }`. `ShowSizeScreen`에서 `_createButton.label.text = _soloMode != null ? "시작" : "방 만들기";`, `ShowThemeScreen`의 `_theme=null` 등은 그대로(솔로 모드 값은 유지).
  - 방을 나올 때(`ShowTitleScreen`) 솔로 상태가 초기화되도록 위에서 처리했다.

- [ ] **Step 2: 컴파일** — 동기화 → 빌드 성공(`error CS` 없음).

- [ ] **Step 3: 화면 확인(스크린샷은 Task 9 테스트가 남긴다)** — 이 작업에서는 컴파일과 멀티 회귀(방 만들기/참가하기 메뉴 노드 이름과 위치가 그대로여야 함: 2인 플레이테스트 `failures=0`, 특히 `create-room button`/`join-room button` 클릭 검증 통과)만 확인한다.

- [ ] **Step 4: 커밋+푸시** — `.meta` 복사 후 커밋. 메시지 `Add solo entry to the main menu with mode cards and bot options`.

---

### Task 9: 솔로 3모드 플레이테스트와 최종 회귀

**Files:**
- Modify: `Assets/_Project/Scripts/Testing/PlaytestDriver.cs`
- Create(스크래치패드, 커밋 안 함): `$WSP/runsolo.sh`

**Interfaces:**
- Consumes: Task 5~8의 노드 이름, `BotBrain.StartDelayOverride/SpeedMultiplier`, 기존 헬퍼(`ClickUi`, `MenuNode`, `Hold`, `TapKey`, `WaitFor`, `Shot`, `PullFlagWithKeyboard`, `HudNode`, `OverlapsWall`, `VerifyBackAtMenu`).
- Produces: 실행 역할 `solobots`, `solohunt`, `solopractice`(`-mirroRole`), 크기/시즌은 기존 `-mirroSize`/`-mirroSeason`.

- [ ] **Step 1: 역할 분기** — `AutoStart`에서 `if (d._role == "solo") d._players = 1;`를 `if (d._role.StartsWith("solo")) d._players = 1;`로. `Run()`에서 `HostMenuFlow` 분기 앞에:

```csharp
            if (_role == "solobots" || _role == "solohunt" || _role == "solopractice")
            {
                yield return SoloModeMenuFlow();
                if (_abort) { Finish(); yield break; }
                yield return WaitFor(() => NetworkPlayer.Local != null, 60f, "solo game scene loaded and local player spawned");
                if (_abort) { Finish(); yield break; }

                if (_role == "solobots") yield return SoloBotsChecks();
                else if (_role == "solohunt") yield return SoloHuntChecks();
                else yield return SoloPracticeChecks();
                Finish();
                yield break;
            }
```

- [ ] **Step 2: 메뉴 클릭 흐름** — 실제 클릭으로 타이틀 → 혼자 하기 → 모드 카드 → 테마 → 크기 → (봇) 설정 → 시작:

```csharp
        private IEnumerator SoloModeMenuFlow()
        {
            string mode = _role == "solobots" ? "Bots" : _role == "solohunt" ? "Treasure" : "Practice";

            yield return ClickUi(MenuNode("TitleScreen/SoloButton"), "solo button", () => MenuScreenActive("SoloModeScreen"));
            Check(MenuScreenActive("SoloModeScreen"), "solo mode screen shown");
            Check(MenuNode("SoloModeScreen/Mode_Bots") != null && MenuNode("SoloModeScreen/Mode_Treasure") != null && MenuNode("SoloModeScreen/Mode_Practice") != null,
                "three mode cards are offered");
            yield return Shot("02_solo_modes");

            yield return ClickUi(MenuNode("SoloModeScreen/Mode_" + mode), "mode card " + mode, () => MenuScreenActive("ThemeScreen"));
            yield return ClickUi(MenuNode("ThemeScreen/Card_" + _season), "theme card " + _season, () => MenuScreenActive("SizeScreen"));
            Check(MenuNode("SizeScreen/StartButton/Label").GetComponent<Text>().text == "시작", "size screen's confirm button says start in solo flow");

            var ring = MenuNode($"SizeScreen/Size_{_size}/Ring");
            yield return ClickUi(MenuNode($"SizeScreen/Size_{_size}/Base"), "size button " + _size, () => ring.gameObject.activeSelf);

            if (_role == "solobots")
            {
                yield return ClickUi(MenuNode("SizeScreen/StartButton"), "size confirm button", () => MenuScreenActive("BotOptionsScreen"));
                Check(MenuScreenActive("BotOptionsScreen"), "bot options screen shown for the bot mode");
                yield return ClickUi(MenuNode("BotOptionsScreen/BotCount_3"), "bot count 3", () => true);
                yield return ClickUi(MenuNode("BotOptionsScreen/Diff_Hard"), "hard difficulty", () => true);
                yield return Shot("03_bot_options");
                yield return ClickUi(MenuNode("BotOptionsScreen/BotStartButton"), "bot start button", () => SceneManager.GetActiveScene().name == GameSession.GameScene);
            }
            else
            {
                yield return ClickUi(MenuNode("SizeScreen/StartButton"), "size confirm button", () => SceneManager.GetActiveScene().name == GameSession.GameScene);
            }

            yield return WaitFor(() => SceneManager.GetActiveScene().name == GameSession.GameScene, 30f, "solo game started without a lobby");
            Check(NetworkSession.Instance != null && NetworkSession.Instance.IsSolo.Value, "the room is a local-only solo room");
        }
```
(`Diff_Hard`를 골라 봇이 이동 시작이 빠르고 추격형이 되게 한다. 테스트는 `BotBrain.StartDelayOverride`로 출발 지연을 덮어쓴다.)

- [ ] **Step 3: 봇과 대결 검증** — `SoloBotsChecks`:

```csharp
        private IEnumerator SoloBotsChecks()
        {
            BotBrain.StartDelayOverride = 4f;
            BotBrain.SpeedMultiplier = 2f;
            var local = NetworkPlayer.Local;
            var maze = MazeGameBootstrap.Instance.Maze;
            float cell = MazeGameBootstrap.Instance.CellSize;

            yield return WaitFor(() => MatchManager.Instance != null && NetworkPlayer.All.Count == 4 && Flag.All.Count == 4, 30f, "the player, 3 bots and 4 flags exist");
            if (_abort) yield break;
            var match = MatchManager.Instance;
            var bots = new List<NetworkPlayer>();
            foreach (var p in NetworkPlayer.All) if (p.IsBot) bots.Add(p);
            var colors = new HashSet<int> { local.ColorIndex.Value };
            foreach (var b in bots) colors.Add(b.ColorIndex.Value);
            Check(bots.Count == 3 && colors.Count == 4, "three bots with distinct colors, none sharing the player's");
            Check(match.CurrentMode == GameMode.Bots && HudNode("AliveLabel/Text").GetComponent<Text>().text == "생존 4 / 4", "HUD shows 4 alive in bot mode");
            foreach (var b in bots) Check(BodyOf(b) != null && BodyOf(b).enabled && MatchManager.IsBotId(b.PlayerId), $"bot {b.PlayerId} has a visible body");
            Check(local.IsLocalHuman && !local.IsBot, "the local player is the only human");
            yield return Shot("06_bots_spawn");

            // 출발 지연 전에는 서 있다가, 지연 후에는 목표(가장 가까운 남의 깃발)를 향해 경로 거리가 줄어들며 걷는다.
            var start = new Dictionary<ulong, Vector3>();
            foreach (var b in bots) start[b.PlayerId] = b.transform.position;
            yield return WaitMatchTime(2.5f);
            foreach (var b in bots) Check(Vector3.Distance(start[b.PlayerId], b.transform.position) < 0.3f, $"bot {b.PlayerId} waits during its start delay");

            var fields = new Dictionary<ulong, int[]>();
            foreach (var flag in Flag.All)
                fields[flag.PlayerId] = SpawnPlacer.PathDistances(maze, SpawnPlacer.CellAt(flag.transform.position, cell));
            System.Func<NetworkPlayer, int> distanceToNearestEnemyFlag = bot =>
            {
                var c = SpawnPlacer.CellAt(bot.transform.position, cell);
                int best = int.MaxValue;
                foreach (var pair in fields) if (pair.Key != bot.PlayerId) best = Mathf.Min(best, pair.Value[maze.Index(Mathf.Clamp(c.x, 0, maze.Width - 1), Mathf.Clamp(c.y, 0, maze.Height - 1))]);
                return best;
            };
            var before = new Dictionary<ulong, int>();
            foreach (var b in bots) before[b.PlayerId] = distanceToNearestEnemyFlag(b);

            int wallHits = 0, samples = 0;
            for (float t = 0f; t < 12f; t += Time.unscaledDeltaTime)
            {
                foreach (var b in bots) { if (OverlapsWall(b.transform.position)) wallHits++; samples++; }
                yield return null;
            }
            foreach (var b in bots)
                Check(distanceToNearestEnemyFlag(b) <= before[b.PlayerId] - 5, $"bot {b.PlayerId} walked toward the nearest flag ({before[b.PlayerId]} -> {distanceToNearestEnemyFlag(b)} cells)");
            Check(wallHits == 0, $"bots never overlap walls while walking ({wallHits}/{samples} samples)");
            yield return Shot("07_bots_walking");

            // 봇 하나를 내 깃발 칸으로 옮기면 사람과 같은 방식으로 서서 뽑고, 사람이 탈락해 패배 결과 화면이 뜬다.
            var attacker = bots[0];
            var myFlag = Flag.FindFor(local.PlayerId);
            attacker.TeleportTo(SpawnPlacer.CellCenter(SpawnPlacer.CellAt(myFlag.transform.position, cell), cell), Quaternion.identity);
            yield return WaitFor(() => match.Finished.Value, 25f, "a bot standing at the player's flag pulls it and ends the game");
            if (_abort) yield break;
            Check(!local.IsAlive.Value && match.WinnerId.Value == MatchManager.NoOne, "the player is eliminated and nobody wins");
            yield return WaitFor(() => ResultScreen.IsShowing, 10f, "result screen shows after the defeat");
            var panel = GameObject.Find("ResultScreen").transform.Find("ResultCanvas/Panel");
            Check(panel.Find("Title").GetComponent<Text>().text == "패배", "result title says defeat");
            int botRows = 0;
            for (int r = 0; r < 4; r++)
            {
                var row = panel.Find("Row" + r);
                if (row.gameObject.activeSelf && row.Find("Name").GetComponent<Text>().text.EndsWith("(봇)")) botRows++;
            }
            Check(botRows == 3, $"result rows mark the three bots ({botRows})");
            Check(panel.Find("RestartButton") != null && panel.Find("MenuButton") != null, "solo result screen offers restart and main menu");
            yield return Shot("08_bots_defeat");

            // 다시 하기 → 새 미로에서 처음부터. 이번엔 봇을 멈춰 두고 내가 봇 깃발을 모두 뽑아 이긴다.
            int firstSeed = GameSession.Seed;
            BotBrain.StartDelayOverride = 9999f;
            yield return ClickUi(panel.Find("RestartButton"), "solo restart button", () => !ResultScreen.IsShowing);
            yield return WaitFor(() => NetworkPlayer.Local != null && NetworkPlayer.Local != local && MatchManager.Instance != null && Flag.All.Count == 4 && NetworkPlayer.All.Count == 4,
                40f, "restart reloads the game with fresh players and flags");
            if (_abort) yield break;
            Check(GameSession.Seed != firstSeed && !ResultScreen.IsShowing, "restart uses a new maze and clears the result screen");
            local = NetworkPlayer.Local;
            match = MatchManager.Instance;
            Check(match.Eliminated.Count == 0 && !match.Finished.Value && local.IsAlive.Value, "restart begins with a fresh match");

            var targets = new List<NetworkPlayer>();
            foreach (var p in NetworkPlayer.All) if (p.IsBot) targets.Add(p);
            foreach (var b in targets)
            {
                yield return PullFlagWithKeyboard(b.PlayerId, PlayerColors.GetName(b.ColorIndex.Value));
                if (_abort) yield break;
                yield return WaitFor(() => match.IsEliminated(b.PlayerId), 15f, $"bot {b.PlayerId} is eliminated");
            }
            yield return WaitFor(() => match.Finished.Value && ResultScreen.IsShowing, 15f, "eliminating every bot finishes the game");
            Check(match.WinnerId.Value == local.PlayerId, "the player wins");
            panel = GameObject.Find("ResultScreen").transform.Find("ResultCanvas/Panel");
            Check(panel.Find("Title").GetComponent<Text>().text == "승리!", "result title says victory");
            yield return Shot("09_bots_victory");

            yield return ClickUi(panel.Find("MenuButton"), "result-screen main-menu button", () => SceneManager.GetActiveScene().name == GameSession.MenuScene);
            yield return VerifyBackAtMenu(false);
            BotBrain.StartDelayOverride = -1f;
            BotBrain.SpeedMultiplier = 1f;
        }
```

- [ ] **Step 4: 깃발 찾기 검증** — `SoloHuntChecks`:

```csharp
        private IEnumerator SoloHuntChecks()
        {
            PlayerPrefs.DeleteKey("mirro.treasure.best." + _size);
            var local = NetworkPlayer.Local;
            int total = Mathf.Max(1, _size / 10);

            yield return WaitFor(() => MatchManager.Instance != null && Flag.All.Count == total, 30f, $"{total} treasure flags exist");
            if (_abort) yield break;
            var match = MatchManager.Instance;
            Check(match.CurrentMode == GameMode.Treasure && match.TotalTreasures.Value == total && NetworkPlayer.All.Count == 1, "treasure mode: one player, no bots");
            bool allGold = true, allTreasureIds = true;
            foreach (var flag in Flag.All)
            {
                allTreasureIds &= MatchManager.IsTreasureId(flag.PlayerId);
                var cloth = flag.transform.Find("ClothPivot/Cloth").GetComponent<Renderer>().sharedMaterial;
                Color c = cloth.color;
                allGold &= Mathf.Abs(c.r - Flag.TreasureColor.r) < 0.05f && Mathf.Abs(c.g - Flag.TreasureColor.g) < 0.05f;
            }
            Check(allGold && allTreasureIds, "every flag is gold and ownerless");
            Check(Flag.FindFor(local.PlayerId) == null, "the player has no flag of their own in this mode");
            var counter = HudNode("AliveLabel/Text").GetComponent<Text>();
            Check(counter.text.StartsWith($"깃발 0 / {total}"), $"HUD counts collected flags (\"{counter.text}\")");
            yield return Shot("06_hunt_start");

            int collected = 0;
            while (Flag.All.Count > 0)
            {
                var flag = Flag.All[0];
                ulong id = flag.PlayerId;
                yield return PullFlagWithKeyboard(id, "금색");
                if (_abort) yield break;
                collected++;
                int target = collected;
                yield return WaitFor(() => match.Collected.Value >= target && counter.text.StartsWith($"깃발 {target} / {total}"), 15f, $"flag {target}/{total} collected and HUD updated");
            }
            yield return WaitFor(() => match.Finished.Value && ResultScreen.IsShowing, 15f, "collecting every flag finishes the run");
            Check(match.WinnerId.Value == local.PlayerId, "the run is completed by the player");
            var panel = GameObject.Find("ResultScreen").transform.Find("ResultCanvas/Panel");
            Check(panel.Find("Title").GetComponent<Text>().text == "완료!", "result title says completed");
            string record = panel.Find("Row0/Note").GetComponent<Text>().text;
            string best = panel.Find("Row1/Note").GetComponent<Text>().text;
            Check(record.Contains(":") && best.Contains("새 기록"), $"the first run is a new record (record {record}, best \"{best}\")");
            Check(TreasureRecords.Best(_size) > 0f && Mathf.Abs(TreasureRecords.Best(_size) - (float)match.Elapsed) < 2f, "the best time is saved on this PC");
            yield return Shot("07_hunt_result");

            // 다시 하기 → 새 미로, 카운터 초기화.
            int firstSeed = GameSession.Seed;
            yield return ClickUi(panel.Find("RestartButton"), "solo restart button", () => !ResultScreen.IsShowing);
            yield return WaitFor(() => MatchManager.Instance != null && MatchManager.Instance != match && Flag.All.Count == total, 40f, "restart sets up a fresh treasure hunt");
            if (_abort) yield break;
            Check(GameSession.Seed != firstSeed && MatchManager.Instance.Collected.Value == 0 && !MatchManager.Instance.Finished.Value, "restart resets the counter on a new maze");

            var pause = FindAnyObjectByType<PauseMenu>();
            yield return LeaveViaPauseMenu(pause);
            yield return VerifyBackAtMenu(false);
            PlayerPrefs.DeleteKey("mirro.treasure.best." + _size);
        }
```

- [ ] **Step 5: 자유 연습 검증** — `SoloPracticeChecks`:

```csharp
        private IEnumerator SoloPracticeChecks()
        {
            yield return WaitFor(() => MatchManager.Instance != null, 30f, "match manager exists");
            if (_abort) yield break;
            var match = MatchManager.Instance;
            var local = NetworkPlayer.Local;
            Check(match.CurrentMode == GameMode.Practice && Flag.All.Count == 0 && NetworkPlayer.All.Count == 1, "practice: no flags and no other players");
            var label = HudNode("AliveLabel/Text").GetComponent<Text>();
            Check(label.text.StartsWith("자유 연습"), $"HUD names the practice mode (\"{label.text}\")");

            Vector3 p0 = local.transform.position;
            yield return Hold(1f, Key.W);
            Check(Vector3.Distance(p0, local.transform.position) > 1.2f, "the player can walk around");
            yield return new WaitForSeconds(6f);
            Check(!match.Finished.Value && !ResultScreen.IsShowing, "a practice run never ends by itself");
            yield return Shot("06_practice");

            var pause = FindAnyObjectByType<PauseMenu>();
            yield return TapKey(Key.Escape);
            var restart = pause.transform.Find("PauseCanvas/Panel/RestartButton");
            Check(pause.IsOpen && restart != null && restart.Find("Label").GetComponent<Text>().text == "다시 시작", "pause menu offers restart in solo");
            int firstSeed = GameSession.Seed;
            var restartLabel = restart.Find("Label").GetComponent<Text>();
            string original = restartLabel.text;
            yield return ClickUi(restart, "pause-menu restart button", () => restartLabel.text != original);
            yield return ClickUi(restart, "pause-menu restart button (confirm)", () => GameSession.Seed != firstSeed || NetworkPlayer.Local != local);
            yield return WaitFor(() => NetworkPlayer.Local != null && NetworkPlayer.Local != local && MatchManager.Instance != null && MatchManager.Instance != match, 40f, "restart reloads the practice game");
            if (_abort) yield break;
            Check(GameSession.Seed != firstSeed, "restart uses a new maze");

            yield return LeaveViaPauseMenu(FindAnyObjectByType<PauseMenu>());
            yield return VerifyBackAtMenu(false);
        }
```
(`using Mirro.Gameplay;`는 이미 있다.)

- [ ] **Step 6: 솔로 실행 스크립트** — 솔로 프로세스와 늦은 접속(`intruder`)을 함께 돌려 "혼자 하기 방이에요." 거절을 확인한다(스크래치패드 `runsolo.sh`):

```bash
#!/bin/bash
# 사용: ROLE=solobots|solohunt|solopractice SIZE=50 SEASON=Spring PLAYDIR=solo_x bash runsolo.sh
SP=/c/Temp/claude/C--project-mirro/77953bc0-a754-4781-bcc3-a7324ae3ec71/scratchpad
DIR="${PLAYDIR:-solo_x}"
OUT='C:\Temp\claude\C--project-mirro\77953bc0-a754-4781-bcc3-a7324ae3ec71\scratchpad\'"$DIR"
mkdir -p "$SP/$DIR"; rm -f "$SP/$DIR"/*
EXE="$SP/build/Mirro.exe"
COMMON="-screen-fullscreen 0 -screen-width 800 -screen-height 450 -mirroPlaytest -mirroOut $OUT -mirroPlayers 1 -mirroSize ${SIZE:-50} -mirroSeason ${SEASON:-Spring} -mirroIp 127.0.0.1"
"$EXE" $COMMON -mirroRole ${ROLE:-solobots} -mirroTag solo -logFile "$OUT\\solo.log" &
SOLO=$!
sleep 14
"$EXE" $COMMON -mirroRole intruder -mirroTag intruder -logFile "$OUT\\intruder.log" &
INTRUDER=$!
wait $SOLO; echo "solo exit=$?"
wait $INTRUDER; echo "intruder exit=$?"
echo ALLDONE
```
그리고 `intruder` 역할이 솔로 방에서 거절 문구를 확인하도록 기존 `IntruderFlow`의 통과 조건(거절 + 사유 표시 + 방 목록에 없음)을 그대로 쓴다.

- [ ] **Step 7: 빌드 후 3모드 실행과 회귀** — 동기화 → 빌드 → `ROLE=solobots SIZE=50 SEASON=Spring`, `ROLE=solohunt SIZE=50 SEASON=Autumn`, `ROLE=solopractice SIZE=70 SEASON=Winter` 각각 `report_solo.txt`와 `report_intruder.txt`가 `failures=0 errors=0 warnings=0`. 이어서 회귀: 기존 `solo`(100×100), 멀티 2/3/4인 플레이테스트 모두 `failures=0 errors=0 warnings=0`, 셀프 테스트 `ALL PASSED`. 실패하면 원인을 고치고 이 단계를 반복한다. 스크린샷(`02_solo_modes`, `03_bot_options`, `06_*`, `08_bots_defeat`, `09_bots_victory`, `07_hunt_result`)을 열어 화면이 의도대로인지 눈으로 확인한다.

- [ ] **Step 8: 커밋+푸시** — 메시지 `Add solo mode playtests (bots, flag hunt, practice)`.

---

### Task 10: 마무리 (문서/메모리/푸시)

**Files:**
- Modify: `docs/superpowers/specs/2026-09-20-solo-modes-design.md`(구현 중 바뀐 점이 있으면 반영, 상태를 "구현됨"으로), 메모리 `project_mirro_maze_game.md`

- [ ] **Step 1: 실제 프로젝트와 검증 복사본 일치 확인** — 스크립트/메타 해시 비교로 차이 0, 복사본에만 있는 새 `.meta` 0.
- [ ] **Step 2: 스펙 상태 갱신** — 스펙 맨 위 상태를 `구현 완료`로 바꾸고, 구현 중 정한 세부 사항(포트 범위, 금색 깃발 이름 "금색" 등)이 스펙과 다르면 스펙을 고친다.
- [ ] **Step 3: 메모리 갱신** — 솔로 모드 완료, 새 파일/클래스 목록, 테스트 실행법(`runsolo.sh`), 주의점을 `project_mirro_maze_game.md`에 기록하고 "승인 대기" 문단을 제거한다.
- [ ] **Step 4: 최종 커밋+푸시** — 메시지 `Finalize solo modes docs`. 사용자에게 결과(모드 3개, 테스트 결과, 남은 위험, 직접 해볼 일)를 보고하고 M6 진행 여부를 묻는다.

---

## Self-Review (계획 대 스펙)

- **1(범위)/2(화면 흐름)/3(솔로 방)/7(UI)**: Task 2(솔로 방, RestartSolo), Task 8(타이틀 버튼/모드 카드/봇 설정, 뒤로 흐름, "시작" 문구), Task 7(결과/일시정지/HUD).
- **4(PlayerId)**: Task 1(멀티 회귀로 검증), Task 6(`IsLocalHuman` 반영).
- **5(모드 규칙, 기록)**: Task 5(Bots 종료 규칙, Treasure 수집, Practice), Task 4(배치/기록), Task 7(결과 화면 기록 표시).
- **6(봇)**: Task 3(경로/계획), Task 6(BotBrain, 서로 통과, 막힘 회피, 뽑기).
- **8(테스트)**: Task 3/4 셀프 테스트, Task 9 플레이테스트(봇/깃발/연습/솔로 방 거절).
- **타입 일관성**: `ServerBegin(GameMode, IReadOnlyList<MatchPlayer>, IReadOnlyList<TreasureFlag>)`은 Task 5 정의를 Task 5(부트스트랩)에서만 호출한다. `BotBrain.Init(NetworkPlayer, MazeData, float, BotDifficulty, int)`은 Task 6의 정의와 호출이 같다. `BotSettings.For`/`BotPlanner.Plan` 시그니처는 Task 3 정의와 Task 6/테스트 사용이 같다.
- 알려진 위험: Task 9의 봇 이동 검증(경로 거리 5칸 감소)은 속도 배율 2와 12초 관찰에 의존한다. 봇이 예상보다 느리면 관찰 시간이나 배율을 조정한다.
