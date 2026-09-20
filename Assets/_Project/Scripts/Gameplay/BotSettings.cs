namespace Mirro.Gameplay
{
    public enum BotDifficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2
    }

    /// <summary>난이도별 봇 능력치. 쉬움은 탐색형(가까운 깃발만 인지), 보통/어려움은 추격형(미로를 안다).</summary>
    public readonly struct BotSettings
    {
        /// <summary>탐색형이 깃발을 알아채는 경로 거리(칸).</summary>
        public const int AwarenessDepth = 12;

        public readonly float speed;
        public readonly float startDelay;
        public readonly bool explorer;

        private BotSettings(float speed, float startDelay, bool explorer)
        {
            this.speed = speed;
            this.startDelay = startDelay;
            this.explorer = explorer;
        }

        public static BotSettings For(BotDifficulty difficulty)
        {
            switch (difficulty)
            {
                case BotDifficulty.Easy: return new BotSettings(3f, 45f, true);
                case BotDifficulty.Hard: return new BotSettings(5f, 10f, false);
                default: return new BotSettings(4f, 25f, false);
            }
        }
    }
}
