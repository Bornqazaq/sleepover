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

        /// <summary>Где лежат паки Synty. В репозиторий не входят — ставятся каждым разработчиком отдельно.</summary>
        private static readonly string[] SyntyFolders = { "Assets/Synty" };

        private const string FarmProps = "Assets/Synty/PolygonFarm/Prefabs/Props/";
        private const string FarmBuildings = "Assets/Synty/PolygonFarm/Prefabs/Buildings/";
        private const string FarmEnvironments = "Assets/Synty/PolygonFarm/Prefabs/Environments/";
        private const string FarmGenerics = "Assets/Synty/PolygonFarm/Prefabs/Generic/";
        private const string FarmVehicles = "Assets/Synty/PolygonFarm/Prefabs/Vehicles/";
        private const string FarmFx = "Assets/Synty/PolygonFarm/Prefabs/FX/";
        private const string AlpineFx = "Assets/Synty/PolygonNatureBiomes/PNB_Alpine_Mountain/FX/FX_Prefabs/";

        /// <summary>Сноп сена на летнем этаже — спека, 9.5.</summary>
        private const string GeyserFxSummer = FarmFx + "FX_Wheat_Spray_01.prefab";

        /// <summary>Снежная струя на зимнем этаже. Пара в паках нет, снежная пыль — ближайшее по смыслу.</summary>
        private const string GeyserFxWinter = AlpineFx + "FX_Snow_Dust_01.prefab";

        /// <summary>Облако пыли из-под провалившегося пола.</summary>
        private const string CollapseFx = FarmFx + "FX_Dust_Wind_01.prefab";

        /// <summary>Уровень земли, ШП. Ниже зоны вылета (-6) быть не должен, иначе упавший приземлится вместо выбывания.</summary>
        private const float GroundLevel = -3.2f;

        /// <summary>Половина стороны плиты земли, ШП.</summary>
        private const float GroundHalfSize = 280f;

        /// <summary>Насколько фундамент выступает за габарит этажа, ШП.</summary>
        private const float FoundationOverhang = 1.2f;

        /// <summary>Свободное поле вокруг габарита башни, куда декорации не ставятся, ШП.</summary>
        private const float SceneryKeepOut = 8f;

        private const int SceneryTreeCount = 26;
        private const int HorizonCount = 14;
        private const int CloudCount = 12;

        private static readonly Color SunColor = new Color(1f, 0.96f, 0.87f);
        private const float SunIntensity = 1.7f;
        private static readonly Color AmbientSky = new Color(0.78f, 0.85f, 0.97f);
        private static readonly Color AmbientEquator = new Color(0.74f, 0.73f, 0.69f);
        private static readonly Color AmbientGround = new Color(0.50f, 0.48f, 0.42f);

        /// <summary>Повторов тайловой текстуры на юнит поверхности. Пол — крупно, чтобы шаг читался.</summary>
        private const float FloorTiling = 0.35f;

        /// <summary>Лёд тайлится мельче: крупный повтор на скользком настиле читается как размазня.</summary>
        private const float IceTiling = 0.9f;

        /// <summary>Трава в поле повторяется чаще: плита земли в четыреста ШП, и крупный тайл на ней размазывается в пятно.</summary>
        private const float GroundTiling = 1.2f;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

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
        /// <summary>Высота тумбы под рычагом ловушки, ШП — по пояс.</summary>
        private const float LeverPedestalHeight = 1.1f;
        /// <summary>Сторона тумбы, ШП.</summary>
        private const float LeverPedestalSize = 0.9f;
        /// <summary>Высота мачты-маркера над полом, ШП. Выше стоящей Утки (2.29) и выше любого укрытия (2.5).</summary>
        private const float MastHeight = 3.2f;
        /// <summary>Толщина мачты, ШП. Тонкая: она метка, а не укрытие.</summary>
        private const float MastThickness = 0.16f;
        /// <summary>Сторона фонаря на мачте, ШП.</summary>
        private const float MastLampSize = 0.6f;
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
        /// <summary>Длина укрытия вдоль трассы, ШП.</summary>
        private const float CoverLength = 2.5f;
        /// <summary>До какой длины укрытие ужимается, если место тесное, ШП.</summary>
        private const float MinCoverLength = 1.5f;
        /// <summary>Глубина укрытия поперёк этажа, ШП.</summary>
        private const float CoverDepth = 2f;
        /// <summary>
        /// Отступ полосы укрытий от открытой грани и от задней стены, ШП.
        ///
        /// Метр с небольшим, а не полтора: на пятом этаже пять укрытий обязаны
        /// встать поперёк одним рядом, потому что вдоль трассы им остаётся два
        /// с половиной ШП — всё остальное занимает плановый открытый пробег в
        /// шестнадцать. При отступе в 1.5 глубины хватало ровно на четыре
        /// полосы, и пятое укрытие каждую пересборку оставалось за бортом.
        /// </summary>
        private const float CoverEdgeMargin = 1f;
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

        /// <summary>Толщина паркурного блока, ШП. Сторона задаётся этажом — см. <see cref="FloorPlan.Platform"/>.</summary>
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

            /// <summary>Сторона паркурной площадки, ШП. Третий параметр прогрессии из спеки: платформы сужаются к верхним этажам.</summary>
            public float Platform { get; }

            public FloorPlan(int covers, float longestOpen, int gaps, float maxGap, bool ice, float platform)
            {
                Covers = covers;
                LongestOpen = longestOpen;
                Gaps = gaps;
                MaxGap = maxGap;
                Ice = ice;
                Platform = platform;
            }
        }

        /// <summary>
        /// Число разрывов урезано против таблицы 3.9, а их размер сохранён:
        /// 4 разрыва по 5 ШП это 20 ШП дыр в секции длиной 12. Решение
        /// геймдизайнера от 19.08 — сложность паркура читается по длине прыжка,
        /// а не по количеству дыр, а ужатый до 1.75 ШП разрыв перешагивается.
        /// </summary>
        /// <summary>
        /// Прогрессия сложности идёт по трём параметрам спеки (раздел 6):
        /// плотность укрытий падает, открытый участок растёт, паркур сужается.
        ///
        /// Ширина площадки была константой в 1.1 ШП на всех пяти этажах — при
        /// теле в 1 ШП поперёк это полоска в ладонь шире ботинка, одинаково
        /// неудобная и на «намеренно лёгком» первом этаже, и на пятом. Спека
        /// же требует именно сужения к верху, и заодно край площадки обязан
        /// читаться в прыжке (14.3): на 1.1 ШП читать там нечего.
        /// </summary>
        private static readonly FloorPlan[] Plans =
        {
            new FloorPlan(12, 6f, 2, 2f, false, 2.6f),   // 1 — ферма, намеренно лёгкий
            new FloorPlan(10, 8f, 2, 3f, false, 2.3f),   // 2 — ферма, Дверь
            new FloorPlan(8, 10f, 2, 3f, true, 2f),      // 3 — зима, Гейзер
            new FloorPlan(6, 13f, 1, 4f, false, 1.7f),   // 4 — ферма, Провал
            new FloorPlan(5, 16f, 1, 5f, true, 1.4f)     // 5 — зима, Дверь + Гейзер
        };

        private static DuckHuntConfig config;
        private static int groundLayer;
        private static int coverLayer;
        private static int barrierLayer;
        private static System.Random random;

        /// <summary>
        /// Отдельный генератор под арт. Брать общий нельзя: выбор модели
        /// сдвинул бы последовательность, по которой раскладываются укрытия
        /// и паркур, и проверенная планировка поехала бы от смены тюка.
        /// </summary>
        private static System.Random dressRandom;

        /// <summary>Биом текущего этажа: зимние этажи одеваются во второй набор моделей.</summary>
        private static bool currentWinter;
        private static readonly List<TrapBase> builtTraps = new List<TrapBase>(8);
        private static readonly List<SpringTrap> builtGeysers = new List<SpringTrap>(4);
        private static readonly List<DuckHuntStairs> builtStairs = new List<DuckHuntStairs>(8);
        private static readonly List<string> warnings = new List<string>(8);

        /// <summary>
        /// Линия «рычаг → зона эффекта» в координатах трассы. Высота цели
        /// хранится отдельно, потому что она разная: гейзер и провал лежат на
        /// полу, а створка висит в лазе на пороге в три ШП.
        /// </summary>
        private readonly struct SightLine
        {
            public readonly float FromProgress;
            public readonly float FromDepth;
            public readonly float ToProgress;
            public readonly float ToDepth;
            public readonly float ToHeight;

            public SightLine(float fromProgress, float fromDepth, float toProgress, float toDepth, float toHeight)
            {
                FromProgress = fromProgress;
                FromDepth = fromDepth;
                ToProgress = toProgress;
                ToDepth = toDepth;
                ToHeight = toHeight;
            }
        }

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

        /// <summary>Куски пола текущего этажа: заполняет <see cref="BuildSlab"/>, читает <see cref="BuildIce"/>.</summary>
        private static readonly List<Rect> slabRects = new List<Rect>(24);

        private static Material floorMaterial;
        private static Material floorWinterMaterial;
        private static Material metalMaterial;
        private static Material groundMaterial;
        private static Material foundationMaterial;
        private static Material barnMaterial;

        /// <summary>Модели окружения, которых не оказалось: паки Synty каждый ставит себе сам.</summary>
        private static readonly List<string> missingScenery = new List<string>(8);
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
            dressRandom = new System.Random(Seed ^ 0x5F3759DF);
            DuckHuntDress.Begin();
            missingScenery.Clear();
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
            BuildEnvironment(ResetGroup(arenaRoot.transform, "Environment"));
            TuneLighting();
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
            currentWinter = plan.Ice;

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
            CutFloorRects(length, depth, hasCollapse, collapseMin, collapseMax);

            for (int i = 0; i < slabRects.Count; i++)
            {
                Rect rect = slabRects[i];
                Box(slab, $"Slab_{++piece}", groundLayer, FloorMaterial,
                    rect.xMin, rect.xMax, bottom, top, rect.yMin, rect.yMax);
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
        /// Нарезать пол этажа на цельные прямоугольники, обходя дыры из
        /// <see cref="slabHoles"/>. Полосами по X, а внутри полосы — по Z:
        /// прямоугольные вырезы иначе не собрать, дыр много, они не выстроены
        /// в линию, и каждая обязана оставить вокруг себя целый пол.
        ///
        /// Результат кладётся в <see cref="slabRects"/> и служит дважды: по нему
        /// строится перекрытие и по нему же — ледяной настил зимнего этажа.
        /// Второе и есть причина, по которой нарезка вынесена в общий метод:
        /// лёд, нарезанный отдельно, накрывал сплошным листом и паркурную
        /// пропасть, и сквозные колодцы — то есть лежал там, где пола нет.
        /// В прямоугольнике X — по длине этажа, Y — по его глубине.
        /// </summary>
        private static void CutFloorRects(float length, float depth, bool hasSkip, float skipMin, float skipMax)
        {
            slabRects.Clear();

            var edges = new List<float> { 0f, length };
            for (int i = 0; i < slabHoles.Count; i++)
            {
                edges.Add(Mathf.Clamp(slabHoles[i].XMin, 0f, length));
                edges.Add(Mathf.Clamp(slabHoles[i].XMax, 0f, length));
            }

            if (hasSkip)
            {
                edges.Add(skipMin);
                edges.Add(skipMax);
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
                if (hasSkip && middle > skipMin && middle < skipMax)
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
                        slabRects.Add(Rect.MinMaxRect(from, cursor, to, gapMin));
                    }

                    cursor = Mathf.Max(cursor, gapMax);
                }

                if (depth - cursor > 0.001f)
                {
                    slabRects.Add(Rect.MinMaxRect(from, cursor, to, depth));
                }
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

            Box(group, "PitFloor", groundLayer, FloorMaterial, xMin, xMax, floorBottom, floorTop, 0f, DepthWidths);
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
            GameObject ledge = DressedBox(walls, "Ledge", groundLayer, wallMaterial,
                0f, length, baseY, baseY + LedgeWidths, -WallThickness, 0f,
                DuckHuntDress.Kind.Ledge);
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
            Box(room, "Wall_TowardElevator", groundLayer, barnMaterial,
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

            BuildDoorTrap(root, floor, partitionMin, partitionMax, gateBottom, gateTop, gateMin, gateMax);
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
                DressedBox(group, $"GateBlock_{i + 1}", groundLayer, platformMaterial,
                    xMin, xMax, baseY, baseY + heights[i], zMin, zMin + zSize,
                    DuckHuntDress.Kind.GateStep);
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
            // Сторона площадки — своя на каждом этаже: это третий параметр
            // прогрессии из спеки. Ниже она участвует и в разрывах, и в
            // поперечном сносе, поэтому берётся один раз в начале.
            float blockSize = plan.Platform;

            float longGap = Mathf.Min(plan.MaxGap, JumpRangeWidths * JumpSafetyShare - blockSize);
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
            float previousZ = ParkourNearZ + (ParkourWidth - blockSize) * 0.5f;
            int index = 0;
            float lastLanded = start;

            while (cursor + blockSize <= ParkourEnd + 0.01f)
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
                float bandMax = Mathf.Min(ParkourNearZ + ParkourWidth - blockSize, previousZ + lateral);
                float z = Mathf.Lerp(bandMin, bandMax, (float)random.NextDouble());

                GetXRange(floor, cursor, cursor + blockSize, out float xMin, out float xMax);
                cursor += blockSize + gap;
                index++;

                if (OverlapsStairwell(floor, xMin, xMax, z, z + blockSize))
                {
                    continue;
                }

                // Блок, чей низ почти касается пола, ставим на пол целиком:
                // восемь сантиметров пустоты под ним — это щель и кромка под ногу.
                float bottom = height - ParkourBlockThickness <= ParkourFloorSnapWidths
                    ? baseY
                    : baseY + height - ParkourBlockThickness;

                DressedBox(group, $"Block_{index:00}", groundLayer, platformMaterial,
                    xMin, xMax, bottom, baseY + height, z, z + blockSize,
                    DuckHuntDress.Kind.Platform);
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
                GetXRange(floor, ParkourEnd - blockSize, ParkourEnd, out float tailXMin, out float tailXMax);
                float tailZ = Mathf.Clamp(previousZ, ParkourNearZ, ParkourNearZ + ParkourWidth - blockSize);
                DressedBox(group, "Block_Tail", groundLayer, platformMaterial,
                    tailXMin, tailXMax, baseY, baseY + 1f, tailZ, tailZ + blockSize,
                    DuckHuntDress.Kind.Platform);
            }
        }

        /// <summary>
        /// Укрытия. Высокие прячут стоящего, низкие — только присевшего, и
        /// присед стоит 55% скорости: за низким укрытием безопасно, но медленно.
        /// Плотность падает от этажа к этажу — это и есть прогрессия сложности.
        ///
        /// Раскладка идёт решёткой «ряд вдоль трассы × полоса по глубине», а не
        /// случайным перебором. Перебор с двумя десятками попыток выдыхался на
        /// верхних этажах: там полоса прогресса сжимается до двух ШП (сорок
        /// минус открытый пробег в шестнадцать), все укрытия приходится ставить
        /// почти на одном p, различая только глубиной, и случайная точка
        /// попадала в уже занятое место снова и снова. Каждая пересборка
        /// теряла по два-три укрытия на этаж — то есть ровно ту величину,
        /// которая и задаёт прогрессию сложности.
        /// </summary>
        private static void BuildCovers(Transform root, int floor, float baseY, FloorPlan plan)
        {
            Transform group = ResetGroup(root, "Covers");

            // Укрытия кончаются там, где начинается плановый открытый пробег.
            // «Самый длинный открытый участок» из таблицы прогрессии — параметр
            // спеки, и получаться он обязан намеренно, а не побочным эффектом
            // от выброшенных укрытий.
            float from = CoverStart;
            float to = Mathf.Max(from + CoverLength, StairRoomStart - plan.LongestOpen);
            int highCount = Mathf.RoundToInt(plan.Covers * HighCoverShare);

            List<SightLine> sightLines = GetTrapSightLines(floor);

            // Полосы глубины: сколько укрытий встаёт поперёк этажа, не задевая
            // друг друга. Считается от глубины этажа, а не задано числом:
            // ширина этажа — настройка конфига, и захардкоженные четыре полосы
            // разъехались бы вместе с ней.
            float bandMin = CoverEdgeMargin;
            float bandMax = DepthWidths - CoverEdgeMargin - CoverDepth;
            int lanes = Mathf.Max(1, Mathf.FloorToInt((bandMax - bandMin) / (CoverDepth + CoverClearance)) + 1);

            // Рядов вдоль трассы столько, чтобы хватило на все укрытия, плюс
            // запасной ряд, если решётка сходится ровно под их число. Запас
            // нужен не для красоты: линию «рычаг — зона эффекта» загораживать
            // нельзя, и на этаже с дверью она съедает ровно одну клетку. Без
            // запаса это стоило этажу одного укрытия каждую пересборку.
            // Ряд добавляется только если он не ужмёт укрытие ниже
            // минимальной длины — на верхних этажах полоса слишком коротка.
            int rows = Mathf.CeilToInt(plan.Covers / (float)lanes);
            if (rows * lanes <= plan.Covers && (to - from) / (rows + 1) >= MinCoverLength + CoverClearance * 2f)
            {
                rows += 1;
            }

            float rowStep = (to - from) / rows;

            var placed = new List<Vector4>(plan.Covers);
            int built = 0;

            for (int i = 0; i < plan.Covers; i++)
            {
                // Высокие раскладываем равномерно по цепочке, а не броском
                // монеты: на пяти укрытиях монета легко оставляет этаж вообще
                // без высоких, и стоящему игроку прятаться негде.
                bool high = i * highCount / plan.Covers != (i + 1) * highCount / plan.Covers;
                float height = high ? HighCoverWidths : LowCoverWidths;

                // Соседние по номеру укрытия расходятся и по глубине, и по
                // ряду: иначе ближний ряд забивается целиком, а дальний
                // остаётся пустым.
                int lane = i % lanes;
                int row = i / lanes;

                float laneDepth = lanes == 1
                    ? Mathf.Lerp(bandMin, bandMax, 0.5f)
                    : Mathf.Lerp(bandMin, bandMax, lane / (float)(lanes - 1));

                // Место укрытия ищется в три захода: своя клетка решётки,
                // затем вся полоса вразброс, затем — если и там не нашлось —
                // сплошной перебор клеток подряд. Последний заход и даёт
                // обещание «сколько укрытий в плане, столько и встанет»:
                // случайный поиск в тесной полосе верхних этажей может
                // не найти щель, которая там есть.
                bool done = false;
                for (int attempt = 0; attempt < CoverPlacementAttempts && !done; attempt++)
                {
                    // Первые попытки — в своей клетке решётки с небольшим
                    // разбросом, дальше клетка отпускается: ровность важна,
                    // наличие важнее.
                    float squeeze = attempt / (float)CoverPlacementAttempts;
                    bool loose = squeeze >= 0.5f;

                    // Длину укрытия задаёт шаг ряда, а не константа. На верхних
                    // этажах полоса всего в два-три ШП, и укрытие в полные 2.5
                    // не помещается даже в один ряд: все они схлопывались на
                    // одном p и различались только глубиной, а глубина даёт
                    // ровно четыре места. Пятому укрытию вставать было некуда.
                    float rowWidth = Mathf.Clamp(rowStep - CoverClearance * 2f, MinCoverLength, CoverLength);
                    float width = Mathf.Lerp(rowWidth, MinCoverLength, squeeze);

                    // Внутри ряда укрытие стоит по центру клетки, а не у её
                    // края: соседние ряды тогда расходятся на полный зазор с
                    // обеих сторон и не считаются слипшимися.
                    float slotMin = from + rowStep * row;
                    float slotMax = Mathf.Min(to, slotMin + rowStep);
                    float slack = Mathf.Max(0f, slotMax - slotMin - width);
                    float p = loose
                        ? Mathf.Lerp(from, to - width, (float)random.NextDouble())
                        : slotMin + slack * (0.25f + 0.5f * (float)random.NextDouble());
                    p = Mathf.Clamp(p, from, Mathf.Max(from, to - width));

                    // Глубина гуляет в пределах своей полосы, чтобы ряд не
                    // выглядел линейкой, но не наезжает на соседнюю.
                    float jitter = CoverClearance * 0.5f * (float)(random.NextDouble() * 2f - 1f);
                    float z = loose
                        ? Mathf.Lerp(bandMin, bandMax, (float)random.NextDouble())
                        : Mathf.Clamp(laneDepth + jitter, bandMin, bandMax);

                    // Линию от рычага до зоны эффекта не загораживаем: спека
                    // требует прямой видимости, иначе нажатие — лотерея.
                    if (BlocksSightLine(sightLines, p, p + width, z, z + CoverDepth))
                    {
                        continue;
                    }

                    GetXRange(floor, p, p + width, out float xMin, out float xMax);
                    if (OverlapsStairwell(floor, xMin, xMax, z, z + CoverDepth))
                    {
                        continue;
                    }

                    // Слипшиеся укрытия читаются как одна стена и съедают
                    // проходимость этажа — держим их врозь.
                    if (OverlapsPlaced(placed, p, p + width, z, z + CoverDepth))
                    {
                        continue;
                    }

                    DressedBox(group, $"Cover_{i + 1:00}_{(high ? "High" : "Low")}", coverLayer,
                        high ? coverHighMaterial : coverLowMaterial,
                        xMin, xMax, baseY, baseY + height, z, z + CoverDepth,
                        high ? DuckHuntDress.Kind.CoverHigh : DuckHuntDress.Kind.CoverLow);

                    placed.Add(new Vector4(p, p + width, z, z + CoverDepth));
                    done = true;
                    built++;
                }

                if (!done)
                {
                    done = SweepForFreeCell(group, floor, baseY, sightLines, placed,
                        from, to, bandMin, bandMax, lanes, rows, high, height, i);
                    if (done)
                    {
                        built++;
                    }
                }
            }

            if (built < plan.Covers)
            {
                warnings.Add(
                    $"этаж {floor + 1}: поставлено {built} укрытий из {plan.Covers} — " +
                    $"полоса p {from:F0}…{to:F0}, полос глубины {lanes}, рядов {rows}");
            }
        }

        /// <summary>
        /// Сплошной перебор клеток решётки под укрытие, которому не нашлось
        /// места вразброс. Идёт по всем рядам и полосам подряд и ставит
        /// укрытие в первую свободную клетку.
        ///
        /// Нужен ровно из-за верхних этажей: там полоса прогресса ужимается до
        /// пары ШП, свободных клеток остаётся одна-две, и случайный тык в них
        /// не попадает даже за два десятка попыток. Потерянное укрытие — это
        /// потерянная прогрессия сложности, а не косметика.
        /// </summary>
        private static bool SweepForFreeCell(Transform group, int floor, float baseY,
            List<SightLine> sightLines, List<Vector4> placed,
            float from, float to, float bandMin, float bandMax, int lanes, int rows,
            bool high, float height, int index)
        {
            float rowStep = (to - from) / rows;
            float width = Mathf.Clamp(rowStep - CoverClearance * 2f, MinCoverLength, CoverLength);

            for (int row = 0; row < rows; row++)
            {
                for (int lane = 0; lane < lanes; lane++)
                {
                    float p = from + rowStep * row + Mathf.Max(0f, rowStep - width) * 0.5f;
                    p = Mathf.Clamp(p, from, Mathf.Max(from, to - width));
                    float z = lanes == 1
                        ? Mathf.Lerp(bandMin, bandMax, 0.5f)
                        : Mathf.Lerp(bandMin, bandMax, lane / (float)(lanes - 1));

                    if (BlocksSightLine(sightLines, p, p + width, z, z + CoverDepth))
                    {
                        continue;
                    }

                    GetXRange(floor, p, p + width, out float xMin, out float xMax);
                    if (OverlapsStairwell(floor, xMin, xMax, z, z + CoverDepth))
                    {
                        continue;
                    }

                    if (OverlapsPlaced(placed, p, p + width, z, z + CoverDepth))
                    {
                        continue;
                    }

                    DressedBox(group, $"Cover_{index + 1:00}_{(high ? "High" : "Low")}", coverLayer,
                        high ? coverHighMaterial : coverLowMaterial,
                        xMin, xMax, baseY, baseY + height, z, z + CoverDepth,
                        high ? DuckHuntDress.Kind.CoverHigh : DuckHuntDress.Kind.CoverLow);

                    placed.Add(new Vector4(p, p + width, z, z + CoverDepth));
                    return true;
                }
            }

            return false;
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
        private static List<SightLine> GetTrapSightLines(int floor)
        {
            var lines = new List<SightLine>(2);

            // Линия идёт из середины тумбы, а не из её угла: иначе проверочный
            // луч стартует внутри самого рычага и упирается в него.
            float buttonZ = PathDepth - ButtonOffsetFromPath + 0.5f;
            float geyserZ = (DepthWidths - 5f) * 0.5f + 2.5f;
            float geyserP = (GeyserStart + GeyserEnd) * 0.5f;

            // Гейзер и провал лежат на полу — смотреть на них надо вниз.
            float groundTarget = EyeHeightWidths * 0.5f;

            // Створка висит в лазе, а лаз поднят на порог в три ШП. Целить в
            // неё на высоте пола бессмысленно: там сплошная перегородка под
            // порогом, и проверка ловит стену вместо створки.
            float gateZ = StairFlightANearZ + DoorwayWidth * 0.5f;
            float gateTarget = GateSillWidths + GateHeightWidths * 0.5f;

            switch (floor)
            {
                case 1:
                    lines.Add(new SightLine(ButtonProgress + 0.5f, buttonZ, DoorProgress, gateZ, gateTarget));
                    break;
                case 2:
                    lines.Add(new SightLine(ButtonProgress + 0.5f, buttonZ, geyserP, geyserZ, groundTarget));
                    break;
                case 3:
                    lines.Add(new SightLine(ButtonProgress + 0.5f, buttonZ, (CollapseStart + CollapseEnd) * 0.5f, DepthWidths * 0.5f, groundTarget));
                    break;
                case 4:
                    lines.Add(new SightLine(ButtonProgressGeyserFloor5 + 0.5f, buttonZ, geyserP, geyserZ, groundTarget));
                    lines.Add(new SightLine(ButtonProgress + 0.5f, buttonZ, DoorProgress, gateZ, gateTarget));
                    break;
            }

            return lines;
        }

        private static bool BlocksSightLine(List<SightLine> lines, float pMin, float pMax, float zMin, float zMax)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                SightLine line = lines[i];
                if (SegmentIntersectsRect(
                        new Vector2(line.FromProgress, line.FromDepth), new Vector2(line.ToProgress, line.ToDepth),
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
            // Рычаг двери стоит там же, где рычаги двух других ловушек, — на
            // тумбе в 29 ШП, в стороне от кратчайшего пути. Раньше он был
            // врезан в саму перегородку у лаза, и это ломало главный принцип
            // спеки (9.1-9.2): нажимающий обязан стоять У КНОПКИ, а не в зоне
            // эффекта. У лаза он стоял ровно в ней — захлопывал створку себе
            // под нос, цена нажатия исчезала, и ловушка догоняющего
            // превращалась в кнопку «закрыть за собой».
            switch (floor)
            {
                case 1:
                    BuildPedestalLever(root, floor, baseY, ButtonProgress, "Door", "Дверь",
                        FindTrap(root, "DoorTrap"));
                    break;
                case 2:
                    BuildGeyser(root, floor, baseY, ButtonProgress);
                    break;
                case 3:
                    BuildPedestalLever(root, floor, baseY, ButtonProgress, "Collapse", "Провал",
                        FindTrap(root, "CollapseTrap"));
                    break;
                case 4:
                    // Пятый этаж несёт обе ловушки (спека, раздел 6), поэтому и
                    // рычага два: гейзер сдвинут на 31 ШП, чтобы тумбы не слиплись.
                    BuildGeyser(root, floor, baseY, ButtonProgressGeyserFloor5);
                    BuildPedestalLever(root, floor, baseY, ButtonProgress, "Door", "Дверь",
                        FindTrap(root, "DoorTrap"));
                    break;
            }
        }

        private static DoorTrap BuildDoorTrap(Transform root, int floor, float xMin, float xMax, float yMin, float yMax, float zMin, float zMax)
        {
            Transform group = ResetGroup(root, "DoorTrap");
            var door = group.gameObject.AddComponent<DoorTrap>();

            // Створка ровно по лазу: он теперь единственный проход на этаж,
            // и закрывать всю перегородку от пола до потолка больше нечего.
            GameObject panel = DressedBox(group, "Panel", groundLayer, trapMaterial,
                xMin, xMax, yMin, yMax, zMin, zMax, DuckHuntDress.Kind.TrapDoor);

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

            // Перезарядка у двери такая же, как у двух других ловушек
            // (спека, 9.3): захлопнулась на пять секунд, потом десять секунд
            // рычаг не работает. Нулевой кулдаун превращал её в переключатель
            // «держать закрытым сколько угодно» — этого спека не описывает
            // нигде, а на этаже с одним проходом это глухая пробка.
            so.FindProperty("cooldown").floatValue = config.ButtonCooldown;

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

            GameObject section = DressedBox(group, "Section", groundLayer, trapMaterial,
                xMin, xMax, bottom, top, 0f, depth, DuckHuntDress.Kind.TrapFloor);

            var so = new SerializedObject(trap);
            SerializedProperty colliders = so.FindProperty("floorColliders");
            colliders.arraySize = 1;
            colliders.GetArrayElementAtIndex(0).objectReferenceValue = section.GetComponent<Collider>();

            // Ловушка прячет то, что видно на самом деле. Дресс гасит рендерер
            // коробки блокаута и ставит внутрь модель: отдав ловушке рендерер
            // коробки, мы получили бы провал, на котором стог остаётся висеть
            // в воздухе, а на закрытии — серую коробку поверх модели, потому
            // что ловушка включает рендереры обратно. Без паков дресса нет,
            // включённым остаётся рендерер коробки — список соберётся сам.
            var visible = new List<Renderer>(4);
            foreach (Renderer candidate in section.GetComponentsInChildren<Renderer>(true))
            {
                if (candidate.enabled)
                {
                    visible.Add(candidate);
                }
            }

            SerializedProperty renderers = so.FindProperty("floorRenderers");
            renderers.arraySize = visible.Count;
            for (int i = 0; i < visible.Count; i++)
            {
                renderers.GetArrayElementAtIndex(i).objectReferenceValue = visible[i];
            }

            so.FindProperty("openDuration").floatValue = config.CollapseDuration;
            so.FindProperty("cooldown").floatValue = config.ButtonCooldown;
            so.ApplyModifiedPropertiesWithoutUndo();

            AttachTrapEffect(group, trap, CollapseFx,
                ToWorld((xMin + xMax) * 0.5f, bottom, depth * 0.5f));

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
            DressedBox(group, "Pad", groundLayer, trapMaterial, xMin, xMax, baseY, baseY + 0.15f, z, z + 5f,
                DuckHuntDress.Kind.TrapPad);

            AttachTrapEffect(group, trap, currentWinter ? GeyserFxWinter : GeyserFxSummer,
                ToWorld((xMin + xMax) * 0.5f, baseY + 0.2f, z + 2.5f));

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

            DressedBox(group, "Pedestal", groundLayer, wallMaterial, xMin, xMax, baseY, top, z, z + LeverPedestalSize,
                DuckHuntDress.Kind.LeverPost);

            float centerX = (xMin + xMax) * 0.5f;
            float centerZ = z + LeverPedestalSize * 0.5f;

            BuildLeverMast(group, centerX, centerZ, baseY);

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
        /// Подвесить ловушке эффект срабатывания.
        ///
        /// Эффект живёт отдельным объектом рядом с ловушкой, а не внутри её
        /// подвижных частей: у провала подвижная часть исчезает вместе с
        /// рендерером, и вложенная в неё партикл-система погасла бы ровно в тот
        /// момент, ради которого её ставили.
        ///
        /// Партикл-система переводится в ручной запуск: у эффектов пака стоит
        /// автостарт с зацикливанием, и без этого сноп сена бил бы из площадки
        /// непрерывно всю игру.
        /// </summary>
        private static void AttachTrapEffect(Transform group, TrapBase trap, string fxPath, Vector3 position)
        {
            if (trap == null)
            {
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fxPath);
            if (prefab == null)
            {
                if (!missingScenery.Contains(fxPath))
                {
                    missingScenery.Add(fxPath);
                }

                return;
            }

            var fx = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            fx.name = "Fx";
            fx.transform.position = position;
            fx.transform.rotation = Quaternion.identity;

            var systems = fx.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.MainModule main = systems[i].main;
                main.playOnAwake = false;
                main.loop = false;
                systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            var effects = group.gameObject.AddComponent<DuckHuntTrapEffects>();
            var so = new SerializedObject(effects);
            so.FindProperty("trap").objectReferenceValue = trap;
            so.FindProperty("burst").objectReferenceValue = systems.Length > 0 ? systems[0] : null;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Мачта над тумбой с красным фонарём наверху.
        ///
        /// Спека (14.3) требует, чтобы кнопка ловушки была крупной, красной и
        /// видной издалека. Сам рычаг таким быть не может: он размером с
        /// ладонь и врезан туда, куда игрок тянется рукой, — это решение
        /// осталось в силе, потому что столб в рост загораживал проход.
        /// Мачта разводит два требования по разным предметам: работает рука,
        /// а видно издалека фонарь над ней.
        ///
        /// Коллайдеров у мачты нет намеренно. Во-первых, она не предмет
        /// взаимодействия. Во-вторых, из тумбы выходит проверочный луч
        /// видимости зоны эффекта, и сплошная мачта ловила бы его на первом
        /// же сантиметре.
        /// </summary>
        private static void BuildLeverMast(Transform group, float centerX, float centerZ, float baseY)
        {
            float half = MastThickness * 0.5f;
            GameObject mast = Box(group, "Mast", groundLayer, wallMaterial,
                centerX - half, centerX + half, baseY, baseY + MastHeight,
                centerZ - half, centerZ + half);
            Object.DestroyImmediate(mast.GetComponent<Collider>());

            float lampHalf = MastLampSize * 0.5f;
            GameObject lamp = Box(group, "Lamp", groundLayer, buttonReadyMaterial,
                centerX - lampHalf, centerX + lampHalf,
                baseY + MastHeight, baseY + MastHeight + MastLampSize,
                centerZ - lampHalf, centerZ + lampHalf);
            Object.DestroyImmediate(lamp.GetComponent<Collider>());
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

            BuildIceTile(group, baseY);
        }

        /// <summary>
        /// Видимая плитка льда: без неё зимний этаж ничем не отличается
        /// от летнего. Коллайдера у неё нет — опору даёт перекрытие.
        ///
        /// Лёд кладётся ровно по кускам перекрытия (<see cref="slabRects"/>),
        /// то есть только там, где под ним есть пол. Своя нарезка тут была
        /// ошибкой: она знала лишь про проём лестницы, и сплошной лист
        /// накрывал и паркурную пропасть, и сквозные колодцы, и случайные
        /// провалы — Утка видела ледяной пол там, где падала насквозь.
        ///
        /// Полоса провала (четвёртый этаж) в нарезку не входит и льда не
        /// получает: она умеет исчезать, и настил обязан исчезать вместе с
        /// ней. Пока провал стоит на летнем этаже, вопрос не возникает.
        /// </summary>
        private static void BuildIceTile(Transform group, float baseY)
        {
            float top = baseY + IceTileWidths;
            int piece = 0;

            for (int i = 0; i < slabRects.Count; i++)
            {
                Rect rect = slabRects[i];
                AddIcePiece(group, ref piece, rect.xMin, rect.xMax, baseY, top, rect.yMin, rect.yMax);
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
            DressedBox(group, "Shield", groundLayer, wallMaterial,
                xMin, xMax, baseY, baseY + SpawnShieldHeight, 0f, SpawnShieldThickness,
                DuckHuntDress.Kind.SpawnShield);
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
            Box(root, "Roof_Near", groundLayer, FloorMaterial,
                0f, Mathf.Min(holeMin, holeMax), baseY - SlabWidths, baseY, 0f, depth);
            Box(root, "Roof_Far", groundLayer, FloorMaterial,
                Mathf.Max(holeMin, holeMax), length, baseY - SlabWidths, baseY, 0f, depth);
            Box(root, "Roof_HoleSideA", groundLayer, FloorMaterial,
                holeMin, holeMax, baseY - SlabWidths, baseY, 0f, holeZMin);
            Box(root, "Roof_HoleSideB", groundLayer, FloorMaterial,
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
            GameObject railA = Box(rails, "Rail_A", groundLayer, metalMaterial,
                x - half - 0.5f, x - half, minH, maxH + 2f, z - half, z - half + 0.5f);
            GameObject railB = Box(rails, "Rail_B", groundLayer, metalMaterial,
                x + half, x + half + 0.5f, minH, maxH + 2f, z + half - 0.5f, z + half);
            Object.DestroyImmediate(railA.GetComponent<Collider>());
            Object.DestroyImmediate(railB.GetComponent<Collider>());

            Transform platformRoot = ResetGroup(root, "Platform");
            platformRoot.position = ToWorld(x, minH, z);

            GameObject deck = DressedBox(platformRoot, "Deck", groundLayer, metalMaterial,
                x - half, x + half, minH - 0.4f, minH, z - half, z + half,
                DuckHuntDress.Kind.ElevatorDeck);
            deck.transform.SetParent(platformRoot, true);

            // Перила по краям: сойти нельзя и так (RidePlatform держит пассажира),
            // но без них платформа не читается как платформа.
            Box(platformRoot, "Rail_Front", groundLayer, metalMaterial,
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
        /// Проверяются все три ловушки: с переносом рычага двери на тумбу
        /// разнесённые рычаг и зона эффекта появились и у неё.
        /// </summary>
        private static void ValidateButtonSightLines()
        {
            int mask = (1 << groundLayer) | (1 << coverLayer);

            for (int floor = 0; floor < config.FloorCount; floor++)
            {
                List<SightLine> lines = GetTrapSightLines(floor);
                float baseY = floor * config.FloorStepWidths;

                for (int i = 0; i < lines.Count; i++)
                {
                    SightLine line = lines[i];
                    Vector3 eye = ToWorld(GetX(floor, line.FromProgress), baseY + EyeHeightWidths, line.FromDepth);
                    Vector3 target = ToWorld(GetX(floor, line.ToProgress), baseY + line.ToHeight, line.ToDepth);
                    Vector3 direction = (target - eye).normalized;

                    // Стартуем на шаг от тумбы: игрок стоит рядом с рычагом,
                    // а не внутри него.
                    Vector3 origin = eye + direction * config.ToUnits(1.5f);
                    float distance = Vector3.Distance(origin, target);

                    if (Physics.Raycast(origin, direction, out RaycastHit hit, distance - 0.1f, mask, QueryTriggerInteraction.Ignore))
                    {
                        warnings.Add(
                            $"этаж {floor + 1}: от рычага на p={line.FromProgress:F0} не видно зону эффекта на p={line.ToProgress:F0} — " +
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

            // Ненайденные модели — отдельной строкой и всегда. Паки Synty в
            // репозиторий не входят, и на машине без них башня соберётся
            // серой: без этой строки разница читалась бы как «арт не сделан».
            var absent = new List<string>(missingScenery);
            for (int i = 0; i < DuckHuntDress.Missing.Count; i++)
            {
                absent.Add(DuckHuntDress.Missing[i]);
            }

            if (absent.Count > 0)
            {
                Debug.LogWarning(
                    $"Duck Hunt: не найдено моделей паков — {absent.Count}. Там, где их нет, башня осталась блокаутом. " +
                    "Поставь паки Synty (POLYGON Farm и POLYGON Nature Biomes: Alpine Mountain) и пересобери.\n— " +
                    string.Join("\n— ", absent.ToArray()));
            }

            if (warnings.Count == 0)
            {
                Debug.Log(summary);
                return;
            }

            Debug.LogWarning(summary + "\nЗамечания:\n— " + string.Join("\n— ", warnings));
        }

        // ========== ГЕОМЕТРИЯ ==========

        /// <summary>Материал пола текущего этажа: земля летом, снег зимой.</summary>
        private static Material FloorMaterial => currentWinter ? floorWinterMaterial : floorMaterial;

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

        /// <summary>
        /// Поле вокруг башни: земля, фундамент, ферма на горизонте, горы и небо.
        ///
        /// Спека (14) ставит башню посреди открытого поля под ярким солнцем, а
        /// до этой сессии вокруг не было вообще ничего: башня висела в пустоте,
        /// и нижней половиной кадра у Охотника была нижняя половина скайбокса.
        ///
        /// Всё окружение — без коллайдеров и вне зоны игры. Земля лежит НИЖЕ
        /// зоны вылета, поэтому упавший всё так же выбывает, а не приземляется
        /// на травку: смерть считает триггер, а не пол.
        /// </summary>
        private static void BuildEnvironment(Transform root)
        {
            float length = config.FloorLengthWidths;
            float depth = DepthWidths;
            float centreX = length * 0.5f;
            float centreZ = depth * 0.5f;

            // Земля: одна большая плита под всей сценой.
            GameObject ground = Box(root, "Ground", groundLayer, groundMaterial,
                centreX - GroundHalfSize, centreX + GroundHalfSize,
                GroundLevel - 1f, GroundLevel,
                centreZ - GroundHalfSize, centreZ + GroundHalfSize);
            Object.DestroyImmediate(ground.GetComponent<Collider>());

            // Фундамент — юбка по периметру первого этажа. Сплошную плиту сюда
            // класть нельзя: под паркуром первого этажа выкопана яма, и плита
            // засыпала бы её.
            Transform basement = ResetGroup(root, "Foundation");
            BuildFoundationSkirt(basement, length, depth);

            BuildFarmScenery(ResetGroup(root, "Scenery"), centreX);
            BuildHorizon(ResetGroup(root, "Horizon"), centreX, centreZ);
        }

        /// <summary>Юбка фундамента по четырём сторонам первого этажа.</summary>
        private static void BuildFoundationSkirt(Transform root, float length, float depth)
        {
            float top = -SlabWidths;
            float bottom = GroundLevel;
            float over = FoundationOverhang;

            AddScenery(Box(root, "Skirt_Front", groundLayer, foundationMaterial,
                -over, length + over, bottom, top, -over, 0f));
            AddScenery(Box(root, "Skirt_Back", groundLayer, foundationMaterial,
                -over, length + over, bottom, top, depth, depth + over));
            AddScenery(Box(root, "Skirt_EndA", groundLayer, foundationMaterial,
                -over, 0f, bottom, top, 0f, depth));
            AddScenery(Box(root, "Skirt_EndB", groundLayer, foundationMaterial,
                length, length + over, bottom, top, 0f, depth));
        }

        /// <summary>
        /// Ферма в поле: амбар, силосы, ветряк, водонапорка, техника, деревья.
        ///
        /// Всё стоит за шахтой лифта, то есть ровно в той половине мира, куда
        /// Утки смотрят с этажей через открытую грань. Ставить это за башней
        /// бессмысленно — там сплошная задняя стена.
        /// </summary>
        private static void BuildFarmScenery(Transform root, float centreX)
        {
            float length = config.FloorLengthWidths;
            float depth = DepthWidths;

            // прогресс вдоль трассы, глубина от открытой грани (отрицательная —
            // за лифтом), доворот, масштаб
            PlaceScenery(root, FarmBuildings + "SM_Bld_Barn_01.prefab", centreX - 34f, -58f, 25f, 1f);
            PlaceScenery(root, FarmBuildings + "SM_Bld_Silo_01.prefab", centreX + 26f, -63f, 0f, 1f);
            PlaceScenery(root, FarmBuildings + "SM_Bld_Silo_02.prefab", centreX + 31f, -66f, 0f, 1f);
            PlaceScenery(root, FarmBuildings + "SM_Bld_Silo_Base_01.prefab", centreX + 26f, -63f, 0f, 1f);
            PlaceScenery(root, FarmBuildings + "SM_Bld_WaterTower_01.prefab", centreX + 8f, -44f, 0f, 1f);
            PlaceScenery(root, FarmProps + "SM_Prop_Windmill_01.prefab", centreX - 12f, -86f, -20f, 1f);
            PlaceScenery(root, FarmBuildings + "SM_Bld_Farmhouse_01.prefab", centreX + 48f, -52f, -35f, 1f);
            PlaceScenery(root, FarmProps + "SM_Prop_Scarecrow_01.prefab", centreX - 4f, -30f, 15f, 1f);
            PlaceScenery(root, FarmVehicles + "SM_Veh_Tractor_01.prefab", centreX + 16f, -33f, 70f, 1f);
            PlaceScenery(root, FarmProps + "SM_Prop_Truck_Rusted_01.prefab", centreX - 22f, -36f, 110f, 1f);
            PlaceScenery(root, FarmProps + "SM_Prop_Well_01.prefab", centreX - 30f, -28f, 0f, 1f);

            // Деревья и кусты по кругу: они и прячут край плиты земли.
            var rng = new System.Random(Seed ^ 0x2BADD00D);
            string[] trees =
            {
                FarmEnvironments + "SM_Env_Tree_Large_01.prefab",
                FarmEnvironments + "SM_Env_Tree_Apple_Grown_01.prefab",
                FarmEnvironments + "SM_Env_Tree_Pear_Grown_01.prefab",
                FarmGenerics + "SM_Generic_Tree_01.prefab",
                FarmGenerics + "SM_Generic_Tree_03.prefab"
            };

            for (int i = 0; i < SceneryTreeCount; i++)
            {
                double angle = i / (double)SceneryTreeCount * System.Math.PI * 2.0;
                float radius = Mathf.Lerp(46f, 96f, (float)rng.NextDouble());
                float p = centreX + (float)System.Math.Cos(angle) * radius;
                float d = (float)System.Math.Sin(angle) * radius;

                // Пятно самой башни и полоса перед открытой гранью держатся
                // чистыми. Башня — потому что дерево, попавшее в её габарит,
                // прорастает сквозь этажи; полоса — потому что там летают
                // выстрелы, и дерево в кадре Охотника помеха, а не пейзаж.
                bool insideTower = p > -SceneryKeepOut && p < length + SceneryKeepOut
                                   && d > -SceneryKeepOut && d < depth + SceneryKeepOut;
                bool inFiringLane = d < 0f && d > -26f && Mathf.Abs(p - centreX) < 30f;
                if (insideTower || inFiringLane)
                {
                    continue;
                }

                PlaceScenery(root, trees[rng.Next(trees.Length)], p, d,
                    (float)rng.NextDouble() * 360f, Mathf.Lerp(0.8f, 1.5f, (float)rng.NextDouble()));
            }
        }

        /// <summary>Горы по горизонту и облака: без них небо кончается голой линией.</summary>
        private static void BuildHorizon(Transform root, float centreX, float centreZ)
        {
            var rng = new System.Random(Seed ^ 0x1EAF);
            string[] ranges =
            {
                FarmGenerics + "SM_Generic_Mountains_Grass_01.prefab",
                FarmGenerics + "SM_Generic_Mountains_Grass_02.prefab",
                FarmGenerics + "SM_Generic_Mountains_Soft_01.prefab",
                FarmGenerics + "SM_Generic_Mountains_Snow_01.prefab"
            };

            for (int i = 0; i < HorizonCount; i++)
            {
                double angle = i / (double)HorizonCount * System.Math.PI * 2.0;
                float radius = Mathf.Lerp(190f, 250f, (float)rng.NextDouble());
                float p = centreX + (float)System.Math.Cos(angle) * radius;
                float d = centreZ + (float)System.Math.Sin(angle) * radius;
                PlaceScenery(root, ranges[rng.Next(ranges.Length)], p, d,
                    (float)rng.NextDouble() * 360f, Mathf.Lerp(2.5f, 4.5f, (float)rng.NextDouble()), GroundLevel);
            }

            string[] clouds =
            {
                FarmGenerics + "SM_Generic_Cloud_01.prefab",
                FarmGenerics + "SM_Generic_Cloud_02.prefab",
                FarmGenerics + "SM_Generic_Cloud_03.prefab",
                FarmGenerics + "SM_Generic_Cloud_04.prefab",
                FarmGenerics + "SM_Generic_Cloud_05.prefab"
            };

            for (int i = 0; i < CloudCount; i++)
            {
                double angle = i / (double)CloudCount * System.Math.PI * 2.0 + 0.4;
                float radius = Mathf.Lerp(70f, 170f, (float)rng.NextDouble());
                float p = centreX + (float)System.Math.Cos(angle) * radius;
                float d = centreZ + (float)System.Math.Sin(angle) * radius;
                float height = Mathf.Lerp(48f, 78f, (float)rng.NextDouble());
                PlaceScenery(root, clouds[rng.Next(clouds.Length)], p, d,
                    (float)rng.NextDouble() * 360f, Mathf.Lerp(1.5f, 3f, (float)rng.NextDouble()), height);
            }
        }

        /// <summary>
        /// Поставить модель окружения в точку поля. Координаты — в ШП: p вдоль
        /// трассы, d по глубине от открытой грани. Высота по умолчанию — уровень
        /// земли.
        /// </summary>
        private static void PlaceScenery(Transform root, string prefabPath, float p, float d, float yaw, float scale)
        {
            PlaceScenery(root, prefabPath, p, d, yaw, scale, GroundLevel);
        }

        private static void PlaceScenery(Transform root, string prefabPath, float p, float d, float yaw, float scale, float height)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                if (!missingScenery.Contains(prefabPath))
                {
                    missingScenery.Add(prefabPath);
                }

                return;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            go.transform.position = ToWorld(p, height, d);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            AddScenery(go);
        }

        /// <summary>
        /// Пометить объект как декорацию: срезать коллайдеры и включить
        /// статическую пакетную отрисовку. Коллайдеры срезаются у всего
        /// окружения без исключений — за открытой гранью летают выстрелы
        /// Охотника, и любое дерево с коллайдером ловило бы их вместо Уток.
        /// </summary>
        private static void AddScenery(GameObject go)
        {
            var colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Object.DestroyImmediate(colliders[i], true);
            }

            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }

        /// <summary>
        /// Солнечный день из раздела 14 спеки. Настройки держим кодом, а не
        /// инспектором: YAML сцены не переживает слияние веток, и правка
        /// освещения молча теряется — тот же довод, что у формата камеры.
        /// </summary>
        private static void TuneLighting()
        {
            GameObject lighting = GameObject.Find("_Lighting");
            Light sun = lighting == null ? null : lighting.GetComponentInChildren<Light>(true);
            if (sun != null)
            {
                sun.type = LightType.Directional;
                sun.color = SunColor;
                sun.intensity = SunIntensity;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.65f;

                // Солнце светит вдоль открытой грани и сверху-сбоку: так тени
                // укрытий ложатся поперёк коридора и читаются как объём, а не
                // как плоские пятна.
                sun.transform.rotation = Quaternion.Euler(52f, 205f, 0f);
                RenderSettings.sun = sun;
            }

            // Этажи перекрыты плитой сверху — прямого солнца внутри почти нет,
            // и всё, чем видно коридор, это рассеянный свет. При стандартной
            // единице интерьер уходил в тёмно-серое, и укрытия переставали
            // отличаться друг от друга.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;
        }

        /// <summary>
        /// Коробка блокаута, одетая в модель пака. Геометрия и коллайдер —
        /// те же, что были на сером блокауте; меняется только то, что видно.
        /// </summary>
        private static GameObject DressedBox(Transform parent, string name, int layer, Material material,
            float xMin, float xMax, float yMin, float yMax, float zMin, float zMax, DuckHuntDress.Kind dress)
        {
            GameObject box = Box(parent, name, layer, material, xMin, xMax, yMin, yMax, zMin, zMax);
            DuckHuntDress.Apply(box, dress, currentWinter, dressRandom);
            return box;
        }

        // ========== СЛУЖЕБНОЕ ==========

        private static void EnsureMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Materials", "DuckHunt");
            }

            // Цвета сняты пипеткой с атласов паков Synty, а не подобраны на
            // глаз: поверхности башни обязаны сидеть в той же палитре, что и
            // реквизит, иначе стык блокаута и моделей видно на любом кадре.
            floorMaterial = EnsureSurface("DH_Floor", new Color(0.341f, 0.275f, 0.200f),
                "Dirt_Texture_01", "Dirt_Normals_01", FloorTiling);
            floorWinterMaterial = EnsureSurface("DH_FloorWinter", new Color(0.86f, 0.89f, 0.93f),
                "Snow_01", "Snow_01_Normals", FloorTiling);
            // Светлее пипетки с атласа: пипетка даёт средний цвет доски на
            // солнце, а стены этажа стоят в постоянной тени под перекрытием и
            // на этом цвете уходили в чёрное. Тон тот же, светлота выше.
            wallMaterial = EnsureMaterial("DH_Wall", new Color(0.52f, 0.46f, 0.38f));
            metalMaterial = EnsureMaterial("DH_Metal", new Color(0.391f, 0.412f, 0.422f));
            groundMaterial = EnsureSurface("DH_Ground", new Color(0.373f, 0.533f, 0.275f),
                "Grass_Texture_01", "Grass_Normals_01", GroundTiling);
            foundationMaterial = EnsureMaterial("DH_Foundation", new Color(0.52f, 0.50f, 0.46f));

            // Красный амбарный торец у лестничной комнаты — не только про
            // раздел 14.1 спеки. Лестничная комната это единственная точка
            // этажа, куда выстрел не достаёт, и её обязано быть видно с любого
            // места коридора. Сплошной коричневый торец сливался со стенами.
            barnMaterial = EnsureMaterial("DH_Barn", new Color(0.62f, 0.21f, 0.18f));

            // Укрытия и площадки почти везде скрыты моделями. Материал под
            // ними остаётся запасным видом: если пака нет, арена обязана
            // читаться серым блокаутом, а не исчезнуть.
            coverHighMaterial = EnsureMaterial("DH_CoverHigh", new Color(0.688f, 0.595f, 0.428f));
            coverLowMaterial = EnsureMaterial("DH_CoverLow", new Color(0.681f, 0.588f, 0.422f));
            platformMaterial = EnsureMaterial("DH_Platform", new Color(0.456f, 0.372f, 0.269f));
            // Лёд идёт на СНЕЖНОЙ текстуре с голубым подтоном и высокой
            // гладкостью, а не на ледяной. Ледяная текстура пака насыщенно
            // синяя: она рассчитана на маленькую полынью, а здесь ей кроют
            // настил во весь этаж — и зимний этаж читался бассейном, а не
            // наледью. Скользкость всё равно задаёт не материал, а конфиг.
            iceMaterial = EnsureSurface("DH_Ice", new Color(0.82f, 0.90f, 0.97f),
                "Snow_01", "Snow_01_Normals", IceTiling);
            iceMaterial.color = new Color(0.80f, 0.89f, 0.98f);
            iceMaterial.SetFloat(SmoothnessId, 0.75f);
            trapMaterial = EnsureMaterial("DH_Trap", new Color(0.65f, 0.3f, 0.3f));
            buttonReadyMaterial = EnsureMaterial("DH_ButtonReady", new Color(0.9f, 0.2f, 0.2f));
            buttonBusyMaterial = EnsureMaterial("DH_ButtonBusy", new Color(0.3f, 0.3f, 0.3f));
        }

        /// <summary>
        /// Материал с тайловой текстурой пака. Текстура ищется по имени, а не
        /// по пути: паки Synty в репозиторий не кладутся, ставит их каждый сам,
        /// и жёсткий путь развалился бы на чужой машине с другой раскладкой
        /// импорта. Не нашлась — материал остаётся заливкой того же цвета,
        /// и арена читается серым блокаутом, а не исчезает.
        /// </summary>
        private static Material EnsureSurface(string name, Color tint, string albedo, string normal, float tiling)
        {
            Material material = EnsureMaterial(name, tint);
            Texture2D albedoMap = FindTexture(albedo);
            if (albedoMap == null)
            {
                return material;
            }

            material.color = Color.white;
            material.SetTexture(BaseMapId, albedoMap);
            material.SetTextureScale(BaseMapId, new Vector2(tiling, tiling));

            Texture2D normalMap = FindTexture(normal);
            if (normalMap != null)
            {
                material.EnableKeyword("_NORMALMAP");
                material.SetTexture(BumpMapId, normalMap);
                material.SetTextureScale(BumpMapId, new Vector2(tiling, tiling));
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Текстура пака по точному имени ассета. Ничего не найдено — null, это допустимо.</summary>
        private static Texture2D FindTexture(string assetName)
        {
            string[] guids = AssetDatabase.FindAssets($"{assetName} t:Texture2D", SyntyFolders);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == assetName)
                {
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
            }

            return null;
        }

        private static Material EnsureMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                // Цвет переписываем всегда: иначе смена палитры не доезжает
                // до проекта, где материал уже создан прошлой пересборкой,
                // и половина арены остаётся в старых тонах.
                existing.color = color;
                existing.SetTexture(BaseMapId, null);
                existing.DisableKeyword("_NORMALMAP");
                existing.SetTexture(BumpMapId, null);
                EditorUtility.SetDirty(existing);
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
