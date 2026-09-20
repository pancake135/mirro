using System;
using UnityEngine;
using UnityEngine.UI;
using Mirro.Gameplay;

namespace Mirro.UI
{
    /// <summary>봇과 대결 설정: 봇 수(1~3)와 난이도(쉬움/보통/어려움).</summary>
    public class BotOptionsScreen
    {
        private static readonly Color Unselected = new Color(0.24f, 0.27f, 0.29f);
        private static readonly string[] DifficultyNames = { "쉬움", "보통", "어려움" };
        private static readonly string[] DifficultyIds = { "Easy", "Normal", "Hard" };

        public RectTransform Root { get; }
        public int BotCount { get; private set; } = 3;
        public BotDifficulty Difficulty { get; private set; } = BotDifficulty.Normal;

        private readonly UIFactory.ButtonParts[] _countButtons = new UIFactory.ButtonParts[3];
        private readonly UIFactory.ButtonParts[] _difficultyButtons = new UIFactory.ButtonParts[3];
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
                _countButtons[i] = UIFactory.AddTextButton(Root, "BotCount_" + count, count + "명", new Vector2((i - 1) * 260f, 150f),
                    new Vector2(220f, 110f), Unselected, Color.white, 48, () => { BotCount = count; Refresh(); }, 40f);
            }

            var difficultyLabel = UIFactory.AddLabel(Root, "DifficultyLabel", "난이도", 44, MainMenuUI.MutedText, FontStyle.Bold);
            UIFactory.SetBox(difficultyLabel.rectTransform, new Vector2(0f, 20f), new Vector2(600f, 60f));
            for (int i = 0; i < 3; i++)
            {
                var level = (BotDifficulty)i;
                _difficultyButtons[i] = UIFactory.AddTextButton(Root, "Diff_" + DifficultyIds[i], DifficultyNames[i], new Vector2((i - 1) * 340f, -80f),
                    new Vector2(300f, 110f), Unselected, Color.white, 48, () => { Difficulty = level; Refresh(); }, 40f);
            }

            var hint = UIFactory.AddLabel(Root, "Hint", "쉬움: 봇이 가까운 깃발만 알아채요 · 보통/어려움: 봇이 길을 알고 곧장 와요", 32, MainMenuUI.MutedText);
            UIFactory.SetBox(hint.rectTransform, new Vector2(0f, -190f), new Vector2(1700f, 50f));

            UIFactory.AddTextButton(Root, "BotStartButton", "시작", new Vector2(0f, -320f), new Vector2(520f, 110f),
                MainMenuUI.DefaultAccent, UIFactory.ContrastText(MainMenuUI.DefaultAccent), 56, () => onStart(), 44f);
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
            Color selectedText = UIFactory.ContrastText(_accent);
            for (int i = 0; i < 3; i++)
            {
                Apply(_countButtons[i], BotCount == i + 1, selectedText);
                Apply(_difficultyButtons[i], (int)Difficulty == i, selectedText);
            }
        }

        private void Apply(UIFactory.ButtonParts button, bool selected, Color selectedText)
        {
            button.image.color = selected ? _accent : Unselected;
            button.label.color = selected ? selectedText : Color.white;
        }
    }
}
