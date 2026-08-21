using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
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

        // Кнопка подтверждения — на торце полки, справа (спека 3.2).
        private const float ButtonSize = 0.18f;
        private const float ButtonLampDiameter = 0.12f;
        private const float ButtonLampHeight = 0.05f;

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

            bool hud = BuildStageHud();
            GameObject fanfare = BuildFanfarePrefab();

            // Фанфара цепляется к контроллеру здесь же: перецеплять
            // руками после каждой пересборки — верный способ забыть.
            var game = Object.FindAnyObjectByType<CansOrderMinigame>(FindObjectsInactive.Include);
            if (game != null && fanfare != null)
            {
                var gameSo = new SerializedObject(game);
                gameSo.FindProperty("solvedFanfarePrefab").objectReferenceValue = fanfare;
                gameSo.ApplyModifiedPropertiesWithoutUndo();
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

            if (!hud)
            {
                Debug.LogWarning("CanOrderPropBuilder: полоса стадии не построена — не найден _UI/Canvas.");
            }

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

        private const string HudObjectName = "CansOrderStageHud";
        private const float HudWidth = 620f;
        private const float HudHeight = 34f;
        private const float HudLabelHeight = 44f;

        /// <summary>
        /// Полоса остатка стадии внизу экрана — единственный элемент
        /// экранного интерфейса этой игры.
        ///
        /// Счёта на экране нет намеренно: напряжение читается по высоте
        /// клеток и по табло над ямой. Внизу, а не вверху — вверху
        /// игрок смотрит на табло, и панель туда лезть не должна.
        /// </summary>
        private static bool BuildStageHud()
        {
            GameObject canvas = GameObject.Find("_UI/Canvas");
            if (canvas == null)
            {
                return false;
            }

            Transform existing = canvas.transform.Find(HudObjectName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var rootGo = new GameObject(HudObjectName, typeof(RectTransform));
            rootGo.transform.SetParent(canvas.transform, false);
            var root = (RectTransform)rootGo.transform;
            root.anchorMin = new Vector2(0.5f, 0f);
            root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot = new Vector2(0.5f, 0f);
            root.anchoredPosition = new Vector2(0f, 96f);
            root.sizeDelta = new Vector2(HudWidth, HudHeight + HudLabelHeight);

            TMP_FontAsset font = TMP_Settings.defaultFontAsset;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(root, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.offsetMin = new Vector2(0f, -HudLabelHeight);
            labelRect.offsetMax = Vector2.zero;
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                label.font = font;
            }

            label.fontSizeMin = 20f;
            label.fontSizeMax = 34f;
            label.enableAutoSizing = true;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            label.color = new Color(0.92f, 0.92f, 0.96f);

            var backdropGo = new GameObject("BarBackdrop", typeof(RectTransform));
            backdropGo.transform.SetParent(root, false);
            var backdropRect = (RectTransform)backdropGo.transform;
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = new Vector2(1f, 0f);
            backdropRect.pivot = new Vector2(0.5f, 0f);
            backdropRect.sizeDelta = new Vector2(0f, HudHeight);
            var backdrop = backdropGo.AddComponent<Image>();
            backdrop.color = new Color(0.05f, 0.05f, 0.08f, 0.7f);
            backdrop.raycastTarget = false;

            var barGo = new GameObject("Bar", typeof(RectTransform));
            barGo.transform.SetParent(backdropRect, false);
            var barRect = (RectTransform)barGo.transform;
            barRect.anchorMin = Vector2.zero;
            barRect.anchorMax = Vector2.one;
            barRect.offsetMin = new Vector2(3f, 3f);
            barRect.offsetMax = new Vector2(-3f, -3f);
            var bar = barGo.AddComponent<Image>();
            bar.raycastTarget = false;
            bar.type = Image.Type.Filled;
            bar.fillMethod = Image.FillMethod.Horizontal;
            bar.fillOrigin = (int)Image.OriginHorizontal.Left;
            bar.fillAmount = 1f;

            var hud = rootGo.AddComponent<CansOrderLocalHud>();
            var serialized = new SerializedObject(hud);
            serialized.FindProperty("label").objectReferenceValue = label;
            serialized.FindProperty("bar").objectReferenceValue = bar;
            serialized.FindProperty("stageState").objectReferenceValue =
                Object.FindAnyObjectByType<Igruha.Core.Minigame.MinigameStageState>(FindObjectsInactive.Include);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private const string FanfarePath = PrefabFolder + "/SolvedFanfare.prefab";

        /// <summary>
        /// Фанфара собравшему: конфетти над его клеткой и вспышка.
        ///
        /// Момент важный: его клетка остаётся висеть, а все остальные
        /// в эту же секунду уезжают вниз. Без акцента это просто тихое
        /// движение геометрии.
        ///
        /// Системные партиклы и точечный свет — заготовка каркаса;
        /// финальный VFX и звук приезжают на арт-фазе (9.22, 9.23).
        /// </summary>
        private static GameObject BuildFanfarePrefab()
        {
            var root = new GameObject("SolvedFanfare");
            try
            {
                var ps = root.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.duration = 1.2f;
                main.loop = false;
                main.startLifetime = 2.2f;
                main.startSpeed = 4.5f;
                main.startSize = 0.09f;
                main.gravityModifier = 0.9f;
                main.maxParticles = 120;
                main.stopAction = ParticleSystemStopAction.None;

                var emission = ps.emission;
                emission.rateOverTime = 0f;
                emission.SetBurst(0, new ParticleSystem.Burst(0f, 90));

                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 32f;
                shape.radius = 0.25f;
                shape.rotation = new Vector3(-90f, 0f, 0f);

                // Конфетти разноцветные: однотонный всплеск с такого
                // расстояния читается как дым, а не как праздник.
                var colorModule = ps.colorOverLifetime;
                colorModule.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(1f, 0.85f, 0.25f), 0f),
                        new GradientColorKey(new Color(0.35f, 0.8f, 1f), 0.5f),
                        new GradientColorKey(new Color(1f, 0.4f, 0.7f), 1f)
                    },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
                colorModule.color = new ParticleSystem.MinMaxGradient(gradient);

                var rotation = ps.rotationOverLifetime;
                rotation.enabled = true;
                rotation.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");

                var flashGo = new GameObject("Flash");
                flashGo.transform.SetParent(root.transform, false);
                var flash = flashGo.AddComponent<Light>();
                flash.type = LightType.Point;
                flash.color = new Color(1f, 0.9f, 0.5f);
                flash.range = 5f;
                flash.intensity = 3.2f;

                return PrefabUtility.SaveAsPrefabAsset(root, FanfarePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }



        /// <summary>
        /// Кнопка подтверждения. Коллайдер только на корпусе: вместе
        /// с доской полки это два коллайдера на клетку — выборка
        /// <c>PlayerInteractor</c> на 16 мест это держит с запасом.
        ///
        /// Сама кнопка — ребёнок корня полки, поэтому
        /// <c>GetComponentInParent</c> от её коллайдера находит именно кнопку,
        /// а не полку: ближайший предок выигрывает.
        /// </summary>
        private static void BuildConfirmButton(Transform root, CanShelf shelf, float shelfLength, float z)
        {
            var buttonGo = new GameObject("ConfirmButton");
            buttonGo.transform.SetParent(root, false);
            buttonGo.transform.localPosition = new Vector3(shelfLength * 0.5f + ButtonSize * 0.5f, ShelfHeight, z);

            GameObject casing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            casing.name = "Casing";
            casing.transform.SetParent(buttonGo.transform, false);
            casing.transform.localScale = Vector3.one * ButtonSize;

            GameObject lampGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lampGo.name = "Lamp";
            lampGo.transform.SetParent(buttonGo.transform, false);
            lampGo.transform.localPosition = new Vector3(0f, ButtonSize * 0.5f + ButtonLampHeight * 0.5f, 0f);
            lampGo.transform.localScale = new Vector3(ButtonLampDiameter, ButtonLampHeight * 0.5f, ButtonLampDiameter);
            Object.DestroyImmediate(lampGo.GetComponent<Collider>());

            var beaconGo = new GameObject("Beacon");
            beaconGo.transform.SetParent(buttonGo.transform, false);
            beaconGo.transform.localPosition = new Vector3(0f, ButtonSize * 0.5f + 0.3f, 0f);
            var beacon = beaconGo.AddComponent<Light>();
            beacon.type = LightType.Point;
            // Свет заметный, но не заливающий: в клетке читаются цвета банок,
            // и залитая светом полка стала бы одним пятном.
            beacon.range = 2.4f;
            beacon.intensity = 1.4f;
            beacon.enabled = false;

            var button = buttonGo.AddComponent<CanConfirmButton>();
            var serialized = new SerializedObject(button);
            serialized.FindProperty("lamp").objectReferenceValue = lampGo.GetComponent<Renderer>();
            serialized.FindProperty("beacon").objectReferenceValue = beacon;
            serialized.FindProperty("shelf").objectReferenceValue = shelf;
            serialized.ApplyModifiedPropertiesWithoutUndo();
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

                // Кнопка подтверждения на торце полки, справа от игрока,
                // смотрящего на полку.
                BuildConfirmButton(root.transform, shelf, length, z);

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
