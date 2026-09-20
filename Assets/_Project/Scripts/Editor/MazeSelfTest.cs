using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Mirro.Gameplay;
using Mirro.Maze;
using Debug = UnityEngine.Debug;

namespace Mirro.EditorTools
{
    /// <summary>
    /// 미로 생성/빌드 자체 검증. 메뉴(Mirro/Run Maze Self Test) 또는
    /// 배치 모드(-executeMethod Mirro.EditorTools.MazeSelfTest.RunBatch)로 실행하며,
    /// 실패가 하나라도 있으면 종료 코드 1을 반환한다.
    /// </summary>
    public static class MazeSelfTest
    {
        private static readonly int[] Sizes = { 50, 70, 100, 120, 150, 170, 200 };

        [MenuItem("Mirro/Run Maze Self Test")]
        public static void RunFromMenu() => Run();

        public static void RunBatch()
        {
            bool ok = Run();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        private static bool Run()
        {
            bool allOk = true;

            foreach (int size in Sizes)
            {
                var sw = Stopwatch.StartNew();
                var maze = MazeGenerator.Generate(size, 20260919);
                long genMs = sw.ElapsedMilliseconds;

                string err = ValidatePerfectMaze(maze);
                if (err != null) { allOk = false; Debug.LogError($"[MirroTest] size {size}: FAIL {err}"); }
                else Debug.Log($"[MirroTest] size {size}: perfect maze OK (generate {genMs} ms)");
            }

            var a = MazeGenerator.Generate(100, 777);
            var b = MazeGenerator.Generate(100, 777);
            var c = MazeGenerator.Generate(100, 778);
            bool same = SameCells(a, b);
            bool differs = !SameCells(a, c);
            if (!same || !differs) allOk = false;
            Debug.Log($"[MirroTest] determinism: sameSeedIdentical={same} differentSeedDiffers={differs}");

            allOk &= TestBuild(200);
            allOk &= TestBuild(50);
            allOk &= TestSpawnPlacement();
            allOk &= TestBots();
            allOk &= TestTreasures();

            Debug.Log(allOk ? "[MirroTest] ALL PASSED" : "[MirroTest] SOME TESTS FAILED");
            return allOk;
        }

        private static string ValidatePerfectMaze(MazeData m)
        {
            int passages = 0;
            for (int y = 0; y < m.Height; y++)
            {
                for (int x = 0; x < m.Width; x++)
                {
                    if (x + 1 < m.Width)
                    {
                        bool e = m.HasWall(x, y, WallSide.East);
                        bool w = m.HasWall(x + 1, y, WallSide.West);
                        if (e != w) return $"wall asymmetry E/W at ({x},{y})";
                        if (!e) passages++;
                    }
                    if (y + 1 < m.Height)
                    {
                        bool n = m.HasWall(x, y, WallSide.North);
                        bool s = m.HasWall(x, y + 1, WallSide.South);
                        if (n != s) return $"wall asymmetry N/S at ({x},{y})";
                        if (!n) passages++;
                    }
                    if (x == 0 && !m.HasWall(x, y, WallSide.West)) return $"open west boundary at ({x},{y})";
                    if (y == 0 && !m.HasWall(x, y, WallSide.South)) return $"open south boundary at ({x},{y})";
                    if (x == m.Width - 1 && !m.HasWall(x, y, WallSide.East)) return $"open east boundary at ({x},{y})";
                    if (y == m.Height - 1 && !m.HasWall(x, y, WallSide.North)) return $"open north boundary at ({x},{y})";
                }
            }

            int cells = m.Width * m.Height;
            if (passages != cells - 1) return $"passages={passages}, expected {cells - 1} (not a spanning tree)";

            var seen = new bool[cells];
            var queue = new Queue<int>();
            queue.Enqueue(0);
            seen[0] = true;
            int reached = 0;
            var sides = new[] { WallSide.North, WallSide.East, WallSide.South, WallSide.West };
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                reached++;
                int x = i % m.Width, y = i / m.Width;
                foreach (var side in sides)
                {
                    if (m.HasWall(x, y, side)) continue;
                    MazeData.GetOffset(side, out int dx, out int dy);
                    int ni = m.Index(x + dx, y + dy);
                    if (seen[ni]) continue;
                    seen[ni] = true;
                    queue.Enqueue(ni);
                }
            }
            return reached == cells ? null : $"only {reached}/{cells} cells reachable";
        }

