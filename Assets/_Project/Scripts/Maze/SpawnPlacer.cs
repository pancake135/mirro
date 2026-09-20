using System.Collections.Generic;
using UnityEngine;
using Mirro.Core;

namespace Mirro.Maze
{
    /// <summary>
    /// 플레이어 시작 칸 배치. 미로에서 "멀다"는 건 직선거리가 아니라 실제로 걸어가야 하는 경로 길이이므로
    /// BFS 경로 거리를 기준으로 서로의 최단 경로 거리가 최대가 되도록 고른다(farthest-point 탐욕 배치 후 다듬기).
    /// 결과는 (미로, 인원 수)만으로 결정되어 같은 입력이면 어느 피어에서 계산해도 같다.
    /// </summary>
    public static class SpawnPlacer
    {
        // 경로 거리가 아무리 멀어도 벽 하나 사이로 붙어 시작하는 일이 없도록, 직선거리도 미로 한 변의 이 비율 이상 벌린다.
        // 미로 안에 4명이 이 간격으로 들어갈 자리는 항상 있다(원 4개로는 정사각형을 덮을 수 없다).
        public const float MinStraightLineFraction = 0.3f;

        private const int MaxRefinePasses = 6;

        // 미로 생성기와 같은 seed를 쓰되 난수열이 겹치지 않도록 섞는 값.
        private const int SeedSalt = 0x51A9C3D7;

        private const int TreasureSeedSalt = 0x2545F491;

        private static readonly WallSide[] Sides = { WallSide.North, WallSide.East, WallSide.South, WallSide.West };

        /// <summary>
        /// 금색 깃발 칸을 count개 고른다. 서로 미로 한 변의 16% 이상, avoid(시작 칸)에서 15% 이상 떨어지게 무작위로 고르고,
        /// 자리가 모자라면 조건을 80%씩 낮춘다. (미로, count, avoid)만으로 결정되어 어느 피어에서 계산해도 같다.
        /// </summary>
        public static Vector2Int[] PlaceTreasures(MazeData maze, int count, Vector2Int avoid)
        {
            count = Mathf.Clamp(count, 0, maze.Cells.Length - 1);
            var result = new List<Vector2Int>(count);
            var rng = new DeterministicRandom(maze.Seed ^ TreasureSeedSalt);
            for (int i = 0; i < 8; i++) rng.NextFloat01();

            float side = Mathf.Min(maze.Width, maze.Height);
            float spacing = Mathf.Max(4f, side * 0.16f);
            float fromStart = side * 0.15f;

            while (result.Count < count)
            {
                for (int attempt = 0; attempt < 400 && result.Count < count; attempt++)
                {
                    var cell = new Vector2Int(rng.NextInt(0, maze.Width), rng.NextInt(0, maze.Height));
                    if (Vector2Int.Distance(cell, avoid) < fromStart) continue;

                    bool ok = true;
                    foreach (var other in result)
                    {
                        if (Vector2Int.Distance(cell, other) >= spacing) continue;
                        ok = false;
                        break;
                    }
                    if (ok) result.Add(cell);
                }
                spacing = Mathf.Max(0.5f, spacing * 0.8f);
                fromStart *= 0.8f;
            }
            return result.ToArray();
        }

        /// <summary>
        /// count개의 서로 다른 시작 칸을 고른다. 첫 칸은 무작위이고, 나머지는 이미 고른 칸들과의 경로 거리가
        /// 가장 큰 칸을 차례로 고른 뒤, 각 칸을 나머지로부터 더 멀리 옮길 수 있으면 옮기는 과정을 반복한다.
        /// </summary>
        public static Vector2Int[] Place(MazeData maze, int count)
        {
            int cellCount = maze.Cells.Length;
            count = Mathf.Clamp(count, 0, cellCount);
            var result = new Vector2Int[count];
            if (count == 0) return result;

            var rng = new DeterministicRandom(maze.Seed ^ SeedSalt);
            // xorshift는 작은 seed에서 처음 몇 개의 값이 서로 비슷해서 몇 번 버리고 시작한다.
            for (int i = 0; i < 8; i++) rng.NextFloat01();

            float minStraight = Mathf.Min(maze.Width, maze.Height) * MinStraightLineFraction;
            var chosen = new int[count];
            var dist = new int[count][];

            chosen[0] = rng.NextInt(0, cellCount);
            dist[0] = PathDistances(maze, chosen[0]);

            for (int k = 1; k < count; k++)
            {
                int pick = FarthestFromOthers(maze, dist, chosen, k, -1, -1, minStraight, rng);
                // 직선거리 조건을 만족하는 칸이 없으면(사실상 없는 경우) 조건 없이 가장 먼 칸을 쓴다.
                if (pick < 0) pick = FarthestFromOthers(maze, dist, chosen, k, -1, -1, 0f, rng);
                chosen[k] = pick;
                dist[k] = PathDistances(maze, pick);
            }

            for (int pass = 0; pass < MaxRefinePasses; pass++)
            {
                bool moved = false;
                for (int i = 0; i < count; i++)
                {
                    int current = NearestOtherDistance(dist, count, i, chosen[i]);
                    int pick = FarthestFromOthers(maze, dist, chosen, count, i, current, minStraight, rng);
                    if (pick < 0) continue;

                    chosen[i] = pick;
                    dist[i] = PathDistances(maze, pick);
                    moved = true;
                }
                if (!moved) break;
            }

            for (int i = 0; i < count; i++)
                result[i] = new Vector2Int(chosen[i] % maze.Width, chosen[i] / maze.Width);
            return result;
        }

