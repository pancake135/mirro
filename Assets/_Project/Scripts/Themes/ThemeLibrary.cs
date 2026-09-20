using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Mirro.Themes
{
    /// <summary>Resources/Themes 아래의 테마 에셋을 계절 순서로 제공한다. 에셋이 없으면 기본값으로 폴백한다.</summary>
    public static class ThemeLibrary
    {
        private static List<MazeThemeConfig> _all;

        public static IReadOnlyList<MazeThemeConfig> All
        {
            get
            {
                if (_all == null || _all.Any(t => t == null))
                    _all = Load();
                return _all;
            }
        }

        public static MazeThemeConfig Get(SeasonTheme season)
        {
            foreach (var theme in All)
                if (theme.season == season) return theme;
            return All[0];
        }

        private static List<MazeThemeConfig> Load()
        {
            var loaded = Resources.LoadAll<MazeThemeConfig>("Themes");
            var bySeason = new Dictionary<SeasonTheme, MazeThemeConfig>();
            foreach (var t in loaded) bySeason[t.season] = t;

            var result = new List<MazeThemeConfig>();
            foreach (SeasonTheme season in Enum.GetValues(typeof(SeasonTheme)))
                result.Add(bySeason.TryGetValue(season, out var t) ? t : ThemeDefaults.Create(season));
            return result;
        }
    }
}
