using System;

namespace Mirro.Maze
{
    [Flags]
    public enum WallSide : byte
    {
        None = 0,
        North = 1 << 0,
        East = 1 << 1,
        South = 1 << 2,
        West = 1 << 3,
        All = North | East | South | West
    }

    /// <summary>
    /// 미로의 논리적 표현. Cells[y * Width + x]의 각 비트는 해당 방향에 벽이
    /// "존재함"을 의미한다 (생성 시 모두 세워진 상태에서 통로를 뚫어가며 비트를 지운다).
    /// </summary>
    public class MazeData
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int Seed;
        public readonly WallSide[] Cells;

        public MazeData(int width, int height, int seed)
        {
            Width = width;
            Height = height;
            Seed = seed;
            Cells = new WallSide[width * height];
        }

        public int Index(int x, int y) => y * Width + x;

        public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        public bool HasWall(int x, int y, WallSide side) => (Cells[Index(x, y)] & side) != 0;

        public static WallSide Opposite(WallSide side)
        {
            switch (side)
            {
                case WallSide.North: return WallSide.South;
                case WallSide.South: return WallSide.North;
                case WallSide.East: return WallSide.West;
                case WallSide.West: return WallSide.East;
                default: return WallSide.None;
            }
        }

        public static void GetOffset(WallSide side, out int dx, out int dy)
        {
            switch (side)
            {
                case WallSide.North: dx = 0; dy = 1; return;
                case WallSide.South: dx = 0; dy = -1; return;
                case WallSide.East: dx = 1; dy = 0; return;
                case WallSide.West: dx = -1; dy = 0; return;
                default: dx = 0; dy = 0; return;
            }
        }
    }
}
