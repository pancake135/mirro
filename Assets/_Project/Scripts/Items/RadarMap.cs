using UnityEngine;
using Mirro.Maze;

namespace Mirro.Items
{
    /// <summary>레이더 미니맵의 바탕: 미로 벽을 그린 텍스처(미로는 모든 피어가 로컬에 갖고 있어 네트워크 없이 굽는다).</summary>
    public static class RadarMap
    {
        public static readonly Color32 Background = new Color32(13, 18, 23, 215);
        public static readonly Color32 Wall = new Color32(214, 230, 240, 255);

        public static int PixelsPerCell(int mazeWidth) => Mathf.Max(3, 480 / Mathf.Max(1, mazeWidth));

        public static Texture2D Bake(MazeData maze)
        {
            int px = PixelsPerCell(maze.Width);
            int width = maze.Width * px + 1;
            int height = maze.Height * px + 1;
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Background;

            int thickness = px >= 8 ? 2 : 1;
            for (int y = 0; y < maze.Height; y++)
            {
                for (int x = 0; x < maze.Width; x++)
                {
                    int x0 = x * px, y0 = y * px, x1 = x0 + px, y1 = y0 + px;
                    if (maze.HasWall(x, y, WallSide.South)) Horizontal(pixels, width, height, x0, x1, y0, thickness);
                    if (maze.HasWall(x, y, WallSide.North)) Horizontal(pixels, width, height, x0, x1, y1, thickness);
                    if (maze.HasWall(x, y, WallSide.West)) Vertical(pixels, width, height, x0, y0, y1, thickness);
                    if (maze.HasWall(x, y, WallSide.East)) Vertical(pixels, width, height, x1, y0, y1, thickness);
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static void Horizontal(Color32[] pixels, int width, int height, int xFrom, int xTo, int y, int thickness)
        {
            for (int x = xFrom; x <= xTo; x++)
                for (int t = 0; t < thickness; t++)
                    Set(pixels, width, height, x, y + t - thickness / 2);
        }

        private static void Vertical(Color32[] pixels, int width, int height, int x, int yFrom, int yTo, int thickness)
        {
            for (int y = yFrom; y <= yTo; y++)
                for (int t = 0; t < thickness; t++)
                    Set(pixels, width, height, x + t - thickness / 2, y);
        }

        private static void Set(Color32[] pixels, int width, int height, int x, int y)
        {
            if (x >= 0 && x < width && y >= 0 && y < height) pixels[y * width + x] = Wall;
        }
    }
}
