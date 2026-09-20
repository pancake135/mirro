using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Mirro.Maze;
using Mirro.Themes;
using Mirro.UI;
using Object = UnityEngine.Object;

namespace Mirro.EditorTools
{
    /// <summary>
    /// 메뉴 UI와 테마별 미로 화면을 PNG로 렌더링하는 검증 도구. 게임 창 없이도 결과를 눈으로 확인할 수 있다.
    /// 배치 모드: -executeMethod Mirro.EditorTools.VisualCapture.CaptureBatch -shotDir &lt;출력 폴더&gt;
    /// (그래픽 장치가 필요하므로 -nographics 없이 실행해야 한다.)
    /// </summary>
    public static class VisualCapture
    {
        private const int Width = 1920;
        private const int Height = 1080;

        [MenuItem("Mirro/Capture Screenshots")]
        public static void CaptureFromMenu() => CaptureAll(Path.Combine(Application.dataPath, "../Temp/Shots"));

        public static void CaptureBatch()
        {
            try
            {
                CaptureAll(ReadShotDir());
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        private static string ReadShotDir()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-shotDir") return args[i + 1];
            return Path.Combine(Application.dataPath, "../Temp/Shots");
        }

        private static void CaptureAll(string dir)
        {
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };

            CaptureMenu(rt, dir);
            foreach (var theme in ThemeLibrary.All)
                CaptureMaze(rt, dir, theme);

            rt.Release();
            Object.DestroyImmediate(rt);
            Debug.Log("[MirroShot] Captured to " + dir);
        }

        private static void CaptureMenu(RenderTexture rt, string dir)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = NewCamera(rt);
            cam.backgroundColor = Color.black;

            var ui = new GameObject("MainMenu").AddComponent<MainMenuUI>();
            ui.Build();
            ui.Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            ui.Canvas.worldCamera = cam;
            ui.Canvas.planeDistance = 1f;
            ui.Canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            ui.ShowThemeScreen();
            Save(cam, rt, Path.Combine(dir, "menu_theme.png"));

            ui.ShowSizeScreen(ThemeLibrary.Get(SeasonTheme.Spring));
            ui.SelectSize(100);
            Save(cam, rt, Path.Combine(dir, "menu_size_spring.png"));

            ui.ShowSizeScreen(ThemeLibrary.Get(SeasonTheme.Winter));
            Save(cam, rt, Path.Combine(dir, "menu_size_winter_unselected.png"));
        }

        private static void CaptureMaze(RenderTexture rt, string dir, MazeThemeConfig theme)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            const int size = 20;
            var maze = MazeGenerator.Generate(size, 42);

            var builder = new GameObject("Maze").AddComponent<MazeBuilder>();
            ThemeApplier.ApplyToBuilder(theme, builder);
            builder.Build(maze);

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cam = NewCamera(rt);
            ThemeApplier.ApplyEnvironment(theme, cam, sun);

            cam.fieldOfView = 75f;
            cam.transform.position = new Vector3(builder.cellSize * 0.5f, 1.6f, builder.cellSize * 0.5f);
            cam.transform.rotation = Quaternion.LookRotation(maze.HasWall(0, 0, WallSide.North) ? Vector3.right : Vector3.forward);
            Save(cam, rt, Path.Combine(dir, $"maze_{theme.englishName}_eye.png"));

            RenderSettings.fog = false;
            float extent = size * builder.cellSize;
            cam.orthographic = true;
            cam.orthographicSize = extent * 0.5f * 1.05f;
            cam.transform.position = new Vector3(extent * 0.5f, 60f, extent * 0.5f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Save(cam, rt, Path.Combine(dir, $"maze_{theme.englishName}_top.png"));
        }

        private static Camera NewCamera(RenderTexture rt)
        {
            var go = new GameObject("CaptureCamera");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.targetTexture = rt;
            return cam;
        }

        private static void Save(Camera cam, RenderTexture rt, string path)
        {
            for (int i = 0; i < 3; i++)
            {
                Canvas.ForceUpdateCanvases();
                cam.Render();
            }

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[MirroShot] " + path);
        }
    }
}
