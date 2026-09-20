using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Mirro.Core;
using Mirro.Gameplay;
using Mirro.Networking;
using Mirro.Themes;

namespace Mirro.UI
{
    /// <summary>
    /// 메인 메뉴. 화면 흐름:
    /// 타이틀 → [방 만들기] 테마(세로 둥근 카드) → 크기(밝은 회색 둥근 정사각형) → 로비(방장)
    ///        → [참가하기] IP 입력 → 로비(참가자)
    /// 모든 UI를 코드로 생성한다(프리팹/이미지 에셋 불필요).
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        public static readonly Color NeutralBackground = new Color(0.09f, 0.11f, 0.13f);
        public static readonly Color MutedText = new Color(0.72f, 0.77f, 0.80f);
        public static readonly Color WarningText = new Color(1.00f, 0.72f, 0.42f);
        public static readonly Color DefaultAccent = new Color(0.62f, 0.79f, 0.93f);

        private static readonly Color SizeButtonColor = new Color(0.88f, 0.89f, 0.91f);
        private static readonly Color SizeButtonText = new Color(0.16f, 0.18f, 0.20f);
        private static readonly Color DisabledColor = new Color(0.30f, 0.32f, 0.34f);
        private static readonly Color SecondaryButton = new Color(0.24f, 0.27f, 0.29f);

        private class SizeButton
        {
            public int size;
            public GameObject ring;
        }

        public Canvas Canvas { get; private set; }
        public RectTransform CanvasRect { get; private set; }

        private Image _background;
        private RectTransform _titleScreen;
        private RectTransform _themeScreen;
        private RectTransform _sizeScreen;
        private JoinScreen _join;
        private LobbyScreen _lobby;
        private SoloModeScreen _soloScreen;
        private BotOptionsScreen _botOptions;
        private RectTransform _activeScreen;

        // 혼자 하기 흐름에서 고른 모드(null이면 멀티 방 만들기 흐름).
        private GameMode? _soloMode;

        private Text _titleMessage;
        private Text _sizeSubtitle;
        private Text _sizeError;
        private UIFactory.ButtonParts _createButton;
        private readonly List<SizeButton> _sizeButtons = new List<SizeButton>();

        private MazeThemeConfig _theme;
        private int _selectedSize;
        private bool _built;

        private void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            UIFactory.EnsureEventSystem();
            Build();

