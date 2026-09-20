using System;
using System.Collections.Generic;
using Mirro.Core;
using Mirro.Maze;

namespace Mirro.Gameplay
{
    /// <summary>
    /// 미로 위 BFS 경로 탐색. 배열을 재사용해 호출마다 메모리를 새로 잡지 않는다(방문 표시는 호출 번호 스탬프).
    /// </summary>
    public sealed class BotPathfinder
    {
        private static readonly WallSide[] Sides = { WallSide.North, WallSide.East, WallSide.South, WallSide.West };

        private readonly MazeData _maze;
        private readonly int[] _offset;
        private readonly int[] _parent;
        private readonly int[] _depth;
        private readonly int[] _queue;
        private readonly int[] _stamp;
        private int _currentStamp;

        public BotPathfinder(MazeData maze)
        {
            _maze = maze;
            int count = maze.Cells.Length;
            // 바깥 테두리는 항상 벽이라 인덱스가 다른 줄로 넘어가는 일이 없다(북/동/남/서 순서).
            _offset = new[] { maze.Width, 1, -maze.Width, -1 };
            _parent = new int[count];
            _depth = new int[count];
            _queue = new int[count];
            _stamp = new int[count];
        }

        /// <summary>
        /// from에서 isGoal을 만족하는 가장 가까운 칸까지의 경로(from 제외, 목표 포함)를 path에 담는다.
        /// maxDepth칸 안에 없으면 false. rng가 있으면 이웃 검사 순서를 돌려 동점 경로가 무작위로 갈리게 한다.
        /// from 자체가 목표면 빈 경로로 true.
        /// </summary>
        public bool TryFindPath(int from, Func<int, bool> isGoal, int maxDepth, DeterministicRandom rng, List<int> path)
        {
            path.Clear();
            if (isGoal(from)) return true;

            _currentStamp++;
            int head = 0, tail = 0;
            _queue[tail++] = from;
            _stamp[from] = _currentStamp;
            _parent[from] = -1;
            _depth[from] = 0;
            int rotation = rng != null ? rng.NextInt(0, 4) : 0;

            while (head < tail)
            {
                int cell = _queue[head++];
                if (_depth[cell] >= maxDepth) continue;

                WallSide walls = _maze.Cells[cell];
                for (int k = 0; k < 4; k++)
                {
                    int side = (k + rotation) & 3;
                    if ((walls & Sides[side]) != 0) continue;

                    int next = cell + _offset[side];
                    if (_stamp[next] == _currentStamp) continue;

                    _stamp[next] = _currentStamp;
                    _parent[next] = cell;
                    _depth[next] = _depth[cell] + 1;
                    if (isGoal(next))
                    {
                        for (int c = next; c != from; c = _parent[c]) path.Add(c);
                        path.Reverse();
                        return true;
                    }
                    _queue[tail++] = next;
                }
            }
            return false;
        }
    }
}
