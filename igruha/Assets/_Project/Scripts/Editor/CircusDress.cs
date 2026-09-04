using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Entry = Igruha.EditorTools.DressKit.Entry;
using Fit = Igruha.EditorTools.DressKit.Fit;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Каталог одевания циркового шатра — общий на «Секундомер» и «Порядок
    /// банок» (подфаза 4.1).
    ///
    /// <b>Почему общий, а не StopwatchDress.</b> Арена строится одним
    /// <see cref="CircusArenaBuilder"/> и физически лежит в двух сценах:
    /// клетки, прутья, цепи, яма и медведь есть и в <c>Stopwatch.unity</c>,
    /// и в <c>CansOrder.unity</c>. Дресс внутри одной игры означал бы, что
    /// вторую придётся одевать заново, после чего два шатра разъедутся по
    /// виду — а это одно и то же место, игрок обязан его узнать.
    ///
    /// Своё у «Секундомера» остаётся ровно четыре вещи и живёт в других
    /// файлах: кнопка в клетке (<see cref="StopwatchPropBuilder"/>), табло,
    /// прожекторы-отвлекалки и звук.
    ///
    /// <b>Чего в каталоге нет намеренно.</b>
    ///
    /// <i>Купол и стена шатра.</i> Полосатой ткани нет ни в одном из
    /// двенадцати паков. `SM_Prop_Tent_Large_01/02` — наружные шатры
    /// 26.6 × 12.1 × 16.8 м: изнутри у них backface, а нам нужен интерьер
    /// диаметром 36 м. Купол строится геометрией — 32 радиальных клина через
    /// два тона палитры, — а стена остаётся на палитре целиком: модель,
    /// растянутая на 3.7 м сегмента, читается бревном (правило подфазы 4.1).
    ///
    /// <i>Клетка.</i> `SM_Prop_Dancing_Cage_01` круглая и 1.5 м в плане против
    /// нашей квадратной 2.88 м. Растяжение вдвое сделало бы прутья толщиной
    /// с руку. Клетка добирается геометрией — угловые стойки, круглые прутья,
    /// поперечные пояса, — и красится палитрой.
    ///
    /// <i>Ферма.</i> `SM_Bld_House_Truss_01/02` — плоские стропильные фермы
    /// двускатной крыши. Кольцевой фермы под куполом в паках нет; она тоже
    /// геометрия.
    ///
    /// <b>Модели идут в родных материалах пака, без перекраски.</b> Пак
    /// HorrorCarnival и так даёт ровно то, что просит бриф: выцветшие афиши,
    /// облезлое дерево трибун, ржавый металл. Перекрашивать это в плоский тон
    /// значило бы стереть уже правильный цвет. Палитра занимается тем, что
    /// моделями не одевается вовсе — куполом, стеной, бортом ямы и прутьями.
    /// </summary>
    internal static class CircusDress
    {
        /// <summary>Что именно одевается моделью пака.</summary>
        internal enum Kind
        {
            None,

            /// <summary>Опилки на дне ямы: диски грунта поверх крашеного пола.</summary>
            Sawdust
        }

        private sealed class Wear
        {
            public readonly string Title;
            public readonly Entry[] Entries;

            public Wear(string title, params Entry[] entries)
            {
                Title = title;
                Entries = entries;
            }
        }

        private const string Carnival = "Assets/Synty/PolygonHorrorCarnival/Prefabs/";
        private const string CarnivalProps = Carnival + "Props/";
        private const string CarnivalEnv = Carnival + "Environment/";
        private const string Generic = "Assets/Synty/PolygonGeneric/Prefabs/Props/";
        private const string Shops = "Assets/Synty/PolygonShops/Prefabs/Props/";

        /// <summary>
        /// Звено цепи. <b>Пивот наверху</b> (<c>pivotOffset.y = −1.10</c>):
        /// модель растёт вниз от точки посадки, а не вверх. Проверено макетом
        /// 4.0 — с посадкой снизу все восемь цепей свесились в яму.
        /// </summary>
        internal const string ChainPath = Generic + "SM_Gen_Prop_Chain_01.prefab";

        /// <summary>Натуральная длина звена цепи, м. По ней считается число копий в подвесе.</summary>
        internal const float ChainSegmentLength = 2.293f;

        /// <summary>Проушина на ферме: место, откуда цепь выходит.</summary>
        internal const string ChainAnchorPath = Generic + "SM_Gen_Prop_Chain_Anchor_01.prefab";

        /// <summary>
        /// Медведь. Вопреки имени это не изваяние, а модель медведя на задних
        /// лапах с открытой пастью — ровно та поза, которую спека 3.5 просит
        /// на нижней ступени. Настоящего медведя в паках нет: поиск по слову
        /// bear среди 8198 префабов индекса даёт бороды, капкан и плюшевых
        /// мишек. Выбрано геймдизайнером 04.09 из трёх вариантов брифа 14.4.
        /// </summary>
        internal const string BearPath = Shops + "SM_Prop_Bear_Statue_01.prefab";

        /// <summary>
        /// Рост медведя, м. Натуральные 3.10 м рядом с капсулой 1.8 м дают
        /// не медведя, а слона: 2.4 — «заметно крупнее игрока», как просит
        /// спека 8.4, и при этом лапа достаёт до решётки нижней клетки.
        /// </summary>
        internal const float BearHeight = 2.4f;

        /// <summary>Цирковая бочка со звёздами — тумба кнопки и полки.</summary>
        internal const string BarrelPath = CarnivalProps + "SM_Prop_Barrel_01.prefab";

        /// <summary>Театральный прожектор с шторками. Пивот сверху — вешается на ферму.</summary>
        internal const string SpotlightPath = CarnivalProps + "SM_Prop_Spotlight_01.prefab";

        /// <summary>Гирлянда лампочек, 2.49 м. Идёт по контуру табло.</summary>
        internal const string BulbStringPath = CarnivalProps + "SM_Prop_Light_01.prefab";

        private static readonly Dictionary<Kind, Wear> Catalog = new Dictionary<Kind, Wear>
        {
            {
                // Опилки. Диск грунта 7.80 × 0.09 × 7.53 м на яму Ø 17.3 —
                // сетка копий, а не растяжение: растянутый втрое диск теряет
                // рваный край, ради которого он и взят, и превращается в блин.
                //
                // Коробка блокаута — цилиндр дна ямы с MeshCollider, и её
                // рендерер гасить нельзя: диски квадратные и углы круга не
                // закроют. Поэтому опилки ставятся <b>поверх</b> крашеного
                // пола отдельным методом, а не через Apply.
                Kind.Sawdust,
                new Wear("опилки", new Entry(CarnivalEnv + "SM_Env_Ground_Dirt_Round_01.prefab", Fit.Tile))
            }
        };

        /// <summary>Сбросить кэш габаритов и список ненайденного перед пересборкой.</summary>
        internal static void Begin()
        {
            DressKit.Begin();
        }

        /// <summary>Пути моделей, которых не оказалось в проекте: паки Synty ставит каждый себе сам.</summary>
        internal static IReadOnlyList<string> Missing
        {
            get { return DressKit.Missing; }
        }

        /// <summary>
        /// Одеть коробку блокаута моделью нужного вида. Рендерер коробки
        /// гаснет, модель садится внутрь по её габаритам; коллайдер и слой
        /// коробки не трогаются.
        /// </summary>
        internal static GameObject Apply(GameObject box, Kind kind, System.Random rng)
        {
            if (kind == Kind.None || !Catalog.TryGetValue(kind, out Wear wear))
            {
                return null;
            }

            return DressKit.Apply(box, wear.Entries, rng, null, false);
        }

        /// <summary>
        /// Поставить модель пака <b>мимо коробки блокаута</b>, в мировую точку
        /// и в натуральном масштабе.
        ///
        /// Нужно там, где коробка — тонкий триггер или цилиндр, а предмет
        /// обязан быть виден целиком: медведь внутри капсулы, бочка кнопки
        /// поверх невидимой тумбы, прожектор на ферме.
        ///
        /// <paramref name="targetHeight"/> — во сколько метров привести высоту
        /// модели; ноль оставляет натуральную. Предмет ставится <b>основанием
        /// в точку</b>, а не центром.
        /// </summary>
        /// <param name="hangFromTop">
        /// Ставить <b>верхом</b> в точку, а не основанием. Нужно всему, что
        /// висит: цепь и прожектор крепятся к ферме сверху, и посадка по низу
        /// увела бы их на свою длину вниз.
        /// </param>
        internal static GameObject Prop(Transform parent, string propName, string prefabPath, Vector3 position,
            float yaw, float targetHeight = 0f, bool castShadows = true, bool hangFromTop = false)
        {
            if (!DressKit.TryLoad(prefabPath, out GameObject prefab))
            {
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = propName;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one;
            MarkAsScenery(go, castShadows);

            if (!TryWorldBounds(go, out Bounds bounds))
            {
                go.transform.position = position;
                return go;
            }

            if (targetHeight > 0.0001f && bounds.size.y > 0.0001f)
            {
                float scale = targetHeight / bounds.size.y;
                go.transform.localScale = Vector3.one * scale;
                TryWorldBounds(go, out bounds);
            }

            // Смещение от корня до края габарита: пивоты моделей пака стоят
            // где угодно, и ставить по корню значит закапывать половину.
            float anchorY = hangFromTop ? bounds.max.y : bounds.min.y;
            var anchor = new Vector3(bounds.center.x, anchorY, bounds.center.z);
            go.transform.position = position + (go.transform.position - anchor);
            return go;
        }

        /// <summary>
        /// Подвес из звеньев цепи: от точки крепления вниз на заданную длину.
        ///
        /// Столбик копий, а не одна растянутая: цепь длиной 5.7–10 м из одного
        /// звена 2.29 м вытянулась бы вчетверо вместе с толщиной, и вместо
        /// цепи получился бы трос. Последнее звено ужимается по высоте на
        /// остаток — шов приходится вплотную к крыше клетки и в кадре не виден.
        /// </summary>
        /// <param name="maxLength">
        /// На какую длину заготовить звенья. Считается по <b>нижней</b> ступени
        /// клетки, а не по стартовой: клетка едет вниз за каждую ошибку, и
        /// цепь обязана дотянуться до неё в самом низком положении. Лишние
        /// звенья выключит <see cref="Igruha.Minigames.Circus.CageChain"/>.
        /// </param>
        internal static GameObject Chain(Transform parent, string chainName, Vector3 top, float maxLength)
        {
            if (maxLength <= 0.01f || !DressKit.TryLoad(ChainPath, out GameObject prefab))
            {
                return null;
            }

            var root = new GameObject(chainName);
            root.transform.SetParent(parent, false);
            root.transform.position = top;

            int count = Mathf.CeilToInt(maxLength / ChainSegmentLength);
            var links = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                links[i] = LinkSegment(root.transform, prefab, i, i * ChainSegmentLength);
            }

            var chain = root.AddComponent<Igruha.Minigames.Circus.CageChain>();
            var serialized = new SerializedObject(chain);
            SerializedProperty array = serialized.FindProperty("links");
            array.arraySize = count;
            for (int i = 0; i < count; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = links[i];
            }

            serialized.FindProperty("segmentLength").floatValue = ChainSegmentLength;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        /// <summary>Привязать цепь к крыше клетки: до неё она и вытравливается.</summary>
        internal static void BindChain(GameObject chainRoot, Transform target)
        {
            if (chainRoot == null || target == null)
            {
                return;
            }

            var chain = chainRoot.GetComponent<Igruha.Minigames.Circus.CageChain>();
            if (chain == null)
            {
                return;
            }

            var serialized = new SerializedObject(chain);
            serialized.FindProperty("target").objectReferenceValue = target;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Подрезать сразу, а не ждать цикла редактора: он не тикает, пока
            // окно Unity не в фокусе, и сцена сохранилась бы с цепями во всю
            // длину — они свисали бы сквозь клетки в яму.
            chain.Apply();
        }

        private static Transform LinkSegment(Transform parent, GameObject prefab, int index, float offset)
        {
            var link = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            link.name = $"Link_{index + 1:00}";
            link.transform.localPosition = new Vector3(0f, -offset, 0f);
            link.transform.localRotation = Quaternion.identity;
            link.transform.localScale = Vector3.one;
            MarkAsScenery(link, true);
            return link.transform;
        }

        /// <summary>
        /// Таблица «вид → модель → габарит»: что во что одето и какой ценой.
        /// Печатается пересборкой, потому что подбор модели обязан быть
        /// проверяемым числом, а не словом «подобрал».
        /// </summary>
        internal static string Report()
        {
            var report = new StringBuilder();
            report.Append("🎪 Цирк, дресс 4.1 — PolygonHorrorCarnival + Generic + Shops");

            foreach (KeyValuePair<Kind, Wear> pair in Catalog)
            {
                AppendModels(report, pair.Value.Title, pair.Value.Entries);
            }

            AppendModel(report, "цепь", ChainPath);
            AppendModel(report, "проушина", ChainAnchorPath);
            AppendModel(report, "медведь", BearPath);
            AppendModel(report, "бочка кнопки", BarrelPath);
            AppendModel(report, "прожектор", SpotlightPath);
            AppendModel(report, "гирлянда", BulbStringPath);

            report.Append("\n— геометрией, моделей нет: купол (32 клина), стена шатра, ферма-кольцо, " +
                          "прутья и стойки клетки, поясок борта ямы, рама табло");
            return report.ToString();
        }

        private static void AppendModels(StringBuilder report, string title, Entry[] entries)
        {
            report.Append("\n— ").Append(title.PadRight(14));
            for (int i = 0; i < entries.Length; i++)
            {
                report.Append(i == 0 ? string.Empty : ", ");
                AppendBounds(report, entries[i].Prefab, entries[i].Fit.ToString());
            }
        }

        private static void AppendModel(StringBuilder report, string title, string path)
        {
            report.Append("\n— ").Append(title.PadRight(14));
            AppendBounds(report, path, "Prop");
        }

        private static void AppendBounds(StringBuilder report, string path, string fit)
        {
            string shortName = path.Substring(path.LastIndexOf('/') + 1).Replace(".prefab", string.Empty);
            report.Append(shortName);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                report.Append(" (нет в проекте)");
                return;
            }

            Bounds bounds = DressKit.GetBounds(prefab, path);
            report.Append(" [")
                .Append(bounds.size.x.ToString("F2")).Append('×')
                .Append(bounds.size.y.ToString("F2")).Append('×')
                .Append(bounds.size.z.ToString("F2")).Append(" м, ")
                .Append(fit).Append(']');
        }

        /// <summary>
        /// Пометить предмет декорацией: снять коллайдеры и увести на
        /// <c>Default</c>.
        ///
        /// Коллайдеры срезаются всегда и без исключений: столкновения на арене
        /// держит блокаут, а мешевый коллайдер модели дал бы вторую поверхность
        /// другой формы поверх выверенной. В этой игре цена особенно высока:
        /// лишний коллайдер под клеткой поймал бы выпавшего вместо опилок,
        /// а лишний коллайдер на ферме — цепь.
        /// </summary>
        internal static void MarkAsScenery(GameObject go, bool castShadows)
        {
            var colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Object.DestroyImmediate(colliders[i], true);
            }

            if (!castShadows)
            {
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            SetLayer(go, LayerMask.NameToLayer("Default"));
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                SetLayer(go.transform.GetChild(i).gameObject, layer);
            }
        }

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
    }
}
