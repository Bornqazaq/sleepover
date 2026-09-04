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
    /// Каталог одевания «Переноски предмета»: какой вид коробки блокаута какой
    /// моделью пака закрывается (подфаза 4.1).
    ///
    /// Здесь только каталог. Замер габаритов, посадка модели в коробку, ряды и
    /// сетки копий, срезание коллайдеров — в <see cref="DressKit"/>, одном на
    /// все мини-игры. Основной пак — <b>POLYGON Construction</b>.
    ///
    /// <b>Модели идут в родных материалах пака, без перекраски.</b> Это отличие
    /// от «Дырки в стене», и оно намеренное: там бриф прямо запрещал узоры на
    /// стене, потому что силуэт выреза и есть игра. Здесь бриф просит ровно то,
    /// что пак и так даёт, — серый бетон, ржавую арматуру, строительный жёлтый,
    /// светлое дерево. Перекрашивать это в плоский тон значило бы стереть уже
    /// правильный цвет. Палитра подфазы 4.2 занимается полами, стенами и
    /// стеклом бутыли — тем, что моделями не одевается вовсе.
    ///
    /// <b>Большая плоскость одевается повтором, а не растяжением.</b>
    ///
    /// До 04.09 полы и стены в каталоге отсутствовали вовсе, и причина
    /// записана была так: модель, растянутая на 28 или 56 метров, читается
    /// бревном. Причина верная, вывод — нет. Растягивать нельзя, а повторять
    /// можно: пол настелен плитами (<see cref="Kind.Floor"/>), периметр собран
    /// из панелей (<see cref="Kind.WallFacade"/>), и подтяжка каждой копии
    /// остаётся у единицы. Запрет касается <c>Fit.Stretch</c> на длинной
    /// коробке, а не самой длинной коробки.
    ///
    /// <b>Чего в каталоге нет намеренно.</b>
    ///
    /// Дно пропасти: у пака нет прямоугольной плиты грунта вовсе. Единственный
    /// подходящий по размеру `SM_Env_Dirt_Square_01` — «органическое» пятно с
    /// рваными краями в прямоугольном габарите, и под ним дно пропасти было
    /// дырявым с 4.1 до 04.09: рендерер коробки гаснет под дресс, а модель углы
    /// не закрывает. Дно красится тоном, фактуру внизу держит нижний ярус.
    ///
    /// Бутыль и бак: их читаемость держится на прозрачности. Бутыли для кулера
    /// нет ни в одном из двенадцати паков (проверено по всем 8198 префабам
    /// индекса), а ближайшие по силуэту — синяя бочка `SM_Prop_Barrel_Water_01`
    /// и `SM_Prop_Barrel_Oil_01` — непрозрачны и закрыли бы собой столбик воды,
    /// то есть отменили бы правку, ради которой делали разбор 3.37. Решение
    /// брифа: бутыль и бак остаются из примитивов, стекло им делает 4.2.
    /// </summary>
    internal static class CarryItemDress
    {
        /// <summary>Что именно одевается.</summary>
        internal enum Kind
        {
            None,

            /// <summary>Завал поперёк общей площадки. Непроходим, сгоняет команды в горлышко.</summary>
            Rubble,

            /// <summary>Доска через пропасть. Единственный путь на ту сторону.</summary>
            Plank,

            /// <summary>Кучка метательного: кирпичи по краям маршрута.</summary>
            Stash,

            /// <summary>Балка на тросе крана. Ходит над горлышком, период 6 сек.</summary>
            Beam,

            /// <summary>Сам кирпич — метательный предмет, живёт в префабе.</summary>
            Brick,

            /// <summary>Перекрытие, по которому бегут: настилается плитами, а не красится.</summary>
            Floor,

            /// <summary>Фасад периметра: нижний пояс стены, панели с окнами и дверями.</summary>
            WallFacade,

            /// <summary>Венец периметра: плита с торчащей арматурой поверх фасада.</summary>
            WallCrown,

            /// <summary>Стенка выемки: борт арены ниже нуля, напротив пропасти.</summary>
            PitWall,

            /// <summary>Кромка обрыва: срез плиты перекрытия с торчащей арматурой.</summary>
            ChasmLip
        }

        /// <summary>Одевание одного вида: чем закрываем и зачем именно этим.</summary>
        private readonly struct Wear
        {
            public readonly string Title;
            public readonly Entry[] Entries;

            /// <summary>
            /// Выбирать модель на каждую копию, а не на коробку целиком.
            /// Нужно ровно фасаду: полсотни одинаковых панелей читаются обоями.
            /// </summary>
            public readonly bool Mix;

            public Wear(string title, params Entry[] entries)
            {
                Title = title;
                Entries = entries;
                Mix = false;
            }

            public Wear(string title, bool mix, params Entry[] entries)
            {
                Title = title;
                Entries = entries;
                Mix = mix;
            }
        }

        private const string Construction = "Assets/Synty/PolygonConstruction/Prefabs/";
        private const string Props = Construction + "Props/";
        private const string Buildings = Construction + "Buildings/";
        private const string Environments = Construction + "Environments/";

        /// <summary>Тачка-ловушка: три штуки вдоль горлышка, поэтому путь вынесен наружу.</summary>
        internal const string WheelbarrowPath = Props + "SM_Prop_Wheelbarrow_01.prefab";

        /// <summary>Стояк прорванной трубы. Ставится рядом со струёй, а не в неё.</summary>
        internal const string StandpipePath = Props + "SM_Prop_Pipe_Concrete_02.prefab";

        /// <summary>Излом прорванной трубы: из него бьёт струя.</summary>
        internal const string SpoutPath = Props + "SM_Prop_Pipe_Concrete_Large_03.prefab";

        /// <summary>Поддон под тарой штабеля.</summary>
        internal const string PalletPath = Props + "SM_Prop_Pallet_01.prefab";

        /// <summary>Лестница на бак: она и объясняет, что это резервуар, а не бочка.</summary>
        internal const string LadderPath = Props + "SM_Prop_Ladder_01.prefab";

        /// <summary>Обвязка у основания бака.</summary>
        internal const string OutletPath = Props + "SM_Prop_Watertank_01.prefab";

        private static readonly Dictionary<Kind, Wear> Catalog = new Dictionary<Kind, Wear>
        {
            {
                // Завал — коробка 6 × 3 × 12 ШИ (4.32 × 2.16 × 8.64 м) на слое
                // Cover, то есть непроходимая и непрозрачная для камеры.
                //
                // Fit.Tile, а не Row: ряд берёт масштаб от высоты коробки и
                // центрируется по короткой оси, оставляя её недозаполненной, —
                // контейнер вышел бы 2.8 м толщиной при коробке 4.32, и полтора
                // метра коллайдера торчали бы за видимой преградой. Игрок
                // упирался бы в пустоту. Сетка заполняет пятно целиком.
                //
                // Одна модель, и она обязана быть <b>выше и короче</b> коробки.
                // Tile умеет только ужимать: модель ниже коробки он прижимает
                // к её верхней грани, и под преградой остаётся щель, а по длине
                // добирает растяжением до предела и вылезает наружу. Замер
                // 04.09 на скипе (2.11 м при коробке 2.16 и 6.14 м при 8.64):
                // щель 5 см снизу и выход 11 см по длине. Контейнер 2.59 × 3.15
                // выше коробки и делит её длину почти нацело — садится в ноль.
                Kind.Rubble,
                new Wear("завал",
                    new Entry(Props + "SM_Prop_Shipping_Container_Small_01.prefab", Fit.Tile))
            },
            {
                // Доска — 12 или 10 ШИ длиной при ширине 2 ШИ и толщине 0.4.
                // Штабель длинных досок 8.23 × 0.20 × 1.11 м ложится в неё почти
                // один в один: растяжение 1.05 по длине и 1.30 по ширине, обе
                // доли внутри предела. Одна копия, а не ряд: доска обязана
                // читаться как сплошной настил, а стык посреди пропасти
                // выглядел бы разрывом ровно там, где по нему идут.
                Kind.Plank,
                new Wear("доска", new Entry(Props + "SM_Prop_Plank_Long_Stack_02.prefab", Fit.Stretch))
            },
            {
                // Кучка кирпича — куб 1.5 ШИ (1.08 м). Четыре штабеля пака
                // высотой 0.82–1.22 м; растяжение по горизонтали ужимающее
                // (0.73–0.76), и это правильная сторона: кучка становится
                // компактнее, а не расползается по полу.
                //
                // Четыре модели на четыре кучки — единственное место каталога,
                // где разнообразие уместно: кучки не принадлежат командам и
                // стоят по разным углам площадки.
                Kind.Stash,
                new Wear("кучка кирпича",
                    new Entry(Props + "SM_Prop_Brick_Stack_01.prefab", Fit.Stretch),
                    new Entry(Props + "SM_Prop_Brick_Stack_02.prefab", Fit.Stretch),
                    new Entry(Props + "SM_Prop_Brick_Stack_03.prefab", Fit.Stretch),
                    new Entry(Props + "SM_Prop_Brick_Stack_04.prefab", Fit.Stretch))
            },
            {
                // Балка — 0.5 × 0.5 × 4 ШИ вдоль Z. Двутавр пака лежит вдоль
                // своей оси X, поэтому доворот на четверть обязателен: без него
                // ряд насчитал бы одиннадцать копий по 25 см и собрал бы из
                // балки гусеницу.
                //
                // Fit.Row с одной копией, а не Stretch: Stretch растянул бы
                // профиль двутавра в 1.8 и 2.6 раза по толщине, и он перестал
                // бы читаться двутавром. Ряд берёт масштаб от высоты коробки и
                // добирает длину в пределах допуска — профиль остаётся собой.
                //
                // Модель обязана быть ребёнком коробки: SwingingBeamTrap водит
                // сам transform балки, и дресс едет вместе с ним.
                Kind.Beam,
                new Wear("балка", new Entry(Props + "SM_Prop_I_Beam_01.prefab", Fit.Row, 1))
            },
            {
                // Пол перекрытия. Плитами, а не краской, и это ответ на прямой
                // вопрос геймдизайнера: тайлить атлас Synty нельзя (это цветовая
                // карта, а не тайловый материал — так ледяная текстура сделала
                // зимний этаж Duck Hunt бассейном), зато в паке есть настоящие
                // плиты перекрытия. Сетка 5 × 5 м даёт швы, кромки и толщину
                // залитого бетона; коробка блокаута под ними остаётся
                // коллайдером, а её верх совпадает с верхом плиты.
                Kind.Floor,
                new Wear("перекрытие",
                    new Entry(Buildings + "SM_Bld_Concrete_Floor_01.prefab", Fit.Tile))
            },
            {
                // Фасад периметра. Панели дома пака идут ровно 2.50 × 2.90 и
                // отличаются только проёмом — глухая, с окном, с дверью, по
                // четыре варианта каждой. Поэтому их можно мешать на каждую
                // копию: сетка копий считается по первой записи, а габарит у
                // всех один.
                //
                // Fit.Wall, а не Tile: повтор нужен и по длине, и по высоте,
                // а тонкая сторона панели сама доворачивается к тонкой стороне
                // кадра — один каталог годится и на борт вдоль X, и на торец
                // вдоль Z.
                //
                // Пояс ровно в высоту панели (2.90 м), поэтому подтяжки по
                // вертикали нет вовсе: панель стоит в натуральный рост, а не
                // приплюснутая под высоту стены.
                Kind.WallFacade,
                new Wear("фасад периметра", true,
                    new Entry(Buildings + "SM_Bld_House_Wall_01.prefab", Fit.Wall),
                    new Entry(Buildings + "SM_Bld_House_Wall_02.prefab", Fit.Wall),
                    new Entry(Buildings + "SM_Bld_House_Wall_03.prefab", Fit.Wall),
                    new Entry(Buildings + "SM_Bld_House_Wall_04.prefab", Fit.Wall),
                    new Entry(Buildings + "SM_Bld_House_Wall_Window_01.prefab", Fit.Wall),
                    new Entry(Buildings + "SM_Bld_House_Wall_Window_02.prefab", Fit.Wall),
                    new Entry(Buildings + "SM_Bld_House_Wall_Window_03.prefab", Fit.Wall),
                    new Entry(Buildings + "SM_Bld_House_Wall_Window_04.prefab", Fit.Wall),
                    new Entry(Buildings + "SM_Bld_House_Wall_Door_01.prefab", Fit.Wall),
                    new Entry(Buildings + "SM_Bld_House_Wall_Door_02.prefab", Fit.Wall),
                    new Entry(Buildings + "SM_Bld_House_Wall_Window_01.prefab", Fit.Wall),
                    new Entry(Buildings + "SM_Bld_House_Wall_Window_03.prefab", Fit.Wall))
            },
            {
                // Венец: плита с арматурой поверх фасада. Пояс 1.42 м при
                // модели 1.16 — подтяжка 1.22, внутри предела 1.35.
                //
                // Это и есть примета недостроя: этаж залит, арматура под
                // следующий торчит наружу. Кроме того венец закрывает разрыв
                // между верхом панели (2.90 м) и верхом стены (4.32 м), иначе
                // там осталась бы полоса крашеной коробки.
                Kind.WallCrown,
                new Wear("венец периметра",
                    new Entry(Buildings + "SM_Bld_ConcreteRebar_Wall_03.prefab", Fit.Wall))
            },
            {
                // Стенка выемки — то, что видно, когда смотришь в пропасть.
                // Пояс 3 м при модели 2.92: подтяжка 1.03. Ниже трёх метров
                // стенку закрывает нижний ярус и дымка, и одевать её незачем.
                Kind.PitWall,
                new Wear("стенка выемки",
                    new Entry(Buildings + "SM_Bld_ConcreteRebar_Wall_01.prefab", Fit.Wall))
            },
            {
                // Кромка обрыва: срез перекрытия. Без неё пропасть читается
                // аккуратно вырезанным прямоугольником — пол выглядит целым,
                // а дыра в нём случайной. С торчащей арматурой он выглядит
                // оборванным, и падать становится страшно раньше, чем упал.
                Kind.ChasmLip,
                new Wear("кромка обрыва",
                    new Entry(Buildings + "SM_Bld_ConcreteRebar_Wall_03.prefab", Fit.Wall))
            },
            {
                // Кирпич — 0.35 × 0.2 × 0.2 ШИ. Кирпич пака 0.43 × 0.19 × 0.25 м
                // почти того же размера. Три варианта отличаются только числом
                // треугольников (30 / 34 / 12), поэтому взят самый дешёвый:
                // кирпичей в кадре бывает до восьми разом, и разглядывать их
                // никто не будет — они летят.
                Kind.Brick,
                new Wear("кирпич", new Entry(Props + "SM_Prop_Brick_03.prefab", Fit.Stretch))
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

            return DressKit.Apply(box, wear.Entries, rng, null, wear.Mix);
        }

        /// <summary>
        /// Поставить модель пака <b>мимо коробки блокаута</b>, в мировую точку и
        /// в натуральном масштабе.
        ///
        /// Нужно там, где коробка блокаута — тонкий триггер, а предмет обязан
        /// быть виден целиком. Тачка-ловушка стоит на слое 0.36 м высотой (это
        /// объём срабатывания, а не тачка), и посадка в него дала бы тачку по
        /// щиколотку. Прорванная труба вовсе не имеет рендерера: у неё есть
        /// только объём струи.
        ///
        /// <paramref name="targetHeight"/> — во сколько метров привести высоту
        /// модели; ноль оставляет натуральную. Предмет ставится <b>основанием
        /// в точку</b>, а не центром: тачка обязана стоять на полу.
        /// </summary>
        /// <param name="byPivot">
        /// Ставить по пивоту модели, а не по центру её габарита. Нужно крупным
        /// несимметричным предметам: у башенного крана габарит на 45 м вытянут
        /// стрелой, и посадка по центру уводит саму мачту на два десятка метров
        /// от заданной точки — кран уезжает в кадр вместо того, чтобы стоять
        /// за стеной.
        /// </param>
        internal static GameObject Prop(Transform parent, string propName, string prefabPath, Vector3 position,
            float yaw, float targetHeight = 0f, bool castShadows = true, bool byPivot = false)
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

            // Смещение от корня до низа габарита: пивоты моделей пака стоят
            // где угодно, и ставить по корню значит закапывать половину.
            Vector3 anchor = byPivot
                ? new Vector3(go.transform.position.x, bounds.min.y, go.transform.position.z)
                : new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            go.transform.position = position + (go.transform.position - anchor);
            return go;
        }

        /// <summary>
        /// Таблица «вид → модель → габарит»: что во что одето и какой ценой.
        /// Печатается пересборкой, потому что подбор модели обязан быть
        /// проверяемым числом, а не словом «подобрал».
        /// </summary>
        internal static string Report()
        {
            var report = new StringBuilder();
            report.Append("🧱 «Переноска предмета», дресс 4.1 — пак POLYGON Construction");

            foreach (KeyValuePair<Kind, Wear> pair in Catalog)
            {
                Wear wear = pair.Value;
                report.Append("\n— ").Append(wear.Title.PadRight(14));

                for (int i = 0; i < wear.Entries.Length; i++)
                {
                    string path = wear.Entries[i].Prefab;
                    string shortName = path.Substring(path.LastIndexOf('/') + 1).Replace(".prefab", string.Empty);
                    report.Append(i == 0 ? string.Empty : ", ").Append(shortName);

                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                    {
                        report.Append(" (нет в проекте)");
                        continue;
                    }

                    Bounds bounds = DressKit.GetBounds(prefab, path);
                    report.Append(" [")
                        .Append(bounds.size.x.ToString("F2")).Append('×')
                        .Append(bounds.size.y.ToString("F2")).Append('×')
                        .Append(bounds.size.z.ToString("F2")).Append(" м, ")
                        .Append(wear.Entries[i].Fit).Append(']');
                }
            }

            return report.ToString();
        }

        /// <summary>
        /// Пометить предмет декорацией: снять коллайдеры и увести на
        /// <c>Default</c>.
        ///
        /// Коллайдеры срезаются всегда и без исключений: столкновения на арене
        /// держит блокаут, а мешевый коллайдер модели дал бы вторую поверхность
        /// другой формы поверх выверенной. В этой игре цена выше обычного —
        /// лишний коллайдер ловил бы броски бутыли, кирпичи и струю трубы.
        /// </summary>
        private static void MarkAsScenery(GameObject go, bool castShadows)
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
