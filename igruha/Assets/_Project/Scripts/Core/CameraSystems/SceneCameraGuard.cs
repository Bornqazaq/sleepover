using System.Text;
using UnityEngine;

namespace Igruha.Core.CameraSystems
{
    /// <summary>
    /// Сторожевая проверка: в сцене должна быть ровно одна активная Camera и
    /// ровно один активный AudioListener. Лишние обычно приезжают с моделью
    /// персонажа (см. CharacterModelCameraLightStripper) — спавн новой модели
    /// самое частое место, где это может снова случиться, поэтому вызывается
    /// оттуда. Задача — чтобы поломка сразу орала в Console с именами объектов,
    /// а не ловилась руками по кадру с неправильным видом.
    /// </summary>
    public static class SceneCameraGuard
    {
        public static void ValidateSingleActiveCameraAndListener()
        {
            LogIfMoreThanOne(Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None), "Camera");
            LogIfMoreThanOne(Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None), "AudioListener");
        }

        private static void LogIfMoreThanOne<T>(T[] found, string label) where T : Component
        {
            if (found.Length <= 1)
            {
                return;
            }

            var names = new StringBuilder();
            for (int i = 0; i < found.Length; i++)
            {
                if (i > 0)
                {
                    names.Append(", ");
                }

                names.Append(found[i].name);
            }

            Debug.LogError($"В сцене {found.Length} активных {label} вместо одного: {names}. " +
                "Обычно это лишняя нода из FBX персонажа — проверь Import Cameras/Import Lights на модели.");
        }
    }
}
