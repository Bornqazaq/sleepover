using System.Collections.Generic;
using System.Text;
using Igruha.Minigames.HoleInWall;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Палитра циркового шатра — общая на «Секундомер» и «Порядок банок».
    ///
    /// <b>Почему общая.</b> Шатёр физически лежит в двух сценах,
    /// <c>Stopwatch.unity</c> и <c>CansOrder.unity</c>, и строится одним
    /// <see cref="CircusArenaBuilder"/>. Заведи палитру внутри дресса одной
    /// игры — и второй шатёр придётся красить заново, после чего два шатра
    /// разъедутся по тону. А это одно и то же место: игрок обязан его узнать.
    ///
    /// <b>Почему замер, а не пипетка.</b> Паки Synty текстурированы одним
    /// атласом: у модели нет «цвета», есть кусок общей картинки. Пипеткой
    /// снимается случайный пиксель. Цвет считается усреднением по UV
    /// (<see cref="SyntyPalette"/>) — тогда кирпич блокаута садится в тон
    /// кирпичу пака и стык коробки с моделью перестаёт читаться.
    ///
    /// <b>Полосы купола замером не берутся.</b> Их назвал бриф (раздел 14.2
    /// спеки, вариант A, утверждён 04.09), и мерить нечего: полосатой ткани
    /// в паках нет вовсе, а усреднение по красно-жёлтому шатру
    /// <c>SM_Prop_Tent_Large_01</c> дало бы мутный оранжевый — цвет, которого
    /// на арене не будет ни на одной поверхности.
    ///
    /// <b>Почему ассеты, а не материалы в памяти.</b> Материал, созданный на
    /// лету, уезжает внутрь `.unity` копией на каждый объект: его нельзя ни
    /// переиспользовать, ни запечь на 4.6, а слияние веток разводит копии
    /// молча. Ассет с GUID переживает и то, и другое — и переживает дважды,
    /// потому что на него ссылаются две сцены.
    /// </summary>
    internal static class CircusPalette
    {
        /// <summary>Поверхность палитры. Одна на роль, а не на объект.</summary>
        internal enum Tone
        {
            /// <summary>Тёмная полоса ткани купола и стены шатра.</summary>
            CanvasStripe,

            /// <summary>Светлая полоса ткани. На ней тёмная клетка и читается силуэтом.</summary>
            CanvasCream,

            /// <summary>Кирпич борта ямы.</summary>
            Brick,

            /// <summary>Поясок поверх борта: тот же кирпич светлее — иначе верх борта не читается.</summary>
            BrickCope,

            /// <summary>Опилки на дне ямы.</summary>
            Sawdust,

            /// <summary>Дерево: настил шатра, пол и крыша клетки, трибуны.</summary>
            Deck,

            /// <summary>Ржавый металл: прутья клетки, ферма, цепи.</summary>
            CageMetal,

            /// <summary>Панель табло. Почти чёрная: на ней светится текст.</summary>
            BoardPanel,

            /// <summary>Рама табло с лампами.</summary>
            BoardFrame,

            /// <summary>
            /// Купол кнопки. <b>Белый, и это не описка.</b> Цвет купола ведёт
            /// <see cref="Igruha.Minigames.Stopwatch.CageButton"/> через
            /// <c>MaterialPropertyBlock</c> — покой, свой отсчёт, чужой отсчёт.
            /// Любая своя окраска под ним перемножилась бы с назначенной,
            /// и «горит» перестало бы отличаться от «не горит».
            /// </summary>
            ButtonBase,

            /// <summary>Шерсть медведя — на случай, если модели в проекте нет.</summary>
            BearFur
        }

        private const string Folder = "Assets/_Project/Materials/Minigames/Circus";
        private const string LitShaderName = "Universal Render Pipeline/Lit";

        private static readonly Dictionary<Tone, string> Files = new Dictionary<Tone, string>
        {
            { Tone.CanvasStripe, "CR_CanvasStripe" },
            { Tone.CanvasCream, "CR_CanvasCream" },
            { Tone.Brick, "CR_Brick" },
            { Tone.BrickCope, "CR_BrickCope" },
            { Tone.Sawdust, "CR_Sawdust" },
            { Tone.Deck, "CR_Deck" },
            { Tone.CageMetal, "CR_CageMetal" },
            { Tone.BoardPanel, "CR_BoardPanel" },
            { Tone.BoardFrame, "CR_BoardFrame" },
            { Tone.ButtonBase, "CR_ButtonBase" },
            { Tone.BearFur, "CR_BearFur" }
        };

        /// <summary>
        /// С какого предмета пака снимается цвет тона. Пусто — цвет назван
        /// брифом прямо: полосы купола, панель табло и белая основа кнопки
        /// к паку отношения не имеют.
        /// </summary>
        private static readonly Dictionary<Tone, string> Sources = new Dictionary<Tone, string>
        {
            { Tone.Brick, "Assets/Synty/PolygonConstruction/Prefabs/Props/SM_Prop_Brick_01.prefab" },
            { Tone.Sawdust, "Assets/Synty/PolygonHorrorCarnival/Prefabs/Environment/SM_Env_Ground_Dirt_Round_01.prefab" },
            // Дерево снимается с трибун, а не с настила: настила в паке нет
            // вовсе, а трибуны — то самое дерево, рядом с которым этот настил
            // и будет лежать.
            { Tone.Deck, "Assets/Synty/PolygonHorrorCarnival/Prefabs/Props/SM_Prop_Bleachers_Straight_01.prefab" },
            // Металл — с перил, а не с клетки-фургона: у фургона корпус
            // раскрашен в цирковые цвета, и усреднение даёт розовый.
            { Tone.CageMetal, "Assets/Synty/PolygonHorrorCarnival/Prefabs/Building/SM_Bld_Rail_01.prefab" },
            { Tone.BearFur, "Assets/Synty/PolygonShops/Prefabs/Props/SM_Prop_Bear_Statue_01.prefab" }
        };

        /// <summary>
        /// <b>Сырые</b> средние пака. Пока пусто: замер снимается на подфазе
        /// 4.2 и закрепляется здесь числами.
        ///
        /// Закреплять обязательно, и причина не в отсутствии паков на чужой
        /// машине. У импортёра Synty выключен Read/Write: Unity держит вершины
        /// только до первой выгрузки, и второй прогон замера в той же сессии
        /// уже ничего не мерит (разбор в STATE 3.70). Без сохранённых чисел
        /// цвет в `.mat` менялся бы от того, в какой момент нажали пересборку.
        /// </summary>
        private static readonly Dictionary<Tone, Color> Fallback = new Dictionary<Tone, Color>();

        /// <summary>Насколько поясок светлее кирпича борта: верх борта обязан читаться отдельной линией.</summary>
        private const float CopeFactor = 1.28f;

        private static readonly Dictionary<Tone, Material> cache = new Dictionary<Tone, Material>(16);
        private static readonly Dictionary<Tone, Color> measured = new Dictionary<Tone, Color>(8);
        private static readonly List<string> unmeasured = new List<string>(8);

        /// <summary>Замерить пак и завести на диске всю палитру целиком.</summary>
        internal static void Begin()
        {
            cache.Clear();
            measured.Clear();
            unmeasured.Clear();
            SyntyPalette.ClearCache();

            foreach (KeyValuePair<Tone, string> pair in Sources)
            {
                if (SyntyPalette.TryAverage(pair.Value, out Color average))
                {
                    measured[pair.Key] = average;
                }
                else
                {
                    unmeasured.Add(pair.Key.ToString());
                }
            }

            foreach (Tone tone in System.Enum.GetValues(typeof(Tone)))
            {
                Get(tone);
            }
        }

        /// <summary>Дописать заведённые материалы на диск. Вызывать в конце пересборки.</summary>
        internal static void Flush()
        {
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Цвет тона: замеренный, если пак на месте, иначе названный брифом.
        ///
        /// Числа вариантов — раздел 14.2 спеки «Секундомера». Утверждён
        /// вариант A («тёплый балаган») — решение геймдизайнера 04.09.
        /// </summary>
        internal static Color ColorOf(Tone tone)
        {
            if (TryRaw(tone, out Color raw))
            {
                return raw;
            }

            switch (tone)
            {
                // Полосы купола. Тёмно-красная и кремовая, а не синяя и охристая,
                // как просил пункт 3.9 спеки: высота клетки в этой игре —
                // единственный интерфейс, и на кремовой полосе тёмная клетка
                // читается силуэтом с любой точки кольца. Разбор — пункт 15
                // раздела 13 спеки.
                case Tone.CanvasStripe:
                    return new Color(0.620f, 0.200f, 0.190f);

                case Tone.CanvasCream:
                    return new Color(0.850f, 0.760f, 0.600f);

                case Tone.Brick:
                    return new Color(0.550f, 0.280f, 0.220f);

                case Tone.BrickCope:
                    return Dim(ColorOf(Tone.Brick), CopeFactor);

                case Tone.Sawdust:
                    return new Color(0.800f, 0.680f, 0.440f);

                case Tone.Deck:
                    return new Color(0.420f, 0.290f, 0.200f);

                case Tone.CageMetal:
                    return new Color(0.300f, 0.240f, 0.220f);

                // Панель табло почти чёрная: по ней идёт светлый текст, и любой
                // подъём яркости панели съедает контраст строк. Читаемость
                // результатов важнее фактуры — их видно 4 секунды на подраунд.
                case Tone.BoardPanel:
                    return new Color(0.080f, 0.070f, 0.075f);

                // Рама табло латунная и намеренно светлая. Тёмная рама вокруг
                // тёмной панели давала силуэт глухой плиты — ровно то, из-за
                // чего табло закрывало две клетки из восьми и разведка 4.0
                // подняла тревогу. Светлый контур читается рамкой, и глаз
                // перестаёт принимать табло за стену.
                case Tone.BoardFrame:
                    return new Color(0.780f, 0.590f, 0.240f);

                case Tone.BearFur:
                    return new Color(0.400f, 0.270f, 0.190f);

                // Белая основа под цвет, который назначает CageButton.
                default:
                    return Color.white;
            }
        }

        /// <summary>Поясок борта считается от кирпича, а не мерится отдельно.</summary>
        private static bool TryRaw(Tone tone, out Color raw)
        {
            if (tone == Tone.BrickCope)
            {
                raw = default;
                return false;
            }

            return measured.TryGetValue(tone, out raw) || Fallback.TryGetValue(tone, out raw);
        }

        /// <summary>Материал тона. Ассет заводится при первом обращении, свойства переписываются всегда.</summary>
        internal static Material Get(Tone tone)
        {
            if (cache.TryGetValue(tone, out Material cached) && cached != null)
            {
                return cached;
            }

            string path = $"{Folder}/{Files[tone]}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find(LitShaderName);
                if (shader == null)
                {
                    Debug.LogError($"Шейдер '{LitShaderName}' не найден — палитру цирка не собрать");
                    return null;
                }

                EnsureFolder();
                material = new Material(shader) { name = Files[tone] };
                AssetDatabase.CreateAsset(material, path);
            }

            Apply(material, tone);
            EditorUtility.SetDirty(material);
            cache[tone] = material;
            return material;
        }

        /// <summary>
        /// Таблица замеров: с какого предмета пака снят каждый цвет и во что он
        /// превратился. Печатается пересборкой — цвет обязан быть проверяемым
        /// числом, а не словом «подобрал».
        /// </summary>
        internal static string Report()
        {
            var report = new StringBuilder();
            report.Append("🎨 Цирк, палитра — общая на «Секундомер» и «Порядок банок»");

            foreach (Tone tone in System.Enum.GetValues(typeof(Tone)))
            {
                Color color = ColorOf(tone);
                report.Append("\n— ").Append(tone.ToString().PadRight(13))
                    .Append(SyntyPalette.Hex(color))
                    .Append(" → ").Append(Files[tone]);

                if (measured.ContainsKey(tone))
                {
                    string source = Sources[tone];
                    report.Append("  (замер: ")
                        .Append(source.Substring(source.LastIndexOf('/') + 1).Replace(".prefab", string.Empty))
                        .Append(')');
                }
                else if (Sources.ContainsKey(tone))
                {
                    report.Append("  (замер недоступен, значение брифа)");
                }
                else
                {
                    report.Append("  (бриф 14.2)");
                }
            }

            if (unmeasured.Count > 0)
            {
                report.Append("\n⚠️ не замерено: ").Append(string.Join(", ", unmeasured));
            }

            return report.ToString();
        }

        private static void Apply(Material material, Tone tone)
        {
            Color color = ColorOf(tone);

            switch (tone)
            {
                // Металл клетки и фермы блестит, но глухо: клетка обязана
                // читаться силуэтом, а не бликом, и висит она под софитами.
                case Tone.CageMetal:
                case Tone.BoardFrame:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.38f, 0.55f);
                    break;

                // Ткань купола матовая наглухо: блик на полотнище во весь кадр
                // спорил бы с прожекторами, а светить в этой игре положено им.
                case Tone.CanvasStripe:
                case Tone.CanvasCream:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.02f, 0f);
                    break;

                case Tone.Sawdust:
                case Tone.Brick:
                case Tone.BrickCope:
                case Tone.Deck:
                case Tone.BearFur:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.05f, 0f);
                    break;

                case Tone.BoardPanel:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.12f, 0f);
                    break;

                default:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.2f, 0f);
                    break;
            }
        }

        /// <summary>
        /// Приглушить или высветлить тон. Умножение идёт в линейном
        /// пространстве: «в полтора раза светлее» — это про свет, а не про
        /// коды цветов.
        /// </summary>
        private static Color Dim(Color srgb, float factor)
        {
            Color linear = srgb.linear;
            return new Color(
                Mathf.Clamp01(linear.r * factor),
                Mathf.Clamp01(linear.g * factor),
                Mathf.Clamp01(linear.b * factor), 1f).gamma;
        }

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(Folder))
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Materials/Minigames"))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Materials", "Minigames");
            }

            AssetDatabase.CreateFolder("Assets/_Project/Materials/Minigames", "Circus");
        }
    }
}
