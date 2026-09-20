using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Раздаточный билд — тот, который уходит друзьям.
    ///
    /// Отдельно от <see cref="AutotestBuild"/>, и это не дублирование.
    /// Стенд собирается в режиме Development: в нём висит счётчик кадров,
    /// работает профайлер и открыт порт отладчика. Раздать такой билд значит
    /// раздать не игру, а стенд — ровно поэтому они разведены по разным папкам
    /// с самого начала.
    ///
    /// Собирает обе платформы одним запуском: на игру садятся и с Windows, и с
    /// Mac, а собирать дважды вручную — лишний повод забыть одну из них.
    /// Модуль Windows в редакторе установлен, кросс-сборка с macOS работает.
    ///
    /// Из терминала:
    /// <c>Unity -batchmode -nographics -quit -projectPath igruha
    /// -executeMethod Igruha.EditorTools.ReleaseBuild.BuildAll</c>
    /// </summary>
    public static class ReleaseBuild
    {
        private const string Root = "Builds/Release";
        private const string MacPath = Root + "/Mac/Komnata.app";
        private const string WindowsPath = Root + "/Windows/Komnata.exe";

        [MenuItem("Igruha/Стенд/Собрать раздаточный билд (Mac + Windows)")]
        public static void BuildAll()
        {
            bool ok = Build(BuildTarget.StandaloneOSX, MacPath, "macOS");
            ok &= Build(BuildTarget.StandaloneWindows64, WindowsPath, "Windows");

            if (Application.isBatchMode && !ok) EditorApplication.Exit(1);
        }

        [MenuItem("Igruha/Стенд/Собрать раздаточный билд macOS")]
        public static void BuildMac()
        {
            if (!Build(BuildTarget.StandaloneOSX, MacPath, "macOS") && Application.isBatchMode) EditorApplication.Exit(1);
        }

        [MenuItem("Igruha/Стенд/Собрать раздаточный билд Windows")]
        public static void BuildWindows()
        {
            if (!Build(BuildTarget.StandaloneWindows64, WindowsPath, "Windows") && Application.isBatchMode) EditorApplication.Exit(1);
        }

        private static bool Build(BuildTarget target, string path, string label)
        {
            // Платформу надо сделать активной до сборки. Иначе Unity собирает
            // против движка текущей платформы и молча не выдаёт ничего: ни
            // файла, ни отчёта, ни ошибки. Так и выглядела первая попытка
            // собрать Windows следом за macOS — в логе линковка шла против
            // MacStandaloneSupport, а папка Windows оставалась пустой.
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                Debug.Log($"[Билд] переключаю платформу на {target} — первый раз это долго, идёт переимпорт.");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(target), target))
                {
                    Debug.LogError($"[Билд] {label}: платформа {target} не включилась. "
                                   + "Проверь, что модуль сборки установлен в Unity Hub.");
                    return false;
                }
            }

            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled) scenes.Add(scene.path);
            }

            if (scenes.Count == 0)
            {
                Debug.LogError("[Билд] В Build Settings нет ни одной включённой сцены.");
                return false;
            }

            // Пересборка поверх прошлой оставляет файлы, которых в новой уже нет:
            // раздатка обязана быть ровно тем, что собрали сейчас.
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = path,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            bool ok = summary.result == BuildResult.Succeeded;
            string size = $"{summary.totalSize / 1024f / 1024f:F0} МБ";

            if (ok)
            {
                Debug.Log($"[Билд] {label}: готово, {size}, {summary.totalTime.TotalSeconds:F0} с → {path}");
            }
            else
            {
                Debug.LogError($"[Билд] {label}: {summary.result}, ошибок {summary.totalErrors} → {path}");
            }

            return ok;
        }
    }
}
