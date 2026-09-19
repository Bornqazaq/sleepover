using System.IO;
using System.Reflection;
using Igruha.Core.CameraSystems;
using Igruha.Core.Player;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Снимок игрового кадра без запуска игры: сцена, персонаж на точке спавна
    /// и камера, рассчитанная тем же ригом, что работает в бою.
    ///
    /// Нужен потому, что кадр — единственное в камере, что не проверяется
    /// тестом: «выше макушки, но не отвесно» проверить числом можно, а
    /// «удобно смотреть» — нельзя. Снимок отдаётся геймдизайнеру.
    ///
    /// Сцены и путь вывода берутся из переменных окружения CAMERA_REVIEW_SCENES
    /// (через запятую, имена файлов без пути) и CAMERA_REVIEW_OUT.
    /// </summary>
    public static class CameraFramingReview
    {
        private const string RigPath = "Assets/_Project/Prefabs/Camera/PartyCameraRig.prefab";
        private const string AvatarPath = "Assets/_Project/Prefabs/Player/Aza.prefab";
        private const int Width = 1600;
        private const int Height = 900;

        public static void Run()
        {
            string output = System.Environment.GetEnvironmentVariable("CAMERA_REVIEW_OUT");
            string scenes = System.Environment.GetEnvironmentVariable("CAMERA_REVIEW_SCENES");
            if (string.IsNullOrEmpty(output) || string.IsNullOrEmpty(scenes))
            {
                Debug.LogError("CameraFramingReview: нужны CAMERA_REVIEW_OUT и CAMERA_REVIEW_SCENES");
                return;
            }

            Directory.CreateDirectory(output);
            foreach (string name in scenes.Split(','))
            {
                CaptureScene(name.Trim(), output);
            }
        }

        private static void CaptureScene(string sceneName, string output)
        {
            string path = FindScene(sceneName);
            if (path == null)
            {
                Debug.LogError($"CameraFramingReview: сцена {sceneName} не найдена");
                return;
            }

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            GameObject avatarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarPath);
            GameObject avatar = Object.Instantiate(avatarPrefab);
            avatar.transform.position = StandingSpot();
            var player = avatar.GetComponent<PlayerController>();
            Physics.SyncTransforms();

            GameObject rigObject = Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>(RigPath));
            var rig = rigObject.GetComponent<ThirdPersonCameraRig>();
            var cam = rigObject.GetComponent<CinemachineCamera>();
            var orbit = rigObject.GetComponent<CinemachineOrbitalFollow>();

            // Awake не вызывается у компонента, созданного в Edit Mode, — а
            // именно он приводит риг к формату проекта.
            typeof(ThirdPersonCameraRig).GetMethod("Awake",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(rig, null);

            cam.Follow = player.CameraTarget;
            cam.LookAt = player.CameraTarget;
            InputAxis horizontal = orbit.HorizontalAxis;
            horizontal.Value = Mathf.Repeat(avatar.transform.eulerAngles.y + 180f, 360f) - 180f;
            orbit.HorizontalAxis = horizontal;
            InputAxis vertical = orbit.VerticalAxis;
            vertical.Value = vertical.Center;
            orbit.VerticalAxis = vertical;
            cam.InternalUpdateCameraState(Vector3.up, -1f);

            CameraState state = cam.State;
            Render(state.GetFinalPosition(), state.GetFinalOrientation(),
                state.Lens.FieldOfView, Path.Combine(output, sceneName + "-now.png"));

            // Прежний кадр для сравнения: привязка к корню персонажа, угол 17.5°,
            // взгляд на 0.3 м выше корня — ровно то, что было до правки.
            Vector3 root = avatar.transform.position;
            Quaternion direction = Quaternion.Euler(17.5f, horizontal.Value, 0f);
            Vector3 before = root - direction * Vector3.forward * orbit.Radius;
            Render(before, Quaternion.LookRotation(root + Vector3.up * 0.3f - before, Vector3.up),
                state.Lens.FieldOfView, Path.Combine(output, sceneName + "-before.png"));

            Debug.Log($"CameraFramingReview: {sceneName} — камера на y={state.GetFinalPosition().y:F2}, "
                + $"персонаж на y={root.y:F2}, макушка {root.y + 1.65f:F2}");

            Object.DestroyImmediate(rigObject);
            Object.DestroyImmediate(avatar);
        }

        private static string FindScene(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:Scene {name}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == name)
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>Точка спавна, а если её нет — пол под началом координат.</summary>
        private static Vector3 StandingSpot()
        {
            var spawns = Object.FindObjectsByType<Igruha.Core.Spawning.SpawnPoint>(
                FindObjectsSortMode.None);
            if (spawns.Length > 0)
            {
                return spawns[0].transform.position;
            }

            return Physics.Raycast(new Vector3(0f, 50f, 0f), Vector3.down, out RaycastHit hit, 200f)
                ? hit.point : Vector3.zero;
        }

        private static void Render(Vector3 position, Quaternion rotation, float fieldOfView, string file)
        {
            var go = new GameObject("TemporaryFramingCamera");
            var camera = go.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.15f;
            camera.farClipPlane = 1000f;
            camera.aspect = (float)Width / Height;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;

            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = rt;
            try
            {
                camera.Render();
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(file, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(go);
            }
        }
    }
}
