using UnityEditor;
using UnityEngine;
using Igruha.Minigames.Exam;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Поверхности «Экзамена» — подфаза 4.2. Всё, что не одевается моделью:
    /// пол, стены, потолок, яма, грифель доски, дорожка зоны возврата.
    ///
    /// <b>Большая плоскость не одевается моделью — но настилается.</b> Забор,
    /// растянутый на двадцать восемь метров, читается бревном толщиной
    /// с человека; тот же забор, повторённый копиями, читается забором. Пол
    /// зала мостится каменными плитами шагом 2.88 м (4 ШП) — вдвое крупнее
    /// настила створок, потому что зал не паркетный, а каменный, и мелкая
    /// сетка на 750 м² превратилась бы в рябь. Стены остаются материалом:
    /// стеновая панель пака 2.5 × 3.0 на высоте 5.76 м потребовала бы двух
    /// рядов с видимым стыком поперёк кадра.
    ///
    /// <b>Паркет ушёл с пола зала на платформы, и это правка брифа.</b>
    /// В брифе (14.2) деревом были и пол, и створки. Рендер приёмки 4.1
    /// показал, почему так нельзя: платформы читаются врезанными в пол именно
    /// потому, что они деревянные, а пол каменный. Сделай пол таким же — и
    /// граница площадки пропадёт вместе с половиной читаемости А/Б.
    /// </summary>
    internal static class ExamSurfaces
    {
        /// <summary>Карниз 2.50 × 0.50 × 0.14 — им закрывается верхняя кромка панелей.</summary>
        private const string TrimPath = "Assets/Synty/PolygonAncientEmpire/Prefabs/Buildings/SM_Bld_Trim_05.prefab";

        /// <summary>Шаг швов пола зала, 4 ШП. Вдвое крупнее настила створок — см. описание класса.</summary>
        private const float FloorModule = 2.88f;

        /// <summary>Ширина шва между плитами пола.</summary>
        private const float SeamWidth = 0.05f;

        /// <summary>
        /// Высота панелей по низу стен. Ровно на уровне глаз персонажа
        /// граница «дерево внизу, штукатурка вверху» резала бы кадр пополам;
        /// 1.2 м держит её под поясом и оставляет стену стеной.
        /// </summary>
        private const float WainscotHeight = 1.2f;

        /// <summary>Насколько панели выступают от стены внутрь зала.</summary>
        private const float WainscotDepth = 0.06f;

        /// <summary>
        /// Покрасить арену и настелить то, что настилается. Вызывается после
        /// дресса: часть покраски ложится на его же модели — карниз кафедры,
        /// обвязку доски и шкафчики красит палитра, а не дресс.
        /// </summary>
        internal static void Apply(GameObject arena, ExamConfig config, System.Random rng)
        {
            if (arena == null || config == null)
            {
                return;
            }

            PaintWalls(arena);
            PaintFloor(arena);
            BuildWainscot(arena, config);
            PaintPit(arena);
            PaintProps(arena);
            RepaintDress(arena);
        }

        /// <summary>Стены и потолок — тон камня; потолок светлее, зал перекрыт сверху.</summary>
        private static void PaintWalls(GameObject arena)
        {
            Material stone = ExamPalette.Get(ExamPalette.Tone.Stone);
            foreach (string wall in new[] { "Wall_Far", "Wall_Near", "Wall_Left", "Wall_Right" })
            {
                Paint(arena, wall, stone);
            }

            Paint(arena, "Ceiling", ExamPalette.Get(ExamPalette.Tone.Ceiling));
        }

        /// <summary>
        /// Пол зала: тон камня плюс разрезка швами по модулю 2.88 м.
        ///
        /// <b>Настилать пол плитами пака оказалось нечем.</b> Первый заход
        /// мостил его каменной плитой базового кита, 68 штук: у неё нет ни
        /// фаски, ни кромки, и рендер приёмки показал ровно ту же заливку, что
        /// и до настила. Плитка PolygonShops со вторым материалом дала то же
        /// самое — второй слот у неё уходит на торец, а не на рисунок.
        ///
        /// Швы поэтому рисуются <b>плоскими примитивами</b>: два десятка полос
        /// вместо семи десятков плит, и они видны. Это тот же приём, которым
        /// делаются трещины и выбоины — десяток треугольников на линию против
        /// сотен у любой модели.
        ///
        /// Линии кладутся <b>внутри каждой плиты пола</b>, а не сеткой на весь
        /// зал: посреди зала проём под платформы, и полоса, проложенная
        /// насквозь, повисла бы над ямой.
        /// </summary>
        private static void PaintFloor(GameObject arena)
        {
            Material floor = ExamPalette.Get(ExamPalette.Tone.Floor);
            Material seam = ExamPalette.Get(ExamPalette.Tone.FloorSeam);
            Transform holder = Group(arena.transform, "FloorSeams");
            int lines = 0;

            foreach (string name in new[] { "Floor_Far", "Floor_Near", "Floor_Left", "Floor_Right" })
            {
                Transform slab = arena.transform.Find(name);
                var renderer = slab != null ? slab.GetComponent<Renderer>() : null;
                if (renderer == null)
                {
                    Debug.LogWarning($"⚠️ На арене нет плиты пола {name} — пол останется серым");
                    continue;
                }

                ExamDress.PaintAll(renderer, floor);
                Bounds box = renderer.bounds;
                lines += Seams(holder, seam, box, true);
                lines += Seams(holder, seam, box, false);
            }

            Debug.Log($"🧱 Пол зала разрезан швами: {lines} линий шагом {FloorModule:F2} м");
        }

        /// <summary>Швы одной плиты пола вдоль выбранной оси. Крайние не ставятся: там кромка зала.</summary>
        private static int Seams(Transform holder, Material tone, Bounds box, bool alongX)
        {
            float span = alongX ? box.size.x : box.size.z;
            int cells = Mathf.Max(1, Mathf.RoundToInt(span / FloorModule));
            float step = span / cells;
            float top = box.max.y + 0.012f;
            int placed = 0;

            for (int i = 1; i < cells; i++)
            {
                float offset = (alongX ? box.min.x : box.min.z) + step * i;
                Vector3 center = alongX
                    ? new Vector3(offset, top - 0.01f, box.center.z)
                    : new Vector3(box.center.x, top - 0.01f, offset);
                Vector3 size = alongX
                    ? new Vector3(SeamWidth, 0.02f, box.size.z)
                    : new Vector3(box.size.x, 0.02f, SeamWidth);

                Band(holder, $"Seam_{(alongX ? "X" : "Z")}_{placed}_{center.z:F0}_{center.x:F0}", tone, center, size);
                placed++;
            }

            return placed;
        }

        /// <summary>
        /// Панели по низу стен и карниз поверх них.
        ///
        /// Панели — декор: коллайдеров у них нет, слой <c>Default</c>. Стены
        /// при этом остаются на <c>Ground</c> своими коробками, и деоклюдер
        /// камеры по-прежнему видит их — панель на <c>Default</c> для него
        /// прозрачна, а останавливает камеру коробка позади неё.
        /// </summary>
        private static void BuildWainscot(GameObject arena, ExamConfig config)
        {
            Transform holder = Group(arena.transform, "Wainscot");
            Material panel = ExamPalette.Get(ExamPalette.Tone.Panel);

            float halfW = config.HallWidth * 0.5f;
            float halfD = config.HallDepth * 0.5f;

            // Внутренние грани стен: коробки стоят центром на границе зала
            // и уходят наружу на половину толщины.
            const float wallHalf = 0.2f;
            float inX = halfW - wallHalf;
            float inZ = halfD - wallHalf;
            float y = WainscotHeight * 0.5f;
            float capY = WainscotHeight + 0.06f;

            Band(holder, "Wainscot_Far", panel,
                new Vector3(0f, y, inZ - WainscotDepth * 0.5f),
                new Vector3(config.HallWidth, WainscotHeight, WainscotDepth));
            Band(holder, "Wainscot_Near", panel,
                new Vector3(0f, y, -inZ + WainscotDepth * 0.5f),
                new Vector3(config.HallWidth, WainscotHeight, WainscotDepth));
            Band(holder, "Wainscot_Left", panel,
                new Vector3(-inX + WainscotDepth * 0.5f, y, 0f),
                new Vector3(WainscotDepth, WainscotHeight, config.HallDepth));
            Band(holder, "Wainscot_Right", panel,
                new Vector3(inX - WainscotDepth * 0.5f, y, 0f),
                new Vector3(WainscotDepth, WainscotHeight, config.HallDepth));

            // Карниз по верхней кромке панелей. Без него граница дерева и
            // штукатурки читается стыком двух красок, а не отделкой.
            Cap(holder, "Cap_Far", panel, new Vector3(0f, capY, inZ - 0.09f), config.HallWidth, 0);
            Cap(holder, "Cap_Near", panel, new Vector3(0f, capY, -inZ + 0.09f), config.HallWidth, 0);
            Cap(holder, "Cap_Left", panel, new Vector3(-inX + 0.09f, capY, 0f), config.HallDepth, 1);
            Cap(holder, "Cap_Right", panel, new Vector3(inX - 0.09f, capY, 0f), config.HallDepth, 1);
        }

        /// <summary>Яма: дно и стенки. Почти чёрные и без блика — провал обязан читаться темнотой.</summary>
        private static void PaintPit(GameObject arena)
        {
            Material pit = ExamPalette.Get(ExamPalette.Tone.Pit);
            foreach (string name in new[] { "PitFloor", "PitWall_Far", "PitWall_Near", "PitWall_Left", "PitWall_Right" })
            {
                Paint(arena, name, pit);
            }
        }

        /// <summary>Доска, кафедра, дорожка возврата и мелочь блокаута, которая осталась видимой.</summary>
        private static void PaintProps(GameObject arena)
        {
            Paint(arena, "Board", ExamPalette.Get(ExamPalette.Tone.Slate));
            Paint(arena, "Podium", ExamPalette.Get(ExamPalette.Tone.Panel));
            Paint(arena, "ReturnZone", ExamPalette.Get(ExamPalette.Tone.Runner));

            Material metal = ExamPalette.Get(ExamPalette.Tone.Metal);
            Paint(arena, "Decor/CoatRack/Post", metal);
            Paint(arena, "Decor/CoatRack/Bar", metal);

            Material plate = ExamPalette.Get(ExamPalette.Tone.SignPlate);
            foreach (string sign in new[] { "Decor/Sign_A", "Decor/Sign_B" })
            {
                Paint(arena, sign + "/Rope", metal);
                Paint(arena, sign + "/Plate", plate);
            }

            Paint(arena, "Decor/HostDesk/Screen", ExamPalette.Get(ExamPalette.Tone.ScreenGlow));
        }

        /// <summary>
        /// Перекрасить те модели дресса, чей родной цвет палитре не подходит.
        ///
        /// Шкафчиков это касается по существу, а не по вкусу: замер их корпуса
        /// даёт `#2C67A7` при цвете варианта А `#2A6BB8`. Синий шкаф в кадре —
        /// это второе «синее = А», и стоит он на стороне одной из платформ.
        /// Карниз кафедры и обвязка доски приезжают светлым камнем пака и
        /// сливаются со стеной — им нужен тон панелей.
        /// </summary>
        private static void RepaintDress(GameObject arena)
        {
            Material panel = ExamPalette.Get(ExamPalette.Tone.Panel);
            RepaintByPrefix(arena, "Plinth_", panel);
            RepaintByPrefix(arena, "Rail_", panel);
            RepaintByPrefix(arena, "Pilaster_", panel);
            RepaintByPrefix(arena, "Locker_", ExamPalette.Get(ExamPalette.Tone.LockerBody));
        }

        // ────────────────────────────────────────────────────────────────

        /// <summary>Плоская планка панелей. Примитив: стена — это плоскость, и модель ей не нужна.</summary>
        private static void Band(Transform parent, string name, Material tone, Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Scenery(go);
            go.transform.SetParent(parent, false);
            go.transform.localScale = size;
            go.transform.position = center;

            ExamDress.PaintAll(go.GetComponent<Renderer>(), tone);
        }

        /// <summary>Карниз поверх панелей: отрезки модели пака встык, а не одна растянутая.</summary>
        private static void Cap(Transform parent, string name, Material tone, Vector3 center, float span, int yawSteps)
        {
            var probe = AssetDatabase.LoadAssetAtPath<GameObject>(TrimPath);
            if (probe == null)
            {
                Debug.LogWarning($"⚠️ Не найден карниз {TrimPath} — кромка панелей останется без отделки");
                return;
            }

            bool alongX = yawSteps % 2 == 0;
            int count = Mathf.Max(1, Mathf.RoundToInt(span / 2.5f));
            float step = span / count;

            for (int i = 0; i < count; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(probe, parent);
                go.name = $"{name}_{i}";
                Scenery(go);
                go.transform.localScale = Vector3.one;
                go.transform.rotation = Quaternion.Euler(0f, 90f * yawSteps, 0f);

                Bounds bounds = WorldBounds(go);
                Vector3 want = alongX
                    ? new Vector3(step, 0.12f, 0.12f)
                    : new Vector3(0.12f, 0.12f, step);
                go.transform.localScale = new Vector3(
                    go.transform.localScale.x * want.x / Mathf.Max(1e-4f, alongX ? bounds.size.x : bounds.size.z),
                    go.transform.localScale.y * want.y / Mathf.Max(1e-4f, bounds.size.y),
                    go.transform.localScale.z * want.z / Mathf.Max(1e-4f, alongX ? bounds.size.z : bounds.size.x));

                bounds = WorldBounds(go);
                float offset = -span * 0.5f + step * (i + 0.5f);
                Vector3 target = alongX
                    ? new Vector3(center.x + offset, center.y, center.z)
                    : new Vector3(center.x, center.y, center.z + offset);
                go.transform.position += target - bounds.center;

                foreach (Renderer part in go.GetComponentsInChildren<Renderer>(true))
                {
                    ExamDress.PaintAll(part, tone);
                }
            }
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

        private static void Paint(GameObject arena, string path, Material tone)
        {
            if (tone == null)
            {
                return;
            }

            Transform target = arena.transform.Find(path);
            if (target == null)
            {
                foreach (Transform candidate in arena.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate.name == path)
                    {
                        target = candidate;
                        break;
                    }
                }
            }

            if (target == null)
            {
                Debug.LogWarning($"⚠️ Палитра: на арене нет объекта {path}");
                return;
            }

            ExamDress.PaintAll(target.GetComponent<Renderer>(), tone);
        }

        private static void RepaintByPrefix(GameObject arena, string prefix, Material tone)
        {
            if (tone == null)
            {
                return;
            }

            foreach (Transform candidate in arena.GetComponentsInChildren<Transform>(true))
            {
                if (!candidate.name.StartsWith(prefix))
                {
                    continue;
                }

                foreach (Renderer part in candidate.GetComponentsInChildren<Renderer>(true))
                {
                    ExamDress.PaintAll(part, tone);
                }
            }
        }

        /// <summary>
        /// Пометить декорацией: снять коллайдеры, увести на <c>Default</c>.
        /// Панели и карнизы обязаны быть прозрачны и для физики, и для камеры —
        /// коллайдер на них удвоил бы стену, а слой <c>Ground</c> заставил бы
        /// деоклюдер останавливаться на шести сантиметрах отделки.
        /// </summary>
        private static void Scenery(GameObject go)
        {
            foreach (Collider collider in go.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider, true);
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

        private static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(go.transform.position, Vector3.one);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }
    }
}
