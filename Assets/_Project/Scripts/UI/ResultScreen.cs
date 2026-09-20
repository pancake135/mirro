using System;
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
    /// 경기가 끝났을 때 뜨는 결과 화면. 우승자와 순위(탈락한 순서의 역순), 누가 누구의 깃발을 뽑았는지, 경기 시간을 보여준다.
    /// 떠 있는 동안에는 조작이 막히고 커서가 풀리며, [메인 메뉴]로 방을 나갈 수 있다.
    /// </summary>
    public class ResultScreen : MonoBehaviour
    {
        private const int MaxRows = 4;

        private static readonly Color Gold = new Color(1f, 0.84f, 0.32f);

        /// <summary>결과 화면이 떠 있으면 true(일시정지 메뉴가 Esc를 무시하는 데 쓴다).</summary>
        public static bool IsShowing { get; private set; }

        private void OnDestroy()
        {
            IsShowing = false;
        }

        public void Show(NetworkPlayer local, MatchManager match, MazeThemeConfig theme)
        {
            IsShowing = true;
            UIFactory.EnsureEventSystem();

            local.controller.IsPaused = true;
            local.controller.SetCursorLock(false);

            Build(local.OwnerClientId, match, theme != null ? theme.accentColor : new Color(0.62f, 0.79f, 0.93f));
        }

        private struct Rank
        {
            public ulong clientId;
            public int colorIndex;
            public string note;
        }

        private static List<Rank> BuildRanking(MatchManager match)
        {
            var ranking = new List<Rank>();
            if (match.WinnerId.Value != MatchManager.NoOne)
                ranking.Add(new Rank { clientId = match.WinnerId.Value, colorIndex = match.WinnerColor.Value, note = "최후의 생존자" });

            // 나중에 탈락한 사람일수록 순위가 높다.
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

        private static int ColorOf(MatchManager match, ulong clientId)
        {
            if (match.WinnerId.Value == clientId) return match.WinnerColor.Value;
            for (int i = 0; i < match.Eliminated.Count; i++)
                if (match.Eliminated[i].clientId == clientId) return match.Eliminated[i].colorIndex;
            var player = NetworkPlayer.Find(clientId);
            return player != null ? player.ColorIndex.Value : 0;
        }

        private void Build(ulong localId, MatchManager match, Color accent)
        {
            var canvasGo = new GameObject("ResultCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var canvasRt = (RectTransform)canvasGo.transform;

            var dim = UIFactory.NewRect("Dim", canvasRt);
            UIFactory.Stretch(dim);
            UIFactory.AddSolid(dim, new Color(0f, 0f, 0f, 0.62f)).raycastTarget = true;

            var panel = UIFactory.NewRect("Panel", canvasRt);
            UIFactory.SetBox(panel, Vector2.zero, new Vector2(1100f, 900f));
            UIFactory.AddRounded(panel, new Color(0.13f, 0.15f, 0.17f), 56f);

            bool won = match.WinnerId.Value == localId;
            var title = UIFactory.AddLabel(panel, "Title", won ? "승리!" : "게임 종료", 96, won ? Gold : Color.white, FontStyle.Bold);
            UIFactory.SetBox(title.rectTransform, new Vector2(0f, 370f), new Vector2(1000f, 130f));

            string winnerLine = won
                ? "마지막까지 살아남았어요"
                : match.WinnerId.Value != MatchManager.NoOne ? PlayerColors.GetName(match.WinnerColor.Value) + " 플레이어가 우승했어요" : "우승자가 없어요";
            var span = TimeSpan.FromSeconds(match.Elapsed);
            var subtitle = UIFactory.AddLabel(panel, "Subtitle", $"{winnerLine}  ·  경기 시간 {(int)span.TotalMinutes}분 {span.Seconds:00}초",
                38, new Color(0.78f, 0.82f, 0.85f));
            UIFactory.SetBox(subtitle.rectTransform, new Vector2(0f, 285f), new Vector2(1000f, 60f));

            var ranking = BuildRanking(match);
            for (int i = 0; i < MaxRows; i++)
            {
                var row = UIFactory.NewRect("Row" + i, panel);
                UIFactory.SetBox(row, new Vector2(0f, 175f - 115f * i), new Vector2(980f, 100f));
                if (i >= ranking.Count)
                {
                    row.gameObject.SetActive(false);
                    continue;
                }

                var rank = ranking[i];
                bool isLocal = rank.clientId == localId;
                UIFactory.AddRounded(row, isLocal ? new Color(0.24f, 0.28f, 0.32f) : new Color(0.18f, 0.21f, 0.24f), 40f);

                var place = UIFactory.AddLabel(row, "Rank", (i + 1) + "위", 44, i == 0 ? Gold : Color.white, FontStyle.Bold);
                UIFactory.SetBox(place.rectTransform, new Vector2(-400f, 0f), new Vector2(140f, 70f));

                var swatchRt = UIFactory.NewRect("Swatch", row);
                UIFactory.SetBox(swatchRt, new Vector2(-290f, 0f), new Vector2(58f, 58f));
                UIFactory.AddCircle(swatchRt, PlayerColors.Get(rank.colorIndex));

                var name = UIFactory.AddLabel(row, "Name", PlayerColors.GetName(rank.colorIndex) + (isLocal ? " (나)" : string.Empty),
                    42, Color.white, FontStyle.Bold);
                name.alignment = TextAnchor.MiddleLeft;
                UIFactory.SetBox(name.rectTransform, new Vector2(-70f, 0f), new Vector2(340f, 70f));

                var note = UIFactory.AddLabel(row, "Note", rank.note, 34, new Color(0.72f, 0.77f, 0.80f));
                note.alignment = TextAnchor.MiddleRight;
                UIFactory.SetBox(note.rectTransform, new Vector2(250f, 0f), new Vector2(440f, 70f));
            }

            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            if (isHost)
            {
                // 방장은 모두를 같은 방의 대기실로 불러 다시 시작할 수 있다.
                var hint = UIFactory.AddLabel(panel, "Hint", "다시 하기: 모두 대기실로 돌아가 새 미로로 다시 시작해요", 30, new Color(0.72f, 0.77f, 0.80f));
                UIFactory.SetBox(hint.rectTransform, new Vector2(0f, -290f), new Vector2(1000f, 50f));

                UIFactory.AddTextButton(panel, "RestartButton", "다시 하기", new Vector2(-270f, -370f), new Vector2(500f, 100f),
                    accent, UIFactory.ContrastText(accent), 46, Restart, 40f);
                UIFactory.AddTextButton(panel, "MenuButton", "메인 메뉴", new Vector2(270f, -370f), new Vector2(500f, 100f),
                    new Color(0.26f, 0.29f, 0.31f), Color.white, 46, () => NetworkFlow.Instance.Leave(true), 40f);
            }
            else
            {
                var hint = UIFactory.AddLabel(panel, "Hint", "방장이 다시 시작하기를 기다리는 중이에요...", 30, new Color(0.72f, 0.77f, 0.80f));
                UIFactory.SetBox(hint.rectTransform, new Vector2(0f, -290f), new Vector2(1000f, 50f));

                UIFactory.AddTextButton(panel, "MenuButton", "메인 메뉴", new Vector2(0f, -370f), new Vector2(520f, 100f),
                    new Color(0.26f, 0.29f, 0.31f), Color.white, 46, () => NetworkFlow.Instance.Leave(true), 40f);
            }
        }

        private static void Restart()
        {
            var session = NetworkSession.Instance;
            if (session != null) session.ReturnToLobby();
        }
    }
}