        /// <summary>
        /// 앞의 count개 시작 칸 중 skip을 뺀 나머지와의 경로 거리가 가장 큰 칸(동점이면 무작위)을 고른다.
        /// mustExceed보다 큰 칸이 없거나 직선거리 조건을 만족하는 칸이 없으면 -1.
        /// </summary>
        private static int FarthestFromOthers(MazeData maze, int[][] dist, int[] chosen, int count, int skip,
            int mustExceed, float minStraight, DeterministicRandom rng)
        {
            int width = maze.Width;
            float minStraightSq = minStraight * minStraight;
            int best = mustExceed;
            int pick = -1;
            int ties = 0;

            for (int y = 0; y < maze.Height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int c = y * width + x;
                    int nearest = int.MaxValue;
                    bool tooClose = false;

                    for (int j = 0; j < count; j++)
                    {
                        if (j == skip) continue;

                        int d = dist[j][c];
                        if (d < nearest) nearest = d;

                        int dx = x - chosen[j] % width;
                        int dy = y - chosen[j] / width;
                        if (dx * dx + dy * dy < minStraightSq)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    if (tooClose) continue;

                    if (nearest > best)
                    {
                        best = nearest;
                        pick = c;
                        ties = 1;
                    }
                    else if (nearest == best && pick >= 0)
                    {
                        ties++;
                        if (rng.NextInt(0, ties) == 0) pick = c;
                    }
                }
            }
            return pick;
        }

        private static int NearestOtherDistance(int[][] dist, int count, int self, int cell)
        {
            int nearest = int.MaxValue;
            for (int j = 0; j < count; j++)
                if (j != self && dist[j][cell] < nearest) nearest = dist[j][cell];
            return nearest;
        }

        /// <summary>from에서 모든 칸까지의 경로 거리(칸 수). 결과는 maze.Index(x, y)로 접근한다.</summary>
        public static int[] PathDistances(MazeData maze, Vector2Int from)
        {
            return PathDistances(maze, maze.Index(from.x, from.y));
        }

        private static int[] PathDistances(MazeData maze, int start)
        {
            int width = maze.Width;
            var dist = new int[maze.Cells.Length];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;

            // 바깥 테두리는 항상 벽이라 인덱스가 다른 줄로 넘어가는 일이 없다.
            var neighborOffset = new[] { width, 1, -width, -1 };
            var queue = new int[dist.Length];
            int head = 0, tail = 0;

            dist[start] = 0;
            queue[tail++] = start;
            while (head < tail)
            {
                int cell = queue[head++];
                WallSide walls = maze.Cells[cell];
                for (int s = 0; s < 4; s++)
                {
                    if ((walls & Sides[s]) != 0) continue;
                    int next = cell + neighborOffset[s];
                    if (dist[next] >= 0) continue;
                    dist[next] = dist[cell] + 1;
                    queue[tail++] = next;
                }
            }
            return dist;
        }

        /// <summary>주어진 시작 칸들 사이의 가장 가까운 두 칸의 경로 거리(칸 수). 칸이 하나뿐이면 int.MaxValue.</summary>
        public static int MinPairwisePathDistance(MazeData maze, IReadOnlyList<Vector2Int> cells)
        {
            int min = int.MaxValue;
            for (int i = 0; i < cells.Count; i++)
            {
                int[] dist = PathDistances(maze, cells[i]);
                for (int j = i + 1; j < cells.Count; j++)
                    min = Mathf.Min(min, dist[maze.Index(cells[j].x, cells[j].y)]);
            }
            return min;
        }

        public static Vector3 CellCenter(Vector2Int cell, float cellSize)
        {
            return new Vector3((cell.x + 0.5f) * cellSize, 0.1f, (cell.y + 0.5f) * cellSize);
        }

        /// <summary>월드 좌표가 속한 칸(CellCenter의 반대).</summary>
        public static Vector2Int CellAt(Vector3 position, float cellSize)
        {
            return new Vector2Int(Mathf.FloorToInt(position.x / cellSize), Mathf.FloorToInt(position.z / cellSize));
        }

        /// <summary>벽이 없는 첫 방향을 바라보는 회전(시작하자마자 벽만 보이지 않도록).</summary>
        public static Quaternion FacingOpenSide(MazeData maze, Vector2Int cell)
        {
            foreach (var side in Sides)
            {
                if (maze.HasWall(cell.x, cell.y, side)) continue;
                MazeData.GetOffset(side, out int dx, out int dy);
                return Quaternion.LookRotation(new Vector3(dx, 0f, dy));
            }
            return Quaternion.identity;
        }
    }
}
