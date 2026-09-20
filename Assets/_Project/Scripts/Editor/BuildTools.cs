using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Mirro.EditorTools
{
    /// <summary>
    /// 개발용 Windows 빌드. 배치 모드: -executeMethod Mirro.EditorTools.BuildTools.BuildWindowsDevBatch -buildPath &lt;폴더&gt;
    /// 빌드에서만 드러나는 문제(셰이더 제거 등)를 확인하고, 멀티플레이 테스트용 실행 파일을 만드는 데 쓴다.
    /// </summary>
    public static class BuildTools
    {
        [MenuItem("Mirro/Build Windows Dev Player")]
        public static void BuildFromMenu() => Build(Application.dataPath + "/../Builds/Dev");

        public static void BuildWindowsDevBatch()
        {
            bool ok = Build(ReadArg("-buildPath") ?? Application.dataPath + "/../Builds/Dev");
            EditorApplication.Exit(ok ? 0 : 1);
        }

        private static bool Build(string folder)
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/MainMenu.unity", "Assets/_Project/Scenes/Game.unity" },
                locationPathName = folder + "/Mirro.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[MirroBuild] {report.summary.result} in {report.summary.totalTime.TotalSeconds:F0}s, errors={report.summary.totalErrors}, warnings={report.summary.totalWarnings}, size={report.summary.totalSize / (1024 * 1024)} MB -> {options.locationPathName}");
            return report.summary.result == BuildResult.Succeeded;
        }

        private static string ReadArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
