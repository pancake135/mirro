using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Mirro.Core;
using Mirro.Gameplay;
using Mirro.Items;
using Mirro.Networking;
using Mirro.Themes;

namespace Mirro.UI
{
    /// <summary>
    /// 게임 중 화면 표시: 남은 인원, 탈락 알림, 깃발 뽑기 안내/진행 막대, 탈락 후 관전 안내.
    /// 경기가 끝나면 결과 화면을 띄운다. 상태는 모두 MatchManager/NetworkPlayer의 복제 값에서 읽는다.
    /// </summary>
    public class MatchHud : MonoBehaviour
    {
        private const float ToastSeconds = 4.5f;
        private const int MaxToasts = 4;
        // 마지막 탈락 알림을 잠깐 보여준 뒤 결과 화면으로 넘어간다.
        private const float ResultDelaySeconds = 1.6f;
        private const float RadarSize = 380f;

        private class Toast
        {
            public RectTransform rect;
            public Text text;
            public float expiresAt;
        }

        private NetworkPlayer _local;
        private MazeThemeConfig _theme;
        private FlagPuller _puller;
        private MatchManager _match;
        private int _seenEliminations;
        private float _finishedSeenAt = -1f;
        private bool _resultShown;

        private RectTransform _canvasRt;
        private Text _alive;
        private RectTransform _aliveRect;
        private RectTransform _prompt;
        private Text _promptText;
        private RectTransform _barFill;
        private RectTransform _spectate;
        private Text _spectateTitle;
        private Text _spectateSub;
        private ItemUser _itemUser;
        private RectTransform _itemBadge;
        private Image _itemBadgeBg;
        private Text _itemBadgeText;
        private RectTransform _itemPrompt;
        private Text _itemPromptText;
        private RectTransform _godsHandMenu;
        private readonly RectTransform[] _godsRows = new RectTransform[4];
        private readonly Text[] _godsRowText = new Text[4];
        private RectTransform _radar;
        private readonly RectTransform[] _radarDots = new RectTransform[4];
        private Texture2D _radarTexture;
        private readonly List<Toast> _toasts = new List<Toast>();

        public void Init(NetworkPlayer local, MazeThemeConfig theme)
        {
            _local = local;
            _theme = theme;
            _puller = local.GetComponent<FlagPuller>();
            _itemUser = local.GetComponent<ItemUser>();
            Build();
        }

        private void OnDestroy()
        {
            if (_radarTexture != null) Destroy(_radarTexture);
        }

        private void Update()
        {
            if (_local == null) return;

            if (_match == null)
            {
                _match = MatchManager.Instance;
                if (_match == null) return;
            }

            UpdateAlive();
            UpdateToasts();
            UpdatePrompt();
            UpdateItems();
            UpdateSpectateBanner();
            UpdateResult();
        }

        // ---- 갱신 ----

        private static string FormatTime(double seconds)
        {
            var span = System.TimeSpan.FromSeconds(seconds);
            return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
        }

        /// <summary>상단 알약: 대결은 남은 인원, 깃발 찾기는 모은 깃발과 시간, 자유 연습은 경과 시간.</summary>
        private void UpdateAlive()
        {
            bool versus = _match.CurrentMode == GameMode.Versus || _match.CurrentMode == GameMode.Bots;
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
            _aliveRect.sizeDelta = new Vector2(versus ? 340f : 560f, 72f);
        }

