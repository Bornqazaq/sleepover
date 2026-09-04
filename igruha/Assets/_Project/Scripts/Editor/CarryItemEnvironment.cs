using System.Collections.Generic;
using UnityEngine;
using Igruha.Minigames.CarryItem;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Окружение «Переноски предмета» — подфаза 4.3. Стройка вокруг арены,
    /// стройка под ней и стройка над стенами.
    ///
    /// Зачем это отдельно от дресса. Дресс закрывает коробки блокаута: он
    /// отвечает на вопрос «из чего сделан этот предмет». Окружение отвечает на
    /// другой — «где я нахожусь», — и без него арена читается коробкой в пустоте
    /// при любом качестве дресса. Первый рендер 4.1 это и показал.
    ///
    /// Три яруса, и каждый решает свою задачу:
    ///
    /// <b>Нижний ярус в пропастях.</b> Самый ценный: пропасть — единственное,
    /// что убивает, и заглянув в неё игрок обязан увидеть, что там низ стройки,
    /// а не серая пустота. Место там ничем не занято, коллайдеры не нужны,
    /// и туда уходит вся крупная техника.
    ///
    /// <b>Пол арены.</b> Здесь спрос строже всего: игрок по нему бежит.
    /// Ставится только вдоль стен и в мёртвых полосах, мимо маршрутов, мимо
    /// горлышка, мимо обхода и мимо 4.5 м позади штабеля и бака.
    ///
    /// <b>Небо над стенами.</b> Борта арены 4.32 м, и всё, что ниже, за ними
    /// не видно вовсе — урок 3.51. Поэтому снаружи стоит только высокое: кран,
    /// водонапорная башня, труба. Кран при этом не декорация — его стрела идёт
    /// над горлышком и объясняет балку-ловушку.
    ///
    /// <b>Коллайдеров нет ни у чего.</b> В этой игре цена лишнего коллайдера
    /// выше обычного: он ловил бы броски бутыли, кирпичи и струю трубы.
    /// </summary>
    internal static class CarryItemEnvironment
    {
        private const string Construction = "Assets/Synty/PolygonConstruction/Prefabs/";
        private const string Props = Construction + "Props/";
        private const string Buildings = Construction + "Buildings/";
        private const string Vehicles = Construction + "Vehicles/";
        private const string Environments = Construction + "Environments/";

        /// <summary>Полоса вдоль маршрута команды, куда декор не ставится, ШИ от линии.</summary>
        private const float RouteClearance = 3.5f;

        /// <summary>Полуширина коридора горлышка, куда декор не ставится, ШИ.</summary>
        private const float NeckClearance = 5.5f;

        /// <summary>Где начинается обход по краю: туда декор тоже нельзя, это путь.</summary>
        private const float BypassInnerZ = 15f;

        /// <summary>Запретный радиус вокруг штабеля и бака, ШИ. Держит правило камеры 4.5 м.</summary>
        private const float PropRadius = 6f;

        /// <summary>Что ставим: модель, сколько её и в каком диапазоне доворота.</summary>
        private readonly struct Scatter
        {
            public readonly string Prefab;
            public readonly int Count;
            public readonly float Height;

            public Scatter(string prefab, int count, float height = 0f)
            {
                Prefab = prefab;
                Count = count;
                Height = height;
            }
        }

        /// <summary>Прямоугольник в ШИ, в котором можно ставить.</summary>
        private readonly struct Zone
        {
            public readonly float MinX;
            public readonly float MaxX;
            public readonly float MinZ;
            public readonly float MaxZ;

            public Zone(float minX, float maxX, float minZ, float maxZ)
            {
                MinX = minX;
                MaxX = maxX;
                MinZ = minZ;
                MaxZ = maxZ;
            }
        }

        /// <summary>
        /// Собрать окружение. Свой генератор случайных чисел приходит снаружи —
        /// тот же, что у дресса: раскладка обязана повторяться от пересборки к
        /// пересборке, иначе приёмку не с чем сравнивать.
        /// </summary>
        internal static void Build(Transform arena, CarryItemConfig config, System.Random rng)
        {
            Transform group = ResetGroup(arena, "Environment");

            BuildSkyline(group, config);
            BuildLowerTier(group, config, rng);
            BuildSiteProps(group, config, rng);
            BuildPerimeter(group, config, rng);
            BuildLight();
        }

        /// <summary>
        /// Над стенами. Борт арены 4.32 м, и всё ниже него из игры не видно —
        /// поэтому здесь только то, что выше стены в разы.
        /// </summary>
        private static void BuildSkyline(Transform group, CarryItemConfig config)
        {
            // Кран стоит так, чтобы стрела шла над горлышком: балка-ловушка
            // висит на тросе, и трос обязан откуда-то идти. Мачта при этом
            // вынесена за борт арены на её половину глубины — иначе она сама
            // встаёт посреди кадра и закрывает площадку.
            //
            // Ставится по пивоту, а не по центру габарита: габарит крана на
            // 45 м вытянут стрелой, и посадка по центру уводит мачту на два
            // десятка метров от заданной точки.
            Place(group, "Crane", Buildings + "SM_Bld_Crane_01.prefab", config,
                new Vector3(3f, 0f, 38f), 180f, byPivot: true);

            Place(group, "WaterTower", Buildings + "SM_Bld_WaterTower_01.prefab", config,
                new Vector3(-34f, 0f, -34f), 20f, byPivot: true);
            Place(group, "SmokeStack", Buildings + "SM_Bld_SmokeStack_01.prefab", config,
                new Vector3(48f, 0f, 30f), 0f, byPivot: true);

            // Второй кран вдалеке: одна стройка на горизонте читается макетом,
            // две — районом.
            Place(group, "CraneFar", Buildings + "SM_Bld_Crane_01.prefab", config,
                new Vector3(-58f, -8f, -46f), 250f, byPivot: true);
        }

        /// <summary>
        /// Нижний ярус: то, что видно в пропасть. Ставится на дно, крупными
        /// предметами — с восьми метров мелочь всё равно не читается.
        /// </summary>
        private static void BuildLowerTier(Transform group, CarryItemConfig config, System.Random rng)
        {
            float floor = -config.ChasmDepth;

            Place(group, "Hut_1", Buildings + "SM_Bld_Portable_Office_01.prefab", config,
                new Vector3(-16f, floor, 14f), 90f);
            Place(group, "Hut_2", Buildings + "SM_Bld_Portable_Office_02.prefab", config,
                new Vector3(-16f, floor, -14f), 270f);
            Place(group, "Excavator", Vehicles + "SM_Veh_Excavator_01.prefab", config,
                new Vector3(17f, floor, 13f), 200f);
            Place(group, "DumpTruck", Vehicles + "SM_Veh_Truck_01_DumpTray_01.prefab", config,
                new Vector3(18f, floor, -13f), 25f);
            Place(group, "Loader", Vehicles + "SM_Veh_Mini_Loader_01.prefab", config,
                new Vector3(-11f, floor, 0f), 140f);
            Place(group, "Generator", Props + "SM_Prop_Generator_Large_01.prefab", config,
                new Vector3(15f, floor, 0f), 60f);

            var below = new[]
            {
                new Zone(-18f, -8f, -18f, 18f),
                new Zone(14f, 22f, -18f, 18f)
            };

            ScatterInto(group, "Low", config, rng, below, floor, new[]
            {
                new Scatter(Environments + "SM_Env_Dirt_Pile_01.prefab", 4),
                new Scatter(Environments + "SM_Env_Dirt_Pile_03.prefab", 4),
                new Scatter(Props + "SM_Prop_Plank_Long_Stack_01.prefab", 3),
                new Scatter(Props + "SM_Prop_Skip_Large_01.prefab", 2),
                new Scatter(Props + "SM_Prop_Junk_Stack_03.prefab", 4),
                new Scatter(Props + "SM_Prop_BarrelStack_01.prefab", 3),
                new Scatter(Props + "SM_Prop_Scaffold_Preset_01.prefab", 3),
                new Scatter(Props + "SM_Prop_Rubble_Concrete_02.prefab", 6),
                new Scatter(Environments + "SM_Generic_Small_Rocks_01.prefab", 6)
            });
        }

        /// <summary>
        /// Реквизит на самом полу арены. Запретов больше, чем разрешений: по
        /// этому полу бегут с бутылью, и предмет не на своём месте тут не
        /// украшение, а помеха обзору.
        /// </summary>
        private static void BuildSiteProps(Transform group, CarryItemConfig config, System.Random rng)
        {
            var zones = new[]
            {
                // Стартовые зоны: середина между маршрутами команд и края.
                new Zone(-36f, -21f, -3f, 3f),
                new Zone(-36f, -21f, 12f, 18.5f),
                new Zone(-36f, -21f, -18.5f, -12f),

                // Зоны баков — то же самое зеркально.
                new Zone(24f, 36f, -3f, 3f),
                new Zone(24f, 36f, 12f, 18.5f),
                new Zone(24f, 36f, -18.5f, -12f),

                // Общая площадка: только карманы у завалов, дальше от прохода.
                new Zone(-6f, -1f, 11f, 14f),
                new Zone(-6f, -1f, -14f, -11f),
                new Zone(8f, 12f, 11f, 14f),
                new Zone(8f, 12f, -14f, -11f)
            };

            ScatterInto(group, "Site", config, rng, zones, 0f, new[]
            {
                new Scatter(Props + "SM_Prop_Cone_01.prefab", 14),
                new Scatter(Props + "SM_Prop_Cone_02.prefab", 6),
                new Scatter(Props + "SM_Prop_Paint_Bucket_Closed_01.prefab", 5),
                new Scatter(Props + "SM_Prop_Paint_Bucket_Open_01.prefab", 4),
                new Scatter(Props + "SM_Prop_Tool_Bucket_01.prefab", 3),
                new Scatter(Props + "SM_Prop_ConcreteBag_Stack_03.prefab", 4),
                new Scatter(Props + "SM_Prop_Plank_Stack_01.prefab", 4),
                new Scatter(Props + "SM_Prop_Pipe_Stack_01.prefab", 3),
                new Scatter(Props + "SM_Prop_Rebar_Stack_01.prefab", 3),
                new Scatter(Props + "SM_Prop_Barrel_01.prefab", 6),
                new Scatter(Props + "SM_Prop_Concrete_Mixer_01.prefab", 2),
                new Scatter(Props + "SM_Prop_Wood_Frame_01.prefab", 3),
                new Scatter(Props + "SM_Prop_Tarp_Generic_03.prefab", 2),
                new Scatter(Props + "SM_Prop_Toilet_Bucket_01.prefab", 2),
                new Scatter(Environments + "SM_Env_Dirt_Dust_01.prefab", 8),
                new Scatter(Props + "SM_Prop_Ladder_02.prefab", 3),
                new Scatter(Props + "SM_Prop_Light_Portable_01.prefab", 4)
            });
        }

        /// <summary>
        /// Вдоль стен: леса, ограждения и прожекторы. Стоят вплотную к борту,
        /// то есть там, где игрок не ходит, но что видно в каждом кадре.
        /// </summary>
        private static void BuildPerimeter(Transform group, CarryItemConfig config, System.Random rng)
        {
            float edge = 18.6f;
            var wall = new List<Vector3>();

            // Вдоль обоих бортов, кроме куска над пропастями — там пола нет.
            for (float x = -35f; x <= 36f; x += 5.5f)
            {
                if (InsideChasm(x, config))
                {
                    continue;
                }

                wall.Add(new Vector3(x, 0f, edge));
                wall.Add(new Vector3(x, 0f, -edge));
            }

            string[] kit =
            {
                Props + "SM_Prop_Scaffold_01.prefab",
                Props + "SM_Prop_Scaffold_02.prefab",
                Props + "SM_Prop_Scaffold_Stackable_01.prefab",
                Props + "SM_Prop_Fence_MetalSheet_01.prefab",
                Props + "SM_Prop_Fence_Wire_01.prefab",
                Buildings + "SM_Bld_ConcreteRebar_Pillar_Short_01.prefab",
                Buildings + "SM_Bld_ConcreteRebar_Wall_02.prefab"
            };

            for (int i = 0; i < wall.Count; i++)
            {
                Vector3 spot = wall[i];
                float yaw = spot.z > 0f ? 0f : 180f;
                Place(group, $"Wall_{i + 1}", kit[rng.Next(kit.Length)], config, spot, yaw);
            }

            // Прожекторы по углам: они и объясняют, почему на площадке светло
            // в тени бортов.
            Place(group, "Flood_1", Props + "SM_Prop_Floodlights_01.prefab", config,
                new Vector3(-33f, 0f, 17.5f), 150f);
            Place(group, "Flood_2", Props + "SM_Prop_Floodlights_01.prefab", config,
                new Vector3(33f, 0f, -17.5f), -30f);

            // Знак и светофор стройки — на подходе к горлышку, но вне обоих
            // маршрутов и вне самого прохода: между линией команды и обходом
            // свободных полос нет вовсе, поэтому они стоят дальше по краю.
            Place(group, "NeckSign", Props + "SM_Prop_Sign_Road_01.prefab", config,
                new Vector3(-4f, 0f, 12.5f), 200f);
            Place(group, "NeckLight", Props + "SM_Prop_TrafficLight_Directional_01.prefab", config,
                new Vector3(-4f, 0f, -12.5f), 20f);
        }

        /// <summary>
        /// Свет — кодом, а не инспектором: YAML сцены не переживает слияние
        /// веток, и чужая правка выигрывает молча.
        ///
        /// Яркий дневной с чистыми тенями, как просит LDD. Рассеянный поднят
        /// выше стандартного: борта арены 11.5 м высотой, и в пропастях без
        /// этого чёрная дыра вместо нижнего яруса.
        /// </summary>
        private static void BuildLight()
        {
            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Light sun = null;
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional)
                {
                    sun = lights[i];
                    break;
                }
            }

            if (sun == null)
            {
                var go = new GameObject("Sun");
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
            }

            sun.color = new Color(1f, 0.96f, 0.87f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.72f;
            sun.transform.rotation = Quaternion.Euler(52f, -34f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.71f, 0.85f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.56f, 0.56f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.31f, 0.29f);
            RenderSettings.fog = false;
        }

        // ========== РАССТАНОВКА ==========

        /// <summary>
        /// Разбросать набор моделей по разрешённым прямоугольникам. Точка
        /// отвергается, если попала в маршрут, в горлышко, в обход или в круг
        /// вокруг штабеля и бака.
        /// </summary>
        private static void ScatterInto(Transform group, string prefix, CarryItemConfig config,
            System.Random rng, Zone[] zones, float floorY, Scatter[] kit)
        {
            var taken = new List<Vector3>(64);

            for (int k = 0; k < kit.Length; k++)
            {
                Scatter item = kit[k];
                for (int n = 0; n < item.Count; n++)
                {
                    if (!TryFindSpot(zones, rng, taken, floorY, config, out Vector3 spot))
                    {
                        continue;
                    }

                    taken.Add(spot);
                    Place(group, $"{prefix}_{k + 1}_{n + 1}", item.Prefab, config, spot,
                        rng.Next(4) * 90f + rng.Next(-25, 25), item.Height);
                }
            }
        }

        private static bool TryFindSpot(Zone[] zones, System.Random rng, List<Vector3> taken,
            float floorY, CarryItemConfig config, out Vector3 spot)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                Zone zone = zones[rng.Next(zones.Length)];
                float x = Mathf.Lerp(zone.MinX, zone.MaxX, (float)rng.NextDouble());
                float z = Mathf.Lerp(zone.MinZ, zone.MaxZ, (float)rng.NextDouble());

                if (floorY >= 0f && !IsFree(x, z, config))
                {
                    continue;
                }

                spot = new Vector3(x, floorY, z);
                if (TooClose(taken, spot))
                {
                    continue;
                }

                return true;
            }

            spot = Vector3.zero;
            return false;
        }

        private static bool TooClose(List<Vector3> taken, Vector3 spot)
        {
            const float minGap = 2.6f;
            for (int i = 0; i < taken.Count; i++)
            {
                Vector2 a = new Vector2(taken[i].x, taken[i].z);
                Vector2 b = new Vector2(spot.x, spot.z);
                if ((a - b).sqrMagnitude < minGap * minGap)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Свободна ли точка пола арены. Запреты ровно те же, что перечислены в
        /// брифе: маршруты команд, горлышко, обход по краю и круги вокруг
        /// штабеля и бака — там держится правило камеры 4.5 м.
        /// </summary>
        private static bool IsFree(float x, float z, CarryItemConfig config)
        {
            if (Mathf.Abs(Mathf.Abs(z) - 7f) < RouteClearance)
            {
                return false;
            }

            if (Mathf.Abs(z) < NeckClearance && x > -10f && x < 16f)
            {
                return false;
            }

            if (Mathf.Abs(z) > BypassInnerZ && x > -8f && x < 14f)
            {
                return false;
            }

            if (InsideChasm(x, config))
            {
                return false;
            }

            for (int i = 0; i < 2; i++)
            {
                float sign = i == 0 ? 1f : -1f;
                if (new Vector2(x + 26f, z - 7f * sign).sqrMagnitude < PropRadius * PropRadius ||
                    new Vector2(x - 28f, z - 7f * sign).sqrMagnitude < PropRadius * PropRadius)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Пропасти: пола там нет, и ставить на него нечего.</summary>
        private static bool InsideChasm(float x, CarryItemConfig config)
        {
            return (x > -19.5f && x < -6.5f) || (x > 12.5f && x < 23.5f);
        }

        /// <summary>
        /// Поставить модель пака в точку арены. Коллайдеры срезаются всегда:
        /// окружение не имеет права ловить броски, кирпичи и струю.
        /// </summary>
        private static GameObject Place(Transform group, string name, string path, CarryItemConfig config,
            Vector3 spotUnits, float yaw, float targetHeight = 0f, bool byPivot = false)
        {
            return CarryItemDress.Prop(group, name, path,
                new Vector3(config.ToMeters(spotUnits.x), config.ToMeters(spotUnits.y), config.ToMeters(spotUnits.z)),
                yaw, targetHeight, false, byPivot);
        }

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
    }
}
