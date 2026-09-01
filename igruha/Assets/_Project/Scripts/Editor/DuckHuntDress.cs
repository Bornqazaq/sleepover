using System.Collections.Generic;
using UnityEngine;
using Entry = Igruha.EditorTools.DressKit.Entry;
using Fit = Igruha.EditorTools.DressKit.Fit;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Каталог одевания Duck Hunt: какой вид объекта какими моделями паков
    /// закрывается летом и зимой (фаза 4, арт).
    ///
    /// Здесь только каталог. Вся механика — замер габаритов, посадка модели
    /// в коробку, ряд копий, срезание коллайдеров, ограничение вылезания —
    /// живёт в <see cref="DressKit"/> и одна на все мини-игры. Раньше она
    /// лежала прямо здесь, и следующей игре пришлось бы копировать её целиком.
    ///
    /// Стен в каталоге намеренно нет. Подгонка модели идёт по высоте
    /// коробки, а задняя стена этажа — это 34 метра длины на пять высоты:
    /// забор, растянутый до пяти метров, превращается в бревно толщиной с
    /// человека, а поленница — в штабель размером с дом. Стены остаются на
    /// материале палитры пака, и это правильный вид: их всё равно закрывают
    /// укрытия, а фон обязан быть спокойным.
    /// </summary>
    internal static class DuckHuntDress
    {
        /// <summary>Что именно одевается. Один вид — один список моделей на биом.</summary>
        internal enum Kind
        {
            None,
            Ledge,
            CoverHigh,
            CoverLow,
            Platform,
            GateStep,
            TrapDoor,
            TrapFloor,
            TrapPad,
            LeverPost,
            SpawnShield,
            ElevatorDeck,
            ElevatorRail
        }

        private const string FarmProps = "Assets/Synty/PolygonFarm/Prefabs/Props/";
        private const string Alpine = "Assets/Synty/PolygonNatureBiomes/PNB_Alpine_Mountain/Prefabs/";
        private const string AlpineProps = Alpine + "Props/";

        /// <summary>
        /// Предел неравномерного растяжения модели, доля. За ним тюк читается
        /// как размазанный блин, и лучше поставить лишнюю копию.
        /// </summary>
        private const float StretchLimit = 1.35f;

        private static readonly Dictionary<Kind, Entry[]> Summer = new Dictionary<Kind, Entry[]>
        {
            {
                Kind.Ledge, new[]
                {
                    new Entry(FarmProps + "SM_Prop_Fence_Wood_01.prefab", Fit.Row),
                    new Entry(FarmProps + "SM_Prop_Fence_Painted_01.prefab", Fit.Row)
                }
            },
            {
                // Высокое укрытие прячет стоящего: круглый тюк, ящик, поленница.
                Kind.CoverHigh, new[]
                {
                    new Entry(FarmProps + "SM_Prop_Hay_Bale_Round_01.prefab", Fit.Row),
                    new Entry(FarmProps + "SM_Prop_Hay_Bale_Round_02.prefab", Fit.Row),
                    new Entry(FarmProps + "SM_Prop_Crate_01.prefab", Fit.Row)
                }
            },
            {
                // Низкое прячет только присевшего: квадратный тюк, бочка, корыто.
                Kind.CoverLow, new[]
                {
                    new Entry(FarmProps + "SM_Prop_Hay_Bale_Square_01.prefab", Fit.Row),
                    new Entry(FarmProps + "SM_Prop_Hay_Bale_Square_02.prefab", Fit.Row),
                    new Entry(FarmProps + "SM_Prop_Trough_01.prefab", Fit.Row),
                    new Entry(FarmProps + "SM_Prop_Wood_Stack_01.prefab", Fit.Row)
                }
            },
            {
                // Паркур: приземляться надо на плоское и с читаемым краем.
                Kind.Platform, new[]
                {
                    new Entry(FarmProps + "SM_Prop_Hay_Bale_Square_01.prefab", Fit.Stretch),
                    new Entry(FarmProps + "SM_Prop_Hay_Bale_Square_02.prefab", Fit.Stretch),
                    new Entry(FarmProps + "SM_Prop_Hay_Bale_Square_03.prefab", Fit.Stretch)
                }
            },
            {
                Kind.GateStep, new[]
                {
                    new Entry(FarmProps + "SM_Prop_Crate_01.prefab", Fit.Stretch),
                    new Entry(FarmProps + "SM_Prop_Hay_Bale_Square_02.prefab", Fit.Stretch)
                }
            },
            { Kind.TrapDoor, new[] { new Entry(FarmProps + "SM_Prop_Fence_Wood_Gate_01.prefab", Fit.Stretch) } },
            { Kind.TrapFloor, new[] { new Entry(FarmProps + "SM_Prop_Hay_Pile_02.prefab", Fit.Stretch, 1) } },
            { Kind.TrapPad, new[] { new Entry(FarmProps + "SM_Prop_Hay_Pile_01.prefab", Fit.Stretch) } },
            { Kind.LeverPost, new[] { new Entry(FarmProps + "SM_Prop_SignPost_03.prefab", Fit.Stretch) } },
            { Kind.SpawnShield, new[] { new Entry(FarmProps + "SM_Prop_Fence_Wood_01.prefab", Fit.Row) } },
            { Kind.ElevatorDeck, new[] { new Entry(FarmProps + "SM_Prop_PalletCrate_01.prefab", Fit.Row) } },
            { Kind.ElevatorRail, new[] { new Entry(FarmProps + "SM_Prop_Fence_Wire_01.prefab", Fit.Row) } }
        };

        private static readonly Dictionary<Kind, Entry[]> Winter = new Dictionary<Kind, Entry[]>
        {
            {
                Kind.Ledge, new[]
                {
                    new Entry(FarmProps + "SM_Prop_Fence_Wood_02.prefab", Fit.Row),
                    new Entry(Alpine + "SM_Env_Snow_Mound_01.prefab", Fit.Row)
                }
            },
            {
                // Спека (14.2) просит зимой сугробы вместо стогов, и это не
                // только про настроение: тёмный камень на снегу читается как
                // дыра в полу, а не как укрытие, за которое можно спрятаться.
                // Камень оставлен один, для разнообразия силуэта.
                Kind.CoverHigh, new[]
                {
                    new Entry(Alpine + "SM_Env_Snow_Mound_04.prefab", Fit.Row),
                    new Entry(Alpine + "SM_Env_Snow_Mound_04.prefab", Fit.Row, 1),
                    new Entry(Alpine + "SM_Env_Rock_04.prefab", Fit.Row)
                }
            },
            {
                Kind.CoverLow, new[]
                {
                    new Entry(Alpine + "SM_Env_Snow_Mound_02.prefab", Fit.Row),
                    new Entry(Alpine + "SM_Env_Snow_Mound_03.prefab", Fit.Row),
                    new Entry(Alpine + "SM_Env_Snow_Mound_01.prefab", Fit.Row),
                    new Entry(AlpineProps + "SM_Prop_Wood_Pile_02.prefab", Fit.Row)
                }
            },
            {
                Kind.Platform, new[]
                {
                    new Entry(Alpine + "SM_Env_Rock_Small_01.prefab", Fit.Stretch),
                    new Entry(Alpine + "SM_Env_Rock_Small_02.prefab", Fit.Stretch),
                    new Entry(AlpineProps + "SM_Prop_Wood_Pile_01.prefab", Fit.Stretch)
                }
            },
            {
                Kind.GateStep, new[]
                {
                    new Entry(Alpine + "SM_Env_Rock_Small_01.prefab", Fit.Stretch),
                    new Entry(Alpine + "SM_Env_Rock_Small_03.prefab", Fit.Stretch)
                }
            },
            { Kind.TrapDoor, new[] { new Entry(FarmProps + "SM_Prop_Fence_Wood_Gate_01.prefab", Fit.Stretch) } },
            { Kind.TrapFloor, new[] { new Entry(Alpine + "SM_Env_Ice_Sheet_05.prefab", Fit.Stretch) } },
            { Kind.TrapPad, new[] { new Entry(Alpine + "SM_Env_Ice_Sheet_03.prefab", Fit.Stretch) } },
            { Kind.LeverPost, new[] { new Entry(FarmProps + "SM_Prop_SignPost_03.prefab", Fit.Stretch) } },
            { Kind.SpawnShield, new[] { new Entry(FarmProps + "SM_Prop_Fence_Wood_01.prefab", Fit.Row) } },
            { Kind.ElevatorDeck, new[] { new Entry(FarmProps + "SM_Prop_PalletCrate_01.prefab", Fit.Row) } },
            { Kind.ElevatorRail, new[] { new Entry(FarmProps + "SM_Prop_Fence_Wire_01.prefab", Fit.Row) } }
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
        internal static GameObject Apply(GameObject box, Kind kind, bool winter, System.Random rng)
        {
            if (kind == Kind.None)
            {
                return null;
            }

            Dictionary<Kind, Entry[]> table = winter ? Winter : Summer;
            Entry[] entries;
            return table.TryGetValue(kind, out entries) ? DressKit.Apply(box, entries, rng) : null;
        }
    }
}