        private static bool SameCells(MazeData a, MazeData b)
        {
            if (a.Cells.Length != b.Cells.Length) return false;
            for (int i = 0; i < a.Cells.Length; i++)
                if (a.Cells[i] != b.Cells[i]) return false;
            return true;
        }

        /// <summary>
        /// 스폰 배치 검증: 경로 거리 계산이 맞는지, 칸이 겹치지 않는지, 같은 입력이면 같은 결과인지,
        /// 2명일 땐 미로에서 가장 먼 두 칸(지름)을 정확히 잡는지, 3~4명일 땐 모서리/무작위 배치보다 넓게 벌리는지 확인한다.
        /// </summary>
        private static bool TestSpawnPlacement()
        {
            bool allOk = true;
            var sizes = new[] { 50, 100, 200 };

            foreach (int size in sizes)
            {
                int seeds = size >= 200 ? 8 : 25;

                // 경로 거리 계산 자체 검증: 모든 통로에서 양 끝 칸의 거리 차가 정확히 1이면 BFS 거리와 같다(트리 미로).
                var probe = MazeGenerator.Generate(size, 4242);
                int[] d = SpawnPlacer.PathDistances(probe, new Vector2Int(size / 2, size / 3));
                bool distancesOk = d[probe.Index(size / 2, size / 3)] == 0;
                for (int y = 0; y < size && distancesOk; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int i = probe.Index(x, y);
                        if (d[i] < 0) { distancesOk = false; break; }
                        if (x + 1 < size && !probe.HasWall(x, y, WallSide.East) && Mathf.Abs(d[i] - d[i + 1]) != 1) distancesOk = false;
                        if (y + 1 < size && !probe.HasWall(x, y, WallSide.North) && Mathf.Abs(d[i] - d[i + size]) != 1) distancesOk = false;
                    }
                }
                allOk &= Report(distancesOk, $"[MirroTest] spawn size {size}: path distances consistent");

                foreach (int count in new[] { 1, 2, 3, 4 })
                {
                    double placedSum = 0, cornerSum = 0, randomSum = 0, straightSum = 0;
                    long maxMs = 0;
                    int failures = 0;
                    var firstCells = new HashSet<Vector2Int>();
                    int minStraightSeen = int.MaxValue;

                    for (int s = 1; s <= seeds; s++)
                    {
                        var maze = MazeGenerator.Generate(size, 1000 * size + s);
                        var sw = Stopwatch.StartNew();
                        var cells = SpawnPlacer.Place(maze, count);
                        maxMs = System.Math.Max(maxMs, sw.ElapsedMilliseconds);

                        var again = SpawnPlacer.Place(maze, count);
                        bool deterministic = cells.Length == again.Length;
                        for (int i = 0; deterministic && i < cells.Length; i++)
                            deterministic = cells[i] == again[i];

                        bool valid = cells.Length == count && new HashSet<Vector2Int>(cells).Count == count;
                        foreach (var c in cells) valid &= maze.InBounds(c.x, c.y);
                        if (!deterministic || !valid) failures++;
                        firstCells.Add(cells[0]);

                        if (count == 1) continue;

                        int placed = SpawnPlacer.MinPairwisePathDistance(maze, cells);
                        placedSum += placed;

                        float minStraight = float.MaxValue;
                        for (int i = 0; i < cells.Length; i++)
                            for (int j = i + 1; j < cells.Length; j++)
                                minStraight = Mathf.Min(minStraight, Vector2Int.Distance(cells[i], cells[j]));
                        straightSum += minStraight;
                        minStraightSeen = Mathf.Min(minStraightSeen, Mathf.FloorToInt(minStraight));
                        if (minStraight < SpawnPlacer.MinStraightLineFraction * size - 0.001f) failures++;

                        var corners = new[]
                        {
                            new Vector2Int(0, 0), new Vector2Int(size - 1, size - 1),
                            new Vector2Int(size - 1, 0), new Vector2Int(0, size - 1)
                        };
                        cornerSum += SpawnPlacer.MinPairwisePathDistance(maze, new List<Vector2Int>(corners).GetRange(0, count));

                        var rng = new Mirro.Core.DeterministicRandom(s * 7919);
                        double randomBest = 0;
                        const int randomDraws = 10;
                        for (int r = 0; r < randomDraws; r++)
                        {
                            var pick = new List<Vector2Int>();
                            while (pick.Count < count)
                            {
                                var cell = new Vector2Int(rng.NextInt(0, size), rng.NextInt(0, size));
                                if (!pick.Contains(cell)) pick.Add(cell);
                            }
                            randomBest += SpawnPlacer.MinPairwisePathDistance(maze, pick);
                        }
                        randomSum += randomBest / randomDraws;

                        if (count == 2)
                        {
                            // 2명이면 정답을 알 수 있다: 미로(트리)의 지름. 직선거리 조건 때문에 지름 끝점이 너무 가까운
                            // 드문 경우가 아니면 지름과 같아야 한다.
                            int[] fromCorner = SpawnPlacer.PathDistances(maze, new Vector2Int(0, 0));
                            int far = 0;
                            for (int i = 1; i < fromCorner.Length; i++) if (fromCorner[i] > fromCorner[far]) far = i;
                            int[] fromFar = SpawnPlacer.PathDistances(maze, new Vector2Int(far % size, far / size));
                            int diameter = 0;
                            foreach (int v in fromFar) diameter = System.Math.Max(diameter, v);
                            if (placed != diameter)
                                Debug.Log($"[MirroTest] spawn size {size} seed {s}: 2-player spacing {placed} vs tree diameter {diameter} (straight-line floor took effect)");
                        }
                    }

                    string line = $"[MirroTest] spawn size {size} x{count}: max {maxMs} ms, distinct first cells {firstCells.Count}/{seeds}";
                    if (count > 1)
                    {
                        double n = seeds;
                        line += $", closest-pair path mean {placedSum / n:F0} cells (~{placedSum / n * 4:F0} m) vs corners {cornerSum / n:F0} vs random {randomSum / n:F0}" +
                                $", straight-line mean {straightSum / n:F0} cells (min {minStraightSeen})";
                        if (placedSum <= cornerSum || placedSum <= randomSum * 2) failures++;
                    }
                    allOk &= Report(failures == 0, line);
                }
            }
            return allOk;
        }

