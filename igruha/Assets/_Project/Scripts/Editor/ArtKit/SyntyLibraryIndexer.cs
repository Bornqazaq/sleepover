using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Индекс библиотеки Synty — подфаза 4.0 арт-конвейера.
    ///
    /// Смысл: подбор модели под коробку блокаута должен быть запросом к таблице,
    /// а не перебором сотен префабов вслепую. Индексатор один раз проходит по
    /// всем импортированным пакам и снимает с каждого префаба то, что нужно для
    /// подбора: габариты, смещение пивота, вес в треугольниках, наличие
    /// коллайдера.
    ///
    /// Габариты пишутся и в юнитах, и в ШП (ширинах персонажа), потому что вся
    /// геометрия арен задана в ШП: 1 ШП = 0.72 юнита (диаметр капсулы игрока).
    ///
    /// Индекс кладётся в docs/art/synty-library.json и коммитится: он маленький,
    /// и по нему можно подбирать ассеты даже на машине, где паков нет.
    /// </summary>
    internal static class SyntyLibraryIndexer
    {
        private const string PacksRoot = "Assets/Synty";
        private const float UnitsPerCharacterWidth = 0.72f;
        private const string IndexRelativePath = "docs/art/synty-library.json";

        [MenuItem("Tools/Арт/Индекс библиотеки Synty")]
        private static void BuildIndex()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { PacksRoot });
            if (guids.Length == 0)
            {
                Debug.LogWarning(
                    $"[Индекс Synty] В {PacksRoot} нет префабов. Паки не импортированы — " +
                    "индексировать нечего.");
                return;
            }

            var entries = new List<PrefabEntry>(guids.Length);
            try
            {
                for (var i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Индекс библиотеки Synty", path, (float)i / guids.Length))
                    {
                        Debug.LogWarning("[Индекс Synty] Прервано пользователем.");
                        return;
                    }

                    var entry = Measure(path, guids[i]);
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            var file = Path.Combine(RepositoryRoot(), IndexRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(file) ?? ".");
            File.WriteAllText(file, Serialize(entries), new UTF8Encoding(false));

            var byPack = entries.GroupBy(e => e.Pack)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key}: {g.Count()}");
            Debug.Log(
                $"[Индекс Synty] {entries.Count} префабов → {IndexRelativePath}\n" +
                string.Join("\n", byPack));
        }

        /// <summary>Снимает с префаба всё, по чему потом подбирается модель под коробку.</summary>
        private static PrefabEntry Measure(string path, string guid)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                return null;
            }

            var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0)
            {
                return null;
            }

            // Габариты снимаются по мешам в локальных координатах префаба: так они не зависят
            // от того, как объект случайно повёрнут в сцене-примере пака.
            var bounds = new Bounds();
            var initialized = false;
            var triangles = 0;
            var materials = new HashSet<string>();

            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                triangles += mesh.triangles.Length / 3;
                var local = LocalBounds(prefab.transform, filter.transform, mesh.bounds);
                if (!initialized)
                {
                    bounds = local;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(local);
                }
            }

            if (!initialized)
            {
                return null;
            }

            foreach (var renderer in renderers)
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null)
                    {
                        materials.Add(material.name);
                    }
                }
            }

            return new PrefabEntry
            {
                Name = prefab.name,
                Path = path,
                Guid = guid,
                Pack = PackOf(path),
                Size = bounds.size,
                PivotOffset = bounds.center,
                Triangles = triangles,
                Renderers = renderers.Length,
                HasCollider = prefab.GetComponentInChildren<Collider>(true) != null,
                Materials = materials.OrderBy(m => m).ToArray(),
            };
        }

        /// <summary>Габариты меша в системе координат корня префаба.</summary>
        private static Bounds LocalBounds(Transform root, Transform node, Bounds meshBounds)
        {
            var matrix = root.worldToLocalMatrix * node.localToWorldMatrix;
            var center = matrix.MultiplyPoint3x4(meshBounds.center);
            var extents = meshBounds.extents;
            var axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            var axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            var axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
            var size = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, size * 2f);
        }

        private static string PackOf(string path)
        {
            var parts = path.Split('/');
            return parts.Length > 2 ? parts[2] : "?";
        }

        internal static string RepositoryRoot()
        {
            // Application.dataPath = <репозиторий>/igruha/Assets
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        }

        private static string Serialize(List<PrefabEntry> entries)
        {
            var culture = CultureInfo.InvariantCulture;
            var builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine($" \"snapshot\": \"{DateTime.Now:yyyy-MM-dd HH:mm}\",");
            builder.AppendLine($" \"unitsPerCharacterWidth\": {UnitsPerCharacterWidth.ToString(culture)},");
            builder.AppendLine(" \"prefabs\": [");

            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var materials = string.Join(", ", e.Materials.Select(m => $"\"{Escape(m)}\""));
                builder.Append("  {");
                builder.Append($"\"name\": \"{Escape(e.Name)}\", ");
                builder.Append($"\"pack\": \"{Escape(e.Pack)}\", ");
                builder.Append($"\"path\": \"{Escape(e.Path)}\", ");
                builder.Append($"\"guid\": \"{e.Guid}\", ");
                builder.Append($"\"size\": [{F(e.Size.x)}, {F(e.Size.y)}, {F(e.Size.z)}], ");
                builder.Append("\"sizeCw\": [" +
                               $"{F(e.Size.x / UnitsPerCharacterWidth)}, " +
                               $"{F(e.Size.y / UnitsPerCharacterWidth)}, " +
                               $"{F(e.Size.z / UnitsPerCharacterWidth)}], ");
                builder.Append($"\"pivotOffset\": [{F(e.PivotOffset.x)}, {F(e.PivotOffset.y)}, {F(e.PivotOffset.z)}], ");
                builder.Append($"\"triangles\": {e.Triangles}, ");
                builder.Append($"\"renderers\": {e.Renderers}, ");
                builder.Append($"\"hasCollider\": {(e.HasCollider ? "true" : "false")}, ");
                builder.Append($"\"materials\": [{materials}]");
                builder.AppendLine(i == entries.Count - 1 ? "}" : "},");
            }

            builder.AppendLine(" ]");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string F(float value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Escape(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private sealed class PrefabEntry
        {
            internal string Name;
            internal string Path;
            internal string Guid;
            internal string Pack;
            internal Vector3 Size;
            internal Vector3 PivotOffset;
            internal int Triangles;
            internal int Renderers;
            internal bool HasCollider;
            internal string[] Materials;
        }
    }
}
