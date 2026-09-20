#if UNITY_EDITOR || DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Mirro.Core;
using Mirro.Gameplay;
using Mirro.Maze;
using Mirro.Networking;
using Mirro.Themes;
using Mirro.UI;

namespace Mirro.Testing
{
    /// <summary>
    /// 자동 플레이 테스트(개발 빌드/에디터 전용). 실행 인자에 -mirroPlaytest가 있을 때만 활성화되며, 실제 입력
    /// 이벤트(마우스/키보드)를 주입해 메뉴 → 방 만들기/참가 → 로비 → 게임 → 이탈까지 검증하고 리포트/스크린샷을
    /// 남긴 뒤 종료 코드로 결과를 알린다(테스트 중에는 마우스/키보드를 만지지 않는 게 좋다).
    /// 인자: -mirroRole solo|host|client  -mirroTag &lt;이름&gt;  -mirroOut &lt;폴더&gt;  -mirroSize &lt;50~200&gt;
    ///       -mirroSeason &lt;Spring|Summer|Autumn|Winter&gt;  -mirroPlayers &lt;총 인원&gt;  -mirroIp &lt;방장 주소&gt;
    /// solo = 혼자 방을 만들어 이동/점프/시점/충돌/일시정지를 검증, host/client = 여러 프로세스가 함께 접속해 복제/이탈을 검증.
    /// </summary>
    public class PlaytestDriver : MonoBehaviour
    {
        private const float WatchdogSeconds = 480f;

        private string _outDir;
        private string _tag = "solo";
        private string _role = "solo";
        private string _ip = "127.0.0.1";
        private int _size = 50;
        private int _players = 1;
        private string _season = "Spring";
        private bool _abort;
        private bool _finished;
        private bool _earlyExit;
        private int _firstSeed;
        private int _rejectedPulls;
        private int _failures;
        private readonly List<string> _report = new List<string>();
        private readonly HashSet<string> _errors = new HashSet<string>();
        private readonly HashSet<string> _warnings = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-mirroPlaytest") < 0) return;

            var go = new GameObject("PlaytestDriver");
            DontDestroyOnLoad(go);
            var d = go.AddComponent<PlaytestDriver>();
            d._outDir = ReadArg(args, "-mirroOut") ?? Application.persistentDataPath;
            d._role = ReadArg(args, "-mirroRole") ?? "solo";
            d._tag = ReadArg(args, "-mirroTag") ?? d._role;
            d._ip = ReadArg(args, "-mirroIp") ?? "127.0.0.1";
            d._season = ReadArg(args, "-mirroSeason") ?? "Spring";
            if (int.TryParse(ReadArg(args, "-mirroSize"), out int size)) d._size = size;
            if (int.TryParse(ReadArg(args, "-mirroPlayers"), out int players)) d._players = Mathf.Clamp(players, 1, 4);
            if (d._role.StartsWith("solo")) d._players = 1;
        }

        private static string ReadArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        private void Start()
        {
            Directory.CreateDirectory(_outDir);
            // vSync를 끄고 실제 프레임 비용과 고프레임 동작을 함께 측정하며, 창이 비활성이어도 주입한 입력이 통하게 한다.
            QualitySettings.vSyncCount = 0;
            Application.runInBackground = true;
            NetworkFlow.ListenAddress = "127.0.0.1";
            LanDiscovery.LoopbackOnly = true;
            LanDiscovery.Enabled = _role != "client";
            // 혼자서 시작할 수 있는 건 자동 테스트뿐이다(실제 게임은 2명부터).
            if (_players == 1) NetworkSession.MinPlayersToStart = 1;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.logMessageReceived += OnLog;
            StartCoroutine(Run());
        }

        private void OnDestroy() => Application.logMessageReceived -= OnLog;

        private void Update()
        {
            if (!_finished && Time.realtimeSinceStartup > WatchdogSeconds)
            {
                Check(false, "watchdog: test did not finish within " + WatchdogSeconds + "s");
                Finish();
            }
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            string line = condition.Length > 300 ? condition.Substring(0, 300) : condition;
            if (type == LogType.Log && condition.StartsWith("[Mirro] Rejected flag pull")) _rejectedPulls++;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                _errors.Add($"{type}: {line}");
            else if (type == LogType.Warning)
                _warnings.Add(line);
        }

        // ---- 기록/유틸 ----

        private void Check(bool ok, string what)
        {
            if (!ok) _failures++;
            AddLine((ok ? "PASS  " : "FAIL  ") + what);
        }

        private void Note(string what) => AddLine("NOTE  " + what);

        private void AddLine(string line)
        {
            _report.Add(line);
            Debug.Log($"[Playtest:{_tag}] {line}");
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;

            var sb = new StringBuilder();
            sb.AppendLine($"tag={_tag} role={_role} players={_players} size={_size} season={_season} failures={_failures} errors={_errors.Count} warnings={_warnings.Count}");
            foreach (var line in _report) sb.AppendLine(line);
            foreach (var e in _errors) sb.AppendLine("LOGERR " + e);
            foreach (var w in _warnings) sb.AppendLine("LOGWARN " + w);
            File.WriteAllText(Path.Combine(_outDir, $"report_{_tag}.txt"), sb.ToString());

            Application.Quit(_failures == 0 && _errors.Count == 0 ? 0 : 1);
        }

