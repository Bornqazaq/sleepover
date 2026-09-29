using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Igruha.Core.CameraSystems
{
    /// <summary>
    /// Сторожевая проверка: в сцене должен быть один активный вывод Camera и
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
            var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var outputs = new List<Camera>();
            foreach (var camera in cameras)
                if (camera.enabled && !IsStackedOverlay(camera, cameras)) outputs.Add(camera);
            LogIfMoreThanOne(outputs.ToArray(), "Camera");
            var listeners = new List<AudioListener>();
            foreach (var listener in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (listener.enabled) listeners.Add(listener);
            LogIfMoreThanOne(listeners.ToArray(), "AudioListener");
        }

        /// <summary>A registered URP overlay shares its base camera's output; an orphan is still an error.</summary>
        public static bool IsStackedOverlay(Camera candidate, IReadOnlyList<Camera> cameras)
        {
            if (!candidate.TryGetComponent<UniversalAdditionalCameraData>(out var overlay) ||
                overlay.renderType != CameraRenderType.Overlay) return false;
            foreach (var camera in cameras)
                if (camera != candidate && camera.isActiveAndEnabled &&
                    camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) &&
                    data.renderType == CameraRenderType.Base && data.cameraStack.Contains(candidate)) return true;
            return false;
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
