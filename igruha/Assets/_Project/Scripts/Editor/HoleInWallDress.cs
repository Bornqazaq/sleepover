using System.Collections.Generic;
using UnityEngine;
using Entry = Igruha.EditorTools.DressKit.Entry;
using Fit = Igruha.EditorTools.DressKit.Fit;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Каталог одевания «Дырки в стене»: какой вид коробки блокаута какой
    /// моделью пака закрывается (фаза 4, подфаза 4.1).
    ///
    /// Здесь только каталог. Замер габаритов, посадка модели в коробку, ряды и
    /// сетки копий, срезание коллайдеров — в <see cref="DressKit"/>, одном на
    /// все мини-игры.
    ///
    /// <b>Стены в каталоге нет намеренно.</b> Плита стены — 12 × 5 ШП, и это
    /// та самая большая плоскость, которую моделями не одевают: любая модель,
    /// растянутая на восемь метров, читается бревном. Стена остаётся на
    /// материале палитры (подфаза 4.2), и это правильный вид: по брифу на ней
    /// не должно быть ничего, кроме контура выреза, — узор спорит с силуэтом,
    /// а силуэт здесь и есть игра.
    ///
    /// <b>У каждого вида ровно одна модель.</b> В Duck Hunt список моделей на
    /// вид давал разнообразие укрытий, здесь это было бы вредом: четыре дорожки
    /// соревнуются между собой, и разный реквизит на них читался бы как разные
    /// условия. Случайность оставлена только на разворот копий.
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

        private const string Nightclubs = "Assets/Synty/PolygonNightclubs/Prefabs/";
        private const string Modular = Nightclubs + "Props/Modular/";
        private const string BaseBuildings = Nightclubs + "Base_Buildings/";
        private const string GenericBuilding = "Assets/Synty/PolygonGeneric/Prefabs/Building/";

        private static readonly Dictionary<Kind, Entry[]> Catalog = new Dictionary<Kind, Entry[]>
        {
            {
                // Сценический подиум: толщина модели 0.5 м при коробке 0.72 —
                // край настила читается как край сцены, а не как бумажный лист.
                // Настил Stage_Floor толщиной 0.1 м дал бы сверху правильный
                // пол, но сбоку — щель в семь сантиметров над пустотой.
                Kind.PlatformDeck, new[] { new Entry(Modular + "SM_Prop_Platform_02.prefab", Fit.Tile) }
            },
            {
                // Та же модульная сцена, но повтором вверх: тумба собирается
                // из подиумов в два яруса, как настоящий сценический помост.
                Kind.Support, new[] { new Entry(Modular + "SM_Prop_Platform_01.prefab", Fit.Wall) }
            },
            {
                // Простая панель в 20 треугольников: бортик идёт по всему
                // периметру арены, это шестьдесят копий, и цену за них платить
                // нечем — цвет всё равно придёт материалом палитры на 4.2.
                Kind.PoolRim, new[] { new Entry(BaseBuildings + "SM_Bld_Base_Wall_01.prefab", Fit.Wall) }
            },
            {
                // Лесенка в 7 ШП высотой собирается из трёхметровых секций.
                // Бассейновая лесенка из Town (1.63 м) на такой высоте дала бы
                // три поручня друг над другом.
                Kind.Ladder, new[] { new Entry(GenericBuilding + "SM_Gen_Bld_Ladder_01.prefab", Fit.Column) }
            }
        };

        /// <summary>Сбросить кэш и список ненайденного перед пересборкой.</summary>
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
            if (kind == Kind.None)
            {
                return null;
            }

            Entry[] entries;
            return Catalog.TryGetValue(kind, out entries) ? DressKit.Apply(box, entries, rng) : null;
        }
    }
}
