using UnityEngine;

namespace Mirro.Themes
{
    /// <summary>테마별 기본값의 단일 출처. 에셋 생성(에디터)과 런타임 폴백이 함께 사용한다.</summary>
    public static class ThemeDefaults
    {
        public static MazeThemeConfig Create(SeasonTheme season)
        {
            var t = ScriptableObject.CreateInstance<MazeThemeConfig>();
            t.season = season;
            t.name = season.ToString();

            switch (season)
            {
                case SeasonTheme.Spring:
                    t.displayName = "봄";
                    t.englishName = "Spring";
                    t.accentColor = new Color(0.96f, 0.66f, 0.78f);
                    t.wallColor = new Color(0.38f, 0.68f, 0.32f);
                    t.flowerColor = new Color(1.00f, 0.70f, 0.82f);
                    t.groundColor = new Color(0.50f, 0.42f, 0.28f);
                    t.skyColor = new Color(0.66f, 0.84f, 0.96f);
                    t.ambientColor = new Color(0.55f, 0.58f, 0.62f);
                    t.sunColor = new Color(1.00f, 0.96f, 0.90f);
                    break;
                case SeasonTheme.Summer:
                    t.displayName = "여름";
                    t.englishName = "Summer";
                    t.accentColor = new Color(0.28f, 0.74f, 0.46f);
                    t.wallColor = new Color(0.10f, 0.46f, 0.16f);
                    t.flowerColor = new Color(1.00f, 0.85f, 0.20f);
                    t.groundColor = new Color(0.45f, 0.38f, 0.24f);
                    t.skyColor = new Color(0.40f, 0.72f, 1.00f);
                    t.ambientColor = new Color(0.55f, 0.58f, 0.55f);
                    t.sunColor = new Color(1.00f, 0.98f, 0.85f);
                    break;
                case SeasonTheme.Autumn:
                    t.displayName = "가을";
                    t.englishName = "Autumn";
                    t.accentColor = new Color(0.93f, 0.56f, 0.20f);
                    t.wallColor = new Color(0.70f, 0.36f, 0.12f);
                    t.flowerColor = new Color(0.85f, 0.22f, 0.12f);
                    t.groundColor = new Color(0.38f, 0.27f, 0.16f);
                    t.skyColor = new Color(0.86f, 0.72f, 0.55f);
                    t.ambientColor = new Color(0.55f, 0.50f, 0.45f);
                    t.sunColor = new Color(1.00f, 0.88f, 0.70f);
                    break;
                default:
                    t.displayName = "겨울";
                    t.englishName = "Winter";
                    t.accentColor = new Color(0.62f, 0.79f, 0.93f);
                    t.wallColor = new Color(0.60f, 0.72f, 0.68f);
                    t.flowerColor = new Color(0.93f, 0.97f, 1.00f);
                    t.groundColor = new Color(0.76f, 0.76f, 0.72f);
                    t.skyColor = new Color(0.78f, 0.85f, 0.92f);
                    t.ambientColor = new Color(0.62f, 0.66f, 0.72f);
                    t.sunColor = new Color(0.90f, 0.95f, 1.00f);
                    break;
            }

            return t;
        }
    }
}
