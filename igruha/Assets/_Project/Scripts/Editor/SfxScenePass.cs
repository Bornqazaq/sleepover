using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Прогон всех проходов звука по сценам проекта — фаза 5.
    ///
    /// Существует, потому что звук ставится в сцены, а сцен четырнадцать:
    /// открыть каждую, вспомнить, какие проходы ей нужны, и не забыть
    /// сохранить — ровно та ручная работа, на которой звук терялся. Здесь
    /// список «сцена → её проходы» записан один раз.
    ///
    /// Зовётся и пунктом меню, и из пакетного запуска редактора
    /// (<c>-executeMethod Igruha.EditorTools.SfxScenePass.RunAll</c>) — Unity
    /// в проекте живёт и без MCP, из терминала.
    ///
    /// <b>Сцена Duck Hunt в списке отсутствует намеренно.</b> Игру ведёт второй
    /// разработчик, и её сцена — его. Звуки Duck Hunt приехали и лежат в
    /// библиотеке; что с ними делать, описано в <c>docs/art/sfx-gaps.md</c>.
    ///
    /// Все проходы повторимы: второй запуск ничего не дублирует.
    /// </summary>
    internal static class SfxScenePass
    {
        private const string ScenesRoot = "Assets/_Project/Scenes";

        /// <summary>Сцена и то, что в ней надо озвучить. Порядок проходов внутри сцены значения не имеет.</summary>
        private readonly struct SceneJob
        {
            public readonly string Path;
            public readonly Action[] Passes;

            public SceneJob(string path, params Action[] passes)
            {
                Path = path;
                Passes = passes;
            }
        }

        private static IEnumerable<SceneJob> Jobs()
        {
            // Хаб — единственная сцена с настоящим интерфейсом: консоль телевизора,
            // выбор персонажа, пауза. Остальным достаётся пауза и перезапуск.
            yield return new SceneJob($"{ScenesRoot}/Hub.unity", UiSoundPass.Run, SurfaceMarkPass.Run);

            yield return new SceneJob($"{ScenesRoot}/Minigames/OneBullet.unity", UiSoundPass.Run, OneBulletSfx.Build);
            yield return new SceneJob($"{ScenesRoot}/Minigames/CryingAngels.unity", UiSoundPass.Run, CryingAngelsSfx.Build);
            yield return new SceneJob($"{ScenesRoot}/Minigames/Infection.unity", UiSoundPass.Run, InfectionSfx.Build, SurfaceMarkPass.Run);
            yield return new SceneJob($"{ScenesRoot}/Minigames/Stopwatch.unity", UiSoundPass.Run, CircusSfx.RefreshClips, SurfaceMarkPass.Run);
            yield return new SceneJob($"{ScenesRoot}/Minigames/CansOrder.unity", UiSoundPass.Run, CircusSfx.RefreshClips, SurfaceMarkPass.Run);

            yield return new SceneJob($"{ScenesRoot}/Minigames/MemoryRun.unity", UiSoundPass.Run, SurfaceMarkPass.Run);
            yield return new SceneJob($"{ScenesRoot}/Minigames/HoleInWall.unity", UiSoundPass.Run);
            yield return new SceneJob($"{ScenesRoot}/Minigames/Exam.unity", UiSoundPass.Run, SurfaceMarkPass.Run);
            yield return new SceneJob($"{ScenesRoot}/Minigames/CarryItem.unity", UiSoundPass.Run);
            yield return new SceneJob($"{ScenesRoot}/Minigames/BelieveOrNot.unity", UiSoundPass.Run, SurfaceMarkPass.Run);

            // Шаблон и песочница — ради ловушек: дверь, провал и рычаг живут
            // только там и в Duck Hunt. Всякая новая игра начинается с шаблона,
            // и звук ловушек достаётся ей бесплатно.
            yield return new SceneJob($"{ScenesRoot}/MinigameTemplate.unity", UiSoundPass.Run, TrapSoundPass.Run);
            yield return new SceneJob($"{ScenesRoot}/Sandbox.unity", TrapSoundPass.Run);
        }

        [MenuItem("Igruha/Звук/Прогнать звук по всем сценам")]
        internal static void RunAll()
        {
            // В пакетном запуске спрашивать некого: диалог сохранения там
            // не показывается, а метод возвращает отказ и тихо срывает прогон.
            if (!Application.isBatchMode
                && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            SfxLibraryBuilder.BuildAll();

            int done = 0;
            foreach (SceneJob job in Jobs())
            {
                Scene scene;
                try
                {
                    scene = EditorSceneManager.OpenScene(job.Path, OpenSceneMode.Single);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Звук] Сцена не открылась: {job.Path} — {e.Message}");
                    continue;
                }

                foreach (Action pass in job.Passes) pass();

                // Сохраняем только то, что проход действительно тронул: пустая
                // пересохранённая сцена всё равно приходит в diff, потому что
                // Unity дописывает в YAML поля, добавленные к компонентам с
                // прошлого сохранения. Чужая сцена от такого шумит зря.
                if (!scene.isDirty)
                {
                    Debug.Log($"[Звук] Сцена «{scene.name}» не изменилась — не сохраняем.");
                    continue;
                }

                EditorSceneManager.SaveScene(scene);
                done++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Звук] Прогон по сценам закончен: обработано {done}.");
        }
    }
}
