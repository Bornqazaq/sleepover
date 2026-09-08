using System.Collections.Generic;
using Igruha.Minigames.HoleInWall;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Материалы палитры «Дырки в стене» как ассеты — подфаза 4.2.
    ///
    /// Цвета и параметры поверхностей берутся из <see cref="HoleInWallPalette"/>,
    /// настройка URP-материала — из <see cref="HoleInWallMaterials"/>. Здесь
    /// только одно: превратить их в <c>.mat</c>-файлы в
    /// <c>Assets/_Project/Materials/HoleInWall/</c> и раздать по коробкам арены.
    ///
    /// <b>Почему ассеты, а не материалы в памяти.</b> Блокаут раздавал
    /// материалы, созданные на лету: они уезжали внутрь <c>.unity</c> и жили
    /// там по копии на каждый цвет. Такой материал нельзя ни переиспользовать,
    /// ни запечь на 4.6, ни увидеть в Project — а слияние веток разводит копии
    /// молча. Ассет с GUID переживает и то, и другое.
    ///
    /// <b>Свойства переписываются при каждой пересборке.</b> Правка цвета руками
    /// в инспекторе не сохранится, и это осознанно: арт здесь — код (правило
    /// фазы 4), источник значений — бриф, а не последний, кто открыл материал.
    /// </summary>
    internal static class HoleInWallPaletteAssets
    {
        /// <summary>Поверхность палитры. Одна на роль, а не на объект.</summary>
        internal enum Tone
        {
            /// <summary>Тёмный тон студии: дно бассейна, тумбы под настилом, фермы.</summary>
            Stage,

            /// <summary>Бортик бассейна: тот же тёмный, поднятый на замеренную паком разницу.</summary>
            PoolRim,

            /// <summary>Белый глянцевый пластик: настил и плита стены.</summary>
            Plastic,

            /// <summary>Своя половина пола платформы.</summary>
            FloorOwn,

            /// <summary>Половина пола партнёра.</summary>
            FloorPartner,

            /// <summary>Сталь лесенки.</summary>
            Metal,

            /// <summary>Розовый неон: моргание контура после подвоха, баннер одиночки.</summary>
            NeonPink,

            /// <summary>Голубой неон: контур выреза.</summary>
            NeonCyan,

            /// <summary>Вода бассейна.</summary>
            Water,

            /// <summary>Светодиодные панели по бокам арены — подфаза 4.3.</summary>
            Led,

            /// <summary>Светофильтр софита и рамка табло нулевой дорожки — подфаза 4.3.</summary>
            Lane0,

            /// <summary>То же для первой дорожки.</summary>
            Lane1,

            /// <summary>То же для второй дорожки.</summary>
            Lane2,

            /// <summary>То же для третьей дорожки.</summary>
            Lane3
        }

        /// <summary>
        /// Тон дорожки по её номеру. Номер заворачивается по кругу вместе
        /// с <see cref="HoleInWallPalette.LaneAccent"/>: сколько дорожек
        /// строит арена — дело конфига, а не палитры.
        /// </summary>
        internal static Tone LaneTone(int track)
        {
            int count = HoleInWallPalette.LaneAccents.Length;
            return Tone.Lane0 + ((track % count) + count) % count;
        }

        private const string MaterialsRoot = "Assets/_Project/Materials";
        private const string Folder = MaterialsRoot + "/HoleInWall";
        private const string Prefix = "HIW_";
        private const string LitShaderName = "Universal Render Pipeline/Lit";

        /// <summary>
        /// Во сколько раз бортик бассейна светлее тумбы у самого пака —
        /// усреднение по UV, прогон 02.09: тумба Y=0.087, бортик Y=0.193.
        ///
        /// Число подставляется, когда паков на машине нет: без них замерять
        /// нечего, а цвет бортика в <c>.mat</c> должен остаться тем же, иначе
        /// он менялся бы от того, у кого открыт проект.
        /// </summary>
        private const float FallbackRimLift = 2.21f;

        /// <summary>
        /// Пределы подъёма. Единица — бортик сливается с дном, и бассейн
        /// становится одной чёрной ямой; выше четырёх — перестаёт быть тёмным
        /// тоном брифа и спорит с белым пластиком платформы.
        /// </summary>
        private const float MinRimLift = 1f;
        private const float MaxRimLift = 4f;

        private static readonly Dictionary<Tone, Material> cache = new Dictionary<Tone, Material>(14);

        private static float rimLift = FallbackRimLift;

        /// <summary>Замер, из которого получен подъём бортика. Пусто — мерить было нечем.</summary>
        internal static string RimLiftSource { get; private set; } = "паков нет, взято сохранённое значение";

        /// <summary>
        /// Начать пересборку. <paramref name="measuredLift"/> — отношение
        /// яркостей «бортик / тумба», замеренное по UV моделей пака;
        /// неположительное значение означает, что мерить было нечем.
        /// </summary>
        internal static void Begin(float measuredLift, string source)
        {
            cache.Clear();

            if (measuredLift > 0f)
            {
                rimLift = Mathf.Clamp(measuredLift, MinRimLift, MaxRimLift);
                RimLiftSource = source;
                EnsureAll();
                return;
            }

            rimLift = FallbackRimLift;
            RimLiftSource = "паков нет, взято сохранённое значение";
            EnsureAll();
        }

        /// <summary>
        /// Завести на диске все тоны палитры, а не только те, что попросила
        /// текущая пересборка. Палитра — предъявляемый результат подфазы 4.2:
        /// геймдизайнер открывает папку и видит набор целиком. Половина набора
        /// вместо набора читается как недоделанная работа, а следующие подфазы
        /// (свет, VFX) ждут материалы уже заведёнными.
        /// </summary>
        private static void EnsureAll()
        {
            foreach (Tone tone in System.Enum.GetValues(typeof(Tone)))
            {
                Get(tone);
            }
        }

        /// <summary>Цвет тона — в том же виде, в каком он записан в брифе.</summary>
        internal static Color ColorOf(Tone tone)
        {
            switch (tone)
            {
                case Tone.Stage: return HoleInWallPalette.Stage;
                case Tone.PoolRim: return Lift(HoleInWallPalette.Stage, rimLift);
                case Tone.Plastic: return HoleInWallPalette.Plastic;
                case Tone.FloorOwn: return HoleInWallPalette.FloorOwn;
                case Tone.FloorPartner: return HoleInWallPalette.FloorPartner;
                case Tone.Metal: return HoleInWallPalette.Metal;
                case Tone.NeonPink: return HoleInWallPalette.NeonPink;
                case Tone.NeonCyan: return HoleInWallPalette.NeonCyan;
                case Tone.Water: return HoleInWallPalette.Water;
                case Tone.Led: return HoleInWallPalette.Led;
                case Tone.Lane0: return HoleInWallPalette.LaneAccent(0);
                case Tone.Lane1: return HoleInWallPalette.LaneAccent(1);
                case Tone.Lane2: return HoleInWallPalette.LaneAccent(2);
                case Tone.Lane3: return HoleInWallPalette.LaneAccent(3);
                default: return Color.magenta;
            }
        }

        /// <summary>
        /// Материал тона. Ассет заводится при первом обращении и дальше живёт
        /// в проекте; свойства переписываются из палитры каждый раз.
        /// </summary>
        internal static Material Get(Tone tone)
        {
            if (cache.TryGetValue(tone, out Material cached) && cached != null)
            {
                return cached;
            }

            string path = $"{Folder}/{Prefix}{tone}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find(LitShaderName);
                if (shader == null)
                {
                    Debug.LogError($"Шейдер '{LitShaderName}' не найден — материалы «Дырки в стене» не собрать");
                    return null;
                }

                EnsureFolder();
                material = new Material(shader) { name = Prefix + tone };
                AssetDatabase.CreateAsset(material, path);
            }

            Apply(material, tone);
            EditorUtility.SetDirty(material);
            cache[tone] = material;
            return material;
        }

        /// <summary>Дописать заведённые материалы на диск. Вызывать в конце пересборки.</summary>
        internal static void Flush()
        {
            AssetDatabase.SaveAssets();
        }

        private static void Apply(Material material, Tone tone)
        {
            Color color = ColorOf(tone);

            switch (tone)
            {
                case Tone.Stage:
                case Tone.PoolRim:
                    HoleInWallMaterials.ConfigureOpaque(material, color, HoleInWallPalette.StageSmoothness, 0f);
                    break;

                case Tone.Plastic:
                case Tone.FloorOwn:
                case Tone.FloorPartner:
                    HoleInWallMaterials.ConfigureOpaque(material, color, HoleInWallPalette.PlasticSmoothness, 0f);
                    break;

                case Tone.Metal:
                    HoleInWallMaterials.ConfigureOpaque(material, color,
                        HoleInWallPalette.MetalSmoothness, HoleInWallPalette.MetalMetallic);
                    break;

                case Tone.NeonPink:
                case Tone.NeonCyan:
                    HoleInWallMaterials.ConfigureEmissive(material, color, HoleInWallPalette.NeonEmission);
                    break;

                // Светящиеся поверхности студии светятся слабее контура выреза,
                // и это не оттенок настройки, а требование чтения: контур —
                // единственное, что обязано читаться с 30 ШП, и всё остальное
                // светящееся в кадре обязано быть тише него.
                case Tone.Led:
                    HoleInWallMaterials.ConfigureEmissive(material, color, HoleInWallPalette.LedEmission);
                    break;

                case Tone.Lane0:
                case Tone.Lane1:
                case Tone.Lane2:
                case Tone.Lane3:
                    HoleInWallMaterials.ConfigureEmissive(material, color, HoleInWallPalette.LaneAccentEmission);
                    break;

                case Tone.Water:
                    HoleInWallMaterials.ConfigureTransparent(material, color, HoleInWallPalette.WaterSmoothness);
                    break;
            }
        }

        /// <summary>
        /// Поднять тон по яркости. Умножение идёт в линейном пространстве:
        /// «в два раза светлее» — это про свет, а не про коды цветов, и в sRGB
        /// то же умножение дало бы заметно более светлый результат.
        /// </summary>
        private static Color Lift(Color srgb, float factor)
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

            AssetDatabase.CreateFolder(MaterialsRoot, "HoleInWall");
        }
    }
}
