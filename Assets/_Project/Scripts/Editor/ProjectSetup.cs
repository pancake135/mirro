using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Mirro.Core;
using Mirro.Themes;
using Mirro.UI;

namespace Mirro.EditorTools
{
    /// <summary>
    /// 프로젝트 초기 구성 도구(여러 번 실행해도 안전). 메뉴(Mirro/Setup Project) 또는
    /// 배치 모드(-executeMethod Mirro.EditorTools.ProjectSetup.SetupAll)로 실행한다.
    /// 테마 에셋 생성, 씬 정리(MainMenu/Game), 빌드 설정 등록을 수행한다.
    /// </summary>
    public static class ProjectSetup
    {
        private const string ScenesDir = "Assets/_Project/Scenes";
        private const string GameScenePath = ScenesDir + "/Game.unity";
        private const string MenuScenePath = ScenesDir + "/MainMenu.unity";
        private const string TemplateScenePath = "Assets/Scenes/SampleScene.unity";
        private const string ThemesDir = "Assets/_Project/Resources/Themes";

        [MenuItem("Mirro/Setup Project")]
        public static void SetupAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureFolder("Assets/_Project/Resources");
            EnsureFolder(ThemesDir);
            EnsureFolder("Assets/_Project/Resources/Materials");
            CreateThemeAssets();
            CreateMazeMaterial();
            NetworkSetup.CreateAll();

            MigrateGameScene();
            ConfigureGameScene();
            CreateMenuScene();
            ConfigureBuildSettings();

            AssetDatabase.SaveAssets();
            Debug.Log("[Mirro] Project setup complete.");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        private static void CreateThemeAssets()
        {
            foreach (SeasonTheme season in System.Enum.GetValues(typeof(SeasonTheme)))
            {
                string path = $"{ThemesDir}/{season}.asset";
                if (AssetDatabase.LoadAssetAtPath<MazeThemeConfig>(path) != null) continue;
                AssetDatabase.CreateAsset(ThemeDefaults.Create(season), path);
            }
        }

        private static void CreateMazeMaterial()
        {
            const string path = "Assets/_Project/Resources/Materials/MazeLit.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[Mirro] URP Lit shader not found; cannot create MazeLit material.");
                return;
            }
            AssetDatabase.CreateAsset(new Material(shader), path);
        }

        private static void MigrateGameScene()
        {
            bool hasTemplate = AssetDatabase.LoadAssetAtPath<SceneAsset>(TemplateScenePath) != null;
            bool hasGame = AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath) != null;
            if (!hasTemplate || hasGame) return;

            string error = AssetDatabase.MoveAsset(TemplateScenePath, GameScenePath);
            if (!string.IsNullOrEmpty(error))
                Debug.LogError("[Mirro] Failed to move template scene: " + error);
            else if (AssetDatabase.FindAssets(string.Empty, new[] { "Assets/Scenes" }).Length == 0)
                AssetDatabase.DeleteAsset("Assets/Scenes");
        }

        private static void ConfigureGameScene()
        {
            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);

            // 플레이어 카메라는 런타임에 코드로 생성되므로 템플릿의 Main Camera는 제거한다.
            foreach (var cam in Object.FindObjectsByType<Camera>())
                Object.DestroyImmediate(cam.gameObject);

            if (Object.FindAnyObjectByType<MazeGameBootstrap>() == null)
            {
                var go = new GameObject("MazeGameBootstrap");
                go.AddComponent<MazeGameBootstrap>();
            }

            // 안개를 런타임에만 켜면 빌드에서 안개 셰이더 변형이 제거될 수 있어 씬에도 미리 켜둔다.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.015f;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void CreateMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.11f, 0.13f);
            camGo.AddComponent<AudioListener>();

            new GameObject("MainMenu").AddComponent<MainMenuUI>();

            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }

        private static void ConfigureBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MenuScenePath, true),
                new EditorBuildSettingsScene(GameScenePath, true)
            };
        }
    }
}
