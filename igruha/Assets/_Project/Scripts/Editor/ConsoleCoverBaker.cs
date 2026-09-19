using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Igruha.Core.Spawning;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Снимает обложку мини-игры для библиотеки телевизора.
    ///
    /// Обложки — это кадры самих арен, снятые с одной высоты и одним объективом,
    /// иначе лента карточек на экране распадается: у каждой игры свой масштаб и
    /// своё настроение, и держит их вместе только одинаковая съёмка.
    ///
    /// Кадр строится от габаритов арены, а не от приколоченных координат: арены
    /// пересобираются билдерами и меняют размер, а обложку надо уметь переснять
    /// одной командой, не подбирая точку заново.
    ///
    /// Запускается только с графикой — <c>-batchmode</c> без <c>-nographics</c>:
    /// без устройства рисования камера отдаёт пустой кадр.
    /// </summary>
    internal static class ConsoleCoverBaker
    {
        private const string CoversFolder = "Assets/_Project/Art/Hub/Console/Covers";

        /// <summary>Размер обложки — тот же, что у девяти снятых раньше.</summary>
        private const int Width = 1672;
        private const int Height = 941;

        /// <summary>Объектив: тот же, что у остальных обзорных кадров проекта.</summary>
        private const float FieldOfView = 55f;

        /// <summary>Во сколько раз отойти от края площадки. Ближе — не влезает, дальше — съедает туман.</summary>
        private const float DistanceScale = 1.45f;

        /// <summary>Высота съёмки в долях от размера площадки: взгляд с трибуны, а не с вертолёта.</summary>
        private const float HeightScale = 0.5f;

        /// <summary>Куда смотреть: на уровень голов, чтобы в кадре была площадка, а не пол под ней.</summary>
        private const float TargetHeight = 2.2f;

        [MenuItem("Igruha/Арт/Снять обложку «Заражения»")]
        internal static void BakeInfection()
        {
            Bake("Assets/_Project/Scenes/Minigames/Infection.unity", "Infection");

            // Библиотека карточек собирается из списка игр и файлов обложек.
            // Пересобрать её надо здесь же: без записи в библиотеке снятый кадр
            // остаётся файлом на диске, а на телевизоре карточка пустая.
            HubConsoleAssets.Library();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Снять обложку сцены. Имя файла — то же, что зовёт <see cref="HubConsoleAssets"/>.</summary>
        internal static void Bake(string scenePath, string coverName)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            if (!TryMeasureArena(scene, out Bounds arena))
            {
                Debug.LogError($"[Обложка] В сцене «{scene.name}» не нашлось геометрии арены — снимать нечего.");
                return;
            }

            var holder = new GameObject("TemporaryCoverCamera");
            Camera camera = holder.AddComponent<Camera>();

            UniversalAdditionalCameraData data = holder.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;

            float reach = Mathf.Max(6f, Mathf.Max(arena.extents.x, arena.extents.z));
            var target = new Vector3(arena.center.x, arena.min.y + TargetHeight, arena.center.z);
            Vector3 eye = target + new Vector3(reach * DistanceScale, reach * HeightScale, -reach * DistanceScale);

            camera.transform.position = eye;
            camera.transform.LookAt(target);
            camera.fieldOfView = FieldOfView;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = reach * 8f + 100f;
            camera.aspect = (float)Width / Height;

            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture buffer = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;

            string path = $"{CoversFolder}/{coverName}.png";

            try
            {
                camera.targetTexture = buffer;
                camera.Render();
                RenderTexture.active = buffer;
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                texture.Apply();

                Directory.CreateDirectory(CoversFolder);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(buffer);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(holder);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[Обложка] «{scene.name}» снята: {path}, камера {eye} → {target}, охват {reach:F1} м.");
        }

        /// <summary>
        /// Площадка, которую надо показать, — по точкам спавна.
        ///
        /// Не по всей видимой геометрии: у «Заражения» вокруг двора стоит
        /// квартал на сто двадцать метров, и кадр, вмещающий его целиком,
        /// показывает бурое пятно в тумане вместо игры. Спавны же стоят ровно
        /// там, где начинается игра, и очерчивают её площадку.
        /// </summary>
        private static bool TryMeasureArena(Scene scene, out Bounds arena)
        {
            arena = default;
            bool found = false;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (SpawnPoint point in root.GetComponentsInChildren<SpawnPoint>(includeInactive: true))
                {
                    if (!found)
                    {
                        arena = new Bounds(point.transform.position, Vector3.zero);
                        found = true;
                        continue;
                    }

                    arena.Encapsulate(point.transform.position);
                }
            }

            return found;
        }
    }
}