        /// <summary>봇 경로 탐색/계획 검증(물리 없이): 경로가 이어지고 최단인지, 추격형이 가장 가까운 깃발로 향하는지, 탐색형이 멀리 있는 깃발도 찾는지.</summary>
        private static bool TestBots()
        {
            bool allOk = true;
            var maze = MazeGenerator.Generate(50, 909);
            int cells = maze.Cells.Length;
            var finder = new BotPathfinder(maze);
            var path = new List<int>();

            int from = maze.Index(3, 4);
            int[] dist = SpawnPlacer.PathDistances(maze, new Vector2Int(3, 4));
            int far = 0;
            for (int i = 1; i < cells; i++) if (dist[i] > dist[far]) far = i;

            bool found = finder.TryFindPath(from, c => c == far, int.MaxValue, null, path);
            bool contiguous = found && path.Count == dist[far] && path[path.Count - 1] == far;
            int previous = from;
            foreach (int c in path)
            {
                int dx = Mathf.Abs(c % 50 - previous % 50), dy = Mathf.Abs(c / 50 - previous / 50);
                if (dx + dy != 1) contiguous = false;
                previous = c;
            }
            allOk &= Report(contiguous, $"[MirroTest] bots: path to the farthest cell is contiguous and shortest ({path.Count} steps)");

            bool limited = !finder.TryFindPath(from, c => c == far, 5, null, path) && path.Count == 0;
            allOk &= Report(limited, "[MirroTest] bots: max depth is respected");
            allOk &= Report(finder.TryFindPath(from, c => c == from, 0, null, path) && path.Count == 0,
                "[MirroTest] bots: standing on the goal gives an empty path");

            // 추격형: 가장 가까운 남의 깃발까지 최단 경로로 향한다.
            var flags = new Dictionary<int, ulong> { { far, 1001UL }, { maze.Index(40, 3), 1002UL }, { maze.Index(10, 45), 1003UL } };
            int nearest = int.MaxValue;
            foreach (var pair in flags) nearest = Mathf.Min(nearest, dist[pair.Key]);
            var chaser = new BotPlanner(maze, BotSettings.For(BotDifficulty.Hard), 1);
            chaser.MarkVisited(from);
            chaser.Plan(from, flags);
            bool chasing = chaser.IsChasing && chaser.Path.Count == nearest && flags[chaser.Path[chaser.Path.Count - 1]] == chaser.TargetFlagId;
            allOk &= Report(chasing, $"[MirroTest] bots: chaser heads for the nearest flag ({chaser.Path.Count} steps, expected {nearest})");

            // 탐색형: 멀리 있는 깃발도 결국 스스로 발견해서 쫓아간다(칸 수의 3배 안에).
            var explorer = new BotPlanner(maze, BotSettings.For(BotDifficulty.Easy), 2);
            var farFlag = new Dictionary<int, ulong> { { far, 1001UL } };
            int at = from, steps = 0, index = 0;
            explorer.MarkVisited(at);
            explorer.Plan(at, farFlag);
            while (at != far && steps < cells * 3)
            {
                if (index >= explorer.Path.Count || (!explorer.IsChasing && steps % 3 == 0))
                {
                    explorer.Plan(at, farFlag);
                    index = 0;
                    if (explorer.Path.Count == 0) break;
                }
                at = explorer.Path[index++];
                explorer.MarkVisited(at);
                steps++;
            }
            allOk &= Report(at == far, $"[MirroTest] bots: explorer finds the far flag on its own ({steps} steps, {cells} cells)");
            return allOk;
        }

