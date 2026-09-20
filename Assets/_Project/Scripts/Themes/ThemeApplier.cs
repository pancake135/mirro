using UnityEngine;
using UnityEngine.Rendering;
using Mirro.Maze;

namespace Mirro.Themes
{
    /// <summary>선택된 테마를 미로 머티리얼과 씬 환경(조명/안개/배경색)에 반영한다.</summary>
    public static class ThemeApplier
    {
        public static void ApplyToBuilder(MazeThemeConfig theme, MazeBuilder builder)
        {
            builder.wallMaterial = MazeBuilder.CreateColoredMaterial(theme.wallColor);
            builder.floorMaterial = MazeBuilder.CreateColoredMaterial(theme.groundColor);
        }

        public static void ApplyEnvironment(MazeThemeConfig theme, Camera camera, Light sun)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = theme.ambientColor;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = theme.skyColor;
            RenderSettings.fogDensity = 0.015f;

            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = theme.skyColor;
            }

            if (sun != null)
                sun.color = theme.sunColor;
        }
    }
}
