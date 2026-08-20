using System.IO;
using UnityEditor;
using UnityEngine;
using Igruha.Minigames.CansOrder;
using Igruha.Minigames.Circus;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Реквизит «Порядка банок»: полка с банками в слот каждой клетки.
    ///
    /// Зеркало <see cref="StopwatchPropBuilder"/> и отдельно от билдера арены
    /// по той же причине: арена общая, а в слот клетки у «Секундомера» встаёт
    /// кнопка, у этой игры — полка. Клетка про содержимое слота не знает,
    /// и это единственное место, которое знает.
    ///
    /// Слоты и сами банки полка создаёт в рантайме: их число берётся из таблицы
    /// конфига и может измениться плейтестом. Здесь строится только доска.
    /// </summary>
    internal static class CanOrderPropBuilder
    {
        private const string SceneName = "CansOrder";
        private const string ArenaConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/CircusArenaConfig.asset";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Minigames/CansOrder";
        private const string PrefabPath = PrefabFolder + "/CanShelf.prefab";

        /// <summary>Длина полки, 3 ШП (спека 3.2).</summary>
        private const float ShelfLengthBodyWidths = 3f;
        /// <summary>Высота полки над полом клетки: уровень пояса персонажа (спека 3.2).</summary>
        private const float ShelfHeight = 0.95f;
        private const float ShelfDepth = 0.4f;
        private const float BoardThickness = 0.06f;
        private const float SupportThickness = 0.08f;
        /// <summary>Зазор между доской и прутьями стены, м. Впритык доска цепляется за коллайдер стены.</summary>
        private const float WallGap = 0.04f;

        [MenuItem("Igruha/Minigames/Rebuild Cans Order Props")]
        private static void Rebuild()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.name != SceneName)
            {
                // Промах по сцене здесь стоит дорого: билдер вычищает слоты
                // реквизита, и запуск на «Секундомере» снёс бы его кнопки.
                Debug.LogError($"CanOrderPropBuilder: активна сцена '{scene.name}', а нужна '{SceneName}'. " +
                               "Открой CansOrder.unity — иначе билдер вычистит реквизит чужой игры.");
                return;
            }

            var config = AssetDatabase.LoadAssetAtPath<CircusArenaConfig>(ArenaConfigPath);
            if (config == null)
            {
                Debug.LogError("CanOrderPropBuilder: не найден " + ArenaConfigPath);
                return;
            }

            GameObject prefab = BuildPrefab(config);
            if (prefab == null)
            {
                return;
            }

            GameObject cagesRoot = GameObject.Find("_Arena/Cages");
            if (cagesRoot == null)
            {
                Debug.LogError("CanOrderPropBuilder: не найден _Arena/Cages — пересобери арену.");
                return;
            }

            var stations = cagesRoot.GetComponentsInChildren<CageStation>(true);
            int placed = 0;
            for (int i = 0; i < stations.Length; i++)
            {
                Transform slot = stations[i].PropSlot;
                if (slot == null)
                {
                    Debug.LogWarning($"CanOrderPropBuilder: у клетки {stations[i].name} нет слота реквизита", stations[i]);
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

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

            float length = ShelfLengthBodyWidths * CircusArenaConfig.MetersPerBodyWidth;
            Debug.Log($"Реквизит «Порядка банок» расставлен: полок {placed} из {stations.Length} клеток. " +
                      $"Полка {length:F2} × {ShelfDepth:F2} м на высоте {ShelfHeight:F2} м, " +
                      $"смещена к внутренней стене на {InnerWallOffset(config):F2} м от центра клетки.");
        }

        /// <summary>
        /// Насколько полка отодвинута от центра клетки к внутренней стене.
        ///
        /// Внутренняя стена — та, что смотрит на центр арены: локальный +Z клетки.
        /// Полка обязана стоять именно там, и это требование кадра, а не украшение:
        /// игрок за полкой смотрит на арену, и табло над ямой попадает в тот же
        /// кадр. У внешней стены он оказался бы спиной к единственному источнику
        /// информации в игре.
        /// </summary>
        private static float InnerWallOffset(CircusArenaConfig config)
        {
            return config.CageInnerSize * 0.5f - ShelfDepth * 0.5f - WallGap;
        }

        private static GameObject BuildPrefab(CircusArenaConfig config)
        {
            if (!Directory.Exists(PrefabFolder))
            {
                Directory.CreateDirectory(PrefabFolder);
                AssetDatabase.Refresh();
            }

            float length = ShelfLengthBodyWidths * CircusArenaConfig.MetersPerBodyWidth;
            float z = InnerWallOffset(config);

            var root = new GameObject("CanShelf");
            try
            {
                // Доска. Коллайдер на ней и только на ней: PlayerInteractor ищет
                // интерактив через OverlapSphere с буфером на 16 коллайдеров,
                // а в клетке их и так семь. Отдельные коллайдеры на слотах или
                // банках вытеснили бы из выборки кнопку подтверждения.
                GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
                board.name = "Board";
                board.transform.SetParent(root.transform, false);
                board.transform.localPosition = new Vector3(0f, ShelfHeight - BoardThickness * 0.5f, z);
                board.transform.localScale = new Vector3(length, BoardThickness, ShelfDepth);

                // Кронштейны — чтобы полка читалась полкой, а не парящей доской.
                for (int i = 0; i < 2; i++)
                {
                    float x = (i == 0 ? -1f : 1f) * (length * 0.5f - SupportThickness);
                    GameObject support = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    support.name = i == 0 ? "Support_L" : "Support_R";
                    support.transform.SetParent(root.transform, false);
                    support.transform.localPosition = new Vector3(x, (ShelfHeight - BoardThickness) * 0.5f, z + ShelfDepth * 0.25f);
                    support.transform.localScale = new Vector3(SupportThickness, ShelfHeight - BoardThickness, SupportThickness);
                    Object.DestroyImmediate(support.GetComponent<Collider>());
                }

                // Корень слотов — верх доски. Слоты и банки полка раскладывает
                // вдоль его локальной оси X уже в рантайме.
                var slotsGo = new GameObject("SlotsRoot");
                slotsGo.transform.SetParent(root.transform, false);
                slotsGo.transform.localPosition = new Vector3(0f, ShelfHeight, z);

                var shelf = root.AddComponent<CanShelf>();
                var serialized = new SerializedObject(shelf);
                serialized.FindProperty("slotsRoot").objectReferenceValue = slotsGo.transform;
                // Ряд короче доски: по краям остаются поля, иначе крайние банки
                // висят на самом срезе.
                serialized.FindProperty("slotSpan").floatValue = length - SupportThickness * 4f;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