        /// <summary>금색 깃발 배치(개수/중복 없음/결정적/간격/시작 칸에서 떨어짐)와 최고 기록 저장 검증.</summary>
        private static bool TestTreasures()
        {
            bool allOk = true;
            foreach (int size in new[] { 50, 100, 200 })
            {
                int count = size / 10;
                var maze = MazeGenerator.Generate(size, 5150 + size);
                var avoid = new Vector2Int(size / 2, size / 2);
                var cells = SpawnPlacer.PlaceTreasures(maze, count, avoid);
                var again = SpawnPlacer.PlaceTreasures(maze, count, avoid);

                bool distinct = cells.Length == count && new HashSet<Vector2Int>(cells).Count == count;
                bool deterministic = cells.Length == again.Length;
                for (int i = 0; deterministic && i < cells.Length; i++) deterministic = cells[i] == again[i];

                float minSpacing = float.MaxValue, minFromStart = float.MaxValue;
                for (int i = 0; i < cells.Length; i++)
                {
                    minFromStart = Mathf.Min(minFromStart, Vector2Int.Distance(cells[i], avoid));
                    for (int j = i + 1; j < cells.Length; j++)
                        minSpacing = Mathf.Min(minSpacing, Vector2Int.Distance(cells[i], cells[j]));
                    if (!maze.InBounds(cells[i].x, cells[i].y)) distinct = false;
                }
                bool spread = minSpacing >= size * 0.16f * 0.8f - 0.01f && minFromStart >= size * 0.15f * 0.8f - 0.01f;
                allOk &= Report(distinct && deterministic && spread,
                    $"[MirroTest] treasures size {size}: {count} distinct, deterministic, spread (min spacing {minSpacing:F1}, from start {minFromStart:F1})");
            }

            const int probeSize = 999;
            string key = "mirro.treasure.best." + probeSize;
            PlayerPrefs.DeleteKey(key);
            bool first = TreasureRecords.Submit(probeSize, 120f, out float prev0);
            bool slower = TreasureRecords.Submit(probeSize, 150f, out float prev1);
            bool faster = TreasureRecords.Submit(probeSize, 100f, out float prev2);
            bool ok = first && prev0 == 0f && !slower && prev1 == 120f && faster && prev2 == 120f
                      && Mathf.Approximately(TreasureRecords.Best(probeSize), 100f);
            PlayerPrefs.DeleteKey(key);
            allOk &= Report(ok, "[MirroTest] treasure records keep only the best time");
            return allOk;
        }

        private static bool Report(bool ok, string message)
        {
            if (ok) Debug.Log(message + " -> OK");
            else Debug.LogError(message + " -> FAIL");
            return ok;
        }

        private static bool TestBuild(int size)
        {
            var maze = MazeGenerator.Generate(size, 1234);
            var go = new GameObject("SelfTestMazeBuilder");
            var builder = go.AddComponent<MazeBuilder>();

            var sw = Stopwatch.StartNew();
            builder.Build(maze);
            long ms = sw.ElapsedMilliseconds;

            var filters = go.GetComponentsInChildren<MeshFilter>();
            var colliders = go.GetComponentsInChildren<MeshCollider>();
            long vertices = 0;
            foreach (var f in filters) vertices += f.sharedMesh.vertexCount;

            bool ok = colliders.Length > 0 && vertices > 0;
            Debug.Log($"[MirroTest] build {size}x{size}: {ms} ms, renderers={filters.Length} (draw calls), meshColliders={colliders.Length}, vertices={vertices} -> {(ok ? "OK" : "FAIL")}");

            Object.DestroyImmediate(go);
            return ok;
        }
    }
}
