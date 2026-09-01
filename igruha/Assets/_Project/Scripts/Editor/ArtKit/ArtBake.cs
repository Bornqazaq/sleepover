using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    /// <b>Зависимости берутся транзитивно, а не первым уровнем.</b> Первый боевой
    /// прогон на DuckHunt (01.09) показал, почему это обязательно: ссылок на паки
    /// осталось 16 при нуле ссылок в самой сцене — их держали уже скопированные
    /// файлы. Префаб ружья тянул свой FBX и коллизию, шейдер пака — три подграфа,
    /// материал снега — второй материал. Каждая такая цепочка возвращает сцене
    /// зависимость от пака целиком.
    ///
    /// <b>Ссылки правятся подменой GUID в тексте копий, а не через SerializedObject.</b>
    /// Ассеты Unity — текст (YAML у материалов и префабов, JSON у shadergraph), и
    /// ссылка в них это строка `guid: &lt;32 hex&gt;`. SerializedObject не видит
    /// внутренностей shadergraph вовсе, а у FBX ссылки на материалы лежат не в
    /// самом файле, а в `.meta` импортёра — поэтому `.meta` копий правятся тоже.
    /// Подменяются только GUID исходников из паков: собственный GUID копии новый,
    /// в карте его нет, и он остаётся нетронутым.
    /// </summary>
    internal static class ArtBake
    {
        private const string PacksRoot = "Assets/Synty/";
        private const string ArtRoot = "Assets/_Project/Art";

        /// <summary>Расширения, внутри которых ссылки лежат текстом и поддаются подмене GUID.</summary>
        private static readonly HashSet<string> TextualAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mat", ".prefab", ".asset", ".shadergraph", ".shadersubgraph",
            ".controller", ".anim", ".overrideController", ".physicMaterial", ".mixer", ".meta"
        };

        [MenuItem("Tools/Арт/Запечь арт сцены")]
        private static void Bake()
        {
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                Debug.LogError("[Запекание] Сцена не сохранена — сначала сохрани её.");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Запечь арт сцены",
                    $"Перенести использованное из паков Synty в {ArtRoot}/{scene.name} и переписать ссылки?\n\n" +
                    "Инстансы префабов паков будут распакованы — связь с исходными префабами теряется.",
                    "Запечь", "Отмена"))
            {
                return;
            }

            Debug.Log(BakeActiveScene());
        }

        /// <summary>
        /// Та же работа без диалога — чтобы запекание прогонялось скриптом и
        /// автопроверкой, а не только руками из меню. Возвращает отчёт строкой:
        /// вызывающий сам решает, логировать его или разбирать числа.
        /// </summary>
        internal static string BakeActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                return "[Запекание] Сцена не сохранена — сначала сохрани её.";
            }

            var target = $"{ArtRoot}/{scene.name}";
            var unpacked = UnpackPackInstances(scene);

            // Сохраняем до сбора: держатели ищутся по зависимостям файла сцены,
            // а несохранённая сцена отдаёт их по прошлому состоянию.
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            var sources = CollectPackAssets(scene, target);
            if (sources.Count == 0)
            {
                return "[Запекание] Сцена не ссылается на паки Synty — запекать нечего.";
            }

            var guidMap = CopyAssets(sources, target, out var copied, out var objectMap);
            var rewritten = RewriteGuids(TextualFilesUnder(target), guidMap);
            var holders = CollectProjectHolders(scene, target);
            var rewrittenHolders = RewriteGuids(holders, guidMap);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            var remappedComponents = RemapScene(scene, objectMap);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            var left = CollectPackAssets(scene, target).Count;
            return
                $"[Запекание] {scene.name}: распаковано инстансов {unpacked}, " +
                $"перенесено файлов {copied} (всего в карте {guidMap.Count}), " +
                $"переписано файлов копий {rewritten}, ассетов проекта {rewrittenHolders}, " +
                $"компонентов сцены {remappedComponents}. " +
                $"Осталось ссылок на паки: {left}." +
                (left == 0 ? " Сцена самодостаточна." : " ⚠️ Разобрать остаток вручную.");
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

        /// <summary>
        /// Все файлы паков, от которых зависит сцена — транзитивно, включая то,
        /// что тянут за собой уже сделанные копии в <paramref name="target"/>.
        /// Второе обязательно: копия префаба продолжает смотреть на FBX пака, и
        /// без этого шага запекание останавливается на первом уровне.
        /// </summary>
        private static HashSet<string> CollectPackAssets(Scene scene, string target)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var roots = scene.GetRootGameObjects().Cast<UnityEngine.Object>().ToArray();

            foreach (var dependency in EditorUtility.CollectDependencies(roots))
            {
                if (dependency == null)
                {
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(dependency);
                if (!string.IsNullOrEmpty(path) && path.StartsWith(PacksRoot, StringComparison.Ordinal))
                {
                    AddWithDependencies(path, found);
                }
            }

            if (AssetDatabase.IsValidFolder(target))
            {
                foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { target }))
                {
                    var copy = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(copy))
                    {
                        continue;
                    }

                    foreach (var dependency in AssetDatabase.GetDependencies(copy, true))
                    {
                        if (dependency.StartsWith(PacksRoot, StringComparison.Ordinal))
                        {
                            AddWithDependencies(dependency, found);
                        }
                    }
                }
            }

            return found;
        }

        /// <summary>Путь плюс всё, что он тянет за собой из паков.</summary>
        private static void AddWithDependencies(string path, HashSet<string> found)
        {
            if (!found.Add(path))
            {
                return;
            }

            foreach (var dependency in AssetDatabase.GetDependencies(path, true))
            {
                if (dependency.StartsWith(PacksRoot, StringComparison.Ordinal))
                {
                    found.Add(dependency);
                }
            }
        }

        /// <summary>
        /// Копирует файлы паков в папку игры, сохраняя путь относительно
        /// <see cref="PacksRoot"/>. Возвращает карту «GUID исходника → GUID копии»
        /// и, отдельно, карту объектов для правки компонентов сцены.
        /// </summary>
        private static Dictionary<string, string> CopyAssets(
            HashSet<string> sourcePaths,
            string target,
            out int copied,
            out Dictionary<UnityEngine.Object, UnityEngine.Object> objectMap)
        {
            var guidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            objectMap = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            copied = 0;

            foreach (var source in sourcePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var relative = source.Substring(PacksRoot.Length);
                var destination = $"{target}/{relative}";
                EnsureFolder(Path.GetDirectoryName(destination)?.Replace('\\', '/'));

                if (!File.Exists(destination))
                {
                    if (!AssetDatabase.CopyAsset(source, destination))
                    {
                        Debug.LogWarning($"[Запекание] Не удалось скопировать {source}");
                        continue;
                    }

                    AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
                    copied++;
                }

                var sourceGuid = AssetDatabase.AssetPathToGUID(source);
                var destinationGuid = AssetDatabase.AssetPathToGUID(destination);
                if (!string.IsNullOrEmpty(sourceGuid) && !string.IsNullOrEmpty(destinationGuid))
                {
                    guidMap[sourceGuid] = destinationGuid;
                }

                MapSubAssets(source, destination, objectMap);
            }

            return guidMap;
        }

        /// <summary>
        /// Внутри одного файла может лежать много подассетов (меши и материалы FBX) —
        /// сопоставляем их по типу и имени, чтобы компоненты сцены переехали на копии.
        /// </summary>
        private static void MapSubAssets(
            string source, string destination, Dictionary<UnityEngine.Object, UnityEngine.Object> objectMap)
        {
            var oldAssets = AssetDatabase.LoadAllAssetsAtPath(source);
            var newAssets = AssetDatabase.LoadAllAssetsAtPath(destination);
            foreach (var oldAsset in oldAssets)
            {
                if (oldAsset == null || objectMap.ContainsKey(oldAsset))
                {
                    continue;
                }

                var match = newAssets.FirstOrDefault(
                    a => a != null && a.name == oldAsset.name && a.GetType() == oldAsset.GetType());
                if (match != null)
                {
                    objectMap[oldAsset] = match;
                }
            }
        }

        /// <summary>Текстовые файлы копий, внутри которых бывают ссылки на паки.</summary>
        private static IEnumerable<string> TextualFilesUnder(string target)
        {
            return Directory.Exists(target)
                ? Directory.GetFiles(target, "*.*", SearchOption.AllDirectories)
                : Enumerable.Empty<string>();
        }

        /// <summary>
        /// Ассеты самого проекта, которые достижимы из сцены и держат ссылку на пак.
        ///
        /// Это вторая половина задачи, и без неё запекание не решает исходную
        /// проблему. На DuckHunt (01.09) ссылок на паки не осталось ни в сцене,
        /// ни в копиях — их держали `DuckHuntConfig.asset` (префаб ружья Охотника,
        /// он спавнится в рантайме) и материалы палитры `DH_FloorWinter.mat`,
        /// `DH_Ice.mat` (текстуры снега и льда). Такой ассет уходит в сборку
        /// вместе с игрой и роняет её ровно так же, как ссылка из сцены.
        ///
        /// Правятся только те, что уже завязаны на пак и участвуют в этой сцене:
        /// чужие ассеты проекта не трогаются.
        /// </summary>
        private static IEnumerable<string> CollectProjectHolders(Scene scene, string target)
        {
            var holders = new List<string>();
            foreach (var dependency in AssetDatabase.GetDependencies(scene.path, true))
            {
                if (dependency.StartsWith(PacksRoot, StringComparison.Ordinal) ||
                    dependency.StartsWith(target, StringComparison.Ordinal) ||
                    !TextualAssets.Contains(Path.GetExtension(dependency)))
                {
                    continue;
                }

                foreach (var direct in AssetDatabase.GetDependencies(dependency, false))
                {
                    if (direct.StartsWith(PacksRoot, StringComparison.Ordinal))
                    {
                        holders.Add(dependency);
                        break;
                    }
                }
            }

            return holders;
        }

        /// <summary>
        /// Переписывает ссылки подменой GUID в тексте. Правятся и сами ассеты,
        /// и их `.meta`: у FBX ссылки на материалы живут именно в `.meta`
        /// импортёра, а у shadergraph — в JSON тела файла.
        /// </summary>
        private static int RewriteGuids(IEnumerable<string> files, Dictionary<string, string> guidMap)
        {
            if (guidMap.Count == 0)
            {
                return 0;
            }

            var pattern = new Regex(
                string.Join("|", guidMap.Keys.Select(Regex.Escape)), RegexOptions.Compiled);
            var rewritten = 0;

            foreach (var file in files)
            {
                if (!TextualAssets.Contains(Path.GetExtension(file)) || !File.Exists(file))
                {
                    continue;
                }

                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (IOException)
                {
                    continue;
                }

                var replaced = pattern.Replace(
                    text, m => guidMap.TryGetValue(m.Value, out var to) ? to : m.Value);
                if (!string.Equals(replaced, text, StringComparison.Ordinal))
                {
                    File.WriteAllText(file, replaced, new UTF8Encoding(false));
                    rewritten++;
                }
            }

            return rewritten;
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
