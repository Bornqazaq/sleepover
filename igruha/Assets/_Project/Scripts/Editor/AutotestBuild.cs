using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Тестовый билд для стенда автопрогона (<c>tools/autorun-*.sh</c>):
    /// все сцены из Build Settings, Development, в <c>Builds/Autotest</c>.
    /// Отдельно от раздаточного билда, чтобы стенд не раздавали по ошибке.
    ///
    /// Зовётся и из меню, и из командной строки, когда редактор закрыт и
    /// MCP недоступен:
    /// <c>Unity -batchmode -quit -projectPath igruha -buildTarget OSXUniversal
    /// -executeMethod Igruha.EditorTools.AutotestBuild.BuildMac</c>.
    /// </summary>
    public static class AutotestBuild
    {
        private const string OutputPath = "Builds/Autotest/sleepover.app";

        [MenuItem("Igruha/Стенд/Собрать тестовый билд macOS")]
        public static void BuildMac()
        {
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    scenes.Add(scene.path);
                }
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"AutotestBuild: {summary.result}, ошибок {summary.totalErrors}, " +
                      $"предупреждений {summary.totalWarnings}, {summary.totalTime.TotalSeconds:F0} с → {OutputPath}");

            if (Application.isBatchMode && summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
