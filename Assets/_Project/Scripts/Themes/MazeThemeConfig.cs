using UnityEngine;

namespace Mirro.Themes
{
    public enum SeasonTheme
    {
        Spring,
        Summer,
        Autumn,
        Winter
    }

    /// <summary>
    /// 계절 테마 하나의 시각/오디오 설정. 새 테마 요소(꽃 프리팹, 아이템 스킨 등)는
    /// 이 에셋에 필드를 추가하는 방식으로 확장한다.
    /// </summary>
    [CreateAssetMenu(menuName = "Mirro/Maze Theme", fileName = "NewTheme")]
    public class MazeThemeConfig : ScriptableObject
    {
        public SeasonTheme season;
        public string displayName;
        public string englishName;

        [Header("UI")]
        public Color accentColor = Color.white;

        [Header("Maze visuals")]
        public Color wallColor = Color.green;
        public Color flowerColor = Color.white;
        public Color groundColor = Color.gray;
        public Color skyColor = Color.cyan;
        public Color ambientColor = Color.gray;
        public Color sunColor = Color.white;

        [Header("Audio (비워두면 무음)")]
        public AudioClip bgm;
    }
}
