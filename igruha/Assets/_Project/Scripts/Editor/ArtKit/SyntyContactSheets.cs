using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Контакт-листы паков Synty — подфаза 4.0 арт-конвейера.
    ///
    /// Индекс отвечает на вопрос «какого размера эта модель», но не отвечает на
    /// вопрос «как она выглядит». Контакт-лист закрывает второй: все префабы пака
    /// рендерятся сеткой в один PNG, и модель выбирается глазами, а не по имени
    /// файла. Рядом кладётся список имён по клеткам.
    ///
    /// Рендер идёт через PreviewRenderUtility — синхронно и без открытой сцены,
    /// поэтому сцена мини-игры не трогается вовсе.
    /// </summary>
    internal static class SyntyContactSheets
    {
        private const string PacksRoot = "Assets/Synty";
        private const string OutputFolder = "Assets/Screenshots/SyntyContactSheets";
        private const int TileSize = 256;
        private const int Columns = 8;
        private const int Rows = 8;
        private const int PerSheet = Columns * Rows;

        [MenuItem("Tools/Арт/Контакт-листы Synty")]
        private static void BuildSheets()
        {
            var root = SelectedPackFolder() ?? PacksRoot;
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { root });
            if (guids.Length == 0)
            {
                Debug.LogWarning($"[Контакт-листы] В {root} нет префабов.");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Контакт-листы Synty",
                    $"Отрендерить {guids.Length} префабов из {root}?\n" +
                    $"Это {Mathf.CeilToInt(guids.Length / (float)PerSheet)} листов по {Columns}×{Rows}.",
                    "Рендерить", "Отмена"))
            {
                return;
            }

            var prefabs = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Directory.CreateDirectory(OutputFolder);
            var preview = new PreviewRenderUtility();
            var written = 0;
            try
            {
                for (var start = 0; start < prefabs.Count; start += PerSheet)
                {
                    var batch = prefabs.Skip(start).Take(PerSheet).ToList();
                    var packName = PackOf(batch[0]);
                    var sheetNumber = start / PerSheet + 1;
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Контакт-листы Synty",
                            $"{packName}, лист {sheetNumber}",
                            (float)start / prefabs.Count))
                    {
                        break;
                    }

                    WriteSheet(preview, batch, $"{packName}_{sheetNumber:00}");
                    written++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                preview.Cleanup();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[Контакт-листы] Листов: {written} → {OutputFolder}");
        }

        private static void WriteSheet(PreviewRenderUtility preview, List<string> paths, string sheetName)
        {
            var sheet = new Texture2D(Columns * TileSize, Rows * TileSize, TextureFormat.RGBA32, false);
            var empty = Enumerable.Repeat(new Color(0.12f, 0.12f, 0.13f, 1f), sheet.width * sheet.height).ToArray();
            sheet.SetPixels(empty);

            var names = new StringBuilder();
            names.AppendLine($"# {sheetName}");
            names.AppendLine();
            names.AppendLine("Клетки идут слева направо, сверху вниз.");
            names.AppendLine();

            for (var i = 0; i < paths.Count; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                if (prefab == null)
                {
                    continue;
                }

                names.AppendLine($"{i + 1}. {prefab.name} — `{paths[i]}`");
                var tile = Render(preview, prefab);
                if (tile == null)
                {
                    continue;
                }

                var column = i % Columns;
                var row = i / Columns;
                // Пиксели текстуры идут снизу вверх, а клетки читаются сверху вниз.
                var x = column * TileSize;
                var y = (Rows - 1 - row) * TileSize;
                sheet.SetPixels(x, y, TileSize, TileSize, tile.GetPixels());
                UnityEngine.Object.DestroyImmediate(tile);
            }

            sheet.Apply();
            File.WriteAllBytes(Path.Combine(OutputFolder, sheetName + ".png"), sheet.EncodeToPNG());
            File.WriteAllText(Path.Combine(OutputFolder, sheetName + ".md"), names.ToString(), new UTF8Encoding(false));
            UnityEngine.Object.DestroyImmediate(sheet);
        }

        /// <summary>Один кадр префаба: камера ставится по его габаритам, чтобы модель заполняла клетку.</summary>
        private static Texture2D Render(PreviewRenderUtility preview, GameObject prefab)
        {
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    return null;
                }

                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                }

                preview.AddSingleGO(instance);
                var radius = Mathf.Max(bounds.extents.magnitude, 0.1f);
                var direction = new Vector3(0.6f, 0.45f, -1f).normalized;

                preview.camera.transform.position = bounds.center + direction * (radius * 3.2f);
                preview.camera.transform.LookAt(bounds.center);
                preview.camera.nearClipPlane = 0.01f;
                preview.camera.farClipPlane = radius * 12f;
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = radius * 1.15f;
                // Свет превью по умолчанию холодный и тусклый — на атласных материалах Synty
                // модель уходит в синеву и форма читается хуже, чем могла бы.
                preview.lights[0].intensity = 1.4f;
                preview.lights[0].color = Color.white;
                preview.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
                preview.lights[1].intensity = 0.7f;
                preview.lights[1].color = Color.white;
                preview.lights[1].transform.rotation = Quaternion.Euler(20f, -140f, 0f);
                preview.ambientColor = new Color(0.45f, 0.45f, 0.47f, 1f);

                preview.BeginStaticPreview(new Rect(0, 0, TileSize, TileSize));
                preview.camera.Render();
                return preview.EndStaticPreview();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Папка пака, выделенная в Project, — чтобы не рендерить всю библиотеку разом.</summary>
        private static string SelectedPackFolder()
        {
            if (Selection.activeObject == null)
            {
                return null;
            }

            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return AssetDatabase.IsValidFolder(path) && path.StartsWith(PacksRoot, StringComparison.Ordinal)
                ? path
                : null;
        }

        private static string PackOf(string path)
        {
            var parts = path.Split('/');
            return parts.Length > 2 ? parts[2] : "Synty";
        }
    }
}
