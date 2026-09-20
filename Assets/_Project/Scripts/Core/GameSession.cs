using Mirro.Themes;

namespace Mirro.Core
{
    /// <summary>
    /// 메뉴에서 고른 게임 설정을 씬 전환 너머로 전달하는 정적 컨테이너.
    /// 멀티플레이(M3)에서는 호스트가 정한 값이 이 자리로 동기화되어 들어온다.
    /// </summary>
    public static class GameSession
    {
        public const string MenuScene = "MainMenu";
        public const string GameScene = "Game";

        public static readonly int[] AllowedSizes = { 50, 70, 100, 120, 150, 170, 200 };

        public static bool HasSelection { get; private set; }
        public static MazeThemeConfig Theme { get; private set; }
        public static int MazeSize { get; private set; }
        public static int Seed { get; private set; }

        public static void Begin(MazeThemeConfig theme, int mazeSize, int seed)
        {
            Theme = theme;
            MazeSize = mazeSize;
            Seed = seed;
            HasSelection = true;
        }

        public static void Clear()
        {
            Theme = null;
            HasSelection = false;
        }
    }
}
