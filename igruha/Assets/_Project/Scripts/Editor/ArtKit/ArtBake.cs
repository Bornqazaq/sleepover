using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Запекание арта — подфаза 4.6 арт-конвейера.
    ///
    /// Зачем: паки Synty в репозиторий не кладутся (правило STATE.md 3a), а сцена
    /// ссылается на них по GUID. У кого паков нет — у того сцена молча теряет меши
    /// и материалы, а сборка падает. Так DuckHunt.unity дала 73 ошибки и была
    /// выключена из билда.
    ///
    /// Запекание переносит РОВНО ТО, что сцена реально использует, в
    /// Assets/_Project/Art/&lt;Сцена&gt;/ и переписывает ссылки на копии. После этого
    /// сцена самодостаточна: собирается у напарника и на машине прогонов без паков.
    ///
    /// Порядок: распаковать инстансы префабов паков → скопировать использованные
    /// файлы → перенаправить ссылки внутри скопированных материалов → перенаправить
    /// ссылки компонентов сцены.
    /// </summary>
    internal static class ArtBake
    {
        private const string PacksRoot = "Assets/Synty/";
        private const string ArtRoot = "Assets/_Project/Art";

        [MenuItem("Tools/Арт/Запечь арт сцены")]
        private static void Bake()
        {
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                Debug.LogError("[Запекание] Сцена не сохранена — сначала сохрани её.");
                return;
            }

            var target = $"{ArtRoot}/{scene.name}";
            if (!EditorUtility.DisplayDialog(
                    "Запечь арт сцены",
                    $"Перенести использованное из паков Synty в {target} и переписать ссылки?\n\n" +
                    "Инстансы префабов паков будут распакованы — связь с исходными префабами теряется.",
                    "Запечь", "Отмена"))
            {
                return;
            }

            var unpacked = UnpackPackInstances(scene);
            var used = CollectPackAssets(scene);
            if (used.Count == 0)
            {
                Debug.Log("[Запекание] Сцена не ссылается на паки Synty — запекать нечего.");
                return;
            }

            var copies = CopyAssets(used, target);
            var remappedMaterials = RemapCopiedMaterials(copies);
            var remappedComponents = RemapScene(scene, copies);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            var left = CollectPackAssets(scene).Count;
            Debug.Log(
                $"[Запекание] {scene.name}: распаковано инстансов {unpacked}, " +
                $"перенесено файлов {copies.Count}, переписано ссылок в материалах {remappedMaterials}, " +
                $"в компонентах {remappedComponents}. Осталось ссылок на паки: {left}." +
                (left == 0 ? " Сцена самодостаточна." : " ⚠️ Разобрать остаток вручную."));
        }

        /// <summary>Инстанс префаба пака нельзя перенаправить на копию — его надо распаковать.</summary>
        private static int UnpackPackInstances(Scene scene)
        {
            var unpacked = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    var go = transform.gameObject;
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(go))
                    {
                        continue;
                    }

                    var source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                    if (string.IsNullOrEmpty(source) || !source.StartsWith(PacksRoot, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    PrefabUtility.UnpackPrefabInstance(
                        go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    unpacked++;
                }
            }

            return unpacked;
        }

        /// <summary>Пути файлов паков, на которые сцена реально ссылается.</summary>
        private static HashSet<string> CollectPackAssets(Scene scene)
        {
            var roots = scene.GetRootGameObjects().Cast<UnityEngine.Object>().ToArray();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dependency in EditorUtility.CollectDependencies(roots))
            {
                if (dependency == null)
                {
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(dependency);
                if (!string.IsNullOrEmpty(path) && path.StartsWith(PacksRoot, StringComparison.Ordinal))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        /// <summary>Копирует файлы паков в папку игры. Возвращает карту «старый объект → новый».</summary>
        private static Dictionary<UnityEngine.Object, UnityEngine.Object> CopyAssets(
            HashSet<string> sourcePaths, string target)
        {
            var map = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            foreach (var source in sourcePaths.OrderBy(p => p))
            {
                var relative = source.Substring(PacksRoot.Length);
                var destination = $"{target}/{relative}";
                EnsureFolder(Path.GetDirectoryName(destination)?.Replace('\\', '/'));

                if (!File.Exists(destination) && !AssetDatabase.CopyAsset(source, destination))
                {
                    Debug.LogWarning($"[Запекание] Не удалось скопировать {source}");
                    continue;
                }

                AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);

                // Внутри одного файла может лежать много подассетов (меши и материалы FBX) —
                // сопоставляем их по типу и имени.
                var oldAssets = AssetDatabase.LoadAllAssetsAtPath(source);
                var newAssets = AssetDatabase.LoadAllAssetsAtPath(destination);
                foreach (var oldAsset in oldAssets)
                {
                    if (oldAsset == null)
                    {
                        continue;
                    }

                    var match = newAssets.FirstOrDefault(
                        a => a != null && a.name == oldAsset.name && a.GetType() == oldAsset.GetType());
                    if (match != null)
                    {
                        map[oldAsset] = match;
                    }
                }
            }

            return map;
        }

        /// <summary>Скопированный материал по-прежнему смотрит на текстуры пака — переводим на копии.</summary>
        private static int RemapCopiedMaterials(Dictionary<UnityEngine.Object, UnityEngine.Object> map)
        {
            var remapped = 0;
            foreach (var material in map.Values.OfType<Material>())
            {
                var serialized = new SerializedObject(material);
                if (RemapProperties(serialized, map))
                {
                    remapped++;
                }
            }

            return remapped;
        }

        /// <summary>Переписывает ссылки во всех компонентах сцены.</summary>
        private static int RemapScene(Scene scene, Dictionary<UnityEngine.Object, UnityEngine.Object> map)
        {
            var remapped = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null)
                    {
                        continue;
                    }

                    var serialized = new SerializedObject(component);
                    if (RemapProperties(serialized, map))
                    {
                        remapped++;
                    }
                }
            }

            return remapped;
        }

        private static bool RemapProperties(
            SerializedObject serialized, Dictionary<UnityEngine.Object, UnityEngine.Object> map)
        {
            var property = serialized.GetIterator();
            var changed = false;
            while (property.NextVisible(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference)
                {
                    continue;
                }

                var value = property.objectReferenceValue;
                if (value != null && map.TryGetValue(value, out var replacement))
                {
                    property.objectReferenceValue = replacement;
                    changed = true;
                }
            }

            if (changed)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return changed;
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
