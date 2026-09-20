using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Mirro.Networking;
using Mirro.Player;
using Mirro.Themes;

namespace Mirro.UI
{
    /// <summary>
    /// Esc로 여는 일시정지 메뉴(계속하기 / 메인 메뉴). 입력만 막고 월드는 멈추지 않는다
    /// (멀티플레이에서도 그대로 재사용할 수 있도록).
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        // 멀티 방장은 "로비로 돌아가기", 혼자 하기는 "다시 시작"(대기실 없이 새 미로로 바로).
        private string _restartText = "로비로 돌아가기";
        private bool _solo;
        private const float RestartConfirmSeconds = 3f;

        private FirstPersonController _controller;
        private MazeThemeConfig _theme;
        private GameObject _overlay;
        private Text _restartLabel;
        private float _restartArmedUntil;

        public bool IsOpen => _overlay != null && _overlay.activeSelf;

        public void Init(FirstPersonController controller, MazeThemeConfig theme)
        {
            _controller = controller;
            _theme = theme;
            UIFactory.EnsureEventSystem();
            Build();
            _overlay.SetActive(false);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            // 결과 화면이 떠 있는 동안에는 결과 화면이 입력/커서를 맡으므로 Esc로 메뉴를 열고 닫지 않는다.
            if (_overlay != null && keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !ResultScreen.IsShowing)
                SetOpen(!IsOpen);

            // 확인 시간이 지나면 [로비로 돌아가기] 버튼을 원래 문구로 되돌린다.
            if (_restartLabel != null && _restartArmedUntil > 0f && Time.unscaledTime > _restartArmedUntil)
                DisarmRestart();
        }

        private void SetOpen(bool open)
        {
            _overlay.SetActive(open);
            _controller.IsPaused = open;
            _controller.SetCursorLock(!open);
            if (!open) DisarmRestart();
        }

        private void ReturnToMenu()
        {
            NetworkFlow.Instance.Leave(true);
        }

        /// <summary>방장 전용. 실수로 모두의 게임을 끊지 않도록 한 번 더 눌러야 실행된다.</summary>
        private void OnRestartClicked()
        {
            if (_restartArmedUntil <= 0f)
            {
                _restartArmedUntil = Time.unscaledTime + RestartConfirmSeconds;
                _restartLabel.text = _solo ? "정말요? 한 번 더" : "모두 로비로! 한 번 더";
                return;
            }

            DisarmRestart();
            var session = NetworkSession.Instance;
            if (session == null) return;

            if (_solo) session.RestartSolo();
            else session.ReturnToLobby();
        }

        private void DisarmRestart()
        {
            _restartArmedUntil = 0f;
            if (_restartLabel != null) _restartLabel.text = _restartText;
        }

        private void Build()
        {
            var canvasGo = new GameObject("PauseCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            _overlay = canvasGo;

            var canvasRt = (RectTransform)canvasGo.transform;

            var dim = UIFactory.NewRect("Dim", canvasRt);
            UIFactory.Stretch(dim);
            UIFactory.AddSolid(dim, new Color(0f, 0f, 0f, 0.6f)).raycastTarget = true;

            // 멀티 방장에게는 [로비로 돌아가기], 혼자 하기에서는 [다시 시작]이 하나 더 보인다.
            _solo = NetworkSession.Instance != null && NetworkSession.Instance.IsSolo.Value;
            bool isHost = _solo || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer);
            _restartText = _solo ? "다시 시작" : "로비로 돌아가기";
            float step = 130f;
            float top = isHost ? 205f : 140f;

            var panel = UIFactory.NewRect("Panel", canvasRt);
            UIFactory.SetBox(panel, Vector2.zero, new Vector2(620f, isHost ? 590f : 460f));
            UIFactory.AddRounded(panel, new Color(0.13f, 0.15f, 0.17f), 56f);

            var title = UIFactory.AddLabel(panel, "Title", "일시정지", 72, Color.white, FontStyle.Bold);
            UIFactory.SetBox(title.rectTransform, new Vector2(0f, top), new Vector2(560f, 110f));

            Color accent = _theme != null ? _theme.accentColor : new Color(0.62f, 0.79f, 0.93f);
            float y = top - step;

            BuildButton(panel, "ResumeButton", "계속하기", new Vector2(0f, y), accent,
                UIFactory.ContrastText(accent), () => SetOpen(false));
            y -= step;

            if (isHost)
            {
                _restartLabel = BuildButton(panel, "RestartButton", _restartText, new Vector2(0f, y), new Color(0.50f, 0.34f, 0.16f),
                    Color.white, OnRestartClicked);
                _restartLabel.fontSize = 38;
                y -= step;
            }

            BuildButton(panel, "MenuButton", "메인 메뉴", new Vector2(0f, y), new Color(0.26f, 0.29f, 0.31f),
                Color.white, ReturnToMenu);
        }

        private static Text BuildButton(RectTransform parent, string name, string label, Vector2 position,
            Color color, Color textColor, UnityEngine.Events.UnityAction onClick)
        {
            var rt = UIFactory.NewRect(name, parent);
            UIFactory.SetBox(rt, position, new Vector2(500f, 100f));
            var image = UIFactory.AddRounded(rt, color, 40f);
            UIFactory.MakeButton(rt, image, onClick, 1.04f);

            var text = UIFactory.AddLabel(rt, "Label", label, 46, textColor, FontStyle.Bold);
            UIFactory.Stretch(text.rectTransform);
            return text;
        }
    }
}
