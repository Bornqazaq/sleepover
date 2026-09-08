using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Igruha.Minigames.BelieveOrNot;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Зал «Верю / не верю» — подфаза 4.3: всё, что стоит за кругом света.
    /// Бархат по стенам, барная стойка, мягкая мебель, картины и тёплые
    /// точки света, на которых зал читается силуэтами.
    ///
    /// <b>Восток и запад — да, север и юг — нет.</b> Сидящие смотрят вдоль
    /// оси Z, значит фоном геройского кадра работают стены North и South:
    /// всё, что на них поставить, встанет ровно за лицом соперника. Поэтому
    /// там только бархат, а бар, диваны и картины — на West и East. Это не
    /// вкус, это единственное требование брифа к планировке зала (14.7).
    ///
    /// <b>Ни одного коллайдера.</b> Вокруг стола весь кон бегают и дерутся
    /// шестеро; коллайдер на диване или на бутылке ловил бы их телами, а
    /// толчок в декорацию — это уже не декорация. Всё окружение уходит на
    /// <c>Default</c>: слоя нет в маске деоклюдера, и камера сквозь него
    /// проходит, вместо того чтобы нырять зрителю в затылок.
    ///
    /// <b>Свободная зона зрителей неприкосновенна.</b> Спека даёт радиус
    /// 10 ШП (7.2 м), внутри которого пусто и ровно. Каждый предмет проверяется
    /// по расстоянию до центра стола, и пересборка печатает ближайший:
    /// «не залезло» обязано быть числом.
    /// </summary>
    internal static class BelieveOrNotHall
    {
        private const string Casino = "Assets/Synty/PolygonCasino/Prefabs/";
        private const string Nightclubs = "Assets/Synty/PolygonNightclubs/Prefabs/";

        private const string CurtainPath = Casino + "Buildings/SM_Bld_Curtain_Closed_01.prefab";
        private const string BarPath = Nightclubs + "Props/Modular/SM_Prop_Bar_01.prefab";
        private const string MinibarPath = Casino + "Props/SM_Prop_Minibar_01.prefab";
        private const string StoolPath = Casino + "Props/SM_Prop_Bar_Stool_01.prefab";
        private const string BottlePath = Casino + "Props/SM_Prop_Bar_Bottle_02.prefab";
        private const string GlassPath = Casino + "Props/SM_Prop_Bar_Glass_02.prefab";
        private const string BucketPath = Casino + "Props/SM_Prop_Champagne_Bucket_02.prefab";
        private const string SofaPath = Casino + "Props/SM_Prop_Sofa_01.prefab";
        private const string CouchPath = Casino + "Props/SM_Prop_Couch_Suite_01.prefab";
        private const string BenchPath = Casino + "Props/SM_Prop_Bench_Pew_01.prefab";
        private const string LowTablePath = Casino + "Props/SM_Prop_Table_Suite_01.prefab";
        private const string ColumnPath = Casino + "Buildings/SM_Bld_Pillar_Lights_Base_01.prefab";
        private const string HighTablePath = Casino + "Props/SM_Prop_Bar_Table_01.prefab";
        private const string ArmchairPath = Casino + "Props/SM_Prop_Chair_Suite_02.prefab";
        private const string SideTablePath = Casino + "Props/SM_Prop_Table_Suite_02.prefab";
        private const string RugPath = Nightclubs + "Props/SM_Prop_Rug_01.prefab";
        private const string TrayPath = Casino + "Props/SM_Prop_Server_Tray_01.prefab";
        private const string FluteGlassPath = Casino + "Props/SM_Prop_Champagne_Glass_01.prefab";
        private const string CigarettePath = Casino + "Items/SM_Item_Cigarette_Pack_01.prefab";
        private const string CardDeckPath = Casino + "Items/Cards/SM_Item_Card_Deck_01.prefab";
        private const string WallArtPath = Casino + "Props/SM_Prop_Wall_Art_02.prefab";
        private const string WallArtAltPath = Casino + "Props/SM_Prop_Wall_Art_01.prefab";

        /// <summary>Стол рулетки 2.69 × 1.25 × 1.81 — угловой якорь восточной половины.</summary>
        private const string RoulettePath = Casino + "Props/SM_Prop_Roulette_Table_01.prefab";

        /// <summary>Покерный стол 3.34 × 0.97 × 1.35 — угловой якорь западной половины.</summary>
        private const string PokerPath = Casino + "Props/SM_Prop_Poker_Table_01.prefab";

        /// <summary>Игровой автомат 0.90 × 2.86 × 1.11 — единственная вертикаль в кольце мебели.</summary>
        private const string SlotPath = Casino + "Props/SM_Prop_Slot_Machine_01.prefab";

        /// <summary>Неон 4.08 × 2.67 — вывеска над баром и над лаунжем.</summary>
        private const string NeonWidePath = Casino + "Props/SM_Prop_Casino_Neon_11.prefab";

        /// <summary>Неон 0.86 × 2.59 — вертикальная вывеска в простенках.</summary>
        private const string NeonTallPath = Casino + "Props/SM_Prop_Casino_Neon_07.prefab";

        /// <summary>Кадка с растением 0.96 × 1.84 × 1.00.</summary>
        private const string PlantPath = Casino + "Props/SM_Prop_Pot_Plants_03.prefab";

        /// <summary>Кадка пониже и пошире, 1.52 × 1.48 × 1.46.</summary>
        private const string PlantWidePath = Casino + "Props/SM_Prop_Pot_Plants_01.prefab";

        private const string HallRoot = "_Hall";

        /// <summary>Сколько полотнищ шторы приходится на одну стену.</summary>
        private const int CurtainsPerWall = 6;

        /// <summary>Полотнищ с каждого края восточной и западной стен; середина отдана бару и диванам.</summary>
        private const int CurtainsAtEnds = 2;

        /// <summary>Отступ предмета от плоскости стены, м: вплотную модель проваливается в стену углами.</summary>
        private const float WallGap = 0.06f;

        private static readonly List<string> notes = new List<string>(8);
        private static float nearest;
        private static int props;

        /// <summary>
        /// Построить зал. <paramref name="wallFace"/> — расстояние от центра
        /// до внутренней плоскости стены.
        /// </summary>
        internal static void Build(Transform arena, BelieveOrNotConfig config, float wallFace)
        {
            if (arena == null || config == null)
            {
                return;
            }

            notes.Clear();
            nearest = float.MaxValue;
            props = 0;

            var root = new GameObject(HallRoot);
            root.transform.SetParent(arena, false);
            Transform hall = root.transform;

            Curtains(hall, config, wallFace);
            Bar(hall, wallFace);
            Lounge(hall, wallFace);
            GamingFloor(hall, config, wallFace);
            Corners(hall, config, wallFace);
            Lights(hall, wallFace);

            notes.Add($"предметов окружения {props}, ближайший к центру {nearest:F2} м " +
                      $"при свободной зоне {config.SpectatorZoneRadius:F2} м");
        }

        /// <summary>Замеры зала для отчёта пересборки.</summary>
        internal static IReadOnlyList<string> Notes
        {
            get { return notes; }
        }

        // ========== БАРХАТ ==========

        /// <summary>
        /// Шторы по стенам. Север и юг закрыты целиком, у востока и запада
        /// занавешены только края: середина отдана бару и диванам.
        ///
        /// Полотнище пака — 5.00 × 5.94 м, и оно подгоняется под стену
        /// по обеим осям: шесть штук ровно закрывают 24.48 м, а по высоте
        /// ткань садится с потолка на пол. Растяжение выходит 0.816 против
        /// 0.848 — разницы в три процента на складках не видно.
        /// </summary>
        private static void Curtains(Transform hall, BelieveOrNotConfig config, float wallFace)
        {
            float span = config.HallWidth / CurtainsPerWall;
            var scale = new Vector3(span / 5.00f, config.CeilingHeight / 5.94f, config.CeilingHeight / 5.94f);
            var group = Group(hall, "Curtains");

            for (int i = 0; i < CurtainsPerWall; i++)
            {
                float offset = -config.HallWidth * 0.5f + span * (i + 0.5f);
                bool atEnd = i < CurtainsAtEnds || i >= CurtainsPerWall - CurtainsAtEnds;

                Prop(group, "Curtain_N", CurtainPath, new Vector3(offset, 0f, wallFace - WallGap), 180f, scale);
                Prop(group, "Curtain_S", CurtainPath, new Vector3(offset, 0f, -wallFace + WallGap), 0f, scale);

                if (!atEnd)
                {
                    continue;
                }

                Prop(group, "Curtain_E", CurtainPath, new Vector3(wallFace - WallGap, 0f, offset), 270f, scale);
                Prop(group, "Curtain_W", CurtainPath, new Vector3(-wallFace + WallGap, 0f, offset), 90f, scale);
            }

            notes.Add($"шторы: {CurtainsPerWall * 2 + CurtainsAtEnds * 4} полотнищ, " +
                      $"полотнище {span:F2} × {config.CeilingHeight:F2} м");
        }

        // ========== БАР ==========

        /// <summary>
        /// Барная зона у восточной стены: стойка из трёх модулей, две тумбы
        /// за ней, табуреты, бутылки и ведро со льдом.
        ///
        /// Стойка стоит фронтом в зал и на два с лишним метра дальше границы
        /// свободной зоны: зритель, отбежавший от стола, упирается не в неё,
        /// а в пустой пол.
        /// </summary>
        private static void Bar(Transform hall, float wallFace)
        {
            Transform group = Group(hall, "Bar");

            // Отступы считаются от стены, а не абсолютными числами. Абсолютные
            // 10.6 писались под зал 24.48 м и пережили бы ужатие до 17.28
            // молча: стойка вышла бы за стену, а табуреты встали бы в неё.
            float counterX = wallFace - 1.15f;
            const float counterWidth = 2.53f;
            const float counterTop = 1.06f;

            for (int i = -1; i <= 1; i++)
            {
                Prop(group, "BarCounter", BarPath, new Vector3(counterX, 0f, i * counterWidth), 90f);
            }

            Prop(group, "BackBar", MinibarPath, new Vector3(wallFace - 0.5f, 0f, 2.2f), 270f);
            Prop(group, "BackBar", MinibarPath, new Vector3(wallFace - 0.5f, 0f, -2.2f), 270f);

            for (int i = -1; i <= 1; i++)
            {
                Prop(group, "Stool", StoolPath, new Vector3(counterX - 0.85f, 0f, i * 2.2f), 90f);
            }

            // Мелочь на стойке. Она ниже кромки и видна только силуэтом
            // на просвет, но именно от неё стойка перестаёт быть бруском.
            Prop(group, "Bottle", BottlePath, new Vector3(counterX + 0.15f, counterTop, -1.9f), 20f);
            Prop(group, "Bottle", BottlePath, new Vector3(counterX + 0.15f, counterTop, -1.6f), 200f);
            Prop(group, "Glass", GlassPath, new Vector3(counterX - 0.1f, counterTop, 0.4f), 0f);
            Prop(group, "Glass", GlassPath, new Vector3(counterX - 0.1f, counterTop, 0.7f), 120f);
            Prop(group, "Bucket", BucketPath, new Vector3(counterX, counterTop, 2.1f), 0f);

            Prop(group, "WallArt", WallArtPath, new Vector3(wallFace - WallGap, 0.3f, 4.9f), 270f);
            Prop(group, "WallArt", WallArtPath, new Vector3(wallFace - WallGap, 0.3f, -4.9f), 270f);
        }

        // ========== МЯГКАЯ ЗОНА ==========

        /// <summary>
        /// Западная стена: диваны, банкетки, низкие столики и картины
        /// в тяжёлых рамах — место, где зритель «дома».
        /// </summary>
        private static void Lounge(Transform hall, float wallFace)
        {
            Transform group = Group(hall, "Lounge");

            // Три пояса от стены внутрь: спинки у стены, ковёр перед ними,
            // столики по внутренней кромке кольца. Числа — отступы от стены,
            // по той же причине, что и в баре.
            float backX = -(wallFace - 1.15f);
            float benchX = -(wallFace - 0.75f);
            float rugX = -(wallFace - 1.85f);
            float tableX = -(wallFace - 2.25f);

            Prop(group, "Sofa", SofaPath, new Vector3(backX, 0f, -2.4f), 90f);
            Prop(group, "Couch", CouchPath, new Vector3(backX + 0.2f, 0f, 2.6f), 90f);
            Prop(group, "Bench", BenchPath, new Vector3(benchX, 0f, -6.6f), 90f);
            Prop(group, "Bench", BenchPath, new Vector3(benchX, 0f, 6.6f), 90f);

            Prop(group, "Rug", RugPath, new Vector3(rugX, 0f, 0.1f), 0f);

            Prop(group, "LowTable", LowTablePath, new Vector3(tableX, 0f, -2.4f), 0f);
            Prop(group, "LowTable", LowTablePath, new Vector3(tableX, 0f, 2.6f), 0f);

            // Мелочь на столиках: поднос с бокалами и пачка сигарет. Высота
            // столика 0.37 м — на неё и садится всё, что на нём стоит.
            Prop(group, "Tray", TrayPath, new Vector3(tableX, 0.37f, -2.4f), 0f);
            Prop(group, "Glass", FluteGlassPath, new Vector3(tableX + 0.15f, 0.42f, -2.3f), 0f);
            Prop(group, "Glass", FluteGlassPath, new Vector3(tableX - 0.15f, 0.42f, -2.5f), 0f);
            Prop(group, "Glass", GlassPath, new Vector3(tableX, 0.37f, 2.6f), 0f);
            Prop(group, "Cigarettes", CigarettePath, new Vector3(tableX + 0.2f, 0.37f, 2.75f), 25f);

            // На стене — золочёный вензель, а не картины пака.
            //
            // Картины пробовались первыми: спека прямо просит «картины
            // в тяжёлых рамах» (4.7). Но холсты Casino — крупная абстракция
            // в жёлтом, бирюзовом и малиновом, и в зале, где светлое пятно
            // обязано быть ровно одно, они стали вторым: рендер 04.09 показал
            // две светящиеся рамы прямо за коробками. Перекрасить их нельзя —
            // холст и рама сидят в одном атласе, и заливка превратила бы
            // картину в тёмную плиту. Вензель даёт ту же «дорогую стену»
            // и не спорит с лампой.
            Prop(group, "WallArt", WallArtAltPath, new Vector3(-wallFace + WallGap, 0.3f, -4.9f), 90f);
            Prop(group, "WallArt", WallArtAltPath, new Vector3(-wallFace + WallGap, 0.3f, 4.9f), 90f);
        }

        // ========== ИГРОВОЙ ЗАЛ ==========

        /// <summary>
        /// Автоматы, вывески, кадки и два стоячих столика — всё, что
        /// заполняет кольцо между свободной зоной и стенами по востоку
        /// и западу.
        ///
        /// <b>Почему это понадобилось.</b> До ужатия зала кольцо было
        /// шириной пять метров, и бар с диванами занимали в нём середину;
        /// остальное честно оставалось пустым, потому что поставить туда
        /// что-то значило поставить это в десяти метрах от стола, где
        /// предмет уже не читается. Ужатый зал даёт кольцо в три метра,
        /// целиком попадающее в кадр зрителя, — и оно обязано быть занято.
        ///
        /// <b>Север и юг не трогаем.</b> Сидящие смотрят вдоль оси Z, и всё
        /// поставленное там встанет за лицом соперника в геройском кадре
        /// (бриф 14.7). Правило старое, ужатие зала его не отменяет —
        /// наоборот, делает строже: стена стала ближе к лицу.
        ///
        /// <b>Вывески светятся, но не спорят с лампой.</b> Неон — это
        /// собственный эмиссивный материал модели, а не источник света:
        /// он виден в темноте пятном на стене и не кладёт ни люмена
        /// на пол, по которому меряется «светлое пятно ровно одно».
        /// </summary>
        private static void GamingFloor(Transform hall, BelieveOrNotConfig config, float wallFace)
        {
            Transform group = Group(hall, "Gaming");

            // Автоматы стоят спиной к стене, по два у каждой боковой стены,
            // разнесённые к северному и южному краям — там кольцо свободно
            // от бара и от диванов.
            float slotX = wallFace - 0.65f;
            foreach (float z in new[] { -6.2f, 6.2f })
            {
                for (int i = 0; i < 2; i++)
                {
                    float offset = (i - 0.5f) * 1.05f;
                    Prop(group, "SlotMachine", SlotPath, new Vector3(slotX, 0f, z + offset), 270f);
                    Prop(group, "SlotMachine", SlotPath, new Vector3(-slotX, 0f, z + offset), 90f);
                }
            }

            // Вывески над баром и над лаунжем — на высоте, где стена
            // остаётся пустой: выше спинок и ниже карниза.
            float signX = wallFace - 0.08f;
            Prop(group, "NeonWide", NeonWidePath, new Vector3(signX, 2.35f, 0f), 270f);
            Prop(group, "NeonWide", NeonWidePath, new Vector3(-signX, 2.35f, 0f), 90f);

            foreach (float z in new[] { -4.3f, 4.3f })
            {
                Prop(group, "NeonTall", NeonTallPath, new Vector3(signX, 1.5f, z), 270f);
                Prop(group, "NeonTall", NeonTallPath, new Vector3(-signX, 1.5f, z), 90f);
            }

            // Кадки в стыках зон: между баром и автоматами, между диванами
            // и автоматами. Растение — единственный предмет кольца с рваным
            // силуэтом, и на фоне прямых линий мебели именно оно не даёт
            // кольцу читаться забором.
            float plantX = wallFace - 1.0f;
            foreach (float z in new[] { -3.9f, 3.9f })
            {
                Prop(group, "Plant", PlantPath, new Vector3(plantX, 0f, z), 0f);
                Prop(group, "Plant", PlantPath, new Vector3(-plantX, 0f, z), 0f);
            }

            Prop(group, "PlantWide", PlantWidePath, new Vector3(plantX, 0f, -8.0f), 0f);
            Prop(group, "PlantWide", PlantWidePath, new Vector3(-plantX, 0f, 8.0f), 0f);

            // Стоячие столики: переехали из углов, которые отдали игровым
            // столам. Стоят по внутренней кромке кольца — там, где зритель
            // отбегает от стола и упирается взглядом в пустоту.
            float standX = wallFace - 2.4f;
            StandingTable(group, new Vector3(standX, 0f, -5.3f));
            StandingTable(group, new Vector3(-standX, 0f, 5.3f));

            notes.Add($"игровой зал: 8 автоматов, 6 вывесок, 6 кадок, ближняя кромка кольца {standX:F2} м " +
                      $"при свободной зоне {config.SpectatorZoneRadius:F2} м");
        }

        // ========== УГЛЫ ==========

        /// <summary>
        /// Углы зала: колонны от пола до потолка, стоячие столики с табуретами
        /// и пара кресел со столиком.
        ///
        /// Зачем вообще: после первой приёмки зал читался пустым. Четыре стены
        /// в бархате и две зоны у стен оставляют между ними двадцать метров
        /// голого пола, и в кадре зрителя, отбежавшего от стола, не было
        /// ничего. Углы — единственное место, где объём можно поставить,
        /// не залезая ни в свободную зону, ни на фон геройского кадра.
        ///
        /// Колонна пака — три метра, потолок — пять с небольшим. Она тянется
        /// по высоте, а не масштабируется целиком: колонна — призма, вытяжка
        /// вдоль оси её не искажает, а равномерный масштаб раздул бы её
        /// в тумбу диаметром почти три метра.
        /// </summary>
        private static void Corners(Transform hall, BelieveOrNotConfig config, float wallFace)
        {
            Transform group = Group(hall, "Corners");
            float column = wallFace - 1.25f;
            float cluster = wallFace - 2.3f;
            var stretch = new Vector3(1f, config.CeilingHeight / 3.00f, 1f);

            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Prop(group, "Column", ColumnPath, new Vector3(sx * column, 0f, sz * column), 0f, stretch);
                }
            }

            // Игровые столы по диагонали — рулетка и покер.
            //
            // Раньше здесь стояли два стоячих столика: они держали угол,
            // пока зал был на шесть метров шире и любой предмет в нём читался
            // мелким. В ужатом зале угол — это уже полноценный второй план,
            // и столик на 1.1 м в нём просто теряется. Рулетка и покерный
            // стол дают то, чего в зале не было вовсе: он перестаёт быть
            // комнатой с одним столом и становится залом, где этот стол —
            // не единственная игра. Столики переехали к бару и в лаунж.
            GamingTable(group, RoulettePath, new Vector3(cluster, 0f, -cluster), 30f);
            GamingTable(group, PokerPath, new Vector3(-cluster, 0f, cluster), -150f);

            // Два кресла со столиком между ними — угол, где сидят и смотрят.
            Prop(group, "Armchair", ArmchairPath, new Vector3(cluster - 0.9f, 0f, cluster), 250f);
            Prop(group, "Armchair", ArmchairPath, new Vector3(cluster + 0.9f, 0f, cluster + 0.6f), 70f);
            GameObject side = Prop(group, "SideTable", SideTablePath, new Vector3(cluster, 0f, cluster + 1.5f), 0f);
            if (side != null)
            {
                Prop(group, "CardDeck", CardDeckPath, new Vector3(cluster, 0.52f, cluster + 1.5f), 15f);
            }

            Prop(group, "Armchair", ArmchairPath, new Vector3(-cluster + 0.9f, 0f, -cluster), 70f);
            Prop(group, "Armchair", ArmchairPath, new Vector3(-cluster - 0.9f, 0f, -cluster - 0.6f), 250f);
            Prop(group, "SideTable", SideTablePath, new Vector3(-cluster, 0f, -cluster - 1.5f), 0f);
            Prop(group, "Cigarettes", CigarettePath, new Vector3(-cluster + 0.2f, 0.52f, -cluster - 1.5f), 40f);
        }

        /// <summary>Стоячий столик с двумя табуретами и брошенным бокалом.</summary>
        private static void StandingTable(Transform group, Vector3 place)
        {
            Prop(group, "HighTable", HighTablePath, place, 0f);
            Prop(group, "Stool", StoolPath, place + new Vector3(0.85f, 0f, 0.2f), 250f);
            Prop(group, "Stool", StoolPath, place + new Vector3(-0.85f, 0f, -0.2f), 70f);
            Prop(group, "Glass", FluteGlassPath, place + new Vector3(0.15f, 1.10f, 0.1f), 0f);
        }

        /// <summary>
        /// Игровой стол в углу: сам стол, три табурета по внешней дуге
        /// и брошенная колода. Табуреты стоят спинами к стене — за столом
        /// в углу садятся лицом в зал, и пустая дуга со стороны зрителя
        /// читается «отсюда только что встали».
        /// </summary>
        private static void GamingTable(Transform group, string path, Vector3 place, float yaw)
        {
            GameObject table = Prop(group, "GamingTable", path, place, yaw);
            if (table == null)
            {
                return;
            }

            Vector3 outward = new Vector3(Mathf.Sign(place.x), 0f, Mathf.Sign(place.z));
            for (int i = -1; i <= 1; i++)
            {
                Vector3 along = new Vector3(-outward.z, 0f, outward.x) * (i * 0.95f);
                Prop(group, "Stool", StoolPath, place + outward * 1.25f + along, yaw + 180f);
            }

            Prop(group, "CardDeck", CardDeckPath, place + new Vector3(0.25f, 0.95f, 0.15f), yaw + 40f);
        }

        // ========== СВЕТ ЗАЛА ==========

        /// <summary>
        /// Тёплые точки над баром и мягкой зоной.
        ///
        /// Зачем они, если зал по спеке тёмный: без них восток и запад
        /// проваливаются в ту же черноту, что север и юг, и предметы,
        /// которые мы только что поставили, в кадре не существуют. Светят
        /// они втрое слабее лампы над столом и не достают до неё — светлое
        /// пятно в кадре обязано остаться ровно одно.
        ///
        /// Ставятся кодом, а не инспектором: настройки света в YAML сцены
        /// не переживают слияние веток.
        /// </summary>
        private static void Lights(Transform hall, float wallFace)
        {
            Transform group = Group(hall, "Lights");
            var warm = new Color(1f, 0.82f, 0.62f);
            float barX = wallFace - 1.35f;
            float loungeX = -(wallFace - 1.75f);

            Lamp(group, "BarLight", new Vector3(barX, 3.2f, 2.3f), warm, 2.5f, 7f);
            Lamp(group, "BarLight", new Vector3(barX, 3.2f, -2.3f), warm, 2.5f, 7f);
            Lamp(group, "LoungeLight", new Vector3(loungeX, 3.0f, 0f), warm, 2.0f, 8f);

            // Точки над игровыми столами в углах. Слабее барных: рулетка
            // и покер стоят дальше от стола, и пятно над ними обязано
            // читаться силуэтом мебели, а не вторым центром кадра.
            float cornerX = wallFace - 2.3f;
            Lamp(group, "GamingLight", new Vector3(cornerX, 2.9f, -cornerX), warm, 1.5f, 6f);
            Lamp(group, "GamingLight", new Vector3(-cornerX, 2.9f, cornerX), warm, 1.5f, 6f);

            // Заливка бархата севера и юга — самые слабые источники зала.
            //
            // Эти две стены и есть фон геройского кадра: в них упирается взгляд
            // сидящего поверх плеча соперника. Без заливки они уходили в тот же
            // чёрный, что и потолок, и соперник читался тёмной фигурой на тёмном
            // фоне — то есть не читался. Светят вниз по ткани, до стола не
            // достают (радиус 6 при расстоянии 7.3 м до его кромки) и светлое
            // пятно кадра не трогают.
            float velvetZ = wallFace - 1.2f;
            Lamp(group, "VelvetWash", new Vector3(0f, 3.4f, velvetZ), warm, 1.2f, 6f);
            Lamp(group, "VelvetWash", new Vector3(0f, 3.4f, -velvetZ), warm, 1.2f, 6f);

            notes.Add("свет зала: две точки над баром (2.5), одна над диванами (2.0), " +
                      "две над игровыми столами (1.5), две заливки бархата (1.2), тени выключены");
        }

        private static void Lamp(Transform parent, string name, Vector3 position, Color color, float intensity,
            float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;

            // Тени от заполняющего света стоят дорого и ничего не добавляют:
            // предметы у стен и так читаются силуэтом.
            light.shadows = LightShadows.None;
        }

        // ========== ОБЩЕЕ ==========

        private static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>
        /// Поставить предмет пака по замеру его собственных габаритов:
        /// центр пятна — в заданную точку, низ — на заданную высоту.
        ///
        /// Именно по замеру, а не по пивоту: у моделей Synty точка отсчёта
        /// стоит где придётся — у шторы она на краю полотнища, у картины
        /// в плоскости рамы. Пивот, принятый за центр, ставит штору в стену
        /// наполовину.
        /// </summary>
        private static GameObject Prop(Transform parent, string name, string path, Vector3 place, float yaw,
            Vector3? scale = null)
        {
            if (!DressKit.TryLoad(path, out GameObject prefab))
            {
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            if (scale.HasValue)
            {
                go.transform.localScale = scale.Value;
            }

            Scenery(go);

            if (!TryBounds(go, out Bounds bounds))
            {
                return go;
            }

            go.transform.position += new Vector3(place.x - bounds.center.x, place.y - bounds.min.y,
                place.z - bounds.center.z);

            props++;
            if (TryBounds(go, out Bounds placed))
            {
                // Считаем расстояние до ближайшей точки габарита, а не до его
                // центра минус диагональ: диагональ занижает ответ на предметах
                // вытянутых, и ковёр 2 × 4.6 м «залезал» в зону, стоя от неё
                // в полутора метрах.
                float dx = Mathf.Max(0f, Mathf.Abs(placed.center.x) - placed.extents.x);
                float dz = Mathf.Max(0f, Mathf.Abs(placed.center.z) - placed.extents.z);
                nearest = Mathf.Min(nearest, Mathf.Sqrt(dx * dx + dz * dz));
            }

            return go;
        }

        /// <summary>
        /// Пометить предмет декорацией: снять коллайдеры, увести на
        /// <c>Default</c>, погасить отброс теней.
        /// </summary>
        private static void Scenery(GameObject go)
        {
            Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Object.DestroyImmediate(colliders[i], true);
            }

            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers[i].gameObject.layer = 0;
            }

            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }

        private static bool TryBounds(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds();
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
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
    }
}
