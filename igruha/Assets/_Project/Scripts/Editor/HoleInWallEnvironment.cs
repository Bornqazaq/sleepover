using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Igruha.Minigames.HoleInWall;
using Entry = Igruha.EditorTools.DressKit.Entry;
using Fit = Igruha.EditorTools.DressKit.Fit;
using Tone = Igruha.EditorTools.HoleInWallPaletteAssets.Tone;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Павильон «Дырки в стене» — подфаза 4.3: интерьер ночного телешоу вокруг
    /// готовой арены, свет студии и Volume с тонмаппингом.
    ///
    /// До неё арена стояла на коричневой земле под дневным небом, и это ломало
    /// брифовое «ночное телешоу» целиком: цвета 4.2 были верными, а читались
    /// неправильно, потому что читается не цвет, а цвет под светом.
    ///
    /// <b>Ни одного коллайдера.</b> Всё, что строит этот файл, — декорация.
    /// Коллайдер здесь ловил бы толчки сметённого со стены игрока и лучи
    /// деокклюдера камеры, то есть менял бы геометрию, выверенную фазами 2–3,
    /// ничего не меняя в самой арене. Слой у всего — <c>Default</c>: он не
    /// входит в маску препятствий камеры (igruha/CLAUDE.md, 2a), поэтому
    /// камера проходит павильон насквозь и не цепляется за трибуну.
    ///
    /// <b>Тени павильон не отбрасывает.</b> Потолок перекрывает арену целиком,
    /// и с включённым отбросом теней он погасил бы направленный свет по всей
    /// площадке: студия ушла бы в один рассеянный серый. Отброс снят у всего
    /// окружения разом — так правило одно и его нельзя забыть на новом куске.
    ///
    /// <b>Запретные зоны.</b> Две, и обе проверяет <see cref="HoleInWallArtAudit"/>:
    /// пятно самой арены до высоты «верх стены плюс запас» — иначе декор
    /// перекрывает вырез, а вырез здесь и есть игра; и полоса перед камерой
    /// за ближним бортом — иначе кадр закрывается тем, что стоит у неё под
    /// носом.
    /// </summary>
    internal static class HoleInWallEnvironment
    {
        private const string StudioRoot = "_Studio";
        private const string LightingRoot = "_Lighting";
        private const string LightingGroup = "HoleInWallStudio";

        // ========== ГАБАРИТЫ ПАВИЛЬОНА, ШП ==========
        //
        // В ширинах персонажа, как и вся геометрия игры: арена задана в них же,
        // и павильон обязан ехать вместе с ней, а не рядом.

        /// <summary>Сколько пола студии лежит вокруг бассейна до стен павильона.</summary>
        private const float ApronWidths = 20f;

        /// <summary>Высота потолка над полом платформы.</summary>
        private const float CeilingWidths = 15f;

        /// <summary>Толщина плит павильона: пола, стен, потолка.</summary>
        private const float ShellWidths = 0.6f;

        /// <summary>Насколько фермы висят ниже потолка.</summary>
        private const float TrussDropWidths = 2f;

        /// <summary>Высота секции фермы. Совпадает с высотой модели пака — повтора по высоте не нужно.</summary>
        private const float TrussHeightWidths = 1.4f;

        /// <summary>Насколько софит висит ниже фермы, на которой закреплён.</summary>
        private const float BarDropWidths = 0.7f;

        /// <summary>
        /// Светодиодный борт по бокам арены: низ и верх относительно пола
        /// платформы.
        ///
        /// Низ лежит на кромке бассейна, верх — чуть выше пола платформы:
        /// это рекламный борт вдоль площадки, а не стена. Первым прогоном он
        /// стоял на три метра выше и загораживал трибуну, ради которой рядом
        /// и поставлен, — «по бокам арены» из брифа значит вдоль неё, а не
        /// вместо неё.
        /// </summary>
        private const float LedBottomWidths = -3f;
        private const float LedTopWidths = 0.9f;

        /// <summary>Задник за табло: высота светодиодной стены дальнего торца.</summary>
        private const float BackdropTopWidths = 10f;

        /// <summary>Насколько панель отставлена наружу от бортика бассейна.</summary>
        private const float LedOffsetWidths = 0.5f;

        /// <summary>Табло: ширина и высота из брифа — 4 × 3 ШП.</summary>
        private const float BoardWidths = 4f;
        private const float BoardHeightWidths = 3f;

        /// <summary>На сколько низ табло поднят над верхом стены.</summary>
        private const float BoardLiftWidths = 1f;

        /// <summary>Насколько ферма табло отставлена за дальний борт бассейна.</summary>
        private const float BoardStandoffWidths = 3.5f;

        // ========== СВЕТ ==========
        //
        // Свет задаётся кодом, а не инспектором, по той же причине, что формат
        // камеры: настройки живут в YAML сцены, YAML не переживает слияние
        // веток, и чужая правка выигрывает молча. Код переживает.

        /// <summary>Ключевой свет студии: холодный белый софит сверху-сзади камеры.</summary>
        private static readonly Color KeyColor = new Color(0.839f, 0.871f, 1f);

        private const float KeyIntensity = 1.15f;

        /// <summary>
        /// Поворот ключевого света. Светит <b>вдоль взгляда камеры</b>, то есть
        /// в сторону +Z, и это не вкус: стена едет на игрока, её лицевая грань
        /// смотрит в −Z, и свет, поставленный «красиво» с дальней стороны,
        /// оставил бы вырез в собственной тени. Силуэт обязан читаться с 30 ШП.
        /// </summary>
        private static readonly Vector3 KeyEuler = new Vector3(52f, 18f, 0f);

        /// <summary>
        /// Рассеянный свет студии тремя тонами. Поднят заметно выше стандартной
        /// единицы: арена перекрыта потолком, прямого света внутрь приходит
        /// мало, и на обычном рассеянном интерьер уходит в тёмно-серое, где
        /// белый пластик платформы перестаёт отличаться от тёмного помоста.
        /// </summary>
        private static readonly Color AmbientSky = new Color32(0x5A, 0x65, 0x88, 0xFF);
        private static readonly Color AmbientEquator = new Color32(0x3A, 0x3F, 0x5C, 0xFF);
        private static readonly Color AmbientGround = new Color32(0x19, 0x1B, 0x24, 0xFF);

        /// <summary>Чем чистится кадр там, где павильон не закрывает. Небо в интерьере не нужно.</summary>
        private static readonly Color VoidColor = new Color32(0x08, 0x08, 0x0C, 0xFF);

        /// <summary>
        /// Яркость софита дорожки.
        ///
        /// Число выглядит диким ровно по двум причинам, и обе физические.
        /// Свет ослабевает обратно квадрату расстояния, а от софита до воды
        /// около десяти метров — это уже сотня. И падает он на дно бассейна
        /// тоном <c>#1A1A20</c>, который отражает около процента: на полутора
        /// сотнях цветное пятно на воде было едва различимо, хотя софит горел.
        /// </summary>
        private const float LaneSpotIntensity = 520f;

        private const float LaneSpotRange = 26f;
        private const float LaneSpotAngle = 46f;
        private const float LaneSpotInnerAngle = 18f;

        // ========== МОДЕЛИ ПАКОВ ==========

        private const string Nightclubs = "Assets/Synty/PolygonNightclubs/Prefabs/Props/";
        private const string Carnival = "Assets/Synty/PolygonHorrorCarnival/Prefabs/Props/";
        private const string Shops = "Assets/Synty/PolygonShops/Prefabs/Props/";

        /// <summary>Секция фермы: из неё набираются и потолочные фермы, и ферма табло.</summary>
        private static readonly Entry Truss = new Entry(Nightclubs + "Modular/SM_Prop_Stage_Frame_01.prefab", Fit.Wall);

        private const string SpotBarPrefab = Nightclubs + "SM_Prop_Light_Spotlight_01.prefab";
        private const string BleacherPrefab = Carnival + "SM_Prop_Bleachers_Straight_01.prefab";
        private const string TripodPrefab = Shops + "SM_Prop_Computer_Camera_Tripod_01.prefab";
        private const string CameraPrefab = Shops + "SM_Prop_Computer_Camera_DSLR_01.prefab";

        /// <summary>Сколько секций трибуны стоит вдоль каждой длинной стороны арены.</summary>
        private const int BleacherCount = 6;

        /// <summary>Верх подиума трибуны над полом платформы, ШП.</summary>
        private const float StandDeckWidths = 1.2f;

        /// <summary>Тёмное небо студии: источник отражений, а не вид.</summary>
        private const string SkyAsset = "Assets/_Project/Materials/HoleInWall/HIW_StudioSky.mat";
        private const string SkyShaderName = "Skybox/Procedural";

        private static readonly Color SkyTint = new Color32(0x3E, 0x46, 0x66, 0xFF);
        private static readonly Color SkyGround = new Color32(0x12, 0x13, 0x1A, 0xFF);

        /// <summary>Профиль Volume: тонмаппинг и bloom студии.</summary>
        private const string VolumeFolder = "Assets/_Project/Settings/Volumes";
        private const string VolumeAsset = VolumeFolder + "/HoleInWall_Studio.asset";

        /// <summary>
        /// Построить павильон вокруг готовой арены и настроить свет.
        /// Вызывается пересборкой арены последним: табло берут дорожки, а они
        /// собираются раньше.
        /// </summary>
        internal static void Build(Transform arena, HoleInWallConfig config, HoleInWallTrack[] tracks,
            System.Random rng)
        {
            var studio = new GameObject(StudioRoot).transform;
            studio.SetParent(arena, false);

            BuildShell(studio, config);
            BuildRigging(studio, config, rng);
            BuildScreens(studio, config);
            BuildStands(studio, config);
            BuildScoreboards(studio, config, tracks, rng);

            TuneLighting(config, studio);
            TunePostProcessing();
        }

        // ========== КОРОБКА ПАВИЛЬОНА ==========

        /// <summary>
        /// Пол студии, стены и потолок.
        ///
        /// <b>Пол — кольцо, а не плита.</b> Сплошная плита на уровне бортика
        /// прошла бы сквозь бассейн и срезала бы ему всю глубину, а с ней и
        /// цену ошибки: игрок обязан видеть, что под ним вода, а не крашеный
        /// пол. Поэтому пол студии идёт четырьмя полосами вокруг бассейна и
        /// внутрь него не заходит.
        ///
        /// Верх пола на два сантиметра ниже верха бортика: совпадающие грани
        /// дерутся за пиксель и мерцают полосами при движении камеры.
        /// </summary>
        private static void BuildShell(Transform parent, HoleInWallConfig config)
        {
            Transform shell = Group(parent, "Shell");

            float unit = config.UnitsPerWidth;
            float apron = ApronWidths * unit;
            float slab = ShellWidths * unit;
            float halfWidth = config.ArenaWidth * 0.5f;
            float floorTop = RimTopY(config) - 0.02f;
            float ceilingY = config.PlatformSurfaceY + CeilingWidths * unit;

            float outerHalfWidth = halfWidth + apron;
            float outerNear = config.ArenaNearZ - apron;
            float outerFar = config.ArenaFarZ + apron;
            float outerDepth = outerFar - outerNear;
            float outerCentreZ = (outerFar + outerNear) * 0.5f;
            float arenaCentreZ = (config.ArenaFarZ + config.ArenaNearZ) * 0.5f;

            Material stage = HoleInWallPaletteAssets.Get(Tone.Stage);

            // Пол: четыре полосы вокруг бассейна.
            Slab(shell, "Floor_Near", new Vector3(outerHalfWidth * 2f, slab, apron),
                new Vector3(0f, floorTop - slab * 0.5f, config.ArenaNearZ - apron * 0.5f), stage);
            Slab(shell, "Floor_Far", new Vector3(outerHalfWidth * 2f, slab, apron),
                new Vector3(0f, floorTop - slab * 0.5f, config.ArenaFarZ + apron * 0.5f), stage);
            Slab(shell, "Floor_Left", new Vector3(apron, slab, config.ArenaDepth),
                new Vector3(-(halfWidth + apron * 0.5f), floorTop - slab * 0.5f, arenaCentreZ), stage);
            Slab(shell, "Floor_Right", new Vector3(apron, slab, config.ArenaDepth),
                new Vector3(halfWidth + apron * 0.5f, floorTop - slab * 0.5f, arenaCentreZ), stage);

            // Стены и потолок: коробка, за которую не видно ни неба, ни пустоты,
            // когда камера обходит игрока по орбите.
            float wallHeight = ceilingY - floorTop;
            float wallCentreY = floorTop + wallHeight * 0.5f;

            Slab(shell, "Wall_Near", new Vector3(outerHalfWidth * 2f, wallHeight, slab),
                new Vector3(0f, wallCentreY, outerNear), stage);
            Slab(shell, "Wall_Far", new Vector3(outerHalfWidth * 2f, wallHeight, slab),
                new Vector3(0f, wallCentreY, outerFar), stage);
            Slab(shell, "Wall_Left", new Vector3(slab, wallHeight, outerDepth),
                new Vector3(-outerHalfWidth, wallCentreY, outerCentreZ), stage);
            Slab(shell, "Wall_Right", new Vector3(slab, wallHeight, outerDepth),
                new Vector3(outerHalfWidth, wallCentreY, outerCentreZ), stage);

            Slab(shell, "Ceiling", new Vector3(outerHalfWidth * 2f, slab, outerDepth),
                new Vector3(0f, ceilingY + slab * 0.5f, outerCentreZ), stage);
        }

        // ========== ФЕРМЫ И СОФИТЫ ==========

        /// <summary>
        /// Потолочные фермы поперёк арены и софиты на них — по одному на
        /// дорожку в каждом ряду.
        ///
        /// <b>Софит светит на воду перед дорожкой, а не на настил.</b> Цветной
        /// свет по белому пластику увёл бы в свой тон обе половины пола сразу,
        /// и «своё место / место партнёра» перестало бы читаться — то есть
        /// подсветка дорожки съела бы то, ради чего она ставится.
        ///
        /// <b>Называет дорожку при этом не он.</b> Вода бирюзовая и красит
        /// падающий на неё свет собственным цветом: янтарь дорожки читается
        /// на ней зелёным, фиолетовый — синим, и четыре дорожки сходятся
        /// в две. Цвет дорожки говорят три вещи с нейтральной поверхностью —
        /// светофильтр этого софита, рамка её табло и светящаяся кромка самой
        /// платформы (<c>HoleInWallArenaBuilder.BuildLaneTrim</c>). Софит
        /// остаётся атмосферой и подтверждением, а не единственным указателем.
        /// </summary>
        private static void BuildRigging(Transform parent, HoleInWallConfig config, System.Random rng)
        {
            Transform rigging = Group(parent, "Rigging");

            float unit = config.UnitsPerWidth;
            float ceilingY = config.PlatformSurfaceY + CeilingWidths * unit;
            float trussY = ceilingY - TrussDropWidths * unit;
            float trussHeight = TrussHeightWidths * unit;
            float trussDepth = ShellWidths * unit;

            // Три ряда: над стартом стены, посередине её пути и над самой
            // платформой. Дальше рядов не нужно — они уходят из кадра.
            float[] rows = { config.WallStartZ, config.WallStartZ * 0.5f, config.CheckLineZ };

            for (int row = 0; row < rows.Length; row++)
            {
                DressedSlab(rigging, $"Truss_{row}",
                    new Vector3(config.ArenaWidth, trussHeight, trussDepth),
                    new Vector3(0f, trussY, rows[row]),
                    Tone.Metal, Truss, rng);
            }

            // Софиты — на двух дальних рядах: над платформой они висели бы
            // прямо над головой и в кадр не попадали бы вовсе.
            Transform bars = Group(parent, "Softboxes");
            float barY = trussY - BarDropWidths * unit;

            for (int track = 0; track < config.TrackCount; track++)
            {
                float x = config.TrackCenterX(track);
                Tone lane = HoleInWallPaletteAssets.LaneTone(track);

                for (int row = 0; row < 2; row++)
                {
                    GameObject bar = HangProp(bars, $"Softbox_{track}_{row}", SpotBarPrefab,
                        new Vector3(x, barY, rows[row]), 0f, Tone.Metal);
                    AddGel(bars, $"Gel_{track}_{row}", bar, x, rows[row], lane);
                }
            }
        }

        /// <summary>
        /// Светофильтр под софитом: цвет дорожки, видимый и когда сам софит
        /// не светит в камеру. Садится ровно под нижнюю грань стойки —
        /// по замеру её рендереров, а не по угаданному отступу.
        /// </summary>
        private static void AddGel(Transform parent, string gelName, GameObject bar, float x, float z, Tone lane)
        {
            if (bar == null || !TryWorldBounds(bar, out Bounds bounds))
            {
                return;
            }

            Slab(parent, gelName, new Vector3(bounds.size.x * 0.9f, 0.08f, bounds.size.z * 0.7f),
                new Vector3(x, bounds.min.y + 0.05f, z), HoleInWallPaletteAssets.Get(lane));
        }

        // ========== ЭКРАНЫ И НЕОН ==========

        /// <summary>
        /// Светодиодные панели по бокам арены и неоновая нитка по верху бортика
        /// бассейна.
        ///
        /// Панель — большая плоскость, и моделями она не одевается: модель,
        /// растянутая на тридцать метров, читается бревном (правило подфазы
        /// 4.1). Светящийся материал палитры на этой длине и есть светодиодная
        /// стена, а не её заменитель.
        ///
        /// Панели стоят снаружи крайних дорожек и ниже пола платформы не
        /// поднимаются выше пояса — вид на соседей они не закрывают, а именно
        /// его бриф защищает отдельным пунктом.
        /// </summary>
        private static void BuildScreens(Transform parent, HoleInWallConfig config)
        {
            Transform screens = Group(parent, "Screens");

            float unit = config.UnitsPerWidth;
            float halfWidth = config.ArenaWidth * 0.5f;
            float centreZ = (config.ArenaFarZ + config.ArenaNearZ) * 0.5f;
            float bottom = config.PlatformSurfaceY + LedBottomWidths * unit;
            float top = config.PlatformSurfaceY + LedTopWidths * unit;
            float height = top - bottom;
            float x = halfWidth + LedOffsetWidths * unit;

            Material led = HoleInWallPaletteAssets.Get(Tone.Led);

            Slab(screens, "Led_Left", new Vector3(0.24f, height, config.ArenaDepth),
                new Vector3(-x, bottom + height * 0.5f, centreZ), led);
            Slab(screens, "Led_Right", new Vector3(0.24f, height, config.ArenaDepth),
                new Vector3(x, bottom + height * 0.5f, centreZ), led);

            // Задник за табло. Дальний торец павильона всегда в кадре — туда
            // смотрит вся игра, — и чёрная пустота там читается не как ночная
            // студия, а как незакрытый край сцены.
            float backdropTop = config.PlatformSurfaceY + BackdropTopWidths * unit;
            float backdropBottom = RimTopY(config);
            float backdropZ = config.ArenaFarZ + (ApronWidths - 0.4f) * unit;

            Slab(screens, "Led_Backdrop",
                new Vector3(config.ArenaWidth + 8f * unit, backdropTop - backdropBottom, 0.24f),
                new Vector3(0f, (backdropTop + backdropBottom) * 0.5f, backdropZ), led);

            // Неоновая нитка по кромке бассейна: она обводит игровое пятно
            // и говорит, где кончается арена, не поднимаясь над полом ни на
            // сантиметр.
            Transform neon = Group(parent, "Neon");
            Material pink = HoleInWallPaletteAssets.Get(Tone.NeonPink);
            float neonY = RimTopY(config) + 0.03f;
            const float NeonThickness = 0.06f;
            const float NeonBand = 0.5f;

            Slab(neon, "Rim_Far", new Vector3(config.ArenaWidth, NeonThickness, NeonBand),
                new Vector3(0f, neonY, config.ArenaFarZ), pink);
            Slab(neon, "Rim_Near", new Vector3(config.ArenaWidth, NeonThickness, NeonBand),
                new Vector3(0f, neonY, config.ArenaNearZ), pink);
            Slab(neon, "Rim_Left", new Vector3(NeonBand, NeonThickness, config.ArenaDepth),
                new Vector3(-halfWidth, neonY, centreZ), pink);
            Slab(neon, "Rim_Right", new Vector3(NeonBand, NeonThickness, config.ArenaDepth),
                new Vector3(halfWidth, neonY, centreZ), pink);
        }

        // ========== ТРИБУНА И ТЕЛЕКАМЕРЫ ==========

        /// <summary>
        /// Трибуна вдоль длинных сторон и телекамеры на штативах.
        ///
        /// Ставятся поштучно, а не рядом копий через <see cref="DressKit"/>:
        /// ряд разворачивает копии через одну на пол-оборота — для бортика и
        /// настила это спасение от «забора из клонов», а для трибуны и камеры
        /// это половина предметов, повёрнутых спиной к арене.
        /// </summary>
        private static void BuildStands(Transform parent, HoleInWallConfig config)
        {
            Transform stands = Group(parent, "Stands");

            float unit = config.UnitsPerWidth;
            float floorY = RimTopY(config) - 0.02f;
            float halfWidth = config.ArenaWidth * 0.5f;

            // Трибуна стоит на подиуме, а не прямо на полу студии: вровень
            // с полом её закрывал бы светодиодный борт, который проходит по
            // кромке бассейна ровно между ней и ареной. Подиум поднимает
            // зрителя над бортом — так, как это и устроено в настоящем зале.
            float standX = halfWidth + 4.5f * unit;
            float step = config.ArenaDepth / (BleacherCount + 1);
            float deckTop = config.PlatformSurfaceY + StandDeckWidths * unit;
            float deckDepth = config.ArenaDepth;
            float deckWidth = 6f * unit;

            Material rim = HoleInWallPaletteAssets.Get(Tone.PoolRim);
            Material led = HoleInWallPaletteAssets.Get(Tone.Led);
            float deckCentreZ = (config.ArenaFarZ + config.ArenaNearZ) * 0.5f;

            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                float x = sign * standX;

                Slab(stands, $"Deck_{side}", new Vector3(deckWidth, deckTop - floorY, deckDepth),
                    new Vector3(x, (deckTop + floorY) * 0.5f, deckCentreZ), rim);

                // Светящаяся кромка подиума: без неё трибуна остаётся чёрным
                // пятном на чёрной стене и в кадре её попросту нет.
                Slab(stands, $"DeckEdge_{side}", new Vector3(0.14f, 0.12f, deckDepth),
                    new Vector3(x - sign * (deckWidth * 0.5f - 0.07f), deckTop + 0.06f, deckCentreZ), led);
            }

            // Трибуна красится тоном бортика, а не тёмным тоном студии:
            // тем же цветом она слилась бы со стеной павильона в одно пятно,
            // а зритель за ареной — часть кадра.
            for (int i = 0; i < BleacherCount; i++)
            {
                float z = config.ArenaNearZ + step * (i + 1);
                SeatProp(stands, $"Bleacher_L_{i}", BleacherPrefab, new Vector3(-standX, deckTop, z), 90f, Tone.PoolRim);
                SeatProp(stands, $"Bleacher_R_{i}", BleacherPrefab, new Vector3(standX, deckTop, z), -90f, Tone.PoolRim);
            }

            // Телекамеры: две по бокам напротив платформ и две за дальним
            // бортом, лицом на игроков. Дальние стоят прямо в кадре, боковые
            // ловят арену, когда камера обходит игрока по орбите.
            // Боковые стоят на переднем крае подиума, перед трибуной: дальше
            // подиум кончается, и камера повисла бы над полом студии — замеры
            // это и поймали.
            float sideX = standX - deckWidth * 0.5f + 0.9f;
            float sideZ = config.CheckLineZ - 1.2f;
            float backZ = config.ArenaFarZ + 6f * unit;

            SeatCamera(stands, "TvCam_L", new Vector3(-sideX, deckTop, sideZ), 90f);
            SeatCamera(stands, "TvCam_R", new Vector3(sideX, deckTop, sideZ), -90f);
            SeatCamera(stands, "TvCam_FarL", new Vector3(-config.TrackPitch * 0.5f, floorY, backZ), 180f);
            SeatCamera(stands, "TvCam_FarR", new Vector3(config.TrackPitch * 0.5f, floorY, backZ), 180f);
        }

        /// <summary>
        /// Телекамера — штатив плюс камера на его верхней грани по замеру.
        ///
        /// Обе части лежат под общим узлом, и это не про порядок в иерархии:
        /// замеры проверяют, что предмет на площадке стоит на полу, а камера
        /// стоит на штативе. Отдельным предметом она честно числилась бы
        /// висящей в воздухе — и замер, который срабатывает на правильном,
        /// перестают читать.
        /// </summary>
        private static void SeatCamera(Transform parent, string cameraName, Vector3 ground, float yaw)
        {
            Transform mount = Group(parent, cameraName);

            GameObject tripod = SeatProp(mount, cameraName + "_Tripod", TripodPrefab, ground, yaw, Tone.Metal);
            if (tripod == null || !TryWorldBounds(tripod, out Bounds bounds))
            {
                return;
            }

            SeatProp(mount, cameraName + "_Body", CameraPrefab,
                new Vector3(ground.x, bounds.max.y, ground.z), yaw, Tone.Stage);
        }

        // ========== ТАБЛО ==========

        /// <summary>
        /// Табло дорожки на собственной ферме: номер стены и счёт пары.
        ///
        /// <b>Стоит за дальним бортом, а не сбоку от дорожки, и это вынужденно.</b>
        /// Бриф просит табло сбоку, но два его же запрета этого не оставляют:
        /// в промежуток между дорожками (3 ШП = <see cref="HoleInWallConfig.TrackGap"/>)
        /// нельзя ставить ничего — соседей обязано быть видно, — а табло по
        /// брифу 4 ШП шириной и в этот промежуток не помещается физически,
        /// свесившись на обе соседние дорожки. Над самой дорожкой его тоже
        /// нельзя: между игроком и стеной не должно быть ничего выше пола
        /// платформы. Остаётся торец: табло стоит прямо по оси своей дорожки
        /// за стартовой позицией стены и низом поднято выше её верха, поэтому
        /// не закрывает вырез ни на каком участке пути и при этом всегда
        /// в кадре — а «в кадре» бриф как раз и требует.
        /// </summary>
        private static void BuildScoreboards(Transform parent, HoleInWallConfig config, HoleInWallTrack[] tracks,
            System.Random rng)
        {
            Transform boards = Group(parent, "Scoreboards");

            var game = Object.FindFirstObjectByType<HoleInWallMinigame>(FindObjectsInactive.Include);
            if (game == null)
            {
                Debug.LogWarning("В сцене нет HoleInWallMinigame — табло дорожек будут пустыми");
            }

            float unit = config.UnitsPerWidth;
            float width = BoardWidths * unit;
            float height = BoardHeightWidths * unit;
            float bottom = config.PlatformSurfaceY + config.WallHeight + BoardLiftWidths * unit;
            float centreY = bottom + height * 0.5f;
            float z = config.ArenaFarZ + BoardStandoffWidths * unit;
            float floorY = RimTopY(config) - 0.02f;

            Material stage = HoleInWallPaletteAssets.Get(Tone.Stage);
            Material metal = HoleInWallPaletteAssets.Get(Tone.Metal);

            for (int track = 0; track < config.TrackCount; track++)
            {
                float x = config.TrackCenterX(track);
                Tone lane = HoleInWallPaletteAssets.LaneTone(track);

                var root = new GameObject($"Scoreboard_{track}");
                root.transform.SetParent(boards, false);

                // Ферма: две стойки от пола студии и перекладина над табло.
                float legTop = centreY + height * 0.5f + 1.1f;
                float legHeight = legTop - floorY;
                float legX = width * 0.5f + 0.3f;

                Slab(root.transform, "Leg_L", new Vector3(0.22f, legHeight, 0.22f),
                    new Vector3(x - legX, floorY + legHeight * 0.5f, z), metal);
                Slab(root.transform, "Leg_R", new Vector3(0.22f, legHeight, 0.22f),
                    new Vector3(x + legX, floorY + legHeight * 0.5f, z), metal);

                DressedSlab(root.transform, "Crossbar",
                    new Vector3(width + 1.2f, TrussHeightWidths * unit, ShellWidths * unit),
                    new Vector3(x, legTop - TrussHeightWidths * unit * 0.5f, z), Tone.Metal, Truss, rng);

                // Рамка сзади крупнее плиты: её кромка обводит табло цветом
                // дорожки, а сама плита остаётся тёмной, чтобы цифры читались.
                Slab(root.transform, "Frame", new Vector3(width + 0.2f, height + 0.2f, 0.1f),
                    new Vector3(x, centreY, z), HoleInWallPaletteAssets.Get(lane));
                Slab(root.transform, "Panel", new Vector3(width, height, 0.16f),
                    new Vector3(x, centreY, z - 0.04f), stage);

                float faceZ = z - 0.14f;
                // Размеры шрифта — под чтение с дорожки, а не под вид в
                // инспекторе: до табло от игрока около двадцати пяти метров,
                // и первым прогоном строка вышла тонким штрихом в пару
                // пикселей.
                TextMeshPro wallLine = BoardLine(root.transform, "WallLine",
                    new Vector3(x, centreY + height * 0.33f, faceZ), new Vector2(width - 0.2f, height * 0.34f),
                    6.5f, HoleInWallPalette.Plastic);
                TextMeshPro scoreLine = BoardLine(root.transform, "ScoreLine",
                    new Vector3(x, centreY - height * 0.19f, faceZ), new Vector2(width - 0.2f, height * 0.62f),
                    24f, HoleInWallPalette.LaneAccent(track));

                WireScoreboard(root, game, track < tracks.Length ? tracks[track] : null, wallLine, scoreLine);
            }
        }

        /// <summary>
        /// Строка табло.
        ///
        /// ⚠️ <b>Без разворота на 180°.</b> Текст TextMeshPro в мире изначально
        /// повёрнут лицом к тому, кто смотрит вдоль +Z, — а камера здесь стоит
        /// со стороны бассейна и смотрит именно туда. Разворот «лицом к камере»
        /// её же и отзеркаливает: первый прогон дал «8/6 АНЕТС».
        /// </summary>
        private static TextMeshPro BoardLine(Transform parent, string lineName, Vector3 position, Vector2 size,
            float fontSize, Color color)
        {
            var go = new GameObject(lineName);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.identity;

            var text = go.AddComponent<TextMeshPro>();
            text.text = "—";
            text.fontSize = fontSize;
            text.enableAutoSizing = false;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta = size;

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            go.layer = LayerMask.NameToLayer("Default");
            return text;
        }

        private static void WireScoreboard(GameObject root, HoleInWallMinigame game, HoleInWallTrack track,
            TextMeshPro wallLine, TextMeshPro scoreLine)
        {
            var board = root.AddComponent<HoleInWallScoreboard>();
            var so = new SerializedObject(board);
            so.FindProperty("game").objectReferenceValue = game;
            so.FindProperty("track").objectReferenceValue = track;
            so.FindProperty("wallLine").objectReferenceValue = wallLine;
            so.FindProperty("scoreLine").objectReferenceValue = scoreLine;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ========== СВЕТ ==========

        /// <summary>
        /// Свет студии: ключевой направленный, рассеянный тремя тонами и по
        /// софиту на дорожку.
        ///
        /// <b>Дополнительных источников ровно четыре, и это предел, а не
        /// округление.</b> URP-ассет проекта разрешает объекту принять четыре
        /// дополнительных света (<c>additionalLightsPerObjectLimit</c>), а вода
        /// бассейна — один меш на всю арену, то есть один объект. Пятый свет
        /// на нём просто не отрисуется, и одна из дорожек молча потеряла бы
        /// свой цвет. Поэтому подсветки-заливки здесь нет: её работу делает
        /// поднятый рассеянный.
        /// </summary>
        private static void TuneLighting(HoleInWallConfig config, Transform studio)
        {
            GameObject lightingRoot = GameObject.Find(LightingRoot);
            Light key = lightingRoot == null ? null : lightingRoot.GetComponentInChildren<Light>(true);

            if (key != null)
            {
                key.type = LightType.Directional;
                key.color = KeyColor;
                key.intensity = KeyIntensity;
                key.shadows = LightShadows.Soft;
                key.shadowStrength = 0.72f;
                key.transform.rotation = Quaternion.Euler(KeyEuler);
                RenderSettings.sun = key;
            }
            else
            {
                Debug.LogWarning($"В сцене нет источника света под {LightingRoot} — ключевой свет студии не настроен");
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;
            RenderSettings.fog = false;

            // Небо дневное здесь не годится, но и убирать его совсем нельзя.
            // Без источника отражений металл рендерится чёрным — отражать ему
            // нечего, — и лесенка, штативы телекамер и сталь ферм превращались
            // в силуэты без объёма. Поэтому небо остаётся, но своё: тёмное,
            // из которого павильон и берёт слабый холодный отблеск.
            //
            // В кадр оно при этом не попадает: камера чистится сплошным цветом
            // (см. TunePostProcessing), а арену со всех сторон закрывает
            // коробка павильона.
            RenderSettings.skybox = EnsureStudioSky();
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.customReflectionTexture = null;
            DynamicGI.UpdateEnvironment();

            // Группа заводится ОДИН раз и передаётся дальше. Пересоздавать её
            // в каждом методе нельзя: второй вызов снёс бы то, что положил
            // первый, — ровно так четыре софита пропали из первого прогона,
            // а Volume, шедший вторым, остался и создал видимость работы.
            Transform group = ResetLightingGroup(lightingRoot, studio);
            BuildLaneSpots(config, group);
            EnsureVolume(group);
        }

        /// <summary>
        /// Софит дорожки: цветной луч сверху на воду перед платформой.
        /// Стоит там же, где висит его стойка, — свет обязан идти из того, что
        /// видно, иначе кадр врёт.
        /// </summary>
        private static void BuildLaneSpots(HoleInWallConfig config, Transform parent)
        {
            float unit = config.UnitsPerWidth;
            float ceilingY = config.PlatformSurfaceY + CeilingWidths * unit;
            float y = ceilingY - (TrussDropWidths + BarDropWidths) * unit - 1.6f;
            float z = config.WallStartZ * 0.5f;

            for (int track = 0; track < config.TrackCount; track++)
            {
                float x = config.TrackCenterX(track);
                var go = new GameObject($"LaneSpot_{track}");
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(x, y, z);

                var target = new Vector3(x, config.WaterSurfaceY, z - config.PlatformDepth * 0.5f);
                go.transform.rotation = Quaternion.LookRotation(target - go.transform.position, Vector3.up);

                var light = go.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = HoleInWallPalette.LaneAccent(track);
                light.intensity = LaneSpotIntensity;
                light.range = LaneSpotRange;
                light.spotAngle = LaneSpotAngle;
                light.innerSpotAngle = LaneSpotInnerAngle;

                // Тени софиту не нужны: он светит на воду, где отбрасывать
                // нечему, а карта теней на каждый из четырёх — плата ни за что.
                light.shadows = LightShadows.None;
            }
        }

        // ========== ТОНМАППИНГ И BLOOM ==========

        /// <summary>
        /// Volume студии и постобработка на камере.
        ///
        /// Ради него и затевалась половина подфазы: до тонмаппинга всё ярче
        /// единицы URP срезал по каналам, неон обоих цветов выбивало в белое,
        /// и <see cref="HoleInWallPalette.NeonEmission"/> упирался в единицу.
        /// </summary>
        private static void EnsureVolume(Transform parent)
        {
            VolumeProfile profile = EnsureProfile();
            if (profile == null)
            {
                return;
            }

            var go = new GameObject("Studio Volume");
            go.transform.SetParent(parent, false);

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = profile;
        }

        private static VolumeProfile EnsureProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeAsset);
            if (profile == null)
            {
                if (!AssetDatabase.IsValidFolder(VolumeFolder))
                {
                    AssetDatabase.CreateFolder("Assets/_Project/Settings", "Volumes");
                }

                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeAsset);
            }

            // Neutral, а не ACES: ACES вытягивает контраст и уводит насыщенность
            // из ярких мест, а именно яркое место здесь и обязано остаться
            // цветным — контур выреза читается и силуэтом, и цветом.
            if (!profile.TryGet(out Tonemapping tonemapping))
            {
                tonemapping = profile.Add<Tonemapping>();
            }

            tonemapping.active = true;
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.Neutral;

            if (!profile.TryGet(out Bloom bloom))
            {
                bloom = profile.Add<Bloom>();
            }

            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.9f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.55f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.68f;
            bloom.highQualityFiltering.overrideState = true;
            bloom.highQualityFiltering.value = true;

            if (!profile.TryGet(out ColorAdjustments grading))
            {
                grading = profile.Add<ColorAdjustments>();
            }

            grading.active = true;
            grading.postExposure.overrideState = true;
            grading.postExposure.value = 0.2f;
            grading.contrast.overrideState = true;
            grading.contrast.value = 10f;
            grading.saturation.overrideState = true;
            grading.saturation.value = 6f;

            // Виньетка держит взгляд на своей дорожке: дорожек четыре, и края
            // кадра заняты чужими.
            if (!profile.TryGet(out Vignette vignette))
            {
                vignette = profile.Add<Vignette>();
            }

            vignette.active = true;
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.3f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.4f;

            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>
        /// Включить постобработку на камере сцены и погасить ей небо.
        ///
        /// ⚠️ Правится <b>камера сцены</b>, а не риг: <c>PartyCameraRig</c>
        /// заморожен (igruha/CLAUDE.md, раздел 0), и ни его дистанции, ни
        /// деокклюдера, ни маски здесь никто не касается. Постобработка —
        /// свойство кадра этой сцены, а не формата камеры.
        /// </summary>
        private static void TunePostProcessing()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("В сцене не найдена камера с тегом MainCamera — тонмаппинг и bloom не включатся");
                return;
            }

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = VoidColor;

            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(data);

            VerifyGradingMode();
        }

        /// <summary>
        /// Проверить, что URP-ассет считает цветокоррекцию в HDR.
        ///
        /// В режиме LDR тонмаппинг здесь бесполезен: шейдер <c>UberPost</c>
        /// зажимает цвет в <c>saturate</c> ДО обращения к таблице, то есть
        /// срезает по каналам ровно так же, как срезал без Volume вовсе, —
        /// и неон снова выбьет в белое. Ассет общий на проект, поэтому здесь
        /// только проверка: молча переписывать чужие настройки рендера из
        /// сборщика одной мини-игры нельзя.
        /// </summary>
        private static void VerifyGradingMode()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null)
            {
                Debug.LogWarning("Активен не URP-ассет — тонмаппинг «Дырки в стене» проверить нечем");
                return;
            }

            if (pipeline.colorGradingMode == ColorGradingMode.HighDynamicRange)
            {
                return;
            }

            Debug.LogWarning(
                $"URP-ассет '{pipeline.name}' считает цветокоррекцию в LDR: тонмаппинг зажмёт цвет до таблицы, " +
                "и неон контура снова выбьет в белое. Поставить Color Grading Mode = High Dynamic Range.",
                pipeline);
        }

        // ========== ОБЩЕЕ ==========

        /// <summary>
        /// Тёмное небо студии. Нужно не для вида — его не видно, — а для
        /// отражений: без источника окружения металл в URP чёрный.
        /// </summary>
        private static Material EnsureStudioSky()
        {
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyAsset);
            if (sky == null)
            {
                Shader shader = Shader.Find(SkyShaderName);
                if (shader == null)
                {
                    Debug.LogWarning($"Шейдер '{SkyShaderName}' не найден — металл студии останется чёрным");
                    return null;
                }

                sky = new Material(shader) { name = "HIW_StudioSky" };
                AssetDatabase.CreateAsset(sky, SkyAsset);
            }

            // Солнечного диска нет: в павильоне неоткуда взяться солнцу, а его
            // блик читался бы бельмом на каждой глянцевой плите.
            sky.SetFloat("_SunDisk", 0f);
            sky.SetFloat("_AtmosphereThickness", 0.45f);
            sky.SetFloat("_Exposure", 0.32f);
            sky.SetColor("_SkyTint", SkyTint);
            sky.SetColor("_GroundColor", SkyGround);
            EditorUtility.SetDirty(sky);
            return sky;
        }

        /// <summary>Верх бортика бассейна: по нему выложен пол студии.</summary>
        private static float RimTopY(HoleInWallConfig config) =>
            config.PoolBottomY + config.PoolDepth + HoleInWallArenaBuilder.PoolRimHeight;

        private static Transform Group(Transform parent, string groupName)
        {
            var group = new GameObject(groupName).transform;
            group.SetParent(parent, false);
            return group;
        }

        /// <summary>
        /// Группа света студии под <c>_Lighting</c>. Сносится и заводится
        /// заново каждой пересборкой: свет — такая же часть арта, и оставшийся
        /// от прошлого прогона софит светил бы в пустоту.
        /// </summary>
        private static Transform ResetLightingGroup(GameObject lightingRoot, Transform studio)
        {
            if (lightingRoot == null)
            {
                return studio;
            }

            Transform existing = lightingRoot.transform.Find(LightingGroup);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            return Group(lightingRoot.transform, LightingGroup);
        }

        /// <summary>
        /// Плита окружения: без коллайдера, на <c>Default</c> и без отброса
        /// теней. Все три свойства обязательны, и ни одно из них не косметика —
        /// разбор в шапке файла.
        /// </summary>
        private static GameObject Slab(Transform parent, string slabName, Vector3 size, Vector3 centre,
            Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = slabName;
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.transform.localScale = size;

            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
            MarkAsScenery(go);
            return go;
        }

        /// <summary>Плита окружения, одетая моделью пака через общий <see cref="DressKit"/>.</summary>
        private static void DressedSlab(Transform parent, string slabName, Vector3 size, Vector3 centre,
            Tone paint, Entry entry, System.Random rng)
        {
            GameObject box = Slab(parent, slabName, size, centre, HoleInWallPaletteAssets.Get(paint));
            GameObject dress = DressKit.Apply(box, new[] { entry }, rng, HoleInWallPaletteAssets.Get(paint));
            if (dress != null)
            {
                MarkAsScenery(dress);
            }
        }

        /// <summary>Предмет, подвешенный за свою точку крепления: софит на ферме.</summary>
        private static GameObject HangProp(Transform parent, string propName, string prefabPath, Vector3 position,
            float yaw, Tone paint)
        {
            GameObject go = SpawnProp(parent, propName, prefabPath, yaw, paint);
            if (go != null)
            {
                go.transform.position = position;
            }

            return go;
        }

        /// <summary>
        /// Предмет, поставленный на пол: нижняя грань его габарита садится
        /// ровно на заданную высоту, центр — в заданную точку по горизонтали.
        /// Замером, а не отступом на глаз: у моделей паков опорная точка стоит
        /// то в центре, то в основании, и предмет, поставленный по опорной
        /// точке, у половины моделей повисает в воздухе.
        /// </summary>
        private static GameObject SeatProp(Transform parent, string propName, string prefabPath, Vector3 ground,
            float yaw, Tone paint)
        {
            GameObject go = SpawnProp(parent, propName, prefabPath, yaw, paint);
            if (go == null || !TryWorldBounds(go, out Bounds bounds))
            {
                return go;
            }

            Vector3 shift = new Vector3(ground.x - bounds.center.x, ground.y - bounds.min.y, ground.z - bounds.center.z);
            go.transform.position += shift;
            return go;
        }

        /// <summary>
        /// Поставить модель пака и перекрасить её тоном палитры.
        ///
        /// Перекраска здесь не украшательство: трибуна приезжает из «Карнавала»
        /// в ярмарочной раскраске, штатив из «Магазинов» — в своей, и в тёмной
        /// студии каждый такой предмет кричал бы громче арены. Правило то же,
        /// что у дресса на 4.2: цвет в кадре назначает палитра, а не атлас
        /// пака, из которого предмет приехал.
        /// </summary>
        private static GameObject SpawnProp(Transform parent, string propName, string prefabPath, float yaw,
            Tone paint)
        {
            if (!DressKit.TryLoad(prefabPath, out GameObject prefab))
            {
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = propName;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            MarkAsScenery(go);
            DressKit.Repaint(go, HoleInWallPaletteAssets.Get(paint));
            return go;
        }

        /// <summary>Габарит предмета по его рендерерам, в мировых координатах.</summary>
        private static bool TryWorldBounds(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds();
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return false;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        /// <summary>
        /// Пометить предмет декорацией: снять коллайдеры, увести на
        /// <c>Default</c>, погасить отброс теней и включить пакетную отрисовку.
        /// </summary>
        private static void MarkAsScenery(GameObject go)
        {
            var colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Object.DestroyImmediate(colliders[i], true);
            }

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = ShadowCastingMode.Off;
            }

            int layer = LayerMask.NameToLayer("Default");
            SetLayer(go, layer);

            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayer(child.gameObject, layer);
            }
        }
    }
}
