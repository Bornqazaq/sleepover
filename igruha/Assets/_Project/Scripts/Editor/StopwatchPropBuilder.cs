using System.IO;
using UnityEditor;
using UnityEngine;
using Igruha.Minigames.Circus;
using Igruha.Minigames.Stopwatch;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Реквизит «Секундомера»: красная кнопка на тумбе в слот каждой клетки.
    ///
    /// Отдельно от билдера арены, потому что арена общая с «Порядком банок»:
    /// там в тот же слот встанет полка с банками. Клетка про содержимое слота
    /// не знает, и это единственное место, которое знает.
    ///
    /// Кнопка — настоящий префаб, в отличие от арены: у неё нет ни одного
    /// размера, который приходил бы из конфига, поэтому замораживать нечего.
    /// </summary>
    internal static class StopwatchPropBuilder
    {
        private const string PrefabFolder = "Assets/_Project/Prefabs/Minigames/Stopwatch";
        private const string PrefabPath = PrefabFolder + "/CageButton.prefab";

        private const float PedestalHeight = 0.55f;
        private const float PedestalDiameter = 0.42f;
        private const float CapHeight = 0.12f;
        private const float CapDiameter = 0.3f;

        [MenuItem("Igruha/Minigames/Rebuild Stopwatch Props")]
        private static void Rebuild()
        {
            GameObject prefab = BuildPrefab();
            if (prefab == null)
            {
                return;
            }

            GameObject cagesRoot = GameObject.Find("_Arena/Cages");
            if (cagesRoot == null)
            {
                Debug.LogError("StopwatchPropBuilder: открой сцену Stopwatch — не найден _Arena/Cages.");
                return;
            }

            var stations = cagesRoot.GetComponentsInChildren<CageStation>(true);
            int placed = 0;
            for (int i = 0; i < stations.Length; i++)
            {
                Transform slot = stations[i].PropSlot;
                if (slot == null)
                {
                    Debug.LogWarning($"StopwatchPropBuilder: у клетки {stations[i].name} нет слота реквизита", stations[i]);
                    continue;
                }

                for (int c = slot.childCount - 1; c >= 0; c--)
                {
                    Object.DestroyImmediate(slot.GetChild(c).gameObject);
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slot);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                placed++;
            }

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log($"Реквизит «Секундомера» расставлен: кнопок {placed} из {stations.Length} клеток.");
        }

        private static GameObject BuildPrefab()
        {
            if (!Directory.Exists(PrefabFolder))
            {
                Directory.CreateDirectory(PrefabFolder);
                AssetDatabase.Refresh();
            }

            var root = new GameObject("CageButton");
            try
            {
                // Тумба. Коллайдер на ней же: PlayerInteractor ищет интерактив
                // через OverlapSphere, и без коллайдера кнопка невидима для поиска.
                GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pedestal.name = "Pedestal";
                pedestal.transform.SetParent(root.transform, false);
                pedestal.transform.localPosition = new Vector3(0f, PedestalHeight * 0.5f, 0f);
                pedestal.transform.localScale = new Vector3(PedestalDiameter, PedestalHeight * 0.5f, PedestalDiameter);

                GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cap.name = "Lamp";
                cap.transform.SetParent(root.transform, false);
                cap.transform.localPosition = new Vector3(0f, PedestalHeight + CapHeight * 0.5f, 0f);
                cap.transform.localScale = new Vector3(CapDiameter, CapHeight * 0.5f, CapDiameter);
                Object.DestroyImmediate(cap.GetComponent<Collider>());

                var button = root.AddComponent<CageButton>();
                var serialized = new SerializedObject(button);
                serialized.FindProperty("lamp").objectReferenceValue = cap.GetComponent<Renderer>();
                serialized.ApplyModifiedPropertiesWithoutUndo();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
