using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Mirro.Core;
using Mirro.Gameplay;
using Mirro.Networking;
using Mirro.Themes;

namespace Mirro.UI
{
    /// <summary>
    /// 경기가 끝났을 때 뜨는 결과 화면. 대결 모드는 우승자와 순위(탈락한 순서의 역순), 누가 누구의 깃발을 뽑았는지, 경기 시간을,
    /// 깃발 찾기는 기록과 최고 기록을 보여준다. 떠 있는 동안에는 조작이 막히고 커서가 풀리며, [메인 메뉴]로 방을 나갈 수 있다.
    /// 방장(혼자 하기 포함)은 [다시 하기]로 다시 시작한다.
    /// </summary>
    public class ResultScreen : MonoBehaviour
    {
        private const int MaxRows = 4;

        private static readonly Color Gold = new Color(1f, 0.84f, 0.32f);
        private static readonly Color Muted = new Color(0.72f, 0.77f, 0.80f);

        /// <summary>결과 화면이 떠 있으면 true(일시정지 메뉴가 Esc를 무시하는 데 쓴다).</summary>
        public static bool IsShowing { get; private set; }

        private bool _newRecord;

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

            // 깃발 찾기 기록은 결과 화면이 뜰 때 한 번만 내 PC에 제출한다.
            if (match.CurrentMode == GameMode.Treasure)
                _newRecord = TreasureRecords.Submit(GameSession.MazeSize, (float)match.Elapsed, out _);

            Build(local.PlayerId, match, theme != null ? theme.accentColor : new Color(0.62f, 0.79f, 0.93f));
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
            {
                ranking.Add(new Rank { clientId = match.WinnerId.Value, colorIndex = match.WinnerColor.Value, note = "최후의 생존자" });
            }
            else
            {
                // 우승자 없이 끝났다면(봇 대결에서 내가 탈락) 아직 살아 있는 플레이어를 맨 위에 둔다.
                foreach (var player in NetworkPlayer.All)
                    if (player.IsAlive.Value) ranking.Add(new Rank { clientId = player.PlayerId, colorIndex = player.ColorIndex.Value, note = "생존" });
            }

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

        private static int ColorOf(MatchManager match, ulong playerId)
        {
            if (match.WinnerId.Value == playerId) return match.WinnerColor.Value;
            for (int i = 0; i < match.Eliminated.Count; i++)
                if (match.Eliminated[i].clientId == playerId) return match.Eliminated[i].colorIndex;
            var player = NetworkPlayer.Find(playerId);
            return player != null ? player.ColorIndex.Value : 0;
        }

        private static string NameOf(Rank rank, ulong localId)
        {
            string name = PlayerColors.GetName(rank.colorIndex);
            if (rank.clientId == localId) return name + " (나)";
            return MatchManager.IsBotId(rank.clientId) ? name + " (봇)" : name;
        }

        private static string FormatTime(double seconds)
        {
            var span = TimeSpan.FromSeconds(seconds);
            return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
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

            var mode = match.CurrentMode;
            bool treasure = mode == GameMode.Treasure;
            bool won = match.WinnerId.Value == localId;
            // 봇 대결은 내가 이기거나(마지막 생존) 지는 것(탈락)으로만 끝난다.
            bool lost = !won && mode == GameMode.Bots;

            string titleText = treasure ? "완료!" : won ? "승리!" : lost ? "패배" : "게임 종료";
            var title = UIFactory.AddLabel(panel, "Title", titleText, 96, treasure || won ? Gold : Color.white, FontStyle.Bold);
            UIFactory.SetBox(title.rectTransform, new Vector2(0f, 370f), new Vector2(1000f, 130f));

            string summary;
            if (treasure) summary = $"깃발 {match.TotalTreasures.Value}개를 모두 찾았어요";
            else if (won) summary = "마지막까지 살아남았어요";
            else if (lost) summary = "내 깃발이 뽑혔어요";
            else summary = match.WinnerId.Value != MatchManager.NoOne ? PlayerColors.GetName(match.WinnerColor.Value) + " 플레이어가 우승했어요" : "우승자가 없어요";
            var span = TimeSpan.FromSeconds(match.Elapsed);
            var subtitle = UIFactory.AddLabel(panel, "Subtitle", $"{summary}  ·  경기 시간 {(int)span.TotalMinutes}분 {span.Seconds:00}초", 38, new Color(0.78f, 0.82f, 0.85f));
            UIFactory.SetBox(subtitle.rectTransform, new Vector2(0f, 285f), new Vector2(1000f, 60f));

            if (treasure) BuildRecordRows(panel, match);
            else BuildRankRows(panel, match, localId);

            BuildButtons(panel, accent);
        }

