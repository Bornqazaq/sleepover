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
        // Паркур занимает треть этажа, а не двенадцать ШП. На коротком отрезке
        // при дальности прыжка в 6.5 ШП умещается два прыжка — это не паркур,
        // а две ступеньки. Удлинение — единственный способ сделать его дорогой,
        // которую действительно проходят, а не перешагивают.
        private const float ParkourEnd = 22f;
        private const float CoverStart = 22f;
        private const float TrapZoneStart = 28f;
        private const float StairRoomStart = 40f;

        private const float ButtonProgress = 29f;
        private const float ButtonProgressGeyserFloor5 = 31f;
        /// <summary>Смещение рычага от середины коридора в сторону открытой грани, ШП. Нажать — значит выйти на простреливаемое место.</summary>
        private const float ButtonOffsetFromPath = 4f;

        private const float DoorProgress = 40f;

        /// <summary>За сколько секунд створка проходит путь целиком.</summary>
        private const float DoorMoveDuration = 0.35f;

        // Рычаг размером с ладонь, а не с человека. Прежняя кнопка была столбом
        // с головой в полтора ШП: она читалась как часть планировки, загораживала
        // проход и стояла в стороне от того, что открывает. Заметность держится
        // не размером, а местом — рычаг врезан туда, куда игрок и так тянется.

        /// <summary>Ширина плиты рычага попёрек грани, ШП.</summary>
        private const float LeverPlateWidth = 0.35f;
        /// <summary>Высота плиты рычага, ШП.</summary>
        private const float LeverPlateHeight = 0.45f;
        /// <summary>На сколько плита выступает из грани, ШП.</summary>
        private const float LeverPlateDepth = 0.12f;
        /// <summary>Длина ручки, ШП.</summary>
        private const float LeverHandleLength = 0.5f;
        /// <summary>Толщина ручки, ШП.</summary>
        private const float LeverHandleThickness = 0.11f;
        /// <summary>Наклон ручки от перпендикуляра к плите, градусы. Вверх — готов, вниз — на перезарядке.</summary>
        private const float LeverTiltDegrees = 42f;
        /// <summary>Высота ручки над полом, на котором стоит нажимающий, ШП. Уровень ладони.</summary>
        private const float LeverHandHeight = 1.15f;
        /// <summary>Отступ рычага двери от края лаза, ШП.</summary>
        private const float LeverGateOffset = 0.55f;
        /// <summary>Высота тумбы под рычагом ловушки, ШП — по пояс.</summary>
        private const float LeverPedestalHeight = 1.1f;
        /// <summary>Сторона тумбы, ШП.</summary>
        private const float LeverPedestalSize = 0.9f;
        private const float CollapseStart = 34f;
        private const float CollapseEnd = 40f;
        private const float GeyserStart = 33f;
        private const float GeyserEnd = 38f;

        private const float DoorwayWidth = 4f;
        /// <summary>Минимальная длина паркурной площадки, ШП. Короче — приземление превращается в лотерею.</summary>
        private const float MinPlatformLength = 1f;
        private const float WallThickness = 0.5f;

        /// <summary>Доля высоких укрытий (спека, 3.9): высокое прячет стоящего, низкое — только присевшего.</summary>
        private const float HighCoverShare = 0.6f;
        /// <summary>Сколько раз переставляем укрытие, прежде чем признать место занятым.</summary>
        private const int CoverPlacementAttempts = 24;
        /// <summary>Зазор вокруг укрытия, ШП: и между соседями, и до линии видимости кнопки.</summary>
        private const float CoverClearance = 0.3f;
        /// <summary>Полос по глубине, по которым перебираются места для укрытий. Шаг полосы больше глубины укрытия с зазором.</summary>
        private const int CoverDepthLanes = 4;
        /// <summary>Высота глаз стоящего, ШП. От неё считается видимость зоны эффекта с кнопки.</summary>
        private const float EyeHeightWidths = 2.08f;

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
        private const float StairFlightBFarZ = 4f;
        /// <summary>Ширина марша, ШП.</summary>
        private const float StairFlightWidth = 3.5f;

        /// <summary>
        /// Запас проёма в перекрытии по обе стороны от марша, ШП.
        /// Раньше проём был ровно шириной марша, без зазора: стоило
        /// подниматься не по самой середине — и макушка упиралась в край
        /// перекрытия. Это и читалось как «потолок не даёт пройти».
        /// </summary>
        private const float StairHoleMargin = 1f;

        /// <summary>
        /// Высота порога лаза в лестничную комнату, ШП. Прыжок берёт
        /// 2.27 ШП, поэтому три ШП с пола не достать никак: на порог надо
        /// забираться по блокам. В этом и смысл — пробежать к лестнице нельзя.
        /// </summary>
        private const float GateSillWidths = 3f;
        /// <summary>Высота самого лаза, ШП. Персонаж 2.29 ШП — проходить надо не приседая.</summary>
        private const float GateHeightWidths = 2.6f;
        /// <summary>Где начинается подъём к лазу, ШП трассы.</summary>
        private const float GateApproachStart = 32.5f;
        /// <summary>Сторона блока подъёма, ШП. Тело 0.72 ШП в поперечнике — приземляться есть куда.</summary>
        private const float GateBlockSize = 1.5f;
        /// <summary>Общий конфиг персонажа — оттуда берётся настоящий порог всхождения.</summary>
        private const string CharacterConfigPath = "Assets/_Project/Settings/Gameplay/CharacterConfig.asset";
        /// <summary>
        /// Глубина площадки перед первой ступенью, ШП. Без неё на марш
        /// можно зайти только сбоку, с длинного помоста вдоль лестницы, а сбоку
        /// каждая следующая ступень выше помоста на свою высоту плюс всё
        /// набранное до неё: на первую 0.26 м, на вторую уже 0.51, на третью 0.77 —
        /// то есть прыжок. Площадка даёт заход в лоб, с одной ступеньки.
        /// </summary>
        private const float StairLandingRunWidths = 2f;
        /// <summary>
        /// Высота первого блока подъёма, ШП. Заход с пола коридора обязан
        /// быть обычным шагом: прыжок на первой же ступени читается как баг,
        /// а не как паркур. Держим ниже порога всхождения с запасом.
        /// </summary>
        private const float GateFirstStepWidths = 0.5f;

        /// <summary>Сторона паркурного блока, ШП.</summary>
        // ========== ПРОПАСТИ ==========

        /// <summary>
        /// Сколько пропастей режется в перекрытии этажа сверх сквозных.
        /// Пол этажа — не площадка, а решето: упасть можно почти везде,
        /// и дорога через этаж выбирается, а не пробегается.
        /// </summary>
        private const int FloorHoleCount = 6;

        /// <summary>
        /// Сквозные пропасти — те, что стоят на одной и той же мировой отметке
        /// на всех этажах. Провалившись в такую, летишь не на этаж ниже, а до
        /// самого низа башни. Треть от общего числа: если такими сделать все,
        /// этажи перестанут отличаться друг от друга.
        /// </summary>
        private const int ThroughHoleCount = 3;

        private const float HoleMinLength = 2.5f;
        private const float HoleMaxLength = 5.5f;
        private const float HoleMinWidth = 3f;
        private const float HoleMaxWidth = 6f;

        /// <summary>Куда пропасти не лезут: торцы этажа, стены, соседние дыры. ШП.</summary>
        private const float HoleClearance = 1.5f;

        /// <summary>Где режутся случайные пропасти: зона укрытий и ловушек, ШП по трассе.</summary>
        private const float HoleZoneStart = 24f;
        private const float HoleZoneEnd = 32f;

        /// <summary>
        /// Полоса, где сквозные пропасти безопасно ложатся на любом этаже.
        /// Считается в мировом X, а не в прогрессе: змейка разворачивает трассу
        /// через этаж, и «одно и то же место» — это одно и то же X, иначе
        /// пропасти разъедутся зеркально и сквозного колодца не выйдет.
        /// Границы держат её подальше от обоих торцов, где на каждом втором
        /// этаже стоит лестничная комната или вход.
        /// </summary>
        private const float ThroughHoleXMin = 12f;
        private const float ThroughHoleXMax = 36f;

        /// <summary>
        /// Глубина ямы под паркуром первого этажа, ШП. На остальных этажах там
        /// сквозная дыра, а на первом ронять некуда — под ним конец башни.
        /// </summary>
        private const float PitDepthWidths = 2.5f;

        private const float ParkourBlockSize = 1.1f;
        /// <summary>Толщина паркурного блока, ШП.</summary>
        private const float ParkourBlockThickness = 0.6f;
        /// <summary>На сколько ШП соседние блоки расходятся по высоте. Прыжок берёт 2.27 — держим втрое меньше.</summary>
        private const float ParkourHeightStep = 0.7f;
        /// <summary>
        /// Если низ блока оказывается ближе этого к полу, ШП, — сажаем блок
        /// на пол. Висящий в восьми сантиметрах над полом блок читается как
        /// щель и как небрежность, а кромка ловит ногу на бегу.
        /// </summary>
        private const float ParkourFloorSnapWidths = 0.3f;
        /// <summary>Прогресс стартовой линии, ШП. Все Утки стоят на ней в ряд попёрек этажа.</summary>
        private const float SpawnProgress = 2f;
        /// <summary>Отступ крайних точек спавна от стен, ШП.</summary>
        private const float SpawnEdgeMargin = 1.5f;

        /// <summary>
        /// Длина стартового щита вдоль трассы, ШП.
        ///
        /// Короткого щита у самого старта не хватает: Охотник стоит посередине
        /// длины этажа и смотрит на старт наискосок — его луч пересекает открытую
        /// грань не у стартовой линии, а дальше по коридору, и тем дальше, чем
        /// глубже стоит Утка. Длина подобрана так, чтобы закрыть все восемь точек,
        /// и проверяется лучами в <see cref="ValidateSpawnShield"/>.
        /// </summary>
        private const float SpawnShieldLength = 13f;
        /// <summary>Толщина стартового щита, ШП.</summary>
        private const float SpawnShieldThickness = 0.6f;
        /// <summary>Высота стартового щита, ШП. Выше стоящей Утки, но Охотник перестреливает его, поднявшись на лифте.</summary>
        private const float SpawnShieldHeight = 3f;

        /// <summary>Дальность прыжка на бегу, ШП. Считано по CharacterConfig: 6.5 м/с и 0.72 с в воздухе.</summary>
        private const float JumpRangeWidths = 6.5f;
        /// <summary>Какую долю дальности прыжка разрешаем тратить. Остаток — запас на неточный разбег.</summary>
        private const float JumpSafetyShare = 0.8f;
        /// <summary>Толщина видимого ледяного настила, ШП. Чисто декоративный лист поверх перекрытия.</summary>
        private const float IceTileWidths = 0.05f;



        /// <summary>
        /// На сколько ШП лестница отодвинута вглубь комнаты от её порога.
        /// Сам проём двери Охотник простреливает по касательной примерно на
        /// полтора ШП внутрь — и это нормально: у входа стоять опасно. Но сама
        /// лестница обязана начинаться уже за этой чертой.
        /// </summary>
        private const float StairStartOffset = 2f;
        /// <summary>
        /// Полоса, по которой идёт паркур: ближе к открытой грани, то есть на
        /// виду у Охотника. Прижимать её к самому краю нельзя — площадка
        /// высотой до 2 ШП встаёт на пути настильного выстрела вдоль этажа.
        /// </summary>
        // Паркур раскинут почти на весь коридор, а не на узкую полосу: под ним
        // теперь пропасть во всю глубину, и прыжок вбок — такая же часть дороги,
        // как прыжок вперёд. Узкая полоса позволяла пробежать по краю.
        private const float ParkourNearZ = 1.5f;
        private const float ParkourWidth = 11f;

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

        /// <summary>
        /// Число разрывов урезано против таблицы 3.9, а их размер сохранён:
        /// 4 разрыва по 5 ШП это 20 ШП дыр в секции длиной 12. Решение
        /// геймдизайнера от 19.08 — сложность паркура читается по длине прыжка,
        /// а не по количеству дыр, а ужатый до 1.75 ШП разрыв перешагивается.
        /// </summary>
        private static readonly FloorPlan[] Plans =
        {
            new FloorPlan(12, 6f, 2, 2f, false),   // 1 — ферма, намеренно лёгкий
            new FloorPlan(10, 8f, 2, 3f, false),   // 2 — ферма, Дверь
            new FloorPlan(8, 10f, 2, 3f, true),    // 3 — зима, Гейзер
            new FloorPlan(6, 13f, 1, 4f, false),   // 4 — ферма, Провал
            new FloorPlan(5, 16f, 1, 5f, true)     // 5 — зима, Дверь + Гейзер
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

        /// <summary>Прямоугольная дыра в перекрытии: координаты арены, X вдоль трассы, Z поперёк.</summary>
        private readonly struct Hole
        {
            public readonly float XMin;
            public readonly float XMax;
            public readonly float ZMin;
            public readonly float ZMax;

            public Hole(float xMin, float xMax, float zMin, float zMax)
            {
                XMin = Mathf.Min(xMin, xMax);
                XMax = Mathf.Max(xMin, xMax);
                ZMin = Mathf.Min(zMin, zMax);
                ZMax = Mathf.Max(zMin, zMax);
            }

            public bool Overlaps(float xMin, float xMax, float zMin, float zMax, float pad) =>
                xMax > XMin - pad && xMin < XMax + pad && zMax > ZMin - pad && zMin < ZMax + pad;

            public bool CoversColumn(float x) => x > XMin && x < XMax;
        }

        /// <summary>Сквозные пропасти — одни и те же на всех этажах.</summary>
        private static readonly List<Hole> throughHoles = new List<Hole>(4);

        /// <summary>Дыры текущего этажа: сквозные, случайные, паркурная пропасть и проём лестницы.</summary>
        private static readonly List<Hole> slabHoles = new List<Hole>(24);

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
            BuildThroughHoles();
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

            if (floor == 0)
            {
                BuildSpawnShield(root, baseY);
            }

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

            // Провал стоит на четвёртом этаже: кусок пола перед выходной дверью.
            bool hasCollapse = floor == 3;
            float collapseMin = 0f;
            float collapseMax = 0f;
            if (hasCollapse)
            {
                GetXRange(floor, CollapseStart, CollapseEnd, out collapseMin, out collapseMax);
            }

            CollectSlabHoles(floor);

            // Перекрытие режется полосами по X, а внутри полосы — по Z.
            // Прямоугольные вырезы иначе не собрать: дыр много, они не выстроены
            // в линию, и каждая обязана оставить вокруг себя целый пол.
            var edges = new List<float> { 0f, length };
            for (int i = 0; i < slabHoles.Count; i++)
            {
                edges.Add(Mathf.Clamp(slabHoles[i].XMin, 0f, length));
                edges.Add(Mathf.Clamp(slabHoles[i].XMax, 0f, length));
            }

            if (hasCollapse)
            {
                edges.Add(collapseMin);
                edges.Add(collapseMax);
            }

            edges.Sort();

            var gaps = new List<Vector2>(8);
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

                gaps.Clear();
                for (int h = 0; h < slabHoles.Count; h++)
                {
                    if (slabHoles[h].CoversColumn(middle))
                    {
                        gaps.Add(new Vector2(slabHoles[h].ZMin, slabHoles[h].ZMax));
                    }
                }

                MergeRanges(gaps);

                float cursor = 0f;
                for (int gIndex = 0; gIndex < gaps.Count; gIndex++)
                {
                    float gapMin = Mathf.Clamp(gaps[gIndex].x, 0f, depth);
                    float gapMax = Mathf.Clamp(gaps[gIndex].y, 0f, depth);
                    if (gapMin - cursor > 0.001f)
                    {
                        Box(slab, $"Slab_{++piece}", groundLayer, floorMaterial, from, to, bottom, top, cursor, gapMin);
                    }

                    cursor = Mathf.Max(cursor, gapMax);
                }

                if (depth - cursor > 0.001f)
                {
                    Box(slab, $"Slab_{++piece}", groundLayer, floorMaterial, from, to, bottom, top, cursor, depth);
                }
            }

            if (hasCollapse)
            {
                BuildCollapseTrap(root, floor, collapseMin, collapseMax, bottom, top, depth);
            }

            if (floor == 0)
            {
                BuildParkourPit(root, floor, baseY);
            }
        }

        /// <summary>
        /// Собрать все дыры этажа: проём лестницы, пропасть под паркуром,
        /// сквозные колодцы и случайные провалы.
        ///
        /// Первый этаж — единственный без пропастей: под ним конца башни нет,
        /// и провалившийся улетал бы из локации, а не на этаж ниже. Паркурная
        /// пропасть там всё равно режется, но под ней строится дно (см.
        /// <see cref="BuildParkourPit"/>) — упасть можно, вылететь из уровня нельзя.
        /// </summary>
        private static void CollectSlabHoles(int floor)
        {
            slabHoles.Clear();

            // Проём над лестницей этажа ниже — без него на этаж не подняться.
            if (floor > 0)
            {
                GetStairHoleRange(floor - 1, out float stairXMin, out float stairXMax);
                slabHoles.Add(new Hole(stairXMin, stairXMax, StairHoleZMin, StairHoleZMax));
            }

            // Пропасть под паркуром во всю глубину коридора. Она и делает
            // паркур единственной дорогой: обойти её по краю нельзя, потому
            // что края у неё нет.
            GetParkourSpan(floor, out float parkourXMin, out float parkourXMax);
            slabHoles.Add(new Hole(parkourXMin, parkourXMax, 0f, DepthWidths));

            if (floor == 0)
            {
                return;
            }

            // Сквозные — одни и те же на всех этажах, поэтому летишь до низа.
            for (int i = 0; i < throughHoles.Count; i++)
            {
                slabHoles.Add(throughHoles[i]);
            }

            // Случайные — свои на каждом этаже.
            int placed = 0;
            for (int attempt = 0; attempt < FloorHoleCount * 12 && placed < FloorHoleCount; attempt++)
            {
                float length = Mathf.Lerp(HoleMinLength, HoleMaxLength, (float)random.NextDouble());
                float width = Mathf.Lerp(HoleMinWidth, HoleMaxWidth, (float)random.NextDouble());

                float progress = Mathf.Lerp(HoleZoneStart, HoleZoneEnd - length, (float)random.NextDouble());
                GetXRange(floor, progress, progress + length, out float xMin, out float xMax);
                float zMin = Mathf.Lerp(HoleClearance, DepthWidths - HoleClearance - width, (float)random.NextDouble());
                float zMax = zMin + width;

                if (IsHoleBlocked(xMin, xMax, zMin, zMax))
                {
                    continue;
                }

                slabHoles.Add(new Hole(xMin, xMax, zMin, zMax));
                placed++;
            }
        }

        /// <summary>
        /// Сквозные пропасти. Раскладываются один раз на всю башню и в мировом
        /// X: змейка разворачивает трассу через этаж, и место, заданное
        /// прогрессом, на соседнем этаже оказалось бы зеркальным — колодца
        /// не получилось бы.
        /// </summary>
        private static void BuildThroughHoles()
        {
            throughHoles.Clear();

            for (int attempt = 0; attempt < ThroughHoleCount * 20 && throughHoles.Count < ThroughHoleCount; attempt++)
            {
                float length = Mathf.Lerp(HoleMinLength, HoleMaxLength, (float)random.NextDouble());
                float width = Mathf.Lerp(HoleMinWidth, HoleMaxWidth, (float)random.NextDouble());

                float xMin = Mathf.Lerp(ThroughHoleXMin, ThroughHoleXMax - length, (float)random.NextDouble());
                float zMin = Mathf.Lerp(HoleClearance, DepthWidths - HoleClearance - width, (float)random.NextDouble());

                var hole = new Hole(xMin, xMin + length, zMin, zMin + width);

                bool clash = false;
                for (int i = 0; i < throughHoles.Count; i++)
                {
                    if (throughHoles[i].Overlaps(hole.XMin, hole.XMax, hole.ZMin, hole.ZMax, HoleClearance))
                    {
                        clash = true;
                        break;
                    }
                }

                if (!clash)
                {
                    throughHoles.Add(hole);
                }
            }
        }

        /// <summary>Пропасть нельзя ставить: она задевает уже занятое место или другую дыру.</summary>
        private static bool IsHoleBlocked(float xMin, float xMax, float zMin, float zMax)
        {
            for (int i = 0; i < slabHoles.Count; i++)
            {
                if (slabHoles[i].Overlaps(xMin, xMax, zMin, zMax, HoleClearance))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Слить пересекающиеся отрезки в один — иначе полоса пола резалась бы на куски внахлёст.</summary>
        private static void MergeRanges(List<Vector2> ranges)
        {
            if (ranges.Count < 2)
            {
                return;
            }

            ranges.Sort((a, b) => a.x.CompareTo(b.x));
            for (int i = ranges.Count - 1; i > 0; i--)
            {
                if (ranges[i].x <= ranges[i - 1].y)
                {
                    ranges[i - 1] = new Vector2(ranges[i - 1].x, Mathf.Max(ranges[i - 1].y, ranges[i].y));
                    ranges.RemoveAt(i);
                }
            }
        }

        /// <summary>Где по X лежит паркурная пропасть этажа.</summary>
        private static void GetParkourSpan(int floor, out float xMin, out float xMax)
        {
            float start = ParkourStart;
            if (floor > 0)
            {
                GetStairHoleRange(floor - 1, out float holeXMin, out float holeXMax);
                start = Mathf.Max(start, Mathf.Max(GetProgress(floor, holeXMin), GetProgress(floor, holeXMax)));
            }

            GetXRange(floor, start, ParkourEnd, out xMin, out xMax);
        }

        /// <summary>
        /// Дно ямы под паркуром первого этажа. Ниже него башня кончается,
        /// поэтому сквозной дыры здесь быть не может: сорвавшийся должен
        /// потерять время и лезть заново, а не вылететь из уровня.
        /// Выбраться помогают две ступени у дальнего края.
        /// </summary>
        private static void BuildParkourPit(Transform root, int floor, float baseY)
        {
            Transform group = ResetGroup(root, "ParkourPit");
            GetParkourSpan(floor, out float xMin, out float xMax);

            float floorTop = baseY - PitDepthWidths;
            float floorBottom = floorTop - SlabWidths;

            Box(group, "PitFloor", groundLayer, floorMaterial, xMin, xMax, floorBottom, floorTop, 0f, DepthWidths);
            Box(group, "PitWall_Near", groundLayer, wallMaterial,
                xMin - WallThickness, xMin, floorBottom, baseY, 0f, DepthWidths);
            Box(group, "PitWall_Far", groundLayer, wallMaterial,
                xMax, xMax + WallThickness, floorBottom, baseY, 0f, DepthWidths);

            // Лесенка наружу — у той стороны, куда игрок и шёл.
            bool forward = IsForward(floor);
            float stepDepth = 1.2f;
            for (int i = 0; i < 2; i++)
            {
                float top = floorTop + (i + 1) * (PitDepthWidths / 3f);
                float from = forward ? xMax - (i + 1) * stepDepth : xMin + i * stepDepth;
                float to = from + stepDepth;
                Box(group, $"PitStep_{i + 1}", groundLayer, platformMaterial,
                    from, to, floorTop, top, 0f, DepthWidths);
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

            // Стена со стороны лифта — та самая, из-за которой комната не простреливается.
            Box(room, "Wall_TowardElevator", groundLayer, wallMaterial,
                xMin, xMax, baseY, baseY + ceiling, -WallThickness, 0f);

            // Перегородка от коридора. Прохода по полу в ней нет намеренно:
            // лаз поднят на три ШП, а прыжок берёт 2.27 — попасть внутрь
            // можно только по блокам подъёма. Паркур перестаёт быть
            // украшением и становится единственной дорогой на этаж выше.
            float doorX = GetX(floor, DoorProgress);
            float partitionMin = Mathf.Min(doorX, doorX + (IsForward(floor) ? -WallThickness : WallThickness));
            float partitionMax = partitionMin + WallThickness;

            // Лаз — на полосе первого марша, у глухой стены. У открытой
            // грани Охотник простреливал бы его по касательной прямо из шахты.
            float gateMin = StairFlightANearZ;
            float gateMax = gateMin + DoorwayWidth;
            float gateBottom = baseY + GateSillWidths;
            float gateTop = gateBottom + GateHeightWidths;

            // Низ перегородки — три куска, а не один: под лазом оставлена ниша
            // ровно по створке. Раньше открытая створка уезжала вниз внутрь
            // сплошной стены, и две стены стояли одна в другой.
            float slotTop = gateBottom;
            float slotBottom = gateBottom - GateHeightWidths;

            Box(room, "Partition_Below_Near", groundLayer, wallMaterial,
                partitionMin, partitionMax, baseY, slotTop, 0f, gateMin);
            Box(room, "Partition_Below_Far", groundLayer, wallMaterial,
                partitionMin, partitionMax, baseY, slotTop, gateMax, DepthWidths);
            Box(room, "Partition_Below_Sill", groundLayer, wallMaterial,
                partitionMin, partitionMax, baseY, slotBottom, gateMin, gateMax);
            Box(room, "Partition_Above", groundLayer, wallMaterial,
                partitionMin, partitionMax, gateTop, baseY + ceiling, 0f, DepthWidths);
            Box(room, "Partition_SideA", groundLayer, wallMaterial,
                partitionMin, partitionMax, gateBottom, gateTop, 0f, gateMin);
            Box(room, "Partition_SideB", groundLayer, wallMaterial,
                partitionMin, partitionMax, gateBottom, gateTop, gateMax, DepthWidths);

            BuildGateApproach(root, floor, baseY, gateMin, gateMax);
            BuildStairPlatforms(room, floor, baseY);

            DoorTrap door = BuildDoorTrap(root, floor, partitionMin, partitionMax, gateBottom, gateTop, gateMin, gateMax);
            BuildDoorLevers(root, floor, door, partitionMin, partitionMax, gateBottom, gateMin);
        }

        /// <summary>
        /// Подъём к лазу — три блока перед перегородкой, каждый на ШП
        /// выше предыдущего. Это обязательный путь, единственный на этаж,
        /// поэтому прыжки здесь заведомо лёгкие: ШП вверх при прыжке в 2.27
        /// и полтора ШП попёрек при дальности 6.5. Сложность здесь не в том,
        /// чтобы не упасть, а в том, что всё это происходит в зоне ловушек,
        /// под выстрелом и медленнее, чем бегом.
        ///
        /// Блоки — столбы от пола, а не парящие плиты: на четвёртом этаже
        /// под ними провал, и после его срабатывания дорога к лазу обязана
        /// остаться проходимой — иначе ловушка не задерживает, а запирает этаж.
        /// </summary>
        private static void BuildGateApproach(Transform root, int floor, float baseY, float gateMin, float gateMax)
        {
            Transform group = ResetGroup(root, "GateApproach");

            // Коридор кончается не на DoorProgress, а на толщину стены раньше:
            // перегородка занимает p 39.5…40, и DoorProgress — это её внутренняя
            // грань, уже в лестничной комнате. Строить блоки до неё значит
            // загнать последний на полметра в толщу стены.
            float wallFace = DoorProgress - WallThickness;
            float span = wallFace - GateApproachStart;
            float gap = (span - 3f * GateBlockSize) * 0.5f;

            // Первый блок — обычный шаг с пола, оставшиеся два — прыжки
            // поровну. Прыжок на первой же ступени читается как баг, не как паркур.
            float jumpRise = (GateSillWidths - GateFirstStepWidths) * 0.5f;
            var heights = new[]
            {
                GateFirstStepWidths,
                GateFirstStepWidths + jumpRise,
                GateSillWidths
            };

            // Сдвиг попёрек держим внутри полосы лаза: слева от неё лежит
            // площадка гейзера. Залезать на неё нельзя — будет врезка,
            // отступать тоже нельзя — будет щель. Ставим вплотную к границе полосы.
            var lateral = new[] { 1.8f, 0f, 0f };

            for (int i = 0; i < heights.Length; i++)
            {
                float p = GateApproachStart + i * (GateBlockSize + gap);

                // Последний блок лежит точно под лазом и во всю его ширину:
                // с него шаг внутрь, а не прыжок в проём.
                bool last = i == heights.Length - 1;
                float zSize = last ? gateMax - gateMin : GateBlockSize;
                float zMin = Mathf.Clamp(gateMin + lateral[i], gateMin, gateMax - zSize);

                GetXRange(floor, p, p + GateBlockSize, out float xMin, out float xMax);
                Box(group, $"GateBlock_{i + 1}", groundLayer, platformMaterial,
                    xMin, xMax, baseY, baseY + heights[i], zMin, zMin + zSize);
            }

            WarnIfTooHighToStep(floor, config.ToUnits(GateFirstStepWidths), "первый блок подъёма");
        }

        /// <summary>
        /// Ругнуться, если подъём выше того, на что персонаж всходит шагом.
        ///
        /// Порог читается из общего CharacterConfig, а не держится копией
        /// константы: копия разъезжается с настройкой молча, и проверка
        /// начинает подтверждать то, чего нет. Заодно сравнение двух констант
        /// компилятор считал недостижимым кодом и ругался на каждой сборке.
        /// </summary>
        private static void WarnIfTooHighToStep(int floor, float riseUnits, string what)
        {
            float stepHeight = ResolveStepHeightUnits();
            if (stepHeight <= 0f || riseUnits <= stepHeight)
            {
                return;
            }

            warnings.Add($"этаж {floor + 1}: {what} {riseUnits:F2} м выше порога всхождения {stepHeight:F2} м — придётся прыгать");
        }

        /// <summary>Порог всхождения в метрах из CharacterConfig. Ноль — конфиг не найден, проверка пропускается.</summary>
        private static float ResolveStepHeightUnits()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(CharacterConfigPath);
            if (asset == null)
            {
                return 0f;
            }

            SerializedProperty property = new SerializedObject(asset).FindProperty("stepHeight");
            return property != null ? property.floatValue : 0f;
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

            // Один марш, а не два. Двумаршевая лестница в этой комнате
            // невозможна в принципе: подняться надо на 8 ШП, а свободной
            // высоты под перекрытием меньше того. Поэтому весь подъём
            // убран в полосу под проёмом перекрытия.
            //
            // Помостов два, и это важно:
            //   — длинный идёт от лаза вдоль всей комнаты до дальнего конца;
            //   — поворотный лежит в полосе марша, прямо перед первой ступенью.
            // Без второго на лестницу можно было зайти только сбоку, а сбоку
            // каждая следующая ступень выше помоста на всю набранную до неё
            // высоту: на первую 0.26 м, на вторую 0.51, на третью 0.77 — прыжок.
            const int StepCount = 14;

            float pStart = StairRoomStart;
            float pEnd = config.FloorLengthWidths;
            float pFlightEnd = pEnd - StairLandingRunWidths;

            float platformHeight = GateSillWidths;
            float flightZMin = StairFlightBFarZ;
            float flightZMax = StairFlightBFarZ + StairFlightWidth;

            var steps = new List<Transform>(StepCount + 2);

            // Длинный помост: от лаза до дальней стены, от края марша
            // до глухой стены. По нему идут от входа к повороту.
            GetXRange(floor, pStart, pEnd, out float platFrom, out float platTo);
            steps.Add(Box(stairs, "Landing", groundLayer, platformMaterial,
                platFrom, platTo, baseY, baseY + platformHeight, flightZMax, DepthWidths).transform);

            // Поворотная площадка перед первой ступенью, заподлицо с длинным
            // помостом и в полосе марша: развернулся — и пошёл вверх в лоб.
            GetXRange(floor, pFlightEnd, pEnd, out float turnFrom, out float turnTo);
            steps.Add(Box(stairs, "LandingTurn", groundLayer, platformMaterial,
                turnFrom, turnTo, baseY, baseY + platformHeight, flightZMin, flightZMax).transform);

            // Марш целиком лежит в полосе проёма: над ним нет перекрытия,
            // и макушке упираться не во что.
            float run = (pFlightEnd - pStart) / StepCount;
            float rise = (config.FloorStepWidths - platformHeight) / StepCount;

            for (int i = 0; i < StepCount; i++)
            {
                GetXRange(floor, pFlightEnd - run * (i + 1), pFlightEnd - run * i, out float from, out float to);
                float top = baseY + platformHeight + rise * (i + 1);
                steps.Add(Box(stairs, $"Step_{i + 1:00}", groundLayer, platformMaterial,
                    from, to, baseY, top, flightZMin, flightZMax).transform);
            }

            WarnIfTooHighToStep(floor, config.ToUnits(rise), "ступень марша");

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

            // Секция начинается за проёмом лестницы снизу: он лежит во входной
            // зоне этажа и заезжает в паркур. Над проёмом строить нельзя —
            // блок становится потолком поднимающемуся.
            float start = ParkourStart;
            if (floor > 0)
            {
                GetStairHoleRange(floor - 1, out float holeXMin, out float holeXMax);
                start = Mathf.Max(start, Mathf.Max(GetProgress(floor, holeXMin), GetProgress(floor, holeXMax)));
            }

            // Блоки, а не длинные помосты: помост в несколько ШП пробегается
            // насквозь и читается как ступенька.
            //
            // Разрывы идут ритмом «длинный — короткий — средний», а не одинаковые:
            // ровная цепочка проходится одним заученным ритмом. Самый длинный
            // разрыв берётся из таблицы 3.9 — это и есть прогрессия сложности по этажам.
            // Ритм считается от предельного прыжка этажа, но большинство
            // разрывов средние: цепочка из восьми прыжков подряд тяжелее двух
            // предельных, и ошибиться в ней можно на любом. Длинный разрыв
            // идёт четвёртым — там, где игрок уже поймал ритм и расслабился.
            float longGap = Mathf.Min(plan.MaxGap, JumpRangeWidths * JumpSafetyShare - ParkourBlockSize);
            float[] gapPattern =
            {
                longGap * 0.55f,
                longGap * 0.75f,
                longGap * 0.45f,
                longGap,
                longGap * 0.6f
            };

            float cursor = start;
            float height = 1f;
            float previousZ = ParkourNearZ + (ParkourWidth - ParkourBlockSize) * 0.5f;
            int index = 0;
            float lastLanded = start;

            while (cursor + ParkourBlockSize <= ParkourEnd + 0.01f)
            {
                float gap = gapPattern[index % gapPattern.Length];

                // Высота гуляет вверх-вниз: ровная лесенка проходится не глядя,
                // а перепад заставляет целиться.
                height = Mathf.Clamp(
                    height + (float)(random.NextDouble() * 2f - 1f) * ParkourHeightStep * 1.6f,
                    0.6f, 2.6f);

                // Сдвиг попёрек — вторая ось прицеливания. Но он складывается
                // с разрывом по теореме Пифагора, и на длинных разрывах его надо
                // урезать, иначе суммарный прыжок выходит за дальность.
                float budget = JumpRangeWidths * JumpSafetyShare;
                float lateral = Mathf.Sqrt(Mathf.Max(0f, budget * budget - gap * gap));
                float bandMin = Mathf.Max(ParkourNearZ, previousZ - lateral);
                float bandMax = Mathf.Min(ParkourNearZ + ParkourWidth - ParkourBlockSize, previousZ + lateral);
                float z = Mathf.Lerp(bandMin, bandMax, (float)random.NextDouble());

                GetXRange(floor, cursor, cursor + ParkourBlockSize, out float xMin, out float xMax);
                cursor += ParkourBlockSize + gap;
                index++;

                if (OverlapsStairwell(floor, xMin, xMax, z, z + ParkourBlockSize))
                {
                    continue;
                }

                // Блок, чей низ почти касается пола, ставим на пол целиком:
                // восемь сантиметров пустоты под ним — это щель и кромка под ногу.
                float bottom = height - ParkourBlockThickness <= ParkourFloorSnapWidths
                    ? baseY
                    : baseY + height - ParkourBlockThickness;

                Box(group, $"Block_{index:00}", groundLayer, platformMaterial,
                    xMin, xMax, bottom, baseY + height, z, z + ParkourBlockSize);
                previousZ = z;
                lastLanded = cursor - gap;
            }

            // Последний блок вплотную к дальнему краю пропасти. Без него цепочка
            // обрывается там, где кончился ритм разрывов, и остаток до твёрдого
            // пола может оказаться длиннее прыжка — паркур станет непроходимым,
            // а не сложным.
            float tail = ParkourEnd - lastLanded;
            if (tail > JumpRangeWidths * JumpSafetyShare)
            {
                GetXRange(floor, ParkourEnd - ParkourBlockSize, ParkourEnd, out float tailXMin, out float tailXMax);
                float tailZ = Mathf.Clamp(previousZ, ParkourNearZ, ParkourNearZ + ParkourWidth - ParkourBlockSize);
                Box(group, "Block_Tail", groundLayer, platformMaterial,
                    tailXMin, tailXMax, baseY, baseY + 1f, tailZ, tailZ + ParkourBlockSize);
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

            // Укрытия кончаются там, где начинается плановый открытый пробег.
            // «Самый длинный открытый участок» из таблицы 3.9 — параметр
            // прогрессии, и получаться он обязан намеренно. Раньше он выходил
            // побочным эффектом: укрытие, попавшее в зону ловушек или в проём
            // лестницы, просто выбрасывалось — этажи теряли до трети укрытий,
            // а голый хвост выходил одинаковым на всех пяти.
            float from = CoverStart;
            float to = Mathf.Max(from + 2f, StairRoomStart - plan.LongestOpen);
            float step = (to - from) / plan.Covers;
            int highCount = Mathf.RoundToInt(plan.Covers * HighCoverShare);

            List<Vector4> sightLines = GetTrapSightLines(floor);
            var placed = new List<Vector4>(plan.Covers);

            for (int i = 0; i < plan.Covers; i++)
            {
                // Высокие раскладываем равномерно по цепочке, а не броском
                // монеты: на пяти укрытиях монета легко оставляет этаж вообще
                // без высоких, и стоящему игроку прятаться негде.
                bool high = i * highCount / plan.Covers != (i + 1) * highCount / plan.Covers;
                float height = high ? HighCoverWidths : LowCoverWidths;
                bool done = false;

                for (int attempt = 0; attempt < CoverPlacementAttempts && !done; attempt++)
                {
                    // С каждой попыткой укрытие уже и переходит на следующую
                    // полосу глубины. Чистый случайный перебор в узкой полосе
                    // поздних этажей выдыхается раньше, чем находит щель, а
                    // терять укрытие нельзя — их число и есть прогрессия.
                    float squeeze = attempt / (float)CoverPlacementAttempts;
                    float width = Mathf.Lerp(2.5f, 1.5f, squeeze);
                    float lane = attempt % CoverDepthLanes / (float)(CoverDepthLanes - 1);

                    // Сначала укрытие ищет место в своей доле полосы — так они
                    // раскладываются ровно. Если доля забита, отпускаем его
                    // гулять по всей полосе: ровность важна, наличие важнее.
                    float p = squeeze < 0.5f
                        ? from + step * (i + (float)random.NextDouble())
                        : Mathf.Lerp(from, to - width, (float)random.NextDouble());
                    p = Mathf.Clamp(p, from, to - width);
                    float z = attempt == 0
                        ? Mathf.Lerp(1.5f, DepthWidths - 3.5f, (float)random.NextDouble())
                        : Mathf.Lerp(1.5f, DepthWidths - 3.5f, lane);

                    // Линию от кнопки до зоны эффекта не загораживаем: спека
                    // (5.1) требует прямой видимости, иначе нажатие — лотерея.
                    if (BlocksSightLine(sightLines, p, p + width, z, z + 2f))
                    {
                        continue;
                    }

                    GetXRange(floor, p, p + width, out float xMin, out float xMax);
                    if (OverlapsStairwell(floor, xMin, xMax, z, z + 2f))
                    {
                        continue;
                    }

                    // Слипшиеся укрытия читаются как одна стена и съедают
                    // проходимость этажа — держим их врозь.
                    if (OverlapsPlaced(placed, p, p + width, z, z + 2f))
                    {
                        continue;
                    }

                    Box(group, $"Cover_{i + 1:00}_{(high ? "High" : "Low")}", coverLayer,
                        high ? coverHighMaterial : coverLowMaterial,
                        xMin, xMax, baseY, baseY + height, z, z + 2f);

                    placed.Add(new Vector4(p, p + width, z, z + 2f));
                    done = true;
                }

                if (!done)
                {
                    warnings.Add(
                        $"этаж {floor + 1}: укрытие {i + 1} из {plan.Covers} поставить некуда — " +
                        $"полоса p {from:F0}…{to:F0} занята");
                }
            }
        }

        /// <summary>
        /// Линии «рычаг → зона эффекта» этажа в координатах трассы (p, z).
        /// Заданы числами, а не построенными объектами: укрытия строятся
        /// раньше ловушек, а расступиться перед линией должны именно они.
        ///
        /// Двери в списке нет намеренно. Её рычаги врезаны в перегородку по обе
        /// стороны лаза: нажимающий стоит вплотную к зоне эффекта и смотрит
        /// прямо в неё, проверять там нечего.
        /// </summary>
        private static List<Vector4> GetTrapSightLines(int floor)
        {
            var lines = new List<Vector4>(2);

            // Линия идёт из середины тумбы, а не из её угла: иначе проверочный
            // луч стартует внутри самого рычага и упирается в него.
            float buttonZ = PathDepth - ButtonOffsetFromPath + 0.5f;
            float geyserZ = (DepthWidths - 5f) * 0.5f + 2.5f;
            float geyserP = (GeyserStart + GeyserEnd) * 0.5f;

            switch (floor)
            {
                case 2:
                    lines.Add(new Vector4(ButtonProgress + 0.5f, buttonZ, geyserP, geyserZ));
                    break;
                case 3:
                    lines.Add(new Vector4(ButtonProgress + 0.5f, buttonZ, (CollapseStart + CollapseEnd) * 0.5f, DepthWidths * 0.5f));
                    break;
                case 4:
                    lines.Add(new Vector4(ButtonProgressGeyserFloor5 + 0.5f, buttonZ, geyserP, geyserZ));
                    break;
            }

            return lines;
        }

        private static bool BlocksSightLine(List<Vector4> lines, float pMin, float pMax, float zMin, float zMax)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                Vector4 line = lines[i];
                if (SegmentIntersectsRect(
                        new Vector2(line.x, line.y), new Vector2(line.z, line.w),
                        pMin - CoverClearance, pMax + CoverClearance,
                        zMin - CoverClearance, zMax + CoverClearance))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool OverlapsPlaced(List<Vector4> placed, float pMin, float pMax, float zMin, float zMax)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                Vector4 other = placed[i];
                bool apart =
                    pMax + CoverClearance < other.x || pMin - CoverClearance > other.y ||
                    zMax + CoverClearance < other.z || zMin - CoverClearance > other.w;

                if (!apart)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Пересекает ли отрезок прямоугольник. Слэб-метод: без корней и без ветвлений по случаям.</summary>
        private static bool SegmentIntersectsRect(Vector2 a, Vector2 b, float xMin, float xMax, float yMin, float yMax)
        {
            float dx = b.x - a.x;
            float dy = b.y - a.y;
            float t0 = 0f;
            float t1 = 1f;

            return ClipSegment(-dx, a.x - xMin, ref t0, ref t1)
                && ClipSegment(dx, xMax - a.x, ref t0, ref t1)
                && ClipSegment(-dy, a.y - yMin, ref t0, ref t1)
                && ClipSegment(dy, yMax - a.y, ref t0, ref t1);
        }

        private static bool ClipSegment(float direction, float distance, ref float t0, ref float t1)
        {
            if (Mathf.Approximately(direction, 0f))
            {
                return distance >= 0f;
            }

            float t = distance / direction;

            if (direction < 0f)
            {
                if (t > t1)
                {
                    return false;
                }

                if (t > t0)
                {
                    t0 = t;
                }

                return true;
            }

            if (t < t0)
            {
                return false;
            }

            if (t < t1)
            {
                t1 = t;
            }

            return true;
        }

        // ========== ЛОВУШКИ ==========

        private static void BuildFloorTrap(Transform root, int floor, float baseY)
        {
            // Рычагов двери здесь нет: они врезаны в саму перегородку и
            // ставятся вместе с ней, в BuildStairRoom — все её числа известны
            // только там.
            switch (floor)
            {
                case 2:
                    BuildGeyser(root, floor, baseY, ButtonProgress);
                    break;
                case 3:
                    BuildPedestalLever(root, floor, baseY, ButtonProgress, "Collapse", "Провал",
                        FindTrap(root, "CollapseTrap"));
                    break;
                case 4:
                    BuildGeyser(root, floor, baseY, ButtonProgressGeyserFloor5);
                    break;
            }
        }

        /// <summary>
        /// Рычаги двери — по одному на каждой грани перегородки, вплотную к
        /// косяку лаза. Внешний жмёт тот, кто подбегает к лазу снаружи,
        /// внутренний — тот, кто уже прошёл и хочет запереть проход за собой.
        /// Захлопнуть можно и догоняя, и убегая.
        ///
        /// Раньше обе кнопки стояли столбами в стороне от лаза, и нажатие
        /// требовало отдельного крюка к постаменту. Рычаг врезан в ту самую
        /// стену, которую он закрывает: до него дотягиваются тем же движением,
        /// каким лезут в проём. Дотянуться можно только с высоты порога — с
        /// пола коридора рычаг вне радиуса, и подниматься по блокам всё равно
        /// придётся.
        /// </summary>
        private static void BuildDoorLevers(Transform root, int floor, TrapBase door,
            float partitionMin, float partitionMax, float gateBottom, float gateMin)
        {
            if (door == null)
            {
                return;
            }

            // Коридор лежит со стороны меньшего прогресса, лестничная комната —
            // большего. На нечётных этажах трасса идёт против X, и грани
            // перегородки меняются местами.
            bool forward = IsForward(floor);
            float outerX = forward ? partitionMin : partitionMax;
            float outerSign = forward ? -1f : 1f;

            // Рычаг сидит на Partition_SideA — глухом куске перегородки сбоку
            // от лаза, на его же высоте. Заняты обе грани: подбегающий видит
            // свой рычаг снаружи, прошедший — свой изнутри.
            float z = gateMin - LeverGateOffset;
            float y = gateBottom + LeverHandHeight;

            BuildWallLever(root, "Lever_DoorOuter", "Дверь", door, outerX, outerSign, y, z);
            BuildWallLever(root, "Lever_DoorInner", "Дверь", door,
                forward ? partitionMax : partitionMin, -outerSign, y, z);
        }

        /// <summary>
        /// Дверь на выходе с этажа. Захлопнувшись, она держит подбежавшего
        /// снаружи — на открытом месте под обстрелом.
        /// </summary>
        private static DoorTrap BuildDoorTrap(Transform root, int floor, float xMin, float xMax, float yMin, float yMax, float zMin, float zMax)
        {
            Transform group = ResetGroup(root, "DoorTrap");
            var door = group.gameObject.AddComponent<DoorTrap>();

            // Створка ровно по лазу: он теперь единственный проход на этаж,
            // и закрывать всю перегородку от пола до потолка больше нечего.
            GameObject panel = Box(group, "Panel", groundLayer, trapMaterial,
                xMin, xMax, yMin, yMax, zMin, zMax);

            // Открытая створка уходит вниз на свою высоту — в глухой кусок
            // перегородки под лазом, где её целиком видно не будет. Раньше
            // створка уезжала вбок на ширину лаза и выставлялась на 0.36 м
            // сквозь заднюю стену башни наружу. Вниз её раньше убирать
            // было нельзя, потому что створка шла от пола до потолка и уходила
            // бы в коридор этажом ниже. Теперь она ровно по лазу, а под лазом
            // три ШП сплошной стены — прячется целиком и никуда не выходит.
            Vector3 closed = panel.transform.localPosition;
            Vector3 open = closed + Vector3.down * config.ToUnits(GateHeightWidths);

            var so = new SerializedObject(door);
            so.FindProperty("doorBody").objectReferenceValue = panel.transform;
            so.FindProperty("closedLocalPosition").vector3Value = closed;
            so.FindProperty("openLocalPosition").vector3Value = open;
            so.FindProperty("closedDuration").floatValue = config.DoorClosedDuration;

            // Дверь — переключатель, и кулдауна у неё нет по определению:
            // рычаг закрывает и открывает проход сколько угодно раз подряд.
            // Кулдаун из конфига остаётся у мгновенных ловушек, где нажатие
            // разовое и повтор надо чем-то ограничивать.
            so.FindProperty("cooldown").floatValue = 0f;

            // Ход створки задаём явно: мгновенная дверь читается как телепорт,
            // медленная перестаёт быть ловушкой. Треть секунды на два с половиной
            // ШП — быстро, но глаз успевает за движением.
            so.FindProperty("moveDuration").floatValue = DoorMoveDuration;
            so.ApplyModifiedPropertiesWithoutUndo();

            panel.transform.localPosition = open;
            builtTraps.Add(door);
            return door;
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
            BuildPedestalLever(root, floor, baseY, buttonProgress, "Geyser", "Гейзер", trap);
        }

        /// <summary>
        /// Рычаг ловушки на тумбе. Место то же, что у прежней кнопки: в стороне
        /// от кратчайшего пути, к открытой грани — отклонился ради подставы,
        /// потерял время и подставился под выстрел. Это и есть цена нажатия.
        /// Изменился только сам предмет: тумба по пояс вместо столба в рост.
        /// </summary>
        private static void BuildPedestalLever(Transform root, int floor, float baseY, float progress,
            string label, string displayName, TrapBase trap)
        {
            if (trap == null)
            {
                return;
            }

            Transform group = ResetGroup(root, $"Lever_{label}");

            float z = PathDepth - ButtonOffsetFromPath;
            GetXRange(floor, progress, progress + LeverPedestalSize, out float xMin, out float xMax);
            float top = baseY + LeverPedestalHeight;

            Box(group, "Pedestal", groundLayer, wallMaterial, xMin, xMax, baseY, top, z, z + LeverPedestalSize);

            float centerX = (xMin + xMax) * 0.5f;
            float centerZ = z + LeverPedestalSize * 0.5f;
            Box(group, "Plate", groundLayer, wallMaterial,
                centerX - LeverPlateWidth * 0.5f, centerX + LeverPlateWidth * 0.5f,
                top, top + LeverPlateDepth,
                centerZ - LeverPlateWidth * 0.5f, centerZ + LeverPlateWidth * 0.5f);

            // Ручка стоит на плите и заваливается вдоль трассы. Поперёк нельзя:
            // с одной стороны она смотрела бы в открытую грань и терялась на
            // фоне пустоты, с другой — в упор в укрытия.
            AttachLever(group, trap, displayName, ToWorld(centerX, top + LeverPlateDepth, centerZ),
                new Vector3(0f, 0f, -LeverTiltDegrees), new Vector3(0f, 0f, LeverTiltDegrees));
        }

        /// <summary>
        /// Рычаг на вертикальной грани: плита заподлицо со стеной и ручка,
        /// торчащая наружу по нормали. <paramref name="outSign"/> — в какую
        /// сторону по X смотрит грань.
        /// </summary>
        private static void BuildWallLever(Transform root, string groupName, string displayName,
            TrapBase trap, float faceX, float outSign, float y, float z)
        {
            Transform group = ResetGroup(root, groupName);

            float plateOuterX = faceX + outSign * LeverPlateDepth;
            Box(group, "Plate", groundLayer, wallMaterial,
                Mathf.Min(faceX, plateOuterX), Mathf.Max(faceX, plateOuterX),
                y - LeverPlateHeight * 0.5f, y + LeverPlateHeight * 0.5f,
                z - LeverPlateWidth * 0.5f, z + LeverPlateWidth * 0.5f);

            // Ручка ходит в вертикальной плоскости: вверх — готов, вниз — на
            // перезарядке. Углы считаются от перпендикуляра к плите, поэтому на
            // противоположных гранях перегородки они зеркальны.
            float ready = outSign > 0f ? -(90f - LeverTiltDegrees) : 90f - LeverTiltDegrees;
            float fired = outSign > 0f ? -(90f + LeverTiltDegrees) : 90f + LeverTiltDegrees;

            AttachLever(group, trap, displayName, ToWorld(plateOuterX, y, z),
                new Vector3(0f, 0f, ready), new Vector3(0f, 0f, fired));
        }

        /// <summary>
        /// Ручка рычага и его начинка: кнопка взаимодействия плюс сам рычаг.
        ///
        /// Ручка — отдельный поворачиваемый узел, а не просто куб: поворот идёт
        /// вокруг точки крепления к плите, а не вокруг середины ручки.
        /// Коллайдера у неё нет намеренно — она вращается, и телу игрока в ней
        /// делать нечего; чтобы дотянуться, хватает коллайдера плиты. Цветом
        /// готовности красится именно ручка: она же и меняет положение, так что
        /// оба признака состояния лежат на одном предмете.
        /// </summary>
        private static void AttachLever(Transform group, TrapBase trap, string displayName,
            Vector3 hinge, Vector3 readyEuler, Vector3 firedEuler)
        {
            var pivot = new GameObject("Handle");
            pivot.transform.SetParent(group, false);
            pivot.transform.position = hinge;

            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Bar";
            bar.layer = groundLayer;
            bar.transform.SetParent(pivot.transform, false);
            Object.DestroyImmediate(bar.GetComponent<Collider>());

            float length = config.ToUnits(LeverHandleLength);
            float thickness = config.ToUnits(LeverHandleThickness);
            bar.transform.localPosition = new Vector3(0f, length * 0.5f, 0f);
            bar.transform.localScale = new Vector3(thickness, length, thickness);
            bar.GetComponent<MeshRenderer>().sharedMaterial = buttonReadyMaterial;

            var button = group.gameObject.AddComponent<TrapActivationButton>();
            var buttonSo = new SerializedObject(button);
            buttonSo.FindProperty("buttonName").stringValue = displayName;

            SerializedProperty traps = buttonSo.FindProperty("traps");
            traps.arraySize = 1;
            traps.GetArrayElementAtIndex(0).objectReferenceValue = trap;

            buttonSo.FindProperty("indicator").objectReferenceValue = bar.GetComponent<MeshRenderer>();
            buttonSo.FindProperty("readyMaterial").objectReferenceValue = buttonReadyMaterial;
            buttonSo.FindProperty("cooldownMaterial").objectReferenceValue = buttonBusyMaterial;
            buttonSo.ApplyModifiedPropertiesWithoutUndo();

            // Без NetworkObject нажатие по сети не доезжает вообще: мост ищет
            // его в родителях цели и, не найдя, отклоняет намерение целиком
            // (предупреждение в лог, ловушка молчит). Компонент висит на самом
            // рычаге, а не на арене: по присланной ссылке сервер обязан узнать
            // именно этот рычаг, иначе он возьмёт первый интерактив в башне.
            group.gameObject.AddComponent<Unity.Netcode.NetworkObject>();

            var lever = group.gameObject.AddComponent<TrapLever>();
            var leverSo = new SerializedObject(lever);
            leverSo.FindProperty("trap").objectReferenceValue = trap;
            leverSo.FindProperty("handle").objectReferenceValue = pivot.transform;
            leverSo.FindProperty("readyEuler").vector3Value = readyEuler;
            leverSo.FindProperty("firedEuler").vector3Value = firedEuler;
            leverSo.ApplyModifiedPropertiesWithoutUndo();

            pivot.transform.localRotation = Quaternion.Euler(readyEuler);
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

            // Скользкая зона остаётся сплошной, включая проём: вышедший
            // с лестницы сразу оказывается на льду, а не через метр.
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

            BuildIceTile(group, floor, baseY);
        }

        /// <summary>
        /// Видимая плитка льда: без неё зимний этаж ничем не отличается
        /// от летнего. Коллайдера у неё нет — опору даёт перекрытие.
        ///
        /// Проём лестницы вырезается в плитке точно так же, как в перекрытии.
        /// Снять один коллайдер было мало: сплошной лист всё равно накрывал
        /// проём сверху, и поднимающийся с этажа ниже упирался взглядом в лёд,
        /// читал это как забетонированный тупик — и проходил сквозь него насквозь.
        /// Замечено на лестницах 2→3 и 4→5, то есть на обоих зимних этажах.
        /// </summary>
        private static void BuildIceTile(Transform group, int floor, float baseY)
        {
            float top = baseY + IceTileWidths;
            float length = config.FloorLengthWidths;
            float depth = DepthWidths;
            int piece = 0;

            bool hasHole = floor > 0;
            GetStairHoleRange(floor - 1, out float holeXMin, out float holeXMax);

            var edges = new List<float> { 0f, length };
            if (hasHole)
            {
                edges.Add(Mathf.Clamp(holeXMin, 0f, length));
                edges.Add(Mathf.Clamp(holeXMax, 0f, length));
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
                if (hasHole && middle > holeXMin && middle < holeXMax)
                {
                    AddIcePiece(group, ref piece, from, to, baseY, top, 0f, StairHoleZMin);
                    AddIcePiece(group, ref piece, from, to, baseY, top, StairHoleZMax, depth);
                    continue;
                }

                AddIcePiece(group, ref piece, from, to, baseY, top, 0f, depth);
            }
        }

        /// <summary>Кусок ледяного настила. Без коллайдера: чисто видимая вещь.</summary>
        private static void AddIcePiece(Transform group, ref int piece, float xMin, float xMax,
            float yMin, float yMax, float zMin, float zMax)
        {
            if (zMax - zMin < 0.001f)
            {
                return;
            }

            GameObject tile = Box(group, $"IceFloor_{++piece}", groundLayer, iceMaterial,
                xMin, xMax, yMin, yMax, zMin, zMax);
            Object.DestroyImmediate(tile.GetComponent<Collider>());
        }

        /// <summary>Точка возврата этажа: сюда StuckDetector вытаскивает застрявшего.</summary>
        private static void BuildCheckpoint(Transform root, int floor, float baseY)
        {
            Transform group = ResetGroup(root, "Checkpoint");
            GetStairRoomXRange(floor, out float xMin, out float xMax);

            // Точка возврата — на помосте лестничной комнаты, а не на её полу:
            // пола там больше нет — середину комнаты занимает марш, и прежняя
            // точка оказывалась внутри ступеней. Замерено на всех пяти этажах.
            float z = (StairPlatformZMin + StairPlatformZMax) * 0.5f;
            group.position = ToWorld((xMin + xMax) * 0.5f, baseY + GateSillWidths + 0.1f, z);

            // Коллайдер первым: чекпоинт требует его как обязательный.
            var box = group.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(config.StairRoomSizeUnits, config.ToUnits(3f), config.StairRoomSizeUnits);
            group.gameObject.AddComponent<RespawnCheckpoint>();
        }

        /// <summary>
        /// Стартовый щит — стенка вдоль открытой грани первого этажа.
        ///
        /// Без него Охотник стреляет по стоящей на старте куче сразу с первого
        /// кадра: бортик у края чисто визуальный, а прозрачная стена лежит на
        /// слое, исключённом из маски выстрела, — пулю до старта не мешает ничто.
        ///
        /// Щит сплошной и на слое геометрии, то есть держит именно выстрел.
        /// Он не вечный: всего три ШП высотой, и поднявшийся на лифте Охотник
        /// стреляет поверх него — фора даётся на старт, а не на весь этаж.
        /// </summary>
        private static void BuildSpawnShield(Transform root, float baseY)
        {
            Transform group = ResetGroup(root, "SpawnShield");
            GetXRange(0, 0f, SpawnShieldLength, out float xMin, out float xMax);
            Box(group, "Shield", groundLayer, wallMaterial,
                xMin, xMax, baseY, baseY + SpawnShieldHeight, 0f, SpawnShieldThickness);
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
            float holeZMin = StairHoleZMin;
            float holeZMax = StairHoleZMax;

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
            // Стоит у бортика, а не у глухой стены: Охотник бьёт с лифта, то
            // есть из-за открытой грани, и укрытие имеет смысл только между ним
            // и Уткой. У дальней стены оно не прикрывало ни от чего.
            // Ближе к проёму лестницы не ставим — там оно перекрыло бы выход.
            Box(root, "Cover_Roof", coverLayer, coverHighMaterial,
                40f, 42f, baseY, baseY + HighCoverWidths, 0.8f, 2.8f);
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

            // Rigidbody создаётся требованием самой платформы, кинематику и
            // интерполяцию она ставит себе в Awake — здесь только границы хода.
            var platform = platformRoot.gameObject.AddComponent<RidePlatform>();

            var so = new SerializedObject(platform);
            so.FindProperty("speed").floatValue = config.ElevatorSpeedUnits;

            // Границы у общей платформы — мировые, а не отсчёт от старта:
            // одна и та же отметка должна значить одно и то же и у лифта, и у
            // клетки «Секундомера», иначе скриптовый ход по общим часам поедет.
            so.FindProperty("minY").floatValue = config.ElevatorMinHeightUnits;
            so.FindProperty("maxY").floatValue = config.ElevatorMaxHeightUnits;
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

            // Восемь точек одной шеренгой попёрек этажа, а не колонной вдоль трассы:
            // стоя в колонну, передние и задние стартуют с разного расстояния до финиша.
            // Шеренга уравнивает всех и читается как стартовая линия. Игроков может
            // быть меньше восьми, разнос по точкам считает SpawnPointSet.
            for (int i = 0; i < 8; i++)
            {
                var go = new GameObject($"Spawn_Duck_{i + 1}");
                go.transform.SetParent(ducks, false);
                float z = Mathf.Lerp(SpawnEdgeMargin, DepthWidths - SpawnEdgeMargin, i / 7f);
                go.transform.position = ToWorld(GetX(0, SpawnProgress), 0.2f, z);
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
            ValidateButtonSightLines();
            ValidateSpawnShield(spawnsRoot);
        }

        /// <summary>
        /// Видит ли нажимающий зону эффекта своей ловушки (спека, 5.1): без
        /// прямой видимости нажатие — лотерея.
        ///
        /// Проверяются только ловушки с разнесёнными рычагом и зоной эффекта —
        /// Гейзер и Провал. У двери они совмещены (рычаги на самой перегородке),
        /// и в списке линий её нет.
        /// </summary>
        private static void ValidateButtonSightLines()
        {
            int mask = (1 << groundLayer) | (1 << coverLayer);

            for (int floor = 0; floor < config.FloorCount; floor++)
            {
                List<Vector4> lines = GetTrapSightLines(floor);
                float baseY = floor * config.FloorStepWidths;

                for (int i = 0; i < lines.Count; i++)
                {
                    Vector4 line = lines[i];
                    Vector3 eye = ToWorld(GetX(floor, line.x), baseY + EyeHeightWidths, line.y);
                    Vector3 target = ToWorld(GetX(floor, line.z), baseY + EyeHeightWidths * 0.5f, line.w);
                    Vector3 direction = (target - eye).normalized;

                    // Стартуем на шаг от тумбы: игрок стоит рядом с рычагом,
                    // а не внутри него.
                    Vector3 origin = eye + direction * config.ToUnits(1.5f);
                    float distance = Vector3.Distance(origin, target);

                    if (Physics.Raycast(origin, direction, out RaycastHit hit, distance - 0.1f, mask, QueryTriggerInteraction.Ignore))
                    {
                        warnings.Add(
                            $"этаж {floor + 1}: от рычага на p={line.x:F0} не видно зону эффекта на p={line.z:F0} — " +
                            $"луч упирается в «{hit.collider.name}» на {hit.distance:F2} м");
                    }
                }
            }
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

        /// <summary>
        /// Закрыты ли все точки спавна от Охотника в момент старта.
        ///
        /// Глаз берётся не из середины хода лифта, а из его нижней точки:
        /// раунд начинается именно там, и именно оттуда Охотник стрелял бы первым
        /// выстрелом. Маска — та же, что у выстрела, иначе проверка считала бы
        /// помехой бортик и прозрачную стену, которые пулю не держат.
        /// </summary>
        private static void ValidateSpawnShield(Transform spawnsRoot)
        {
            int mask = config.ShotMask.value;
            Vector3 eye = ToWorld(
                config.FloorLengthWidths * 0.5f,
                config.ElevatorMinHeightUnits / config.CharacterWidth + EyeHeightWidths,
                -config.DistanceToElevatorUnits / config.CharacterWidth);

            int exposed = 0;
            var points = spawnsRoot.GetComponentsInChildren<SpawnPoint>(true);
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i].Role != SpawnRole.Default)
                {
                    continue;
                }

                Vector3 chest = points[i].transform.position + Vector3.up * config.ToUnits(EyeHeightWidths * 0.6f);
                Vector3 direction = chest - eye;
                if (!Physics.Raycast(eye, direction.normalized, direction.magnitude, mask, QueryTriggerInteraction.Ignore))
                {
                    exposed++;
                }
            }

            if (exposed > 0)
            {
                warnings.Add(
                    $"старт: {exposed} точек спавна простреливаются с лифта — " +
                    $"удлини стартовый щит (сейчас {SpawnShieldLength:F0} ШП)");
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
        /// <summary>Ближняя граница проёма в перекрытии: полоса второго марша плюс запас.</summary>
        private static float StairHoleZMin => StairFlightBFarZ - StairHoleMargin;
        /// <summary>Дальняя граница проёма в перекрытии.</summary>
        private static float StairHoleZMax => StairFlightBFarZ + StairFlightWidth + StairHoleMargin;
        /// <summary>Ближняя граница помоста лестничной комнаты: сразу за полосой марша.</summary>
        private static float StairPlatformZMin => StairFlightBFarZ + StairFlightWidth + 0.5f;
        /// <summary>Дальняя граница помоста — до глухой стены.</summary>
        private static float StairPlatformZMax => DepthWidths - 0.5f;


        private static float HighCoverWidths => config.HighCoverHeightUnits / config.CharacterWidth;
        private static float LowCoverWidths => config.LowCoverHeightUnits / config.CharacterWidth;
        private static float LedgeWidths => config.LedgeHeightUnits / config.CharacterWidth;

        private static bool IsForward(int floor) => DuckHuntArena.IsForwardFloor(floor);

        private static float GetX(int floor, float progress) =>
            IsForward(floor) ? progress : config.FloorLengthWidths - progress;

        /// <summary>Прогресс трассы по мировому X этажа — обратная к GetX.</summary>
        private static float GetProgress(int floor, float x) =>
            IsForward(floor) ? x : config.FloorLengthWidths - x;

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
            float holeZMin = StairHoleZMin;
            float holeZMax = StairHoleZMax;

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
            // Укрытия и площадки — того же серого, что и вся башня: решение
            // геймдизайнера. Цвета были разные (коричневый, песочный, синий),
            // и арена рябила. Обратная сторона — низкое укрытие больше не
            // отличается от высокого на глаз, и разбирать их придётся по высоте.
            Color blockGrey = new Color(0.5f, 0.5f, 0.53f);
            coverHighMaterial = EnsureMaterial("DH_CoverHigh", blockGrey);
            coverLowMaterial = EnsureMaterial("DH_CoverLow", blockGrey);
            platformMaterial = EnsureMaterial("DH_Platform", blockGrey);
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
