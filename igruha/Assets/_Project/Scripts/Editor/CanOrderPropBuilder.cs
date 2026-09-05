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
    /// конфига и может измениться плейтестом. Здесь строится доска — и префаб
    /// банки, ссылку на который билдер кладёт в полку сам.
    ///
    /// <b>Шатёр этот билдер не трогает.</b> Арена — общий слой
    /// <see cref="CircusDress"/> / <see cref="CircusPalette"/> /
    /// <see cref="CircusEnvironment"/>, одетый и запечённый на «Секундомере»:
    /// любая правка там меняет обе игры и требует пересборки и запекания обеих
    /// сцен. Здесь только содержимое <c>PropSlot</c>.
    /// </summary>
    internal static class CanOrderPropBuilder
    {
        private const string SceneName = "CansOrder";
        private const string ArenaConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/CircusArenaConfig.asset";
        private const string GameConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/CansOrderConfig.asset";
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

        // ── дресс (подфазы 4.1 и 4.2) ─────────────────────────────────────

        private const string CanPrefabPath = PrefabFolder + "/Can.prefab";
        private const string ArtFolder = "Assets/_Project/Art/CansOrder/Symbols";
        private const string MaterialFolder = "Assets/_Project/Materials/CansOrder";

        private const string Carnival = "Assets/Synty/PolygonHorrorCarnival/Prefabs/Props/";
        private const string Casino = "Assets/Synty/PolygonCasino/Prefabs/Props/";

        /// <summary>
        /// Полка — ярмарочная лавка того же шатра. Родная высота 0.97 м против
        /// нужных 0.95: вертикального растяжения нет вовсе.
        /// </summary>
        private const string BenchPath = Carnival + "SM_Prop_Stall_Bench_01.prefab";

        /// <summary>
        /// Банка — из киоска «сбей банки» (<c>SM_Prop_Can_Toss_01</c>) этого же
        /// шатра: игра играет реквизитом собственной ярмарки, и меш сидит на
        /// общем атласе арены, то есть батчится с ней.
        /// </summary>
        private const string CanModelPath = Carnival + "SM_Prop_Can_Toss_Can_01.prefab";

        /// <summary>Габарит модели банки, м. По нему считается посадка знака на грань корпуса.</summary>
        private static readonly Vector3 CanModelSize = new Vector3(0.149f, 0.204f, 0.129f);

        /// <summary>
        /// Кнопка — колокол «готово», а не тот же красный купол, что стоит
        /// в клетке «Секундомера»: две игры в одной и той же клетке обязаны
        /// отличаться с первого взгляда.
        /// </summary>
        private const string BellPath = Casino + "SM_Prop_Reception_Bell_01.prefab";

        /// <summary>Габариты банки. Билдер пишет их в полку сам: два источника правды разъезжаются.</summary>
        private const float CanHeight = 0.24f;
        private const float CanDiameter = 0.14f;

        /// <summary>
        /// Задник ряда. Не украшение: без него силуэты банок читаются на фоне
        /// прутьев, зала и афиш. Высота <b>ниже верха банок</b> — всё, что выше,
        /// лезет на луч зрения к табло (разбор в спеке 14.5, 14.7).
        /// </summary>
        private const float BackdropHeight = 0.22f;
        private const float BackdropThickness = 0.04f;
        private const int BackdropStripes = 7;

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
        ///
        /// <b>Вид — колокол, коробка та же.</b> Корпус 0.18³ остаётся со своим
        /// коллайдером и гасит рендерер, внутрь садится модель. Коллайдер не
        /// тронут ни размером, ни положением.
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
            casing.GetComponent<MeshRenderer>().enabled = false;

            // Колокол стоит НА доске полки, а не в центре коробки: коробка —
            // это объём поиска интерактива, и её середина приходится на
            // полтолщины ниже поверхности.
            float bellHeight = ButtonSize * 1.25f;
            GameObject bell = CircusDress.Prop(buttonGo.transform, "Bell", BellPath,
                buttonGo.transform.position - Vector3.up * (ButtonSize * 0.5f), 0f, bellHeight);

            // Жемчужина считается от НИЗА колокола, а не от центра коробки:
            // колокол посажен на полтолщины ниже, и от центра лампа улетала
            // в воздух над ним.
            float lampY = -ButtonSize * 0.5f + bellHeight * (bell != null ? 0.96f : 0.5f);

            // Лампа — жемчужина на макушке колокола, а не сам колокол: цвет ей
            // назначает CanConfirmButton через MaterialPropertyBlock, и покраска
            // всего колокола стёрла бы его золото в глухой серый на всё время
            // круга. Материал белый и это не описка — под назначенным цветом
            // любая своя окраска перемножилась бы.
            GameObject lampGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lampGo.name = "Lamp";
            lampGo.transform.SetParent(buttonGo.transform, false);
            lampGo.transform.localPosition = new Vector3(0f, lampY, 0f);
            lampGo.transform.localScale = new Vector3(ButtonLampDiameter * 0.55f, ButtonLampHeight * 1.1f, ButtonLampDiameter * 0.55f);
            Object.DestroyImmediate(lampGo.GetComponent<Collider>());
            lampGo.GetComponent<Renderer>().sharedMaterial = FlatMaterial("CO_LampWhite", Color.white);

            var beaconGo = new GameObject("Beacon");
            beaconGo.transform.SetParent(buttonGo.transform, false);
            beaconGo.transform.localPosition = new Vector3(0f, lampY + 0.22f, 0f);
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

            GameObject canPrefab = BuildCanPrefab();

            float length = ShelfLengthBodyWidths * CircusArenaConfig.MetersPerBodyWidth;
            float z = InnerWallOffset(config);

            var root = new GameObject("CanShelf");
            try
            {
                // Доска. Коллайдер на ней и только на ней: PlayerInteractor ищет
                // интерактив через OverlapSphere с буфером на 16 коллайдеров,
                // а в клетке их и так семь. Отдельные коллайдеры на слотах или
                // банках вытеснили бы из выборки кнопку подтверждения.
                //
                // Рендерер гаснет — её место занимает ярмарочная лавка. Сама
                // коробка остаётся: она и есть объём, который находит поиск.
                GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
                board.name = "Board";
                board.transform.SetParent(root.transform, false);
                board.transform.localPosition = new Vector3(0f, ShelfHeight - BoardThickness * 0.5f, z);
                board.transform.localScale = new Vector3(length, BoardThickness, ShelfDepth);
                board.GetComponent<MeshRenderer>().enabled = false;

                // Кронштейны блокаута. Рендереры гасятся: у лавки свои ноги,
                // а коробки остаются на месте как запись геометрии каркаса.
                for (int i = 0; i < 2; i++)
                {
                    float x = (i == 0 ? -1f : 1f) * (length * 0.5f - SupportThickness);
                    GameObject support = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    support.name = i == 0 ? "Support_L" : "Support_R";
                    support.transform.SetParent(root.transform, false);
                    support.transform.localPosition = new Vector3(x, (ShelfHeight - BoardThickness) * 0.5f, z + ShelfDepth * 0.25f);
                    support.transform.localScale = new Vector3(SupportThickness, ShelfHeight - BoardThickness, SupportThickness);
                    Object.DestroyImmediate(support.GetComponent<Collider>());
                    support.GetComponent<MeshRenderer>().enabled = false;
                }

                BuildBench(root.transform, length, z);
                BuildBackdrop(root.transform, length, z);

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
                // Габариты банки пишет билдер, а не инспектор: по ним же он
                // сажает знак на грань корпуса, и два источника правды здесь
                // разъехались бы молча.
                serialized.FindProperty("canDiameter").floatValue = CanDiameter;
                serialized.FindProperty("canHeight").floatValue = CanHeight;
                serialized.FindProperty("canPrefab").objectReferenceValue = canPrefab;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Полка — ярмарочная лавка. Растягивается по замеренным габаритам
        /// коробки блокаута, а не по «на глаз»: длина 2.16 и глубина 0.42
        /// заданы коллайдером доски, и вид обязан сесть в них.
        /// </summary>
        private static void BuildBench(Transform root, float length, float z)
        {
            if (!TryLoad(BenchPath, out GameObject prefab))
            {
                return;
            }

            var bench = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            bench.name = "Bench";
            bench.transform.localRotation = Quaternion.identity;
            CircusDress.MarkAsScenery(bench, true);

            // Лавка длиннее доски ровно на вылет кнопки. Кнопка стоит на торце
            // полки (спека 3.2), то есть за краем коллайдера доски: на серой
            // коробке этого не было видно, а колокол повисал в воздухе над ямой.
            // Коллайдеров правка не касается — лавка их не имеет вовсе.
            float right = length * 0.5f + ButtonSize * 1.4f;
            float left = -length * 0.5f;
            FitToBox(bench,
                new Vector3((left + right) * 0.5f, ShelfHeight * 0.5f, z),
                new Vector3(right - left, ShelfHeight, ShelfDepth * 1.05f));
        }

        /// <summary>
        /// Задник ряда: полосатая доска за банками, в тон ткани шатра.
        ///
        /// Без него силуэты банок читаются на фоне прутьев, зала и афиш — ровно
        /// то, что показала проба 4.0. <b>Высота 0.22 м, ниже верха банок</b>
        /// (0.24): всё, что выше, попадает на луч зрения к табло, а табло —
        /// вся игра.
        /// </summary>
        private static void BuildBackdrop(Transform root, float length, float z)
        {
            float back = z + ShelfDepth * 0.5f - BackdropThickness;
            float width = length * 0.94f;

            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Backdrop";
            panel.transform.SetParent(root, false);
            panel.transform.localPosition = new Vector3(0f, ShelfHeight + BackdropHeight * 0.5f, back);
            panel.transform.localScale = new Vector3(width, BackdropHeight, BackdropThickness);
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            Paint(panel, CircusPalette.Tone.CanvasStripe);

            for (int i = 0; i < BackdropStripes; i++)
            {
                GameObject stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stripe.name = $"Backdrop_Stripe_{i + 1}";
                stripe.transform.SetParent(root, false);
                stripe.transform.localPosition = new Vector3(
                    Mathf.Lerp(-width * 0.42f, width * 0.42f, i / (float)(BackdropStripes - 1)),
                    ShelfHeight + BackdropHeight * 0.5f,
                    back - BackdropThickness * 0.6f);
                stripe.transform.localScale = new Vector3(width / (BackdropStripes * 2.2f), BackdropHeight, BackdropThickness * 0.5f);
                Object.DestroyImmediate(stripe.GetComponent<Collider>());
                Paint(stripe, CircusPalette.Tone.CanvasCream);
            }
        }

        // ── банка ─────────────────────────────────────────────────────────

        /// <summary>
        /// Префаб банки: корпус модели пака плюс пять знаков, из которых
        /// <see cref="Can"/> включает один по идентификатору.
        ///
        /// <b>Знак — не косметика, а второй канал различения.</b> Замер по
        /// имитации Viénot–Brettel–Mollon: минимальное ΔE76 между пятью цветами
        /// палитры падает с 42.9 в норме до 12.5 на протанопии и 17.8 на
        /// дейтеранопии — по цвету банки не различаются вовсе. Палитрой это не
        /// лечится ни одной (перебор в спеке 14.2), поэтому знак стоит при любой.
        ///
        /// <b>Материал корпуса свой и плоский</b>, а не атласный материал пака:
        /// цвет ведёт <c>MaterialPropertyBlock</c>, и умножение тона на текстуру
        /// атласа дало бы грязь вместо чистого цвета.
        ///
        /// <b>Коллайдера нет.</b> Буфер <c>PlayerInteractor</c> на 16, в клетке
        /// уже семь своих, пять банок вытеснили бы кнопку.
        /// </summary>
        private static GameObject BuildCanPrefab()
        {
            var config = AssetDatabase.LoadAssetAtPath<CansOrderConfig>(GameConfigPath);
            if (config == null)
            {
                Debug.LogWarning($"CanOrderPropBuilder: не найден {GameConfigPath} — банка останется заготовкой блокаута");
                return null;
            }

            if (!TryLoad(CanModelPath, out GameObject model))
            {
                return null;
            }

            var root = new GameObject("Can");
            try
            {
                var can = root.AddComponent<Can>();

                var body = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                body.name = "Body";
                body.transform.localRotation = Quaternion.identity;
                CircusDress.MarkAsScenery(body, true);

                // Банка ужимается по высоте, пропорции модели сохраняются.
                float scale = CanHeight / CanModelSize.y;
                body.transform.localScale = Vector3.one * scale;
                // Слот стоит на середине высоты банки (CanShelf.Build), значит
                // корпус садится на полкорпуса ниже своего корня.
                AlignBottom(body, -CanHeight * 0.5f);

                Renderer bodyRenderer = null;
                foreach (var r in body.GetComponentsInChildren<Renderer>(true))
                {
                    r.sharedMaterial = FlatMaterial("CO_CanBody", Color.white);
                    if (bodyRenderer == null)
                    {
                        bodyRenderer = r;
                    }
                }

                int count = config.PaletteSize;
                var symbols = new GameObject[count];
                for (int i = 0; i < count; i++)
                {
                    symbols[i] = BuildSymbol(root.transform, i, config.GetCanKind(i).color, scale);
                    symbols[i].SetActive(i == 0);
                }

                var serialized = new SerializedObject(can);
                serialized.FindProperty("body").objectReferenceValue = bodyRenderer;
                SerializedProperty array = serialized.FindProperty("symbols");
                array.arraySize = count;
                for (int i = 0; i < count; i++)
                {
                    array.GetArrayElementAtIndex(i).objectReferenceValue = symbols[i];
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, CanPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Знак одной банки: выпуклый контур на боку и повтор на крышке.
        /// Бок читается из кадра полки, крышка — от третьего лица сверху.
        /// </summary>
        private static GameObject BuildSymbol(Transform root, int canId, Color canColor, float bodyScale)
        {
            var holder = new GameObject($"Symbol_{canId}");
            holder.transform.SetParent(root, false);

            Material ink = InkMaterial(canColor);
            Mesh mesh = SymbolMesh(canId);

            // Знак садится по НАСТОЯЩЕЙ грани корпуса, а не по номиналу слота:
            // модель 0.149 × 0.204 × 0.129, ужатая до высоты 0.24, в плане
            // 0.175 × 0.152 — полуглубина 0.076, а не 0.07. По номиналу знак
            // тонет в корпусе целиком (проверено пробой 4.0).
            float face = CanModelSize.z * 0.5f * bodyScale + 0.006f;

            var side = new GameObject("Side");
            side.transform.SetParent(holder.transform, false);
            side.transform.localPosition = new Vector3(0f, CanHeight * 0.02f, -face);
            // Поворот −90°, а не +90°: контур строится в плоскости XZ, его
            // «верх» — локальный +Z, и при +90° он уходит в мировой −Y,
            // то есть треугольник встаёт вершиной вниз.
            side.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            side.transform.localScale = new Vector3(0.095f, 0.014f, 0.095f);
            Attach(side, mesh, ink);

            var top = new GameObject("Top");
            top.transform.SetParent(holder.transform, false);
            top.transform.localPosition = new Vector3(0f, CanHeight * 0.51f, 0f);
            top.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            top.transform.localScale = new Vector3(0.082f, 0.010f, 0.082f);
            Attach(top, mesh, ink);

            return holder;
        }

        private static void Attach(GameObject go, Mesh mesh, Material material)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// Краска знака по светлоте корпуса. <b>Порог 0.62, а не 0.5:</b> на
        /// половине тушь доставалась фиолетовой банке (0.470) и зелёной (0.577),
        /// и знак на них тонул — видно на пробе 4.0. С этим порогом тёмный знак
        /// остаётся ровно у жёлтой, и это само по себе её пятое отличие.
        /// </summary>
        private static Material InkMaterial(Color canColor)
        {
            float luminance = 0.2126f * canColor.r + 0.7152f * canColor.g + 0.0722f * canColor.b;
            return luminance > 0.62f
                ? FlatMaterial("CO_Ink_Dark", new Color(0.12f, 0.10f, 0.10f))
                : FlatMaterial("CO_Ink_Cream", new Color(0.97f, 0.96f, 0.92f));
        }

        // ── контуры знаков ────────────────────────────────────────────────

        private static readonly string[] SymbolNames = { "Circle", "Triangle", "Square", "Star", "Cross" };

        /// <summary>
        /// Меш знака. Ассет заводится один раз на контур: банок до сорока
        /// в сцене, и генерировать им меши в рантайме значит сорок копий одного
        /// и того же.
        /// </summary>
        private static Mesh SymbolMesh(int canId)
        {
            string name = "CO_Sym_" + SymbolNames[Mathf.Clamp(canId, 0, SymbolNames.Length - 1)];
            string path = $"{ArtFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                return existing;
            }

            if (!Directory.Exists(ArtFolder))
            {
                Directory.CreateDirectory(ArtFolder);
                AssetDatabase.Refresh();
            }

            Mesh mesh = Prism(Contour(canId));
            mesh.name = name;
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            return mesh;
        }

        /// <summary>
        /// Контур знака в единичном круге. Порядок — как в палитре конфига,
        /// и это же контуры, которыми табло рисует расстановки: знак на полке
        /// и знак на табло обязаны быть одним и тем же знаком.
        /// </summary>
        private static Vector2[] Contour(int canId)
        {
            switch (canId)
            {
                case 0: return Regular(24, 0.50f, 0f);
                case 1: return Regular(3, 0.58f, 90f);
                case 2: return Regular(4, 0.53f, 45f);
                case 3: return Star(5, 0.58f, 0.26f);
                default: return Cross(0.60f, 0.19f, 45f);
            }
        }

        private static Vector2[] Regular(int sides, float radius, float phaseDeg)
        {
            var points = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = Mathf.Deg2Rad * (phaseDeg + 360f * i / sides);
                points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            }

            return points;
        }

        private static Vector2[] Star(int points, float outer, float inner)
        {
            var contour = new Vector2[points * 2];
            for (int i = 0; i < points * 2; i++)
            {
                float a = Mathf.Deg2Rad * (90f + 180f * i / points);
                float r = (i % 2 == 0) ? outer : inner;
                contour[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }

            return contour;
        }

        private static Vector2[] Cross(float arm, float half, float rotationDeg)
        {
            var contour = new[]
            {
                new Vector2(half, half), new Vector2(arm, half), new Vector2(arm, -half),
                new Vector2(half, -half), new Vector2(half, -arm), new Vector2(-half, -arm),
                new Vector2(-half, -half), new Vector2(-arm, -half), new Vector2(-arm, half),
                new Vector2(-half, half), new Vector2(-half, arm), new Vector2(half, arm)
            };

            float cos = Mathf.Cos(rotationDeg * Mathf.Deg2Rad);
            float sin = Mathf.Sin(rotationDeg * Mathf.Deg2Rad);
            for (int i = 0; i < contour.Length; i++)
            {
                contour[i] = new Vector2(
                    contour[i].x * cos - contour[i].y * sin,
                    contour[i].x * sin + contour[i].y * cos);
            }

            return contour;
        }

        /// <summary>
        /// Плоский контур в объём. Триангуляция веером от центра: все пять
        /// контуров звёздчатые относительно своего центра, поэтому веер корректен
        /// и для звезды, и для креста.
        /// </summary>
        private static Mesh Prism(Vector2[] contour)
        {
            int n = contour.Length;
            var vertices = new Vector3[(n + 1) * 2];
            vertices[0] = new Vector3(0f, 0.5f, 0f);
            vertices[n + 1] = new Vector3(0f, -0.5f, 0f);
            for (int i = 0; i < n; i++)
            {
                vertices[1 + i] = new Vector3(contour[i].x, 0.5f, contour[i].y);
                vertices[n + 2 + i] = new Vector3(contour[i].x, -0.5f, contour[i].y);
            }

            var triangles = new int[n * 12];
            int t = 0;
            for (int i = 0; i < n; i++)
            {
                int a = 1 + i;
                int b = 1 + (i + 1) % n;
                int a2 = n + 2 + i;
                int b2 = n + 2 + (i + 1) % n;

                triangles[t++] = 0; triangles[t++] = b; triangles[t++] = a;
                triangles[t++] = n + 1; triangles[t++] = a2; triangles[t++] = b2;
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = a2;
                triangles[t++] = b; triangles[t++] = b2; triangles[t++] = a2;
            }

            var mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ── утилиты дресса ────────────────────────────────────────────────

        private static bool TryLoad(string path, out GameObject prefab)
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"CanOrderPropBuilder: модель не найдена — {path}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Растянуть модель по габаритам коробки блокаута и посадить по её
        /// центру. Пивоты моделей Synty стоят где угодно — у лавки он смещён
        /// на 1.04 м по Z, и посадка по корню уводит её сквозь стену клетки
        /// в яму (проверено пробой 4.0).
        /// </summary>
        private static void FitToBox(GameObject go, Vector3 localCenter, Vector3 localSize)
        {
            if (!TryLocalBounds(go, out Bounds bounds) || bounds.size.sqrMagnitude < 0.0001f)
            {
                go.transform.localPosition = localCenter;
                return;
            }

            var scale = new Vector3(
                bounds.size.x > 0.0001f ? localSize.x / bounds.size.x : 1f,
                bounds.size.y > 0.0001f ? localSize.y / bounds.size.y : 1f,
                bounds.size.z > 0.0001f ? localSize.z / bounds.size.z : 1f);
            go.transform.localScale = scale;

            TryLocalBounds(go, out bounds);
            go.transform.localPosition += localCenter - bounds.center;
        }

        /// <summary>Посадить модель нижней гранью габарита на заданную высоту.</summary>
        private static void AlignBottom(GameObject go, float localBottomY)
        {
            if (!TryLocalBounds(go, out Bounds bounds))
            {
                return;
            }

            go.transform.localPosition += new Vector3(0f, localBottomY - bounds.min.y, 0f);
        }

        private static bool TryLocalBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            Transform parent = go.transform.parent;
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                Bounds local = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? local.min.x : local.max.x,
                        (c & 2) == 0 ? local.min.y : local.max.y,
                        (c & 4) == 0 ? local.min.z : local.max.z);
                    Vector3 world = filter.transform.TransformPoint(corner);
                    Vector3 point = parent != null ? parent.InverseTransformPoint(world) : world;
                    if (!any)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return any;
        }

        private static void Paint(GameObject go, CircusPalette.Tone tone)
        {
            Material material = CircusPalette.Get(tone);
            if (material == null)
            {
                return;
            }

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = material;
            }
        }

        /// <summary>Плоский URP-материал игры. Ассет заводится при первом обращении.</summary>
        private static Material FlatMaterial(string materialName, Color color)
        {
            string path = $"{MaterialFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    Debug.LogError("CanOrderPropBuilder: шейдер 'Universal Render Pipeline/Lit' не найден");
                    return null;
                }

                if (!Directory.Exists(MaterialFolder))
                {
                    Directory.CreateDirectory(MaterialFolder);
                    AssetDatabase.Refresh();
                }

                material = new Material(shader) { name = materialName };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor(BaseColorId, color);
            material.SetColor(LegacyColorId, color);
            material.SetFloat(SmoothnessId, 0.15f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    }
}
