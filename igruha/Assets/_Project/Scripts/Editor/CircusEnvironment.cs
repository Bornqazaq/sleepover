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
    /// Декор живёт на настиле шатра — кольце между бортом ямы и стеной,
    /// шириной около девяти метров. Там его видно и там он никому не мешает.
    /// </summary>
    internal static class CircusEnvironment
    {
        private const string Carnival = "Assets/Synty/PolygonHorrorCarnival/Prefabs/";
        private const string Props = Carnival + "Props/";

        private const string BleacherPath = Props + "SM_Prop_Bleachers_Straight_01.prefab";
        private const string BuntingPath = Props + "SM_Prop_Flags_05.prefab";
        private const string BulbLinePath = Props + "SM_Prop_Light_03.prefab";
        private const string SweepLightPath = "Assets/Synty/PolygonNightclubs/Prefabs/Props/SM_Prop_Light_Spotlight_03.prefab";

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
            Props + "SM_Prop_Sign_Games_01.prefab"
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
            Props + "SM_Prop_Papers_03.prefab",
            Props + "SM_Prop_Ticket_04.prefab"
        };

        /// <summary>Сколько трибун по кольцу. При 24 хорда 3.27 м почти совпадает с шириной модели 3.01.</summary>
        private const int BleacherCount = 24;

        private const int BuntingCount = 12;
        private const int PosterCount = 14;
        private const int LitterOnDeck = 26;
        private const int LitterInPit = 10;

        /// <summary>Мусор в яме — только по краю: центр отдан забегу и медведю.</summary>
        private const float PitLitterInnerFactor = 0.55f;

        /// <summary>Высота, на которой висит гирлянда флажков над настилом, м.</summary>
        private const float BuntingHeight = 7.5f;

        internal static void Build(Transform arena, CircusArenaConfig config, System.Random rng)
        {
            Transform root = ResetGroup(arena, "Environment");

            BuildBleachers(root, config);
            BuildWallDecor(root, config, rng);
            BuildLitter(root, config, rng);
            BuildSpotlights(root, config);
            BuildLighting(root, config);
        }

        /// <summary>
        /// Трибуны кольцом на настиле. Прямые секции, а не угловые: угловая
        /// вдвое дороже по треугольникам (1629 против 1062), а на 24 сегментах
        /// многоугольник и так читается кругом.
        ///
        /// Пустые — зрителей в этой игре нет. Ряды нужны не для толпы, а как
        /// вторая шкала высоты: рядом с трибуной в человеческий рост сразу
        /// понятно, насколько высоко висят клетки.
        /// </summary>
        private static void BuildBleachers(Transform root, CircusArenaConfig config)
        {
            Transform group = ResetGroup(root, "Bleachers");
            float radius = (config.PitRadius + 0.4f + config.TentRadius) * 0.5f;

            for (int i = 0; i < BleacherCount; i++)
            {
                float angle = 360f / BleacherCount * i;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                // Спинкой наружу: ряды поднимаются от ямы к стене, как в цирке.
                CircusDress.Prop(group, $"Bleacher_{i + 1:00}", BleacherPath,
                    dir * radius + Vector3.up * config.TentFloorHeight, angle + 180f, 0f, false);
            }
        }

        /// <summary>
        /// Стена шатра: афиши, вывески, гирлянды флажков и лампочек.
        ///
        /// Всё прижато к стене на радиусе шатра. Внутрь кольца клеток не
        /// заходит ничего — там запретная зона.
        /// </summary>
        private static void BuildWallDecor(Transform root, CircusArenaConfig config, System.Random rng)
        {
            Transform group = ResetGroup(root, "WallDecor");
            float wall = config.TentRadius - 0.35f;

            for (int i = 0; i < PosterCount; i++)
            {
                float angle = 360f / PosterCount * i + 7f;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                bool isSign = i % 4 == 3;
                string path = isSign
                    ? Signs[rng.Next(Signs.Length)]
                    : Posters[rng.Next(Posters.Length)];

                float height = config.TentFloorHeight + (isSign ? 5.2f : 3.4f);
                GameObject go = CircusDress.Prop(group, $"Poster_{i + 1:00}", path,
                    dir * wall + Vector3.up * height, angle + 180f, 0f, false);
                if (go != null)
                {
                    go.transform.localScale *= isSign ? 1.6f : 2.2f;
                }
            }

            for (int i = 0; i < BuntingCount; i++)
            {
                float angle = 360f / BuntingCount * i;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 at = dir * (wall - 0.4f) + Vector3.up * (config.TentFloorHeight + BuntingHeight);

                // Гирлянда идёт вдоль стены, а не поперёк: доворот на 90°
                // от радиуса. Поперёк она уходила бы в кольцо клеток.
                CircusDress.Prop(group, $"Bunting_{i + 1:00}", BuntingPath, at, angle + 90f, 0f, false);
                CircusDress.Prop(group, $"Bulbs_{i + 1:00}", BulbLinePath,
                    at + Vector3.up * 2.4f, angle + 90f, 0f, false);
            }
        }

        /// <summary>
        /// Мусор и износ. Плоское — единственное, что можно класть на маршрут:
        /// оно не задевает ни ног, ни камеры, и наполняет ровно те места, где
        /// игрок проводит весь раунд.
        ///
        /// В яме мусор лежит только по краю: центр отдан забегу от медведя,
        /// а укрытий там быть не должно (спека 3.3).
        /// </summary>
        private static void BuildLitter(Transform root, CircusArenaConfig config, System.Random rng)
        {
            Transform group = ResetGroup(root, "Litter");

            float inner = config.PitRadius + 0.8f;
            float outer = config.TentRadius - 1.2f;
            for (int i = 0; i < LitterOnDeck; i++)
            {
                float angle = (float)rng.NextDouble() * 360f;
                float radius = Mathf.Lerp(inner, outer, (float)rng.NextDouble());
                Vector3 at = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                CircusDress.Prop(group, $"Litter_{i + 1:00}", Litter[rng.Next(Litter.Length)],
                    at + Vector3.up * config.TentFloorHeight, (float)rng.NextDouble() * 360f, 0f, false);
            }

            for (int i = 0; i < LitterInPit; i++)
            {
                float angle = (float)rng.NextDouble() * 360f;
                float radius = Mathf.Lerp(config.PitRadius * PitLitterInnerFactor, config.PitRadius - 0.6f,
                    (float)rng.NextDouble());
                Vector3 at = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                CircusDress.Prop(group, $"PitLitter_{i + 1:00}", Litter[rng.Next(Litter.Length)],
                    at + Vector3.up * 0.05f, (float)rng.NextDouble() * 360f, 0f, false);
            }
        }

        /// <summary>
        /// Корпуса прожекторов на ферме. Пивот у модели сверху — вешаем за
        /// него, иначе прожектор уезжает под ферму на свою высоту.
        ///
        /// Ставятся <b>снаружи</b> кольца фермы и смотрят внутрь: над самими
        /// клетками им нельзя, там проходят цепи.
        /// </summary>
        private static void BuildSpotlights(Transform root, CircusArenaConfig config)
        {
            Transform group = ResetGroup(root, "Spotlights");
            float radius = config.CageRingRadius + 1.1f;

            for (int i = 0; i < config.CageAnchorCount; i++)
            {
                // Смещение на полсектора: прожектор висит между клетками,
                // а не над клеткой — так его корпус не спорит с цепью.
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

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            // Числа подобраны замером кадра, а не на глаз: на 0.46/0.38/0.24
            // интерьер уходил в тёмно-серое и восемь клеток переставали
            // различаться между собой — то есть гас единственный интерфейс.
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
