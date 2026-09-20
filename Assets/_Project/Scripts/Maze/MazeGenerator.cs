using System.Collections.Generic;
using Mirro.Core;

namespace Mirro.Maze
{
    /// <summary>
    /// 재귀 백트래커 알고리즘으로 완전 미로(perfect maze)를 생성한다.
    /// 콜스택 재귀 대신 명시적 스택을 사용해 200x200(=40000셀)에서도
    /// 스택 오버플로 위험 없이 동작한다. seed가 같으면 항상 동일한 결과를 낸다.
    /// </summary>
    public static class MazeGenerator
    {
        private static readonly WallSide[] AllSides = { WallSide.North, WallSide.East, WallSide.South, WallSide.West };

        public static MazeData Generate(int size, int seed)
        {
            var maze = new MazeData(size, size, seed);
            for (int i = 0; i < maze.Cells.Length; i++)
                maze.Cells[i] = WallSide.All;

            var rng = new DeterministicRandom(seed);
            var visited = new bool[size * size];
            var stack = new Stack<(int x, int y)>();
            var candidateBuffer = new List<(int x, int y, WallSide dir)>(4);

            visited[maze.Index(0, 0)] = true;
            stack.Push((0, 0));

            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Peek();

                candidateBuffer.Clear();
                foreach (var side in AllSides)
                {
                    MazeData.GetOffset(side, out int dx, out int dy);
                    int nx = cx + dx;
                    int ny = cy + dy;
                    if (maze.InBounds(nx, ny) && !visited[maze.Index(nx, ny)])
                        candidateBuffer.Add((nx, ny, side));
                }

                if (candidateBuffer.Count == 0)
                {
                    stack.Pop();
                    continue;
                }

                var (nextX, nextY, dir) = candidateBuffer[rng.NextInt(0, candidateBuffer.Count)];

                maze.Cells[maze.Index(cx, cy)] &= ~dir;
                maze.Cells[maze.Index(nextX, nextY)] &= ~MazeData.Opposite(dir);

                visited[maze.Index(nextX, nextY)] = true;
                stack.Push((nextX, nextY));
            }

            return maze;
        }
    }
}
