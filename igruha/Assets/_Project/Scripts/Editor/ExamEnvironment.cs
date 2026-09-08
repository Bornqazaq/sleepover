using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.Exam;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Окружение и свет «Экзамена» — подфаза 4.3. Всё, что делает зал залом:
    /// арочные окна с дневным светом, пилястры, портреты в золочёных рамах,
    /// люстры, подвесные часы, ряды парт, мелочь на полу.
    ///
    /// <b>Симметрия здесь не вкусовщина, а правило игры.</b> Вся игра держится
    /// на том, что подсказок нет: любой предмет, стоящий у А и отсутствующий
    /// у Б, — это ориентир, по которому можно угадывать. Поэтому окружение
    /// ставится <b>парами через <see cref="Pair"/></b>, а то, что стоит на оси
    /// зала, ставится ровно по центру. Мелочь на полу тоже зеркалится: двадцать
    /// сантиметров бумаги ориентиром не станут, но правило, у которого есть
    /// исключения, перестаёт проверяться числом.
    ///
    /// <b>Окружение не имеет ни одного коллайдера</b> и не отбрасывает теней.
    /// Столкновения на арене держит блокаут; лишний коллайдер здесь ловил бы
    /// толчки и падения, а тени полутора сотен предметов в зале с четырьмя
    /// точечными лампами стоили бы кадра.
    ///
    /// <b>Куда что ставить — по 14.10 спеки.</b> Обе камеры игры смотрят
    /// в одну сторону, значит дальняя стена — фон каждой секунды матча: туда
    /// доска, портреты и акцентный свет. Ближняя стена и запас за зоной
    /// возврата видны только обернувшемуся Ученику — туда наполнение.
    /// </summary>
    internal static class ExamEnvironment
    {
        private const string Empire = "Assets/Synty/PolygonAncientEmpire/Prefabs/";
        private const string Casino = "Assets/Synty/PolygonCasino/Prefabs/";
        private const string Kids = "Assets/Synty/PolygonKids/Prefabs/";
        private const string Town = "Assets/Synty/PolygonTown/Prefabs/";
        private const string Shops = "Assets/Synty/PolygonShops/Prefabs/";
        private const string Generic = "Assets/Synty/PolygonGeneric/Prefabs/";
        private const string Clubs = "Assets/Synty/PolygonNightclubs/Prefabs/";

        /// <summary>Высокое арочное окно 2.50 × 6.01 — под потолок зала 5.76 садится почти без правки.</summary>
        private const string WindowPath = Casino + "Buildings/SM_Bld_Casino_Window_02.prefab";

        private const string PilasterPath = Empire + "Buildings/Base/SM_Bld_Base_Pillar_02.prefab";
        private const string DoorwayPath = Empire + "Buildings/Base/SM_Bld_Base_Wall_Door_Double_01.prefab";
        private const string PicturePath = Casino + "Props/SM_Prop_Picture_05.prefab";
        private const string SconcePath = Generic + "Props/SM_Gen_Prop_Light_Wall_01.prefab";
        private const string ChandelierPath = Casino + "Props/SM_Prop_Chandelier_01.prefab";
        private const string WallClockPath = Town + "Props/SM_Prop_Clock_01.prefab";
        private const string HeaterPath = Town + "Props/SM_Prop_Heater_02.prefab";
        private const string GlobePath = Town + "Props/SM_Prop_Globe_01.prefab";
        private const string PlinthPath = Empire + "Props/SM_Prop_Plinth_02.prefab";
        private const string NoticePath = Shops + "Signs/SM_Prop_Notice_Board_01.prefab";
        private const string ShelfPath = Generic + "Props/SM_Gen_Prop_Shelf_03.prefab";
        private const string BinPath = Clubs + "Props/SM_Prop_Rubbish_Bin_02.prefab";
        private const string PewPath = Casino + "Props/SM_Prop_Bench_Pew_01.prefab";
        private const string SpotPath = Clubs + "Props/SM_Prop_Light_Stage_Spot_01.prefab";
        private const string ExamDeskPath = Kids + "Props/School/SM_Prop_Chair_Desk_01.prefab";
        private const string LockerPath = Kids + "Props/School/SM_Prop_Locker_02.prefab";
        private const string CoatBagPath = Kids + "Attachments/Bags/SM_Chr_Attach_Bag_School_01.prefab";
        private const string PaperPlanePath = Kids + "Props/SM_Prop_Paper_Plane_01.prefab";

        /// <summary>Кадка с растением 0.29 × 0.66 × 0.33 — в простенки между окнами.</summary>
        private const string PlantPath = Town + "Props/SM_Prop_PotPlant_04.prefab";

        /// <summary>Книжный шкаф 0.89 × 1.03 × 0.29 — у ближней стены, парой.</summary>
        private const string BookshelfPath = Town + "Props/SM_Prop_Bookshelf_02.prefab";

        /// <summary>Стопка учебников: ею заполняются полки и подоконники.</summary>
        private const string BooksPath = Town + "Props/SM_Prop_Book_Group_07.prefab";

        /// <summary>Стопка потоньше — вторая модель, чтобы полки не повторялись.</summary>
        private const string BooksAltPath = Town + "Props/SM_Prop_Book_Group_05.prefab";

        /// <summary>Скелет 2.06 × 1.96 × 0.62 (на подвесе) — наглядное пособие в углах у кафедры.</summary>
        private const string SkeletonPath = Generic + "Characters/SM_Gen_Chr_Skeleton_01.prefab";

        private static readonly string[] Papers =
        {
            Generic + "Props/SM_Gen_Prop_Papers_01.prefab",
            Generic + "Props/SM_Gen_Prop_Papers_02.prefab",
            Generic + "Props/SM_Gen_Prop_Papers_03.prefab",
            Generic + "Props/SM_Gen_Prop_Papers_04.prefab",
            Generic + "Props/SM_Gen_Prop_Papers_05.prefab",
            Generic + "Props/SM_Gen_Prop_Papers_06.prefab"
        };

        private static readonly string[] Stationery =
        {
            Kids + "Props/School/SM_Prop_Pencil_01.prefab",
            Kids + "Props/School/SM_Prop_Pen_01.prefab",
            Kids + "Props/School/SM_Prop_Crayon_01.prefab",
            Kids + "Props/School/SM_Prop_Highlighter_01.prefab"
        };

        private static readonly string PortraitFolder = "Assets/_Project/Art/Exam/Portraits";

        /// <summary>Половина толщины стены зала: коробки стоят центром на границе и уходят наружу.</summary>
        private const float WallHalf = 0.2f;

        /// <summary>Окон на каждой боковой стене.</summary>
        private const int WindowCount = 6;

        /// <summary>
        /// Ширина оконного модуля в мире, м. Модуль пака 2.50 × 6.01 —
        /// он на этаж выше нашего потолка, и подгонять его равномерно нельзя:
        /// ужатый по высоте до 4.68 он сузился бы до 1.95 и превратил стену
        /// в частокол бойниц. Ширину задаём сами (3 ШП), высоту тянем до
        /// потолка — арка при этом слегка вытягивается, и на плоскости
        /// стены этого не видно.
        /// </summary>
        private const float WindowWidth = 2.16f;

        /// <summary>
        /// Раскладка по глубине: окна равномерно вдоль стены, пилястры
        /// в промежутках между ними и по углам.
        ///
        /// Считается от размера зала, а не таблицей чисел. Таблица под зал
        /// 25.92 м пережила бы ужатие до 20.16 молча: два крайних окна
        /// уехали бы в торцевые стены, а пилястры — за пределы зала.
        /// </summary>
        private static float[] WindowLine(ExamConfig config)
        {
            float step = config.HallDepth / WindowCount;
            var line = new float[WindowCount];
            for (int i = 0; i < WindowCount; i++)
            {
                line[i] = -config.HallDepth * 0.5f + step * (i + 0.5f);
            }

            return line;
        }

        /// <summary>Пилястры между окнами и по углам — на одну больше, чем окон.</summary>
        private static float[] PilasterLine(ExamConfig config)
        {
            float step = config.HallDepth / WindowCount;
            float corner = config.HallDepth * 0.5f - 0.25f;
            var line = new float[WindowCount + 1];
            for (int i = 0; i <= WindowCount; i++)
            {
                line[i] = Mathf.Clamp(-config.HallDepth * 0.5f + step * i, -corner, corner);
            }

            return line;
        }

        /// <summary>Насколько мелочь считается мелочью: ниже неё предмет можно класть и на маршруты.</summary>
        private const float LitterHeight = 0.2f;

        /// <summary>Рядов парт в запасе за зоной возврата.</summary>
        private const int BackRowCount = 3;

        /// <summary>Балок перекрытия поперёк зала.</summary>
        private const int CeilingBeamCount = 5;

        /// <summary>Насколько балка свисает из потолка, м.</summary>
        private const float BeamHeight = 0.26f;

        /// <summary>Толщина балки по глубине зала, м.</summary>
        private const float BeamDepth = 0.34f;

        /// <summary>Насколько пилястра выступает от стены в зал, м. Замер модели пака.</summary>
        private const float PilasterDepth = 0.43f;

        /// <summary>Ширина объявления на пилястре, м: по её лицевой грани, не шире.</summary>
        private const float ChartWidth = 0.38f;

        /// <summary>Высота объявления, м.</summary>
        private const float ChartHeight = 1.05f;

        /// <summary>Высота центра плаката над полом, м: выше панелей, ниже карниза.</summary>
        private const float ChartCenterY = 2.35f;

        /// <summary>
        /// Половина ширины доски в долях ширины зала. Дублирует
        /// <c>ExamArenaBuilder.BoardWidthFactor / 2</c>: окружение обязано
        /// знать, где кончается доска, чтобы не ставить предметы поверх неё.
        /// </summary>
        private const float BoardHalfFactor = 0.25f;

        /// <summary>
        /// Габаритная высота задней парты. Та же, что у парт в нефах
        /// (<c>ExamDress.ExamDeskHeight</c>): одинаковая мебель на одной
        /// глубине кадра — единственный способ не получить в зале два
        /// разных масштаба сразу.
        /// </summary>
        private const float BackDeskHeight = 0.95f;

        private static readonly List<string> Notes = new List<string>(8);
        private static int placed;
        private static int lights;

        /// <summary>
        /// Построить окружение. Вызывается после палитры: часть предметов
        /// красится её тонами, и материалы к этому моменту уже заведены.
        /// </summary>
        internal static void Build(GameObject arena, ExamConfig config, System.Random rng)
        {
            if (arena == null || config == null)
            {
                return;
            }

            Notes.Clear();
            placed = 0;
            lights = 0;

            Transform root = Group(arena.transform, "Environment");

            BuildSideWalls(root, config);
            BuildFarWall(root, config);
            BuildNearWall(root, config);
            BuildCeiling(root, config);
            BuildFurniture(root, config);
            BuildLitter(root, config, rng);
            BuildLights(root, config);

            // Тени снимаются со всего окружения одним проходом, а не при
            // постановке каждого предмета: полторы сотни теневых кастеров
            // в зале с семью источниками стоят кадра, а забыть флаг у одной
            // ветки — вопрос времени.
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // ────────────────────────────────────────────────────────────────
        // Стены
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Боковые стены: шесть арочных окон на каждой, пилястры между ними,
        /// радиаторы под окнами.
        ///
        /// <b>За каждым окном стоит светящаяся плоскость.</b> Зал закрыт со
        /// всех сторон, и за стеклом окна честно видна наша же стена — окно
        /// читалось бы нишей. Плоскость между модулем окна и стеной даёт
        /// пересвеченный дневной свет: приём дешёвый, а окно перестаёт быть
        /// декорацией и становится источником.
        /// </summary>
        private static void BuildSideWalls(Transform root, ExamConfig config)
        {
            Transform group = Group(root, "SideWalls");
            float inX = config.HallWidth * 0.5f - WallHalf;
            float height = config.CeilingHeight;
            float[] windowZ = WindowLine(config);
            float[] pilasterZ = PilasterLine(config);
            Material daylight = ExamPalette.Get(ExamPalette.Tone.Daylight);
            Material stone = ExamPalette.Get(ExamPalette.Tone.Stone);

            for (int i = 0; i < windowZ.Length; i++)
            {
                float z = windowZ[i];
                Pair(inX, x =>
                {
                    GameObject window = ExamDress.Spawn(group, $"Window_{i}", WindowPath, x < 0 ? 1 : 3);
                    if (window == null)
                    {
                        return;
                    }

                    // ⚠️ Габарит задаётся в МИРОВЫХ осях, а окно боковой стены
                    // развёрнуто на 90°: его ширина идёт по Z, а по X стоит
                    // толщина стены. Подставленная в X ширина раздула модули
                    // в плиты 2.16 м толщиной, вылезшие в зал поверх парт.
                    // Ось выбирается по замеру, а не по предположению.
                    Vector3 natural = ExamDress.WorldSize(window);
                    ExamDress.SizeTo(window, natural.x > natural.z
                        ? new Vector3(WindowWidth, height, natural.z)
                        : new Vector3(natural.x, height, WindowWidth));
                    Vector3 size = ExamDress.WorldSize(window);
                    ExamDress.CenterAt(window, new Vector3(
                        x - Mathf.Sign(x) * size.x * 0.5f, height * 0.5f, z));

                    // Стену модуля красим тоном зала, а стекло не трогаем:
                    // перекрась его — и окно станет глухой плитой.
                    PaintExceptGlass(window, stone);
                    placed++;

                    // Светящаяся плоскость за окном, вплотную к нашей стене.
                    ExamDress.Bar(group, $"Daylight_{i}", daylight,
                        new Vector3(x - Mathf.Sign(x) * 0.06f, height * 0.55f, z),
                        new Vector3(0.04f, height * 0.72f, WindowWidth * 0.7f));
                    placed++;
                });
            }

            foreach (float z in pilasterZ)
            {
                Pair(inX, x =>
                {
                    GameObject pilaster = ExamDress.Spawn(group, $"Pilaster_{z:F0}", PilasterPath, 0);
                    if (pilaster == null)
                    {
                        return;
                    }

                    // Тянем только по высоте: пилястра — простой брус, и
                    // равномерный масштаб сделал бы её вдвое толще.
                    Vector3 natural = ExamDress.WorldSize(pilaster);
                    ExamDress.SizeTo(pilaster, new Vector3(natural.x, height, natural.z));
                    ExamDress.CenterAt(pilaster, new Vector3(
                        x - Mathf.Sign(x) * natural.x * 0.5f, height * 0.5f, z));
                    ExamDress.PaintTree(pilaster, stone);
                    placed++;
                });
            }

            for (int i = 0; i < windowZ.Length; i++)
            {
                float z = windowZ[i];
                Pair(inX, x =>
                {
                    GameObject heater = ExamDress.Prop(group, $"Heater_{i}", HeaterPath,
                        new Vector3(x - Mathf.Sign(x) * 0.22f, 0.32f, z), x < 0 ? 90f : -90f);
                    if (heater != null)
                    {
                        placed++;
                    }
                });
            }

            // Кадки с растениями в простенках между окнами — через один,
            // чтобы стена не превратилась в оранжерею. Ставятся парами,
            // как и всё здесь: горшок только слева был бы приметой стороны.
            for (int i = 1; i < pilasterZ.Length - 1; i += 2)
            {
                float z = pilasterZ[i];
                Pair(inX - 0.55f, x =>
                {
                    if (ExamDress.Prop(group, $"WallPlant_{z:F0}", PlantPath,
                            new Vector3(x, 0f, z), x < 0f ? 25f : -25f, 1.15f) != null)
                    {
                        placed++;
                    }
                });
            }

            // Объявления на пилястрах между окнами.
            //
            // ⚠️ Висят на ЛИЦЕВОЙ грани пилястры, а не на плоскости стены.
            // Первый заход повесил их на стену с отступом 6 см — и они
            // целиком спрятались внутрь пилястры, которая выступает в зал
            // на 43 см. В кадре не появилось ничего, и это был не промах
            // по месту, а промах по оси: между окнами тут не стена, а колонна.
            //
            // Ширина — по ширине самой пилястры: лист, вылезающий за её
            // грань, читается висящим в воздухе.
            Material frameTone = ExamPalette.Get(ExamPalette.Tone.Panel);
            Material chartTone = ExamPalette.Get(ExamPalette.Tone.SignPlate);
            for (int i = 2; i < pilasterZ.Length - 2; i += 2)
            {
                float z = pilasterZ[i];
                Pair(inX, x =>
                {
                    float sign = Mathf.Sign(x);
                    ExamDress.Bar(group, $"ChartFrame_{z:F0}", frameTone,
                        new Vector3(x - sign * (PilasterDepth + 0.02f), ChartCenterY, z),
                        new Vector3(0.04f, ChartHeight + 0.1f, ChartWidth + 0.1f));
                    ExamDress.Bar(group, $"Chart_{z:F0}", chartTone,
                        new Vector3(x - sign * (PilasterDepth + 0.05f), ChartCenterY, z),
                        new Vector3(0.02f, ChartHeight, ChartWidth));
                    placed += 2;
                });
            }

            // Бра на пилястрах между окнами: боковые стены до сих пор жили
            // одним дневным подсветом из-за стекла, и всё, что на них висит,
            // проваливалось в тень ровно там, где кончался этот подсвет.
            foreach (float z in new[] { pilasterZ[1], pilasterZ[WindowCount - 1] })
            {
                Pair(inX, x =>
                {
                    GameObject sconce = ExamDress.Spawn(group, $"SideSconce_{z:F0}", SconcePath, x < 0 ? 1 : 3);
                    if (sconce == null)
                    {
                        return;
                    }

                    Vector3 natural = ExamDress.WorldSize(sconce);
                    ExamDress.CenterAt(sconce, new Vector3(
                        x - Mathf.Sign(x) * natural.x * 0.5f, config.CeilingHeight - 1.35f, z));
                    ExamDress.PaintTree(sconce, ExamPalette.Get(ExamPalette.Tone.Gilt));
                    placed++;
                });
            }

            // Часы — на боковых стенах, а не под потолком по центру: на оси
            // зала они попадают между камерой и доской. Две штуки, зеркально,
            // над простенками между окнами.
            // Часы вешаются на пилястру, а не «примерно там»: между окнами
            // стена шириной чуть больше метра, и абсолютное число рано или
            // поздно попадает циферблатом на стекло.
            float clockY = config.CeilingHeight - 0.75f;
            foreach (float z in new[] { pilasterZ[2], pilasterZ[WindowCount - 2] })
            {
                Pair(inX, x =>
                {
                    GameObject clock = ExamDress.Spawn(group, $"SideClock_{z:F0}", WallClockPath, x < 0 ? 1 : 3);
                    if (clock == null)
                    {
                        return;
                    }

                    Vector3 natural = ExamDress.WorldSize(clock);
                    ExamDress.SizeTo(clock, natural * (1.15f / Mathf.Max(0.01f, natural.y)));
                    Vector3 size = ExamDress.WorldSize(clock);
                    ExamDress.CenterAt(clock, new Vector3(x - Mathf.Sign(x) * (size.x * 0.5f + 0.02f), clockY, z));
                    placed++;
                });
            }
        }

        /// <summary>
        /// Дальняя стена — фон обоих кадров игры. Портреты в золочёных рамах
        /// по обе стороны доски и бра между ними.
        ///
        /// <b>Холст в раме — свой.</b> Рама пака приезжает с яркой абстракцией
        /// в магенте и жёлтом, и перекрасить холст отдельно нельзя: он сидит
        /// с рамой в одном атласе. Поэтому поверх него ложится своя плоскость
        /// со сгенерированным портретом — решение геймдизайнера 04.09 (14.12).
        /// </summary>
        private static void BuildFarWall(Transform root, ExamConfig config)
        {
            Transform group = Group(root, "FarWall");
            float z = config.HallDepth * 0.5f - WallHalf;
            float inX = config.HallWidth * 0.5f - WallHalf;

            // Портреты живут между краем доски и углом зала. Абсолютные 8.6
            // и 11.6 считались от стены шириной 28.8 м; на 19.44 второй из них
            // оказался бы за пределами зала. Считаем от свободного простенка.
            float boardEdge = config.HallWidth * BoardHalfFactor + 0.4f;
            float span = Mathf.Max(0.6f, inX - 0.7f - boardEdge);
            float[] portraitX = { boardEdge + span * 0.32f, boardEdge + span * 0.78f };
            float portraitY = config.CeilingHeight * 0.66f;

            for (int i = 0; i < portraitX.Length; i++)
            {
                int index = i;
                Pair(portraitX[i], x =>
                {
                    GameObject frame = ExamDress.Spawn(group, $"Portrait_{index}", PicturePath, 0);
                    if (frame == null)
                    {
                        return;
                    }

                    Vector3 natural = ExamDress.WorldSize(frame);
                    ExamDress.CenterAt(frame, new Vector3(x, portraitY, z - natural.z * 0.5f - 0.01f));
                    ExamDress.PaintTree(frame, ExamPalette.Get(ExamPalette.Tone.Gilt));
                    placed++;

                    // Номер портрета берётся от места, а не от случайности:
                    // зеркальная пара обязана быть одинаковой, иначе портрет
                    // сам становится приметой стороны.
                    Canvas(group, $"PortraitCanvas_{index}_{(x < 0 ? "L" : "R")}", index,
                        new Vector3(x, portraitY, z - natural.z - 0.02f),
                        natural.x * 0.79f, natural.y * 0.77f);
                });
            }

            Pair(boardEdge + span * 0.55f, x =>
            {
                GameObject sconce = ExamDress.Spawn(group, "Sconce_Far", SconcePath, 0);
                if (sconce == null)
                {
                    return;
                }

                Vector3 natural = ExamDress.WorldSize(sconce);
                ExamDress.CenterAt(sconce, new Vector3(x, config.CeilingHeight - 0.75f, z - natural.z * 0.5f));
                ExamDress.PaintTree(sconce, ExamPalette.Get(ExamPalette.Tone.Gilt));
                placed++;
            });

            // Скелеты по краям доски — наглядное пособие, по которому зал
            // читается школьным кабинетом с первого кадра.
            //
            // Стояли в дальних углах и попадали внутрь шкафчиков: угол уже
            // занят их зеркальной парой. Место у края доски свободно, стоит
            // ровно на фоне обеих камер игры и не спорит с портретами —
            // те висят выше и дальше от оси.
            Pair(config.HallWidth * BoardHalfFactor + 0.7f, x =>
            {
                GameObject skeleton = ExamDress.Prop(group, "Skeleton", SkeletonPath,
                    new Vector3(x, 0f, z - 0.55f), x < 0f ? 22f : -22f, 2.05f);
                if (skeleton != null)
                {
                    placed++;
                }
            });
        }

        /// <summary>
        /// Ближняя стена: портал двери по центру, большие часы над ним, доски
        /// объявлений и стеллажи. Видна только обернувшемуся Ученику, поэтому
        /// сюда идёт наполнение, а не акценты.
        /// </summary>
        private static void BuildNearWall(Transform root, ExamConfig config)
        {
            Transform group = Group(root, "NearWall");
            float z = -(config.HallDepth * 0.5f - WallHalf);
            float inX = config.HallWidth * 0.5f - WallHalf;
            Material stone = ExamPalette.Get(ExamPalette.Tone.Stone);

            GameObject doorway = ExamDress.Spawn(group, "Doorway", DoorwayPath, 2);
            if (doorway != null)
            {
                Vector3 natural = ExamDress.WorldSize(doorway);
                float scale = 3.6f / Mathf.Max(0.01f, natural.y);
                ExamDress.SizeTo(doorway, natural * scale);
                Vector3 size = ExamDress.WorldSize(doorway);
                ExamDress.CenterAt(doorway, new Vector3(0f, size.y * 0.5f, z + size.z * 0.5f));
                PaintExceptGlass(doorway, stone);
                placed++;
            }

            // Доворот 0, а не 180: у часов и доски объявлений лицо смотрит
            // на +Z в своей модели, и «развернуть к залу» здесь значит
            // не трогать. Первый заход развернул их на 180°, и на ближней
            // стене висели два чёрных прямоугольника и чёрный диск —
            // изнанки.
            GameObject clock = ExamDress.Spawn(group, "WallClock", WallClockPath, 0);
            if (clock != null)
            {
                Vector3 natural = ExamDress.WorldSize(clock);
                ExamDress.SizeTo(clock, natural * (1.35f / Mathf.Max(0.01f, natural.y)));
                Vector3 size = ExamDress.WorldSize(clock);
                ExamDress.CenterAt(clock, new Vector3(0f, config.CeilingHeight - 0.75f, z + size.z * 0.5f + 0.02f));
                placed++;
            }

            Pair(inX * 0.35f, x =>
            {
                GameObject notice = ExamDress.Spawn(group, "Notice", NoticePath, 0);
                if (notice == null)
                {
                    return;
                }

                Vector3 natural = ExamDress.WorldSize(notice);
                ExamDress.SizeTo(notice, natural * 1.35f);
                Vector3 size = ExamDress.WorldSize(notice);
                ExamDress.CenterAt(notice, new Vector3(x, 2.35f, z + size.z * 0.5f + 0.02f));
                placed++;
            });

            Pair(inX * 0.72f, x =>
            {
                if (ExamDress.Prop(group, "Shelf", ShelfPath, new Vector3(x, 0f, z + 0.4f), 180f) != null)
                {
                    placed++;
                }
            });

            // Книжные шкафы по краям ближней стены и учебники на них.
            // Ближняя стена — это фон для обернувшегося Ученика и для всей
            // зоны возврата; после ужатия зала она подошла к площадкам вплотную
            // и стала попадать в кадр чаще, чем раньше попадала дальняя.
            Pair(inX - 0.65f, x =>
            {
                GameObject shelf = ExamDress.Prop(group, "Bookcase", BookshelfPath,
                    new Vector3(x, 0f, z + 0.25f), 180f, 1.85f);
                if (shelf == null)
                {
                    return;
                }

                placed++;
                for (int tier = 0; tier < 3; tier++)
                {
                    float y = 0.42f + tier * 0.52f;
                    if (ExamDress.Prop(group, $"Bookcase_Books_{tier}", tier % 2 == 0 ? BooksPath : BooksAltPath,
                            new Vector3(x + (tier % 2 == 0 ? -0.18f : 0.18f), y, z + 0.25f), 180f) != null)
                    {
                        placed++;
                    }
                }
            });

            // Ещё две пары портретов. Сгенерированных холстов четыре, и все
            // четыре идут в дело: два висят у доски, два здесь. Пара — это
            // один и тот же портрет слева и справа, иначе сам портрет
            // становится приметой стороны.
            float[] portraitX = { inX * 0.28f, inX * 0.58f };
            float portraitY = config.CeilingHeight * 0.66f;
            for (int i = 0; i < portraitX.Length; i++)
            {
                int index = i + 2;
                Pair(portraitX[i], x =>
                {
                    GameObject frame = ExamDress.Spawn(group, $"Portrait_{index}", PicturePath, 2);
                    if (frame == null)
                    {
                        return;
                    }

                    Vector3 natural = ExamDress.WorldSize(frame);
                    ExamDress.CenterAt(frame, new Vector3(x, portraitY, z + natural.z * 0.5f + 0.01f));
                    ExamDress.PaintTree(frame, ExamPalette.Get(ExamPalette.Tone.Gilt));
                    placed++;

                    Canvas(group, $"PortraitCanvas_{index}_{(x < 0 ? "L" : "R")}", index,
                        new Vector3(x, portraitY, z + natural.z + 0.02f),
                        natural.x * 0.79f, natural.y * 0.77f, 180f);
                });
            }
        }

        /// <summary>
        /// Потолок: люстры над залом, подвесные часы над проходом и софиты.
        ///
        /// Люстры висят <b>только над ближней половиной</b>. Над платформами
        /// им нельзя: люстра 2.2 м спускается до 3.5 м, а низ подвесного
        /// указателя варианта — на 3.25, и в кадре они начали бы спорить
        /// ровно за то место, ради которого указатель и повешен.
        /// </summary>
        private static void BuildCeiling(Transform root, ExamConfig config)
        {
            Transform group = Group(root, "Ceiling");
            float top = config.CeilingHeight;

            // Балки поперёк зала. До них потолок был ровным светлым полем
            // на всю ширину кадра — единственная плоскость сцены, по которой
            // глазу нечем померить расстояние. Ритм балок задаёт масштаб
            // сверху так же, как шов пола задаёт его снизу, и стоит это
            // пяти растянутых кубов.
            // Балка не ставится за спиной камеры зала: та висит на 4.08 м,
            // и ближайшая балка оказалась бы в полутора метрах перед объективом,
            // занимая собой верхнюю треть кадра. Считаем границу от запаса
            // за зоной возврата — там, где эта камера и стоит.
            Material beamTone = ExamPalette.Get(ExamPalette.Tone.Panel);
            float beamStep = config.HallDepth / (CeilingBeamCount + 1);
            float beamNearLimit = -config.HallDepth * 0.5f + config.CameraSlackDepth;
            for (int i = 1; i <= CeilingBeamCount; i++)
            {
                float z = -config.HallDepth * 0.5f + beamStep * i;
                if (z < beamNearLimit)
                {
                    continue;
                }

                ExamDress.Bar(group, $"Beam_{i}", beamTone,
                    new Vector3(0f, top - BeamHeight * 0.5f, z),
                    new Vector3(config.HallWidth, BeamHeight, BeamDepth));
                placed++;
            }

            // Люстра 2.2 м спускалась бы теперь до 2.48 — ровно на уровень
            // головы бегущего. Под потолок 4.68 её высота считается от него:
            // низ обязан остаться выше человека с запасом на прыжок.
            float chandelierHeight = Mathf.Clamp(top * 0.3f, 1.1f, 2.2f);
            float chandelierZ = -config.HallDepth * 0.5f + config.CameraSlackDepth * 0.75f;

            Pair(config.HallWidth * 0.27f, x =>
            {
                GameObject chandelier = ExamDress.Spawn(group, "Chandelier", ChandelierPath, 0);
                if (chandelier == null)
                {
                    return;
                }

                Vector3 natural = ExamDress.WorldSize(chandelier);
                ExamDress.SizeTo(chandelier, natural * (chandelierHeight / Mathf.Max(0.01f, natural.y)));
                Vector3 size = ExamDress.WorldSize(chandelier);
                ExamDress.CenterAt(chandelier, new Vector3(x, top - size.y * 0.5f, chandelierZ));
                placed++;
            });

            // Подвесных часов посреди зала не будет, и это разбор рендера,
            // а не вкус. Камера зала стоит на оси у задней стены и смотрит
            // вперёд: всё, что висит по центру, оказывается между зрителем
            // и доской. На z = −2.4 часы вставали перед текстом вопроса,
            // на z = −9.2 — вплотную к камере и закрывали пол-кадра.
            // Часы переехали на боковые стены парой (см. BuildSideWalls).

            // Софиты: по одному одинаковому над каждой платформой и один
            // на кафедру. Решение геймдизайнера 04.09 (14.12, п. 4) — свет
            // ставится ради читаемости, а не ради студийности.
            float platformsZ = config.HallDepth * 0.5f - config.PodiumDepth - 2.16f - config.PlatformDepth * 0.5f;
            float offset = (config.PlatformWidth + config.PlatformGap) * 0.5f;

            Pair(offset, x => Spot(group, "Spot_Platform", new Vector3(x, top, platformsZ), 0.9f));
            Spot(group, "Spot_Podium", new Vector3(0f, top, config.HallDepth * 0.5f - config.PodiumDepth * 0.5f - 1.3f), 0.8f);
        }

        private static void Spot(Transform group, string name, Vector3 ceilingPoint, float scale)
        {
            GameObject spot = ExamDress.Spawn(group, name, SpotPath, 0);
            if (spot == null)
            {
                return;
            }

            spot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Vector3 natural = ExamDress.WorldSize(spot);
            ExamDress.SizeTo(spot, natural * scale);
            Vector3 size = ExamDress.WorldSize(spot);
            ExamDress.CenterAt(spot, new Vector3(ceilingPoint.x, ceilingPoint.y - size.y * 0.5f, ceilingPoint.z));
            placed++;
        }

        // ────────────────────────────────────────────────────────────────
        // Мебель
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Мебель зала. Задние ряды парт в запасе за зоной возврата — тот самый
        /// запас, который спека 4.2 просит заполнить декором, — плюс шкафчики,
        /// вешалки, скамьи, урны и глобусы у кафедры.
        ///
        /// Шкафчики и вешалка блокаута стоят по одной штуке и <b>по разным
        /// сторонам зала</b>: шкаф слева, вешалка справа. Здесь ставятся их
        /// зеркальные двойники — без коллайдеров, чисто декорацией. Иначе
        /// у платформы А есть примета, которой нет у Б.
        /// </summary>
        private static void BuildFurniture(Transform root, ExamConfig config)
        {
            Transform group = Group(root, "Furniture");
            float halfW = config.HallWidth * 0.5f;
            float halfD = config.HallDepth * 0.5f;

            // Задние ряды парт — «класс» за спиной Учеников. Считаются от
            // ближней стены, а не абсолютными числами: после ужатия зала
            // прежние −9.6 и −11.3 уехали бы в стену и наружу.
            //
            // Рядов три, по шесть мест в каждом: запас за зоной возврата —
            // единственное место в зале, где помещается настоящий ряд парт,
            // и именно он попадает в кадр каждый раз, когда камера отходит
            // за спину игрока.
            float backRowStart = -halfD + 1.35f;
            for (int r = 0; r < BackRowCount; r++)
            {
                float z = backRowStart + r * 1.55f;
                for (int i = 0; i < 3; i++)
                {
                    float baseX = 1.1f + i * 1.7f;
                    Pair(baseX, x =>
                    {
                        if (ExamDress.Prop(group, $"BackDesk_{r}_{i}", ExamDeskPath,
                                new Vector3(x, 0f, z), 0f, BackDeskHeight) != null)
                        {
                            placed++;
                        }
                    });
                }
            }

            // Портфели у задних парт: брошенная сумка читается «здесь сидели»
            // убедительнее любой третьей парты.
            Pair(1.1f + 1.7f, x =>
            {
                if (ExamDress.Prop(group, "BackBag", CoatBagPath,
                        new Vector3(x + 0.55f, 0f, backRowStart + 0.75f), x < 0f ? 200f : 160f) != null)
                {
                    placed++;
                }
            });

            // Зеркальный двойник шкафчиков: блокаутный шкаф стоит слева.
            Pair(halfW - 1.2f, x =>
            {
                if (x < 0f)
                {
                    return;
                }

                for (int c = 0; c < 2; c++)
                {
                    for (int row = 0; row < 2; row++)
                    {
                        GameObject locker = ExamDress.Spawn(group, $"MirrorLocker_{c}_{row}", LockerPath, 2);
                        if (locker == null)
                        {
                            return;
                        }

                        ExamDress.SizeTo(locker, new Vector3(0.9f, 1.15f, 0.6f));
                        ExamDress.CenterAt(locker, new Vector3(
                            x - 0.45f + c * 0.9f, 0.575f + row * 1.15f, halfD - 0.55f));
                        ExamDress.PaintTree(locker, ExamPalette.Get(ExamPalette.Tone.LockerBody));
                        placed++;
                    }
                }
            });

            // Зеркальный двойник вешалки: блокаутная стоит справа.
            Pair(halfW - 0.9f, x =>
            {
                if (x > 0f)
                {
                    return;
                }

                Material metal = ExamPalette.Get(ExamPalette.Tone.Metal);
                ExamDress.Bar(group, "MirrorRack_Post", metal, new Vector3(x, 0.95f, -2.5f),
                    new Vector3(0.1f, 1.9f, 0.1f));
                ExamDress.Bar(group, "MirrorRack_Bar", metal, new Vector3(x, 1.85f, -2.5f),
                    new Vector3(0.08f, 0.08f, 2.4f));
                placed += 2;

                for (int i = 0; i < 2; i++)
                {
                    float z = -3.1f + i * 1.2f;
                    if (ExamDress.Prop(group, $"MirrorBag_{i}", CoatBagPath,
                            new Vector3(x, 1.45f, z), i == 0 ? 90f : 270f) != null)
                    {
                        placed++;
                    }
                }
            });

            // Скамьи вдоль боковых стен за колоннами парт: ждущие своей
            // очереди сидят там же, где и в настоящем классе, — у стены.
            Pair(halfW - 0.75f, x =>
            {
                for (int i = 0; i < 2; i++)
                {
                    float z = halfD - 3.4f - i * 2.0f;
                    if (ExamDress.Prop(group, $"Pew_{i}", PewPath, new Vector3(x, 0f, z),
                            x < 0f ? 90f : -90f) != null)
                    {
                        placed++;
                    }
                }
            });

            // Глобусы на плинтах по краям кафедры: пара опознавательных знаков
            // «здесь начальство», и обе — на оси симметрии зала. Z считается
            // от стены: абсолютные 11.2 после ужатия зала оказались бы за ней.
            float globeZ = halfD - config.PodiumDepth * 0.5f;
            Pair(config.PodiumWidth * 0.5f + 0.9f, x =>
            {
                GameObject plinth = ExamDress.Prop(group, "Plinth", PlinthPath, new Vector3(x, 0f, globeZ), 0f);
                if (plinth == null)
                {
                    return;
                }

                placed++;
                float top = ExamDress.WorldSize(plinth).y;
                if (ExamDress.Prop(group, "Globe", GlobePath, new Vector3(x, top, globeZ), x < 0f ? 30f : -30f) != null)
                {
                    placed++;
                }
            });

            // Урны по четырём углам.
            Pair(halfW - 1.1f, x =>
            {
                foreach (float z in new[] { -halfD + 1.2f, halfD - 1.2f })
                {
                    if (ExamDress.Prop(group, "Bin", BinPath, new Vector3(x, 0f, z), 0f) != null)
                    {
                        placed++;
                    }
                }
            });
        }

        // ────────────────────────────────────────────────────────────────
        // Мелочь
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Бумага, самолётики и карандаши на полу.
        ///
        /// Это единственное, что правила разрешают класть <b>на маршруты</b>:
        /// всё ниже двадцати сантиметров не задевает ни ног, ни камеры,
        /// а наполнять надо ровно те места, где игрок проводит весь раунд.
        /// На сами платформы мелочь не кладётся: площадка — это игровое поле
        /// на семерых, а не витрина.
        ///
        /// Раскладка зеркальна: каждая бумажка ставится парой. Свой генератор
        /// случайных чисел приходит снаружи — общий с билдером сдвинул бы
        /// последовательность, по которой раскладывается геймплей.
        /// </summary>
        private static void BuildLitter(Transform root, ExamConfig config, System.Random rng)
        {
            Transform group = Group(root, "Litter");
            float halfW = config.HallWidth * 0.5f - 1.0f;
            float halfD = config.HallDepth * 0.5f - 1.0f;

            // Бумаги — самые крупные из мелочи: россыпь листов у пака бывает
            // до двух метров в поперечнике, и четырнадцати пар хватало, чтобы
            // строгий зал начал читаться помойкой. Девять пар держат «здесь
            // только что писали», не перебивая пол.
            Scatter(group, rng, Papers, 9, halfW, halfD, config, "Paper", 0f);
            Scatter(group, rng, new[] { PaperPlanePath }, 5, halfW, halfD, config, "Plane", 0f);
            Scatter(group, rng, Stationery, 10, halfW, halfD, config, "Pen", 0f);
        }

        private static void Scatter(Transform group, System.Random rng, string[] paths, int pairs,
            float halfW, float halfD, ExamConfig config, string name, float lift)
        {
            for (int i = 0; i < pairs; i++)
            {
                string path = paths[rng.Next(paths.Length)];
                float yaw = (float)(rng.NextDouble() * 360.0);
                Vector3 point = Vector3.zero;
                bool found = false;

                for (int attempt = 0; attempt < 40 && !found; attempt++)
                {
                    float x = 0.6f + (float)rng.NextDouble() * (halfW - 0.6f);
                    float z = -halfD + (float)rng.NextDouble() * (halfD * 2f);
                    if (OnPlatform(new Vector3(x, 0f, z), config))
                    {
                        continue;
                    }

                    point = new Vector3(x, lift, z);
                    found = true;
                }

                if (!found)
                {
                    continue;
                }

                int index = i;
                Pair(point.x, x =>
                {
                    if (ExamDress.Prop(group, $"{name}_{index}", path,
                            new Vector3(x, point.y, point.z), x < 0f ? -yaw : yaw) != null)
                    {
                        placed++;
                    }
                });
            }
        }

        /// <summary>Пятно платформ вместе с зазором: сюда не кладётся даже мелочь.</summary>
        private static bool OnPlatform(Vector3 point, ExamConfig config)
        {
            float platformsZ = config.HallDepth * 0.5f - config.PodiumDepth - 2.16f - config.PlatformDepth * 0.5f;
            float halfSpan = config.PlatformWidth + config.PlatformGap * 0.5f + 0.4f;
            float halfDepth = config.PlatformDepth * 0.5f + 0.4f;
            return Mathf.Abs(point.x) < halfSpan && Mathf.Abs(point.z - platformsZ) < halfDepth;
        }

        // ────────────────────────────────────────────────────────────────
        // Свет
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Свет ставится кодом, а не инспектором: настройки в YAML сцены
        /// не переживают слияние веток, а код переживает.
        ///
        /// Три роли. <b>Окна</b> дают холодный дневной подсвет по боковым
        /// стенам. <b>Софиты</b> — по одному одинаковому на платформу: это
        /// решение геймдизайнера, и равенство их яркости проверяется числом.
        /// <b>Пятно на кафедру</b> отвечает за «кто сейчас Ведущий»: роль
        /// меняется каждый круг, и новый человек обязан узнать себя сразу.
        /// </summary>
        private static void BuildLights(Transform root, ExamConfig config)
        {
            Transform group = Group(root, "Lights");
            float inX = config.HallWidth * 0.5f - WallHalf;
            float platformsZ = config.HallDepth * 0.5f - config.PodiumDepth - 2.16f - config.PlatformDepth * 0.5f;
            float offset = (config.PlatformWidth + config.PlatformGap) * 0.5f;

            float halfD = config.HallDepth * 0.5f;
            foreach (float z in new[] { -halfD * 0.85f, -halfD * 0.42f, 0f, halfD * 0.62f })
            {
                Pair(inX - 1.2f, x =>
                {
                    var go = new GameObject($"WindowLight_{z:F0}");
                    go.transform.SetParent(group, false);
                    go.transform.position = new Vector3(x, 3.4f, z);

                    var light = go.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.range = 9f;
                    light.intensity = 2.2f;
                    light.color = new Color(0.85f, 0.91f, 1f);
                    light.shadows = LightShadows.None;
                    lights++;
                });
            }

            // Софиты платформ. Одна и та же яркость на оба: разница здесь —
            // это подсказка, на какую платформу бежать.
            Pair(offset, x => Beam(group, "PlatformBeam", new Vector3(x, config.CeilingHeight - 0.5f, platformsZ),
                new Color(1f, 0.97f, 0.9f), 5.5f, 46f));

            Beam(group, "PodiumBeam",
                new Vector3(0f, config.CeilingHeight - 0.5f, config.HallDepth * 0.5f - config.PodiumDepth * 0.5f - 0.8f),
                new Color(1f, 0.95f, 0.86f), 6.5f, 34f);

            // Рассеянный поднимается выше стандартного: зал перекрыт потолком,
            // направленный свет в него не попадает, и на единице интерьер
            // уходит в тёмно-серое (урок 3.70).
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.43f, 0.46f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.33f, 0.32f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.21f, 0.20f);
            RenderSettings.ambientIntensity = 1f;
        }

        private static void Beam(Transform group, string name, Vector3 position, Color color, float intensity, float angle)
        {
            var go = new GameObject(name);
            go.transform.SetParent(group, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            var light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = angle;
            light.range = 12f;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;
            lights++;
        }

        // ────────────────────────────────────────────────────────────────
        // Общая механика
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Поставить предмет на обеих половинах зала. Единственный способ
        /// расставлять здесь что бы то ни было: асимметрия — это подсказка,
        /// на какую платформу бежать, а игра построена на том, что подсказок
        /// нет. Ноль по X ставит предмет один раз, по оси.
        /// </summary>
        private static void Pair(float x, System.Action<float> place)
        {
            if (Mathf.Abs(x) < 0.01f)
            {
                place(0f);
                return;
            }

            place(Mathf.Abs(x));
            place(-Mathf.Abs(x));
        }

        /// <summary>
        /// Перекрасить модель, не трогая стекло. Оконный модуль собран из стены,
        /// рамы и стекла в разных слотах: покрась всё — и окно превратится
        /// в глухую плиту ровно там, где оно единственный источник света.
        /// </summary>
        private static void PaintExceptGlass(GameObject go, Material tone)
        {
            if (tone == null)
            {
                return;
            }

            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null || slots[i].name.IndexOf("Glass", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    slots[i] = tone;
                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = slots;
                }
            }
        }

        /// <summary>
        /// Холст портрета: плоскость со сгенерированной картинкой поверх
        /// абстракции пака. Картинки нет — холст остаётся тёмным полем, и рама
        /// всё равно читается портретом, а не окном в мультфильм.
        /// </summary>
        private static void Canvas(Transform parent, string name, int index, Vector3 center, float width, float height,
            float yaw = 0f)
        {
            Material tone = PortraitMaterial(index);
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            ExamDress.Scenery(go, tone);
            go.transform.SetParent(parent, false);
            // По умолчанию без доворота: у примитива Quad нормаль смотрит
            // на −Z, то есть уже в зал с дальней стены. Разворот на 180°
            // отворачивал холст от зрителя, и в раме оставалась видна
            // абстракция пака — ровно то, ради чего холст и ставится.
            // Портретам ближней стены доворот нужен: они смотрят в другую сторону.
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = new Vector3(width, height, 1f);
            go.transform.position = center;
            placed++;
        }

        private static readonly Dictionary<int, Material> portraitCache = new Dictionary<int, Material>(4);

        private static Material PortraitMaterial(int index)
        {
            if (portraitCache.TryGetValue(index, out Material cached) && cached != null)
            {
                return cached;
            }

            string materialPath = $"{PortraitFolder}/Exam_Portrait_{index + 1:00}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    return null;
                }

                if (!AssetDatabase.IsValidFolder(PortraitFolder))
                {
                    Note("папки портретов нет — рамы останутся с абстракцией пака");
                    return null;
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PortraitFolder}/Exam_Portrait_{index + 1:00}.png");
            material.SetFloat("_Smoothness", 0.04f);
            material.SetFloat("_Metallic", 0f);
            if (texture != null)
            {
                material.mainTexture = texture;
                material.SetTexture("_BaseMap", texture);
                material.color = Color.white;
                material.SetColor("_BaseColor", Color.white);
            }
            else
            {
                Note($"портрет Exam_Portrait_{index + 1:00}.png не найден — холст остался тёмным");
                material.color = new Color(0.14f, 0.12f, 0.11f);
                material.SetColor("_BaseColor", new Color(0.14f, 0.12f, 0.11f));
            }

            EditorUtility.SetDirty(material);
            portraitCache[index] = material;
            return material;
        }

        private static Transform Group(Transform parent, string name)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            group.localPosition = Vector3.zero;
            group.localRotation = Quaternion.identity;
            group.localScale = Vector3.one;
            return group;
        }

        private static void Note(string text)
        {
            if (!Notes.Contains(text))
            {
                Notes.Add(text);
            }
        }

        /// <summary>Сколько предметов и источников поставлено. Плотность деталей — часть приёмки.</summary>
        internal static string Report()
        {
            var report = new StringBuilder();
            report.Append("🏛 «Экзамен», окружение 4.3 — ")
                .Append(placed).Append(" предметов, ").Append(lights).Append(" источников света");
            report.Append("\n   всё расставлено парами: асимметрия в этой игре — подсказка");

            foreach (string note in Notes)
            {
                report.Append("\n⚠️ ").Append(note);
            }

            report.Append("\nЗамечаний окружения: ").Append(Notes.Count);
            return report.ToString();
        }
    }
}
