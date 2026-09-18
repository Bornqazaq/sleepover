using System.Collections.Generic;
using System.Text;
using Igruha.Minigames.HoleInWall;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Local CarryItem materials, authored colours; gameplay transparency retained.</summary>
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

            /// <summary>Кромка проёма: красно-белая разметка по краю пропасти.</summary>
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
            Pour,

            /// <summary>Латка на бетоне: выбоина, залитый заново кусок, вытертое место.</summary>
            Patch,

            /// <summary>Трещина в перекрытии.</summary>
            Crack,

            /// <summary>Лужа: мокрый бетон, единственное блестящее место на полу.</summary>
            Puddle
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
            { Tone.Pour, "CI_PourWater" },
            { Tone.Patch, "CI_Patch" },
            { Tone.Crack, "CI_Crack" },
            { Tone.Puddle, "CI_Puddle" }
        };

        /// <summary>
        /// С какого предмета пака снимается цвет тона. Пусто — цвет назван
        /// брифом прямо, и мерить нечего: вода, стекло и белая основа под цвет
        /// команды к паку отношения не имеют.
        /// </summary>
        private static readonly Dictionary<Tone, Color> Fallback = new Dictionary<Tone, Color>
        {
            { Tone.Concrete, new Color(0.66f, 0.675f, 0.66f) },
            { Tone.Wall, new Color(0.66f, 0.675f, 0.66f) },
            { Tone.Steel, new Color(0.388f, 0.220f, 0.192f) },
            { Tone.Wood, new Color(0.69f, 0.48f, 0.26f) },
            { Tone.Metal, new Color(0.43f, 0.50f, 0.53f) }
        };

        /// <summary>Во сколько раз дно пропасти темнее пола. Ниже — дно сливается в чёрную дыру.</summary>
        private const float DeepFactor = 0.62f;

        /// <summary>Во сколько раз стена темнее пола: граница площадки обязана читаться.</summary>
        private const float WallFactor = 0.74f;

        /// <summary>Латка на бетоне: заметна, но не дыра.</summary>
        private const float PatchFactor = 0.88f;

        /// <summary>Трещина: она и должна быть тёмной полосой.</summary>
        private const float CrackFactor = 0.62f;

        /// <summary>Лужа: мокрый бетон темнее сухого примерно вдвое.</summary>
        private const float PuddleFactor = 0.52f;

        /// <summary>
        /// Сколько цветности остаётся у бетона. Решение геймдизайнера 04.09:
        /// замер с пака даёт тёплый `#867C70`, и площадка читается земляной,
        /// а не бетонной. Яркость берётся из замера, цветность снимается почти
        /// до нуля — это ровно то, чем серый бетон отличается от глины.
        /// </summary>
        private const float ConcreteChroma = 0.18f;

        private static readonly Dictionary<Tone, Material> cache = new Dictionary<Tone, Material>(16);
        private static readonly Dictionary<Tone, Color> measured = new Dictionary<Tone, Color>(8);
        private static readonly List<string> unmeasured = new List<string>(8);

        /// <summary>Замерить пак и завести на диске всю палитру целиком.</summary>
        internal static void Begin()
        {
            cache.Clear();
            measured.Clear();
            unmeasured.Clear();
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
            if (TryRaw(tone, out Color raw))
            {
                switch (tone)
                {
                    // Бетон обесцвечивается почти до серого. Яркость берётся из
                    // замера, цветность снимается — решение геймдизайнера 04.09:
                    // тёплый замер пака делал площадку земляной, а не бетонной.
                    case Tone.Concrete:
                        return Desaturate(raw, ConcreteChroma);

                    // Стена и пол у пака один и тот же бетон, и замер даёт им
                    // один цвет — на кадре они сливались в сплошное пятно, и
                    // граница площадки пропадала. Разница яркости отделяет
                    // стену, не выдумывая нового материала.
                    case Tone.Wall:
                        return Desaturate(Dim(raw, WallFactor), ConcreteChroma);

                    default:
                        return raw;
                }
            }

            switch (tone)
            {
                // Дно пропасти — тот же бетон, приглушённый. Требование LDD:
                // край проёма обязан контрастировать с полом, а самый дешёвый
                // и самый надёжный контраст — разница яркости, а не узор.
                case Tone.ConcreteDeep:
                    return Dim(ColorOf(Tone.Concrete), DeepFactor);

                // Красный сегмент зебры; оранжевый остаётся цветом команды B.
                case Tone.EdgeStripe:
                    return new Color(0.84f, 0.12f, 0.10f);

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

                // Латка и трещина — тот же бетон, приглушённый. Свой цвет им
                // не нужен: на настоящем перекрытии выбоина не другого
                // материала, она просто темнее и грязнее.
                case Tone.Patch:
                    return Dim(ColorOf(Tone.Concrete), PatchFactor);

                case Tone.Crack:
                    return Dim(ColorOf(Tone.Concrete), CrackFactor);

                case Tone.Puddle:
                    return Dim(ColorOf(Tone.Concrete), PuddleFactor);

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

        /// <summary>Палитра собственных материалов этой игры.</summary>
        internal static string Report() => "CarryItem: original concrete, timber and red/white hazard palette";

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
                case Tone.Patch:
                case Tone.Crack:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.06f, 0f);
                    break;

                // Лужа — единственное блестящее место на полу, и этим она и
                // читается лужей, а не тёмным пятном краски.
                case Tone.Puddle:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.88f, 0f);
                    break;

                default:
                    HoleInWallMaterials.ConfigureOpaque(material, color, 0.2f, 0f);
                    break;
            }
        }

        /// <summary>Сырое среднее пака: замеренное в этом прогоне либо сохранённое.</summary>
        private static bool TryRaw(Tone tone, out Color raw)
        {
            return measured.TryGetValue(tone, out raw) || Fallback.TryGetValue(tone, out raw);
        }

        /// <summary>
        /// Снять цветность, оставив яркость. Доля <paramref name="keep"/> —
        /// сколько цветности остаётся: ноль даёт чистый серый.
        ///
        /// Считается в линейном пространстве по той же причине, что и
        /// приглушение: яркость — это про свет, а не про коды цветов.
        /// </summary>
        private static Color Desaturate(Color srgb, float keep)
        {
            Color linear = srgb.linear;
            float grey = linear.r * 0.2126f + linear.g * 0.7152f + linear.b * 0.0722f;
            return new Color(
                Mathf.Lerp(grey, linear.r, keep),
                Mathf.Lerp(grey, linear.g, keep),
                Mathf.Lerp(grey, linear.b, keep), 1f).gamma;
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
