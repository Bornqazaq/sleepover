using UnityEditor;
using UnityEngine;
using Igruha.Minigames.Circus;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Окружение и свет циркового шатра — подфаза 4.3, общая на «Секундомер»
    /// и «Порядок банок».
    ///
    /// <b>Ни одного коллайдера.</b> Всё, что здесь ставится, — декорация:
    /// коллайдеры срезаются, слой Default, тени не отбрасываются. Цена
    /// нарушения в этой игре выше обычного: лишний коллайдер на настиле
    /// поймает выпавшего из клетки, а лишний коллайдер на ферме — цепь.
    ///
    /// <b>Запретные зоны — не рекомендация, а условие игры.</b>
    /// <list type="bullet">
    /// <item>Кольцо клеток (R 5.76…8.64 м) свободно от пола до фермы: по
    /// просветам между клетками игрок сравнивает свою высоту с чужой, и это
    /// единственный интерфейс «Секундомера». Одна растяжка поперёк отменяет
    /// его целиком.</item>
    /// <item>Яма пуста, кроме плоского мусора: спека 3.3 говорит «укрытий
    /// внутри нет, бежать можно только по кругу». Тюк сена посреди ямы —
    /// это укрытие, и забег от медведя перестаёт быть забегом.</item>
    /// </list>
    ///
    /// <b>Планировка — четыре кольца, а не россыпь.</b> Настил шатра шириной
    /// девять метров, и разбросанный по нему реквизит читается свалкой.
    /// Кольцами он читается ярмаркой:
    /// <list type="number">
    /// <item><b>R 9.7</b> — барьер вокруг ямы. Он же объясняет яму: к ней
    /// не подходят.</item>
    /// <item><b>R 12.2</b> — трибуны с четырьмя проходами.</item>
    /// <item><b>R 15.6</b> — балаганный ряд: палатки, аттракционы, фургоны.</item>
    /// <item><b>R 17.6</b> — стена: афиши, занавес, призы, гирлянды.</item>
    /// </list>
    /// Между кольцами — мелочь вразброс, она и убирает ощущение пустого пола.
    /// </summary>
    internal static class CircusEnvironment
    {
        private const string Carnival = "Assets/Synty/PolygonHorrorCarnival/Prefabs/";
        private const string Props = Carnival + "Props/";
        private const string Vehicles = Carnival + "Vehicles/";
        private const string Buildings = Carnival + "Building/";
        private const string Weapons = Carnival + "Weapons/";
        private const string Casino = "Assets/Synty/PolygonCasino/Prefabs/Buildings/";

        // ---- Кольцо 1: барьер вокруг ямы
        private static readonly string[] Barricades =
        {
            Props + "SM_Prop_Barricade_01.prefab",
            Props + "SM_Prop_Barricade_02.prefab",
            Props + "SM_Prop_Barricade_03.prefab",
            Props + "SM_Prop_Barricade_04.prefab",
            Props + "SM_Prop_Barricade_Connectors_01.prefab",
            Props + "SM_Prop_Barricade_Connectors_02.prefab"
        };

        private const string BleacherPath = Props + "SM_Prop_Bleachers_Straight_01.prefab";
        private const string StairsPath = Buildings + "SM_Bld_Stairs_Small_01.prefab";
        private const string EntrancePath = Props + "SM_Prop_Carnival_Entrance_01.prefab";

        // ---- Кольцо 3: балаганный ряд. Крупное, ставится поимённо.
        private static readonly string[] BigStands =
        {
            Props + "SM_Prop_Stall_01.prefab",
            Props + "SM_Prop_Stall_02.prefab",
            Props + "SM_Prop_Stall_01_Damaged_01.prefab",
            Props + "SM_Prop_Stall_02_Damaged_01.prefab",
            Props + "SM_Prop_Stall_03.prefab",
            Props + "SM_Prop_Ticket_Booth_01.prefab",
            Props + "SM_Prop_Cart_Candyfloss_01.prefab",
            Vehicles + "SM_Veh_Truck_Food_01.prefab",
            Vehicles + "SM_Veh_Wagon_Cage_01.prefab",
            Vehicles + "SM_Veh_Carnival_Train_01.prefab",
            Props + "SM_Prop_High_Striker_01.prefab",
            Props + "SM_Prop_High_Striker_02.prefab",
            Props + "SM_Prop_Prize_Wheel_01.prefab",
            Props + "SM_Prop_Target_Wheel_01.prefab",
            Props + "SM_Prop_Can_Toss_01.prefab",
            Props + "SM_Prop_Ring_Toss_01.prefab",
            Props + "SM_Prop_Pie_Throwing_Wall_01.prefab",
            Props + "SM_Prop_Target_Ducks_01.prefab",
            Props + "SM_Prop_Clown_Box_01.prefab",
            Props + "SM_Prop_Photo_Stand_01.prefab",
            Weapons + "SM_Wep_Cannon_01.prefab",
            Props + "SM_Prop_Stall_Closed_01.prefab"
        };

        // ---- Кольцо 4: стена шатра
        private static readonly string[] Posters =
        {
            Props + "SM_Prop_Sign_Poster_01.prefab",
            Props + "SM_Prop_Sign_Poster_02.prefab"
        };

        private static readonly string[] Signs =
        {
            Props + "SM_Prop_Sign_Freakshow_01.prefab",
            Props + "SM_Prop_Sign_Tickets_01.prefab",
            Props + "SM_Prop_Sign_Show_01_Alt.prefab",
            Props + "SM_Prop_Sign_Games_01.prefab",
            Props + "SM_Prop_Sign_Prizes_01.prefab",
            Props + "SM_Prop_Sign_Win_01.prefab",
            Props + "SM_Prop_Sign_Boom_01.prefab",
            Props + "SM_Prop_Sign_Ball_Toss_01.prefab"
        };

        private static readonly string[] PrizeWalls =
        {
            Props + "SM_Prop_Plushies_Hanging_01.prefab",
            Props + "SM_Prop_Plushies_Hanging_02.prefab",
            Props + "SM_Prop_Plushies_Pined_01.prefab",
            Props + "SM_Prop_Plushies_Pined_02.prefab"
        };

        private static readonly string[] Curtains =
        {
            Casino + "SM_Bld_Curtain_Closed_01.prefab",
            Casino + "SM_Bld_Curtain_Open_01.prefab"
        };

        private const string BuntingPath = Props + "SM_Prop_Flags_05.prefab";
        private const string BuntingShortPath = Props + "SM_Prop_Flags_03.prefab";
        private const string BulbLinePath = Props + "SM_Prop_Light_03.prefab";
        private const string BulbLineShortPath = Props + "SM_Prop_Light_02.prefab";
        private const string HangingBulbPath = Props + "SM_Prop_Light_04.prefab";
        private const string MastPath = Props + "SM_Prop_Bunting_Pole_01.prefab";
        private const string SpeakerPath = Props + "SM_Prop_Loud_Speaker_01.prefab";
        private const string SweepLightPath = "Assets/Synty/PolygonNightclubs/Prefabs/Props/SM_Prop_Light_Spotlight_03.prefab";

        private static readonly string[] LampPosts =
        {
            Props + "SM_Prop_Lamp_Post_01.prefab",
            Props + "SM_Prop_Lamp_Post_02.prefab",
            Props + "SM_Prop_Lamp_Post_03.prefab",
            Props + "SM_Prop_Light_Pole_01.prefab"
        };

        /// <summary>Мелочь по настилу: ящики, тюки, бочки, бидоны. Стоит между кольцами.</summary>
        private static readonly string[] Clutter =
        {
            Props + "SM_Prop_Trunk_01.prefab",
            Props + "SM_Prop_Trunk_02.prefab",
            Props + "SM_Prop_Barrel_01.prefab",
            Props + "SM_Prop_Barrel_02.prefab",
            Props + "SM_Prop_Hay_Bale_Round_01.prefab",
            Props + "SM_Prop_Hay_Bale_Square_01.prefab",
            Props + "SM_Prop_Hay_Seat_01.prefab",
            Props + "SM_Prop_Stool_01.prefab",
            Props + "SM_Prop_Rubbish_Bin_01.prefab",
            Props + "SM_Prop_Rubbish_Bin_02.prefab",
            Props + "SM_Prop_Generator_01.prefab",
            Props + "SM_Prop_Table_01.prefab",
            Props + "SM_Prop_Table_02.prefab",
            Props + "SM_Prop_Stall_Bench_01.prefab",
            Props + "SM_Prop_Plushies_Grouped_04.prefab",
            Props + "SM_Prop_Plushies_Grouped_06.prefab",
            Props + "SM_Prop_Laughing_Clown_01.prefab",
            Props + "SM_Prop_Balloon_02.prefab",
            Props + "SM_Prop_Balloon_04.prefab",
            Vehicles + "SM_Veh_Trolley_01.prefab",
            Vehicles + "SM_Veh_Wagon_Steps_02.prefab",
            Props + "SM_Prop_Barbell_01.prefab",
            Props + "SM_Prop_Popcorn_01.prefab",
            Props + "SM_Prop_Cable_01.prefab"
        };

        /// <summary>Мусор и износ. Всё ниже 0.20 м — такое можно класть даже на маршрут забега.</summary>
        private static readonly string[] Litter =
        {
            Props + "SM_Prop_Rubbish_Pile_01.prefab",
            Props + "SM_Prop_Rubbish_Pile_02.prefab",
            Props + "SM_Prop_Rubbish_Pile_03.prefab",
            Props + "SM_Prop_Rubbish_Pile_04.prefab",
            Props + "SM_Prop_Rubbish_Popcorn_01.prefab",
            Props + "SM_Prop_Rubbish_Popcorn_02.prefab",
            Props + "SM_Prop_Rubbish_Candy_01.prefab",
            Props + "SM_Prop_Rubbish_Napkin_01.prefab",
            Props + "SM_Prop_Rubbish_Hotdog_01.prefab",
            Props + "SM_Prop_Rubbish_Milkshake_01.prefab",
            Props + "SM_Prop_Papers_03.prefab",
            Props + "SM_Prop_Papers_04.prefab",
            Props + "SM_Prop_Papers_05.prefab",
            Props + "SM_Prop_Ticket_04.prefab",
            Props + "SM_Prop_Hoops_Hoola_01.prefab",
            Props + "SM_Prop_Cannon_Ball_01.prefab",
            Props + "SM_Prop_Cobwebs_01.prefab",
            Props + "SM_Prop_Cobwebs_02.prefab"
        };

        // ---- Радиусы колец, м. Настил идёт от 9.04 до 18.
        private const float BarrierRadius = 9.7f;
        private const float BleacherRadius = 12.2f;
        private const float StandRadius = 15.6f;
        private const float WallRadius = 17.6f;

        private const int BarrierCount = 26;
        private const int BleacherCount = 24;
        private const int StandCount = 22;
        private const int PosterCount = 22;
        private const int BuntingCount = 16;
        private const int RadialGarlands = 16;
        private const int MastCount = 6;
        private const int ClutterCount = 46;
        private const int LitterOnDeck = 60;
        private const int LitterInPit = 16;

        /// <summary>Проходов на трибунах. Через них видно балаганный ряд за ними, и кольцо перестаёт быть стеной.</summary>
        private const int AisleCount = 4;

        /// <summary>Мусор в яме — только от этой доли радиуса и наружу: центр отдан забегу.</summary>
        private const float PitLitterInnerFactor = 0.45f;

        private const float BuntingHeight = 7.5f;

        internal static void Build(Transform arena, CircusArenaConfig config, System.Random rng)
        {
            Transform root = ResetGroup(arena, "Environment");

            BuildPitBarrier(root, config, rng);
            BuildBleachers(root, config, rng);
            BuildStands(root, config, rng);
            BuildWallDecor(root, config, rng);
            BuildOverhead(root, config, rng);
            BuildClutter(root, config, rng);
            BuildLitter(root, config, rng);
            BuildSpotlights(root, config);
            BuildLighting(root, config);
        }

        /// <summary>
        /// Барьер по краю ямы. Не только украшение: он объясняет яму. Без него
        /// круглый провал посреди настила читается дырой в полу, а с оградой —
        /// местом, к которому не подходят. И даёт вторую линию у края, по
        /// которой глаз находит границу опилок.
        /// </summary>
        private static void BuildPitBarrier(Transform root, CircusArenaConfig config, System.Random rng)
        {
            Transform group = ResetGroup(root, "PitBarrier");
            for (int i = 0; i < BarrierCount; i++)
            {
                float angle = 360f / BarrierCount * i;
                Place(group, $"Barrier_{i + 1:00}", Barricades[rng.Next(Barricades.Length)],
                    angle, BarrierRadius, config.TentFloorHeight, angle);
            }
        }

        /// <summary>
        /// Трибуны кольцом с четырьмя проходами. Прямые секции, а не угловые:
        /// угловая вдвое дороже по треугольникам (1629 против 1062), а на
        /// 24 сегментах многоугольник и так читается кругом.
        ///
        /// Проходы важнее ровного кольца. Сплошная стена трибун закрывает
        /// балаганный ряд за собой, и весь третий слой пропадает зря; четыре
        /// разрыва с лесенками и аркой дают глубину.
        ///
        /// Трибуны пустые — зрителей в игре нет. Ряды нужны не для толпы,
        /// а как вторая шкала высоты: рядом с трибуной в человеческий рост
        /// сразу понятно, насколько высоко висят клетки.
        /// </summary>
        private static void BuildBleachers(Transform root, CircusArenaConfig config, System.Random rng)
        {
            Transform group = ResetGroup(root, "Bleachers");
            int aisleStep = BleacherCount / AisleCount;

            for (int i = 0; i < BleacherCount; i++)
            {
                float angle = 360f / BleacherCount * i;
                if (i % aisleStep == 0)
                {
                    // Проход: лесенка вместо секции, арка чуть дальше.
                    Place(group, $"Aisle_{i / aisleStep + 1}", StairsPath,
                        angle, BleacherRadius, config.TentFloorHeight, angle + 180f, 1.6f);
                    Place(group, $"AisleArch_{i / aisleStep + 1}", EntrancePath,
                        angle, BleacherRadius + 1.9f, config.TentFloorHeight, angle + 180f);
                    continue;
                }

                // Спинкой наружу: ряды поднимаются от ямы к стене, как в цирке.
                Place(group, $"Bleacher_{i + 1:00}", BleacherPath,
                    angle, BleacherRadius, config.TentFloorHeight, angle + 180f);
            }
        }

        /// <summary>
        /// Балаганный ряд по внешнему кольцу: палатки, аттракционы, фургоны.
        /// Каждая модель встречается один раз — этим ряд и читается ярмаркой,
        /// а не тиражом одной палатки.
        ///
        /// Всё лицом к центру: игрок смотрит на кольцо изнутри, и развёрнутая
        /// наружу палатка показала бы ему заднюю стенку.
        /// </summary>
        private static void BuildStands(Transform root, CircusArenaConfig config, System.Random rng)
        {
            Transform group = ResetGroup(root, "Stands");
            int count = Mathf.Min(StandCount, BigStands.Length);

            for (int i = 0; i < count; i++)
            {
                float angle = 360f / count * i + 8f;
                float radius = StandRadius + (float)(rng.NextDouble() - 0.5) * 1.1f;
                Place(group, $"Stand_{i + 1:00}", BigStands[i], angle, radius,
                    config.TentFloorHeight, angle + 180f);
            }
        }

        /// <summary>
        /// Стена шатра: афиши, вывески, стенды с призами, красный занавес,
        /// гирлянды флажков и лампочек в два яруса, фонари и рупоры.
        ///
        /// Всё прижато к стене. Внутрь кольца клеток не заходит ничего —
        /// там запретная зона.
        /// </summary>
        private static void BuildWallDecor(Transform root, CircusArenaConfig config, System.Random rng)
        {
            Transform group = ResetGroup(root, "WallDecor");
            float floor = config.TentFloorHeight;

            for (int i = 0; i < PosterCount; i++)
            {
                float angle = 360f / PosterCount * i + 5f;
                int kind = i % 5;
                string path;
                float height;
                float scale;

                switch (kind)
                {
                    case 0:
                        path = Signs[rng.Next(Signs.Length)];
                        height = floor + 6.4f;
                        scale = 1.8f;
                        break;
                    case 1:
                        path = PrizeWalls[rng.Next(PrizeWalls.Length)];
                        height = floor + 2.2f;
                        scale = 1.3f;
                        break;
                    default:
                        path = Posters[rng.Next(Posters.Length)];
                        height = floor + (kind == 2 ? 3.6f : 5.0f);
                        scale = 2.2f;
                        break;
                }

                Place(group, $"Poster_{i + 1:00}", path, angle, WallRadius, height, angle + 180f, scale);
            }

            // Красный занавес: четыре полотнища между афишами. Он же связывает
            // шатёр с табло — на референсе занавес стоит ровно за ним.
            for (int i = 0; i < 4; i++)
            {
                float angle = 90f * i + 45f;
                Place(group, $"Curtain_{i + 1}", Curtains[i % Curtains.Length],
                    angle, WallRadius - 0.1f, floor, angle + 180f, 1.35f);
            }

            // Гирлянды в два яруса: флажки ниже, лампочки выше. Один ярус
            // читается случайной верёвкой, два — украшенным шатром.
            for (int i = 0; i < BuntingCount; i++)
            {
                float angle = 360f / BuntingCount * i;
                Place(group, $"Bunting_{i + 1:00}", BuntingPath,
                    angle, WallRadius - 0.4f, floor + BuntingHeight, angle + 90f);
                Place(group, $"Bulbs_{i + 1:00}", BulbLinePath,
                    angle, WallRadius - 0.5f, floor + BuntingHeight + 2.6f, angle + 90f);
                Place(group, $"BuntingLow_{i + 1:00}", BuntingShortPath,
                    angle + 180f / BuntingCount, WallRadius - 0.3f, floor + 4.2f, angle + 90f);
            }

            for (int i = 0; i < 8; i++)
            {
                float angle = 45f * i + 22f;
                Place(group, $"Lamp_{i + 1}", LampPosts[i % LampPosts.Length],
                    angle, WallRadius - 1.6f, floor, angle + 180f);
            }

            for (int i = 0; i < 4; i++)
            {
                float angle = 90f * i;
                Place(group, $"Speaker_{i + 1}", SpeakerPath, angle, WallRadius - 0.8f, floor, angle + 180f);
            }

            // Мачты шатра: они объясняют, на чём вообще держится купол.
            //
            // Вплотную к стене, а не в глубине настила. На радиусе 14.2 мачта
            // высотой 9.5 м вставала посреди прохода и перечёркивала кадр
            // наискось: камера игрока отходит от клетки как раз на 11–12 м,
            // и столб оказывался ровно между ней и ареной. У стены он
            // читается опорой шатра и никому не мешает.
            for (int i = 0; i < MastCount; i++)
            {
                float angle = 360f / MastCount * i + 30f;
                Place(group, $"Mast_{i + 1}", MastPath, angle, config.TentRadius - 0.9f, floor, angle);
            }
        }

        /// <summary>
        /// Под куполом: гирлянды лучами от фермы к стене и висячие лампы.
        ///
        /// Лучи проходят на высоте фермы — 17.28 м, то есть на 5.7 м выше
        /// самой верхней клетки. Обзор между клетками они не задевают: тот
        /// идёт горизонтально на 4…11 м. Именно эти лучи и делают из шатра
        /// шатёр — без них купол пустой.
        /// </summary>
        private static void BuildOverhead(Transform root, CircusArenaConfig config, System.Random rng)
        {
            Transform group = ResetGroup(root, "Overhead");
            float y = config.RiggingHeight - 0.2f;
            float mid = (config.CageRingRadius + config.TentRadius) * 0.5f;

            for (int i = 0; i < RadialGarlands; i++)
            {
                float angle = 360f / RadialGarlands * i;
                // Луч идёт по радиусу, поэтому доворота на 90° здесь нет —
                // в отличие от гирлянд вдоль стены.
                GameObject go = Place(group, $"Radial_{i + 1:00}",
                    i % 2 == 0 ? BuntingPath : BulbLineShortPath, angle, mid, y, angle);
                if (go != null)
                {
                    go.transform.localScale = new Vector3(1.15f, 1f, 1f);
                }
            }

            for (int i = 0; i < 8; i++)
            {
                float angle = 45f * i + 22f;
                // Тоже к стене: на открытом настиле нить длиной 7.55 м висит
                // в пустоте и читается белыми пятнами в воздухе, а у стены —
                // спуском гирлянды.
                Place(group, $"HangingBulbs_{i + 1}", HangingBulbPath,
                    angle, config.TentRadius - 0.9f, config.RiggingHeight - 0.3f, angle, 1f, true);
            }
        }

        /// <summary>
        /// Мелочь между кольцами: ящики, тюки, бочки, бидоны, шары, призы.
        /// Она и убирает ощущение пустого пола — кольца дают структуру,
        /// а мелочь плотность.
        ///
        /// Ставится в полосе между барьером ямы и балаганным рядом, куда
        /// игрок в этой игре всё равно не попадает: он либо в клетке, либо
        /// в яме. Поэтому объёмное здесь разрешено — маршрутов тут нет.
        /// </summary>
        private static void BuildClutter(Transform root, CircusArenaConfig config, System.Random rng)
        {
            Transform group = ResetGroup(root, "Clutter");
            for (int i = 0; i < ClutterCount; i++)
            {
                float angle = (float)rng.NextDouble() * 360f;
                float radius = Mathf.Lerp(BarrierRadius + 0.9f, StandRadius - 1.4f, (float)rng.NextDouble());
                Place(group, $"Clutter_{i + 1:00}", Clutter[rng.Next(Clutter.Length)],
                    angle, radius, config.TentFloorHeight, (float)rng.NextDouble() * 360f);
            }
        }

        /// <summary>
        /// Мусор и износ. Плоское — единственное, что можно класть на маршрут:
        /// оно не задевает ни ног, ни камеры, и наполняет ровно те места, где
        /// игрок проводит весь раунд.
        ///
        /// В яме мусор лежит только от 45% радиуса и наружу: середина отдана
        /// забегу от медведя, а укрытий там быть не должно (спека 3.3).
        /// </summary>
        private static void BuildLitter(Transform root, CircusArenaConfig config, System.Random rng)
        {
            Transform group = ResetGroup(root, "Litter");

            for (int i = 0; i < LitterOnDeck; i++)
            {
                float angle = (float)rng.NextDouble() * 360f;
                float radius = Mathf.Lerp(config.PitRadius + 0.6f, config.TentRadius - 0.9f,
                    (float)rng.NextDouble());
                Place(group, $"Litter_{i + 1:00}", Litter[rng.Next(Litter.Length)],
                    angle, radius, config.TentFloorHeight, (float)rng.NextDouble() * 360f);
            }

            for (int i = 0; i < LitterInPit; i++)
            {
                float angle = (float)rng.NextDouble() * 360f;
                float radius = Mathf.Lerp(config.PitRadius * PitLitterInnerFactor, config.PitRadius - 0.5f,
                    (float)rng.NextDouble());
                Place(group, $"PitLitter_{i + 1:00}", Litter[rng.Next(Litter.Length)],
                    angle, radius, 0.05f, (float)rng.NextDouble() * 360f);
            }
        }

        /// <summary>
        /// Корпуса прожекторов на ферме. Пивот у модели сверху — вешаем за
        /// него, иначе прожектор уезжает под ферму на свою высоту.
        ///
        /// Ставятся <b>между</b> клетками, а не над ними: над клеткой проходит
        /// её цепь, и корпус спорил бы с ней в кадре.
        /// </summary>
        private static void BuildSpotlights(Transform root, CircusArenaConfig config)
        {
            Transform group = ResetGroup(root, "Spotlights");
            float radius = config.CageRingRadius + 1.1f;

            for (int i = 0; i < config.CageAnchorCount; i++)
            {
                float angle = config.GetAnchorAngle(i) + 180f / config.CageAnchorCount;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 at = dir * radius + Vector3.up * (config.RiggingHeight - 0.45f);

                GameObject body = CircusDress.Prop(group, $"Spotlight_{i + 1:00}", CircusDress.SpotlightPath,
                    at, angle + 180f, 0f, false, true);
                if (body != null)
                {
                    body.transform.rotation = Quaternion.LookRotation((Vector3.up * 2f - dir * radius).normalized);
                }
            }

            // Бегающий луч из 8.5 — отвлекалка. Корпус ставится здесь,
            // а водит его DistractionDirector.
            CircusDress.Prop(group, "SweepBody", SweepLightPath,
                Vector3.up * (config.RiggingHeight - 0.6f), 0f, 0f, false, true);
        }

        /// <summary>
        /// Свет шатра — кодом, а не инспектором.
        ///
        /// <b>Рассеянный поднят заметно выше стандартного.</b> Шатёр перекрыт
        /// куполом: направленный свет внутрь не попадает вовсе, и на штатной
        /// единице интерьер уходит в тёмно-серое, где восемь клеток перестают
        /// различаться между собой. Здесь рассеянный и есть основной свет,
        /// а софиты добавляют форму поверх него.
        ///
        /// Софит на каждую клетку, а не один общий: клетки висят на разной
        /// высоте, и общий верхний свет оставлял бы нижние в тени — то есть
        /// гасил бы ровно тот сигнал, ради которого игра и построена.
        /// </summary>
        private static void BuildLighting(Transform root, CircusArenaConfig config)
        {
            Transform group = ResetGroup(root, "Lights");

            // Числа подобраны замером кадра, а не на глаз: на 0.46/0.38/0.24
            // интерьер уходил в тёмно-серое и восемь клеток переставали
            // различаться между собой — то есть гас единственный интерфейс.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.68f, 0.60f, 0.52f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.50f, 0.43f);
            RenderSettings.ambientGroundColor = new Color(0.34f, 0.28f, 0.23f);
            RenderSettings.fog = false;

            for (int i = 0; i < config.CageAnchorCount; i++)
            {
                float angle = config.GetAnchorAngle(i);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 at = dir * (config.CageRingRadius + 1.1f) + Vector3.up * (config.RiggingHeight - 1.2f);

                var go = new GameObject($"CageSpot_{i + 1:00}");
                go.transform.SetParent(group, false);
                go.transform.position = at;
                // Целимся в нижнюю ступень, а не в верхнюю: конус накрывает
                // весь ход клетки сверху донизу, и опустившаяся не выпадает
                // из света.
                Vector3 aim = dir * config.CageRingRadius + Vector3.up * config.GetCageBottomHeight(0);
                go.transform.rotation = Quaternion.LookRotation((aim - at).normalized);

                var light = go.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = 46f;
                light.range = 30f;
                // URP отдаёт объекту ограниченное число дополнительных
                // источников за проход, поэтому софиты здесь — форма поверх
                // рассеянного, а не основной свет. Отсюда и запас яркости.
                light.intensity = 14f;
                light.color = new Color(1f, 0.89f, 0.72f);
                light.shadows = LightShadows.None;
            }

            // Яма. Широкий тёплый конус сверху: медведь и выпавший обязаны
            // читаться со всех восьми клеток разом.
            var pitGo = new GameObject("PitKey");
            pitGo.transform.SetParent(group, false);
            pitGo.transform.position = Vector3.up * (config.RiggingHeight - 2f);
            pitGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var pit = pitGo.AddComponent<Light>();
            pit.type = LightType.Spot;
            pit.spotAngle = 88f;
            pit.range = 34f;
            pit.intensity = 20f;
            pit.color = new Color(1f, 0.90f, 0.74f);
            pit.shadows = LightShadows.None;

            // Тёплая подсветка балаганного ряда: без неё внешнее кольцо
            // проваливается в тень и вся добавленная плотность пропадает зря.
            for (int i = 0; i < 6; i++)
            {
                float angle = 60f * i + 30f;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                var go = new GameObject($"MidwayGlow_{i + 1}");
                go.transform.SetParent(group, false);
                go.transform.position = dir * (StandRadius - 2f) + Vector3.up * (config.TentFloorHeight + 5.5f);
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 16f;
                light.intensity = 6f;
                light.color = new Color(1f, 0.84f, 0.62f);
                light.shadows = LightShadows.None;
            }

            // Направленный свет шаблона гасим до подсветки: под куполом он
            // физически ни при чём, а на полной яркости спорит с софитами.
            GameObject rig = GameObject.Find("_Lighting");
            if (rig == null)
            {
                return;
            }

            foreach (Light light in rig.GetComponentsInChildren<Light>(true))
            {
                if (light.type != LightType.Directional)
                {
                    continue;
                }

                light.intensity = 0.25f;
                light.color = new Color(0.86f, 0.82f, 0.78f);
                light.shadows = LightShadows.Soft;
            }
        }

        /// <summary>
        /// Поставить предмет на кольцо: угол и радиус вместо координат.
        /// Вся планировка здесь кольцевая, и через угол она читается, а через
        /// x/z — нет.
        /// </summary>
        private static GameObject Place(Transform group, string name, string path, float angle, float radius,
            float y, float yaw, float scale = 1f, bool hangFromTop = false)
        {
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            GameObject go = CircusDress.Prop(group, name, path, dir * radius + Vector3.up * y,
                yaw, 0f, false, hangFromTop);
            if (go != null && !Mathf.Approximately(scale, 1f))
            {
                go.transform.localScale *= scale;
            }

            return go;
        }

        private static Transform ResetGroup(Transform parent, string name)
        {
            Transform group = parent.Find(name);
            if (group == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = Vector3.zero;
                return go.transform;
            }

            for (int i = group.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(group.GetChild(i).gameObject);
            }

            group.localPosition = Vector3.zero;
            group.localRotation = Quaternion.identity;
            return group;
        }
    }
}
