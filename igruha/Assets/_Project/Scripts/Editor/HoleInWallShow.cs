using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.HoleInWall;
using Tone = Igruha.EditorTools.HoleInWallPaletteAssets.Tone;
using static Igruha.EditorTools.HoleInWallProps;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Оформление шоу «Дырки в стене»: подвесной куб, гирлянды, логотип на
    /// заднике и мелочь площадки — конфетти-пушки, дым-машины, вышка,
    /// кофры и флаги.
    ///
    /// <b>Появилось 08.09 по разбору кадра.</b> Замечание геймдизайнера —
    /// «очень много пустого пространства» — при разборе распалось на три
    /// разные пустоты, и каждая лечится своим:
    ///
    /// <list type="table">
    /// <item><term>Верх кадра</term><description>от ферм (9.4 м) до потолка
    /// (10.8 м) и вся полоса над стеной были чёрными — примерно треть экрана
    /// ни о чём. Закрывается подвесным кубом, гирляндами и шарами: они висят
    /// <b>выше запретной зоны</b> (4.2 м), поэтому вырезу не мешают ничем,
    /// а кадр держат.</description></item>
    /// <item><term>Дальний торец</term><description>за стенами стояла ровная
    /// светодиодная плоскость. Теперь на ней логотип шоу, а перед ней —
    /// амфитеатр <see cref="HoleInWallStands"/>.</description></item>
    /// <item><term>Пол вокруг воды</term><description>по девять метров голого
    /// пола на сторону и четырнадцать на торцах. Часть съели пояса трибун,
    /// остальное — вышка, кофры, столбы с гирляндами и арка входа.</description></item>
    /// </list>
    ///
    /// <b>Ни одного нового партикла, и это решение.</b> Правило подфазы 4.4
    /// требует, чтобы верх постоянного эффекта лежал ниже пола платформы,
    /// а замер эффектов считает только группу <c>_Effects</c>. Конфетти,
    /// висящее над ареной, обошло бы и правило, и замер разом — то есть
    /// сломало бы ровно ту проверку, которая ловит перекрытый вырез. Оформление
    /// здесь целиком из геометрии; событийные эффекты остаются за 4.4.
    /// </summary>
    /// <remarks>
    /// <b>Куб висит выше запретной зоны, но ниже ферм.</b> Оба предела
    /// проверяются числом: низ куба — <see cref="CubeCentreY"/> минус половина
    /// высоты — обязан быть выше 4.2 м (иначе он в зоне арены и перекрывает
    /// вырез), верх — ниже фермы на 9.4 м (иначе он въезжает в неё).
    ///
    /// <b>Правила павильона держит <c>Strip</c> декора:</b> коллайдеров нет,
    /// слой <c>Default</c>, тени не отбрасываются. Свет здесь не ставится
    /// вовсе — <c>Strip</c> его снимает, и это правильно: четыре
    /// дополнительных источника арены уже заняты софитами дорожек.
    /// </remarks>
    internal static class HoleInWallShow
    {
        private const string ShowGroup = "Show";

        private const string Nightclubs = "Assets/Synty/PolygonNightclubs/Prefabs/Props/";
        private const string Carnival = "Assets/Synty/PolygonHorrorCarnival/Prefabs/Props/";
        private const string Shops = "Assets/Synty/PolygonShops/Prefabs/";
        private const string Construction = "Assets/Synty/PolygonConstruction/Prefabs/Props/";

        private const string DiscoBall = Nightclubs + "SM_Prop_Disco_Ball_01.prefab";
        private const string SmokeMachine = Nightclubs + "SM_Prop_Smoke_Machine_01.prefab";
        private const string SpeakerTall = Nightclubs + "SM_Prop_Speaker_Large_02.prefab";
        private const string Bunting = Carnival + "SM_Prop_Flags_05.prefab";
        private const string BuntingPole = Carnival + "SM_Prop_Bunting_Pole_01.prefab";
        private const string ConfettiCannon = Carnival + "SM_Prop_Confetti_Cannon_01.prefab";
        private const string DiveTower = Carnival + "SM_Prop_Dive_Tower_Pool_01.prefab";
        private const string Entrance = Carnival + "SM_Prop_Carnival_Entrance_01.prefab";
        private const string WallFlag = Carnival + "SM_Prop_Flag_01.prefab";
        private const string CanvasBanner = Shops + "Signs/SM_Sign_Canvas_Banner_03.prefab";
        private static readonly string[] Crates =
        {
            Construction + "SM_Prop_Crate_01.prefab",
            Construction + "SM_Prop_Crate_02.prefab"
        };

        // ========== ПОДВЕСНЫЕ ЭКРАНЫ ==========

        /// <summary>
        /// Высота центра экранов над полом платформы, м.
        ///
        /// Оба предела посчитаны, а не подобраны: низ экрана встаёт на 5.6 м
        /// при потолке запретной зоны 4.2 м, верх — на 8.8 м при ферме
        /// на 9.4 м. Опустить ниже нельзя (перекроет вырез), поднять выше
        /// некуда (упрётся в ферму).
        /// </summary>
        private const float CubeCentreY = 7.2f;

        /// <summary>Полувысота и полуширина экрана, м.</summary>
        private const float CubeHalfHeight = 1.6f;
        private const float CubeHalfWidth = 3.0f;

        /// <summary>Полуглубина экрана: он читается коробом, а не листом.</summary>
        private const float CubeHalfDepth = 1.2f;

        /// <summary>Где экраны висят по глубине: над серединой пути стены.</summary>
        private const float CubeZ = 14f;

        /// <summary>
        /// Насколько экраны разведены от оси арены, м.
        ///
        /// ⚠️ <b>Один экран по оси не годится, хотя так и было сделано
        /// первым прогоном.</b> Куб над серединой закрывает собой дальний
        /// торец — то есть задник, логотип и верхние ряды амфитеатра, ради
        /// которых торец и оформляли. Два экрана по сторонам держат верх
        /// кадра ровно так же, а середину оставляют пустой.
        /// </summary>
        private const float CubeOffsetX = 18.5f;

        /// <summary>Разворот экрана внутрь, к середине арены, градусов.</summary>
        private const float CubeYaw = 22f;

        /// <summary>Толщина экрана и рамки, м.</summary>
        private const float CubeSkin = 0.18f;

        // ========== ПОТОЛОК ==========

        /// <summary>Высота светящихся полос потолка над полом платформы, м.</summary>
        private const float GridY = 10.1f;

        /// <summary>Шаг полос по глубине и их ширина, м.</summary>
        private const float GridStep = 4.2f;
        private const float GridWidth = 0.34f;

        /// <summary>Длина подвесного полотнища и на сколько оно свисает с потолка, м.</summary>
        private const float DropBannerWidth = 2.6f;
        private const float DropBannerHeight = 5.2f;

        /// <summary>По каким Z свисают полотнища у стен павильона.</summary>
        private static readonly float[] DropBannerZ = { -12f, -1f, 10f, 21f, 31f };

        // ========== ГИРЛЯНДЫ И ШАРЫ ==========

        /// <summary>Высота гирлянд над полом платформы, м. Между запретной зоной и кубом.</summary>
        private const float BuntingY = 7.8f;

        /// <summary>Шаг гирлянд по глубине и по ширине, м. Ширина — длина модели без нахлёста.</summary>
        private const float BuntingStepZ = 6.4f;
        private const float BuntingStepX = 9.6f;

        /// <summary>Сколько рядов гирлянд и сколько штук в ряду.</summary>
        private const int BuntingRows = 3;
        private const int BuntingPerRow = 3;

        /// <summary>Высота центра шара и во сколько раз он крупнее модели пака.</summary>
        private const float BallY = 6.9f;
        private const float BallScale = 1.7f;

        /// <summary>Куда шар подвешен: до этой высоты идёт его штанга.</summary>
        private const float BallMountY = 9.2f;

        // ========== ЛОГОТИП ==========

        /// <summary>Название шоу на заднике дальнего торца.</summary>
        private const string ShowTitle = "ДЫРКА В СТЕНЕ";

        /// <summary>
        /// Высота центра вывески и кегль названия.
        ///
        /// ⚠️ <b>Выше задника, а не на нём.</b> Первым прогоном логотип лежал
        /// на плоскости задника на 6.1 м — и его закрывали разом табло дорожек
        /// (4.2…6.4 м), гирлянды и верхние ряды амфитеатра. Над задником, чей
        /// верх на 7.2 м, свободна вся полоса до фермы: там вывеску не
        /// перекрывает ничто, и заодно она закрывает собой ту самую чёрную
        /// полосу под потолком дальнего торца.
        /// </summary>
        private const float TitleY = 8.5f;
        private const float TitleFontSize = 30f;

        /// <summary>Полоса, продолжающая задник вверх: высота центра и толщина, м.</summary>
        private const float UpperBandY = 8.9f;
        private const float UpperBandHeight = 3.4f;

        /// <summary>Габарит плиты вывески, м.</summary>
        private const float TitlePlateWidth = 30f;
        private const float TitlePlateHeight = 2.9f;

        /// <summary>
        /// Насколько вывеска вынесена вперёд от плоскости задника, м.
        ///
        /// Задник — плита толщиной 0.24 м, и её передняя грань лежит на
        /// 37.03. Рамка вывески при отступе 0.4 попадала внутрь этой толщи
        /// и дралась с ней за пиксель: в кадре это мерцающие полосы при
        /// каждом движении камеры.
        /// </summary>
        private const float TitleStandoff = 0.7f;

        /// <summary>Габарит поля логотипа, м.</summary>
        private static readonly Vector2 TitleBox = new Vector2(29f, 2.8f);

        /// <summary>Полоса неона над и под логотипом: она и держит его на плоскости задника.</summary>
        private const float TitleRuleWidth = 29f;
        private const float TitleRuleThickness = 0.16f;
        private const float TitleRuleGap = 1.6f;

        // ========== ПЛОЩАДКА ==========

        /// <summary>Насколько реквизит кромки вынесен наружу от края бассейна, м.</summary>
        private const float RimOut = 0.75f;

        /// <summary>По каким Z стоят конфетти-пушки и дым-машины на кромке.</summary>
        private static readonly float[] CannonZ = { -1.5f, 5.5f, 12.5f, 19.5f };
        private static readonly float[] SmokeZ = { 1.5f, 16f };

        /// <summary>Во сколько раз пушка крупнее модели пака: своим ростом 0.63 м она теряется.</summary>
        private const float CannonScale = 1.7f;

        /// <summary>Ось полосы у стены павильона, где стоят кофры и колонки, м от центра.</summary>
        private const float BackLaneX = 33.4f;

        /// <summary>По каким Z стоят кофры в полосе у стены.</summary>
        private static readonly float[] CrateZ = { -4f, 3f, 11f, 19f };

        /// <summary>По каким Z стоят колонки в полосе у стены.</summary>
        private static readonly float[] StackZ = { -0.5f, 15f };

        /// <summary>Высота центра флага на стене павильона и шаг по глубине, м.</summary>
        private const float WallFlagY = 5.4f;
        private static readonly float[] WallFlagZ = { -6f, 4f, 14f };

        /// <summary>Насколько флаг отставлен от плоскости стены внутрь, м.</summary>
        private const float WallFlagStandoff = 0.6f;

        /// <summary>Где стоит вышка спасателя: по бокам, перед ближним бортом.</summary>
        private const float TowerX = 25.5f;
        private const float TowerZ = -11.5f;

        /// <summary>Где стоят арки входа: по краям ближнего торца.</summary>
        private const float EntranceZ = -16.5f;
        private const float EntranceX = 25f;

        /// <summary>Задник ближнего торца: верх и низ относительно пола платформы, м.</summary>
        private const float NearBackdropTop = 8.2f;

        /// <summary>Насколько задник ближнего торца отставлен внутрь от стены павильона, м.</summary>
        private const float NearBackdropInset = 0.28f;

        /// <summary>Высота, шаг и число баннеров на ближнем заднике, м.</summary>
        private const float NearBannerY = 5.6f;
        private const float NearBannerStep = 7.5f;
        private const int NearBannerCount = 7;

        /// <summary>Столбы с гирляндами по углам павильона.</summary>
        private const float PoleX = 32f;
        private static readonly float[] PoleZ = { -17f, 33f };

        /// <summary>
        /// Собрать оформление. Возвращает число поставленных предметов —
        /// для отчёта и приёмки.
        /// </summary>
        internal static int Build(Transform decor, HoleInWallConfig config, System.Random rng)
        {
            Transform show = Group(decor, ShowGroup);

            BuildJumbotron(show, config);
            BuildCeiling(show, config);
            BuildBunting(show, config);
            BuildBalls(show);
            BuildTitle(show, config);
            BuildRimProps(show, config, rng);
            BuildBackLane(show, config, rng);
            BuildLandmarks(show, config, rng);

            return show.GetComponentsInChildren<Renderer>(true).Length;
        }

        // ========== ПОДВЕСНЫЕ ЭКРАНЫ ==========

        /// <summary>
        /// Два экрана над ареной: светодиодные короба в металлической рамке,
        /// подвешенные к потолку тросами и развёрнутые к середине.
        ///
        /// <b>Самые крупные вещи в верхней половине кадра, и их там нужно
        /// ровно две.</b> Пустой верх лечится не количеством мелочи, а
        /// предметом понятного размера: по нему глаз читает, что зал большой,
        /// а стена — маленькая. Гирлянды и шары вокруг них уже мелочь,
        /// и в одиночку они бы эту работу не сделали.
        /// </summary>
        private static void BuildJumbotron(Transform parent, HoleInWallConfig config)
        {
            Transform group = Group(parent, "Jumbotron");

            float ceilingY = config.PlatformSurfaceY + CeilingY(config);
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                BuildScreenBox(group, side == 0 ? "Screen_L" : "Screen_R",
                    new Vector3(sign * CubeOffsetX, config.PlatformSurfaceY + CubeCentreY, CubeZ),
                    -sign * CubeYaw, ceilingY);
            }
        }

        /// <summary>
        /// Один короб: четыре светящиеся грани, рамка сверху и снизу, тросы
        /// до потолка. Разворот делается поворотом узла, а не пересчётом
        /// каждой грани: иначе рамка и тросы разъезжаются с экраном при
        /// первой же правке угла.
        /// </summary>
        private static void BuildScreenBox(Transform parent, string boxName, Vector3 centre, float yaw,
            float ceilingY)
        {
            Transform box = Group(parent, boxName);
            box.position = centre;
            box.rotation = Quaternion.Euler(0f, yaw, 0f);

            Material led = HoleInWallPaletteAssets.Get(Tone.Led);
            Material metal = HoleInWallPaletteAssets.Get(Tone.Metal);

            float height = CubeHalfHeight * 2f;
            float width = CubeHalfWidth * 2f;
            float depth = CubeHalfDepth * 2f;

            // Четыре грани: две поперёк, две вдоль. Камера ходит вокруг
            // игрока, и экран обязан быть экраном с любой стороны.
            Local(box, "Face_Near", new Vector3(width, height, CubeSkin),
                new Vector3(0f, 0f, -CubeHalfDepth), led);
            Local(box, "Face_Far", new Vector3(width, height, CubeSkin),
                new Vector3(0f, 0f, CubeHalfDepth), led);
            Local(box, "Face_Left", new Vector3(CubeSkin, height, depth),
                new Vector3(-CubeHalfWidth, 0f, 0f), led);
            Local(box, "Face_Right", new Vector3(CubeSkin, height, depth),
                new Vector3(CubeHalfWidth, 0f, 0f), led);

            Local(box, "Rim_Top", new Vector3(width + 0.3f, 0.28f, depth + 0.3f),
                new Vector3(0f, CubeHalfHeight + 0.14f, 0f), metal);
            Local(box, "Rim_Bottom", new Vector3(width + 0.3f, 0.28f, depth + 0.3f),
                new Vector3(0f, -CubeHalfHeight - 0.14f, 0f), metal);

            // Тросы идут до потолка, а не обрываются в воздухе: висящий
            // ни на чём короб читается ошибкой сцены с первого взгляда.
            float cableHeight = ceilingY - (centre.y + CubeHalfHeight + 0.28f);
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f;
                float sz = (i & 2) == 0 ? -1f : 1f;
                Local(box, $"Cable_{i}", new Vector3(0.08f, cableHeight, 0.08f),
                    new Vector3(sx * (CubeHalfWidth - 0.4f), CubeHalfHeight + 0.28f + cableHeight * 0.5f,
                        sz * (CubeHalfDepth - 0.3f)), metal);
            }
        }

        /// <summary>Плита внутри повёрнутого узла: размер и смещение задаются в его осях.</summary>
        private static void Local(Transform parent, string slabName, Vector3 size, Vector3 offset,
            Material material)
        {
            GameObject go = Slab(parent, slabName, size, Vector3.zero, material);
            go.transform.localPosition = offset;
            go.transform.localRotation = Quaternion.identity;
        }

        // ========== ПОТОЛОК ==========

        /// <summary>
        /// Потолок студии: светящиеся полосы поперёк и полотнища, свисающие
        /// вдоль стен.
        ///
        /// <b>Верхняя треть кадра до этого была чёрной.</b> Фермы кончаются
        /// на 9.4 м, потолок лежит на 10.8 — и полтора метра между ними,
        /// вместе со всей плоскостью потолка, читались не потолком, а дырой,
        /// в которой студия просто кончается. Полосы дают потолку структуру
        /// и высоту: по ним глаз меряет, насколько зал больше площадки.
        ///
        /// Полосы <b>светятся тоном панелей</b>, а не белым: светящееся
        /// в этой сцене обязано быть тише контура выреза, и панели —
        /// единственный тон, который это условие уже соблюдает.
        /// </summary>
        private static void BuildCeiling(Transform parent, HoleInWallConfig config)
        {
            Transform group = Group(parent, "Ceiling");

            float unit = config.UnitsPerWidth;
            float apron = 20f * unit;
            float outerHalfWidth = config.ArenaWidth * 0.5f + apron;
            float nearZ = config.ArenaNearZ - apron;
            float farZ = config.ArenaFarZ + apron;
            float y = config.PlatformSurfaceY + GridY;

            Material led = HoleInWallPaletteAssets.Get(Tone.Led);
            Material metal = HoleInWallPaletteAssets.Get(Tone.Metal);

            int rows = Mathf.FloorToInt((farZ - nearZ) / GridStep);
            float from = nearZ + ((farZ - nearZ) - (rows - 1) * GridStep) * 0.5f;

            for (int i = 0; i < rows; i++)
            {
                Slab(group, $"Grid_{i}", new Vector3(outerHalfWidth * 2f, 0.12f, GridWidth),
                    new Vector3(0f, y, from + i * GridStep), led);
            }

            // Продольные полосы поверх поперечных: одни только поперечные
            // читаются полосатым фоном, а не потолком. Решётка читается
            // потолком сразу — по ней глаз меряет и высоту, и глубину.
            int columns = Mathf.FloorToInt(outerHalfWidth * 2f / (GridStep * 1.6f));
            float fromX = -(columns - 1) * GridStep * 1.6f * 0.5f;
            for (int i = 0; i < columns; i++)
            {
                Slab(group, $"GridRib_{i}", new Vector3(GridWidth, 0.1f, farZ - nearZ),
                    new Vector3(fromX + i * GridStep * 1.6f, y - 0.13f, (farZ + nearZ) * 0.5f), metal);
            }

            // Полотнища вдоль стен: они и режут высоту стены пополам, и
            // говорят, что зал уходит вверх, а не обрывается фермой.
            float bannerX = outerHalfWidth - 0.5f;
            float ceilingY = config.PlatformSurfaceY + CeilingY(config);
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                for (int i = 0; i < DropBannerZ.Length; i++)
                {
                    Slab(group, $"Drop_{side}_{i}",
                        new Vector3(0.12f, DropBannerHeight, DropBannerWidth),
                        new Vector3(sign * bannerX, ceilingY - DropBannerHeight * 0.5f, DropBannerZ[i]),
                        i % 2 == 0 ? led : metal);
                }
            }
        }

        /// <summary>
        /// Задник ближнего торца: та же светодиодная плоскость, что у дальнего,
        /// с неоновой полосой по верху зала и рядом баннеров.
        ///
        /// <b>Ставится ради орбитальной камеры.</b> Раз в раунд игрок
        /// разворачивает камеру и видит то, что у него за спиной. Там была
        /// чёрная стена во всю ширину павильона — та самая «пустота сзади».
        /// Плоскость закрывает её целиком; трибуна
        /// <see cref="HoleInWallStands"/> стоит перед ней, и вместе они
        /// читаются торцом зала, а не краем сцены.
        /// </summary>
        private static void BuildNearBackdrop(Transform parent, HoleInWallConfig config)
        {
            float apron = 20f * config.UnitsPerWidth;
            float outerHalfWidth = config.ArenaWidth * 0.5f + apron;
            float z = config.ArenaNearZ - apron + NearBackdropInset;

            float bottom = RimTopY(config);
            float top = config.PlatformSurfaceY + NearBackdropTop;

            Slab(parent, "NearBackdrop", new Vector3(outerHalfWidth * 2f, top - bottom, 0.24f),
                new Vector3(0f, (top + bottom) * 0.5f, z), HoleInWallPaletteAssets.Get(Tone.Led));

            // Плюс, а не минус: ближний торец стоит за спиной у камеры,
            // и «ближе к арене» здесь означает БОЛЬШЕЕ Z. На дальнем торце
            // знак обратный — на этом легко ошибиться и утопить полосу
            // в толще задника.
            Slab(parent, "NearRule", new Vector3(outerHalfWidth * 2f, 0.18f, 0.12f),
                new Vector3(0f, config.PlatformSurfaceY + 4.6f, z + 0.18f),
                HoleInWallPaletteAssets.Get(Tone.NeonPink));

            // Баннеры садятся на плоскость задника, а не висят перед ней
            // в воздухе: первым прогоном пять полотнищ стояли на чёрном фоне
            // сами по себе и читались висящими тряпками.
            float from = -(NearBannerCount - 1) * NearBannerStep * 0.5f;
            for (int i = 0; i < NearBannerCount; i++)
            {
                HangProp(parent, $"NearBanner_{i}", CanvasBanner,
                    new Vector3(from + i * NearBannerStep, config.PlatformSurfaceY + NearBannerY, z + 0.3f),
                    0f, i % 2 == 0 ? Tone.NeonCyan : Tone.NeonPink);
            }
        }

        /// <summary>
        /// Поставить предмет с наклоном и в своём масштабе: сначала поворот
        /// и размер, потом посадка нижней гранью на заданную высоту.
        ///
        /// Порядок обязателен: <see cref="SeatProp"/> сажает предмет по замеру
        /// его габарита, а поворот и масштаб габарит меняют — сделанные после,
        /// они увели бы предмет в пол ровно на ту разницу, которую замер
        /// уже учёл.
        /// </summary>
        private static void Tilted(Transform parent, string propName, string prefabPath, Vector3 ground,
            Quaternion rotation, float scale, Tone paint)
        {
            GameObject go = SpawnProp(parent, propName, prefabPath, 0f, paint);
            if (go == null)
            {
                return;
            }

            go.transform.rotation = rotation;
            go.transform.localScale = Vector3.one * scale;

            if (!TryWorldBounds(go, out Bounds bounds))
            {
                go.transform.position = ground;
                return;
            }

            go.transform.position += new Vector3(
                ground.x - bounds.center.x, ground.y - bounds.min.y, ground.z - bounds.center.z);
        }

        /// <summary>Высота потолка павильона над полом платформы, м. Число одно с павильоном.</summary>
        private static float CeilingY(HoleInWallConfig config) => 15f * config.UnitsPerWidth;

        // ========== ГИРЛЯНДЫ И ШАРЫ ==========

        /// <summary>
        /// Гирлянды флажков поперёк зала. Ставятся сеткой, а не по одной:
        /// одна нитка на тридцать метров пустоты читается забытой верёвкой.
        /// </summary>
        private static void BuildBunting(Transform parent, HoleInWallConfig config)
        {
            Transform group = Group(parent, "Bunting");

            float y = config.PlatformSurfaceY + BuntingY;
            float fromZ = config.CheckLineZ - BuntingStepZ * 0.5f;
            float fromX = -(BuntingPerRow - 1) * BuntingStepX * 0.5f;

            for (int row = 0; row < BuntingRows; row++)
            {
                for (int i = 0; i < BuntingPerRow; i++)
                {
                    // Цвет чередуется по рядам: четыре одинаково розовые
                    // нитки поперёк кадра читаются не праздником, а сеткой
                    // на объективе — проверено первым прогоном.
                    HangProp(group, $"Bunting_{row}_{i}", Bunting,
                        new Vector3(fromX + i * BuntingStepX, y, fromZ + row * BuntingStepZ),
                        0f, row % 2 == 0 ? Tone.NeonPink : Tone.NeonCyan);
                }
            }
        }

        /// <summary>
        /// Зеркальные шары на штангах. Висят в промежутках между дорожками,
        /// а не над ними: над дорожкой ничего быть не должно ниже потолка
        /// запретной зоны, и хотя шар выше неё, промежуток честнее — там
        /// он не спорит с софитом дорожки за место в кадре.
        /// </summary>
        private static void BuildBalls(Transform parent)
        {
            Transform group = Group(parent, "Balls");
            Material metal = HoleInWallPaletteAssets.Get(Tone.Metal);

            for (int i = -1; i <= 1; i++)
            {
                float x = i * 10.8f;
                const float z = 9f;

                GameObject ball = HangProp(group, $"Ball_{i + 1}", DiscoBall,
                    new Vector3(x, BallY, z), 0f, Tone.Metal);
                if (ball != null)
                {
                    ball.transform.localScale = Vector3.one * BallScale;
                }

                float rodHeight = BallMountY - BallY;
                Slab(group, $"BallRod_{i + 1}", new Vector3(0.07f, rodHeight, 0.07f),
                    new Vector3(x, BallY + rodHeight * 0.5f, z), metal);
            }
        }

        // ========== ЛОГОТИП ==========

        /// <summary>
        /// Название шоу на заднике дальнего торца.
        ///
        /// ⚠️ <b>Без разворота на 180°.</b> Текст TextMeshPro в мире изначально
        /// повёрнут лицом к тому, кто смотрит вдоль +Z, — а камера здесь стоит
        /// со стороны бассейна и смотрит именно туда. Разворот «лицом к камере»
        /// её же и отзеркаливает: на табло это уже стоило одного прогона
        /// с «8/6 АНЕТС».
        /// </summary>
        private static void BuildTitle(Transform parent, HoleInWallConfig config)
        {
            Transform group = Group(parent, "Title");

            float backdropZ = config.ArenaFarZ + (20f - 0.4f) * config.UnitsPerWidth;
            float z = backdropZ - TitleStandoff;
            float y = config.PlatformSurfaceY + TitleY;

            // Задник кончается на 7.2 м, потолок лежит на 10.8 — и вся полоса
            // между ними шла по дальнему торцу чёрной. Она видна из игры
            // всегда: туда смотрит вся дорожка. Полоса продолжает задник
            // вверх тем же тоном и той же плоскостью, поэтому читается его
            // частью, а не отдельной вывеской.
            float apron = 20f * config.UnitsPerWidth;
            float outerWidth = (config.ArenaWidth * 0.5f + apron) * 2f;
            Slab(group, "Backdrop_Upper", new Vector3(outerWidth, UpperBandHeight, 0.24f),
                new Vector3(0f, config.PlatformSurfaceY + UpperBandY, backdropZ),
                HoleInWallPaletteAssets.Get(Tone.Led));

            Material pink = HoleInWallPaletteAssets.Get(Tone.NeonPink);

            // Плита под названием: белые буквы, повешенные на чёрную стену,
            // читаются надписью маркером. Тёмная плита в неоновой рамке —
            // вывеской, а вывеска в этой студии и нужна.
            Slab(group, "Plate", new Vector3(TitlePlateWidth, TitlePlateHeight, 0.22f),
                new Vector3(0f, y, z + 0.2f), HoleInWallPaletteAssets.Get(Tone.Stage));
            Slab(group, "Plate_Frame", new Vector3(TitlePlateWidth + 0.5f, TitlePlateHeight + 0.5f, 0.14f),
                new Vector3(0f, y, z + 0.34f), HoleInWallPaletteAssets.Get(Tone.Led));

            Slab(group, "Rule_Top", new Vector3(TitleRuleWidth, TitleRuleThickness, 0.12f),
                new Vector3(0f, y + TitleRuleGap, z + 0.06f), pink);
            Slab(group, "Rule_Bottom", new Vector3(TitleRuleWidth, TitleRuleThickness, 0.12f),
                new Vector3(0f, y - TitleRuleGap, z + 0.06f), pink);

            var go = new GameObject("ShowTitle");
            go.transform.SetParent(group, false);
            go.transform.position = new Vector3(0f, y, z);
            go.transform.rotation = Quaternion.identity;
            go.layer = LayerMask.NameToLayer("Default");

            var text = go.AddComponent<TextMeshPro>();
            text.text = ShowTitle;
            text.fontSize = TitleFontSize;
            text.enableAutoSizing = false;
            text.color = HoleInWallPalette.Plastic;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta = TitleBox;

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // ========== ПЛОЩАДКА ==========

        /// <summary>
        /// Мелочь на кромке бассейна: конфетти-пушки и дым-машины.
        ///
        /// Стоят снаружи кромки, за полуширину запретной зоны: пятно арены
        /// кончается на 21.0 м от оси, кромка идёт по 21.6, реквизит — по 22.35.
        /// </summary>
        private static void BuildRimProps(Transform parent, HoleInWallConfig config, System.Random rng)
        {
            Transform group = Group(parent, "Rim");

            float x = config.ArenaWidth * 0.5f + RimOut;
            float y = RimTopY(config);

            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;

                for (int i = 0; i < CannonZ.Length; i++)
                {
                    // Пушка смотрит внутрь арены и вверх: жерло, глядящее
                    // в стену павильона, читается брошенным реквизитом.
                    //
                    // Наклон и размер ставятся ДО посадки: SeatProp сажает
                    // предмет по замеру его габарита, а поворот и масштаб
                    // габарит меняют — сделанные после, они увели бы пушку
                    // в пол ровно на ту разницу, которую замер уже учёл.
                    Tilted(group, $"Cannon_{side}_{i}", ConfettiCannon, new Vector3(sign * x, y, CannonZ[i]),
                        Quaternion.Euler(-38f, sign < 0f ? 90f : -90f, 0f), CannonScale, Tone.Metal);
                }

                for (int i = 0; i < SmokeZ.Length; i++)
                {
                    SeatProp(group, $"Smoke_{side}_{i}", SmokeMachine,
                        new Vector3(sign * x, y, SmokeZ[i]), sign < 0f ? 90f : -90f + Jitter(rng, 8f),
                        Tone.Stage);
                }
            }
        }

        /// <summary>
        /// Полоса у стены павильона за верхним поясом трибун: кофры, колонки
        /// и флаги на стене.
        ///
        /// Между верхним поясом и стеной остаётся около четырёх метров —
        /// последняя полоса голого пола на сторону. Заполняется тем, что
        /// стоит в любой настоящей студии за трибуной, и заодно даёт кадру
        /// глубину: за головами зрителей есть ещё один план.
        /// </summary>
        private static void BuildBackLane(Transform parent, HoleInWallConfig config, System.Random rng)
        {
            Transform group = Group(parent, "BackLane");

            float y = RimTopY(config) - 0.02f;
            float wallX = config.ArenaWidth * 0.5f + 20f * config.UnitsPerWidth;

            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                float yaw = sign < 0f ? 90f : -90f;

                for (int i = 0; i < CrateZ.Length; i++)
                {
                    string crate = Crates[i % Crates.Length];
                    SeatProp(group, $"Crate_{side}_{i}", crate,
                        new Vector3(sign * BackLaneX, y, CrateZ[i]), yaw + Jitter(rng, 22f), Tone.Metal);

                    // Второй ящик сверху через один: ровный ряд одинаковых
                    // коробок читается забором, штабель — складом.
                    if (i % 2 != 0)
                    {
                        continue;
                    }

                    SeatProp(group, $"Crate_{side}_{i}_Top", Crates[(i + 1) % Crates.Length],
                        new Vector3(sign * BackLaneX + Jitter(rng, 0.2f), y + 0.82f, CrateZ[i] + Jitter(rng, 0.2f)),
                        yaw + Jitter(rng, 30f), Tone.Stage);
                }

                for (int i = 0; i < StackZ.Length; i++)
                {
                    SeatProp(group, $"Stack_{side}_{i}", SpeakerTall,
                        new Vector3(sign * (BackLaneX + 0.9f), y, StackZ[i]), yaw, Tone.Stage);
                }

                for (int i = 0; i < WallFlagZ.Length; i++)
                {
                    HangProp(group, $"WallFlag_{side}_{i}", WallFlag,
                        new Vector3(sign * (wallX - WallFlagStandoff), config.PlatformSurfaceY + WallFlagY,
                            WallFlagZ[i]),
                        yaw, i % 2 == 0 ? Tone.NeonPink : Tone.NeonCyan);
                }
            }
        }

        /// <summary>
        /// Крупные приметы павильона: вышка у воды, арка входа за камерой и
        /// столбы с гирляндами по углам.
        ///
        /// <b>Все три стоят вне обеих запретных зон.</b> Вышка — за
        /// полушириной арены; арка — за полосой отхода камеры, которая
        /// кончается на 13.3 м перед ближним бортом; столбы — по углам, где
        /// нет ни того, ни другого.
        /// </summary>
        private static void BuildLandmarks(Transform parent, HoleInWallConfig config, System.Random rng)
        {
            Transform group = Group(parent, "Landmarks");

            float floorY = RimTopY(config) - 0.02f;

            // Вышка над бассейном: единственный предмет площадки, который
            // объясняет, зачем внизу вода. Смотрит на арену.
            SeatProp(group, "Tower_L", DiveTower, new Vector3(-TowerX, floorY, TowerZ), 60f, Tone.PoolRim);
            SeatProp(group, "Tower_R", DiveTower, new Vector3(TowerX, floorY, TowerZ), -60f, Tone.PoolRim);

            // Арки по краям ближней трибуны, а не по её оси: середину торца
            // занял зал, и арка, оставленная по центру, встала бы прямо
            // в первом ряду.
            SeatProp(group, "Entrance_L", Entrance, new Vector3(-EntranceX, floorY, EntranceZ), 0f, Tone.NeonPink);
            SeatProp(group, "Entrance_R", Entrance, new Vector3(EntranceX, floorY, EntranceZ), 0f, Tone.NeonCyan);

            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                for (int i = 0; i < PoleZ.Length; i++)
                {
                    SeatProp(group, $"Pole_{side}_{i}", BuntingPole,
                        new Vector3(sign * PoleX, floorY, PoleZ[i]), Jitter(rng, 30f), Tone.NeonCyan);
                }
            }

            BuildNearBackdrop(group, config);
        }
    }
}
