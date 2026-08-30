using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Igruha.Core.Arena;
using Igruha.Core.Items;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Core.Spawning;
using Igruha.Core.Traps;
using Igruha.Minigames.CarryItem;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Сборка арены «Переноски предмета» по числам из <see cref="CarryItemConfig"/>.
    ///
    /// Расстановка руками означала бы, что смена ширины горлышка или глубины
    /// пропасти — это переделка уровня. Здесь это правка числа в ассете плюс
    /// пункт меню, и все камерные ограничения спеки 3.1 проверяются заново
    /// при каждой пересборке.
    ///
    /// Раскладка вдоль маршрута (ШИ, ось X), команда A на +Z, команда B на −Z:
    ///
    ///   −33 … −19  стартовые зоны 14 × 14, в каждой штабель
    ///   −19 …  −7  пропасть 1, 12 ШИ поперёк, по доске на команду
    ///    −7 … +13  общая площадка 20 × 40: завалы, горлышко, ловушки
    ///   +13 … +23  пропасть 2, 10 ШИ поперёк
    ///   +23 … +33  зоны баков 10 × 10
    ///
    /// Маршрут каждой команды идёт по z = ±7 — по линии её досок, штабеля и
    /// бака. Завалы сгоняют обе команды в горлышко на z = 0; обход по краю
    /// (z = ±18) длиннее ровно на разницу двух боковых ходов.
    /// </summary>
    internal static class CarryItemArenaBuilder
    {
        private const string ConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/CarryItemConfig.asset";
        private const string DefinitionPath = "Assets/_Project/Settings/Gameplay/Minigames/CarryItem.asset";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Minigames/CarryItem";
        private const string BottlePrefabPath = PrefabFolder + "/Bottle.prefab";
        private const string BrickPrefabPath = PrefabFolder + "/Brick.prefab";
        private const string TankPrefabPath = PrefabFolder + "/Tank.prefab";
        private const string StackPrefabPath = PrefabFolder + "/BottleStack.prefab";

        // Раскладка вдоль маршрута, ШИ. Сумма секций = 66, как в спеке 3.1.
        private const float StartMinX = -33f;
        private const float StartMaxX = -19f;
        private const float CommonMinX = -7f;
        private const float CommonMaxX = 13f;
        private const float TankMinX = 23f;
        private const float TankMaxX = 33f;

        /// <summary>Половина ширины игровой площадки, ШИ. Она же ширина общей площадки, 40 ШИ.</summary>
        private const float HalfWidth = 20f;

        /// <summary>
        /// Запас пола за стартовой зоной и за зоной бака, ШИ.
        ///
        /// Секции спеки задают <b>игровую</b> площадь, а камере нужно 4.5 м
        /// позади остановившегося игрока. У бака это не сходилось: от бака в
        /// центре десятиметровой зоны до торца оставалось 5 ШИ = 3.6 м, и
        /// сливающий получал камеру, прижатую к затылку, — ровно то, что
        /// случилось с Ведущим в «Экзамене». Запас идёт из тех 24 ШИ длины
        /// арены, которые спека и отвела под стены и реквизит.
        /// </summary>
        private const float EndMarginX = 5f;

        /// <summary>Линия маршрута команды, ШИ по Z. По ней стоят штабель, доски и бак.</summary>
        private const float RouteZ = 7f;

        /// <summary>Толщина завала вдоль маршрута, ШИ. Она же глубина горлышка: 6 ШИ = 4.32 м.</summary>
        private const float RubbleDepth = 6f;

        /// <summary>Начало завала по X, ШИ. Завал стоит в середине общей площадки.</summary>
        private const float RubbleMinX = 0f;

        /// <summary>Ширина обхода по краю, ШИ. Уже 4 ШИ бутыль в него не пройдёт.</summary>
        private const float BypassWidth = 4f;

        /// <summary>Насколько кучка метательного отнесена от линии маршрута, ШИ.</summary>
        private const float StashOffsetZ = 6f;

        /// <summary>
        /// По какой полосе горлышка идёт команда, ШИ от осевой. Больше размаха
        /// балки (половина её длины, 2 ШИ), меньше половины прохода (4 ШИ).
        /// </summary>
        private const float NeckLaneZ = 2.5f;

        private const float FloorThickness = 1f;
        private const float PlankThickness = 0.4f;
        private const float WallThickness = 1f;

        /// <summary>Сколько места нужно бутыли и несущему, чтобы протиснуться мимо балки, ШИ.</summary>
        private const float PassageClearance = 2f;

        /// <summary>Правило камеры: сколько метров свободного места нужно позади остановившегося игрока.</summary>
        private const float CameraClearance = 4.5f;

        [MenuItem("Igruha/Minigames/Rebuild Carry Item Arena")]
        private static void Rebuild()
        {
            var config = AssetDatabase.LoadAssetAtPath<CarryItemConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogError($"CarryItemArenaBuilder: не найден {ConfigPath}");
                return;
            }

            GameObject arena = GameObject.Find("_Arena");
            GameObject spawns = GameObject.Find("_Spawns");
            GameObject bounds = GameObject.Find("_Bounds");
            GameObject traps = GameObject.Find("_Traps");
            GameObject pickups = GameObject.Find("_Pickups");

            if (arena == null || spawns == null || bounds == null || traps == null || pickups == null)
            {
                Debug.LogError("CarryItemArenaBuilder: открой сцену CarryItem — не найдены _Arena/_Spawns/_Bounds/_Traps/_Pickups.");
                return;
            }

            int ground = LayerMask.NameToLayer("Ground");
            int cover = LayerMask.NameToLayer("Cover");
            if (ground < 0 || cover < 0)
            {
                Debug.LogError("CarryItemArenaBuilder: нет слоёв Ground/Cover — камера пройдёт сквозь стены.");
                return;
            }

            EnsurePrefabs(config);
            ClearTemplateContent(arena, spawns, bounds, traps, pickups);

            BuildFloors(arena.transform, config, ground);
            BuildWalls(arena.transform, config, ground);
            BuildPlanks(arena.transform, config, ground);
            BuildRubble(arena.transform, config, cover);
            BuildBotRoutes(arena.transform, config);
            BuildStacksAndTanks(arena.transform, config);
            BuildSpawns(spawns.transform, config);
            BuildBounds(bounds.transform, config);
            BuildTraps(traps.transform, config, ground);
            BuildPickups(pickups.transform, config);
            BuildProgressBar(config);
            WireManager(config);

            Physics.SyncTransforms();
            Report(config);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        /// <summary>
        /// Вымести начинку шаблона: пол, платформу, препятствие, его ловушки,
        /// подбираемый куб и точки спавна.
        ///
        /// Сносим всё, что не создаёт эта сборка. Оставленный кусок шаблона —
        /// это лишний коллайдер посреди арены, который потом ищут глазами:
        /// в «Ангелах» так пережил переделку пол шаблона под настоящим полом.
        /// </summary>
        private static void ClearTemplateContent(params GameObject[] groups)
        {
            for (int g = 0; g < groups.Length; g++)
            {
                Transform parent = groups[g].transform;
                for (int i = parent.childCount - 1; i >= 0; i--)
                {
                    Object.DestroyImmediate(parent.GetChild(i).gameObject);
                }
            }
        }

        // ========== ГЕОМЕТРИЯ ==========

        private static void BuildFloors(Transform root, CarryItemConfig config, int ground)
        {
            Transform group = ResetGroup(root, "Floors");

            // Три площадки маршрута. Между ними — пропасти, и пола там нет
            // намеренно: это и есть единственное, что убивает игрока.
            Slab(group, config, ground, "Floor_Start", StartMinX - EndMarginX, StartMaxX,
                -HalfWidth, HalfWidth, 0f);
            Slab(group, config, ground, "Floor_Common", CommonMinX, CommonMaxX, -HalfWidth, HalfWidth, 0f);
            Slab(group, config, ground, "Floor_Tanks", TankMinX, TankMaxX + EndMarginX,
                -HalfWidth, HalfWidth, 0f);

            // Дно пропастей: на нём стоит KillZone, и об него же считается
            // объявленная LDD пара секунд до респавна.
            float depth = -config.ChasmDepth;
            Slab(group, config, ground, "Floor_Chasm_1", StartMaxX, CommonMinX, -HalfWidth, HalfWidth, depth);
            Slab(group, config, ground, "Floor_Chasm_2", CommonMaxX, TankMinX, -HalfWidth, HalfWidth, depth);
        }

        private static void BuildWalls(Transform root, CarryItemConfig config, int ground)
        {
            Transform group = ResetGroup(root, "Walls");

            float height = config.WallHeight;
            float startEdge = StartMinX - EndMarginX;
            float tankEdge = TankMaxX + EndMarginX;
            float minX = startEdge - WallThickness;
            float maxX = tankEdge + WallThickness;

            // Торцы: за штабелем и за баком, с отступом на правило камеры.
            // Расстояние до них проверяется отчётом.
            Box(group, config, ground, "Wall_Start", startEdge - WallThickness, startEdge,
                -HalfWidth - WallThickness, HalfWidth + WallThickness, 0f, height);
            Box(group, config, ground, "Wall_Tanks", tankEdge, tankEdge + WallThickness,
                -HalfWidth - WallThickness, HalfWidth + WallThickness, 0f, height);

            // Борта во всю длину, включая пропасти: свалившийся не должен
            // укатиться из-под арены.
            Box(group, config, ground, "Wall_SideA", minX, maxX, HalfWidth, HalfWidth + WallThickness,
                -config.ChasmDepth, height + config.ChasmDepth);
            Box(group, config, ground, "Wall_SideB", minX, maxX, -HalfWidth - WallThickness, -HalfWidth,
                -config.ChasmDepth, height + config.ChasmDepth);
        }

        private static void BuildPlanks(Transform root, CarryItemConfig config, int ground)
        {
            Transform group = ResetGroup(root, "Planks");

            float half = config.PlankWidth * 0.5f;

            // По доске на команду на каждой пропасти, по линии её маршрута.
            // К бортам не примыкают: вдоль доски камера отходит назад, и
            // упереться ей не во что (спека 3.3).
            Box(group, config, ground, "Plank_1_A", StartMaxX, CommonMinX, RouteZ - half, RouteZ + half,
                -PlankThickness, PlankThickness);
            Box(group, config, ground, "Plank_1_B", StartMaxX, CommonMinX, -RouteZ - half, -RouteZ + half,
                -PlankThickness, PlankThickness);
            Box(group, config, ground, "Plank_2_A", CommonMaxX, TankMinX, RouteZ - half, RouteZ + half,
                -PlankThickness, PlankThickness);
            Box(group, config, ground, "Plank_2_B", CommonMaxX, TankMinX, -RouteZ - half, -RouteZ + half,
                -PlankThickness, PlankThickness);
        }

        /// <summary>
        /// Завалы: стена поперёк общей площадки с одним свободным проходом по
        /// центру и обходом по обоим краям.
        ///
        /// Проход один потому, что площадка в 40 ШИ иначе разводит команды по
        /// краям и они не встречаются ни разу за раунд — саботаж вырождается в
        /// метание кирпичей издалека. Обход обязан существовать: без него
        /// команда, у которой на горлышке стоит соперник, просто заперта.
        /// </summary>
        private static void BuildRubble(Transform root, CarryItemConfig config, int cover)
        {
            Transform group = ResetGroup(root, "Rubble");

            float neckHalf = config.NeckWidth * 0.5f;
            float bypassInner = HalfWidth - BypassWidth;
            float maxX = RubbleMinX + RubbleDepth;

            Box(group, config, cover, "Rubble_A", RubbleMinX, maxX, neckHalf, bypassInner,
                0f, config.RubbleHeight);
            Box(group, config, cover, "Rubble_B", RubbleMinX, maxX, -bypassInner, -neckHalf,
                0f, config.RubbleHeight);

        }

        /// <summary>
        /// Маршруты болванок соло-теста: доска через первую пропасть,
        /// горлышко, доска через вторую. Зачем они нужны — в
        /// <see cref="CarryItemBotRoute"/>.
        /// </summary>
        private static void BuildBotRoutes(Transform root, CarryItemConfig config)
        {
            Transform group = ResetGroup(root, "BotRoutes");

            MakeRoute(group, config, "Route_A", RouteZ);
            MakeRoute(group, config, "Route_B", -RouteZ);
        }

        private static void MakeRoute(Transform parent, CarryItemConfig config, string name, float routeZ)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            float neckExitX = RubbleMinX + RubbleDepth + 2f;

            // Через горлышко команда идёт по своей половине прохода, а не по
            // осевой. Осевая — худшая линия из возможных: там ходит балка, и
            // там же в лоб встречается соперник, идущий ровно так же. Живой
            // игрок жмётся к своей стороне; болванка, идущая по центру,
            // меряет не игру, а собственную наивность.
            //
            // Смещение больше досягаемости балки (половина её длины) и меньше
            // половины горлышка — то есть в проходе, но вне размаха.
            float neckZ = Mathf.Sign(routeZ) * NeckLaneZ;

            // Ворота идут строго от штабеля к баку: обратный путь читается тем
            // же списком с конца.
            var points = new[]
            {
                MakeMarker(root.transform, config, "Plank1_Near", StartMaxX - 1.5f, routeZ),
                MakeMarker(root.transform, config, "Plank1_Far", CommonMinX + 1.5f, routeZ),
                MakeMarker(root.transform, config, "Neck_In", RubbleMinX - 2f, neckZ),
                MakeMarker(root.transform, config, "Neck_Out", neckExitX, neckZ),
                MakeMarker(root.transform, config, "Plank2_Near", CommonMaxX - 1.5f, routeZ),
                MakeMarker(root.transform, config, "Plank2_Far", TankMinX + 1.5f, routeZ)
            };

            root.AddComponent<CarryItemBotRoute>().SetWaypoints(points);
        }

        private static Transform MakeMarker(Transform parent, CarryItemConfig config, string name, float x, float z)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(config.ToMeters(x), 0f, config.ToMeters(z));
            return go.transform;
        }

        private static void BuildStacksAndTanks(Transform root, CarryItemConfig config)
        {
            Transform group = ResetGroup(root, "TeamProps");

            float stackX = (StartMinX + StartMaxX) * 0.5f;
            float tankX = (TankMinX + TankMaxX) * 0.5f;

            PlacePrefab(group, StackPrefabPath, "Stack_A", config, stackX, RouteZ, 0f);
            PlacePrefab(group, StackPrefabPath, "Stack_B", config, stackX, -RouteZ, 0f);
            PlacePrefab(group, TankPrefabPath, "Tank_A", config, tankX, RouteZ, 0f);
            PlacePrefab(group, TankPrefabPath, "Tank_B", config, tankX, -RouteZ, 0f);
        }

        /// <summary>
        /// Точки спавна — <b>перед</b> штабелем, лицом к нему.
        ///
        /// Не за ним: позади стоящего у штабеля игрока камере нужно 4.5 м, а от
        /// торцевой стены до штабеля их всего 7 ШИ. Встав между стеной и
        /// штабелем, игрок начинал бы раунд с камерой, прижатой к затылку, —
        /// ровно то, что случилось с Ведущим в «Экзамене».
        /// </summary>
        private static void BuildSpawns(Transform root, CarryItemConfig config)
        {
            Transform group = ResetGroup(root, "Points");

            float spawnX = StartMaxX - 3f;
            float halfZone = config.StartZoneSize * 0.5f;

            for (int i = 0; i < 4; i++)
            {
                float offset = Mathf.Lerp(-halfZone * 0.45f, halfZone * 0.45f, i / 3f);
                MakeSpawn(group, config, $"Spawn_A_{i + 1}", SpawnRole.TeamA, spawnX, RouteZ + offset, 270f);
                MakeSpawn(group, config, $"Spawn_B_{i + 1}", SpawnRole.TeamB, spawnX, -RouteZ + offset, 270f);
            }

            // Спавнер раздаёт точки одной роли; команды разводит уже мини-игра.
            var spawner = Object.FindFirstObjectByType<PlayerSpawner>();
            if (spawner != null)
            {
                var so = new SerializedObject(spawner);
                so.FindProperty("defaultRole").enumValueIndex = (int)SpawnRole.TeamA;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Зоны падения. Тонкий триггер у самого дна не годится: падающий с
        /// высоты набирает такую скорость, что за один такт физики проходит
        /// его насквозь и продолжает лететь вечно — так в первом же прогоне
        /// улетел за −33 км игрок, сбитый балкой у второй пропасти.
        ///
        /// Поэтому триггер заполняет пропасть целиком, от дна до уровня чуть
        /// ниже пола, а под всей ареной лежит вторая, общая: она ловит всё,
        /// что промахнулось мимо пропастей — вылетевших за борт, выбитых с
        /// доски, унесённых струёй.
        /// </summary>
        private static void BuildBounds(Transform root, CarryItemConfig config)
        {
            Transform group = ResetGroup(root, "KillZones");

            MakeKillZone(group, config, "KillZone_Chasm_1", StartMaxX, CommonMinX,
                -config.ChasmDepth, -1f, HalfWidth);
            MakeKillZone(group, config, "KillZone_Chasm_2", CommonMaxX, TankMinX,
                -config.ChasmDepth, -1f, HalfWidth);

            float bottom = -config.ChasmDepth * 2f;
            MakeKillZone(group, config, "KillZone_Bottom",
                StartMinX - EndMarginX * 2f, TankMaxX + EndMarginX * 2f,
                bottom - config.ChasmDepth, bottom, HalfWidth * 2f);
        }

        /// <summary>
        /// Три ловушки, ни одной кнопки. Балка над горлышком, тачка на подходе
        /// к нему, труба на выходе — все на осевой линии z = 0, поэтому обе
        /// команды получают от них ровно одинаково.
        /// </summary>
        private static void BuildTraps(Transform root, CarryItemConfig config, int ground)
        {
            Transform group = ResetGroup(root, "CarryItemTraps");

            BuildBeam(group, config, ground);
            BuildCart(group, config, ground);
            BuildPipe(group, config);
        }

        private static void BuildBeam(Transform group, CarryItemConfig config, int ground)
        {
            // Длина балки — ровно доля ширины горлышка из конфига. Она и
            // задаёт свободное окно на проход: сколько времени за оборот
            // балка не перекрывает нужный для бутыли зазор.
            float length = config.NeckWidth * config.BeamNeckCoverage;

            var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = "SwingingBeam";
            beam.layer = ground;
            beam.transform.SetParent(group, false);
            beam.transform.position = new Vector3(
                config.ToMeters(RubbleMinX + RubbleDepth * 0.5f),
                config.HandleHeight,
                0f);
            beam.transform.localScale = new Vector3(
                config.ToMeters(0.5f), config.ToMeters(0.5f), config.ToMeters(length));

            Object.DestroyImmediate(beam.GetComponent<BoxCollider>());
            var trigger = beam.AddComponent<BoxCollider>();
            trigger.isTrigger = true;

            var trap = beam.AddComponent<SwingingBeamTrap>();
            var so = new SerializedObject(trap);
            so.FindProperty("radius").floatValue = 0f;
            so.FindProperty("period").floatValue = config.BeamPeriod;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildCart(Transform group, CarryItemConfig config, int ground)
        {
            var cart = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cart.name = "TippingCart";
            cart.layer = ground;
            cart.transform.SetParent(group, false);
            cart.transform.position = new Vector3(config.ToMeters(RubbleMinX - 4f), config.ToMeters(0.25f), 0f);
            cart.transform.localScale = new Vector3(
                config.ToMeters(2.5f), config.ToMeters(0.5f), config.ToMeters(config.NeckWidth));

            Object.DestroyImmediate(cart.GetComponent<BoxCollider>());
            var trigger = cart.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(1f, 4f, 1f);

            var spring = cart.AddComponent<SpringTrap>();
            var springSo = new SerializedObject(spring);
            springSo.FindProperty("launchForce").floatValue = config.CartLaunchForce;
            springSo.FindProperty("cooldown").floatValue = config.CartPeriod * 0.5f;
            springSo.ApplyModifiedPropertiesWithoutUndo();

            var driver = cart.AddComponent<PeriodicTrapDriver>();
            var driverSo = new SerializedObject(driver);
            driverSo.FindProperty("trap").objectReferenceValue = spring;
            driverSo.FindProperty("period").floatValue = config.CartPeriod;
            driverSo.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildPipe(Transform group, CarryItemConfig config)
        {
            var pipe = new GameObject("BurstPipe");
            pipe.transform.SetParent(group, false);
            pipe.transform.position = new Vector3(
                config.ToMeters(RubbleMinX + RubbleDepth + 4f), config.ToMeters(1f), 0f);

            var box = pipe.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(
                config.ToMeters(3f), config.ToMeters(2f), config.ToMeters(config.NeckWidth));

            var zone = pipe.AddComponent<PushZone>();
            var so = new SerializedObject(zone);

            // Сила струи — доля от удара проекта: сбивает курс, но не роняет.
            so.FindProperty("force").floatValue = PunchForce() * config.PushZoneForceFactor;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Толкает поперёк маршрута, одинаково для обеих команд.
            pipe.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
        }

        private static void BuildPickups(Transform root, CarryItemConfig config)
        {
            Transform group = ResetGroup(root, "Stashes");

            var brick = AssetDatabase.LoadAssetAtPath<PickupItem>(BrickPrefabPath);

            // По кучке с каждой стороны маршрута, до завала и после него:
            // за кирпичом надо отклониться и потерять время.
            MakeStash(group, config, brick, "Stash_A_Near", RubbleMinX - 4f, RouteZ + StashOffsetZ);
            MakeStash(group, config, brick, "Stash_B_Near", RubbleMinX - 4f, -RouteZ - StashOffsetZ);
            MakeStash(group, config, brick, "Stash_A_Far", RubbleMinX + RubbleDepth + 4f, RouteZ + StashOffsetZ);
            MakeStash(group, config, brick, "Stash_B_Far", RubbleMinX + RubbleDepth + 4f, -RouteZ - StashOffsetZ);
        }

        // ========== UI ==========

        /// <summary>
        /// Двойная полоса прогресса поверх обычного HUD раунда. Строится
        /// сборкой, а не руками в инспекторе: полоса живёт в сцене, а сцены
        /// не переживают слияние веток, и собранная руками разъедется первой.
        /// </summary>
        private static void BuildProgressBar(CarryItemConfig config)
        {
            var canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogWarning("CarryItemArenaBuilder: в сцене нет Canvas — полосу прогресса вешать некуда.");
                return;
            }

            Transform existing = canvas.transform.Find("TeamProgress");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var root = new GameObject("TeamProgress", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);

            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -20f);
            rect.sizeDelta = new Vector2(760f, 90f);

            var bar = root.AddComponent<Igruha.Core.UI.TeamProgressBar>();
            var so = new SerializedObject(bar);

            BuildBarRow(root.transform, "A", -180f, so, "fillA", "labelA", "frameA");
            BuildBarRow(root.transform, "B", 180f, so, "fillB", "labelB", "frameB");

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildBarRow(Transform parent, string suffix, float offsetX, SerializedObject so,
            string fillField, string labelField, string frameField)
        {
            Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            var frame = new GameObject($"Frame_{suffix}", typeof(RectTransform));
            frame.transform.SetParent(parent, false);
            var frameRect = frame.GetComponent<RectTransform>();
            frameRect.anchoredPosition = new Vector2(offsetX, -20f);
            frameRect.sizeDelta = new Vector2(340f, 34f);

            var frameImage = frame.AddComponent<Image>();
            frameImage.sprite = sprite;
            frameImage.type = Image.Type.Sliced;
            frameImage.color = new Color(0f, 0f, 0f, 0.55f);

            var fill = new GameObject($"Fill_{suffix}", typeof(RectTransform));
            fill.transform.SetParent(frame.transform, false);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(3f, 3f);
            fillRect.offsetMax = new Vector2(-3f, -3f);

            var fillImage = fill.AddComponent<Image>();
            fillImage.sprite = sprite;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillAmount = 0f;

            var label = new GameObject($"Label_{suffix}", typeof(RectTransform));
            label.transform.SetParent(frame.transform, false);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var text = label.AddComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 20f;
            text.color = Color.white;
            text.raycastTarget = false;
            text.SetText("0 / 0");

            so.FindProperty(fillField).objectReferenceValue = fillImage;
            so.FindProperty(labelField).objectReferenceValue = text;
            so.FindProperty(frameField).objectReferenceValue = frameImage;
        }

        // ========== ПРЕФАБЫ ==========

        private static void EnsurePrefabs(CarryItemConfig config)
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Prefabs/Minigames", "CarryItem");
            }

            BuildBottlePrefab(config);
            BuildBrickPrefab(config);
            BuildTankPrefab(config);
            BuildStackPrefab(config);
        }

        /// <summary>
        /// Бутыль. <b>Начало координат — в дне</b>: модель наклона вращает объект
        /// вокруг основания, и любое другое положение пивота дало бы вместо
        /// крена подпрыгивание.
        /// </summary>
        private static void BuildBottlePrefab(CarryItemConfig config)
        {
            var root = new GameObject("Bottle");

            float height = config.HandleHeight * 1.25f;
            float radius = config.HandleRadius * 0.7f;

            var shell = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shell.name = "Shell";
            shell.transform.SetParent(root.transform, false);
            shell.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            shell.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            Object.DestroyImmediate(shell.GetComponent<Collider>());

            // Уровень воды растёт от дна, поэтому масштабируется пустышка-пивот,
            // а не сам цилиндр: у примитива пивот в середине, и он рос бы в обе
            // стороны сразу.
            var waterPivot = new GameObject("WaterPivot");
            waterPivot.transform.SetParent(root.transform, false);
            waterPivot.transform.localPosition = Vector3.zero;

            var water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            water.name = "WaterMesh";
            water.transform.SetParent(waterPivot.transform, false);
            water.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            water.transform.localScale = new Vector3(radius * 1.7f, height * 0.5f, radius * 1.7f);
            Object.DestroyImmediate(water.GetComponent<Collider>());

            var body = root.AddComponent<Rigidbody>();
            body.mass = 8f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, height * 0.5f, 0f);
            capsule.height = height;
            capsule.radius = radius;

            var carry = root.AddComponent<MultiCarryObject>();
            var carrySo = new SerializedObject(carry);
            carrySo.FindProperty("interactionPrompt").stringValue = "Взяться за бутыль (E)";
            carrySo.FindProperty("groundLayers").intValue =
                (1 << LayerMask.NameToLayer("Ground")) | (1 << LayerMask.NameToLayer("Cover"));
            ApplySettings(carrySo.FindProperty("settings"), WaterBottle.BuildCarrySettings(config));
            carrySo.ApplyModifiedPropertiesWithoutUndo();

            var bottle = root.AddComponent<WaterBottle>();
            var bottleSo = new SerializedObject(bottle);
            bottleSo.FindProperty("config").objectReferenceValue = config;
            bottleSo.FindProperty("waterMesh").objectReferenceValue = waterPivot.transform;
            bottleSo.FindProperty("tiltIndicator").objectReferenceValue = water.GetComponent<Renderer>();
            bottleSo.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, BottlePrefabPath);
            Object.DestroyImmediate(root);
        }

        private static void BuildBrickPrefab(CarryItemConfig config)
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "Brick";
            root.transform.localScale = new Vector3(
                config.ToMeters(0.35f), config.ToMeters(0.2f), config.ToMeters(0.2f));

            var body = root.AddComponent<Rigidbody>();
            body.mass = 1.5f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var pickup = root.AddComponent<PickupItem>();
            var so = new SerializedObject(pickup);
            so.FindProperty("itemName").stringValue = "Кирпич";
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, BrickPrefabPath);
            Object.DestroyImmediate(root);
        }

        private static void BuildTankPrefab(CarryItemConfig config)
        {
            var root = new GameObject("Tank");

            int ground = LayerMask.NameToLayer("Ground");
            float side = 4f;
            float rim = 1.2f;

            // Борта: и вид бака, и то, во что упирается камера. На Ground,
            // иначе она пройдёт их насквозь.
            for (int i = 0; i < 4; i++)
            {
                bool alongX = i < 2;
                float sign = i % 2 == 0 ? 1f : -1f;

                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = $"Rim_{i + 1}";
                wall.layer = ground;
                wall.transform.SetParent(root.transform, false);
                wall.transform.localPosition = new Vector3(
                    alongX ? config.ToMeters(side * 0.5f * sign) : 0f,
                    config.ToMeters(rim * 0.5f),
                    alongX ? 0f : config.ToMeters(side * 0.5f * sign));
                wall.transform.localScale = new Vector3(
                    config.ToMeters(alongX ? 0.3f : side),
                    config.ToMeters(rim),
                    config.ToMeters(alongX ? side : 0.3f));
            }

            var waterPivot = new GameObject("WaterPivot");
            waterPivot.transform.SetParent(root.transform, false);

            var water = GameObject.CreatePrimitive(PrimitiveType.Cube);
            water.name = "WaterMesh";
            water.transform.SetParent(waterPivot.transform, false);
            water.transform.localPosition = new Vector3(0f, config.ToMeters(rim * 0.5f), 0f);
            water.transform.localScale = new Vector3(
                config.ToMeters(side * 0.9f), config.ToMeters(rim), config.ToMeters(side * 0.9f));
            Object.DestroyImmediate(water.GetComponent<Collider>());

            // Зона приёма заметно шире бортов и выше их.
            //
            // Спека говорит «бутыль внесена в зону бака», и зона — это площадка
            // вокруг него, а не коробка по борта. Тесная зона делает слив
            // недостижимым: несущий стоит в метре от оси тары, борт не пускает
            // его ближе, и бутыль до коробки просто не доезжает — на первом
            // прогоне ни одна ходка не засчиталась именно поэтому.
            var zone = root.AddComponent<BoxCollider>();
            zone.isTrigger = true;
            zone.center = new Vector3(0f, config.ToMeters(1.5f), 0f);
            zone.size = new Vector3(
                config.ToMeters(side + 4f), config.ToMeters(3f), config.ToMeters(side + 4f));

            var tank = root.AddComponent<WaterTank>();
            var so = new SerializedObject(tank);
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("waterMesh").objectReferenceValue = waterPivot.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, TankPrefabPath);
            Object.DestroyImmediate(root);
        }

        /// <summary>
        /// Штабель. <b>Начало координат — на полу</b>, ящик уходит вверх от него.
        ///
        /// Пивот примитива стоит в середине, и корень-куб половиной ушёл бы под
        /// пол: игроки взбирались бы на него как на ступень, а точка выдачи
        /// оказалась бы внутри плиты пола вместе с бутылью.
        /// </summary>
        private static void BuildStackPrefab(CarryItemConfig config)
        {
            var root = new GameObject("BottleStack");

            float side = 2.5f;
            float height = 1.5f;

            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "Crate";
            crate.layer = LayerMask.NameToLayer("Ground");
            crate.transform.SetParent(root.transform, false);
            crate.transform.localPosition = new Vector3(0f, config.ToMeters(height * 0.5f), 0f);
            crate.transform.localScale = new Vector3(
                config.ToMeters(side), config.ToMeters(height), config.ToMeters(side));

            var indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            indicator.name = "ReadyIndicator";
            indicator.transform.SetParent(root.transform, false);
            indicator.transform.localPosition = new Vector3(0f, config.ToMeters(height + 0.4f), 0f);
            indicator.transform.localScale = Vector3.one * config.ToMeters(0.5f);
            Object.DestroyImmediate(indicator.GetComponent<Collider>());

            // Тара появляется перед ящиком, со стороны маршрута: взявший
            // сразу оказывается лицом туда, куда её нести.
            var spawnPoint = new GameObject("BottleSpawn");
            spawnPoint.transform.SetParent(root.transform, false);
            spawnPoint.transform.localPosition = new Vector3(config.ToMeters(side * 0.9f), 0f, 0f);

            var stack = root.AddComponent<BottleStack>();
            var so = new SerializedObject(stack);
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("bottlePrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<WaterBottle>(BottlePrefabPath);
            so.FindProperty("spawnPoint").objectReferenceValue = spawnPoint.transform;
            so.FindProperty("readyIndicator").objectReferenceValue = indicator;
            so.FindProperty("prompt").stringValue = "Взять бутыль (держать E)";
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, StackPrefabPath);
            Object.DestroyImmediate(root);
        }

        // ========== СВЯЗЫВАНИЕ ==========

        private static void WireManager(CarryItemConfig config)
        {
            GameObject manager = GameObject.Find("MinigameManager");
            if (manager == null)
            {
                Debug.LogError("CarryItemArenaBuilder: в сцене нет MinigameManager.");
                return;
            }

            var template = manager.GetComponent<TemplateMinigame>();
            if (template != null)
            {
                Object.DestroyImmediate(template);
            }

            var game = manager.GetComponent<CarryItemMinigame>();
            if (game == null)
            {
                game = manager.AddComponent<CarryItemMinigame>();
            }

            var ram = manager.GetComponent<BottleRamDetector>();
            if (ram == null)
            {
                ram = manager.AddComponent<BottleRamDetector>();
            }

            var ramSo = new SerializedObject(ram);
            ramSo.FindProperty("config").objectReferenceValue = config;
            ramSo.ApplyModifiedPropertiesWithoutUndo();

            var so = new SerializedObject(game);
            so.FindProperty("definition").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<MinigameDefinition>(DefinitionPath);
            so.FindProperty("roundTimer").objectReferenceValue = manager.GetComponent<RoundTimer>();
            so.FindProperty("tutorialScreen").objectReferenceValue =
                Object.FindFirstObjectByType<Igruha.Core.UI.TutorialScreen>(FindObjectsInactive.Include);
            so.FindProperty("hud").objectReferenceValue =
                Object.FindFirstObjectByType<Igruha.Core.UI.RoundHud>(FindObjectsInactive.Include);

            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("spawnPoints").objectReferenceValue = Object.FindFirstObjectByType<SpawnPointSet>();
            so.FindProperty("ramDetector").objectReferenceValue = ram;
            so.FindProperty("progressBar").objectReferenceValue =
                Object.FindFirstObjectByType<Igruha.Core.UI.TeamProgressBar>(FindObjectsInactive.Include);

            so.FindProperty("teamA.Stack").objectReferenceValue = FindStack("Stack_A");
            so.FindProperty("teamA.Tank").objectReferenceValue = FindTank("Tank_A");
            so.FindProperty("teamA.SpawnRole").enumValueIndex = (int)SpawnRole.TeamA;
            so.FindProperty("teamA.Route").objectReferenceValue = FindRoute("Route_A");
            so.FindProperty("teamB.Stack").objectReferenceValue = FindStack("Stack_B");
            so.FindProperty("teamB.Tank").objectReferenceValue = FindTank("Tank_B");
            so.FindProperty("teamB.SpawnRole").enumValueIndex = (int)SpawnRole.TeamB;
            so.FindProperty("teamB.Route").objectReferenceValue = FindRoute("Route_B");

            // Бутыль ниже уровня пола считается улетевшей в пропасть. Отметка
            // с запасом ниже пола и заметно выше дна: падать ей ещё далеко,
            // а пропасть засчитывается сразу.

            so.FindProperty("voidLevel").floatValue = -config.ToMeters(config.ChasmDepth * 0.3f);
            so.FindProperty("botObstacles").intValue =
                (1 << LayerMask.NameToLayer("Ground")) | (1 << LayerMask.NameToLayer("Cover"));
            so.ApplyModifiedPropertiesWithoutUndo();

            var bootstrap = manager.GetComponent<MinigameBootstrap>();
            if (bootstrap != null)
            {
                var bootSo = new SerializedObject(bootstrap);
                bootSo.FindProperty("minigame").objectReferenceValue = game;
                bootSo.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static BottleStack FindStack(string name)
        {
            GameObject go = GameObject.Find(name);
            return go != null ? go.GetComponent<BottleStack>() : null;
        }

        private static WaterTank FindTank(string name)
        {
            GameObject go = GameObject.Find(name);
            return go != null ? go.GetComponent<WaterTank>() : null;
        }

        private static CarryItemBotRoute FindRoute(string name)
        {
            GameObject go = GameObject.Find(name);
            return go != null ? go.GetComponent<CarryItemBotRoute>() : null;
        }

        // ========== ОТЧЁТ ==========

        /// <summary>
        /// Проверить числа, которые уже ломались в других мини-играх, и сказать
        /// о них вслух. Молчаливая арена, не проходящая по правилу камеры, —
        /// это неделя поисков потом.
        /// </summary>
        private static void Report(CarryItemConfig config)
        {
            float stackToWall = config.ToMeters((StartMinX + StartMaxX) * 0.5f - StartMinX + EndMarginX);
            float tankToWall = config.ToMeters(TankMaxX + EndMarginX - (TankMinX + TankMaxX) * 0.5f);
            float neckWidth = config.ToMeters(config.NeckWidth);

            if (stackToWall < CameraClearance)
            {
                Debug.LogError($"CarryItemArenaBuilder: от штабеля до торцевой стены {stackToWall:F2} м " +
                               $"при нужных {CameraClearance} — камера прижмётся к затылку. Увеличь стартовую зону.");
            }

            if (tankToWall < CameraClearance)
            {
                Debug.LogWarning($"CarryItemArenaBuilder: от бака до торцевой стены {tankToWall:F2} м " +
                                 $"при нужных {CameraClearance} — проверь ракурс у бака вручную.");
            }

            float window = FreePassageWindow(config);
            if (window < config.BeamMinWindowSeconds)
            {
                Debug.LogWarning($"CarryItemArenaBuilder: свободное окно на проход горлышка {window:F2} с " +
                                 $"при нужных {config.BeamMinWindowSeconds}. Укороти балку или расширь проход — " +
                                 "период балки не трогать.");
            }

            float routeLength = config.ToMeters(TankMinX - StartMaxX + config.StartZoneSize * 0.5f +
                                                config.TankZoneSize * 0.5f + config.NeckWidth);

            Debug.Log($"🫙 Арена «Переноски предмета» собрана. " +
                      $"Горлышко {neckWidth:F2} м, балка {config.ToMeters(config.NeckWidth * config.BeamNeckCoverage):F2} м, " +
                      $"окно на проход {window:F2} с (нужно ≥ {config.BeamMinWindowSeconds}). " +
                      $"От штабеля до стены {stackToWall:F2} м, от бака до стены {tankToWall:F2} м " +
                      $"(правило камеры {CameraClearance}). Маршрут ≈ {routeLength:F1} м, " +
                      $"пропасти {config.ToMeters(config.ChasmDepth):F2} м глубиной.");
        }

        /// <summary>
        /// Сколько секунд за половину оборота балка оставляет проход шире, чем
        /// нужно бутыли. Балка вращается вокруг центра горлышка, поэтому её
        /// поперечный размах — половина длины на синус угла.
        /// </summary>
        private static float FreePassageWindow(CarryItemConfig config)
        {
            float halfNeck = config.NeckWidth * 0.5f;
            float halfBeam = config.NeckWidth * config.BeamNeckCoverage * 0.5f;

            if (halfBeam <= 0f)
            {
                return config.BeamPeriod;
            }

            // Проход свободен, пока по одну сторону балки остаётся зазор
            // на бутыль с несущим.
            float limit = (halfNeck - PassageClearance) / halfBeam;
            if (limit >= 1f)
            {
                return config.BeamPeriod;
            }

            if (limit <= 0f)
            {
                return 0f;
            }

            float openAngle = Mathf.Asin(limit) * Mathf.Rad2Deg;
            return config.BeamPeriod * 0.5f * (2f * openAngle / 180f);
        }

        private static float PunchForce()
        {
            var character = AssetDatabase.LoadAssetAtPath<Igruha.Core.Player.CharacterConfig>(
                "Assets/_Project/Settings/Gameplay/CharacterConfig.asset");
            return character != null ? character.PushForce : 14f;
        }

        // ========== ПРИМИТИВЫ ==========

        private static void Slab(Transform parent, CarryItemConfig config, int layer, string name,
            float minX, float maxX, float minZ, float maxZ, float topY)
        {
            Box(parent, config, layer, name, minX, maxX, minZ, maxZ, topY - FloorThickness, FloorThickness);
        }

        /// <summary>Куб по границам в ШИ. Высота задаётся низом и толщиной — так читаются все размеры спеки.</summary>
        private static void Box(Transform parent, CarryItemConfig config, int layer, string name,
            float minX, float maxX, float minZ, float maxZ, float bottomY, float height)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(
                config.ToMeters((minX + maxX) * 0.5f),
                config.ToMeters(bottomY + height * 0.5f),
                config.ToMeters((minZ + maxZ) * 0.5f));
            go.transform.localScale = new Vector3(
                config.ToMeters(maxX - minX),
                config.ToMeters(height),
                config.ToMeters(maxZ - minZ));
        }

        private static void MakeSpawn(Transform parent, CarryItemConfig config, string name, SpawnRole role,
            float x, float z, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(config.ToMeters(x), 0.1f, config.ToMeters(z));
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var point = go.AddComponent<SpawnPoint>();
            var so = new SerializedObject(point);
            so.FindProperty("role").enumValueIndex = (int)role;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void MakeKillZone(Transform parent, CarryItemConfig config, string name,
            float minX, float maxX, float bottomY, float topY, float halfDepth)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(
                config.ToMeters((minX + maxX) * 0.5f),
                config.ToMeters((bottomY + topY) * 0.5f),
                0f);

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(
                config.ToMeters(maxX - minX),
                config.ToMeters(topY - bottomY),
                config.ToMeters(halfDepth * 2f));

            go.AddComponent<KillZone>();
        }

        private static void MakeStash(Transform parent, CarryItemConfig config, PickupItem brick,
            string name, float x, float z)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = LayerMask.NameToLayer("Ground");
            go.transform.SetParent(parent, false);
            // Куб ставим на пол, а не в него: пивот примитива в середине.
            go.transform.position = new Vector3(config.ToMeters(x), config.ToMeters(0.75f), config.ToMeters(z));
            go.transform.localScale = Vector3.one * config.ToMeters(1.5f);

            var spawnPoint = new GameObject("ItemSpawn");
            spawnPoint.transform.SetParent(go.transform, false);
            spawnPoint.transform.localPosition = new Vector3(0f, 1.2f, 0f);

            var dispenser = go.AddComponent<ItemDispenser>();
            var so = new SerializedObject(dispenser);
            so.FindProperty("itemPrefab").objectReferenceValue = brick;
            so.FindProperty("spawnPoint").objectReferenceValue = spawnPoint.transform;
            so.FindProperty("prompt").stringValue = "Взять кирпич (E)";
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void PlacePrefab(Transform parent, string path, string name, CarryItemConfig config,
            float x, float z, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"CarryItemArenaBuilder: не найден префаб {path}");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            instance.transform.position = new Vector3(config.ToMeters(x), 0f, config.ToMeters(z));
            instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private static void ApplySettings(SerializedProperty property, MultiCarrySettings settings)
        {
            property.FindPropertyRelative("handleRadius").floatValue = settings.handleRadius;
            property.FindPropertyRelative("handleHeight").floatValue = settings.handleHeight;
            property.FindPropertyRelative("carrierStandoff").floatValue = settings.carrierStandoff;
            property.FindPropertyRelative("carryClearance").floatValue = settings.carryClearance;
            property.FindPropertyRelative("maxObjectSpeed").floatValue = settings.maxObjectSpeed;
            property.FindPropertyRelative("carrierSpeedCap").floatValue = settings.carrierSpeedCap;
            property.FindPropertyRelative("pullToSpeed").floatValue = settings.pullToSpeed;
            property.FindPropertyRelative("tensionDeadzone").floatValue = settings.tensionDeadzone;
            property.FindPropertyRelative("breakDistance").floatValue = settings.breakDistance;
            property.FindPropertyRelative("tetherFreeSpeedPerMeter").floatValue = settings.tetherFreeSpeedPerMeter;
            property.FindPropertyRelative("tetherGrip").floatValue = settings.tetherGrip;
            property.FindPropertyRelative("tiltThreshold").floatValue = settings.tiltThreshold;
            property.FindPropertyRelative("maxTiltAngle").floatValue = settings.maxTiltAngle;
            property.FindPropertyRelative("tiltFromTorque").floatValue = settings.tiltFromTorque;
            property.FindPropertyRelative("tiltFromSupportLoss").floatValue = settings.tiltFromSupportLoss;
            property.FindPropertyRelative("tiltDamping").floatValue = settings.tiltDamping;
            property.FindPropertyRelative("tiltRestoring").floatValue = settings.tiltRestoring;
            property.FindPropertyRelative("throwImpulsePerCarrier").floatValue = settings.throwImpulsePerCarrier;
            property.FindPropertyRelative("throwUpward").floatValue = settings.throwUpward;
        }

        private static Transform ResetGroup(Transform parent, string name)
        {
            Transform group = parent.Find(name);
            if (group != null)
            {
                Object.DestroyImmediate(group.gameObject);
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }
}
