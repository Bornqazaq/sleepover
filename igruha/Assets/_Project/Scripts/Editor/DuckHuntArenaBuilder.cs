using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Arena;
using Igruha.Core.Spawning;
using Igruha.Core.Traps;
using Igruha.Minigames.DuckHunt;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Пересборка башни Duck Hunt по числам из DuckHuntConfig.
    ///
    /// Вся геометрия задана в ШП и строится от одной константы. Смысл тот же,
    /// что у арены «Ангелов»: башня выстроена под персонажа, а не под метры,
    /// и смена радиуса капсулы должна двигать всю конструкцию разом, а не
    /// оставлять половину размеров от старого персонажа.
    ///
    /// Змейка: лестничная комната этажа N — это вход этажа N+1, поэтому
    /// направление трассы чередуется. Чётные этажи бегут 0 → 48, нечётные
    /// 48 → 0, а перекрытие каждого этажа строится с дырой ровно над
    /// лестничной комнатой предыдущего.
    /// </summary>
    internal static class DuckHuntArenaBuilder
    {
        private const string ConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/DuckHuntConfig.asset";
        private const string MaterialFolder = "Assets/_Project/Materials/DuckHunt";

        private const string GroundLayerName = "Ground";
        private const string CoverLayerName = "Cover";
        private const string BarrierLayerName = "PlayerBarrier";

        private const int Seed = 20260817;

        // Границы секций этажа в ШП (спека, раздел 3.4).
        private const float EntryEnd = 6f;
        private const float ParkourStart = 6f;
        private const float ParkourEnd = 18f;
        private const float CoverStart = 18f;
        private const float TrapZoneStart = 28f;
        private const float StairRoomStart = 40f;

        private const float ButtonProgress = 29f;
        private const float ButtonProgressDoorFloor5 = 27f;
        private const float ButtonProgressGeyserFloor5 = 31f;
        /// <summary>Смещение кнопки от середины коридора в сторону открытой грани, ШП. Нажать — значит выйти на простреливаемое место.</summary>
        private const float ButtonOffsetFromPath = 4f;

        private const float DoorProgress = 40f;
        private const float CollapseStart = 34f;
        private const float CollapseEnd = 40f;
        private const float GeyserStart = 33f;
        private const float GeyserEnd = 38f;

        private const float DoorwayWidth = 4f;
        /// <summary>Длина паркурной площадки, ШП. Держим короткой: всё, что не ушло в площадки, достаётся разрывам.</summary>
        private const float PlatformLength = 1f;
        private const float WallThickness = 0.5f;

        /// <summary>Ширина трассы посередине коридора — от неё меряется смещение кнопки.</summary>
        private const float PathDepth = 7f;

        /// <summary>
        /// Полоса первого марша и дверного проёма — у ГЛУХОЙ стены, дальше
        /// всего от открытой грани.
        ///
        /// Проём обязан быть именно там. Лестничная комната закрыта от лифта
        /// стеной, но войти в неё как-то надо, и проём у открытой грани даёт
        /// Охотнику косой прострел прямо сквозь него: луч из шахты входит в
        /// коридор, проходит в проём по касательной и достаёт до тех, кто уже
        /// внутри. У глухой стены такого угла не существует.
        /// </summary>
        private const float StairFlightANearZ = 9.5f;
        /// <summary>Полоса второго марша — в середине комнаты. Над ней и делается проём в перекрытии.</summary>
        private const float StairFlightBFarZ = 4.5f;
        /// <summary>Ширина марша, ШП. Она же ширина проёма в перекрытии над ним.</summary>
        private const float StairFlightWidth = 3f;

        /// <summary>
        /// На сколько ШП лестница отодвинута вглубь комнаты от её порога.
        /// Сам проём двери Охотник простреливает по касательной примерно на
        /// полтора ШП внутрь — и это нормально: у входа стоять опасно. Но сама
        /// лестница обязана начинаться уже за этой чертой.
        /// </summary>
        private const float StairStartOffset = 2f;
        /// <summary>Полоса, по которой идёт паркур: ближе к открытой грани, то есть на виду у Охотника.</summary>
        private const float ParkourNearZ = 3f;
        private const float ParkourWidth = 4f;

        /// <summary>Этаж: сколько укрытий, самый длинный открытый участок, разрывов паркура и максимальный разрыв (спека, раздел 3.9).</summary>
        private readonly struct FloorPlan
        {
            public int Covers { get; }
            public float LongestOpen { get; }
            public int Gaps { get; }
            public float MaxGap { get; }
            public bool Ice { get; }

            public FloorPlan(int covers, float longestOpen, int gaps, float maxGap, bool ice)
            {
                Covers = covers;
                LongestOpen = longestOpen;
                Gaps = gaps;
                MaxGap = maxGap;
                Ice = ice;
            }
        }

        private static readonly FloorPlan[] Plans =
        {
            new FloorPlan(12, 6f, 2, 2f, false),   // 1 — ферма, намеренно лёгкий
            new FloorPlan(10, 8f, 3, 3f, false),   // 2 — ферма, Дверь
            new FloorPlan(8, 10f, 3, 3f, true),    // 3 — зима, Гейзер
            new FloorPlan(6, 13f, 4, 4f, false),   // 4 — ферма, Провал
            new FloorPlan(5, 16f, 4, 5f, true)     // 5 — зима, Дверь + Гейзер
        };

        private static DuckHuntConfig config;
        private static int groundLayer;
        private static int coverLayer;
        private static int barrierLayer;
        private static System.Random random;
        private static readonly List<TrapBase> builtTraps = new List<TrapBase>(8);
        private static readonly List<SpringTrap> builtGeysers = new List<SpringTrap>(4);
        private static readonly List<DuckHuntStairs> builtStairs = new List<DuckHuntStairs>(8);
        private static readonly List<string> warnings = new List<string>(8);

        private static Material floorMaterial;
        private static Material wallMaterial;
        private static Material coverHighMaterial;
        private static Material coverLowMaterial;
        private static Material platformMaterial;
        private static Material iceMaterial;
        private static Material trapMaterial;
        private static Material buttonReadyMaterial;
        private static Material buttonBusyMaterial;

        [MenuItem("Igruha/Minigames/Rebuild Duck Hunt Arena")]
        private static void Rebuild()
        {
            config = AssetDatabase.LoadAssetAtPath<DuckHuntConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogError($"DuckHuntArenaBuilder: не найден {ConfigPath}. Создай ассет через Create → Igruha → Duck Hunt Config.");
                return;
            }

            GameObject arenaRoot = GameObject.Find("_Arena");
            GameObject spawnsRoot = GameObject.Find("_Spawns");
            if (arenaRoot == null || spawnsRoot == null)
            {
                Debug.LogError("DuckHuntArenaBuilder: открой сцену DuckHunt — не найдены _Arena/_Spawns.");
                return;
            }

            if (!ResolveLayers())
            {
                return;
            }

            random = new System.Random(Seed);
            builtTraps.Clear();
            builtGeysers.Clear();
            builtStairs.Clear();
            warnings.Clear();
            EnsureMaterials();

            arenaRoot.transform.position = Vector3.zero;
            arenaRoot.transform.rotation = Quaternion.identity;

            // Сносим всё содержимое арены, а не только свои группы. Планировка
            // прошлой спеки лежала под другими именами, и пересборка её не
            // трогала: старые укрытия оставались стоять поверх новых, в том
            // числе прямо на точках спавна.
            for (int i = arenaRoot.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(arenaRoot.transform.GetChild(i).gameObject);
            }

            Transform tower = ResetGroup(arenaRoot.transform, "Tower");
            for (int floor = 0; floor < config.FloorCount; floor++)
            {
                BuildFloor(tower, floor);
            }

            BuildRoof(tower);
            RidePlatform platform = BuildElevator(ResetGroup(arenaRoot.transform, "ElevatorShaft"));
            DuckHuntFinishZone finish = BuildFinishZone(tower);
            BuildSpawns(spawnsRoot.transform, platform);
            BuildKillZone();

            DuckHuntArena arena = EnsureArenaComponent(arenaRoot);
            WireMinigame(arena, platform, finish);

            Physics.SyncTransforms();
            Validate(spawnsRoot.transform);

            MarkSceneDirty();
            ReportResult();
        }

        private static bool ResolveLayers()
        {
            groundLayer = LayerMask.NameToLayer(GroundLayerName);
            coverLayer = LayerMask.NameToLayer(CoverLayerName);
            barrierLayer = LayerMask.NameToLayer(BarrierLayerName);

            if (groundLayer < 0 || coverLayer < 0 || barrierLayer < 0)
            {
                Debug.LogError($"DuckHuntArenaBuilder: нет слоёв {GroundLayerName}/{CoverLayerName}/{BarrierLayerName} — " +
                               "без них не работают ни прыжок, ни прозрачная стена.");
                return false;
            }

            return true;
        }

        // ========== ЭТАЖ ==========

        private static void BuildFloor(Transform tower, int floor)
        {
            Transform root = ResetGroup(tower, $"Floor_{floor + 1}");
            FloorPlan plan = Plans[floor];

            float baseY = floor * config.FloorStepWidths;
            float length = config.FloorLengthWidths;
            float depth = DepthWidths;
            float ceiling = CeilingWidths;

            BuildSlab(root, floor, baseY);
            BuildFloorWalls(root, floor, baseY, length, depth, ceiling);
            BuildStairRoom(root, floor, baseY);
            BuildBarrier(root, baseY, length, depth, ceiling);
            BuildParkour(root, floor, baseY, plan);
            BuildCovers(root, floor, baseY, plan);
            BuildFloorTrap(root, floor, baseY);
            BuildCheckpoint(root, floor, baseY);

            if (plan.Ice)
            {
                BuildIce(root, floor, baseY);
            }
        }

        /// <summary>
        /// Перекрытие этажа. Строится кусками, потому что в нём две дыры:
        /// над лестничной комнатой этажа ниже (иначе снизу не подняться) и,
        /// на четвёртом этаже, участок под ловушку-провал.
        /// </summary>
        /// <summary>
        /// Перекрытие этажа. В нём два выреза: проём над лестницей этажа ниже и,
        /// на четвёртом этаже, участок под ловушку-провал.
        ///
        /// Проём делается ровно по верхней части лестницы, а не во всю
        /// лестничную комнату. По змейке вход этажа N+1 — это та же зона, что
        /// лестничная комната этажа N, и дыра во всю комнату означала бы, что
        /// поднявшаяся Утка выходит прямо в пустоту и падает обратно вниз.
        /// </summary>
        private static void BuildSlab(Transform root, int floor, float baseY)
        {
            Transform slab = ResetGroup(root, "Slab");
            float length = config.FloorLengthWidths;
            float depth = DepthWidths;
            float top = baseY;
            float bottom = baseY - SlabWidths;
            int piece = 0;

            bool hasHole = floor > 0;
            GetStairHoleRange(floor - 1, out float holeXMin, out float holeXMax);
            float holeZMin = StairFlightBFarZ;
            float holeZMax = StairFlightBFarZ + StairFlightWidth;

            // Провал стоит на четвёртом этаже: кусок пола перед выходной дверью.
            bool hasCollapse = floor == 3;
            float collapseMin = 0f;
            float collapseMax = 0f;
            if (hasCollapse)
            {
                GetXRange(floor, CollapseStart, CollapseEnd, out collapseMin, out collapseMax);
            }

            // Границы по X, на которых меняется характер перекрытия.
            var edges = new List<float> { 0f, length };
            if (hasHole)
            {
                edges.Add(holeXMin);
                edges.Add(holeXMax);
            }

            if (hasCollapse)
            {
                edges.Add(collapseMin);
                edges.Add(collapseMax);
            }

            edges.Sort();

            for (int i = 0; i < edges.Count - 1; i++)
            {
                float from = edges[i];
                float to = edges[i + 1];
                if (to - from < 0.001f)
                {
                    continue;
                }

                float middle = (from + to) * 0.5f;

                // Полоса провала строится отдельно — она умеет исчезать.
                if (hasCollapse && middle > collapseMin && middle < collapseMax)
                {
                    continue;
                }

                // Полоса с проёмом: сплошная всюду, кроме глубины самого проёма.
                if (hasHole && middle > holeXMin && middle < holeXMax)
                {
                    Box(slab, $"Slab_{++piece}", groundLayer, floorMaterial, from, to, bottom, top, 0f, holeZMin);
                    Box(slab, $"Slab_{++piece}", groundLayer, floorMaterial, from, to, bottom, top, holeZMax, depth);
                    continue;
                }

                Box(slab, $"Slab_{++piece}", groundLayer, floorMaterial, from, to, bottom, top, 0f, depth);
            }

            if (hasCollapse)
            {
                BuildCollapseTrap(root, floor, collapseMin, collapseMax, bottom, top, depth);
            }
        }

        /// <summary>
        /// Где лестница пробивает перекрытие: вся длина лестничной комнаты, но
        /// только полоса второго марша по глубине.
        ///
        /// Во всю длину — потому что потолок этажа 7 ШП, а подняться надо на 8:
        /// уже со второй ступени второго марша макушка идущего оказывается выше
        /// перекрытия. Только полоса марша — потому что вход следующего этажа
        /// это та же зона, и во всю ширину поднявшийся выходил бы в пустоту.
        /// </summary>
        private static void GetStairHoleRange(int floorBelow, out float xMin, out float xMax)
        {
            if (floorBelow < 0)
            {
                xMin = 0f;
                xMax = 0f;
                return;
            }

            GetXRange(floorBelow, StairRoomStart, config.FloorLengthWidths, out xMin, out xMax);
        }

        private static void BuildFloorWalls(Transform root, int floor, float baseY, float length, float depth, float ceiling)
        {
            Transform walls = ResetGroup(root, "Walls");
            float top = baseY + ceiling;

            // Глухая задняя стена и два торца. Открытой остаётся только грань Z = 0.
            Box(walls, "Wall_Back", groundLayer, wallMaterial, 0f, length, baseY, top, depth, depth + WallThickness);
            Box(walls, "Wall_EndA", groundLayer, wallMaterial, -WallThickness, 0f, baseY, top, 0f, depth);
            Box(walls, "Wall_EndB", groundLayer, wallMaterial, length, length + WallThickness, baseY, top, 0f, depth);

            // Бортик вдоль открытой грани — чисто визуальный, чтобы край
            // читался глазом. Коллайдера у него нет намеренно: держит игрока
            // прозрачная стена, а сплошной бортик ловил бы выстрелы. Он всего
            // 1 ШП высотой, но глаз Охотника в нижней точке лифта примерно на
            // этом же уровне — и весь первый этаж переставал простреливаться.
            GameObject ledge = Box(walls, "Ledge", groundLayer, wallMaterial,
                0f, length, baseY, baseY + LedgeWidths, -WallThickness, 0f);
            Object.DestroyImmediate(ledge.GetComponent<Collider>());
        }

        /// <summary>
        /// Лестничная комната — сейф-зона. Она не «запрещает урон», а закрыта
        /// сплошной стеной со стороны лифта: Охотник туда не попадает потому,
        /// что мешает стена.
        ///
        /// Подъём собран из площадок под высоту прыжка, а не из ступеней:
        /// этаж поднимается на 8 ШП внутри комнаты 8 × 8, и настоящая лестница
        /// на таком уклоне встаёт круче 45° — по ней капсула персонажа просто
        /// сползает. В арт-фазе площадки прячутся под вид лестницы.
        /// </summary>
        private static void BuildStairRoom(Transform root, int floor, float baseY)
        {
            Transform room = ResetGroup(root, "StairRoom");
            GetStairRoomXRange(floor, out float xMin, out float xMax);
            float ceiling = CeilingWidths;
            float roomDepth = StairRoomDepthWidths;

            // Стена со стороны лифта — та самая, из-за которой комната не простреливается.
            Box(room, "Wall_TowardElevator", groundLayer, wallMaterial,
                xMin, xMax, baseY, baseY + ceiling, -WallThickness, 0f);

            // Перегородка от коридора с проёмом под дверь.
            float doorX = GetX(floor, DoorProgress);
            float partitionMin = Mathf.Min(doorX, doorX + (IsForward(floor) ? -WallThickness : WallThickness));
            float partitionMax = partitionMin + WallThickness;
            // Проём ведёт ровно на первый марш: иначе вошедший упирается в
            // высокие ступени второго и подъёма не находит.
            float doorwayMin = StairFlightANearZ;
            float doorwayMax = doorwayMin + DoorwayWidth;

            Box(room, "Partition_A", groundLayer, wallMaterial,
                partitionMin, partitionMax, baseY, baseY + ceiling, 0f, doorwayMin);
            Box(room, "Partition_B", groundLayer, wallMaterial,
                partitionMin, partitionMax, baseY, baseY + ceiling, doorwayMax, DepthWidths);

            BuildStairPlatforms(room, floor, baseY);
            BuildDoorTrap(root, floor, partitionMin, partitionMax, baseY, doorwayMin, doorwayMax);
        }

        /// <summary>
        /// Подъём на следующий этаж — двухмаршевая лестница с площадкой на
        /// развороте. Один прямой марш здесь невозможен: комната 8 × 8 ШП, а
        /// подняться нужно на 8 ШП, то есть уклон вышел бы 45°. Два марша дают
        /// 16 ШП разбега на те же 8 ШП высоты — обычные 26°.
        ///
        /// Ступени сплошные, от пола до своей высоты, а не парящие площадки:
        /// провалиться между ними нельзя, и подъём одинаково проходим и для
        /// живого игрока, и для болванки соло-теста.
        /// </summary>
        private static void BuildStairPlatforms(Transform room, int floor, float baseY)
        {
            Transform stairs = ResetGroup(room, "Stairs");

            const int StepsPerFlight = 8;
            float rise = config.FloorStepWidths / (StepsPerFlight * 2);
            float flightWidth = StairFlightWidth;
            float landingHeight = config.FloorStepWidths * 0.5f;

            // Строим в прогрессе трассы, а не в мировом X. По змейке половина
            // этажей бежит навстречу, и лестница, заданная в мировых
            // координатах, на них оказывается развёрнутой: вход у одного конца
            // комнаты, первая ступень — у противоположного.
            float pStart = StairRoomStart + StairStartOffset;
            float pEnd = config.FloorLengthWidths - 0.5f;
            float run = (pEnd - pStart) / StepsPerFlight;

            var steps = new List<Transform>(StepsPerFlight * 2 + 1);

            // Первый марш: от входа вглубь комнаты, вдоль глухой стены.
            for (int i = 0; i < StepsPerFlight; i++)
            {
                GetXRange(floor, pStart + run * i, pStart + run * (i + 1), out float from, out float to);
                float top = baseY + rise * (i + 1);
                steps.Add(Box(stairs, $"FlightA_{i + 1}", groundLayer, platformMaterial,
                    from, to, baseY, top, StairFlightANearZ, StairFlightANearZ + flightWidth).transform);
            }

            // Площадка разворота — заподлицо с верхом первого марша и ровно на
            // его последней ступени по длине. Шире её делать нельзя: площадка
            // выше марша накрыла бы его верхнюю половину и встала бы стеной
            // посреди собственной лестницы. По глубине она перекидывает с
            // полосы первого марша на полосу второго.
            float landingZMin = Mathf.Min(StairFlightANearZ, StairFlightBFarZ);
            float landingZMax = Mathf.Max(StairFlightANearZ, StairFlightBFarZ) + flightWidth;
            GetXRange(floor, pEnd - run, pEnd, out float landFrom, out float landTo);
            steps.Add(Box(stairs, "Landing", groundLayer, platformMaterial,
                landFrom, landTo, baseY, baseY + landingHeight, landingZMin, landingZMax).transform);

            // Второй марш идёт обратно и выводит ровно на уровень следующего этажа.
            for (int i = 0; i < StepsPerFlight; i++)
            {
                GetXRange(floor, pEnd - run * (i + 1), pEnd - run * i, out float from, out float to);
                float top = baseY + landingHeight + rise * (i + 1);
                steps.Add(Box(stairs, $"FlightB_{i + 1}", groundLayer, platformMaterial,
                    from, to, baseY, top, StairFlightBFarZ, StairFlightBFarZ + flightWidth).transform);
            }

            var ladder = stairs.gameObject.AddComponent<DuckHuntStairs>();
            var so = new SerializedObject(ladder);
            SerializedProperty list = so.FindProperty("steps");
            list.arraySize = steps.Count;
            for (int i = 0; i < steps.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = steps[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            builtStairs.Add(ladder);
        }

        /// <summary>
        /// Прозрачная стена вдоль открытой грани. Лежит на отдельном слое,
        /// исключённом из маски выстрела: игрока она держит, а луч Охотника
        /// проходит сквозь неё свободно. Рендерера у неё нет — край показывает
        /// бортик.
        ///
        /// Без неё выбитый ударом или гейзером персонаж улетал бы с этажа
        /// наружу и падал мимо всей башни до земли.
        /// </summary>
        private static void BuildBarrier(Transform root, float baseY, float length, float depth, float ceiling)
        {
            Transform group = ResetGroup(root, "Barrier");
            GameObject go = Box(group, "OpenEdgeBarrier", barrierLayer, null,
                0f, length, baseY, baseY + ceiling, -WallThickness, 0f);

            Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(go.GetComponent<MeshFilter>());
        }

        /// <summary>
        /// Паркур: площадки разной высоты с разрывами. Разрыв меряется между
        /// краями площадок и обязан оставаться заметно ниже дальности прыжка
        /// на бегу (6.5 ШП), иначе секция превращается в лотерею.
        /// </summary>
        private static void BuildParkour(Transform root, int floor, float baseY, FloorPlan plan)
        {
            Transform group = ResetGroup(root, "Parkour");
            int platforms = plan.Gaps + 1;
            float span = ParkourEnd - ParkourStart;
            float available = span - platforms * PlatformLength;
            float gap = plan.Gaps > 0 ? Mathf.Min(plan.MaxGap, available / plan.Gaps) : 0f;

            if (plan.Gaps > 0 && gap < plan.MaxGap - 0.01f)
            {
                warnings.Add(
                    $"этаж {floor + 1}: разрыв паркура ужат до {gap:F2} ШП вместо {plan.MaxGap:F0} по спеке — " +
                    $"{plan.Gaps} разрывов по {plan.MaxGap:F0} ШП не помещаются в секцию длиной {span:F0} ШП");
            }

            float cursor = ParkourStart;
            for (int i = 0; i < platforms; i++)
            {
                // Высоты в пределах 0.5…2.0 ШП: под потолком 7 ШП с площадки
                // в 2 ШП полный прыжок ещё проходит, не задевая макушкой.
                float height = Mathf.Lerp(0.5f, 2f, (float)random.NextDouble());
                GetXRange(floor, cursor, cursor + PlatformLength, out float xMin, out float xMax);
                cursor += PlatformLength + gap;

                // Над проёмом лестницы площадку не ставим — она станет потолком
                // тому, кто по этой лестнице поднимается.
                if (OverlapsStairwell(floor, xMin, xMax, ParkourNearZ, ParkourNearZ + ParkourWidth))
                {
                    continue;
                }

                Box(group, $"Platform_{i + 1}", groundLayer, platformMaterial,
                    xMin, xMax, baseY + height - 0.35f, baseY + height, ParkourNearZ, ParkourNearZ + ParkourWidth);
            }
        }

        /// <summary>
        /// Укрытия. Высокие прячут стоящего, низкие — только присевшего, и
        /// присед стоит 55% скорости: за низким укрытием безопасно, но медленно.
        /// Плотность падает от этажа к этажу — это и есть прогрессия сложности.
        /// </summary>
        private static void BuildCovers(Transform root, int floor, float baseY, FloorPlan plan)
        {
            Transform group = ResetGroup(root, "Covers");
            float from = CoverStart;
            float to = StairRoomStart - 2f;
            float step = (to - from) / Mathf.Max(1, plan.Covers);

            for (int i = 0; i < plan.Covers; i++)
            {
                float p = from + step * (i + 0.5f) + (float)(random.NextDouble() - 0.5) * step * 0.5f;

                // Зону эффекта ловушек не загораживаем: спека требует прямой
                // видимости от кнопки до зоны, иначе нажатие — лотерея.
                if (p > GeyserStart - 1f && p < CollapseEnd + 1f)
                {
                    continue;
                }

                bool high = random.NextDouble() < 0.6;
                float height = high ? HighCoverWidths : LowCoverWidths;
                float width = Mathf.Lerp(1.5f, 2.5f, (float)random.NextDouble());
                float z = Mathf.Lerp(1.5f, DepthWidths - 3.5f, (float)random.NextDouble());

                GetXRange(floor, p, p + width, out float xMin, out float xMax);
                if (OverlapsStairwell(floor, xMin, xMax, z, z + 2f))
                {
                    continue;
                }

                Box(group, $"Cover_{i + 1:00}_{(high ? "High" : "Low")}", coverLayer,
                    high ? coverHighMaterial : coverLowMaterial,
                    xMin, xMax, baseY, baseY + height, z, z + 2f);
            }
        }

        // ========== ЛОВУШКИ ==========

        private static void BuildFloorTrap(Transform root, int floor, float baseY)
        {
            switch (floor)
            {
                case 1:
                    BuildButton(root, floor, baseY, ButtonProgress, "Door", FindTrap(root, "DoorTrap"));
                    break;
                case 2:
                    BuildGeyser(root, floor, baseY, ButtonProgress);
                    break;
                case 3:
                    BuildButton(root, floor, baseY, ButtonProgress, "Collapse", FindTrap(root, "CollapseTrap"));
                    break;
                case 4:
                    BuildButton(root, floor, baseY, ButtonProgressDoorFloor5, "Door", FindTrap(root, "DoorTrap"));
                    BuildGeyser(root, floor, baseY, ButtonProgressGeyserFloor5);
                    break;
            }
        }

        /// <summary>
        /// Дверь на выходе с этажа. Захлопнувшись, она держит подбежавшего
        /// снаружи — на открытом месте под обстрелом.
        /// </summary>
        private static void BuildDoorTrap(Transform root, int floor, float xMin, float xMax, float baseY, float zMin, float zMax)
        {
            if (floor != 1 && floor != 4)
            {
                return;
            }

            Transform group = ResetGroup(root, "DoorTrap");
            var door = group.gameObject.AddComponent<DoorTrap>();
            float height = CeilingWidths;

            GameObject panel = Box(group, "Panel", groundLayer, trapMaterial,
                xMin, xMax, baseY, baseY + height, zMin, zMax);

            // Открытая створка уезжает вбок, в толщу стены рядом с проёмом.
            // Вниз её убирать нельзя: этаж стоит на перекрытии толщиной в один
            // ШП, и опустившаяся на свою высоту створка оказывается в коридоре
            // этажом ниже — перегораживая его наглухо и навсегда.
            Vector3 closed = panel.transform.localPosition;
            Vector3 open = closed + Vector3.forward * config.ToUnits(DoorwayWidth);

            var so = new SerializedObject(door);
            so.FindProperty("doorBody").objectReferenceValue = panel.transform;
            so.FindProperty("closedLocalPosition").vector3Value = closed;
            so.FindProperty("openLocalPosition").vector3Value = open;
            so.FindProperty("closedDuration").floatValue = config.DoorClosedDuration;
            so.FindProperty("cooldown").floatValue = config.ButtonCooldown;
            so.ApplyModifiedPropertiesWithoutUndo();

            panel.transform.localPosition = open;
            builtTraps.Add(door);
        }

        /// <summary>
        /// Провал: участок пола перед выходной дверью исчезает, и все, кто на
        /// нём стоял, падают на этаж ниже. Обойти нельзя — участок перекрывает
        /// этаж поперёк целиком.
        /// </summary>
        private static void BuildCollapseTrap(Transform root, int floor, float xMin, float xMax, float bottom, float top, float depth)
        {
            Transform group = ResetGroup(root, "CollapseTrap");
            var trap = group.gameObject.AddComponent<CollapsingFloorTrap>();

            GameObject section = Box(group, "Section", groundLayer, trapMaterial,
                xMin, xMax, bottom, top, 0f, depth);

            var so = new SerializedObject(trap);
            SerializedProperty colliders = so.FindProperty("floorColliders");
            colliders.arraySize = 1;
            colliders.GetArrayElementAtIndex(0).objectReferenceValue = section.GetComponent<Collider>();

            SerializedProperty renderers = so.FindProperty("floorRenderers");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = section.GetComponent<MeshRenderer>();

            so.FindProperty("openDuration").floatValue = config.CollapseDuration;
            so.FindProperty("cooldown").floatValue = config.ButtonCooldown;
            so.ApplyModifiedPropertiesWithoutUndo();

            builtTraps.Add(trap);
        }

        /// <summary>
        /// Гейзер: площадка выстреливает жертву вверх — вдвое выше высокого
        /// укрытия и заметно ниже потолка. Задерживает минимально, зато
        /// выносит на открытое место в воздухе. Убивает не ловушка, а Охотник.
        /// </summary>
        private static void BuildGeyser(Transform root, int floor, float baseY, float buttonProgress)
        {
            Transform group = ResetGroup(root, "GeyserTrap");

            GetXRange(floor, GeyserStart, GeyserEnd, out float xMin, out float xMax);
            float z = (DepthWidths - 5f) * 0.5f;

            // Коллайдер добавляется до ловушки: SpringTrap требует его как
            // обязательный, и Unity подсунула бы свой второй.
            var box = group.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            var trap = group.gameObject.AddComponent<SpringTrap>();
            Vector3 min = ToWorld(xMin, baseY, z);
            Vector3 max = ToWorld(xMax, baseY + 2f, z + 5f);
            box.center = (min + max) * 0.5f;
            box.size = max - min;

            // Видимая площадка, чтобы зона читалась с кнопки: спека требует
            // прямой видимости, а невидимую зону не во что целиться глазом.
            Box(group, "Pad", groundLayer, trapMaterial, xMin, xMax, baseY, baseY + 0.15f, z, z + 5f);

            var so = new SerializedObject(trap);
            so.FindProperty("cooldown").floatValue = config.ButtonCooldown;
            so.FindProperty("forwardTilt").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();

            builtTraps.Add(trap);
            builtGeysers.Add(trap);
            BuildButton(root, floor, baseY, buttonProgress, "Geyser", trap);
        }

        /// <summary>
        /// Кнопка ловушки. Смещена от кратчайшего пути в сторону открытой грани:
        /// отклонился ради подставы — потерял время и подставился под выстрел.
        /// Это и есть цена нажатия.
        /// </summary>
        private static void BuildButton(Transform root, int floor, float baseY, float progress, string label, TrapBase trap)
        {
            if (trap == null)
            {
                return;
            }

            Transform group = ResetGroup(root, $"Button_{label}");
            float z = PathDepth - ButtonOffsetFromPath;

            GetXRange(floor, progress, progress + 1f, out float xMin, out float xMax);
            GameObject post = Box(group, "Post", groundLayer, buttonReadyMaterial,
                xMin, xMax, baseY, baseY + 2f, z, z + 1f);

            var button = group.gameObject.AddComponent<TrapActivationButton>();
            var so = new SerializedObject(button);
            so.FindProperty("buttonName").stringValue = label;

            SerializedProperty traps = so.FindProperty("traps");
            traps.arraySize = 1;
            traps.GetArrayElementAtIndex(0).objectReferenceValue = trap;

            so.FindProperty("indicator").objectReferenceValue = post.GetComponent<MeshRenderer>();
            so.FindProperty("readyMaterial").objectReferenceValue = buttonReadyMaterial;
            so.FindProperty("cooldownMaterial").objectReferenceValue = buttonBusyMaterial;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static TrapBase FindTrap(Transform root, string groupName)
        {
            Transform group = root.Find(groupName);
            return group != null ? group.GetComponent<TrapBase>() : null;
        }

        /// <summary>
        /// Лёд на зимних этажах. Физматериалом это не делается: контроллер
        /// задаёт скорость напрямую, и трение коллайдера на горизонтальное
        /// движение не влияет вообще.
        /// </summary>
        private static void BuildIce(Transform root, int floor, float baseY)
        {
            Transform group = ResetGroup(root, "Ice");

            var box = group.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            var modifier = group.gameObject.AddComponent<SurfaceModifier>();
            Vector3 min = ToWorld(0f, baseY, 0f);
            Vector3 max = ToWorld(config.FloorLengthWidths, baseY + 2f, DepthWidths);
            box.center = (min + max) * 0.5f;
            box.size = max - min;

            var so = new SerializedObject(modifier);
            so.FindProperty("accelerationMultiplier").floatValue = config.IceAccelerationMultiplier;
            so.FindProperty("decelerationMultiplier").floatValue = config.IceDecelerationMultiplier;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Видимая плитка льда: без неё зимний этаж ничем не отличается от
            // летнего. Коллайдер ей снимаем — опору даёт перекрытие, а лишний
            // сплошной лист поверх всего этажа накрывал бы и проём лестницы.
            GameObject tile = Box(group, "IceFloor", groundLayer, iceMaterial,
                0f, config.FloorLengthWidths, baseY, baseY + 0.05f, 0f, DepthWidths);
            Object.DestroyImmediate(tile.GetComponent<Collider>());
        }

        /// <summary>Точка возврата этажа: сюда StuckDetector вытаскивает застрявшего.</summary>
        private static void BuildCheckpoint(Transform root, int floor, float baseY)
        {
            Transform group = ResetGroup(root, "Checkpoint");
            GetStairRoomXRange(floor, out float xMin, out float xMax);

            group.position = ToWorld((xMin + xMax) * 0.5f, baseY + 0.1f, StairRoomDepthWidths * 0.5f);

            // Коллайдер первым: чекпоинт требует его как обязательный.
            var box = group.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(config.StairRoomSizeUnits, config.ToUnits(3f), config.StairRoomSizeUnits);
            group.gameObject.AddComponent<RespawnCheckpoint>();
        }

        // ========== КРЫША, ЛИФТ, ФИНИШ ==========

        private static void BuildRoof(Transform tower)
        {
            Transform root = ResetGroup(tower, "Roof");
            int lastFloor = config.FloorCount - 1;
            float baseY = config.FloorCount * config.FloorStepWidths;
            float length = config.FloorLengthWidths;
            float depth = DepthWidths;

            GetStairHoleRange(lastFloor, out float holeMin, out float holeMax);
            float holeZMin = StairFlightBFarZ;
            float holeZMax = StairFlightBFarZ + StairFlightWidth;

            // Настил с проёмом ровно над верхом лестницы: вокруг него крыша
            // сплошная, иначе вышедший на неё проваливается обратно.
            Box(root, "Roof_Near", groundLayer, floorMaterial,
                0f, Mathf.Min(holeMin, holeMax), baseY - SlabWidths, baseY, 0f, depth);
            Box(root, "Roof_Far", groundLayer, floorMaterial,
                Mathf.Max(holeMin, holeMax), length, baseY - SlabWidths, baseY, 0f, depth);
            Box(root, "Roof_HoleSideA", groundLayer, floorMaterial,
                holeMin, holeMax, baseY - SlabWidths, baseY, 0f, holeZMin);
            Box(root, "Roof_HoleSideB", groundLayer, floorMaterial,
                holeMin, holeMax, baseY - SlabWidths, baseY, holeZMax, depth);

            // Крыша простреливается — по спеке это осознанно. Бортик и барьер
            // те же, что на этажах: упасть с крыши нельзя.
            GameObject roofLedge = Box(root, "Ledge", groundLayer, wallMaterial,
                0f, length, baseY, baseY + LedgeWidths, -WallThickness, 0f);
            Object.DestroyImmediate(roofLedge.GetComponent<Collider>());

            GameObject barrier = Box(root, "Barrier", barrierLayer, null,
                0f, length, baseY, baseY + CeilingWidths, -WallThickness, 0f);
            Object.DestroyImmediate(barrier.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(barrier.GetComponent<MeshFilter>());

            // Единственное укрытие на крыше — по спеке ровно одно, на X = 40.
            // По глубине отодвинуто от проёма лестницы: там оно перекрыло бы
            // выход на крышу.
            Box(root, "Cover_Roof", coverLayer, coverHighMaterial,
                40f, 42f, baseY, baseY + HighCoverWidths, 9f, 11f);
        }

        private static DuckHuntFinishZone BuildFinishZone(Transform tower)
        {
            Transform root = ResetGroup(tower, "FinishZone");
            float baseY = config.FloorCount * config.FloorStepWidths;

            // Площадка 6 × 6 ШП на X 30–36, Z 4–10 (спека, раздел 3.8).
            Box(root, "Pad", groundLayer, trapMaterial, 30f, 36f, baseY, baseY + 0.1f, 4f, 10f);

            var box = root.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            var zone = root.gameObject.AddComponent<DuckHuntFinishZone>();
            Vector3 min = ToWorld(30f, baseY, 4f);
            Vector3 max = ToWorld(36f, baseY + 3f, 10f);
            box.center = (min + max) * 0.5f;
            box.size = max - min;

            return zone;
        }

        /// <summary>
        /// Шахта лифта — отдельная конструкция напротив середины открытой грани.
        /// Охотник заперт на платформе на весь раунд: в башню он не попадает
        /// ни при каких условиях.
        /// </summary>
        private static RidePlatform BuildElevator(Transform root)
        {
            float x = config.FloorLengthWidths * 0.5f;
            float z = -config.DistanceToElevatorUnits / config.CharacterWidth;
            float size = config.ElevatorPlatformSizeUnits / config.CharacterWidth;
            float half = size * 0.5f;
            float minH = config.ElevatorMinHeightUnits / config.CharacterWidth;
            float maxH = config.ElevatorMaxHeightUnits / config.CharacterWidth;

            // Направляющие шахты — чисто визуальный ориентир высоты, поэтому
            // без коллайдеров. Со сплошными они встают ровно в линию огня и
            // срезают Охотнику выстрелы вдоль этажа: стоят они у самой
            // платформы, и на косых углах перекрывают весь дальний конец башни.
            Transform rails = ResetGroup(root, "Rails");
            GameObject railA = Box(rails, "Rail_A", groundLayer, wallMaterial,
                x - half - 0.5f, x - half, minH, maxH + 2f, z - half, z - half + 0.5f);
            GameObject railB = Box(rails, "Rail_B", groundLayer, wallMaterial,
                x + half, x + half + 0.5f, minH, maxH + 2f, z + half - 0.5f, z + half);
            Object.DestroyImmediate(railA.GetComponent<Collider>());
            Object.DestroyImmediate(railB.GetComponent<Collider>());

            Transform platformRoot = ResetGroup(root, "Platform");
            platformRoot.position = ToWorld(x, minH, z);

            GameObject deck = Box(platformRoot, "Deck", groundLayer, platformMaterial,
                x - half, x + half, minH - 0.4f, minH, z - half, z + half);
            deck.transform.SetParent(platformRoot, true);

            // Перила по краям: сойти нельзя и так (RidePlatform держит пассажира),
            // но без них платформа не читается как платформа.
            Box(platformRoot, "Rail_Front", groundLayer, wallMaterial,
                x - half, x + half, minH, minH + 1f, z - half, z - half + 0.3f);

            var platform = platformRoot.gameObject.AddComponent<RidePlatform>();
            var body = platformRoot.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var so = new SerializedObject(platform);
            so.FindProperty("speed").floatValue = config.ElevatorSpeedUnits;
            so.FindProperty("minTravel").floatValue = 0f;
            so.FindProperty("maxTravel").floatValue = config.ElevatorMaxHeightUnits - config.ElevatorMinHeightUnits;
            so.FindProperty("rideZoneCenter").vector3Value = new Vector3(0f, config.ToUnits(1.6f), 0f);
            so.FindProperty("rideZoneSize").vector3Value = new Vector3(
                config.ElevatorPlatformSizeUnits, config.ToUnits(3.2f), config.ElevatorPlatformSizeUnits);
            so.FindProperty("confineMargin").floatValue = config.CharacterWidth * 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();

            return platform;
        }

        // ========== СПАВНЫ, ГРАНИЦЫ, ССЫЛКИ ==========

        private static void BuildSpawns(Transform spawnsRoot, RidePlatform platform)
        {
            // Точки, оставшиеся от прежней планировки, сносим: SpawnPointSet
            // собирает их по всей иерархии, и забытая точка старой арены
            // отправила бы игрока внутрь стены.
            var stale = spawnsRoot.GetComponentsInChildren<SpawnPoint>(true);
            for (int i = 0; i < stale.Length; i++)
            {
                if (stale[i] != null)
                {
                    Object.DestroyImmediate(stale[i].gameObject);
                }
            }

            Transform ducks = ResetGroup(spawnsRoot, "Ducks");

            // Восемь точек на входе первого этажа: игроков может быть меньше,
            // разнос по ним считает SpawnPointSet.
            for (int i = 0; i < 8; i++)
            {
                var go = new GameObject($"Spawn_Duck_{i + 1}");
                go.transform.SetParent(ducks, false);
                float p = 1f + i % 4;
                float z = 3f + (i / 4) * 4f;
                go.transform.position = ToWorld(GetX(0, p), 0.2f, z);
                // Смотрят вдоль трассы — иначе на старте игрок развёрнут в стену.
                go.transform.rotation = Quaternion.LookRotation(Vector3.right);
                go.AddComponent<SpawnPoint>();
            }

            Transform hunterRoot = ResetGroup(spawnsRoot, "Hunter");
            var hunterSpawn = new GameObject("Spawn_Hunter");
            hunterSpawn.transform.SetParent(hunterRoot, false);
            hunterSpawn.transform.position = platform != null
                ? platform.transform.position + Vector3.up * 0.5f
                : Vector3.zero;
            hunterSpawn.transform.rotation = Quaternion.LookRotation(Vector3.forward);

            var point = hunterSpawn.AddComponent<SpawnPoint>();
            var so = new SerializedObject(point);
            so.FindProperty("role").enumValueIndex = (int)SpawnRole.Special;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildKillZone()
        {
            GameObject bounds = GameObject.Find("_Bounds");
            if (bounds == null)
            {
                return;
            }

            var colliders = bounds.GetComponentsInChildren<BoxCollider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Transform t = colliders[i].transform;
                t.position = ToWorld(config.FloorLengthWidths * 0.5f, -6f, DepthWidths * 0.5f);
                t.localScale = new Vector3(
                    config.FloorLengthUnits * 1.5f, 1f, config.FloorDepthUnits + config.DistanceToElevatorUnits * 2.5f);
            }
        }

        private static DuckHuntArena EnsureArenaComponent(GameObject arenaRoot)
        {
            var arena = arenaRoot.GetComponent<DuckHuntArena>();
            if (arena == null)
            {
                arena = arenaRoot.AddComponent<DuckHuntArena>();
            }

            var so = new SerializedObject(arena);
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("origin").objectReferenceValue = arenaRoot.transform;

            SerializedProperty stairs = so.FindProperty("stairs");
            stairs.arraySize = builtStairs.Count;
            for (int i = 0; i < builtStairs.Count; i++)
            {
                stairs.GetArrayElementAtIndex(i).objectReferenceValue = builtStairs[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return arena;
        }

        /// <summary>
        /// Проставить контроллеру ссылки на всё, что только что построено.
        /// Иначе после каждой пересборки их пришлось бы тащить в инспектор
        /// руками — а их два десятка, и забытая ссылка выясняется уже в игре.
        /// </summary>
        private static void WireMinigame(DuckHuntArena arena, RidePlatform platform, DuckHuntFinishZone finish)
        {
            var minigame = Object.FindFirstObjectByType<DuckHuntMinigame>();
            if (minigame == null)
            {
                warnings.Add("в сцене нет DuckHuntMinigame — ссылки на арену, лифт и ловушки проставить некуда");
                return;
            }

            var so = new SerializedObject(minigame);
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("arena").objectReferenceValue = arena;
            so.FindProperty("elevator").objectReferenceValue = platform;
            so.FindProperty("finishZone").objectReferenceValue = finish;

            SerializedProperty traps = so.FindProperty("traps");
            traps.arraySize = builtTraps.Count;
            for (int i = 0; i < builtTraps.Count; i++)
            {
                traps.GetArrayElementAtIndex(i).objectReferenceValue = builtTraps[i];
            }

            SerializedProperty geysers = so.FindProperty("geysers");
            geysers.arraySize = builtGeysers.Count;
            for (int i = 0; i < builtGeysers.Count; i++)
            {
                geysers.GetArrayElementAtIndex(i).objectReferenceValue = builtGeysers[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ========== ПРОВЕРКА ==========

        /// <summary>
        /// Проверяем то, что обязано держаться по спеке и молча ломается при
        /// правке чисел: лестничная комната не простреливается, а спавны Уток
        /// на старте прикрыты.
        /// </summary>
        private static void Validate(Transform spawnsRoot)
        {
            int mask = (1 << groundLayer) | (1 << coverLayer);

            for (int floor = 0; floor < config.FloorCount; floor++)
            {
                GetStairRoomXRange(floor, out float xMin, out float xMax);
                Vector3 inside = ToWorld((xMin + xMax) * 0.5f, floor * config.FloorStepWidths + 1.5f, StairRoomDepthWidths * 0.5f);
                Vector3 eye = ElevatorEye(floor);

                Vector3 direction = inside - eye;
                if (!Physics.Raycast(eye, direction.normalized, direction.magnitude, mask))
                {
                    warnings.Add($"этаж {floor + 1}: лестничная комната простреливается с лифта — сейф-зоны нет");
                }
            }

            ValidateLineOfFire();
        }

        /// <summary>
        /// Достаёт ли выстрел до открытой части этажа. Проверка обратная
        /// сейф-зоне и не менее нужная: любой декоративный коллайдер вдоль
        /// открытой грани превращает Охотника в мебель, причём молча — стрелять
        /// он может, патроны тратятся, а луч упирается в бортик в метре от дула.
        /// </summary>
        private static void ValidateLineOfFire()
        {
            int mask = config.ShotMask.value;

            for (int floor = 0; floor < config.FloorCount; floor++)
            {
                Vector3 eye = ElevatorEye(floor);
                int blocked = 0;
                string blocker = null;

                // Секция укрытий — та самая, где у Охотника прямой угол.
                for (float p = 18f; p <= 28f; p += 2f)
                {
                    Vector3 target = ToWorld(p, floor * config.FloorStepWidths + 1.2f, 2f);
                    Vector3 direction = target - eye;

                    if (!Physics.Raycast(eye, direction.normalized, out RaycastHit hit, direction.magnitude, mask, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }

                    // Укрытие на линии — это и есть задумка, оно не считается помехой.
                    if (hit.collider.gameObject.layer == coverLayer)
                    {
                        continue;
                    }

                    blocked++;
                    blocker ??= hit.collider.name;
                }

                if (blocked > 0)
                {
                    warnings.Add(
                        $"этаж {floor + 1}: {blocked} из 6 линий огня перекрыты не укрытием, а «{blocker}» — " +
                        "Охотник по этому этажу стрелять не сможет");
                }
            }
        }

        private static Vector3 ElevatorEye(int floor) => ToWorld(
            config.FloorLengthWidths * 0.5f,
            floor * config.FloorStepWidths + 2f,
            -config.DistanceToElevatorUnits / config.CharacterWidth);

        private static void ReportResult()
        {
            string summary =
                $"Duck Hunt: башня пересобрана. ШП = {config.CharacterWidth:F2}, этажей {config.FloorCount}, " +
                $"этаж {config.FloorLengthUnits:F2} × {config.FloorDepthUnits:F2}, потолок {config.CeilingHeightUnits:F2}, " +
                $"шаг {config.FloorStepUnits:F2}, высота башни {config.TowerHeightUnits:F2}, " +
                $"ход лифта {config.ElevatorMinHeightUnits:F2}…{config.ElevatorMaxHeightUnits:F2}, ловушек {builtTraps.Count}.";

            if (warnings.Count == 0)
            {
                Debug.Log(summary);
                return;
            }

            Debug.LogWarning(summary + "\nЗамечания:\n— " + string.Join("\n— ", warnings));
        }

        // ========== ГЕОМЕТРИЯ ==========

        private static float DepthWidths => config.FloorDepthUnits / config.CharacterWidth;
        private static float CeilingWidths => config.CeilingHeightUnits / config.CharacterWidth;
        private static float SlabWidths => config.SlabThicknessUnits / config.CharacterWidth;
        private static float StairRoomDepthWidths => config.StairRoomSizeUnits / config.CharacterWidth;

        /// <summary>Длина лестничной комнаты вдоль трассы, ШП: от её начала до конца этажа.</summary>
        private static float StairRoomWidthWidths => config.FloorLengthWidths - StairRoomStart;
        private static float HighCoverWidths => config.HighCoverHeightUnits / config.CharacterWidth;
        private static float LowCoverWidths => config.LowCoverHeightUnits / config.CharacterWidth;
        private static float LedgeWidths => config.LedgeHeightUnits / config.CharacterWidth;

        private static bool IsForward(int floor) => DuckHuntArena.IsForwardFloor(floor);

        private static float GetX(int floor, float progress) =>
            IsForward(floor) ? progress : config.FloorLengthWidths - progress;

        private static void GetXRange(int floor, float fromProgress, float toProgress, out float xMin, out float xMax)
        {
            float a = GetX(floor, fromProgress);
            float b = GetX(floor, toProgress);
            xMin = Mathf.Min(a, b);
            xMax = Mathf.Max(a, b);
        }

        private static void GetStairRoomXRange(int floor, out float xMin, out float xMax) =>
            GetXRange(floor, StairRoomStart, config.FloorLengthWidths, out xMin, out xMax);

        /// <summary>
        /// Занимает ли прямоугольник шахту лестницы, ведущей на этот этаж снизу.
        ///
        /// Над проёмом нельзя ставить ничего: любой коллайдер там становится
        /// потолком для того, кто поднимается, и подъём упирается в него
        /// макушкой. Проверять приходится всё подряд — и паркур, и укрытия, и
        /// декоративную плитку льда, потому что проём лежит ровно во входной
        /// зоне этажа, куда всё это и просится по планировке.
        /// </summary>
        private static bool OverlapsStairwell(int floor, float xMin, float xMax, float zMin, float zMax)
        {
            if (floor <= 0)
            {
                return false;
            }

            GetStairHoleRange(floor - 1, out float holeXMin, out float holeXMax);
            float holeZMin = StairFlightBFarZ;
            float holeZMax = StairFlightBFarZ + StairFlightWidth;

            return xMax > holeXMin && xMin < holeXMax && zMax > holeZMin && zMin < holeZMax;
        }

        /// <summary>Точка арены из координат в ШП: X вдоль трассы, Y высота, Z поперёк от открытой грани.</summary>
        private static Vector3 ToWorld(float x, float y, float z) =>
            new Vector3(config.ToUnits(x), config.ToUnits(y), config.ToUnits(z));

        /// <summary>Коробка по границам в ШП. Материал null — рендерер остаётся с материалом по умолчанию.</summary>
        private static GameObject Box(Transform parent, string name, int layer, Material material,
            float xMin, float xMax, float yMin, float yMax, float zMin, float zMax)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            go.transform.SetParent(parent, false);

            Vector3 min = ToWorld(xMin, yMin, zMin);
            Vector3 max = ToWorld(xMax, yMax, zMax);
            go.transform.position = (min + max) * 0.5f;
            go.transform.localScale = max - min;

            if (material != null)
            {
                go.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            return go;
        }

        // ========== СЛУЖЕБНОЕ ==========

        private static void EnsureMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Materials", "DuckHunt");
            }

            floorMaterial = EnsureMaterial("DH_Floor", new Color(0.55f, 0.55f, 0.58f));
            wallMaterial = EnsureMaterial("DH_Wall", new Color(0.38f, 0.38f, 0.42f));
            coverHighMaterial = EnsureMaterial("DH_CoverHigh", new Color(0.62f, 0.45f, 0.28f));
            coverLowMaterial = EnsureMaterial("DH_CoverLow", new Color(0.78f, 0.66f, 0.36f));
            platformMaterial = EnsureMaterial("DH_Platform", new Color(0.35f, 0.55f, 0.65f));
            iceMaterial = EnsureMaterial("DH_Ice", new Color(0.72f, 0.86f, 0.95f));
            trapMaterial = EnsureMaterial("DH_Trap", new Color(0.65f, 0.3f, 0.3f));
            buttonReadyMaterial = EnsureMaterial("DH_ButtonReady", new Color(0.9f, 0.2f, 0.2f));
            buttonBusyMaterial = EnsureMaterial("DH_ButtonBusy", new Color(0.3f, 0.3f, 0.3f));
        }

        private static Material EnsureMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// Пустая группа под этим именем. Старая сносится целиком, а не
        /// вычищается по частям: на группах висят ловушки со своими
        /// обязательными коллайдерами, и снять их поштучно нельзя — Unity не
        /// даёт удалить компонент, от которого зависит другой. Пересоздание
        /// заодно гарантирует, что после второй пересборки не остаётся ни
        /// одного объекта от первой.
        /// </summary>
        private static Transform ResetGroup(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void MarkSceneDirty()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        }
    }
}
