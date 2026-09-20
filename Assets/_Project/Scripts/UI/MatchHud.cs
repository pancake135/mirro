using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Mirro.Gameplay;
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
        private RectTransform _prompt;
        private Text _promptText;
        private RectTransform _barFill;
        private RectTransform _spectate;
        private Text _spectateTitle;
        private Text _spectateSub;
        private readonly List<Toast> _toasts = new List<Toast>();

        public void Init(NetworkPlayer local, MazeThemeConfig theme)
        {
            _local = local;
            _theme = theme;
            _puller = local.GetComponent<FlagPuller>();
            Build();
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
            UpdateSpectateBanner();
            UpdateResult();
        }

        // ---- 갱신 ----

        private void UpdateAlive()
        {
            _alive.text = $"생존 {_match.AliveCount} / {_match.TotalPlayers.Value}";
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

            _promptText.text = $"E 키를 길게 눌러 {PlayerColors.GetName(candidate.ColorIndex)} 깃발 뽑기";
            float progress = _puller.Progress;
            _barFill.gameObject.SetActive(progress > 0.02f);
            _barFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
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
            bool complete = _match.Eliminated.Count >= _match.TotalPlayers.Value - 1;
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

            var aliveRt = UIFactory.NewRect("AliveLabel", _canvasRt);
            UIFactory.SetBox(aliveRt, new Vector2(0f, 480f), new Vector2(340f, 72f));
            UIFactory.AddRounded(aliveRt, new Color(0f, 0f, 0f, 0.5f), 34f);
            _alive = UIFactory.AddLabel(aliveRt, "Text", "생존", 40, Color.white, FontStyle.Bold);

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
        }
    }
}
