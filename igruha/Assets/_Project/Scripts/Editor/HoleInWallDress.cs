using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Entry = Igruha.EditorTools.DressKit.Entry;
using Fit = Igruha.EditorTools.DressKit.Fit;
using Tone = Igruha.EditorTools.HoleInWallPaletteAssets.Tone;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Каталог одевания «Дырки в стене»: какой вид коробки блокаута какой
    /// моделью пака закрывается (подфаза 4.1) и каким тоном палитры она потом
    /// красится (подфаза 4.2).
    ///
    /// Здесь только каталог. Замер габаритов, посадка модели в коробку, ряды и
    /// сетки копий, срезание коллайдеров, перекраска — в <see cref="DressKit"/>,
    /// одном на все мини-игры; сами цвета — в
    /// <see cref="HoleInWallPaletteAssets"/>.
    ///
    /// <b>Стены в каталоге нет намеренно.</b> Плита стены — 12 × 5 ШП, и это
    /// та самая большая плоскость, которую моделями не одевают: любая модель,
    /// растянутая на восемь метров, читается бревном. Стена остаётся на
    /// материале палитры, и это правильный вид: по брифу на ней не должно быть
    /// ничего, кроме контура выреза, — узор спорит с силуэтом, а силуэт здесь
    /// и есть игра.
    ///
    /// <b>У каждого вида ровно одна модель.</b> В Duck Hunt список моделей на
    /// вид давал разнообразие укрытий, здесь это было бы вредом: четыре дорожки
    /// соревнуются между собой, и разный реквизит на них читался бы как разные
    /// условия. Случайность оставлена только на разворот копий.
    ///
    /// <b>Родные материалы пака не переживают одевание.</b> Модели приходят
    /// с атласом: зебра на борту тумбы, шахматка на верху подиума. Бриф требует
    /// белого глянцевого пластика и прямо запрещает узоры — «никаких ярких
    /// пятен и узоров, кроме контура выреза». Поэтому каждой модели назначается
    /// тон палитры, а не подбирается похожий кусок атласа.
    /// </summary>
    internal static class HoleInWallDress
    {
        /// <summary>Что именно одевается.</summary>
        internal enum Kind
        {
            None,

            /// <summary>Настил дорожки: по нему ходят, его край — граница отхода.</summary>
            PlatformDeck,

            /// <summary>Тумба под настилом, видна над водой.</summary>
            Support,

            /// <summary>Бортик бассейна: стенка студийного бассейна по кругу арены.</summary>
            PoolRim,

            /// <summary>Лесенка из воды. Декор: возврат на платформу автоматический.</summary>
            Ladder
        }

        /// <summary>Одевание одного вида: чем закрываем и каким тоном красим.</summary>
        private readonly struct Wear
        {
            public readonly string Title;
            public readonly Tone Paint;
            public readonly Entry[] Entries;

            public Wear(string title, Tone paint, params Entry[] entries)
            {
                Title = title;
                Paint = paint;
                Entries = entries;
            }
        }

        private const string Nightclubs = "Assets/Synty/PolygonNightclubs/Prefabs/";
        private const string Modular = Nightclubs + "Props/Modular/";
        private const string BaseBuildings = Nightclubs + "Base_Buildings/";
        private const string GenericBuilding = "Assets/Synty/PolygonGeneric/Prefabs/Building/";

        private static readonly Dictionary<Kind, Wear> Catalog = new Dictionary<Kind, Wear>
        {
            {
                // Сценический подиум: толщина модели 0.5 м при коробке 0.72 —
                // край настила читается как край сцены, а не как бумажный лист.
                // Настил Stage_Floor толщиной 0.1 м дал бы сверху правильный
                // пол, но сбоку — щель в семь сантиметров над пустотой.
                //
                // Тон — белый пластик: настил обязан читаться краем над тёмной
                // водой, это граница отхода, а не украшение.
                Kind.PlatformDeck,
                new Wear("настил", Tone.Plastic, new Entry(Modular + "SM_Prop_Platform_02.prefab", Fit.Tile))
            },
            {
                // Та же модульная сцена, но повтором вверх: тумба собирается
                // из подиумов в два яруса, как настоящий сценический помост.
                //
                // Тон — тёмный: белая тумба под белым настилом слила бы их
                // в один брусок, и высота платформы над водой перестала бы
                // читаться.
                Kind.Support,
                new Wear("тумба", Tone.Stage, new Entry(Modular + "SM_Prop_Platform_01.prefab", Fit.Wall))
            },
            {
                // Простая панель в 20 треугольников: бортик идёт по всему
                // периметру арены, это шестьдесят копий, и цену за них платить
                // нечем — цвет всё равно приходит материалом палитры.
                Kind.PoolRim,
                new Wear("бортик", Tone.PoolRim, new Entry(BaseBuildings + "SM_Bld_Base_Wall_01.prefab", Fit.Wall))
            },
            {
                // Лесенка в 7 ШП высотой собирается из трёхметровых секций.
                // Бассейновая лесенка из Town (1.63 м) на такой высоте дала бы
                // три поручня друг над другом.
                Kind.Ladder,
                new Wear("лесенка", Tone.Metal, new Entry(GenericBuilding + "SM_Gen_Bld_Ladder_01.prefab", Fit.Column))
            }
        };

        /// <summary>
        /// Сбросить кэш и список ненайденного перед пересборкой, замерить пак
        /// и завести по замеру палитру.
        /// </summary>
        internal static void Begin()
        {
            DressKit.Begin();
            SyntyPalette.ClearCache();
            MeasurePalette();
        }

        /// <summary>Пути моделей, которых не оказалось в проекте: паки Synty ставит каждый себе сам.</summary>
        internal static IReadOnlyList<string> Missing
        {
            get { return DressKit.Missing; }
        }

        /// <summary>
        /// Одеть коробку блокаута моделью нужного вида и покрасить её тоном
        /// палитры. Рендерер коробки гаснет, модель садится внутрь по её
        /// габаритам; коллайдер и слой коробки не трогаются.
        /// </summary>
        internal static GameObject Apply(GameObject box, Kind kind, System.Random rng)
        {
            if (kind == Kind.None || !Catalog.TryGetValue(kind, out Wear wear))
            {
                return null;
            }

            return DressKit.Apply(box, wear.Entries, rng, HoleInWallPaletteAssets.Get(wear.Paint));
        }

        /// <summary>
        /// Материал палитры этого вида. Им красится и модель пака, и сама
        /// коробка под ней: на машине без паков коробка остаётся видимой, и
        /// цвет у неё обязан быть тот же, иначе разница читается как «арт
        /// сделан наполовину».
        /// </summary>
        internal static Material PaintOf(Kind kind)
        {
            return Catalog.TryGetValue(kind, out Wear wear) ? HoleInWallPaletteAssets.Get(wear.Paint) : null;
        }

        /// <summary>
        /// Таблица замеров: что пак показывает сам и во что это перекрашено.
        /// Печатается пересборкой — цвет палитры обязан быть проверяемым числом,
        /// а не словом «подобрал».
        /// </summary>
        internal static string MeasurementReport()
        {
            var report = new StringBuilder();
            report.Append("🎨 «Дырка в стене», замер по UV моделей пака → палитра (подъём бортика: ")
                .Append(HoleInWallPaletteAssets.RimLiftSource)
                .Append(')');

            foreach (KeyValuePair<Kind, Wear> pair in Catalog)
            {
                Wear wear = pair.Value;
                Color tone = HoleInWallPaletteAssets.ColorOf(wear.Paint);
                report.Append("\n— ").Append(wear.Title.PadRight(8));

                if (TryAverage(wear, out Color pack))
                {
                    report.Append("пак ").Append(SyntyPalette.Hex(pack))
                        .Append(" (Y=").Append(SyntyPalette.Luminance(pack).ToString("F3")).Append(')');
                }
                else
                {
                    report.Append("пак не замерен — модели нет в проекте");
                }

                report.Append(" → HIW_").Append(wear.Paint).Append(' ').Append(SyntyPalette.Hex(tone));
            }

            return report.ToString();
        }

        /// <summary>
        /// Замерить пак и отдать палитре одно число — насколько бортик бассейна
        /// светлее тумбы. Больше замер палитре не нужен: остальные цвета названы
        /// брифом прямо, а вот разницу между двумя тёмными поверхностями бриф
        /// не задаёт, и брать её с потолка — значит слить бортик с дном.
        /// </summary>
        private static void MeasurePalette()
        {
            if (TryLuminance(Kind.Support, out float support) &&
                TryLuminance(Kind.PoolRim, out float rim) &&
                support > 0f)
            {
                HoleInWallPaletteAssets.Begin(rim / support,
                    $"замер по UV, тумба Y={support:F3} и бортик Y={rim:F3}");
                return;
            }

            HoleInWallPaletteAssets.Begin(0f, null);
        }

        private static bool TryLuminance(Kind kind, out float luminance)
        {
            luminance = 0f;
            if (!Catalog.TryGetValue(kind, out Wear wear) || !TryAverage(wear, out Color average))
            {
                return false;
            }

            luminance = SyntyPalette.Luminance(average);
            return true;
        }

        private static bool TryAverage(Wear wear, out Color average)
        {
            average = Color.black;
            return wear.Entries != null && wear.Entries.Length > 0 &&
                   SyntyPalette.TryAverage(wear.Entries[0].Prefab, out average);
        }
    }
}