        /// <summary>깃발 찾기: 이번 기록과 최고 기록(새 기록이면 표시).</summary>
        private void BuildRecordRows(RectTransform panel, MatchManager match)
        {
            float best = TreasureRecords.Best(GameSession.MazeSize);
            AddInfoRow(panel, 0, "기록", FormatTime(match.Elapsed), Color.white);
            AddInfoRow(panel, 1, "최고 기록", FormatTime(best) + (_newRecord ? " · 새 기록!" : string.Empty), _newRecord ? Gold : Muted);
            for (int i = 2; i < MaxRows; i++)
            {
                var row = UIFactory.NewRect("Row" + i, panel);
                row.gameObject.SetActive(false);
            }
        }

        private static void AddInfoRow(RectTransform panel, int index, string label, string value, Color valueColor)
        {
            var row = UIFactory.NewRect("Row" + index, panel);
            UIFactory.SetBox(row, new Vector2(0f, 175f - 115f * index), new Vector2(980f, 100f));
            UIFactory.AddRounded(row, new Color(0.18f, 0.21f, 0.24f), 40f);

            var name = UIFactory.AddLabel(row, "Name", label, 44, Color.white, FontStyle.Bold);
            name.alignment = TextAnchor.MiddleLeft;
            UIFactory.SetBox(name.rectTransform, new Vector2(-250f, 0f), new Vector2(400f, 70f));

            var note = UIFactory.AddLabel(row, "Note", value, 44, valueColor, FontStyle.Bold);
            note.alignment = TextAnchor.MiddleRight;
            UIFactory.SetBox(note.rectTransform, new Vector2(180f, 0f), new Vector2(560f, 70f));
        }

        private static void BuildRankRows(RectTransform panel, MatchManager match, ulong localId)
        {
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

                var name = UIFactory.AddLabel(row, "Name", NameOf(rank, localId), 42, Color.white, FontStyle.Bold);
                name.alignment = TextAnchor.MiddleLeft;
                UIFactory.SetBox(name.rectTransform, new Vector2(-70f, 0f), new Vector2(340f, 70f));

                var note = UIFactory.AddLabel(row, "Note", rank.note, 34, Muted);
                note.alignment = TextAnchor.MiddleRight;
                UIFactory.SetBox(note.rectTransform, new Vector2(250f, 0f), new Vector2(440f, 70f));
            }
        }

        private static void BuildButtons(RectTransform panel, Color accent)
        {
            var session = NetworkSession.Instance;
            bool solo = session != null && session.IsSolo.Value;
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            var secondary = new Color(0.26f, 0.29f, 0.31f);

            if (solo || isHost)
            {
                // 방장(혼자 하기 포함)은 다시 시작할 수 있다. 혼자 하기는 새 미로로 바로, 멀티는 모두를 대기실로 부른다.
                string hintText = solo ? "다시 하기: 같은 설정으로 새 미로에서 처음부터 시작해요" : "다시 하기: 모두 대기실로 돌아가 새 미로로 다시 시작해요";
                var hint = UIFactory.AddLabel(panel, "Hint", hintText, 30, Muted);
                UIFactory.SetBox(hint.rectTransform, new Vector2(0f, -290f), new Vector2(1000f, 50f));

                UIFactory.AddTextButton(panel, "RestartButton", "다시 하기", new Vector2(-270f, -370f), new Vector2(500f, 100f),
                    accent, UIFactory.ContrastText(accent), 46, Restart, 40f);
                UIFactory.AddTextButton(panel, "MenuButton", "메인 메뉴", new Vector2(270f, -370f), new Vector2(500f, 100f),
                    secondary, Color.white, 46, () => NetworkFlow.Instance.Leave(true), 40f);
            }
            else
            {
                var hint = UIFactory.AddLabel(panel, "Hint", "방장이 다시 시작하기를 기다리는 중이에요...", 30, Muted);
                UIFactory.SetBox(hint.rectTransform, new Vector2(0f, -290f), new Vector2(1000f, 50f));

                UIFactory.AddTextButton(panel, "MenuButton", "메인 메뉴", new Vector2(0f, -370f), new Vector2(520f, 100f),
                    secondary, Color.white, 46, () => NetworkFlow.Instance.Leave(true), 40f);
            }
        }

        private static void Restart()
        {
            var session = NetworkSession.Instance;
            if (session == null) return;

            if (session.IsSolo.Value) session.RestartSolo();
            else session.ReturnToLobby();
        }
    }
}
