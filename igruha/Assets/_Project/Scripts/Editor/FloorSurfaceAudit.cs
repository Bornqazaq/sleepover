using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Igruha.Core.Spawning;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Сверяет видимый пол с физическим во всех сценах.
    ///
    /// <b>Зачем.</b> Персонаж стоит на коллайдере, а игрок смотрит на модель
    /// пола. Если пол нарисован выше коллайдера, подошвы утоплены в него
    /// всегда — а в танцах, где тело ложится на пол, в него уходят ещё и
    /// кисти с коленями. Правка 21.09 подняла модель персонажа над
    /// коллайдером, и остаток стал виден: в пяти сценах арт клали на
    /// сантиметр-три выше нуля.
    ///
    /// Замер честный, по самой сетке: на время проверки на подозрительные
    /// меши вешается <see cref="MeshCollider"/>, и вниз пускается луч. По
    /// габаритам (bounds) считать нельзя — у наклонных и составных мешей
    /// коробка сильно выше поверхности.
    ///
    /// Сцены не изменяются и не сохраняются.
    /// </summary>
    public static class FloorSurfaceAudit
    {
        /// <summary>Разница больше этой считается провалом, м.</summary>
        public const float Tolerance = 0.005f;

        /// <summary>Что считается полом — та же маска, что у персонажа.</summary>
        private const int GroundMask = 320;

        /// <summary>Насколько выше физического пола ещё может лежать видимая поверхность, м.</summary>
        private const float SearchAbove = 0.15f;

        private static readonly string[] Scenes =
        {
            "Hub",
            "Minigames/BelieveOrNot", "Minigames/CansOrder", "Minigames/CarryItem",
            "Minigames/CryingAngels", "Minigames/DuckHunt", "Minigames/Exam",
            "Minigames/HoleInWall", "Minigames/Infection", "Minigames/MemoryRun",
            "Minigames/Stopwatch"
        };

        /// <summary>Найдено на точке спавна: где пол по физике и где он нарисован.</summary>
        public struct Gap
        {
            public string Scene;
            public Vector3 Point;
            public float PhysicsY;
            public float VisualY;
            public string VisualObject;
            public string VisualPath;
            public string GroundCollider;

            public float Size => VisualY - PhysicsY;
        }

        [MenuItem("Igruha/Проверка/Видимый пол против физического")]
        public static void Run()
        {
            Debug.Log(Report());
        }

        /// <summary>Точка входа для пакетного запуска.</summary>
        public static void RunBatch()
        {
            Debug.Log(Report());
            EditorApplication.Exit(0);
        }

        public static string Report()
        {
            var text = new StringBuilder("=== видимый пол против физического ===\n");
            foreach (string scene in Scenes)
            {
                List<Gap> gaps = Measure(scene, out int points);
                if (gaps.Count == 0)
                {
                    text.AppendLine($"{scene}: точек {points} — совпадает ✓");
                    continue;
                }

                float worst = 0f;
                foreach (Gap gap in gaps)
                {
                    worst = Mathf.Max(worst, gap.Size);
                }

                text.AppendLine($"{scene}: точек {points}, расходится в {gaps.Count}, хуже всего {worst * 100f:F1} см");
                var seen = new HashSet<string>();
                foreach (Gap gap in gaps)
                {
                    if (!seen.Add(gap.VisualObject))
                    {
                        continue;
                    }

                    text.AppendLine($"    '{gap.VisualObject}' на {gap.Size * 100f:F1} см выше коллайдера '{gap.GroundCollider}'");
                    text.AppendLine($"      путь: {gap.VisualPath}");
                }
            }

            return text.ToString();
        }

        /// <summary>Промерить одну сцену. Сцена открывается добавлением и закрывается без сохранения.</summary>
        public static List<Gap> Measure(string sceneName, out int pointCount)
        {
            string path = $"Assets/_Project/Scenes/{sceneName}.unity";
            Scene scene = SceneManager.GetSceneByPath(path);
            bool wasOpen = scene.isLoaded;
            if (!wasOpen)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            }

            var found = new List<Gap>();
            var temporary = new List<MeshCollider>();
            pointCount = 0;

            try
            {
                Physics.SyncTransforms();

                var points = new List<Vector3>();
                var renderers = new List<MeshRenderer>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (SpawnPoint spawn in root.GetComponentsInChildren<SpawnPoint>(true))
                    {
                        points.Add(spawn.transform.position);
                    }

                    renderers.AddRange(root.GetComponentsInChildren<MeshRenderer>(false));
                }

                pointCount = points.Count;

                foreach (Vector3 point in points)
                {
                    if (!Physics.Raycast(point + Vector3.up, Vector3.down, out RaycastHit floor, 3f,
                            GroundMask, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }

                    Gap gap = MeasureAt(point, floor, renderers, temporary);
                    if (gap.Size > Tolerance)
                    {
                        gap.Scene = sceneName;
                        found.Add(gap);
                    }
                }
            }
            finally
            {
                foreach (MeshCollider collider in temporary)
                {
                    if (collider != null)
                    {
                        Object.DestroyImmediate(collider);
                    }
                }

                if (!wasOpen)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            return found;
        }

        private static Gap MeasureAt(Vector3 point, RaycastHit floor, List<MeshRenderer> renderers,
            List<MeshCollider> temporary)
        {
            float physicsY = floor.point.y;
            temporary.Clear();

            foreach (MeshRenderer renderer in renderers)
            {
                Bounds bounds = renderer.bounds;
                if (point.x < bounds.min.x || point.x > bounds.max.x ||
                    point.z < bounds.min.z || point.z > bounds.max.z)
                {
                    continue;
                }

                if (bounds.max.y < physicsY - 0.01f || bounds.min.y > physicsY + SearchAbove)
                {
                    continue;
                }

                if (!renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                {
                    continue;
                }

                MeshCollider probe = renderer.gameObject.AddComponent<MeshCollider>();
                probe.sharedMesh = filter.sharedMesh;
                temporary.Add(probe);
            }

            Physics.SyncTransforms();

            var gap = new Gap
            {
                Point = point,
                PhysicsY = physicsY,
                VisualY = physicsY,
                GroundCollider = floor.collider.name,
                VisualObject = string.Empty,
                VisualPath = string.Empty
            };

            var origin = new Vector3(point.x, physicsY + SearchAbove, point.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, SearchAbove * 2f, ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                if (!(hit.collider is MeshCollider probe) || !temporary.Contains(probe))
                {
                    continue;
                }

                if (hit.point.y > gap.VisualY)
                {
                    gap.VisualY = hit.point.y;
                    gap.VisualObject = hit.collider.name;
                    gap.VisualPath = Path(hit.collider.transform);
                }
            }

            foreach (MeshCollider collider in temporary)
            {
                Object.DestroyImmediate(collider);
            }

            temporary.Clear();
            return gap;
        }

        private static string Path(Transform target)
        {
            var path = new StringBuilder(target.name);
            for (Transform parent = target.parent; parent != null; parent = parent.parent)
            {
                path.Insert(0, parent.name + "/");
            }

            return path.ToString();
        }
    }
}
