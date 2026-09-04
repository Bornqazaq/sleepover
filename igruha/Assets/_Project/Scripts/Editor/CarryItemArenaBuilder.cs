using System.Collections.Generic;
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
        /// По какой полосе горлышка идёт команда, ШИ от осевой. Свободная
        /// полоса лежит между размахом балки (2 ШИ) и стеной завала (4 ШИ),
        /// поэтому полоса — ровно её середина, 3 ШИ.
        ///
        /// Было 2.5, и это стоило клиентам матча. При 2.5 внутренний край
        /// персонажа (радиус 0.5 ШИ) попадал точно на кончик балки: зазор
        /// нулевой. У хоста позиция точная, и он проходил; позицию клиента
        /// сервер видит через интерполяцию, ошибка в считанные сантиметры —
        /// и клиента сшибало. На стенде 1 на 1 это давало 5 нокдаунов у
        /// клиента против нуля у хоста, а нокдаун несущего-одиночки — это
        /// сразу уроненная бутыль. При 3 ШИ с каждой стороны остаётся
        /// по 0.5 ШИ запаса, и интерполяция перестаёт решать исход.
        /// </summary>
        private const float NeckLaneZ = 3f;

        private const float FloorThickness = 1f;
        private const float PlankThickness = 0.4f;
        private const float WallThickness = 1f;

        /// <summary>Сколько места нужно бутыли и несущему, чтобы протиснуться мимо балки, ШИ.</summary>
        private const float PassageClearance = 2f;

        /// <summary>Правило камеры: сколько метров свободного места нужно позади остановившегося игрока.</summary>
        private const float CameraClearance = 4.5f;

        private const string MaterialFolder = "Assets/_Project/Materials/Minigames/CarryItem/";

        /// <summary>
        /// Свой генератор случайных чисел у дресса — требование конвейера.
        /// Общий с билдером сдвинул бы последовательность, по которой
        /// раскладываются геймплейные объекты, и проверенная фазами 2–3
        /// планировка поехала бы от одной лишь смены модели.
        /// </summary>
        private const int DressSeed = 20260904;

        /// <summary>Высота поддона под тарой, ШИ.</summary>
        private const float PalletHeight = 0.35f;

        /// <summary>Сколько бутылей стоит на поддоне штабеля: ряды и столбцы.</summary>
        private const int StackRows = 2;
        private const int StackColumns = 3;

        /// <summary>Высота бака, ШИ. Выше прежнего обода: бак — табло команды, и его должно быть видно издали.</summary>
        private const float TankHeight = 2.15f;

        /// <summary>Ширина разметки по краю проёма, ШИ.</summary>
        private const float EdgeStripeWidth = 0.6f;

        private static System.Random dressRandom;

        /// <summary>
        /// Ловушки текущей пересборки: их разбирает подфаза 4.4.
        ///
        /// Держатся полями, а не ищутся по имени: имя объекта — это то, что
        /// молча ломается при первой же переименовке, а эффект, потерявший
        /// балку, не падает и не пишет ни строчки, он просто не играет.
        /// </summary>
        private static Transform builtBeam;
        private static Transform builtPipe;
        private static SpringTrap builtCart;

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

            dressRandom = new System.Random(DressSeed);
            CarryItemDress.Begin();
            CarryItemPalette.Begin();

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
            CarryItemEnvironment.Build(arena.transform, config, dressRandom);
            BuildProgressBar(config);
            WireManager(config);

            // Строки интерфейса раунда. Пустые ссылки на них ломали игру уже
            // трижды и всегда молча — см. MinigameHudLines.
            var hud = Object.FindFirstObjectByType<Igruha.Core.UI.RoundHud>(FindObjectsInactive.Include);
            MinigameHudLines.EnsureStatusLine(hud);
            MinigameHudLines.EnsureCountdownLine(hud);

            Physics.SyncTransforms();
            Report(config);
            GameObject manager = GameObject.Find("MinigameManager");
            BottleStack[] stacks = { FindStack("Stack_A"), FindStack("Stack_B") };
            WaterTank[] tanks = { FindTank("Tank_A"), FindTank("Tank_B") };

            CarryItemVfx.Build(arena.transform, config, manager, stacks, builtCart, builtPipe, builtBeam);
            CarryItemSfx.Build(arena.transform, config, manager, stacks, tanks, builtCart, builtPipe, builtBeam);

            CarryItemPalette.Flush();
            ReportMissingModels();
            Debug.Log(CarryItemDress.Report(), arena);
            Debug.Log(CarryItemPalette.Report(), arena);

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
            //
            // Пол настилается плитами перекрытия из пака, а не красится ровным
            // тоном: у плиты есть шов, кромка и толщина, и с ними перекрытие
            // читается залитым, а не покрашенным. Тайлить атлас Synty при этом
            // нельзя — это цветовая карта, а не тайловый материал.
            Floor(group, config, ground, "Floor_Start", StartMinX - EndMarginX, StartMaxX, 0f);
            Floor(group, config, ground, "Floor_Common", CommonMinX, CommonMaxX, 0f);
            Floor(group, config, ground, "Floor_Tanks", TankMinX, TankMaxX + EndMarginX, 0f);

            // Дно пропастей: на нём стоит KillZone, и об него же считается
            // объявленная LDD пара секунд до респавна. Грунт, а не бетон —
            // внизу стройка ещё не залита, и разница материала работает на то
            // же, что и разница тона: край проёма обязан читаться.
            float depth = -config.ChasmDepth;
            GameObject deep1 = Slab(group, config, ground, "Floor_Chasm_1",
                StartMaxX, CommonMinX, -HalfWidth, HalfWidth, depth);
            GameObject deep2 = Slab(group, config, ground, "Floor_Chasm_2",
                CommonMaxX, TankMinX, -HalfWidth, HalfWidth, depth);

            foreach (GameObject bottom in new[] { deep1, deep2 })
            {
                Paint(bottom, CarryItemPalette.Get(CarryItemPalette.Tone.ConcreteDeep));
                CarryItemDress.Apply(bottom, CarryItemDress.Kind.ChasmFloor, dressRandom);
            }

            BuildChasmEdges(group, config);
        }

        /// <summary>
        /// Плита перекрытия: коробка блокаута со своим коллайдером, поверх неё
        /// настил плитами пака. Верх плиты совпадает с верхом коробки — по нему
        /// и бегут.
        /// </summary>
        private static void Floor(Transform group, CarryItemConfig config, int ground, string name,
            float minX, float maxX, float topY)
        {
            GameObject slab = Slab(group, config, ground, name, minX, maxX, -HalfWidth, HalfWidth, topY);
            Paint(slab, CarryItemPalette.Get(CarryItemPalette.Tone.Concrete));
            CarryItemDress.Apply(slab, CarryItemDress.Kind.Floor, dressRandom);
        }

        /// <summary>
        /// Разметка по краю проёма — требование LDD: края пропастей обязаны
        /// явно контрастировать с полом.
        ///
        /// Полосой, а не тоном всего пола: тон уже разведён (дно темнее
        /// перекрытия), но сверху, с игровой камеры, разницу яркости съедает
        /// перспектива. Полоса лежит на самом краю и видна ровно оттуда,
        /// откуда в пропасть падают. Коллайдера у неё нет.
        /// </summary>
        private static void BuildChasmEdges(Transform group, CarryItemConfig config)
        {
            var edges = new[] { StartMaxX, CommonMinX, CommonMaxX, TankMinX };
            Material stripe = CarryItemPalette.Get(CarryItemPalette.Tone.EdgeStripe);

            for (int i = 0; i < edges.Length; i++)
            {
                // Полоса кладётся на пол со стороны перекрытия, а не в воздух
                // над проёмом: наружу от края её было бы не на чем держать.
                float inward = i % 2 == 0 ? -EdgeStripeWidth : EdgeStripeWidth;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"ChasmEdge_{i + 1}";
                go.transform.SetParent(group, false);
                go.transform.position = new Vector3(
                    config.ToMeters(edges[i] + inward * 0.5f),
                    config.ToMeters(0.02f),
                    0f);
                go.transform.localScale = new Vector3(
                    config.ToMeters(EdgeStripeWidth), config.ToMeters(0.04f), config.ToMeters(HalfWidth * 2f));
                Object.DestroyImmediate(go.GetComponent<Collider>());
                Paint(go, stripe);
            }
        }

        private static void BuildWalls(Transform root, CarryItemConfig config, int ground)
        {
            Transform group = ResetGroup(root, "Walls");

            float height = config.WallHeight;
            float startEdge = StartMinX - EndMarginX;
            float tankEdge = TankMaxX + EndMarginX;
            float minX = startEdge - WallThickness;
            float maxX = tankEdge + WallThickness;

            Material concrete = CarryItemPalette.Get(CarryItemPalette.Tone.Wall);

            // Торцы: за штабелем и за баком, с отступом на правило камеры.
            // Расстояние до них проверяется отчётом.
            Paint(Box(group, config, ground, "Wall_Start", startEdge - WallThickness, startEdge,
                -HalfWidth - WallThickness, HalfWidth + WallThickness, 0f, height), concrete);
            Paint(Box(group, config, ground, "Wall_Tanks", tankEdge, tankEdge + WallThickness,
                -HalfWidth - WallThickness, HalfWidth + WallThickness, 0f, height), concrete);

            // Борта во всю длину, включая пропасти: свалившийся не должен
            // укатиться из-под арены.
            Paint(Box(group, config, ground, "Wall_SideA", minX, maxX, HalfWidth, HalfWidth + WallThickness,
                -config.ChasmDepth, height + config.ChasmDepth), concrete);
            Paint(Box(group, config, ground, "Wall_SideB", minX, maxX, -HalfWidth - WallThickness, -HalfWidth,
                -config.ChasmDepth, height + config.ChasmDepth), concrete);
        }

        private static void BuildPlanks(Transform root, CarryItemConfig config, int ground)
        {
            Transform group = ResetGroup(root, "Planks");

            float half = config.PlankWidth * 0.5f;
            Material plank = Mat("CI_Plank");

            // По доске на команду на каждой пропасти, по линии её маршрута.
            // К бортам не примыкают: вдоль доски камера отходит назад, и
            // упереться ей не во что (спека 3.3).
            Plank(group, config, ground, plank, "Plank_1_A", StartMaxX, CommonMinX, RouteZ - half, RouteZ + half);
            Plank(group, config, ground, plank, "Plank_1_B", StartMaxX, CommonMinX, -RouteZ - half, -RouteZ + half);
            Plank(group, config, ground, plank, "Plank_2_A", CommonMaxX, TankMinX, RouteZ - half, RouteZ + half);
            Plank(group, config, ground, plank, "Plank_2_B", CommonMaxX, TankMinX, -RouteZ - half, -RouteZ + half);
        }

        /// <summary>
        /// Одна доска: коробка блокаута с материалом читаемости и настилом пака
        /// поверх. Материал остаётся назначенным и после дресса — на машине без
        /// паков доска обязана оставаться жёлтой, а не серой, иначе край
        /// пропасти перестаёт читаться.
        /// </summary>
        private static void Plank(Transform group, CarryItemConfig config, int ground, Material material,
            string name, float minX, float maxX, float minZ, float maxZ)
        {
            GameObject box = Box(group, config, ground, name, minX, maxX, minZ, maxZ,
                -PlankThickness, PlankThickness);
            Paint(box, material);
            CarryItemDress.Apply(box, CarryItemDress.Kind.Plank, dressRandom);
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

            GameObject rubbleA = Box(group, config, cover, "Rubble_A", RubbleMinX, maxX, neckHalf, bypassInner,
                0f, config.RubbleHeight);
            GameObject rubbleB = Box(group, config, cover, "Rubble_B", RubbleMinX, maxX, -bypassInner, -neckHalf,
                0f, config.RubbleHeight);

            CarryItemDress.Apply(rubbleA, CarryItemDress.Kind.Rubble, dressRandom);
            CarryItemDress.Apply(rubbleB, CarryItemDress.Kind.Rubble, dressRandom);
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

            // Материал опасности и двутавр пака поверх. Дресс обязан быть
            // ребёнком самой балки: SwingingBeamTrap водит её transform, и
            // модель едет вместе с ним. Поставь её рядом — балка ушла бы
            // крутиться одна, а двутавр остался бы висеть над горлышком.
            Paint(beam, CarryItemPalette.Get(CarryItemPalette.Tone.Steel));
            CarryItemDress.Apply(beam, CarryItemDress.Kind.Beam, dressRandom);

            builtBeam = beam.transform;

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

            Paint(cart, CarryItemPalette.Get(CarryItemPalette.Tone.Hazard));
            BuildCartProps(group, config, cart);

            var spring = cart.AddComponent<SpringTrap>();
            var springSo = new SerializedObject(spring);
            springSo.FindProperty("launchForce").floatValue = config.CartLaunchForce;
            springSo.FindProperty("cooldown").floatValue = config.CartPeriod * 0.5f;
            springSo.ApplyModifiedPropertiesWithoutUndo();

            builtCart = spring;

            var driver = cart.AddComponent<PeriodicTrapDriver>();
            var driverSo = new SerializedObject(driver);
            driverSo.FindProperty("trap").objectReferenceValue = spring;
            driverSo.FindProperty("period").floatValue = config.CartPeriod;
            driverSo.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Тачки, которыми видна ловушка. Стоят <b>рядом с триггером</b>, а не
        /// внутри него, и это не придирка.
        ///
        /// Коробка ловушки — плита 2.5 × 0.5 × 8 ШИ на высоте 0.18 м: это объём
        /// срабатывания, а не тачка. Дресс садит модель в габарит коробки, и
        /// тачка вышла бы 36 сантиметров высотой — по щиколотку. Ловушка,
        /// которую не видно заранее, читается как несправедливость (спека 8.5),
        /// поэтому тачки ставятся в натуральный рост поперёк горлышка, а
        /// рендерер плиты гаснет.
        ///
        /// Три штуки на 5.76 м ширины горлышка: одна оставила бы половину
        /// прохода с невидимым триггером, а пять слились бы в стену и
        /// перекрыли бы обзор на балку за ними.
        /// </summary>
        private static void BuildCartProps(Transform group, CarryItemConfig config, GameObject cart)
        {
            const int count = 3;
            float span = config.NeckWidth * 0.72f;
            var placed = new GameObject("TippingCartProps");
            placed.transform.SetParent(group, false);

            bool any = false;
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : i / (float)(count - 1);
                float z = Mathf.Lerp(-span * 0.5f, span * 0.5f, t);

                // Развёрнуты поперёк маршрута и чередуют сторону: ряд одинаково
                // повёрнутых тачек читается складом, а не завалом.
                float yaw = i % 2 == 0 ? 90f : 270f;
                GameObject prop = CarryItemDress.Prop(placed.transform, $"Wheelbarrow_{i + 1}",
                    CarryItemDress.WheelbarrowPath,
                    new Vector3(cart.transform.position.x, 0f, config.ToMeters(z)), yaw);
                any |= prop != null;
            }

            if (any)
            {
                var renderer = cart.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }
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

            builtPipe = pipe.transform;
            BuildPipeVisuals(group, config, pipe, box.size);
        }

        /// <summary>
        /// Видимый объём струи и стояк, из которого она бьёт.
        ///
        /// До разбора 01.09 у трубы <b>не было рендерера вовсе</b> — игрока
        /// сносило ничем. Объём струи заведён тогда же и жил в сцене руками;
        /// здесь он собирается кодом, потому что пересборка арены сносила
        /// группу ловушек целиком вместе с ним.
        ///
        /// Стояк ставится <b>сбоку от струи</b>, у самого края горлышка, а не в
        /// её объёме: труба посреди прохода была бы препятствием в 4.32-метровом
        /// проходе, где и так ходит балка. Струя при этом идёт от него поперёк
        /// маршрута — ровно как толкает зона.
        /// </summary>
        private static void BuildPipeVisuals(Transform group, CarryItemConfig config, GameObject pipe, Vector3 size)
        {
            // Объём струи больше не рисуется мешами.
            //
            // До 4.4 он был кубом во весь объём зоны, потом тремя слоями брызг:
            // и то и другое читалось «прозрачной штукой» непонятного назначения
            // — так его и назвал геймдизайнер. Теперь воду показывают частицы
            // (см. CarryItemVfx), а на полу остаётся лужа: она объясняет, что
            // здесь мокро, и остаётся видимой даже без паков.
            Material spray = CarryItemPalette.Get(CarryItemPalette.Tone.Spray);

            GameObject puddle = Cylinder(pipe.transform, "Puddle",
                -config.ToMeters(1f) + 0.02f, 0.01f, size.x * 1.35f, spray);
            puddle.transform.localPosition = new Vector3(0f, -config.ToMeters(1f) + 0.02f, -size.z * 0.2f);

            // Стояк — у кромки свободной полосы, со стороны команды A. Высота
            // приведена к высоте завалов: труба выше них перекрыла бы обзор
            // на балку, ниже — потерялась бы на их фоне.
            float edgeZ = config.NeckWidth * 0.5f - 0.5f;
            CarryItemDress.Prop(group, "Standpipe", CarryItemDress.StandpipePath,
                new Vector3(pipe.transform.position.x, 0f, config.ToMeters(edgeZ)),
                0f, config.ToMeters(config.RubbleHeight));

            // Излом со срезанным концом на высоте струи. Без него объём струи
            // висит в воздухе ничем: до разбора 01.09 игрока сносило вообще
            // невидимой силой, а полупрозрачная коробка одна отвечает на
            // вопрос «где толкает», но не на вопрос «откуда».
            CarryItemDress.Prop(group, "PipeSpout", CarryItemDress.SpoutPath,
                new Vector3(pipe.transform.position.x, pipe.transform.position.y, config.ToMeters(edgeZ - 0.6f)),
                90f, config.ToMeters(0.5f));
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
        /// <summary>
        /// Один пояс силуэта бутыли: от какой доли полной высоты до какой и
        /// какой доли диаметра тела.
        /// </summary>
        private readonly struct BottleSection
        {
            public readonly string Name;
            public readonly string Material;
            public readonly float Bottom;
            public readonly float Top;
            public readonly float Diameter;

            public BottleSection(string name, string material, float bottom, float top, float diameter)
            {
                Name = name;
                Material = material;
                Bottom = bottom;
                Top = top;
                Diameter = diameter;
            }
        }

        /// <summary>
        /// Силуэт бутыли для кулера — тело, два плеча, горлышко, крышка.
        ///
        /// Доли от полной высоты и от диаметра тела, а не метры: высота и
        /// радиус приходят из <see cref="CarryItemConfig"/>, и смена высоты
        /// ручек в ассете обязана двигать весь силуэт целиком.
        ///
        /// Пояса стыкуются без зазоров: верх каждого — низ следующего. Крышка
        /// торчит над горлышком на 2.7 % высоты, и это единственное место, где
        /// доля больше единицы.
        /// </summary>
        private static readonly BottleSection[] BottleSilhouette =
        {
            // Тело. Прозрачное: сквозь него виден столбик воды, и это
            // единственный способ узнать счёт — уровень воды и есть очки.
            new BottleSection("Body", "CI_BottleShell", 0f, 0.633f, 1f),

            // Два плеча вместо одного конуса: примитивов-конусов у Unity нет,
            // а ступенька из двух цилиндров читается как плечо бутыли и стоит
            // те же два меша, что и любая другая заглушка.
            new BottleSection("Shoulder1", "CI_BottleShell", 0.633f, 0.747f, 0.743f),
            new BottleSection("Shoulder2", "CI_BottleShell", 0.747f, 0.827f, 0.486f),

            // Горлышко: из него бьёт струя, и оно же — вторая метка команды.
            new BottleSection("Neck", "CI_BottleShell", 0.827f, 0.96f, 0.314f),

            // Крышка. Красится в цвет команды из TeamPalette в рантайме: в
            // горлышке бутылей две, и обе синие от воды — отличает их крышка.
            new BottleSection("Cap", "CI_BottleCap", 0.96f, 1.027f, 0.429f)
        };

        private static void BuildBottlePrefab(CarryItemConfig config)
        {
            var root = new GameObject("Bottle");

            float height = config.HandleHeight * 1.25f;
            float radius = config.HandleRadius * 0.7f;

            Renderer capRenderer = BuildBottleShape(root.transform, height, radius);

            // Уровень воды растёт от дна, поэтому масштабируется пустышка-пивот,
            // а не сам цилиндр: у примитива пивот в середине, и он рос бы в обе
            // стороны сразу.
            var waterPivot = new GameObject("WaterPivot");
            waterPivot.transform.SetParent(root.transform, false);
            waterPivot.transform.localPosition = Vector3.zero;

            // Вода уже тела ровно настолько, чтобы корпус читался стенкой,
            // а не плёнкой. Высота — по телу: воды выше плеч не бывает.
            BottleSection bodySection = BottleSilhouette[0];
            GameObject water = Cylinder(waterPivot.transform, "WaterMesh",
                (bodySection.Bottom + bodySection.Top) * 0.5f * height,
                (bodySection.Top - bodySection.Bottom) * 0.5f * height,
                radius * 2f * 0.857f,
                Mat("CI_BottleWater"));

            GameObject[] handles = BuildBottleHandles(root.transform, config);
            ParticleSystem jet = BuildPourJet(root.transform, config, height);

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
            bottleSo.FindProperty("pourJet").objectReferenceValue = jet;
            SetArray(bottleSo.FindProperty("teamTint"), new Object[] { capRenderer });
            bottleSo.ApplyModifiedPropertiesWithoutUndo();

            // Показ ручек. Без него игрок не знает ни куда встать, ни осталось
            // ли свободное место: ручки живут в расчёте, а не в модели.
            var markers = root.AddComponent<Igruha.Core.Items.MultiCarryHandleMarkers>();
            var markerSo = new SerializedObject(markers);
            var markerTargets = new Object[handles.Length];
            for (int i = 0; i < handles.Length; i++)
            {
                markerTargets[i] = handles[i].transform;
            }

            SetArray(markerSo.FindProperty("markers"), markerTargets);
            markerSo.ApplyModifiedPropertiesWithoutUndo();

            AddNetworking(root);

            PrefabUtility.SaveAsPrefabAsset(root, BottlePrefabPath);
            Object.DestroyImmediate(root);
        }

        /// <summary>
        /// Полотнище старта на двух стойках — метка команды, видная от самого
        /// бака. Стоит <b>сбоку</b> от штабеля: позади него камере нужны 4.5 м,
        /// а поперёк маршрута полотнище было бы препятствием на старте.
        /// </summary>
        private static GameObject BuildStartBanner(Transform root, CarryItemConfig config, float side)
        {
            var group = new GameObject("StartBanner");
            group.transform.SetParent(root, false);
            group.transform.localPosition = new Vector3(0f, 0f, config.ToMeters(side * 0.95f));

            float postHeight = config.ToMeters(2f);
            float span = config.ToMeters(side * 0.8f);

            for (int i = 0; i < 2; i++)
            {
                float z = i == 0 ? -span * 0.5f : span * 0.5f;
                GameObject post = Cylinder(group.transform, $"Post_{i + 1}",
                    postHeight * 0.5f, postHeight * 0.5f, config.ToMeters(0.12f), Mat("CI_TankRim"));
                post.transform.localPosition = new Vector3(0f, postHeight * 0.5f, z);
            }

            var cloth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cloth.name = "Cloth";
            cloth.transform.SetParent(group.transform, false);
            cloth.transform.localPosition = new Vector3(0f, postHeight * 0.78f, 0f);
            cloth.transform.localScale = new Vector3(
                config.ToMeters(0.08f), postHeight * 0.36f, span);
            Object.DestroyImmediate(cloth.GetComponent<Collider>());
            Paint(cloth, Mat("CI_Crate"));
            return cloth;
        }

        /// <summary>
        /// Силуэт бутыли пятью поясами. Отдельным методом потому, что бутыль
        /// стоит не только в руках: тем же силуэтом набран штабель, откуда её
        /// берут. Две разные модели одного и того же предмета читались бы как
        /// два разных предмета.
        /// </summary>
        /// <param name="shellOverride">
        /// Чем закрыть корпус вместо прозрачного стекла. Носимая бутыль обязана
        /// показывать уровень воды насквозь, а стоящая на штабеле — наоборот,
        /// полная, и прозрачной она читается пустой стекляшкой. Полная бутыль
        /// синяя, потому что в ней вода, а не потому что так покрасили.
        /// </param>
        /// <returns>Рендерер крышки — он красится в цвет команды.</returns>
        private static Renderer BuildBottleShape(Transform parent, float height, float radius,
            Material shellOverride = null)
        {
            Renderer cap = null;
            for (int i = 0; i < BottleSilhouette.Length; i++)
            {
                BottleSection section = BottleSilhouette[i];
                Material material = section.Name == "Cap" || shellOverride == null
                    ? Mat(section.Material)
                    : shellOverride;

                GameObject part = Cylinder(parent, section.Name,
                    (section.Bottom + section.Top) * 0.5f * height,
                    (section.Top - section.Bottom) * 0.5f * height,
                    radius * 2f * section.Diameter,
                    material);

                if (section.Name == "Cap")
                {
                    cap = part.GetComponent<Renderer>();
                }
            }

            return cap;
        }

        /// <summary>
        /// Четыре держалки по окружности ручек. Лишние выключает
        /// <see cref="Igruha.Core.Items.MultiCarryHandleMarkers"/> сам, по числу
        /// ручек: их столько же, сколько человек в команде.
        ///
        /// Заготовки лежат в префабе готовыми и не создаются на ходу: тару
        /// выдают шесть раз за раунд на каждую команду, и <c>Instantiate</c> на
        /// каждую был бы мусором на ровном месте.
        ///
        /// Каждая держалка — скоба с креплением: сама рукоять и одна перемычка,
        /// уходящая к корпусу вдоль −Z. Разворот наружу маркеры делают в
        /// рантайме, поэтому −Z всегда смотрит на бутыль.
        /// </summary>
        private static GameObject[] BuildBottleHandles(Transform root, CarryItemConfig config)
        {
            var group = new GameObject("Handles");
            group.transform.SetParent(root, false);

            float reach = config.HandleRadius;
            float grip = reach * 0.34f;
            float gripHeight = reach * 0.26f;
            Material material = Mat("CI_HandleGrip");

            var offsets = new[]
            {
                new Vector3(reach, config.HandleHeight, 0f),
                new Vector3(0f, config.HandleHeight, reach),
                new Vector3(-reach, config.HandleHeight, 0f),
                new Vector3(0f, config.HandleHeight, -reach)
            };

            var handles = new GameObject[offsets.Length];
            for (int i = 0; i < offsets.Length; i++)
            {
                var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                handle.name = $"Handle{i}";
                handle.transform.SetParent(group.transform, false);
                handle.transform.localPosition = offsets[i];
                handle.transform.localScale = new Vector3(grip, gripHeight, grip);
                Object.DestroyImmediate(handle.GetComponent<Collider>());
                Paint(handle, material);

                var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arm.name = "Arm";
                arm.transform.SetParent(handle.transform, false);
                arm.transform.localPosition = new Vector3(0f, 0f, -0.62f);
                arm.transform.localScale = new Vector3(0.3f, 0.3f, 1.25f);
                Object.DestroyImmediate(arm.GetComponent<Collider>());
                Paint(arm, material);

                handles[i] = handle;
            }

            return handles;
        }

        /// <summary>
        /// Струя из горлышка. Единственное, по чему видно <b>куда</b> уходит
        /// вода: цвет корпуса говорит «плохо», а льётся она вот отсюда.
        ///
        /// Запуск ручной (<c>playOnAwake</c> выключен): включает и выключает её
        /// <see cref="WaterBottle"/> по наклону за порогом, и каждая машина
        /// решает это сама — наклон приезжает поворотом.
        /// </summary>
        private static ParticleSystem BuildPourJet(Transform root, CarryItemConfig config, float height)
        {
            var go = new GameObject("PourJet");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, height * 1.013f, 0f);

            // Конус смотрит по локальному +Z, поэтому четверть оборота вокруг X
            // разворачивает его вверх вдоль оси бутыли. Дальше наклон бутыли
            // сам укладывает струю набок, а гравитация тянет её вниз.
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            var jet = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = jet.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 1.3f;
            main.startSpeed = 2.6f;
            main.startSize = 0.15f;
            main.gravityModifier = 1.35f;
            main.maxParticles = 220;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Local;

            ParticleSystem.EmissionModule emission = jet.emission;
            emission.rateOverTime = 75f;

            ParticleSystem.ShapeModule shape = jet.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 7f;
            shape.radius = config.HandleRadius * 0.15f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Mat("CI_PourWater");

            return jet;
        }

        /// <summary>
        /// Цилиндр по центру, полувысоте и диаметру. У примитива Unity высота
        /// два юнита при масштабе единица, поэтому в масштаб идёт полувысота —
        /// иначе каждая строка силуэта несла бы делённое на два число.
        /// </summary>
        private static GameObject Cylinder(Transform parent, string name, float centreY, float halfHeight,
            float diameter, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, centreY, 0f);
            go.transform.localScale = new Vector3(diameter, halfHeight, diameter);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            Paint(go, material);
            return go;
        }

        /// <summary>
        /// Сетевой слой предмета, который спавнит сервер.
        ///
        /// Заводится здесь, а не руками: до этой правки <c>NetworkObject</c> и
        /// <c>NetworkTransform</c> стояли на префабах бутыли и кирпича вручную,
        /// с фазы 3, а пересборка арены пересоздаёт префабы целиком — то есть
        /// одно нажатие пункта меню снимало с игры весь мультиплеер. Разошлось
        /// молча и держалось только тем, что пересборку с 27.08 никто не
        /// запускал.
        ///
        /// Масштаб не синхронизируем: он у этих предметов не меняется никогда,
        /// а три лишних поля идут в каждом пакете движения.
        /// </summary>
        private static void AddNetworking(GameObject root)
        {
            root.AddComponent<Unity.Netcode.NetworkObject>();

            var transform = root.AddComponent<Unity.Netcode.Components.NetworkTransform>();
            transform.SyncScaleX = false;
            transform.SyncScaleY = false;
            transform.SyncScaleZ = false;
            transform.Interpolate = true;
        }

        /// <summary>Заполнить сериализованный массив ссылками: длина и элементы разом.</summary>
        private static void SetArray(SerializedProperty property, Object[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
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

            AddNetworking(root);
            CarryItemDress.Apply(root, CarryItemDress.Kind.Brick, dressRandom);

            PrefabUtility.SaveAsPrefabAsset(root, BrickPrefabPath);
            Object.DestroyImmediate(root);
        }

        private static void BuildTankPrefab(CarryItemConfig config)
        {
            var root = new GameObject("Tank");

            int ground = LayerMask.NameToLayer("Ground");
            float side = 4f;
            float height = TankHeight;

            // Корпус — цельный цилиндр, а не четыре борта коробкой.
            //
            // Коробка была подпоркой блокаута: она держала коллайдер и
            // показывала уровень воды сверху. Настоящий бак на стройке круглый
            // и закрытый, и на референсе геймдизайнера он именно такой.
            // Коллайдер при этом остаётся ровно тем же пятном 4 × 4 ШИ — только
            // круглым: выпуклый меш-коллайдер цилиндра. Внутрь бака игрок
            // по-прежнему не попадает, камера сквозь него не проходит.
            GameObject shell = Cylinder(root.transform, "Shell",
                config.ToMeters(height * 0.5f), config.ToMeters(height * 0.5f),
                config.ToMeters(side), Mat("CI_TankRim"));
            shell.layer = ground;
            var body = shell.AddComponent<MeshCollider>();
            body.convex = true;

            // Крышка с ободом: обод и есть метка команды. На закрытом баке она
            // видна с любой стороны, тогда как борт коробки — только с двух.
            GameObject lid = Cylinder(root.transform, "Lid",
                config.ToMeters(height + 0.12f), config.ToMeters(0.12f),
                config.ToMeters(side * 0.82f), Mat("CI_TankRim"));
            lid.layer = ground;

            // Обод шире корпуса заметно, а не на сантиметр: это метка команды,
            // и её обязано быть видно от середины площадки, а не только вплотную.
            GameObject band = Cylinder(root.transform, "TeamBand",
                config.ToMeters(height - 0.22f), config.ToMeters(0.22f),
                config.ToMeters(side * 1.11f), Mat("CI_TankRim"));
            band.layer = ground;

            // Смотровое стекло: столбик воды снаружи, во всю высоту бака.
            //
            // Закрытый бак прячет уровень, а уровень — это счёт команды. Мерное
            // стекло на стройке вещь обычная и читается лучше, чем взгляд
            // сверху в открытую бочку: столбик виден с подхода, а не только
            // когда стоишь вплотную. Смотрит на маршрут, то есть в −X.
            float gaugeX = -config.ToMeters(side * 0.5f);
            float gaugeHeight = config.ToMeters(height * 0.86f);

            var waterPivot = new GameObject("WaterPivot");
            waterPivot.transform.SetParent(root.transform, false);

            var water = GameObject.CreatePrimitive(PrimitiveType.Cube);
            water.name = "WaterMesh";
            water.transform.SetParent(waterPivot.transform, false);
            water.transform.localPosition = new Vector3(gaugeX, gaugeHeight * 0.5f, 0f);
            water.transform.localScale = new Vector3(
                config.ToMeters(0.22f), gaugeHeight, config.ToMeters(side * 0.34f));
            Object.DestroyImmediate(water.GetComponent<Collider>());
            Paint(water, Mat("CI_TankWater"));

            var glass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            glass.name = "GaugeGlass";
            glass.transform.SetParent(root.transform, false);
            glass.transform.localPosition = new Vector3(gaugeX - config.ToMeters(0.06f), gaugeHeight * 0.5f, 0f);
            glass.transform.localScale = new Vector3(
                config.ToMeters(0.12f), gaugeHeight, config.ToMeters(side * 0.4f));
            Object.DestroyImmediate(glass.GetComponent<Collider>());
            Paint(glass, Mat("CI_TankGlass"));

            // Лестница на бак и труба у основания — то, из-за чего резервуар
            // читается резервуаром, а не бочкой. Декор, коллайдеров нет.
            // Лестница прислонена к наружной стенке. Была на 0.46 стороны —
            // то есть внутри корпуса радиусом в половину стороны, и её просто
            // не было видно.
            CarryItemDress.Prop(root.transform, "Ladder", CarryItemDress.LadderPath,
                root.transform.position + new Vector3(config.ToMeters(side * 0.54f), 0f, 0f),
                90f, config.ToMeters(height));
            CarryItemDress.Prop(root.transform, "Outlet", CarryItemDress.OutletPath,
                root.transform.position + new Vector3(0f, 0f, config.ToMeters(side * 0.5f)),
                0f, config.ToMeters(0.9f));

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

            // Обод — цветом команды: бак это её табло, и с середины площадки
            // должно быть видно, чей он, а не только сколько в нём.
            SetArray(so.FindProperty("teamTint"), new Object[] { band.GetComponent<Renderer>() });
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

            // Коробка блокаута остаётся коллайдером и гаснет: на неё игрок
            // натыкается, но видит он поддон с тарой, а не куб.
            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "Crate";
            crate.layer = LayerMask.NameToLayer("Ground");
            crate.transform.SetParent(root.transform, false);
            crate.transform.localPosition = new Vector3(0f, config.ToMeters(height * 0.5f), 0f);
            crate.transform.localScale = new Vector3(
                config.ToMeters(side), config.ToMeters(height), config.ToMeters(side));
            crate.GetComponent<MeshRenderer>().enabled = false;

            CarryItemDress.Prop(root.transform, "Pallet", CarryItemDress.PalletPath,
                root.transform.position, 0f, config.ToMeters(PalletHeight));

            // Тара на поддоне. Тем же силуэтом, что и носимая бутыль: две разные
            // модели одного предмета читались бы как два разных предмета.
            Material fullBottle = Mat("CI_BottleWater");
            float bottleHeight = config.ToMeters(height - PalletHeight);
            float bottleRadius = bottleHeight * (config.HandleRadius * 0.7f) / (config.HandleHeight * 1.25f);
            var tinted = new List<Object>(8);

            for (int i = 0; i < StackRows * StackColumns; i++)
            {
                float u = StackColumns == 1 ? 0.5f : i % StackColumns / (float)(StackColumns - 1);
                float v = StackRows == 1 ? 0.5f : i / StackColumns / (float)(StackRows - 1);
                float spread = config.ToMeters(side) * 0.5f - bottleRadius * 1.15f;

                var slot = new GameObject($"Bottle_{i + 1}");
                slot.transform.SetParent(root.transform, false);
                slot.transform.localPosition = new Vector3(
                    Mathf.Lerp(-spread, spread, u),
                    config.ToMeters(PalletHeight),
                    Mathf.Lerp(-spread, spread, v));

                Renderer cap = BuildBottleShape(slot.transform, bottleHeight, bottleRadius, fullBottle);
                if (cap != null)
                {
                    tinted.Add(cap);
                }
            }

            // Готовность — не шарик над ящиком, а стоящая с краю бутыль: она
            // есть, когда её можно взять, и её нет, пока команда несёт свою.
            var indicator = new GameObject("ReadyIndicator");
            indicator.transform.SetParent(root.transform, false);
            indicator.transform.localPosition = new Vector3(
                config.ToMeters(side * 0.42f), config.ToMeters(PalletHeight), 0f);
            Renderer readyCap = BuildBottleShape(
                indicator.transform, bottleHeight * 1.3f, bottleRadius * 1.3f, fullBottle);
            if (readyCap != null)
            {
                tinted.Add(readyCap);
            }

            // Полотнище старта на стойке — как на референсе геймдизайнера. Стоит
            // сбоку от штабеля, а не позади: позади нужны 4.5 м камере.
            GameObject banner = BuildStartBanner(root.transform, config, side);
            tinted.Add(banner.GetComponent<Renderer>());

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
            SetArray(so.FindProperty("teamTint"), tinted.ToArray());
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

        private static GameObject Slab(Transform parent, CarryItemConfig config, int layer, string name,
            float minX, float maxX, float minZ, float maxZ, float topY)
        {
            return Box(parent, config, layer, name, minX, maxX, minZ, maxZ, topY - FloorThickness, FloorThickness);
        }

        /// <summary>Куб по границам в ШИ. Высота задаётся низом и толщиной — так читаются все размеры спеки.</summary>
        private static GameObject Box(Transform parent, CarryItemConfig config, int layer, string name,
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
            return go;
        }

        /// <summary>
        /// Материал игры из <c>Materials/Minigames/CarryItem/</c>.
        ///
        /// Материалы заведены разбором читаемости 01.09 (STATE 3.37) и до сих
        /// пор жили только в сцене и в префабах, то есть держались руками.
        /// Здесь они назначаются кодом: это ровно то, что теряется при первой
        /// же пересборке арены, если не назначить.
        /// </summary>
        private static Material Mat(string name)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + name + ".mat");
            if (material == null)
            {
                Debug.LogWarning($"CarryItemArenaBuilder: не найден материал {name} в {MaterialFolder} — " +
                                 "объект останется на встроенном сером, и читаемость пропадёт.");
            }

            return material;
        }

        /// <summary>Назначить материал рендереру объекта, если и тот и другой есть.</summary>
        private static void Paint(GameObject go, Material material)
        {
            if (go == null || material == null)
            {
                return;
            }

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        /// <summary>
        /// Модели, которых не нашлось в проекте, — списком и всегда. Паки Synty
        /// в репозиторий не входят, и на машине без них арена соберётся серой:
        /// без этой строки разница читалась бы как «арт не сделан».
        /// </summary>
        private static void ReportMissingModels()
        {
            IReadOnlyList<string> missing = CarryItemDress.Missing;
            if (missing.Count == 0)
            {
                return;
            }

            var paths = new string[missing.Count];
            for (int i = 0; i < missing.Count; i++)
            {
                paths[i] = missing[i];
            }

            Debug.LogWarning(
                $"«Переноска предмета»: не найдено моделей паков — {missing.Count}. Там, где их нет, арена осталась " +
                "блокаутом. Поставь пак POLYGON Construction и пересобери.\n— " + string.Join("\n— ", paths));
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
            CarryItemDress.Apply(go, CarryItemDress.Kind.Stash, dressRandom);

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