            // 게임이 끝나거나 방장이 중단해서 같은 방의 대기실로 돌아온 경우에는 방을 유지한 채 대기실을 보여준다.
            if (NetworkFlow.IsRunning && NetworkSession.Instance != null)
            {
                ShowLobbyScreen();
            }
            else
            {
                ShowTitleScreen(NetworkFlow.Instance.PendingMessage);
            }
            NetworkFlow.Instance.PendingMessage = null;
        }

        private void Update()
        {
            if (!_built) return;

            if (_activeScreen == _join.Root) _join.Tick();
            else if (_activeScreen == _lobby.Root) _lobby.Tick();
        }

        public void Build()
        {
            if (_built) return;
            _built = true;

            var canvasGo = new GameObject("MenuCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas = canvasGo.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            CanvasRect = (RectTransform)canvasGo.transform;

            var bgRt = UIFactory.NewRect("Background", CanvasRect);
            UIFactory.Stretch(bgRt);
            _background = UIFactory.AddSolid(bgRt, NeutralBackground);

            _titleScreen = NewScreen("TitleScreen");
            _themeScreen = NewScreen("ThemeScreen");
            _sizeScreen = NewScreen("SizeScreen");

            BuildTitleScreen();
            BuildThemeScreen();
            BuildSizeScreen();

            _join = new JoinScreen(this);
            _lobby = new LobbyScreen(this);
            _soloScreen = new SoloModeScreen(this, PickSoloMode, () => ShowTitleScreen());
            _botOptions = new BotOptionsScreen(this, StartSolo, () => ShowSizeScreen(_theme));
        }

        public RectTransform NewScreen(string name)
        {
            var rt = UIFactory.NewRect(name, CanvasRect);
            UIFactory.Stretch(rt);
            rt.gameObject.SetActive(false);
            return rt;
        }

        public void SetBackground(Color color) => _background.color = color;

        public void SetBackgroundTint(Color accent) => _background.color = Color.Lerp(NeutralBackground, accent, 0.16f);

        private void Activate(RectTransform screen)
        {
            foreach (var s in new[] { _titleScreen, _themeScreen, _sizeScreen, _join.Root, _lobby.Root, _soloScreen.Root, _botOptions.Root })
                s.gameObject.SetActive(s == screen);
            _activeScreen = screen;

            // LAN 방 검색은 참가 화면이 열려 있는 동안만 수신한다.
            if (screen == _join.Root) LanDiscovery.Instance.StartListening();
            else LanDiscovery.StopListeningIfExists();
        }

        private void OnDestroy() => LanDiscovery.StopListeningIfExists();

        // ---- 화면 전환 ----

        public void ShowTitleScreen(string message = null)
        {
            _theme = null;
            _selectedSize = 0;
            _soloMode = null;
            SetBackground(NeutralBackground);
            _titleMessage.text = message ?? string.Empty;
            Activate(_titleScreen);
        }

        public void ShowThemeScreen()
        {
            _theme = null;
            _selectedSize = 0;
            SetBackground(NeutralBackground);
            Activate(_themeScreen);
        }

        public void ShowSizeScreen(MazeThemeConfig theme)
        {
            _theme = theme;
            _selectedSize = 0;

            SetBackgroundTint(theme.accentColor);
            _sizeSubtitle.text = "선택한 테마: " + theme.displayName;
            _sizeSubtitle.color = theme.accentColor;
            _sizeError.text = string.Empty;
            _createButton.label.text = _soloMode != null ? "시작" : "방 만들기";

            RefreshSizeSelection();
            Activate(_sizeScreen);
        }

        // ---- 혼자 하기 ----

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
            ShowThemeScreen();
        }

        private void ShowBotOptionsScreen()
        {
            _botOptions.Show(_theme.accentColor);
            SetBackgroundTint(_theme.accentColor);
            Activate(_botOptions.Root);
        }

        /// <summary>방을 내 PC 안에만 열고 대기실 없이 바로 시작한다.</summary>
        private void StartSolo()
        {
            if (_soloMode == null || _theme == null || _selectedSize == 0) return;

            if (!NetworkFlow.Instance.HostRoom(_theme, _selectedSize, NetworkFlow.DefaultPort, solo: true))
            {
                string reason = NetworkFlow.Instance.FailureReason ?? "게임을 시작하지 못했어요.";
                if (_soloMode == GameMode.Bots) _botOptions.SetError(reason);
                else _sizeError.text = reason;
                return;
            }

            var session = NetworkSession.Instance;
            session.ConfigureSolo(_soloMode.Value, _botOptions.BotCount, _botOptions.Difficulty);
            session.StartSoloGame();
        }

        public void ShowJoinScreen()
        {
            SetBackground(NeutralBackground);
            _join.Show();
            Activate(_join.Root);
        }

        public void ShowLobbyScreen()
        {
            _lobby.Show();
            Activate(_lobby.Root);
        }

        public void SelectSize(int size)
        {
            _selectedSize = size;
            RefreshSizeSelection();
        }

        public void LeaveRoomToTitle(string message = null)
        {
            NetworkFlow.Instance.Leave(false);
            ShowTitleScreen(message);
        }

        private void CreateRoom()
        {
            if (_theme == null || _selectedSize == 0) return;

            if (_soloMode != null)
            {
                if (_soloMode == GameMode.Bots) ShowBotOptionsScreen();
                else StartSolo();
                return;
            }

            if (!NetworkFlow.Instance.HostRoom(_theme, _selectedSize))
            {
                _sizeError.text = NetworkFlow.Instance.FailureReason ?? "방을 만들지 못했어요.";
                return;
            }
            ShowLobbyScreen();
        }

        // ---- 타이틀 ----

        private void BuildTitleScreen()
        {
            var title = UIFactory.AddLabel(_titleScreen, "Title", "미로 서바이벌", 120, Color.white, FontStyle.Bold);
            UIFactory.SetBox(title.rectTransform, new Vector2(0f, 250f), new Vector2(1500f, 180f));

            var subtitle = UIFactory.AddLabel(_titleScreen, "Subtitle", "최대 4명 · 상대의 깃발을 뽑아 탈락시키세요", 40, MutedText);
            UIFactory.SetBox(subtitle.rectTransform, new Vector2(0f, 130f), new Vector2(1500f, 70f));

            UIFactory.AddTextButton(_titleScreen, "CreateRoomButton", "방 만들기", new Vector2(0f, -20f), new Vector2(620f, 130f),
                DefaultAccent, UIFactory.ContrastText(DefaultAccent), 60, ShowThemeScreen, 56f);
            UIFactory.AddTextButton(_titleScreen, "JoinRoomButton", "참가하기", new Vector2(0f, -170f), new Vector2(620f, 130f),
                SizeButtonColor, SizeButtonText, 60, ShowJoinScreen, 56f);
            UIFactory.AddTextButton(_titleScreen, "SoloButton", "혼자 하기", new Vector2(0f, -320f), new Vector2(620f, 130f),
                SecondaryButton, Color.white, 60, ShowSoloModeScreen, 56f);

            _titleMessage = UIFactory.AddLabel(_titleScreen, "Message", string.Empty, 38, WarningText);
            UIFactory.SetBox(_titleMessage.rectTransform, new Vector2(0f, -450f), new Vector2(1500f, 70f));
        }

        // ---- 테마 선택 ----

        private void BuildThemeScreen()
        {
            var title = UIFactory.AddLabel(_themeScreen, "Title", "테마를 선택하세요", 90, Color.white, FontStyle.Bold);
            UIFactory.SetBox(title.rectTransform, new Vector2(0f, 420f), new Vector2(1400f, 140f));

            var subtitle = UIFactory.AddLabel(_themeScreen, "Subtitle", "방장이 고른 테마로 모두가 함께 플레이해요", 38, MutedText);
            UIFactory.SetBox(subtitle.rectTransform, new Vector2(0f, 335f), new Vector2(1000f, 60f));

            var themes = ThemeLibrary.All;
            for (int i = 0; i < themes.Count; i++)
            {
                float x = (i - (themes.Count - 1) * 0.5f) * 360f;
                BuildThemeCard(themes[i], new Vector2(x, -60f));
            }

            // 혼자 하기 흐름에서는 모드 선택으로, 멀티 방 만들기 흐름에서는 타이틀로 돌아간다.
            UIFactory.AddTextButton(_themeScreen, "BackButton", "뒤로", new Vector2(-780f, -450f), new Vector2(240f, 90f),
                SecondaryButton, Color.white, 40, () => { if (_soloMode != null) ShowSoloModeScreen(); else ShowTitleScreen(); }, 36f);
        }

        private void BuildThemeCard(MazeThemeConfig theme, Vector2 position)
        {
            var card = UIFactory.NewRect("Card_" + theme.englishName, _themeScreen);
            UIFactory.SetBox(card, position, new Vector2(300f, 620f));
            var baseImage = UIFactory.AddRounded(card, theme.accentColor, 56f);
            UIFactory.MakeButton(card, baseImage, () => ShowSizeScreen(theme), 1.06f);

            var emblem = UIFactory.NewRect("Emblem", card);
            UIFactory.SetBox(emblem, new Vector2(0f, 110f), new Vector2(240f, 240f));
            ThemeEmblem.Build(emblem, theme.season, theme.accentColor);

            Color textColor = UIFactory.ContrastText(theme.accentColor);

            var nameLabel = UIFactory.AddLabel(card, "Name", theme.displayName, 88, textColor, FontStyle.Bold);
            UIFactory.SetBox(nameLabel.rectTransform, new Vector2(0f, -150f), new Vector2(300f, 120f));

            var englishLabel = UIFactory.AddLabel(card, "EnglishName", theme.englishName.ToUpperInvariant(), 32,
                new Color(textColor.r, textColor.g, textColor.b, 0.75f));
            UIFactory.SetBox(englishLabel.rectTransform, new Vector2(0f, -232f), new Vector2(300f, 50f));
        }

        // ---- 크기 선택 ----

        private void BuildSizeScreen()
        {
            var title = UIFactory.AddLabel(_sizeScreen, "Title", "맵 크기를 선택하세요", 80, Color.white, FontStyle.Bold);
            UIFactory.SetBox(title.rectTransform, new Vector2(0f, 400f), new Vector2(1400f, 120f));

            _sizeSubtitle = UIFactory.AddLabel(_sizeScreen, "ThemeName", string.Empty, 40, MutedText, FontStyle.Bold);
            UIFactory.SetBox(_sizeSubtitle.rectTransform, new Vector2(0f, 320f), new Vector2(1000f, 60f));

            var sizes = GameSession.AllowedSizes;
            const int firstRowCount = 4;
            const float spacing = 270f;
            for (int i = 0; i < sizes.Length; i++)
            {
                bool firstRow = i < firstRowCount;
                int rowCount = firstRow ? firstRowCount : sizes.Length - firstRowCount;
                int indexInRow = firstRow ? i : i - firstRowCount;
                float x = (indexInRow - (rowCount - 1) * 0.5f) * spacing;
                float y = firstRow ? 110f : -160f;
                BuildSizeButton(sizes[i], new Vector2(x, y));
            }

            UIFactory.AddTextButton(_sizeScreen, "BackButton", "뒤로", new Vector2(-780f, -450f), new Vector2(240f, 90f),
                SecondaryButton, Color.white, 40, ShowThemeScreen, 36f);

            _createButton = UIFactory.AddTextButton(_sizeScreen, "StartButton", "방 만들기", new Vector2(0f, -400f),
                new Vector2(520f, 110f), DisabledColor, Color.white, 56, CreateRoom, 44f);

            _sizeError = UIFactory.AddLabel(_sizeScreen, "Error", string.Empty, 34, WarningText);
            UIFactory.SetBox(_sizeError.rectTransform, new Vector2(0f, -488f), new Vector2(1500f, 50f));
        }

        private void BuildSizeButton(int size, Vector2 position)
        {
            var holder = UIFactory.NewRect("Size_" + size, _sizeScreen);
            UIFactory.SetBox(holder, position, new Vector2(220f, 220f));
            holder.gameObject.AddComponent<UIHoverScale>().hoverScale = 1.06f;

            var ringRt = UIFactory.NewRect("Ring", holder);
            UIFactory.Stretch(ringRt);
            ringRt.offsetMin = new Vector2(-12f, -12f);
            ringRt.offsetMax = new Vector2(12f, 12f);
            UIFactory.AddRounded(ringRt, Color.white, 56f);
            ringRt.gameObject.SetActive(false);

            var baseRt = UIFactory.NewRect("Base", holder);
            UIFactory.Stretch(baseRt);
            var baseImage = UIFactory.AddRounded(baseRt, SizeButtonColor, 44f);
            UIFactory.MakeButton(baseRt, baseImage, () => SelectSize(size), 1f);

            var number = UIFactory.AddLabel(baseRt, "Number", size.ToString(), 88, SizeButtonText, FontStyle.Bold);
            UIFactory.SetBox(number.rectTransform, new Vector2(0f, 22f), new Vector2(220f, 110f));

            var caption = UIFactory.AddLabel(baseRt, "Caption", "× " + size, 34,
                new Color(SizeButtonText.r, SizeButtonText.g, SizeButtonText.b, 0.6f));
            UIFactory.SetBox(caption.rectTransform, new Vector2(0f, -56f), new Vector2(220f, 50f));

            _sizeButtons.Add(new SizeButton { size = size, ring = ringRt.gameObject });
        }

        private void RefreshSizeSelection()
        {
            Color accent = _theme != null ? _theme.accentColor : Color.white;

            foreach (var sizeButton in _sizeButtons)
            {
                sizeButton.ring.GetComponent<Image>().color = accent;
                sizeButton.ring.SetActive(sizeButton.size == _selectedSize);
            }

            bool ready = _theme != null && _selectedSize != 0;
            _createButton.SetInteractable(ready, accent, UIFactory.ContrastText(accent));
        }
    }
}