        private void UpdateToasts()
        {
            // 새로 쌓인 탈락 기록마다 알림을 하나씩 띄운다.
            while (_seenEliminations < _match.Eliminated.Count)
                AddToast(DescribeElimination(_match.Eliminated[_seenEliminations++]));

            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < _toasts[i].expiresAt) continue;
                Destroy(_toasts[i].rect.gameObject);
                _toasts.RemoveAt(i);
            }
            for (int i = 0; i < _toasts.Count; i++)
                _toasts[i].rect.anchoredPosition = new Vector2(0f, 400f - 74f * i);
        }

        private void UpdatePrompt()
        {
            var candidate = _puller != null ? _puller.Candidate : null;
            _prompt.gameObject.SetActive(candidate != null);
            if (candidate == null) return;

            _promptText.text = $"E 키를 길게 눌러 {candidate.DisplayName} 깃발 뽑기";
            float progress = _puller.Progress;
            _barFill.gameObject.SetActive(progress > 0.02f);
            _barFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        }

        /// <summary>소지 아이템 배지, "E: 줍기" 안내, 신의 손 대상 목록, 레이더 미니맵.</summary>
        private void UpdateItems()
        {
            bool alive = _local.IsAlive.Value && !_match.Finished.Value;
            var held = _local.Held;

            _itemBadge.gameObject.SetActive(alive && held != ItemType.None);
            if (_itemBadge.gameObject.activeSelf)
            {
                Color c = ItemInfo.ColorOf(held);
                _itemBadgeBg.color = new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 0.88f);
                _itemBadgeText.text = ItemInfo.DisplayName(held) + "    F: 사용";
            }

            var pickup = alive && _local.controller != null ? _local.controller.LookTarget as ItemPickup : null;
            bool showPrompt = pickup != null && !_local.controller.IsPaused;
            _itemPrompt.gameObject.SetActive(showPrompt);
            if (showPrompt) _itemPromptText.text = "E 키로 줍기: " + pickup.DisplayName;

            UpdateGodsHandMenu(alive);
            UpdateRadar(alive);
        }

        private void UpdateGodsHandMenu(bool alive)
        {
            bool open = alive && _itemUser != null && _itemUser.TargetMenuOpen;
            _godsHandMenu.gameObject.SetActive(open);
            if (!open) return;

            for (int i = 0; i < _godsRows.Length; i++)
            {
                var target = ItemUser.FindByColor(i);
                _godsRows[i].gameObject.SetActive(target != null);
                if (target == null) continue;

                string suffix = target == _local ? " (나)" : target.IsBot ? " (봇)" : string.Empty;
                _godsRowText[i].text = $"{i + 1}    {PlayerColors.GetName(i)}{suffix}";
            }
        }

        private void UpdateRadar(bool alive)
        {
            var bootstrap = MazeGameBootstrap.Instance;
            bool active = alive && _local.RadarActive && _radarTexture != null && bootstrap != null && bootstrap.Maze != null;
            _radar.gameObject.SetActive(active);
            if (!active) return;

            float width = bootstrap.Maze.Width * bootstrap.CellSize;
            float height = bootstrap.Maze.Height * bootstrap.CellSize;
            for (int i = 0; i < _radarDots.Length; i++)
            {
                var target = ItemUser.FindByColor(i);
                _radarDots[i].gameObject.SetActive(target != null);
                if (target == null) continue;

                Vector3 p = target.transform.position;
                float nx = Mathf.Clamp01(p.x / width);
                float ny = Mathf.Clamp01(p.z / height);
                _radarDots[i].anchoredPosition = new Vector2((nx - 0.5f) * RadarSize, (ny - 0.5f) * RadarSize);
                _radarDots[i].localScale = Vector3.one * (target == _local ? 1.4f : 1f);
            }
        }

        private void UpdateSpectateBanner()
        {
            bool eliminated = !_local.IsAlive.Value;
            _spectate.gameObject.SetActive(eliminated && !_match.Finished.Value);
            if (!eliminated) return;

            var view = _local.GetComponent<SpectatorView>();
            if (view != null && view.Mode == SpectatorMode.Free)
            {
                _spectateTitle.text = "탈락했어요 · 자유 시점";
                _spectateSub.text = "마우스 시선 · WASD 이동 · Space/Ctrl 위·아래 · Shift 빠르게 · 1~4 플레이어 시점 · Tab 고정";
            }
            else
            {
                string watching = view != null && view.Target != null ? PlayerColors.GetName(view.Target.ColorIndex.Value) + " 시점" : "관전 중";
                _spectateTitle.text = "탈락했어요 · " + watching;
                _spectateSub.text = "좌·우클릭: 다음·이전 플레이어 · 1~4: 색으로 선택 · Tab: 자유 시점";
            }
        }

        private void UpdateResult()
        {
            if (_resultShown || !_match.Finished.Value) return;

            if (_finishedSeenAt < 0f) _finishedSeenAt = Time.unscaledTime;
            // 마지막 탈락 기록까지 복제된 뒤에 순위를 만든다(변수와 목록이 서로 다른 메시지로 올 수 있다).
            int expectedEliminations;
            switch (_match.CurrentMode)
            {
                case GameMode.Treasure:
                case GameMode.Practice:
                    expectedEliminations = 0;
                    break;
                case GameMode.Bots:
                    // 내가 이기면 봇 전원이, 내가 지면(우승자 없음) 내가 탈락 기록에 있어야 한다.
                    expectedEliminations = _match.WinnerId.Value != MatchManager.NoOne ? _match.TotalPlayers.Value - 1 : 1;
                    break;
                default:
                    expectedEliminations = _match.TotalPlayers.Value - 1;
                    break;
            }
            bool complete = _match.Eliminated.Count >= expectedEliminations;
            if (!complete || Time.unscaledTime - _finishedSeenAt < ResultDelaySeconds) return;

            _resultShown = true;
            var go = new GameObject("ResultScreen");
            go.AddComponent<ResultScreen>().Show(_local, _match, _theme);
        }

        // ---- 알림 문구 ----

        private static string DescribeElimination(MatchEntry entry)
        {
            string victim = PlayerColors.GetName(entry.colorIndex);
            if (entry.eliminatedBy == MatchManager.NoOne)
                return $"{victim} 플레이어의 연결이 끊겨 탈락했어요";

            var killer = NetworkPlayer.Find(entry.eliminatedBy);
            string by = killer != null ? PlayerColors.GetName(killer.ColorIndex.Value) : "누군가";
            return $"{Subject(by)} {victim}의 깃발을 뽑았어요!  {victim} 탈락";
        }

        /// <summary>받침이 있으면 "이", 없으면 "가"를 붙인다.</summary>
        private static string Subject(string word)
        {
            if (string.IsNullOrEmpty(word)) return word;
            char last = word[word.Length - 1];
            bool hasFinalConsonant = last >= 0xAC00 && last <= 0xD7A3 && (last - 0xAC00) % 28 != 0;
            return word + (hasFinalConsonant ? "이" : "가");
        }

        private void AddToast(string message)
        {
            if (_toasts.Count >= MaxToasts)
            {
                Destroy(_toasts[0].rect.gameObject);
                _toasts.RemoveAt(0);
            }

            var rt = UIFactory.NewRect("Toast", _canvasRt);
            UIFactory.SetBox(rt, Vector2.zero, new Vector2(1000f, 64f));
            UIFactory.AddRounded(rt, new Color(0f, 0f, 0f, 0.6f), 30f);
            var text = UIFactory.AddLabel(rt, "Text", message, 34, Color.white, FontStyle.Bold);
            _toasts.Add(new Toast { rect = rt, text = text, expiresAt = Time.unscaledTime + ToastSeconds });
        }

        // ---- 화면 구성 ----

        private void Build()
        {
            var canvasGo = new GameObject("MatchCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasRt = (RectTransform)canvasGo.transform;

            Color accent = _theme != null ? _theme.accentColor : new Color(0.62f, 0.79f, 0.93f);

            _aliveRect = UIFactory.NewRect("AliveLabel", _canvasRt);
            UIFactory.SetBox(_aliveRect, new Vector2(0f, 480f), new Vector2(340f, 72f));
            UIFactory.AddRounded(_aliveRect, new Color(0f, 0f, 0f, 0.5f), 34f);
            _alive = UIFactory.AddLabel(_aliveRect, "Text", "생존", 40, Color.white, FontStyle.Bold);

            _prompt = UIFactory.NewRect("PullPrompt", _canvasRt);
            UIFactory.SetBox(_prompt, new Vector2(0f, -300f), new Vector2(900f, 150f));
            UIFactory.AddRounded(_prompt, new Color(0f, 0f, 0f, 0.55f), 40f);
            _promptText = UIFactory.AddLabel(_prompt, "Text", string.Empty, 42, Color.white, FontStyle.Bold);
            UIFactory.SetBox(_promptText.rectTransform, new Vector2(0f, 26f), new Vector2(860f, 64f));

            var bar = UIFactory.NewRect("Bar", _prompt);
            UIFactory.SetBox(bar, new Vector2(0f, -36f), new Vector2(640f, 26f));
            UIFactory.AddRounded(bar, new Color(1f, 1f, 1f, 0.22f), 13f);
            _barFill = UIFactory.NewRect("Fill", bar);
            _barFill.anchorMin = Vector2.zero;
            _barFill.anchorMax = new Vector2(0f, 1f);
            _barFill.offsetMin = Vector2.zero;
            _barFill.offsetMax = Vector2.zero;
            UIFactory.AddRounded(_barFill, accent, 13f);
            _prompt.gameObject.SetActive(false);

            _spectate = UIFactory.NewRect("SpectateBanner", _canvasRt);
            UIFactory.SetBox(_spectate, new Vector2(0f, -400f), new Vector2(1500f, 150f));
            UIFactory.AddRounded(_spectate, new Color(0f, 0f, 0f, 0.6f), 44f);
            _spectateTitle = UIFactory.AddLabel(_spectate, "Title", string.Empty, 52, Color.white, FontStyle.Bold);
            UIFactory.SetBox(_spectateTitle.rectTransform, new Vector2(0f, 24f), new Vector2(1460f, 70f));
            _spectateSub = UIFactory.AddLabel(_spectate, "Sub", string.Empty, 30, new Color(0.78f, 0.82f, 0.85f));
            UIFactory.SetBox(_spectateSub.rectTransform, new Vector2(0f, -36f), new Vector2(1460f, 50f));
            _spectate.gameObject.SetActive(false);

            BuildItemUi();
        }

        private void BuildItemUi()
        {
            _itemBadge = UIFactory.NewRect("ItemBadge", _canvasRt);
            UIFactory.SetBox(_itemBadge, new Vector2(700f, -450f), new Vector2(420f, 100f));
            _itemBadgeBg = UIFactory.AddRounded(_itemBadge, new Color(0f, 0f, 0f, 0.6f), 40f);
            _itemBadgeText = UIFactory.AddLabel(_itemBadge, "Text", string.Empty, 40, Color.white, FontStyle.Bold);
            _itemBadge.gameObject.SetActive(false);

            _itemPrompt = UIFactory.NewRect("ItemPrompt", _canvasRt);
            UIFactory.SetBox(_itemPrompt, new Vector2(0f, -240f), new Vector2(720f, 90f));
            UIFactory.AddRounded(_itemPrompt, new Color(0f, 0f, 0f, 0.55f), 36f);
            _itemPromptText = UIFactory.AddLabel(_itemPrompt, "Text", string.Empty, 40, Color.white, FontStyle.Bold);
            _itemPrompt.gameObject.SetActive(false);

            _godsHandMenu = UIFactory.NewRect("GodsHandMenu", _canvasRt);
            UIFactory.SetBox(_godsHandMenu, Vector2.zero, new Vector2(760f, 640f));
            UIFactory.AddRounded(_godsHandMenu, new Color(0.10f, 0.08f, 0.16f, 0.9f), 48f);
            var menuTitle = UIFactory.AddLabel(_godsHandMenu, "Title", "신의 손 — 시작 지점으로 보낼 사람의 번호", 38, ItemInfo.ColorOf(ItemType.GodsHand), FontStyle.Bold);
            UIFactory.SetBox(menuTitle.rectTransform, new Vector2(0f, 268f), new Vector2(720f, 70f));
            var menuHint = UIFactory.AddLabel(_godsHandMenu, "Hint", "F: 닫기", 30, new Color(0.78f, 0.82f, 0.85f));
            UIFactory.SetBox(menuHint.rectTransform, new Vector2(0f, -272f), new Vector2(720f, 50f));
            for (int i = 0; i < _godsRows.Length; i++)
            {
                var row = UIFactory.NewRect("Row" + i, _godsHandMenu);
                UIFactory.SetBox(row, new Vector2(0f, 160f - 108f * i), new Vector2(660f, 92f));
                UIFactory.AddRounded(row, new Color(0.18f, 0.15f, 0.26f), 36f);
                var swatch = UIFactory.NewRect("Swatch", row);
                UIFactory.SetBox(swatch, new Vector2(-280f, 0f), new Vector2(52f, 52f));
                UIFactory.AddCircle(swatch, PlayerColors.Get(i));
                var label = UIFactory.AddLabel(row, "Text", string.Empty, 44, Color.white, FontStyle.Bold);
                label.alignment = TextAnchor.MiddleLeft;
                UIFactory.SetBox(label.rectTransform, new Vector2(30f, 0f), new Vector2(520f, 70f));
                _godsRows[i] = row;
                _godsRowText[i] = label;
            }
            _godsHandMenu.gameObject.SetActive(false);

            _radar = UIFactory.NewRect("RadarMap", _canvasRt);
            UIFactory.SetBox(_radar, new Vector2(740f, 300f), new Vector2(RadarSize + 24f, RadarSize + 24f));
            UIFactory.AddRounded(_radar, new Color(0.30f, 0.90f, 0.55f, 0.9f), 20f);
            var mapRt = UIFactory.NewRect("Map", _radar);
            UIFactory.SetBox(mapRt, Vector2.zero, new Vector2(RadarSize, RadarSize));
            var bootstrap = MazeGameBootstrap.Instance;
            if (bootstrap != null && bootstrap.Maze != null)
            {
                _radarTexture = RadarMap.Bake(bootstrap.Maze);
                var image = mapRt.gameObject.AddComponent<RawImage>();
                image.texture = _radarTexture;
                image.raycastTarget = false;
            }
            for (int i = 0; i < _radarDots.Length; i++)
            {
                var dot = UIFactory.NewRect("Dot" + i, mapRt);
                UIFactory.SetBox(dot, Vector2.zero, new Vector2(24f, 24f));
                UIFactory.AddCircle(dot, PlayerColors.Get(i));
                _radarDots[i] = dot;
            }
            _radar.gameObject.SetActive(false);
        }
    }
}
