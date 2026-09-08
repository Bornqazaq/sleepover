using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Igruha.Minigames.Exam;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Дресс «Экзамена» — подфаза 4.1. Каталог «коробка блокаута → чем одета»
    /// и вся посадка моделей на арену.
    ///
    /// <b>Дресс — слой поверх блокаута, а не замена.</b> Коробка остаётся на
    /// месте со своим коллайдером и слоем, гасится только её рендерер, внутрь
    /// садится модель по замеренным габаритам. Выверенная фазами 2–3 геометрия
    /// физически не может сдвинуться от арта: 23 коллайдера на <c>Ground</c>
    /// и 38 на <c>Default</c> обязаны остаться теми же (бриф, 14.9).
    ///
    /// <b>Почему не через <see cref="DressKit"/> целиком.</b> Кит сажает модель
    /// в коробку по её габариту и сам считает сетку копий — этого хватает
    /// «Переноске» и «Дырке». Здесь половина работы другая: петли живут не
    /// в коробке створки, а на её оси вращения; рамка люка лежит снаружи
    /// платформы, на полу зала; настил обязан идти шагом 1.44 м, а не шагом
    /// модели. Всё это — мировые координаты, поэтому дресс кладёт модели через
    /// <see cref="Holder"/> — пустышку с единичным мировым масштабом внутри
    /// растянутой коробки. Из кита берётся то, ради чего он и заводился:
    /// замер габаритов и общий список ненайденного.
    ///
    /// <b>Модуль 1.44 м (2 ШП) делит арену нацело:</b> зал 28.80 = 20 модулей
    /// и 25.92 = 18, платформа 8.64 = 6 и 7.20 = 5, створка 4.32 = 3, зазор
    /// 1.44 = 1. Настил этим шагом не режет шов посреди плиты, а кромка
    /// платформы всегда приходится на настоящий стык.
    ///
    /// <b>Посадка моделей отсюда переиспользуется.</b> <see cref="Prop"/>,
    /// <see cref="Spawn"/>, <see cref="SizeTo"/> и <see cref="Scenery"/>
    /// вызывает и палитра 4.2, и окружение 4.3: «поставить модель основанием
    /// в точку, привести к габариту, срезать коллайдеры» — задача одна на все
    /// три подфазы, и решать её трижды значило бы завести три набора одних
    /// и тех же ошибок.
    ///
    /// <b>Чего здесь нет намеренно.</b> Пол, стены и потолок зала — большие
    /// плоскости, они уходят в палитру 4.2. Доска не одевается моделью вовсе:
    /// перед ней висит холст с текстом вопроса, и модель на её месте закрыла бы
    /// собой то единственное, ради чего доска существует, — одевается только
    /// её обвязка. Классная доска пака 2.0 м здесь тоже не годится: семь штук
    /// в ряд на 12.96 м дают семь рам подряд, то есть забор (решение
    /// геймдизайнера 04.09, спека 14.12).
    /// </summary>
    internal static class ExamDress
    {
        private const string Empire = "Assets/Synty/PolygonAncientEmpire/Prefabs/";
        private const string Kids = "Assets/Synty/PolygonKids/Prefabs/";
        private const string Generic = "Assets/Synty/PolygonGeneric/Prefabs/";
        private const string Clubs = "Assets/Synty/PolygonNightclubs/Prefabs/";

        /// <summary>Дощатая плита настила: ею мостятся створки. 2.50 × 0.10 × 2.50.</summary>
        private const string DeckSlab = Generic + "Base/SM_Bld_Base_Floor_Combined_01.prefab";

        /// <summary>Учительский стол: 2.18 × 0.95 × 0.93, с тумбами и ящиками.</summary>
        private const string TeacherDeskPath = Clubs + "Props/SM_Prop_Desk_01.prefab";

        /// <summary>ЭЛТ-телевизор 0.67 × 0.58 × 0.51 — единственный кубический монитор в паках.</summary>
        private const string CrtPath = Clubs + "Props/SM_Prop_TV_02.prefab";

        /// <summary>Карниз 2.50 × 0.50 × 0.14: обвязка доски сверху и полка для мела снизу.</summary>
        private const string TrimPath = Empire + "Buildings/SM_Bld_Trim_05.prefab";

        /// <summary>Пилястра 0.43 × 3.01 × 0.43 — вертикали по краям доски.</summary>
        private const string PilasterPath = Empire + "Buildings/Base/SM_Bld_Base_Pillar_02.prefab";

        private const string ChalkPath = Kids + "Props/School/SM_Prop_Chalk_01.prefab";
        private const string DusterPath = Kids + "Props/School/SM_Prop_Blackboard_Duster_01.prefab";
        private const string ExamDeskPath = Kids + "Props/School/SM_Prop_Chair_Desk_01.prefab";
        private const string LockerPath = Kids + "Props/School/SM_Prop_Locker_02.prefab";
        private const string SchoolBagPath = Kids + "Attachments/Bags/SM_Chr_Attach_Bag_School_01.prefab";

        /// <summary>Шаг настила, 2 ШП. Делит нацело и створку, и платформу, и зал.</summary>
        private const float Module = 1.44f;

        /// <summary>Ширина тёмного шва по внутренней кромке створки, на каждую половину.</summary>
        private const float SeamWidth = 0.05f;

        /// <summary>Полос рамки люка на платформу: внешняя, передняя и задняя. Четвёртая — сам зазор.</summary>
        private const float BandWidth = 0.20f;

        /// <summary>Петель на створку. Три — минимум, при котором ось читается линией, а не точкой.</summary>
        private const int HingesPerDoor = 3;

        /// <summary>
        /// Габаритная высота парты, к которой приводится модель.
        ///
        /// Мебель PolygonKids детская: моноблок «стол со стулом» приезжает
        /// 0.51 × 0.62 × 0.65, и высота 0.62 — это спинка стула, а не
        /// столешница. Прежние 0.84 давали спинку по бедро персонажу ростом
        /// 1.8 м, отчего класс и читался детским садом даже после подгонки.
        /// 0.95 ставит спинку под лопатку сидящему взрослому, а столешницу
        /// на 0.72 — ровно на высоту коробки блокаута.
        /// </summary>
        private const float ExamDeskHeight = 0.95f;

        /// <summary>Стопка учебников на парте: 0.44 × 0.31 × 0.26.</summary>
        private const string BooksPath = "Assets/Synty/PolygonTown/Prefabs/Props/SM_Prop_Book_Group_07.prefab";

        /// <summary>Стопка потоньше, для разнообразия: 0.37 × 0.23 × 0.35.</summary>
        private const string BooksAltPath = "Assets/Synty/PolygonTown/Prefabs/Props/SM_Prop_Book_Group_05.prefab";

        private static readonly List<string> Notes = new List<string>(8);
        private static readonly List<string> Placed = new List<string>(24);

        /// <summary>Сбросить отчёт и общий список ненайденного перед пересборкой.</summary>
        internal static void Begin()
        {
            DressKit.Begin();
            Notes.Clear();
            Placed.Clear();
        }

        /// <summary>Замечания пересборки: ноль — часть приёмки подфазы.</summary>
        internal static IReadOnlyList<string> Notices
        {
            get { return Notes; }
        }

        /// <summary>
        /// Одеть построенную арену. Вызывается из <see cref="ExamArenaBuilder"/>
        /// в конце сборки: всё, что не воспроизводится пересборкой, теряется при
        /// первом же слиянии веток — YAML сцены слияние не переживает.
        /// </summary>
        internal static void Build(GameObject arena, ExamConfig config, System.Random rng)
        {
            if (arena == null || config == null)
            {
                return;
            }

            DressPlatform(arena, ExamSide.A, config, rng);
            DressPlatform(arena, ExamSide.B, config, rng);
            DressPodium(arena, config, rng);
            DressHostDesk(arena);
            DressBoard(arena, config);
            DressSign(arena, ExamSide.A);
            DressSign(arena, ExamSide.B);
            DressDeskRows(arena);
            DressCabinet(arena);
            DressCoatRack(arena);
        }

        // ────────────────────────────────────────────────────────────────
        // Платформа: настил створок, шов, петли, рамка люка
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Одеть платформу варианта. Три вещи, и каждая отвечает за свой пункт
        /// брифа 14.4: настил делает створку полом, шов и петли — люком,
        /// цветная рамка — вариантом ответа.
        /// </summary>
        private static void DressPlatform(GameObject arena, ExamSide side, ExamConfig config, System.Random rng)
        {
            string platformName = side == ExamSide.A ? "Platform_A" : "Platform_B";
            Transform platform = Find(arena, platformName);
            if (platform == null)
            {
                return;
            }

            DressDoor(platform, "DoorLeft", rng);
            DressDoor(platform, "DoorRight", rng);
            BuildHatchBand(platform, side, config);
        }

        /// <summary>
        /// Половина пола: настил, тёмный шов по внутренней кромке и петли на оси.
        ///
        /// Петли и внутренние накладки вешаются на <b>пивот</b>, а не на створку:
        /// пивот и есть ось вращения, поэтому накладка поворачивается вместе
        /// с полом, а ствол петли крутится на месте — как у настоящего люка.
        /// Вложи их в створку — и они уехали бы от оси на два метра.
        /// </summary>
        private static void DressDoor(Transform platform, string doorName, System.Random rng)
        {
            Transform pivot = platform.Find(doorName);
            if (pivot == null)
            {
                Note($"на {platform.name} нет створки {doorName}");
                return;
            }

            Transform leaf = pivot.Find("Leaf");
            var leafRenderer = leaf != null ? leaf.GetComponent<Renderer>() : null;
            if (leafRenderer == null)
            {
                Note($"у {doorName} нет полотна Leaf с рендерером");
                return;
            }

            Bounds box = leafRenderer.bounds;

            // Створка тянется от петли внутрь платформы; знак говорит, в какую
            // сторону смотрит её внутренняя кромка и куда ложатся накладки.
            float inward = Mathf.Sign(leaf.localPosition.x);

            Deck(leaf.gameObject, box, rng);
            Seam(leaf.gameObject, box, inward);
            Hinges(pivot, box, inward);
        }

        /// <summary>
        /// Настелить створку дощатыми плитами шагом 1.44 м.
        ///
        /// Плита пака — 2.50 м, и на створке 4.32 × 7.20 она даёт сетку 2 × 3
        /// с плитами по 2.16 × 2.40: шов приходится куда попало и с кромкой
        /// платформы не совпадает. Шаг 1.44 делит створку нацело — 3 × 5, — и
        /// каждый шов лежит на модуле арены.
        ///
        /// Доски идут <b>поперёк шва</b>, от петли к середине платформы: так
        /// настил показывает, куда откинется половина, ещё до первого раскрытия.
        /// </summary>
        private static void Deck(GameObject leaf, Bounds box, System.Random rng)
        {
            Transform holder = Holder(leaf, "Dress");
            if (holder == null)
            {
                return;
            }

            // Рендерер коробки гаснет только после того, как настил встал:
            // не нашлась модель — створка обязана остаться серой и видимой,
            // а не превратиться в дыру, сквозь которую видно яму.
            if (!DeckSurface(holder, box, rng, 0f, DeckSlab, out int countX, out int countZ, out float stepX, out float stepZ))
            {
                return;
            }

            HideRenderer(leaf);
            Record("створка", $"настил {countX}×{countZ} плит по {stepX:F2}×{stepZ:F2} м", DeckSlab);
        }

        /// <summary>
        /// Настелить верхнюю грань коробки плитами шагом <see cref="Module"/>.
        ///
        /// <paramref name="lift"/> поднимает настил над гранью. Створке он не
        /// нужен — её рендерер гаснет, — а кафедре нужен: у неё борта остаются
        /// коробкой и красятся палитрой, и настил вровень с её верхом дал бы
        /// полосу z-fighting'а по всей площадке.
        /// </summary>
        private static bool DeckSurface(Transform holder, Bounds box, System.Random rng, float lift,
            string prefabPath, out int countX, out int countZ, out float stepX, out float stepZ)
        {
            countX = Mathf.Max(1, Mathf.RoundToInt(box.size.x / Module));
            countZ = Mathf.Max(1, Mathf.RoundToInt(box.size.z / Module));
            stepX = box.size.x / countX;
            stepZ = box.size.z / countZ;

            for (int x = 0; x < countX; x++)
            {
                for (int z = 0; z < countZ; z++)
                {
                    // Четверть оборота ставит доски поперёк шва, от петли
                    // к середине платформы: доска кончается ровно там, где
                    // расходятся половины, и настил сам показывает линию
                    // раскрытия. Вдоль шва доски шли бы через него насквозь
                    // и прятали бы его.
                    //
                    // Пол-оборота сверху через раз: направление не меняется,
                    // а рисунок перестаёт быть штампом из одной плиты. Свой
                    // генератор — общий с билдером сдвинул бы последовательность,
                    // по которой раскладывается геймплей.
                    int yaw = 1 + (rng.Next(2) == 0 ? 0 : 2);
                    GameObject tile = Spawn(holder, $"Deck_{x}_{z}", prefabPath, yaw);
                    if (tile == null)
                    {
                        return false;
                    }

                    // Толщина остаётся своей: плита обязана остаться плитой,
                    // а коробка добирается числом копий, а не растяжением.
                    Vector3 natural = WorldSize(tile);
                    SizeTo(tile, new Vector3(stepX, natural.y, stepZ));
                    CenterAt(tile, new Vector3(
                        box.min.x + stepX * (x + 0.5f),
                        box.max.y + lift - natural.y * 0.5f,
                        box.min.z + stepZ * (z + 0.5f)));
                }
            }

            return true;
        }

        /// <summary>
        /// Тёмная полоса по внутренней кромке створки. Две такие полосы, по одной
        /// с каждой половины, дают линию раздела шириной 0.10 м ровно по центру
        /// платформы — единственное, что отличает люк от пола, пока он закрыт.
        ///
        /// Полоса живёт на створке и уезжает вместе с ней: шов обязан исчезать
        /// в тот момент, когда пол раскрывается, иначе он повиснет над ямой.
        /// </summary>
        private static void Seam(GameObject leaf, Bounds box, float inward)
        {
            Transform holder = Holder(leaf, "Seam");
            if (holder == null)
            {
                return;
            }

            Material tone = ExamPalette.Get(ExamPalette.Tone.Seam);
            Bar(holder, "SeamStrip", tone,
                new Vector3(box.center.x + inward * (box.size.x * 0.5f - SeamWidth * 0.5f),
                    box.max.y - 0.03f,
                    box.center.z),
                new Vector3(SeamWidth, 0.07f, box.size.z));
        }

        /// <summary>
        /// Петли по внешнему краю створки: ствол на оси и накладка на полотне.
        ///
        /// Ни в одном из двенадцати паков петли нет — проверено по всем 8198
        /// префабам индекса, — поэтому она собирается из примитивов. Это тот
        /// случай, о котором говорит правило: «в паке такого нет» — не ответ,
        /// а начало работы.
        /// </summary>
        private static void Hinges(Transform pivot, Bounds box, float inward)
        {
            Transform holder = new GameObject("Hinges").transform;
            holder.SetParent(pivot, false);
            holder.localPosition = Vector3.zero;
            holder.localRotation = Quaternion.identity;
            holder.localScale = Vector3.one;

            Material metal = ExamPalette.Get(ExamPalette.Tone.Metal);

            for (int i = 0; i < HingesPerDoor; i++)
            {
                float t = (i + 0.5f) / HingesPerDoor;
                float z = box.min.z + box.size.z * t;

                // Ствол лежит вдоль оси вращения, то есть вдоль Z. Половина его
                // диаметра торчит над настилом — ровно так и выглядит петля
                // напольного люка; коллайдера у неё нет, зацепиться не за что.
                var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                barrel.name = $"HingeBarrel_{i + 1}";
                Scenery(barrel, metal);
                barrel.transform.SetParent(holder, false);
                barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                barrel.transform.localScale = new Vector3(0.10f, 0.22f, 0.10f);
                barrel.transform.position = new Vector3(pivot.position.x, box.max.y, z);

                Bar(holder, $"HingeStrap_{i + 1}", metal,
                    new Vector3(pivot.position.x + inward * 0.19f, box.max.y + 0.015f, z),
                    new Vector3(0.34f, 0.03f, 0.11f));
            }
        }

        /// <summary>
        /// Рамка люка на полу зала вокруг платформы: она и говорит, что площадка
        /// вставлена в пол, а не является им.
        ///
        /// Полос три, а не четыре. Четвёртая сторона — сам зазор между А и Б:
        /// под ним нет пола, и в него видно яму, поэтому он и так читается
        /// тёмной щелью. Полоса поверх зазора висела бы над ямой.
        ///
        /// Цвет — цвет варианта: это третий канал читаемости А/Б из брифа 14.4,
        /// единственный, который не гасит толпа из семерых. Свечение ему
        /// добавит палитра 4.2.
        /// </summary>
        private static void BuildHatchBand(Transform platform, ExamSide side, ExamConfig config)
        {
            Transform holder = new GameObject("HatchBand").transform;
            holder.SetParent(platform, false);
            holder.localPosition = Vector3.zero;
            holder.localRotation = Quaternion.identity;
            holder.localScale = Vector3.one;

            Material tone = SideTone(side);
            float halfW = config.PlatformWidth * 0.5f;
            float halfD = config.PlatformDepth * 0.5f;

            // Наружу — это в сторону боковой стены: у А влево, у Б вправо.
            float outward = side == ExamSide.A ? -1f : 1f;
            const float top = 0.02f;
            const float thickness = 0.05f;
            Vector3 origin = platform.position;

            Bar(holder, "Band_Outer", tone,
                new Vector3(origin.x + outward * (halfW + BandWidth * 0.5f), origin.y + top - thickness * 0.5f, origin.z),
                new Vector3(BandWidth, thickness, config.PlatformDepth + BandWidth * 2f));

            Bar(holder, "Band_Near", tone,
                new Vector3(origin.x, origin.y + top - thickness * 0.5f, origin.z - halfD - BandWidth * 0.5f),
                new Vector3(config.PlatformWidth, thickness, BandWidth));

            Bar(holder, "Band_Far", tone,
                new Vector3(origin.x, origin.y + top - thickness * 0.5f, origin.z + halfD + BandWidth * 0.5f),
                new Vector3(config.PlatformWidth, thickness, BandWidth));

            Record(side == ExamSide.A ? "рамка люка А" : "рамка люка Б",
                $"три полосы {BandWidth:F2} м по контуру платформы", "примитивы");
        }

        // ────────────────────────────────────────────────────────────────
        // Кафедра и стол Ведущего
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Возвышение Ведущего: настил поверх и карниз по трём видимым сторонам.
        ///
        /// Сначала здесь стоял сценический подиум казино — 7.87 × 0.50 × 3.93,
        /// почти габарит коробки. Рендер приёмки его отменил: модель оказалась
        /// почти чёрной и с округлым передом, и кафедра читалась тёмным горбом
        /// на светлом полу, а Ведущий на её фоне — силуэтом в силуэте.
        ///
        /// Поэтому <b>борта остаются коробкой блокаута</b> с включённым
        /// рендерером: это простая плоскогранная тумба, и её место — в палитре
        /// 4.2, вместе с полом и стенами. Дресс кладёт то, что палитрой не
        /// делается: доски на верх и профиль по кромке. Настил приподнят на
        /// 5 мм — вровень он дал бы полосу z-fighting'а во всю площадку.
        /// </summary>
        private static void DressPodium(GameObject arena, ExamConfig config, System.Random rng)
        {
            Transform podium = Find(arena, "Podium");
            var renderer = podium != null ? podium.GetComponent<Renderer>() : null;
            if (renderer == null)
            {
                Note("на арене нет кафедры Podium");
                return;
            }

            Bounds box = renderer.bounds;
            Transform holder = Holder(podium.gameObject, "Dress");
            if (holder == null)
            {
                return;
            }

            const float lift = 0.005f;
            if (!DeckSurface(holder, box, rng, lift, DeckSlab, out int countX, out int countZ, out float stepX, out float stepZ))
            {
                return;
            }

            // Карниз только по трём сторонам: задняя прижата к дальней стене
            // и не видна ни из одного кадра игры.
            const float height = 0.50f;
            const float depth = 0.14f;
            float railY = box.max.y - height * 0.5f;

            Rail(holder, "Plinth_Front", TrimPath,
                new Vector3(box.center.x, railY, box.min.z - depth * 0.5f),
                box.size.x + depth * 2f, height, depth, 0);
            Rail(holder, "Plinth_Left", TrimPath,
                new Vector3(box.min.x - depth * 0.5f, railY, box.center.z),
                box.size.z, height, depth, 1);
            Rail(holder, "Plinth_Right", TrimPath,
                new Vector3(box.max.x + depth * 0.5f, railY, box.center.z),
                box.size.z, height, depth, 1);

            Record("кафедра", $"настил {countX}×{countZ} по {stepX:F2}×{stepZ:F2} м, карниз по трём сторонам", DeckSlab);
        }

        /// <summary>
        /// Стол Ведущего и ретро-монитор на нём (спека 4.6).
        ///
        /// Трибуны на кафедре не будет, и это решение, а не пропуск. Блокаут
        /// ставит учительскую мебель одним пятном по центру переднего края
        /// возвышения — место ровно на один предмет. Стол с ЭЛТ закрывает
        /// требование спеки дословно, а трибуна его бы вытеснила. «Кто сейчас
        /// Ведущий» держат возвышение, отдельное пятно света (4.3) и то, что он
        /// единственный выше уровня пола.
        /// </summary>
        private static void DressHostDesk(GameObject arena)
        {
            Fill(arena, "HostDesk/Stand", TeacherDeskPath, "стол Ведущего", 2);
            Fill(arena, "HostDesk/Monitor", CrtPath, "ретро-монитор", 0);
        }

        // ────────────────────────────────────────────────────────────────
        // Доска
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Обвязка доски: карниз сверху, полка с мелом снизу, пилястры по краям.
        ///
        /// Само полотно доски остаётся коробкой блокаута с включённым рендерером
        /// — это грифель, и красит его палитра 4.2. Модель на его месте закрыла
        /// бы холст с текстом вопроса, который висит в двух сантиметрах перед
        /// доской.
        ///
        /// Четыре бруска рамки блокаута гаснут, но их коллайдеры остаются:
        /// это декор на <c>Default</c> на высоте от двух метров, трогать его
        /// незачем, а число коллайдеров — контрольное (бриф 14.9).
        /// </summary>
        private static void DressBoard(GameObject arena, ExamConfig config)
        {
            Transform board = Find(arena, "Board");
            var renderer = board != null ? board.GetComponent<Renderer>() : null;
            if (renderer == null)
            {
                Note("на арене нет доски Board");
                return;
            }

            Bounds box = renderer.bounds;
            foreach (string bar in new[] { "BoardFrame_Top", "BoardFrame_Bottom", "BoardFrame_Left", "BoardFrame_Right" })
            {
                Transform old = Find(arena, "Decor/" + bar);
                if (old != null)
                {
                    HideRenderer(old.gameObject);
                }
            }

            Transform holder = new GameObject("BoardTrim").transform;
            holder.SetParent(board.parent, false);
            holder.localPosition = Vector3.zero;
            holder.localRotation = Quaternion.identity;
            holder.localScale = Vector3.one;

            // Пилястры стоят на полу, а не висят по краям доски: обвязка, у
            // которой не видно опоры, читается забытым в воздухе куском.
            float pilasterX = box.size.x * 0.5f + 0.30f;
            float faceZ = box.min.z - 0.02f;
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -pilasterX : pilasterX;
                GameObject pilaster = Spawn(holder, i == 0 ? "Pilaster_L" : "Pilaster_R", PilasterPath, 0);
                if (pilaster == null)
                {
                    break;
                }

                Vector3 natural = WorldSize(pilaster);
                SizeTo(pilaster, new Vector3(natural.x, box.max.y, natural.z));
                CenterAt(pilaster, new Vector3(x, box.max.y * 0.5f, faceZ - natural.z * 0.5f));
            }

            float railSpan = pilasterX * 2f + 0.44f;
            Rail(holder, "Rail_Top", TrimPath, new Vector3(0f, box.max.y + 0.25f, faceZ), railSpan, 0.50f, 0.14f, 0);

            // Нижняя тяга — не только карниз, но и полка: на ней лежат мел
            // и тряпка. Родная глубина профиля 0.14 м для этого мала — тряпка
            // 0.20 м свисала бы с неё, — поэтому полка вынесена на 0.26.
            const float ledgeDepth = 0.26f;
            Rail(holder, "Rail_Ledge", TrimPath, new Vector3(0f, box.min.y - 0.25f, faceZ), railSpan, 0.50f, ledgeDepth, 0);

            // Мел и тряпка на полке: без них полка читается просто ещё одним
            // карнизом, а с ними доска становится доской.
            //
            // Раскладываются парами. Сначала тряпка лежала слева, а мел справа,
            // и замер зеркальности поймал ровно это: у половины зала оказалась
            // примета, которой нет у второй. Восемьдесят треугольников тряпки
            // подсказкой, конечно, не станут, но правило с исключениями
            // перестаёт проверяться числом, а числом оно здесь и держится.
            for (int side = 0; side < 2; side++)
            {
                float mirror = side == 0 ? -1f : 1f;
                Prop(holder, $"Duster_{side}", DusterPath, new Vector3(mirror * 1.10f, box.min.y, faceZ), 0f);
                Prop(holder, $"Chalk_{side}_1", ChalkPath, new Vector3(mirror * 0.95f, box.min.y, faceZ), 90f);
                Prop(holder, $"Chalk_{side}_2", ChalkPath, new Vector3(mirror * 0.78f, box.min.y, faceZ), 90f);
            }

            Record("доска", $"карниз, полка с мелом, две пилястры на {box.max.y:F2} м", TrimPath);
        }

        /// <summary>
        /// Горизонтальная тяга из карнизных отрезков. Профиль не растягивается
        /// на всю длину: карниз в тринадцать метров — это размазанная по стене
        /// одна деталь. Он набирается кусками, каждый в пределах своей доли.
        /// </summary>
        private static void Rail(Transform parent, string name, string path, Vector3 center, float span,
            float height, float depth, int yawSteps)
        {
            GameObject probe = Spawn(parent, name + "_0", path, yawSteps);
            if (probe == null)
            {
                return;
            }

            bool alongX = Mathf.Abs(yawSteps) % 2 == 0;
            Vector3 natural = WorldSize(probe);
            float piece = alongX ? natural.x : natural.z;
            int count = Mathf.Max(1, Mathf.RoundToInt(span / Mathf.Max(0.01f, piece)));
            float step = span / count;
            Vector3 size = alongX ? new Vector3(step, height, depth) : new Vector3(depth, height, step);

            for (int i = 0; i < count; i++)
            {
                GameObject part = i == 0 ? probe : Spawn(parent, $"{name}_{i}", path, yawSteps);
                if (part == null)
                {
                    return;
                }

                part.name = $"{name}_{i}";
                float offset = -span * 0.5f + step * (i + 0.5f);
                SizeTo(part, size);
                CenterAt(part, alongX
                    ? new Vector3(center.x + offset, center.y, center.z)
                    : new Vector3(center.x, center.y, center.z + offset));
            }
        }

        // ────────────────────────────────────────────────────────────────
        // Подвесные указатели, парты, шкаф, вешалка
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Рамка вокруг подвесной таблички. Саму табличку моделью не одеваем:
        /// буквы висят в двух сантиметрах от её граней, и любая модель другой
        /// толщины утопила бы их внутрь.
        ///
        /// Рамка идёт цветом варианта — тем же, что буква и полосы на полу.
        /// Так указатель читается своим цветом даже с расстояния, на котором
        /// букву уже не разобрать.
        /// </summary>
        private static void DressSign(GameObject arena, ExamSide side)
        {
            string signName = side == ExamSide.A ? "Decor/Sign_A" : "Decor/Sign_B";
            Transform sign = Find(arena, signName);
            Transform plate = sign != null ? sign.Find("Plate") : null;
            var renderer = plate != null ? plate.GetComponent<Renderer>() : null;
            if (renderer == null)
            {
                Note($"на арене нет таблички {signName}");
                return;
            }

            Bounds box = renderer.bounds;
            Transform holder = new GameObject("Frame").transform;
            holder.SetParent(sign, false);
            holder.localPosition = Vector3.zero;
            holder.localRotation = Quaternion.identity;
            holder.localScale = Vector3.one;

            Material tone = SideTone(side);
            const float bar = 0.10f;
            float depth = box.size.z + 0.05f;

            Bar(holder, "Frame_Top", tone,
                new Vector3(box.center.x, box.max.y + bar * 0.5f, box.center.z),
                new Vector3(box.size.x + bar * 2f, bar, depth));
            Bar(holder, "Frame_Bottom", tone,
                new Vector3(box.center.x, box.min.y - bar * 0.5f, box.center.z),
                new Vector3(box.size.x + bar * 2f, bar, depth));
            Bar(holder, "Frame_Left", tone,
                new Vector3(box.min.x - bar * 0.5f, box.center.y, box.center.z),
                new Vector3(bar, box.size.y, depth));
            Bar(holder, "Frame_Right", tone,
                new Vector3(box.max.x + bar * 0.5f, box.center.y, box.center.z),
                new Vector3(bar, box.size.y, depth));
        }

        /// <summary>
        /// Парты вдоль стен. Коробка блокаута — это двухместная парта со
        /// скамьёй; в паке двухместных нет, зато есть школьный стул с пюпитром,
        /// и два таких внутри одной коробки читаются экзаменационным рядом
        /// лучше, чем один растянутый вдвое стол.
        ///
        /// Мебель пака <b>детская</b>, и в натуральный рост она не годится:
        /// парта 0.51 × 0.62 рядом с персонажем шириной 0.72 читалась на
        /// рендере приёмки детским садом, а не экзаменом. Габарит приведён
        /// к 0.95 м — см. <see cref="ExamDeskHeight"/>. Пара таких занимает
        /// 1.56 м в коробке 1.50, то есть по краям выходит на три сантиметра,
        /// и это разрешено: предмет шире своей коробки правилами не запрещён,
        /// а вплотную сдвинутые парты как раз и читаются рядом, а не мебелью
        /// на витрине.
        ///
        /// <b>На партах лежат учебники.</b> Пустая столешница на любом ракурсе
        /// выдаёт декорацию: класс, в котором идёт экзамен, не бывает убранным.
        /// Стопка кладётся через одну парту и с чередованием модели — так ряд
        /// не превращается в узор из одинаковых кубиков.
        /// </summary>
        private static void DressDeskRows(GameObject arena)
        {
            int dressed = 0;
            int books = 0;

            foreach (string sideLetter in new[] { "L", "R" })
            {
                for (int i = 1; ; i++)
                {
                    Transform desk = Find(arena, $"Decor/Desk_{sideLetter}{i}");
                    if (desk == null)
                    {
                        break;
                    }

                    foreach (Renderer part in desk.GetComponentsInChildren<Renderer>(true))
                    {
                        part.enabled = false;
                    }

                    Transform holder = new GameObject("Dress").transform;
                    holder.SetParent(desk, false);
                    holder.localPosition = Vector3.zero;
                    holder.localRotation = Quaternion.identity;
                    holder.localScale = Vector3.one;

                    for (int seat = 0; seat < 2; seat++)
                    {
                        float x = seat == 0 ? -0.38f : 0.38f;
                        Prop(holder, $"ExamDesk_{seat + 1}", ExamDeskPath,
                            desk.position + new Vector3(x, 0f, -0.10f), 0f, ExamDeskHeight);
                    }

                    if (i % 2 == 1)
                    {
                        // Столешница моноблока — на 0.72 от пола: коробка
                        // блокаута и модель после подгонки сходятся высотой,
                        // поэтому книги кладутся на число, а не на замер.
                        // Модель выбирается по НОМЕРУ парты, а не по стороне
                        // зала: разные стопки слева и справа разводят зеркальность
                        // зала на пару сотен треугольников, и замер это ловит.
                        // Зеркалятся и смещение, и разворот — левая колонна
                        // обязана быть отражением правой, а не её вариацией.
                        string stack = i % 4 == 1 ? BooksPath : BooksAltPath;
                        float side = sideLetter == "L" ? -1f : 1f;
                        if (Prop(holder, "Books", stack,
                                desk.position + new Vector3(side * 0.34f, 0.72f, 0.06f),
                                side * 18f, 0f) != null)
                        {
                            books++;
                        }
                    }

                    dressed++;
                }
            }

            if (dressed > 0)
            {
                Record("парты", $"{dressed} коробок, по два места в каждой, стопок книг {books}",
                    ExamDeskPath);
            }
        }

        /// <summary>
        /// Шкаф у дальней стены превращается в банк школьных шкафчиков: четыре
        /// секции 0.90 × 1.15, два ряда в высоту. Одна растянутая на всю коробку
        /// секция дала бы дверцу в человеческий рост и ручку размером с голову.
        /// </summary>
        private static void DressCabinet(GameObject arena)
        {
            Transform cabinet = Find(arena, "Decor/Cabinet");
            var renderer = cabinet != null ? cabinet.GetComponent<Renderer>() : null;
            if (renderer == null)
            {
                Note("на арене нет шкафа Cabinet");
                return;
            }

            Bounds box = renderer.bounds;
            Transform holder = Holder(cabinet.gameObject, "Dress");
            if (holder == null)
            {
                return;
            }

            const int columns = 2;
            const int rows = 2;
            float stepX = box.size.x / columns;
            float stepY = box.size.y / rows;

            for (int c = 0; c < columns; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    GameObject locker = Spawn(holder, $"Locker_{c}_{r}", LockerPath, 2);
                    if (locker == null)
                    {
                        return;
                    }

                    SizeTo(locker, new Vector3(stepX, stepY, box.size.z));
                    CenterAt(locker, new Vector3(
                        box.min.x + stepX * (c + 0.5f),
                        box.min.y + stepY * (r + 0.5f),
                        box.center.z));
                }
            }

            HideRenderer(cabinet.gameObject);
            Record("шкаф", $"{columns}×{rows} шкафчиков по {stepX:F2}×{stepY:F2} м", LockerPath);
        }

        /// <summary>
        /// Вешалка остаётся стойкой блокаута — стойки с перекладиной в паках
        /// нет, — но получает то, ради чего вешалка в классе и стоит: рюкзаки.
        /// Без них она читается голой трубой непонятного назначения.
        /// </summary>
        private static void DressCoatRack(GameObject arena)
        {
            Transform rack = Find(arena, "Decor/CoatRack");
            Transform bar = rack != null ? rack.Find("Bar") : null;
            var renderer = bar != null ? bar.GetComponent<Renderer>() : null;
            if (renderer == null)
            {
                Note("на арене нет вешалки CoatRack");
                return;
            }

            Bounds box = renderer.bounds;
            Transform holder = new GameObject("Bags").transform;
            holder.SetParent(rack, false);
            holder.localPosition = Vector3.zero;
            holder.localRotation = Quaternion.identity;
            holder.localScale = Vector3.one;

            for (int i = 0; i < 2; i++)
            {
                float t = (i + 0.5f) / 2f;
                GameObject bag = Spawn(holder, $"SchoolBag_{i + 1}", SchoolBagPath, i == 0 ? 1 : 3);
                if (bag == null)
                {
                    return;
                }

                Vector3 natural = WorldSize(bag);
                CenterAt(bag, new Vector3(
                    box.center.x,
                    box.min.y - natural.y * 0.5f,
                    box.min.z + box.size.z * t));
            }

            Record("вешалка", "два рюкзака на перекладине", SchoolBagPath);
        }

        // ────────────────────────────────────────────────────────────────
        // Общая механика посадки
        // ────────────────────────────────────────────────────────────────

        /// <summary>Одеть коробку одной моделью, растянутой точно в её габарит.</summary>
        private static void Fill(GameObject arena, string path, string prefab, string title, int yawSteps)
        {
            Transform box = Find(arena, "Decor/" + path);
            var renderer = box != null ? box.GetComponent<Renderer>() : null;
            if (renderer == null)
            {
                Note($"на арене нет коробки {path}");
                return;
            }

            Bounds bounds = renderer.bounds;
            Transform holder = Holder(box.gameObject, "Dress");
            GameObject model = Spawn(holder, "Model", prefab, yawSteps);
            if (model == null)
            {
                return;
            }

            HideRenderer(box.gameObject);
            SizeTo(model, bounds.size);
            CenterAt(model, bounds.center);
            Record(title, $"{bounds.size.x:F2}×{bounds.size.y:F2}×{bounds.size.z:F2} м", prefab);
        }

        /// <summary>
        /// Поставить модель основанием в точку.
        ///
        /// <paramref name="targetHeight"/> — во сколько метров привести высоту;
        /// ноль оставляет натуральную. Нужно там, где пак сделан под другой
        /// рост: мебель PolygonKids — детская, и парта 0.63 м рядом с нашим
        /// персонажем шириной 0.72 читается игрушечной.
        /// </summary>
        internal static GameObject Prop(Transform parent, string name, string path, Vector3 basePoint, float yaw,
            float targetHeight = 0f)
        {
            if (!DressKit.TryLoad(path, out GameObject prefab))
            {
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.localScale = Vector3.one;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Scenery(go, null);

            if (targetHeight > 0.0001f && WorldBounds(go, out Bounds natural) && natural.size.y > 0.0001f)
            {
                go.transform.localScale *= targetHeight / natural.size.y;
            }

            if (!WorldBounds(go, out Bounds bounds))
            {
                go.transform.position = basePoint;
                return go;
            }

            // Ставим основанием, а не центром: пивоты моделей пака стоят где
            // угодно, и посадка по корню закапывает предмет наполовину.
            go.transform.position += basePoint - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            return go;
        }

        /// <summary>
        /// Пустышка с единичным мировым масштабом внутри растянутой коробки.
        ///
        /// Коробки блокаута — это кубы, растянутые до размера, и всё вложенное
        /// в них наследует растяжение. Пустышка с обратным масштабом возвращает
        /// детям метры: дальше модели ставятся мировыми координатами, без
        /// пересчёта в доли коробки.
        /// </summary>
        private static Transform Holder(GameObject box, string name)
        {
            Vector3 parentScale = box.transform.lossyScale;
            if (Mathf.Abs(parentScale.x) < 1e-4f || Mathf.Abs(parentScale.y) < 1e-4f || Mathf.Abs(parentScale.z) < 1e-4f)
            {
                Note($"коробка {box.name} вырождена, дресс в неё не садится");
                return null;
            }

            var holder = new GameObject(name).transform;
            holder.SetParent(box.transform, false);
            holder.localPosition = Vector3.zero;
            holder.localRotation = Quaternion.identity;
            holder.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);
            return holder;
        }

        internal static GameObject Spawn(Transform parent, string name, string path, int yawSteps)
        {
            if (parent == null || !DressKit.TryLoad(path, out GameObject prefab))
            {
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.localScale = Vector3.one;
            go.transform.localRotation = Quaternion.Euler(0f, 90f * yawSteps, 0f);
            Scenery(go, null);
            return go;
        }

        /// <summary>
        /// Привести мировой габарит модели к заданному. Считается по мировым
        /// границам, поэтому доворот на 90° учитывается сам собой — не тем
        /// способом, которым он однажды положил стог поперёк этажа Duck Hunt.
        /// </summary>
        internal static void SizeTo(GameObject go, Vector3 worldSize)
        {
            if (!WorldBounds(go, out Bounds bounds))
            {
                return;
            }

            Vector3 s = go.transform.localScale;
            Quaternion rotation = go.transform.rotation;
            Vector3 have = Quaternion.Inverse(rotation) * bounds.size;
            Vector3 want = Quaternion.Inverse(rotation) * worldSize;

            go.transform.localScale = new Vector3(
                s.x * Mathf.Abs(want.x) / Mathf.Max(1e-4f, Mathf.Abs(have.x)),
                s.y * Mathf.Abs(want.y) / Mathf.Max(1e-4f, Mathf.Abs(have.y)),
                s.z * Mathf.Abs(want.z) / Mathf.Max(1e-4f, Mathf.Abs(have.z)));
        }

        internal static void CenterAt(GameObject go, Vector3 worldCenter)
        {
            if (!WorldBounds(go, out Bounds bounds))
            {
                go.transform.position = worldCenter;
                return;
            }

            go.transform.position += worldCenter - bounds.center;
        }

        internal static Vector3 WorldSize(GameObject go)
        {
            return WorldBounds(go, out Bounds bounds) ? bounds.size : Vector3.one;
        }

        private static bool WorldBounds(GameObject go, out Bounds bounds)
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

        /// <summary>Плоская планка из примитива: рамка, шов, накладка петли.</summary>
        internal static void Bar(Transform parent, string name, Material tone, Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Scenery(go, tone);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            SizeTo(go, size);
            CenterAt(go, center);
        }

        /// <summary>
        /// Пометить объект декорацией: снять коллайдеры, увести на <c>Default</c>,
        /// пометить статикой.
        ///
        /// Коллайдеры срезаются всегда. Здесь это критично вдвойне: створки
        /// собирают опору игрока из <b>всех коллайдеров в своих детях</b>
        /// (<c>HingedFloorHatch.CacheDoorColliders</c>), и забытый коллайдер
        /// петли стал бы второй поверхностью прямо на оси вращения.
        /// </summary>
        internal static void Scenery(GameObject go, Material tone)
        {
            foreach (Collider collider in go.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider, true);
            }

            if (tone != null)
            {
                foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterial = tone;
                }
            }

            SetLayer(go, LayerMask.NameToLayer("Default"));
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                SetLayer(go.transform.GetChild(i).gameObject, layer);
            }
        }

        /// <summary>
        /// Перекрасить <b>все слоты</b> рендерера, а не первый.
        ///
        /// Модели Synty собраны из нескольких подмешей с разными материалами
        /// атласа: у каменной плиты пола это сама плита и сколы в ней.
        /// Присвоение <c>sharedMaterial</c> пишет только нулевой слот, и пол
        /// зала вышел из первой пересборки крашеным наполовину — пятнами
        /// чужого пака поверх нашего тона. Ровно об этом предупреждает
        /// <see cref="DressKit.Repaint"/>, и ровно это здесь и повторилось.
        ///
        /// <paramref name="extra"/> красит слоты со второго. Пусто — красятся
        /// все одинаково.
        /// </summary>
        internal static void PaintAll(Renderer renderer, Material primary, Material extra = null)
        {
            if (renderer == null || primary == null)
            {
                return;
            }

            Material[] slots = renderer.sharedMaterials;
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = i == 0 || extra == null ? primary : extra;
            }

            renderer.sharedMaterials = slots;
        }

        /// <summary>
        /// Перекрасить предмет целиком, вместе с детьми.
        ///
        /// Отдельный вход нужен потому, что у моделей Synty рендерер сидит
        /// <b>не на корне</b>: у префаба корень пустой, а меш висит ребёнком.
        /// Перекраска через <c>GetComponent</c> на таком предмете молча
        /// не делает ничего — так зеркальные шкафчики остались синими после
        /// того, как настоящие уже стали серыми.
        /// </summary>
        internal static void PaintTree(GameObject go, Material primary, Material extra = null)
        {
            if (go == null)
            {
                return;
            }

            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                PaintAll(renderer, primary, extra);
            }
        }

        private static void HideRenderer(GameObject box)
        {
            var renderer = box.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }
        }

        private static Transform Find(GameObject arena, string path)
        {
            Transform found = arena.transform.Find(path);
            if (found == null)
            {
                foreach (Transform candidate in arena.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate.name == path)
                    {
                        return candidate;
                    }
                }
            }

            return found;
        }

        // ────────────────────────────────────────────────────────────────
        // Материалы и отчёт
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Цвет варианта — тот же, которым билдер красит букву и указатель,
        /// и приходит он из палитры 4.2. Один источник: разойдись они, «синее
        /// = А» перестало бы работать ровно там, где оно нужнее всего.
        /// </summary>
        private static Material SideTone(ExamSide side)
        {
            return ExamPalette.Get(side == ExamSide.A ? ExamPalette.Tone.SideA : ExamPalette.Tone.SideB);
        }

        private static void Note(string text)
        {
            if (!Notes.Contains(text))
            {
                Notes.Add(text);
            }
        }

        private static void Record(string title, string detail, string prefabPath)
        {
            string shortName = prefabPath.Contains("/")
                ? prefabPath.Substring(prefabPath.LastIndexOf('/') + 1).Replace(".prefab", string.Empty)
                : prefabPath;
            Placed.Add($"{title.PadRight(16)} {shortName.PadRight(34)} {detail}");
        }

        /// <summary>
        /// Таблица «что чем одето и какой ценой». Печатается пересборкой:
        /// подбор модели обязан быть проверяемым числом, а не словом «подобрал».
        /// </summary>
        internal static string Report()
        {
            var report = new StringBuilder();
            report.Append("🪑 «Экзамен», дресс 4.1 — AncientEmpire, Kids, Generic, Nightclubs");

            foreach (string line in Placed)
            {
                report.Append("\n— ").Append(line);
            }

            IReadOnlyList<string> missing = DressKit.Missing;
            for (int i = 0; i < missing.Count; i++)
            {
                report.Append("\n⚠️ модель не найдена: ").Append(missing[i]);
            }

            for (int i = 0; i < Notes.Count; i++)
            {
                report.Append("\n⚠️ ").Append(Notes[i]);
            }

            int total = missing.Count + Notes.Count;
            report.Append("\nЗамечаний пересборки: ").Append(total);
            return report.ToString();
        }
    }
}
