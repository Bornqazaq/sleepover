using System.Collections.Generic;
using System.Text;
using Igruha.Minigames.HoleInWall;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Палитра «Переноски предмета» как ассеты — подфаза 4.2.
    ///
    /// <b>Почему замер, а не пипетка.</b> Паки Synty текстурированы одним
    /// атласом: у модели нет «цвета», есть кусок общей картинки. Пипеткой
    /// снимается случайный пиксель, и на полосатом куске ответ будет либо
    /// чёрный, либо белый — оба неверны. Цвет считается усреднением по UV
    /// (<see cref="SyntyPalette"/>), и тогда бетон блокаута садится ровно в тон
    /// бетона пака, а стык коробки с моделью перестаёт читаться.
    ///
    /// <b>Почему ассеты, а не материалы в памяти.</b> Материал, созданный на
    /// лету, уезжает внутрь `.unity` копией на каждый объект: его нельзя ни
    /// переиспользовать, ни запечь на 4.6, ни увидеть в Project, а слияние
    /// веток разводит копии молча. Ассет с GUID переживает и то, и другое.
    ///
    /// <b>Имена сохранены прежними.</b> Материалы `CI_*` завёл разбор
    /// читаемости 01.09, и на них уже ссылаются префабы и сцена. Палитра их не
    /// переименовывает, а переписывает: цвет приходит из замера, а ссылки
    /// остаются целыми.
    ///
    /// ⚠️ Настройка URP-материала взята из <see cref="HoleInWallMaterials"/> —
    /// он `public static` и чистый, а дублировать возню с режимами поверхности
    /// и ключевыми словами значит завести второй набор тех же ошибок. Имя у
    /// него от чужой игры, и его стоит переименовать в общий кит, когда
    /// «Дырка в стене» освободится: отдельной задачей, не походя.
    /// </summary>
    internal static class CarryItemPalette
    {
        /// <summary>Поверхность палитры. Одна на роль, а не на объект.</summary>
        internal enum Tone
        {
            /// <summary>Бетон перекрытия, по которому бегут.</summary>
            Concrete,

            /// <summary>Бетон дна пропасти. Темнее пола — иначе край проёма не читается.</summary>
            ConcreteDeep,

            /// <summary>Бетон стен и торцов.</summary>
            Wall,

            /// <summary>Кромка проёма: жёлто-чёрная разметка по краю пропасти.</summary>
            EdgeStripe,

            /// <summary>Ржавая сталь: балка крана.</summary>
            Steel,

            /// <summary>Светлое дерево: доски над пропастями.</summary>
            Wood,

            /// <summary>Оцинковка: корпус бака, стойки полотнища.</summary>
            Metal,

            /// <summary>Опасность. Красно-белая зебра, а не оранжевый: оранжевый занят командой B.</summary>
            Hazard,

            /// <summary>Вода: в бутыли, в баке, в мерном стекле.</summary>
            Water,

            /// <summary>Стекло бутыли. Сквозь него виден уровень — это счёт.</summary>
            Glass,

            /// <summary>Мерное стекло бака: прозрачнее бутыли, за ним столбик.</summary>
            Gauge,

            /// <summary>Белая основа под цвет команды: крышка, тент, обод. Красится в рантайме.</summary>
            TeamBase,

            /// <summary>Белая основа держалок: свободна или занята — решает рантайм.</summary>
            GripBase,

            /// <summary>Готовность штабеля. Осталась от блокаута, сейчас не используется.</summary>
            Ready,

            /// <summary>Брызги струи и лужа под ней.</summary>
            Spray,

            /// <summary>Струя из горлышка наклонённой бутыли.</summary>
            Pour
        }

        private const string Folder = "Assets/_Project/Materials/Minigames/CarryItem";
        private const string LitShaderName = "Universal Render Pipeline/Lit";

        /// <summary>Где какой тон живёт на диске. Имена заведены разбором 01.09 и не меняются.</summary>
        private static readonly Dictionary<Tone, string> Files = new Dictionary<Tone, string>
        {
            { Tone.Concrete, "CI_Concrete" },
            { Tone.ConcreteDeep, "CI_ConcreteDeep" },
            { Tone.Wall, "CI_WallConcrete" },
            { Tone.EdgeStripe, "CI_EdgeStripe" },
            { Tone.Steel, "CI_Hazard" },
            { Tone.Wood, "CI_Plank" },
            { Tone.Metal, "CI_TankRim" },
            { Tone.Hazard, "CI_HazardStripe" },
            { Tone.Water, "CI_BottleWater" },
            { Tone.Glass, "CI_BottleShell" },
            { Tone.Gauge, "CI_TankGlass" },
            { Tone.TeamBase, "CI_BottleCap" },
            { Tone.GripBase, "CI_HandleGrip" },
            { Tone.Ready, "CI_StackReady" },
            { Tone.Spray, "CI_PushZone" },
            { Tone.Pour, "CI_PourWater" }
        };

        /// <summary>
        /// С какого предмета пака снимается цвет тона. Пусто — цвет назван
        /// брифом прямо, и мерить нечего: вода, стекло и белая основа под цвет
        /// команды к паку отношения не имеют.
        /// </summary>
        private static readonly Dictionary<Tone, string> Sources = new Dictionary<Tone, string>
        {
            { Tone.Concrete, "Assets/Synty/PolygonConstruction/Prefabs/Buildings/SM_Bld_Concrete_Floor_01.prefab" },
            { Tone.Wall, "Assets/Synty/PolygonConstruction/Prefabs/Buildings/SM_Bld_Concrete_Wall_02.prefab" },
            { Tone.Steel, "Assets/Synty/PolygonConstruction/Prefabs/Props/SM_Prop_I_Beam_01.prefab" },
            { Tone.Wood, "Assets/Synty/PolygonConstruction/Prefabs/Props/SM_Prop_Plank_Long_Stack_02.prefab" },
            // Металл снимается с лесов, а не с бака-кубоконтейнера: у того
            // корпус в решётке и грязных подтёках, и усреднение даёт бурый —
            // бак получался нефтяной бочкой. Труба лесов оцинкована, и это
            // ровно тот металл, который просит бриф.
            { Tone.Metal, "Assets/Synty/PolygonConstruction/Prefabs/Props/SM_Prop_Scaffold_01.prefab" }
        };

        /// <summary>
        /// Значения на случай, когда паков на машине нет. Это не «примерно
        /// такие»: они сняты замером 04.09 и записаны сюда, чтобы цвет в `.mat`
        /// не менялся от того, у кого открыт проект.
        /// </summary>
        private static readonly Dictionary<Tone, Color> Fallback = new Dictionary<Tone, Color>
        {
            { Tone.Concrete, new Color(0.725f, 0.706f, 0.674f) },
            { Tone.Steel, new Color(0.557f, 0.231f, 0.200f) },
            { Tone.Wood, new Color(0.902f, 0.804f, 0.557f) },
            { Tone.Metal, new Color(0.635f, 0.659f, 0.690f) },
            { Tone.Wall, new Color(0.694f, 0.678f, 0.651f) }
        };

        /// <summary>Во сколько раз дно пропасти темнее пола. Ниже — дно сливается в чёрную дыру.</summary>
        private const float DeepFactor = 0.62f;

        /// <summary>Во сколько раз стена темнее пола: граница площадки обязана читаться.</summary>
        private const float WallFactor = 0.74f;

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
                    // Стена и пол у пака один и тот же бетон, и замер даёт им
                    // один цвет — на кадре они сливаются в сплошное пятно, и
                    // граница площадки пропадает. Стена приглушается: разница
                    // яркости отделяет её, не выдумывая нового материала.
                    measured[pair.Key] = pair.Key == Tone.Wall ? Dim(average, WallFactor) : average;
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

        /// <summary>Цвет тона: замеренный, если пак на месте, иначе сохранённый.</summary>
        internal static Color ColorOf(Tone tone)
        {
            if (measured.TryGetValue(tone, out Color average))
            {
                return average;
            }

            if (Fallback.TryGetValue(tone, out Color saved))
            {
                return saved;
            }

            switch (tone)
            {
                // Дно пропасти — тот же бетон, приглушённый. Требование LDD:
                // край проёма обязан контрастировать с полом, а самый дешёвый
                // и самый надёжный контраст — разница яркости, а не узор.
                case Tone.ConcreteDeep:
                    return Dim(ColorOf(Tone.Concrete), DeepFactor);

                // Кромка проёма. Жёлтая, а не красная и не оранжевая: красное
                // отдано ловушкам, оранжевое — команде B, а жёлтое в этой игре
                // не значит ничего другого.
                case Tone.EdgeStripe:
                    return new Color(0.91f, 0.65f, 0.16f);

                case Tone.Hazard:
                    return new Color(0.85f, 0.23f, 0.20f);

                case Tone.Water:
                    return new Color(0.48f, 0.73f, 0.98f);

                case Tone.Glass:
                    return new Color(0.80f, 0.92f, 0.98f, 0.18f);

                case Tone.Gauge:
                    return new Color(0.85f, 0.94f, 0.98f, 0.12f);

                case Tone.Spray:
                    return new Color(0.62f, 0.93f, 0.98f, 0.30f);

                case Tone.Pour:
                    return new Color(0.62f, 0.84f, 1f, 0.85f);

                case Tone.Ready:
                    return new Color(0.62f, 0.98f, 0.66f);

                // Белая основа под цвет команды. Именно белая: цвет приезжает
                // в рантайме через MaterialPropertyBlock, и любая своя окраска
                // под ним смешалась бы с командной.
                default:
                    return Color.white;
            }
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
                    Debug.LogError($"Шейдер '{LitShaderName}' не найден — палитру «Переноски» не собрать");
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
            report.Append("🎨 «Переноска предмета», палитра 4.2 — замер по UV моделей пака");

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
                    report.Append("  (пак не найден, сохранённое значение)");
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
                case Tone.Glass:
                case Tone.Gauge:
                case Tone.Spray:
                case Tone.Pour:
                    HoleInWallMaterials.ConfigureTransparent(material, color, 0.85f);
                    break;

                case Tone.Metal:
                case Tone.Steel:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.42f, 0.55f);
                    break;

                case Tone.Water:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.72f, 0f);
                    break;

                // Бетон нарочно матовый: блик на полу во весь кадр спорит с
                // бутылью, а она здесь единственное, что обязано блестеть.
                case Tone.Concrete:
                case Tone.ConcreteDeep:
                case Tone.Wall:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.06f, 0f);
                    break;

                default:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.2f, 0f);
                    break;
            }
        }

        /// <summary>
        /// Приглушить тон. Умножение идёт в линейном пространстве: «в полтора
        /// раза темнее» — это про свет, а не про коды цветов.
        /// </summary>
        private static Color Dim(Color srgb, float factor)
        {
            Color linear = srgb.linear;
            return new Color(linear.r * factor, linear.g * factor, linear.b * factor, 1f).gamma;
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

            AssetDatabase.CreateFolder("Assets/_Project/Materials/Minigames", "CarryItem");
        }
    }
}
