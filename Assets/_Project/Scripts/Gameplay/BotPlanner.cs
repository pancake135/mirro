using System.Collections.Generic;
using Mirro.Core;
using Mirro.Maze;

namespace Mirro.Gameplay
{
    /// <summary>
    /// 봇이 "어디로 갈지"만 정하는 순수 로직(물리/유니티 오브젝트 없음). 추격형은 가장 가까운 남의 깃발로,
    /// 탐색형은 12칸 안의 깃발이 보이면 그쪽으로, 아니면 가장 가까운 안 가본 칸으로 향한다.
    /// </summary>
    public sealed class BotPlanner
    {
        private readonly BotSettings _settings;
        private readonly BotPathfinder _finder;
        private readonly DeterministicRandom _rng;
        private readonly bool[] _visited;
        private readonly List<int> _path = new List<int>();

        public BotPlanner(MazeData maze, BotSettings settings, int seed)
        {
            _settings = settings;
            _finder = new BotPathfinder(maze);
            _rng = new DeterministicRandom(seed);
            for (int i = 0; i < 8; i++) _rng.NextFloat01();
            _visited = new bool[maze.Cells.Length];
        }

        /// <summary>현재 칸 다음부터 목표까지의 칸 목록.</summary>
        public IReadOnlyList<int> Path => _path;

        /// <summary>쫓고 있는 깃발의 주인(PlayerId). 탐색 중이면 MatchManager.NoOne.</summary>
        public ulong TargetFlagId { get; private set; } = MatchManager.NoOne;

        public bool IsChasing => TargetFlagId != MatchManager.NoOne;

        public void MarkVisited(int cell) => _visited[cell] = true;

        public void ClearTarget()
        {
            TargetFlagId = MatchManager.NoOne;
            _path.Clear();
        }

        /// <summary>enemyFlags: 칸 번호 → 그 칸에 서 있는 남의 깃발 주인.</summary>
        public void Plan(int cell, IReadOnlyDictionary<int, ulong> enemyFlags)
        {
            ClearTarget();
            _visited[cell] = true;

            if (enemyFlags.Count > 0)
            {
                int depth = _settings.explorer ? BotSettings.AwarenessDepth : int.MaxValue;
                if (TryChase(cell, enemyFlags, depth)) return;
            }

            if (_settings.explorer)
            {
                _finder.TryFindPath(cell, c => !_visited[c], int.MaxValue, _rng, _path);
                // 미로를 다 돌았는데도 아무도 못 찾았다면 마지막으로 아는 깃발을 쫓는다.
                if (_path.Count == 0 && enemyFlags.Count > 0) TryChase(cell, enemyFlags, int.MaxValue);
            }
        }

        private bool TryChase(int cell, IReadOnlyDictionary<int, ulong> enemyFlags, int depth)
        {
            if (!_finder.TryFindPath(cell, enemyFlags.ContainsKey, depth, null, _path)) return false;
            int goal = _path.Count > 0 ? _path[_path.Count - 1] : cell;
            TargetFlagId = enemyFlags[goal];
            return true;
        }
    }
}
