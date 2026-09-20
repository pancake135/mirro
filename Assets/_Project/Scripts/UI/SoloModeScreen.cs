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