        private IEnumerator WaitFor(Func<bool> condition, float timeout, string label)
        {
            float t = 0f;
            while (!condition() && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            bool ok = condition();
            if (!ok) _abort = true;
            Check(ok, label + $" (waited {t:F1}s)");
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_outDir, $"{_tag}_{name}.png"), tex.EncodeToPNG());
            Destroy(tex);
        }

        private static Transform MenuNode(string path)
        {
            var ui = FindAnyObjectByType<MainMenuUI>();
            return ui != null ? ui.transform.Find("MenuCanvas/" + path) : null;
        }

        private static bool MenuScreenActive(string screen)
        {
            var node = MenuNode(screen);
            return node != null && node.gameObject.activeSelf;
        }

        private static Vector2 ScreenPos(Transform t) => RectTransformUtility.WorldToScreenPoint(null, t.position);

        private static GameObject TopHit(Vector2 screenPos)
        {
            var eventData = new PointerEventData(EventSystem.current) { position = screenPos };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, hits);
            return hits.Count > 0 ? hits[0].gameObject : null;
        }

        private IEnumerator RealClick(Vector2 p)
        {
            var mouse = Mouse.current;
            if (mouse == null) { Note("no Mouse device available"); yield break; }

            InputSystem.QueueStateEvent(mouse, new MouseState { position = p });
            yield return new WaitForSeconds(0.25f);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = p }.WithButton(MouseButton.Left, true));
            yield return new WaitForSeconds(0.1f);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = p });
            yield return new WaitForSeconds(0.1f);
        }

        private static void ExecuteClick(GameObject target)
        {
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerPress = target };
            ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler);
        }

        /// <summary>실제 마우스 클릭을 주입하고, 효과가 없으면(창이 비활성 등) 직접 클릭 이벤트로 대체한다.</summary>
        private IEnumerator ClickUi(Transform target, string label, Func<bool> effectApplied)
        {
            if (target == null)
            {
                Check(false, label + ": target exists");
                _abort = true;
                yield break;
            }

            Vector2 p = ScreenPos(target);
            var hit = TopHit(p);
            Check(hit == target.gameObject, $"{label}: raycast at center hits it (hit={(hit != null ? hit.name : "nothing")})");

            yield return RealClick(p);
            yield return new WaitForSeconds(0.3f);
            if (effectApplied()) yield break;

            Note($"{label}: injected mouse click had no effect -> falling back to direct event");
            ExecuteClick(target.gameObject);
            yield return new WaitForSeconds(0.3f);
        }

        private IEnumerator Hold(float seconds, params Key[] keys)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) { Note("no Keyboard device available"); yield return new WaitForSeconds(seconds); yield break; }

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            yield return new WaitForSeconds(seconds);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null; yield return null;
        }

        private IEnumerator TapKey(Key key)
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(key));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return new WaitForSeconds(0.3f);
        }

        // ---- 전체 흐름 ----

        private IEnumerator Run()
        {
            yield return WaitFor(() => FindAnyObjectByType<MainMenuUI>() != null, 15f, "main menu loaded");
            if (_abort) { Finish(); yield break; }
            yield return new WaitForSeconds(1f);

            Check(EventSystem.current != null, "EventSystem exists");
            Note($"role={_role} players={_players} resolution {Screen.width}x{Screen.height}, keyboard={(Keyboard.current != null)}, mouse={(Mouse.current != null)}");
            Check(MenuScreenActive("TitleScreen"), "title screen is shown first");
            yield return Shot("01_title");

            if (_role == "intruder")
            {
                yield return IntruderFlow();
                Finish();
                yield break;
            }

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

            if (_role == "client" || _role == "discover") yield return ClientMenuFlow(_role == "discover");
            else yield return HostMenuFlow();
            if (_abort) { Finish(); yield break; }

            yield return WaitFor(() => NetworkPlayer.Local != null, 60f, "game scene loaded and local player spawned");
            if (_abort) { Finish(); yield break; }

            yield return CommonGameChecks();
            if (_abort) { Finish(); yield break; }

            if (_players == 1) yield return SoloChecks();
            else yield return MultiChecks();

            Finish();
        }

        private IEnumerator HostMenuFlow()
        {
            yield return ClickUi(MenuNode("TitleScreen/CreateRoomButton"), "create-room button", () => MenuScreenActive("ThemeScreen"));
            Check(MenuScreenActive("ThemeScreen"), "theme screen shown");
            yield return Shot("02_theme");

            yield return ClickUi(MenuNode("ThemeScreen/Card_" + _season), "theme card " + _season, () => MenuScreenActive("SizeScreen"));
            Check(MenuScreenActive("SizeScreen"), "size screen shown after picking theme");

            var startButton = MenuNode("SizeScreen/StartButton");
            Check(!startButton.GetComponent<Button>().interactable, "create-room disabled before choosing a size");

            var ring = MenuNode($"SizeScreen/Size_{_size}/Ring");
            yield return ClickUi(MenuNode($"SizeScreen/Size_{_size}/Base"), "size button " + _size, () => ring.gameObject.activeSelf);
            Check(ring.gameObject.activeSelf, "selection ring shown");
            Check(startButton.GetComponent<Button>().interactable, "create-room enabled after choosing a size");
            yield return Shot("03_size");

            yield return ClickUi(startButton, "create-room button", () => MenuScreenActive("LobbyScreen"));
            Check(MenuScreenActive("LobbyScreen"), "lobby shown after creating the room");
            Check(NetworkFlow.IsRunning && NetworkSession.Instance != null, "host started and session spawned");
            if (_abort || NetworkSession.Instance == null) { _abort = true; yield break; }

            var session = NetworkSession.Instance;
            Check(session.Slots.Count >= 1 && session.Slots[0].clientId == NetworkManager.ServerClientId, "host owns the first slot");
            Check(session.Theme.englishName == _season && session.MazeSize.Value == _size, "session carries chosen theme and size");
            yield return new WaitForSeconds(0.5f);
            yield return Shot("04_lobby_host_alone");

            if (_players > 1)
            {
                yield return WaitFor(() => session.Slots.Count >= _players, 90f, $"{_players} players joined");
                yield return WaitFor(() => session.CanStart, 60f, "every player is ready");
                if (_abort) yield break;

                var colors = new HashSet<int>();
                for (int i = 0; i < session.Slots.Count; i++) colors.Add(session.Slots[i].colorIndex);
                Check(colors.Count == session.Slots.Count, "every player got a distinct color");
                yield return new WaitForSeconds(0.6f);
                yield return Shot("05_lobby_full");
            }

            yield return ClickUi(MenuNode("LobbyScreen/MainButton"), "start-game button", () => SceneManager.GetActiveScene().name == GameSession.GameScene);
        }

        private static bool RoomRowActive(int index)
        {
            var row = MenuNode("JoinScreen/Room" + index);
            return row != null && row.gameObject.activeSelf;
        }

        private IEnumerator ClientMenuFlow(bool useDiscovery)
        {
            yield return ClickUi(MenuNode("TitleScreen/JoinRoomButton"), "join-room button", () => MenuScreenActive("JoinScreen"));
            Check(MenuScreenActive("JoinScreen"), "join screen shown");

            if (useDiscovery)
            {
                // 실제 브로드캐스트 주소 계산(어댑터의 IP/서브넷 마스크 기반)이 이 PC에서 동작하는지 확인한다.
                bool loopback = LanDiscovery.LoopbackOnly;
                LanDiscovery.LoopbackOnly = false;
                var targets = LanDiscovery.GetBroadcastTargets();
                LanDiscovery.LoopbackOnly = loopback;
                var names = new List<string>();
                foreach (var t in targets) names.Add(t.ToString());
                Note("broadcast targets on this machine: " + string.Join(", ", names));
                Check(targets.Contains(IPAddress.Broadcast) && targets.Count >= 1, "broadcast target list is built");
                Check(LanDiscovery.Instance.IsListening, "join screen listens for LAN rooms");

                yield return WaitFor(() => RoomRowActive(0), 25f, "discovery lists the host's room");
                if (_abort) yield break;

                string theme = "?";
                foreach (var t in ThemeLibrary.All) if (t.englishName == _season) theme = t.displayName;
                var row = MenuNode("JoinScreen/Room0");
                string nameText = row.Find("Name").GetComponent<Text>().text;
                string infoText = row.Find("Info").GetComponent<Text>().text;
                string countText = row.Find("Count").GetComponent<Text>().text;
                Check(nameText.EndsWith("의 방") && nameText.Length > 2, $"room card shows the host's name (\"{nameText}\")");
                Check(infoText == $"{theme} · {_size}×{_size}", $"room card shows theme and size (\"{infoText}\")");
                Check(countText.EndsWith("/4명"), $"room card shows the player count (\"{countText}\")");
                Check(!MenuNode("JoinScreen/EmptyText").gameObject.activeSelf, "empty-state text is hidden while a room is listed");
                yield return Shot("03_join_rooms");

                yield return ClickUi(row, "room card", () => NetworkFlow.Instance.Status != ConnectionStatus.Idle);
            }
            else
            {
                var input = MenuNode("JoinScreen/AddressInput").GetComponent<InputField>();
                input.text = _ip;
                yield return ClickUi(MenuNode("JoinScreen/ConnectButton"), "connect button", () => MenuScreenActive("LobbyScreen"));
            }
            yield return WaitFor(() => MenuScreenActive("LobbyScreen") && NetworkSession.Instance != null, 40f, "joined the host's lobby");
            if (_abort) yield break;

            var session = NetworkSession.Instance;
            Check(session.MazeSize.Value == _size && session.Theme.englishName == _season, "lobby shows the host's theme and size");
            yield return WaitFor(() => session.Slots.Count >= 2, 20f, "client sees itself and the host in the slot list");
            yield return Shot("04_lobby_client");

            var mainButton = MenuNode("LobbyScreen/MainButton");
            var label = mainButton.Find("Label").GetComponent<Text>();
            Check(label.text == "준비", "client's main button offers ready");
            yield return ClickUi(mainButton, "ready button", () => LocalSlotReady(session));
            yield return WaitFor(() => LocalSlotReady(session), 10f, "ready state replicated back to the client");
            yield return WaitFor(() => SceneManager.GetActiveScene().name == GameSession.GameScene, 120f, "host started the game and the client followed");
        }

        /// <summary>이미 시작했거나 가득 찬 방에 뒤늦게 접속을 시도해 거절되는지 확인한다.</summary>
        private IEnumerator IntruderFlow()
        {
            yield return ClickUi(MenuNode("TitleScreen/JoinRoomButton"), "join-room button", () => MenuScreenActive("JoinScreen"));

            // 이미 시작한 방은 더 이상 알리지 않으므로 목록에 나타나면 안 된다(비콘 만료 시간보다 오래 기다린다).
            yield return new WaitForSeconds(4.5f);
            Check(LanDiscovery.Instance.IsListening, "join screen listens for LAN rooms");
            Check(LanDiscovery.Instance.Rooms.Count == 0 && !RoomRowActive(0), "a room whose game already started is not listed");
            Check(MenuNode("JoinScreen/EmptyText").gameObject.activeSelf, "empty-state text shown when no rooms are found");
            yield return Shot("01_join_empty");

            MenuNode("JoinScreen/AddressInput").GetComponent<InputField>().text = _ip;
            yield return ClickUi(MenuNode("JoinScreen/ConnectButton"), "connect button",
                () => NetworkFlow.Instance.Status != ConnectionStatus.Idle);

            yield return WaitFor(() => NetworkFlow.Instance.Status == ConnectionStatus.Failed, 40f, "late join attempt is refused");
            yield return new WaitForSeconds(0.5f);

            string status = MenuNode("JoinScreen/Status").GetComponent<Text>().text;
            Check(!string.IsNullOrEmpty(status), $"refusal reason is shown to the user (\"{status}\")");
            Check(MenuScreenActive("JoinScreen"), "stays on the join screen so the user can retry");
            Check(NetworkSession.Instance == null, "no session leaked on the refused client");
            yield return Shot("02_refused");
        }

        private static bool LocalSlotReady(NetworkSession session)
        {
            return session.TryGetSlot(NetworkManager.Singleton.LocalClientId, out var slot) && slot.ready;
        }

        private IEnumerator CommonGameChecks()
        {
            var local = NetworkPlayer.Local;
            var builder = FindAnyObjectByType<MazeBuilder>();

            for (int i = 0; i < 6; i++)
            {
                var p = local.transform.position;
                Note($"spawn trace +{i * 0.25f:F2}s: local pos=({p.x:F1},{p.y:F2},{p.z:F1}) grounded={local.GetComponent<CharacterController>().isGrounded}");
                yield return new WaitForSeconds(0.25f);
            }

            Check(GameSession.HasSelection && GameSession.MazeSize == _size, $"GameSession carries size {_size}");
            Check(GameSession.Theme != null && GameSession.Theme.englishName == _season, "GameSession carries theme " + _season);
            Check(local.IsOwner && local.controller.enabled && local.playerCamera.enabled, "local player has its controller and camera active");
            Check(Cursor.lockState == CursorLockMode.Locked, "cursor locked in game");
            Check(RenderSettings.fog, "fog enabled");

            var wallChunk = GameObject.Find("WallChunk_0_0");
            Check(wallChunk != null, "wall chunk exists");
            if (wallChunk != null)
            {
                var mat = wallChunk.GetComponent<MeshRenderer>().sharedMaterial;
                Check(mat != null && mat.shader != null && mat.shader.isSupported, $"wall shader works in this build ({(mat != null ? mat.shader.name : "no material")})");
            }

            yield return WaitFor(() => NetworkPlayer.All.Count >= _players, 40f, $"all {_players} player objects are visible on this peer");
            if (_abort) yield break;
            yield return new WaitForSeconds(1.0f);

            foreach (var p in NetworkPlayer.All)
            {
                Vector3 pos = p.transform.position;
                bool floor = Physics.Raycast(new Vector3(pos.x, 5f, pos.z), Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore);
                Note($"player {p.OwnerClientId} local={p.IsOwner} pos=({pos.x:F1},{pos.y:F1},{pos.z:F1}) floorBelow={(floor ? hit.collider.name + "@" + hit.point.y.ToString("F2") : "NONE")}");
            }

            int localCount = 0, remoteCount = 0;
            var colors = new HashSet<int>();
            foreach (var p in NetworkPlayer.All)
            {
                colors.Add(p.ColorIndex.Value);
                if (p.IsOwner) localCount++; else remoteCount++;
            }
            Check(localCount == 1 && remoteCount == _players - 1, $"exactly one local and {_players - 1} remote players (local={localCount}, remote={remoteCount})");
            Check(colors.Count == _players, "players have distinct colors");
            Check(Mathf.Abs(local.transform.position.y) < 0.25f, $"local player rests on the floor (y={local.transform.position.y:F2})");

            foreach (var p in NetworkPlayer.All)
            {
                if (p.IsOwner) continue;
                Check(p.GetComponent<CapsuleCollider>() != null && p.transform.Find("Body") != null && !p.playerCamera.enabled,
                    $"remote player {p.OwnerClientId} has body+collider and no camera");
            }

            // 각 플레이어가 서버가 정한 칸의 중앙에서 열린 방향을 보고 시작했는지(위치와 회전 복제) 확인한다.
            var maze = MazeGameBootstrap.Instance.Maze;
            var spawnCells = SpawnPlacer.Place(maze, _players);
            var sessionForSlots = NetworkSession.Instance;
            foreach (var p in NetworkPlayer.All)
            {
                int idx = SlotIndexOf(sessionForSlots, p.OwnerClientId);
                Vector3 expected = SpawnPlacer.CellCenter(spawnCells[idx], builder.cellSize);
                float expectedYaw = SpawnPlacer.FacingOpenSide(maze, spawnCells[idx]).eulerAngles.y;
                Vector3 actual = p.transform.position;
                float posError = Vector2.Distance(new Vector2(expected.x, expected.z), new Vector2(actual.x, actual.z));
                float yawError = Mathf.Abs(Mathf.DeltaAngle(expectedYaw, p.transform.eulerAngles.y));
                Check(posError < 0.5f && yawError < 10f,
                    $"player {p.OwnerClientId} starts in its assigned cell facing the open side (pos error {posError:F2} m, yaw error {yawError:F1} deg)");
            }

            var positions = new List<Vector3>();
            foreach (var p in NetworkPlayer.All) positions.Add(p.transform.position);
            float minDistance = float.MaxValue;
            for (int i = 0; i < positions.Count; i++)
                for (int j = i + 1; j < positions.Count; j++)
                    minDistance = Mathf.Min(minDistance, Vector3.Distance(positions[i], positions[j]));
            if (positions.Count > 1)
            {
                // 칸 중앙 기준이라 최대 한 칸(대각선이면 1.5칸)까지 오차가 생길 수 있다.
                float requiredStraight = (SpawnPlacer.MinStraightLineFraction * maze.Width - 1.5f) * builder.cellSize;
                Check(minDistance >= requiredStraight, $"start positions are far apart in a straight line (min {minDistance:F0} m, need {requiredStraight:F0} m)");

                int pathApart = SpawnPlacer.MinPairwisePathDistance(maze, spawnCells);
                Check(pathApart > maze.Width, $"start positions are far apart by walking path (closest pair {pathApart} cells, maze side {maze.Width})");
                Note($"spawn spacing: closest pair {pathApart} cells by path (~{pathApart * builder.cellSize:F0} m of walking), {minDistance:F0} m straight line");
            }

            yield return Shot("06_game_spawn");
        }

        // ---- 솔로: 이동/점프/시점/충돌/성능/일시정지 ----

        private IEnumerator SoloChecks()
        {
            var player = NetworkPlayer.Local;
            var builder = FindAnyObjectByType<MazeBuilder>();

            var idle = new List<float>();
            for (float t = 0f; t < 2f; t += Time.unscaledDeltaTime) { idle.Add(Time.unscaledDeltaTime); yield return null; }
            Note("idle frame time " + Stats(idle));

            // 혼자일 땐 깃발이 내 것 하나뿐이라 뽑을 수 없고, 이길 상대가 없으니 경기도 끝나지 않는다.
            yield return WaitFor(() => MatchManager.Instance != null && Flag.All.Count == 1, 10f, "solo: match manager and own flag exist");
            if (_abort) yield break;
            var soloMatch = MatchManager.Instance;
            Check(soloMatch.TotalPlayers.Value == 1 && !soloMatch.Finished.Value, "solo: a one-player match never finishes on its own");
            Check(HudNode("AliveLabel/Text").GetComponent<Text>().text == "생존 1 / 1", "solo: HUD shows 1/1 alive");
            var soloPuller = player.GetComponent<FlagPuller>();
            yield return Hold(MatchManager.PullSeconds + 0.5f, Key.E);
            Check(soloPuller.Candidate == null && soloMatch.Eliminated.Count == 0 && !HudNode("PullPrompt").gameObject.activeSelf,
                "solo: holding E next to your own flag does nothing");

            Vector3 p0 = player.transform.position;
            yield return Hold(1f, Key.W);
            float moved = Vector3.Distance(p0, player.transform.position);
            Check(moved > 1.2f, $"W moves the player forward (moved {moved:F2} m in 1 s)");

            float yaw0 = player.transform.eulerAngles.y;
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = Mouse.current.position.ReadValue(), delta = new Vector2(80f, 0f) });
            yield return null; yield return null;
            float yawDelta = Mathf.Abs(Mathf.DeltaAngle(yaw0, player.transform.eulerAngles.y));
            Check(yawDelta > 2f && yawDelta < 20f, $"mouse delta rotates the view (yaw changed {yawDelta:F1} deg)");

            yield return new WaitForSeconds(0.3f);
            float baseY = player.transform.position.y;
            float maxY = baseY;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.Space));
            for (float t = 0f; t < 0.8f; t += Time.unscaledDeltaTime)
            {
                maxY = Mathf.Max(maxY, player.transform.position.y);
                if (t > 0.05f) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                yield return null;
            }
            Check(maxY - baseY > 0.4f, $"Space jumps (peak +{maxY - baseY:F2} m)");

            var rng = new System.Random(20260919);
            float bound = GameSession.MazeSize * builder.cellSize;
            int wallOverlaps = 0, outOfBounds = 0, samples = 0;
            var walkFrames = new List<float>();
            float walkStart = Time.realtimeSinceStartup;
            Vector3 startPos = player.transform.position;
            float farthest = 0f;

            for (int leg = 0; leg < 24; leg++)
            {
                player.transform.Rotate(0f, rng.Next(-1, 2) * 90f + (float)(rng.NextDouble() - 0.5) * 40f, 0f);
                bool sprint = leg % 2 == 0;
                InputSystem.QueueStateEvent(Keyboard.current, sprint ? new KeyboardState(Key.W, Key.LeftShift) : new KeyboardState(Key.W));
                for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime)
                {
                    walkFrames.Add(Time.unscaledDeltaTime);
                    Vector3 pos = player.transform.position;
                    farthest = Mathf.Max(farthest, Vector3.Distance(startPos, pos));
                    if (pos.x < 0f || pos.z < 0f || pos.x > bound || pos.z > bound || pos.y < -0.5f) outOfBounds++;
                    if (OverlapsWall(pos)) wallOverlaps++;
                    samples++;
                    yield return null;
                }
            }
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return null; yield return null;

            Check(wallOverlaps == 0, $"player never overlaps a wall during random walk ({wallOverlaps}/{samples} samples)");
            Check(outOfBounds == 0, $"player never leaves the maze or falls ({outOfBounds}/{samples} samples)");
            Note($"random walk {Time.realtimeSinceStartup - walkStart:F1}s, farthest {farthest:F1} m from start, frame time {Stats(walkFrames)}");
            yield return Shot("07_game_walk");

            var pause = FindAnyObjectByType<PauseMenu>();
            Check(pause != null, "pause menu exists");
            if (pause == null) yield break;

            yield return TapKey(Key.Escape);
            Check(pause.IsOpen, "Esc opens the pause menu");
            Check(player.controller.IsPaused && Cursor.lockState == CursorLockMode.None, "pause frees the cursor and blocks input");
            yield return Shot("08_pause");

            Vector3 pausedPos = player.transform.position;
            yield return Hold(0.5f, Key.W);
            Check(Vector3.Distance(pausedPos, player.transform.position) < 0.05f, "player cannot move while paused");

            var resume = pause.transform.Find("PauseCanvas/Panel/ResumeButton");
            yield return ClickUi(resume, "resume button", () => !pause.IsOpen);
            Check(!pause.IsOpen && !player.controller.IsPaused && Cursor.lockState == CursorLockMode.Locked, "resume closes the menu and relocks the cursor");

            yield return TapKey(Key.Escape);
            Check(pause.IsOpen, "Esc reopens the pause menu");
            yield return LeaveViaPauseMenu(pause);
            yield return VerifyBackAtMenu(false);
        }

        // ---- 혼자 하기: 봇과 대결 / 깃발 찾기 / 자유 연습 ----

        /// <summary>실제 클릭으로 타이틀 → 혼자 하기 → 모드 카드 → 테마 → 크기 → (봇 설정) → 시작까지 진행한다(대기실 없이 시작).</summary>
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
            Check(MenuNode("SizeScreen/StartButton/Label").GetComponent<Text>().text == "시작", "size screen's confirm button says start in the solo flow");

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

            yield return WaitFor(() => SceneManager.GetActiveScene().name == GameSession.GameScene, 30f, "the solo game starts without a lobby");
            Check(NetworkSession.Instance != null && NetworkSession.Instance.IsSolo.Value, "the room is a local-only solo room");
        }

        private IEnumerator SoloBotsChecks()
        {
            // 출발 지연을 짧게, 속도를 2배로 해서 봇의 움직임을 짧은 시간에 확인한다.
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
            foreach (var b in bots)
                Check(BodyOf(b) != null && BodyOf(b).enabled && MatchManager.IsBotId(b.PlayerId), $"bot {b.PlayerId} has a visible body");
            Check(local.IsLocalHuman && !local.IsBot, "the local player is the only human");
            yield return Shot("06_bots_spawn");

            // 출발 지연 전에는 서 있다.
            var start = new Dictionary<ulong, Vector3>();
            foreach (var b in bots) start[b.PlayerId] = b.transform.position;
            yield return WaitMatchTime(2.5f);
            foreach (var b in bots)
                Check(Vector3.Distance(start[b.PlayerId], b.transform.position) < 0.3f, $"bot {b.PlayerId} waits during its start delay");

            // 지연 뒤에는 가장 가까운 남의 깃발을 향해 걷는다(경로 거리가 줄어든다). 걷는 동안 벽에 겹치지 않는다.
            var fields = new Dictionary<ulong, int[]>();
            foreach (var flag in Flag.All)
                fields[flag.PlayerId] = SpawnPlacer.PathDistances(maze, SpawnPlacer.CellAt(flag.transform.position, cell));
            Func<NetworkPlayer, int> distanceToNearestEnemyFlag = bot =>
            {
                var c = SpawnPlacer.CellAt(bot.transform.position, cell);
                int index = maze.Index(Mathf.Clamp(c.x, 0, maze.Width - 1), Mathf.Clamp(c.y, 0, maze.Height - 1));
                int best = int.MaxValue;
                foreach (var pair in fields)
                    if (pair.Key != bot.PlayerId) best = Mathf.Min(best, pair.Value[index]);
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
            {
                int now = distanceToNearestEnemyFlag(b);
                Check(now <= before[b.PlayerId] - 5, $"bot {b.PlayerId} walked toward the nearest flag ({before[b.PlayerId]} -> {now} cells)");
            }
            Check(wallHits == 0, $"bots never overlap walls while walking ({wallHits}/{samples} samples)");
            yield return Shot("07_bots_walking");

            // 봇 하나를 내 깃발 칸으로 옮기면 사람과 같은 방식으로 서서 뽑고, 사람이 탈락해 패배 결과 화면이 뜬다.
            var attacker = bots[0];
            var myFlag = Flag.FindFor(local.PlayerId);
            attacker.TeleportTo(SpawnPlacer.CellCenter(SpawnPlacer.CellAt(myFlag.transform.position, cell), cell), Quaternion.identity);
            yield return WaitFor(() => match.Finished.Value, 25f, "a bot standing at the player's flag pulls it and ends the game");
            if (_abort) yield break;
            Check(!local.IsAlive.Value && match.WinnerId.Value == MatchManager.NoOne, "the player is eliminated and nobody wins");
            yield return WaitFor(() => ResultScreen.IsShowing, 10f, "the result screen shows after the defeat");
            if (_abort) yield break;
            var panel = GameObject.Find("ResultScreen").transform.Find("ResultCanvas/Panel");
            Check(panel.Find("Title").GetComponent<Text>().text == "패배", "result title says defeat");
            int botRows = 0;
            for (int r = 0; r < 4; r++)
            {
                var row = panel.Find("Row" + r);
                if (row.gameObject.activeSelf && row.Find("Name").GetComponent<Text>().text.EndsWith("(봇)")) botRows++;
            }
            Check(botRows == 3, $"result rows mark the three bots ({botRows})");
            Check(panel.Find("RestartButton") != null && panel.Find("MenuButton") != null, "the solo result screen offers restart and main menu");
            yield return Shot("08_bots_defeat");

            // 다시 하기 → 새 미로에서 처음부터. 이번엔 봇을 멈춰 두고 내가 봇 깃발을 모두 뽑아 이긴다.
            int firstSeed = GameSession.Seed;
            BotBrain.StartDelayOverride = 9999f;
            yield return ClickUi(panel.Find("RestartButton"), "solo restart button", () => !ResultScreen.IsShowing);
            yield return WaitFor(() => NetworkPlayer.Local != null && NetworkPlayer.Local != local && MatchManager.Instance != null && MatchManager.Instance != match
                                       && Flag.All.Count == 4 && NetworkPlayer.All.Count == 4, 40f, "restart reloads the game with fresh players, bots and flags");
            if (_abort) yield break;
            Check(GameSession.Seed != firstSeed && !ResultScreen.IsShowing, "restart uses a new maze and clears the result screen");
            local = NetworkPlayer.Local;
            match = MatchManager.Instance;
            Check(match.Eliminated.Count == 0 && !match.Finished.Value && local.IsAlive.Value, "restart begins with a fresh match");

            var targets = new List<NetworkPlayer>();
            foreach (var p in NetworkPlayer.All) if (p.IsBot) targets.Add(p);
            foreach (var b in targets)
            {
                ulong botId = b.PlayerId;
                yield return PullFlagWithKeyboard(botId, PlayerColors.GetName(b.ColorIndex.Value));
                if (_abort) yield break;
                yield return WaitFor(() => match.IsEliminated(botId), 15f, $"bot {botId} is eliminated");
            }
            yield return WaitFor(() => match.Finished.Value && ResultScreen.IsShowing, 15f, "eliminating every bot finishes the game");
            if (_abort) yield break;
            Check(match.WinnerId.Value == local.PlayerId, "the player wins");
            panel = GameObject.Find("ResultScreen").transform.Find("ResultCanvas/Panel");
            Check(panel.Find("Title").GetComponent<Text>().text == "승리!", "result title says victory");
            yield return Shot("09_bots_victory");

            yield return ClickUi(panel.Find("MenuButton"), "result-screen main-menu button", () => SceneManager.GetActiveScene().name == GameSession.MenuScene);
            yield return VerifyBackAtMenu(false);
            BotBrain.StartDelayOverride = -1f;
            BotBrain.SpeedMultiplier = 1f;
        }

        private IEnumerator SoloHuntChecks()
        {
            PlayerPrefs.DeleteKey("mirro.treasure.best." + _size);
            var local = NetworkPlayer.Local;
            int total = Mathf.Max(1, _size / 10);

            yield return WaitFor(() => MatchManager.Instance != null && Flag.All.Count == total, 30f, $"{total} treasure flags exist");
            if (_abort) yield break;
            var match = MatchManager.Instance;
            Check(match.CurrentMode == GameMode.Treasure && match.TotalTreasures.Value == total && NetworkPlayer.All.Count == 1, "treasure mode: one player and no bots");

            bool allGold = true, allTreasureIds = true;
            foreach (var flag in Flag.All)
            {
                allTreasureIds &= MatchManager.IsTreasureId(flag.PlayerId);
                Color c = flag.transform.Find("ClothPivot/Cloth").GetComponent<Renderer>().sharedMaterial.color;
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
                ulong id = Flag.All[0].PlayerId;
                yield return PullFlagWithKeyboard(id, "금색");
                if (_abort) yield break;
                collected++;
                int target = collected;
                yield return WaitFor(() => match.Collected.Value >= target && counter.text.StartsWith($"깃발 {target} / {total}"), 15f, $"flag {target}/{total} is collected and the HUD updates");
                if (_abort) yield break;
            }
            yield return WaitFor(() => match.Finished.Value && ResultScreen.IsShowing, 15f, "collecting every flag finishes the run");
            if (_abort) yield break;
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
            Check(GameSession.Seed != firstSeed && MatchManager.Instance.Collected.Value == 0 && !MatchManager.Instance.Finished.Value,
                "restart resets the counter on a new maze");
            yield return Shot("08_hunt_restarted");

            yield return LeaveViaPauseMenu(FindAnyObjectByType<PauseMenu>());
            yield return VerifyBackAtMenu(false);
            PlayerPrefs.DeleteKey("mirro.treasure.best." + _size);
        }

        private IEnumerator SoloPracticeChecks()
        {
            yield return WaitFor(() => MatchManager.Instance != null, 30f, "the match manager exists");
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
            var restartLabel = restart != null ? restart.Find("Label").GetComponent<Text>() : null;
            Check(pause.IsOpen && restartLabel != null && restartLabel.text == "다시 시작", "the pause menu offers restart in solo");
            if (restartLabel == null) { _abort = true; yield break; }

            int firstSeed = GameSession.Seed;
            string original = restartLabel.text;
            yield return ClickUi(restart, "pause-menu restart button", () => restartLabel.text != original);
            yield return ClickUi(restart, "pause-menu restart button (confirm)", () => GameSession.Seed != firstSeed || NetworkPlayer.Local != local);
            yield return WaitFor(() => NetworkPlayer.Local != null && NetworkPlayer.Local != local && MatchManager.Instance != null && MatchManager.Instance != match,
                40f, "restart reloads the practice game");
            if (_abort) yield break;
            Check(GameSession.Seed != firstSeed, "restart uses a new maze");

            yield return LeaveViaPauseMenu(FindAnyObjectByType<PauseMenu>());
            yield return VerifyBackAtMenu(false);
        }

        // ---- 멀티: 복제/모으기/이탈 ----

        private IEnumerator MultiChecks()
        {
            var local = NetworkPlayer.Local;
            var session = NetworkSession.Instance;
            int myIndex = SlotIndexOf(session, NetworkManager.Singleton.LocalClientId);
            Note($"my slot index = {myIndex}");

            // 관측 시작 시점의 위치/회전(모든 플레이어의 스폰 지점)을 기록한다.
            var firstPos = new Dictionary<ulong, Vector3>();
            var firstRot = new Dictionary<ulong, Quaternion>();
            var maxDisp = new Dictionary<ulong, float>();
            foreach (var p in NetworkPlayer.All)
            {
                firstPos[p.OwnerClientId] = p.transform.position;
                firstRot[p.OwnerClientId] = p.transform.rotation;
                maxDisp[p.OwnerClientId] = 0f;
            }

            float t0 = Time.time;
            float walkStart = 1f + 3f * myIndex;
            float windowEnd = 1f + 3f * _players + 2.5f;
            bool walked = false;

            while (Time.time - t0 < windowEnd)
            {
                float elapsed = Time.time - t0;
                if (!walked && elapsed >= walkStart)
                {
                    walked = true;
                    StartCoroutine(Hold(2f, Key.W));
                }
                foreach (var p in NetworkPlayer.All)
                {
                    if (!firstPos.ContainsKey(p.OwnerClientId)) continue;
                    float d = Vector3.Distance(firstPos[p.OwnerClientId], p.transform.position);
                    if (d > maxDisp[p.OwnerClientId]) maxDisp[p.OwnerClientId] = d;
                }
                yield return null;
            }

            foreach (var p in NetworkPlayer.All)
            {
                string who = p.IsOwner ? "local" : "remote " + p.OwnerClientId;
                Check(maxDisp[p.OwnerClientId] > 1f, $"{who} player moved and it is visible here (peak displacement {maxDisp[p.OwnerClientId]:F1} m)");
            }
            yield return Shot("07_after_walk");

            // 모으기: 모두 방장의 스폰 칸으로 이동해 서로의 몸체가 보이는지 확인한다.
            // 기대 위치/회전은 관측값이 아니라 미로에서 결정적으로 계산해, 피어마다 목표 지점이 어긋나지 않게 한다.
            var maze = MazeGameBootstrap.Instance.Maze;
            var spawnCells = SpawnPlacer.Place(maze, _players);
            Vector3 hostSpawn = SpawnPlacer.CellCenter(spawnCells[0], MazeGameBootstrap.Instance.CellSize);
            Quaternion hostRot = SpawnPlacer.FacingOpenSide(maze, spawnCells[0]);
            float[] offsetsX = { 0f, -1.2f, 1.2f, 0f };
            float[] offsetsZ = { 0f, 1.4f, 1.4f, 1.4f };
            Vector3 targetPos = hostSpawn + hostRot * new Vector3(offsetsX[myIndex], 0f, offsetsZ[myIndex]);
            Quaternion targetRot = myIndex == 0 ? hostRot : hostRot * Quaternion.Euler(0f, 180f, 0f);

            yield return new WaitForSeconds(0.5f * myIndex);
            local.TeleportTo(targetPos, targetRot);
            yield return new WaitForSeconds(3.5f);

            Vector3 mine = local.transform.position;
            Note($"gather: idx={myIndex} target=({targetPos.x:F2},{targetPos.z:F2}) actual=({mine.x:F2},{mine.z:F2}) " +
                 $"facingYaw={hostRot.eulerAngles.y:F0} cell00 walls N={maze.HasWall(0, 0, WallSide.North)} E={maze.HasWall(0, 0, WallSide.East)}");

            foreach (var p in NetworkPlayer.All)
            {
                if (p.IsOwner) continue;
                int idx = SlotIndexOf(session, p.OwnerClientId);
                Vector3 expected = hostSpawn + hostRot * new Vector3(offsetsX[idx], 0f, offsetsZ[idx]);
                float error = Vector3.Distance(expected, p.transform.position);
                Check(error < 1.0f, $"remote {p.OwnerClientId} arrived at its gather spot (error {error:F2} m)");
            }
            yield return Shot("08_gather");

            // 깃발 뽑기 → 탈락 → 관전 → 승리 → 결과 화면.
            yield return MatchChecks(session, myIndex);
            if (_abort || _earlyExit) yield break;

            // 다시 하기 → 대기실 → 새 판 → (도중 중단 또는 다시 하기) → 대기실 → 방장이 나가면 참가자는 타이틀로.
            yield return RematchFlow(session, myIndex);
        }

        // ---- 멀티: 재시작(다시 하기 / 로비로 돌아가기) ----

        private static bool IsLobbyShown()
        {
            return SceneManager.GetActiveScene().name == GameSession.MenuScene && FindAnyObjectByType<MainMenuUI>() != null && MenuScreenActive("LobbyScreen");
        }

        private static bool ReadyStatesReset(NetworkSession session)
        {
            for (int i = 0; i < session.Slots.Count; i++)
                if (session.Slots[i].ready != (session.Slots[i].clientId == NetworkManager.ServerClientId)) return false;
            return true;
        }

        private IEnumerator RematchFlow(NetworkSession session, int myIndex)
        {
            bool isHost = NetworkManager.Singleton.IsServer;
            // 4인 판에서는 한 명이 경기 도중에 나갔다. 나머지가 다시 하기로 새 판을 한다.
            int remaining = _players - (_players >= 4 ? 1 : 0);

            // ---- 결과 화면의 버튼 구성 ----
            var resultRoot = GameObject.Find("ResultScreen");
            var panel = resultRoot != null ? resultRoot.transform.Find("ResultCanvas/Panel") : null;
            if (panel == null) { Check(false, "result screen is still shown"); _abort = true; yield break; }
            var restart = panel.Find("RestartButton");
            var menuButton = panel.Find("MenuButton");
            Check(menuButton != null && TopHit(ScreenPos(menuButton)) == menuButton.gameObject, "result screen's main-menu button can be clicked");
            if (isHost)
                Check(restart != null && TopHit(ScreenPos(restart)) == restart.gameObject, "host's result screen offers a clickable restart button");
            else
                Check(restart == null && panel.Find("Hint") != null, "other players see a waiting hint instead of a restart button");

            // ---- 다시 하기 ----
            if (isHost)
            {
                yield return WaitFor(() => session.Slots.Count == remaining, 40f, "everyone who stays is still in the room");
                yield return new WaitForSeconds(1f);
                yield return ClickUi(restart, "result-screen restart button", () => SceneManager.GetActiveScene().name == GameSession.MenuScene);
            }
            yield return WaitFor(IsLobbyShown, 30f, "everyone is back in the room's lobby after the restart");
            if (_abort) yield break;
            yield return VerifyLobbyAfterRestart(session, remaining, "after_result");

            // ---- 새 판 ----
            yield return StartNextMatch(session, isHost);
            if (_abort) yield break;
            yield return SecondMatchChecks(session, myIndex, remaining);
            if (_abort) yield break;

            // ---- 마무리: 방장이 대기실에서 나가면 남은 참가자는 안내 문구와 함께 타이틀로 돌아간다 ----
            if (isHost)
            {
                yield return new WaitForSeconds(2f);
                yield return ClickUi(MenuNode("LobbyScreen/LeaveButton"), "lobby leave button", () => MenuScreenActive("TitleScreen"));
                yield return WaitFor(() => !NetworkFlow.IsRunning && NetworkSession.Instance == null, 15f, "host's room is closed");
                yield return VerifyBackAtMenu(true);
            }
            else
            {
                yield return WaitFor(() => MenuScreenActive("TitleScreen"), 60f, "client returns to the title screen after the host closed the room");
                yield return new WaitForSeconds(0.8f);
                var message = MenuNode("TitleScreen/Message");
                string text = message != null ? message.GetComponent<Text>().text : string.Empty;
                Check(text.Length > 0, $"title screen explains the disconnect (\"{text}\")");
                yield return VerifyBackAtMenu(true);
            }
        }

        private IEnumerator VerifyLobbyAfterRestart(NetworkSession session, int remaining, string shotName)
        {
            yield return WaitFor(() => session.CurrentPhase == SessionPhase.Lobby, 8f, "room is back in the lobby phase");
            Check(NetworkSession.Instance == session && NetworkFlow.IsRunning, "the same room and connection are kept");
            Check(session.Slots.Count == remaining, $"the lobby lists the {remaining} remaining players ({session.Slots.Count})");
            Check(NetworkPlayer.All.Count == 0 && Flag.All.Count == 0 && MatchManager.Instance == null, "players, flags and the match were cleaned up");
            yield return WaitFor(() => ReadyStatesReset(session), 8f, "ready states are reset (only the host starts ready)");
            Check(!GameSession.HasSelection && Cursor.lockState == CursorLockMode.None, "game selection cleared and cursor free in the lobby");
            Check(session.Theme.englishName == _season && session.MazeSize.Value == _size, "the room keeps its theme and size");
            yield return new WaitForSeconds(0.4f);
            yield return Shot("13_lobby_" + shotName);
        }

        private IEnumerator StartNextMatch(NetworkSession session, bool isHost)
        {
            var mainButton = MenuNode("LobbyScreen/MainButton");
            if (isHost)
            {
                yield return WaitFor(() => session.CanStart, 40f, "every remaining player readied up again");
                if (_abort) yield break;
                yield return ClickUi(mainButton, "start-game button", () => SceneManager.GetActiveScene().name == GameSession.GameScene);
            }
            else
            {
                yield return ClickUi(mainButton, "ready button", () => LocalSlotReady(session));
                yield return WaitFor(() => LocalSlotReady(session), 10f, "ready state replicated back after the restart");
            }
            yield return WaitFor(() => SceneManager.GetActiveScene().name == GameSession.GameScene, 90f, "the new game started");
            yield return WaitFor(() => NetworkPlayer.Local != null, 60f, "the new game spawned the local player");
        }

        private IEnumerator SecondMatchChecks(NetworkSession session, int myIndex, int remaining)
        {
            var local = NetworkPlayer.Local;
            bool isHost = NetworkManager.Singleton.IsServer;
            var maze = MazeGameBootstrap.Instance.Maze;
            float cell = MazeGameBootstrap.Instance.CellSize;

            yield return WaitFor(() => MatchManager.Instance != null && Flag.All.Count == remaining && NetworkPlayer.All.Count == remaining
                                       && MatchManager.Instance.StartTime.Value > 0.0, 40f, $"new match with {remaining} players and flags is visible");
            if (_abort) yield break;
            var match = MatchManager.Instance;

            var idOf = new ulong[remaining];
            for (int i = 0; i < remaining; i++) idOf[i] = session.Slots[i].clientId;

            Check(GameSession.Seed != _firstSeed, $"the new game uses a new maze (seed {GameSession.Seed} vs {_firstSeed})");
            Check(match.TotalPlayers.Value == remaining && match.AliveCount == remaining && match.Eliminated.Count == 0 && !match.Finished.Value,
                $"the new match starts fresh ({match.AliveCount}/{match.TotalPlayers.Value} alive, {match.Eliminated.Count} eliminated)");
            Check(local.IsAlive.Value && local.controller.enabled && local.GetComponent<SpectatorView>() == null && !ResultScreen.IsShowing,
                "the local player starts alive with controls and no leftover result or spectator state");
            Check(HudNode("AliveLabel/Text").GetComponent<Text>().text == $"생존 {remaining} / {remaining}", "the new HUD shows everyone alive");

            var spawnCells = SpawnPlacer.Place(maze, remaining);
            yield return new WaitForSeconds(1f);
            foreach (var p in NetworkPlayer.All)
            {
                int idx = SlotIndexOf(session, p.OwnerClientId);
                Vector3 expected = SpawnPlacer.CellCenter(spawnCells[idx], cell);
                float error = Vector2.Distance(new Vector2(expected.x, expected.z), new Vector2(p.transform.position.x, p.transform.position.z));
                Check(error < 0.5f, $"player {p.OwnerClientId} starts at its spot in the new maze (error {error:F2} m)");
            }
            yield return Shot("14_second_game");

            // 새 판에서도 깃발 뽑기가 그대로 동작한다.
            yield return WaitMatchTime(8f);
            ulong victimId = idOf[1];
            string victimColor = PlayerColors.GetName(session.Slots[1].colorIndex);
            if (isHost)
            {
                yield return PullFlagWithKeyboard(victimId, victimColor);
                if (_abort) yield break;
            }
            yield return WaitFor(() => match.Eliminated.Count >= 1, 25f, $"{victimColor} is eliminated in the new match");
            if (_abort) yield break;
            Check(match.Eliminated[0].clientId == victimId && match.Eliminated[0].eliminatedBy == idOf[0], "the new match credits the flag puller");
            Check(match.AliveCount == remaining - 1, $"survivor count drops to {remaining - 1} in the new match ({match.AliveCount})");

            if (remaining == 2)
            {
                // 2명이면 바로 끝나고, 결과 화면의 [다시 하기]로 한 번 더 대기실로 돌아간다.
                yield return WaitFor(() => match.Finished.Value && ResultScreen.IsShowing, 15f, "the new match ends and shows the result screen");
                if (_abort) yield break;
                Check(match.WinnerId.Value == idOf[0], "the new match's winner is the last survivor");
                yield return new WaitForSeconds(1f);
                if (isHost)
                {
                    var restart = GameObject.Find("ResultScreen").transform.Find("ResultCanvas/Panel/RestartButton");
                    yield return ClickUi(restart, "result-screen restart button (second time)", () => SceneManager.GetActiveScene().name == GameSession.MenuScene);
                }
                yield return WaitFor(IsLobbyShown, 30f, "everyone is back in the lobby after the second restart");
                if (_abort) yield break;
                yield return VerifyLobbyAfterRestart(session, remaining, "after_second_result");
            }
            else
            {
                // 아직 끝나지 않은 판을 방장이 일시정지 메뉴에서 중단하고 모두 대기실로 돌아간다(관전 중인 사람 포함).
                Check(!match.Finished.Value, "the match is still running with more than one survivor");
                yield return WaitMatchTime(17f);
                if (!isHost)
                {
                    yield return TapKey(Key.Escape);
                    var clientPause = FindAnyObjectByType<PauseMenu>();
                    Check(clientPause != null && clientPause.IsOpen && clientPause.transform.Find("PauseCanvas/Panel/RestartButton") == null,
                        "only the host's pause menu offers returning to the lobby");
                    yield return TapKey(Key.Escape);
                }

                yield return WaitMatchTime(21f);
                if (isHost)
                {
                    yield return TapKey(Key.Escape);
                    var pause = FindAnyObjectByType<PauseMenu>();
                    var lobbyButton = pause != null ? pause.transform.Find("PauseCanvas/Panel/RestartButton") : null;
                    Check(lobbyButton != null, "host's pause menu offers returning to the lobby");
                    if (lobbyButton == null) { _abort = true; yield break; }

                    var label = lobbyButton.Find("Label").GetComponent<Text>();
                    string original = label.text;
                    yield return ClickUi(lobbyButton, "pause-menu lobby button", () => label.text != original);
                    yield return new WaitForSeconds(0.6f);
                    Check(SceneManager.GetActiveScene().name == GameSession.GameScene && label.text != original,
                        $"the first click only asks for confirmation (\"{label.text}\")");
                    yield return ClickUi(lobbyButton, "pause-menu lobby button (confirm)", () => SceneManager.GetActiveScene().name == GameSession.MenuScene);
                }
                yield return WaitFor(IsLobbyShown, 30f, "everyone is back in the lobby after the host aborted the match");
                if (_abort) yield break;
                yield return VerifyLobbyAfterRestart(session, remaining, "after_abort");
            }
        }

        // ---- 멀티: 관전 조작 ----

        private static readonly Key[] DigitKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 };

        /// <summary>방장이 탈락 직후에 시선을 위로 올려, 관전자가 그 시선(상하)을 그대로 보는지 확인할 수 있게 한다.</summary>
        private IEnumerator HostLooksUp()
        {
            yield return new WaitForSeconds(0.5f);
            if (Mouse.current == null) yield break;
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = Mouse.current.position.ReadValue(), delta = new Vector2(0f, 200f) });
            yield return null; yield return null;
            Note($"host looked up: pitch = {NetworkPlayer.Local.controller.Pitch:F1} deg");
        }

        private IEnumerator SpectatorControlChecks(NetworkPlayer local, SpectatorView view, ulong[] idOf, int[] colorOf, int myIndex)
        {
            var host = NetworkPlayer.Find(idOf[0]);
            var cam = local.playerCamera.transform;

            // 색 번호로 특정 플레이어의 시점을 고른다.
            yield return TapKey(DigitKeys[colorOf[0]]);
            Check(view.Mode == SpectatorMode.Follow && view.Target == host, $"pressing {colorOf[0] + 1} follows the {PlayerColors.GetName(colorOf[0])} player");

            // 그 플레이어의 위/아래 시선까지 그대로 보인다.
            yield return WaitFor(() => Mathf.Abs(host.LookPitch.Value) > 10f, 8f, "the followed player's up/down look direction is replicated");
            yield return new WaitForSeconds(0.7f);
            float camPitch = Mathf.DeltaAngle(0f, cam.eulerAngles.x);
            Check(Mathf.Abs(camPitch - host.LookPitch.Value) < 4f,
                $"spectator camera looks up/down like the followed player (camera {camPitch:F1} deg, player {host.LookPitch.Value:F1} deg)");

            // 내 색 번호는 무시하고, 다른 생존자의 번호와 클릭으로 시점을 바꾼다.
            var before = view.Target;
            yield return TapKey(DigitKeys[colorOf[myIndex]]);
            Check(view.Target == before, "pressing your own color does nothing");

            var other = NetworkPlayer.Find(idOf[2]);
            yield return TapKey(DigitKeys[colorOf[2]]);
            Check(view.Target == other && other != null, $"pressing {colorOf[2] + 1} switches to the {PlayerColors.GetName(colorOf[2])} player");

            var previous = view.Target;
            yield return RealClick(Mouse.current.position.ReadValue());
            yield return new WaitForSeconds(0.3f);
            Check(view.Target != null && view.Target != previous, "clicking switches to another surviving player");

            // Tab: 자유 시점으로 나가 상하좌우로 둘러보고 날아다닌다.
            yield return TapKey(Key.Tab);
            Check(view.Mode == SpectatorMode.Free && view.Target == null, "Tab detaches to the free camera");
            var hostBody = BodyOf(host);
            Check(hostBody != null && hostBody.enabled, "players are fully visible again from the free camera");
            var title = HudNode("SpectateBanner/Title").GetComponent<Text>().text;
            Check(title.Contains("자유 시점"), $"banner announces the free camera (\"{title}\")");

            Vector3 euler0 = cam.eulerAngles;
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = Mouse.current.position.ReadValue(), delta = new Vector2(300f, 200f) });
            yield return null; yield return null;
            float yawChange = Mathf.Abs(Mathf.DeltaAngle(euler0.y, cam.eulerAngles.y));
            float pitchChange = Mathf.Abs(Mathf.DeltaAngle(euler0.x, cam.eulerAngles.x));
            Check(yawChange > 20f && pitchChange > 10f, $"the mouse looks left/right and up/down (yaw {yawChange:F0} deg, pitch {pitchChange:F0} deg)");

            Vector3 p0 = cam.position;
            Vector3 forward = cam.forward;
            yield return Hold(0.5f, Key.W);
            Vector3 moved = cam.position - p0;
            Check(moved.magnitude > 2.5f && Vector3.Dot(moved.normalized, forward) > 0.85f, $"W flies where the camera looks ({moved.magnitude:F1} m)");

            float y0 = cam.position.y;
            yield return Hold(0.4f, Key.Space);
            Check(cam.position.y - y0 > 2f, $"Space flies up ({cam.position.y - y0:F1} m)");
            float y1 = cam.position.y;
            yield return Hold(0.4f, Key.LeftCtrl);
            Check(y1 - cam.position.y > 2f, $"Ctrl flies down ({y1 - cam.position.y:F1} m)");

            Vector3 p1 = cam.position;
            yield return Hold(0.5f, Key.W, Key.LeftShift);
            Check((cam.position - p1).magnitude > 8f, $"Shift flies faster ({(cam.position - p1).magnitude:F1} m in 0.5 s)");
            yield return Shot("11_spectate_free");

            // 아래를 내려다보며 높이 올라가 미로 전체를 위에서 본다(화면 확인용).
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = Mouse.current.position.ReadValue(), delta = new Vector2(0f, -700f) });
            yield return null; yield return null;
            yield return Hold(1.5f, Key.Space);
            yield return new WaitForSeconds(0.2f);
            yield return Shot("11_spectate_topdown");

            yield return TapKey(Key.Tab);
            Check(view.Mode == SpectatorMode.Follow && view.Target != null, "Tab attaches to a player again");
        }

        // ---- 멀티: 깃발/탈락/관전/승리/결과 ----

        private static Transform HudNode(string path)
        {
            var hud = GameObject.Find("MatchHud");
            return hud != null ? hud.transform.Find("MatchCanvas/" + path) : null;
        }

        private static bool AnyToastContains(string fragment)
        {
            var hud = GameObject.Find("MatchHud");
            var canvas = hud != null ? hud.transform.Find("MatchCanvas") : null;
            if (canvas == null) return false;
            foreach (Transform child in canvas)
                if (child.name == "Toast" && child.Find("Text").GetComponent<Text>().text.Contains(fragment)) return true;
            return false;
        }

        private static Renderer BodyOf(NetworkPlayer player)
        {
            var body = player != null ? player.transform.Find("Body") : null;
            return body != null ? body.GetComponent<Renderer>() : null;
        }

        /// <summary>서버 시계 기준 경기 시작 후 seconds가 될 때까지 기다린다(모든 피어가 같은 시점에 각 단계를 시작하게 한다).</summary>
        private IEnumerator WaitMatchTime(float seconds)
        {
            while (MatchManager.Instance != null && !MatchManager.Instance.Finished.Value && MatchManager.Instance.Elapsed < seconds)
                yield return null;
        }

        /// <summary>깃발 곁(같은 칸 안, 칸 중심 쪽 1m)으로 이동해 서 있을 자리.</summary>
        private static Vector3 PositionNextTo(Flag flag, float cell)
        {
            Vector3 f = flag.transform.position;
            Vector3 center = SpawnPlacer.CellCenter(SpawnPlacer.CellAt(f, cell), cell);
            Vector3 toCenter = new Vector3(center.x - f.x, 0f, center.z - f.z).normalized;
            return new Vector3(f.x, 0.1f, f.z) + toCenter * 1f;
        }

        /// <summary>깃발과 직선으로는 가깝지만 벽 너머(다른 칸)인 자리를 찾는다. 없으면 false.</summary>
        private static bool TryFindPositionBehindWall(Flag flag, MazeData maze, float cell, out Vector3 position)
        {
            Vector3 f = flag.transform.position;
            Vector2Int c = SpawnPlacer.CellAt(f, cell);
            foreach (var side in new[] { WallSide.North, WallSide.East, WallSide.South, WallSide.West })
            {
                MazeData.GetOffset(side, out int dx, out int dy);
                if (!maze.HasWall(c.x, c.y, side) || !maze.InBounds(c.x + dx, c.y + dy)) continue;

                var p = new Vector3(f.x, 0.1f, f.z);
                if (dx != 0) p.x = dx > 0 ? (c.x + 1) * cell + 0.6f : c.x * cell - 0.6f;
                if (dy != 0) p.z = dy > 0 ? (c.y + 1) * cell + 0.6f : c.y * cell - 0.6f;

                float distance = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(f.x, f.z));
                if (distance >= MatchManager.PullRange) continue;
                position = p;
                return true;
            }
            position = default;
            return false;
        }

        private static Quaternion LookAtFlat(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            d.y = 0f;
            return d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d) : Quaternion.identity;
        }

        /// <summary>깃발 앞으로 가서 실제 E 키를 길게 눌러 뽑기 시작한다(진행 막대/안내가 뜨는지 확인하고 도중에 돌아온다).</summary>
        private IEnumerator PullFlagWithKeyboard(ulong victimId, string victimColor)
        {
            var local = NetworkPlayer.Local;
            var flag = Flag.FindFor(victimId);
            if (flag == null) { Check(false, $"{victimColor} flag exists to pull"); _abort = true; yield break; }

            float cell = MazeGameBootstrap.Instance.CellSize;
            Vector3 stand = PositionNextTo(flag, cell);
            local.TeleportTo(stand, LookAtFlat(stand, flag.transform.position));
            yield return new WaitForSeconds(1f);

            var puller = local.GetComponent<FlagPuller>();
            var prompt = HudNode("PullPrompt");
            Check(puller.Candidate == flag, $"standing next to the {victimColor} flag makes it the pull candidate");
            Check(prompt.gameObject.activeSelf && prompt.Find("Text").GetComponent<Text>().text.Contains(victimColor),
                $"pull prompt names the {victimColor} flag (\"{prompt.Find("Text").GetComponent<Text>().text}\")");

            StartCoroutine(Hold(MatchManager.PullSeconds + 0.8f, Key.E));
            yield return new WaitForSeconds(0.8f);
            Check(puller.Progress > 0.25f && puller.Progress < 0.95f, $"progress fills while E is held ({puller.Progress:F2})");
            var fill = HudNode("PullPrompt/Bar/Fill").GetComponent<RectTransform>();
            Check(fill.gameObject.activeSelf && fill.anchorMax.x > 0.2f, $"progress bar shows the progress (fill {fill.anchorMax.x:F2})");
            yield return Shot("10_pulling");
        }

        private IEnumerator MatchChecks(NetworkSession session, int myIndex)
        {
            var local = NetworkPlayer.Local;
            var maze = MazeGameBootstrap.Instance.Maze;
            float cell = MazeGameBootstrap.Instance.CellSize;
            var spawnCells = SpawnPlacer.Place(maze, _players);
            bool isHost = NetworkManager.Singleton.IsServer;

            yield return WaitFor(() => MatchManager.Instance != null && Flag.All.Count == _players && MatchManager.Instance.StartTime.Value > 0.0,
                30f, $"match manager and {_players} flags are visible on this peer");
            if (_abort) yield break;
            var match = MatchManager.Instance;
            _firstSeed = GameSession.Seed;

            var idOf = new ulong[_players];
            var colorOf = new int[_players];
            for (int i = 0; i < _players; i++)
            {
                idOf[i] = session.Slots[i].clientId;
                colorOf[i] = session.Slots[i].colorIndex;
            }

            // ---- A. 깃발 배치 ----
            yield return WaitMatchTime(28f);
            Check(match.TotalPlayers.Value == _players && match.AliveCount == _players && !match.Finished.Value,
                $"match starts with everyone alive ({match.AliveCount}/{match.TotalPlayers.Value})");
            for (int i = 0; i < _players; i++)
            {
                var flag = Flag.FindFor(idOf[i]);
                var owner = NetworkPlayer.Find(idOf[i]);
                string label = PlayerColors.GetName(colorOf[i]);
                Check(flag != null && owner != null, $"{label} has a flag and a player object");
                if (flag == null || owner == null) continue;

                Check(flag.ColorIndex == colorOf[i], $"{label} flag has its owner's color");
                Check(SpawnPlacer.CellAt(flag.transform.position, cell) == spawnCells[i], $"{label} flag stands in its owner's start cell");
                float beside = Vector2.Distance(new Vector2(flag.transform.position.x, flag.transform.position.z),
                    new Vector2(owner.SpawnPosition.x, owner.SpawnPosition.z));
                Check(beside > 0.8f && beside < 2.2f, $"{label} flag stands beside the start point, not on it ({beside:F2} m)");
                Check(flag.transform.Find("Pole") != null && flag.transform.Find("ClothPivot/Cloth") != null, $"{label} flag has its pole and cloth");
            }

            var aliveText = HudNode("AliveLabel/Text").GetComponent<Text>();
            Check(aliveText.text == $"생존 {_players} / {_players}", $"HUD shows the survivor count (\"{aliveText.text}\")");
            if (myIndex == 0)
                Check(!HudNode("PullPrompt").gameObject.activeSelf, "no pull prompt is shown next to your own flag");
            yield return Shot("09_flags");

            // ---- B. 서버가 잘못된 뽑기를 거부하는지 ----
            yield return WaitMatchTime(31f);
            if (myIndex == 1)
            {
                // 자기 시작 지점(상대 깃발에서 아주 먼 곳)에서 방장의 깃발을 뽑겠다고 서버에 직접 요청한다.
                local.TeleportTo(local.SpawnPosition, Quaternion.Euler(0f, local.SpawnYaw, 0f));
                yield return new WaitForSeconds(1f);
                local.PullFlagRpc(idOf[0]);
                Note("cheat attempt: pulled the host's flag from far away");
            }
            else if (myIndex == 0)
            {
                // 내 깃발은 뽑을 수 없다.
                local.PullFlagRpc(idOf[0]);
                Note("cheat attempt: pulled my own flag");
                yield return new WaitForSeconds(1f);

                // 깃발과 직선으로는 가깝지만 벽 너머인 자리에서 E를 길게 누르고 서버에도 직접 요청한다.
                bool wallTested = false;
                for (int i = 1; i < _players && !wallTested; i++)
                {
                    var flag = Flag.FindFor(idOf[i]);
                    if (flag == null || !TryFindPositionBehindWall(flag, maze, cell, out var behind)) continue;

                    wallTested = true;
                    local.TeleportTo(behind, LookAtFlat(behind, flag.transform.position));
                    yield return new WaitForSeconds(1f);
                    var puller = local.GetComponent<FlagPuller>();
                    yield return Hold(MatchManager.PullSeconds + 0.5f, Key.E);
                    Check(puller.Candidate == null && puller.Progress == 0f,
                        $"a flag right behind a wall cannot be pulled (candidate={(puller.Candidate != null ? "yes" : "none")})");
                    local.PullFlagRpc(idOf[i]);
                    Note($"cheat attempt: pulled {PlayerColors.GetName(colorOf[i])}'s flag through a wall");
                    yield return Shot("10_behind_wall");
                }
                if (!wallTested) Note("no flag had a wall right next to it; through-wall check skipped");
            }

            yield return WaitMatchTime(37f);
            Check(match.Eliminated.Count == 0 && match.AliveCount == _players && !match.Finished.Value,
                $"rejected pulls eliminated nobody ({match.AliveCount}/{match.TotalPlayers.Value} alive)");
            if (isHost)
                Check(_rejectedPulls >= 1, $"server logged the rejected pull attempts ({_rejectedPulls})");

            // ---- C~E. 탈락이 이어지고 마지막 한 명이 남으면 끝난다 ----
            var steps = new List<(int victim, bool disconnect)>();
            if (_players == 2) steps.Add((1, false));
            else if (_players == 3) { steps.Add((1, false)); steps.Add((2, false)); }
            else { steps.Add((1, false)); steps.Add((3, true)); steps.Add((2, false)); }
            float[] stepTimes = { 40f, 62f, 74f };

            for (int k = 0; k < steps.Count; k++)
            {
                yield return WaitMatchTime(stepTimes[k]);

                int victim = steps[k].victim;
                bool disconnect = steps[k].disconnect;
                ulong victimId = idOf[victim];
                string victimColor = PlayerColors.GetName(colorOf[victim]);
                bool iAmVictim = myIndex == victim;
                int aliveAfter = _players - (k + 1);
                bool last = k == steps.Count - 1;

                if (disconnect)
                {
                    if (iAmVictim)
                    {
                        // 경기 도중에 나가면 그 자리에서 탈락 처리된다.
                        yield return LeaveViaPauseMenu(FindAnyObjectByType<PauseMenu>());
                        yield return VerifyBackAtMenu(false);
                        _earlyExit = true;
                        yield break;
                    }
                }
                else if (myIndex == 0)
                {
                    yield return PullFlagWithKeyboard(victimId, victimColor);
                    if (_abort) yield break;
                }

                yield return WaitFor(() => match.Eliminated.Count >= k + 1, 25f, $"{victimColor} is eliminated on this peer");
                if (_abort) yield break;

                var entry = match.Eliminated[k];
                Check(entry.clientId == victimId && entry.colorIndex == colorOf[victim], $"elimination record #{k + 1} names {victimColor}");
                Check(entry.eliminatedBy == (disconnect ? MatchManager.NoOne : idOf[0]),
                    disconnect ? "a player who leaves mid-game is eliminated with no one credited" : $"elimination credited to the player who pulled the flag");
                Check(match.AliveCount == aliveAfter, $"survivor count is {aliveAfter} after the elimination (got {match.AliveCount})");
                if (k == 0 && !last && myIndex == 0) StartCoroutine(HostLooksUp());
                yield return WaitFor(() => Flag.FindFor(victimId) == null, 6f, $"{victimColor} flag is removed on this peer");
                yield return WaitFor(() => aliveText.text == $"생존 {aliveAfter} / {_players}", 4f, "HUD survivor count updates");
                yield return WaitFor(() => AnyToastContains(victimColor), 5f, $"elimination notice for {victimColor} appears");

                if (!disconnect && !iAmVictim)
                {
                    var victimPlayer = NetworkPlayer.Find(victimId);
                    yield return WaitFor(() => victimPlayer != null && !victimPlayer.IsAlive.Value, 5f, $"{victimColor} player is marked eliminated");
                    var body = BodyOf(victimPlayer);
                    var capsule = victimPlayer != null ? victimPlayer.GetComponent<CapsuleCollider>() : null;
                    Check(body != null && !body.enabled && capsule != null && !capsule.enabled, $"eliminated {victimColor}'s body and collider are gone for other players");
                }

                if (!disconnect && iAmVictim)
                {
                    yield return WaitFor(() => local.GetComponent<SpectatorView>() != null, 5f, "eliminated player enters spectator mode");
                    var view = local.GetComponent<SpectatorView>();
                    Check(!local.controller.enabled && !local.IsAlive.Value, "eliminated player's controls are disabled");
                    yield return new WaitForSeconds(1f);

                    Check(view.Target != null && view.Target.IsAlive.Value && !view.Target.IsOwner, "spectator follows a surviving player");
                    if (view.Target != null)
                    {
                        float off = Vector3.Distance(local.playerCamera.transform.position, view.Target.transform.position + Vector3.up * 1.6f);
                        Check(off < 0.7f, $"spectator camera sits at the followed player's eyes (off by {off:F2} m)");
                        var targetBody = BodyOf(view.Target);
                        Check(targetBody != null && !targetBody.enabled, "followed player's body is hidden from the spectator's own view");
                    }

                    // 마지막 탈락으로 경기가 끝나면 관전 안내 대신 결과 화면이 이어받으므로 배너는 (의도대로) 바로 사라진다.
                    var banner = HudNode("SpectateBanner");
                    if (!last)
                        Check(banner.gameObject.activeSelf && banner.Find("Title").GetComponent<Text>().text.Contains("탈락"),
                            $"spectator banner is shown (\"{banner.Find("Title").GetComponent<Text>().text}\")");
                    else
                        Check(!banner.gameObject.activeSelf && match.Finished.Value, "spectator banner gives way to the result screen when the last elimination ends the match");

                    Vector3 before = local.transform.position;
                    yield return Hold(0.6f, Key.W);
                    Check(Vector3.Distance(before, local.transform.position) < 0.05f, "eliminated player cannot walk");
                    yield return Shot("11_spectating");

                    // 탈락 직후 첫 판정에서만 관전 조작(플레이어 시점 선택/자유 시점)을 자세히 확인한다.
                    if (k == 0 && !last) yield return SpectatorControlChecks(local, view, idOf, colorOf, myIndex);
                }
            }

            // ---- 종료: 승자와 결과 화면 ----
            yield return WaitFor(() => match.Finished.Value, 15f, "match is finished on this peer");
            if (_abort) yield break;
            Check(match.WinnerId.Value == idOf[0] && match.WinnerColor.Value == colorOf[0], "the last surviving player is the winner");
            yield return WaitFor(() => ResultScreen.IsShowing, 12f, "result screen appears on this peer");
            if (_abort) yield break;
            yield return new WaitForSeconds(0.5f);

            var resultRoot = GameObject.Find("ResultScreen");
            var panel = resultRoot != null ? resultRoot.transform.Find("ResultCanvas/Panel") : null;
            Check(panel != null, "result screen has its panel");
            if (panel == null) { _abort = true; yield break; }

            string title = panel.Find("Title").GetComponent<Text>().text;
            Check(title == (myIndex == 0 ? "승리!" : "게임 종료"), $"result title matches this player's outcome (\"{title}\")");

            // 순위: 우승자, 그다음은 나중에 탈락한 사람부터.
            var order = new List<int> { 0 };
            for (int k = steps.Count - 1; k >= 0; k--) order.Add(steps[k].victim);
            for (int r = 0; r < 4; r++)
            {
                var row = panel.Find("Row" + r);
                if (r >= order.Count)
                {
                    Check(!row.gameObject.activeSelf, $"row {r + 1} is hidden when there are only {order.Count} players");
                    continue;
                }

                int idx = order[r];
                string name = row.Find("Name").GetComponent<Text>().text;
                string note = row.Find("Note").GetComponent<Text>().text;
                string expectedName = PlayerColors.GetName(colorOf[idx]) + (idx == myIndex ? " (나)" : string.Empty);
                Check(row.Find("Rank").GetComponent<Text>().text == (r + 1) + "위" && name == expectedName, $"result row {r + 1} is {expectedName} (\"{name}\")");

                bool disconnected = steps.Exists(s => s.victim == idx && s.disconnect);
                string expectedNote = r == 0 ? "최후의 생존자" : disconnected ? "연결이 끊겨 탈락" : PlayerColors.GetName(colorOf[0]) + "에게 깃발을 뽑힘";
                Check(note == expectedNote, $"result row {r + 1} note is \"{expectedNote}\" (\"{note}\")");
            }
            Check(panel.Find("Subtitle").GetComponent<Text>().text.Contains("경기 시간"), "result screen shows the match time");

            Check(local.controller.IsPaused && Cursor.lockState == CursorLockMode.None, "result screen frees the cursor and blocks input");
            yield return TapKey(Key.Escape);
            var pause = FindAnyObjectByType<PauseMenu>();
            Check(pause != null && !pause.IsOpen, "Esc does not open the pause menu over the result screen");
            Check(!HudNode("SpectateBanner").gameObject.activeSelf, "spectator banner is hidden once the match is over");
            yield return Shot("12_result");
        }

        private IEnumerator LeaveViaPauseMenu(PauseMenu pause)
        {
            if (pause == null) { Check(false, "pause menu available to leave"); yield break; }

            if (!pause.IsOpen) yield return TapKey(Key.Escape);
            var toMenu = pause.transform.Find("PauseCanvas/Panel/MenuButton");
            yield return ClickUi(toMenu, "main-menu button", () => SceneManager.GetActiveScene().name == GameSession.MenuScene);
        }

        private IEnumerator VerifyBackAtMenu(bool alreadyChecked)
        {
            if (!alreadyChecked)
                yield return WaitFor(() => SceneManager.GetActiveScene().name == GameSession.MenuScene && FindAnyObjectByType<MainMenuUI>() != null, 30f, "returned to main menu");
            yield return new WaitForSeconds(0.8f);
            Check(!GameSession.HasSelection, "session cleared after returning to the menu");
            Check(!NetworkFlow.IsRunning && NetworkSession.Instance == null, "network shut down and session destroyed");
            Check(Cursor.lockState == CursorLockMode.None, "cursor free in the menu");
            Check(MenuScreenActive("TitleScreen"), "title screen is shown");
            yield return Shot("09_back_at_menu");
        }

        private static int SlotIndexOf(NetworkSession session, ulong clientId)
        {
            for (int i = 0; i < session.Slots.Count; i++)
                if (session.Slots[i].clientId == clientId) return i;
            return 0;
        }

        private static bool OverlapsWall(Vector3 pos)
        {
            var hits = Physics.OverlapCapsule(pos + Vector3.up * 0.4f, pos + Vector3.up * 1.4f, 0.33f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
                if (h.name.StartsWith("WallChunk")) return true;
            return false;
        }

        private static string Stats(List<float> frames)
        {
            if (frames.Count == 0) return "n/a";
            var sorted = new List<float>(frames);
            sorted.Sort();
            float sum = 0f;
            foreach (var f in sorted) sum += f;
            float avg = sum / sorted.Count * 1000f;
            float p95 = sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * 0.95f))] * 1000f;
            float max = sorted[sorted.Count - 1] * 1000f;
            return $"avg {avg:F1} ms ({1000f / avg:F0} fps), p95 {p95:F1} ms, max {max:F1} ms over {sorted.Count} frames";
        }
    }
}
#endif
