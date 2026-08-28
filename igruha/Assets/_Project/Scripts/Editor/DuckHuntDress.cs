using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Одевание блокаута Duck Hunt в модели паков Synty (фаза 4, арт).
    ///
    /// Устроено как слой ПОВЕРХ геометрии, а не вместо неё. Коробка блокаута
    /// остаётся на месте со своим коллайдером и слоем — гасится только её
    /// рендерер, а внутрь подсаживается модель, растянутая ровно по габаритам
    /// коробки. Смысл ровно один: вся выверенная геометрия башни — дальности
    /// прыжков, линии прострела, видимость зоны с рычага — держится на
    /// коллайдерах, и арт не имеет права её сдвинуть ни на сантиметр.
    ///
    /// Отсюда же следует, что у моделей дресса коллайдеры срезаются: мешевый
    /// коллайдер снопа сена поверх коробки паркура дал бы вторую поверхность
    /// другой формы, и приземление стало бы лотереей.
    ///
    /// Габариты моделей не угаданы, а замерены по мешам самих префабов и
    /// кэшируются на прогон.
    /// </summary>
    internal static class DuckHuntDress
    {
        // Стен в каталоге намеренно нет. Подгонка модели идёт по высоте
        // коробки, а задняя стена этажа — это 34 метра длины на пять высоты:
        // забор, растянутый до пяти метров, превращается в бревно толщиной с
        // человека, а поленница — в штабель размером с дом. Стены остаются на
        // материале палитры пака, и это правильный вид: их всё равно закрывают
        // укрытия, а фон обязан быть спокойным.

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

        /// <summary>Как модель садится в коробку.</summary>
        private enum Fit
        {
            /// <summary>Одна копия, растянутая по всем трём осям точно в габарит.</summary>
            Stretch,

            /// <summary>
            /// Копии в ряд вдоль длинной горизонтальной оси. Масштаб берётся
            /// по высоте коробки, число копий — по её длине. Так тюк остаётся
            /// тюком, а не растянутым по коридору бревном.
            /// </summary>
            Row
        }

        private readonly struct Entry
        {
            public readonly string Prefab;
            public readonly Fit Fit;

            /// <summary>Доворот вокруг Y, четвертями. Кратен 90: иначе габарит модели перестаёт ложиться в габарит коробки.</summary>
            public readonly int YawSteps;

            public Entry(string prefab, Fit fit, int yawSteps = 0)
            {
                Prefab = prefab;
                Fit = fit;
                YawSteps = yawSteps;
            }
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

        private static readonly Dictionary<string, Bounds> boundsCache = new Dictionary<string, Bounds>(64);
        private static readonly List<string> missing = new List<string>(8);

        /// <summary>Сбросить кэш и список ненайденного перед пересборкой.</summary>
        internal static void Begin()
        {
            boundsCache.Clear();
            missing.Clear();
        }

        /// <summary>Пути моделей, которых не оказалось в проекте: паки Synty ставит каждый себе сам.</summary>
        internal static IReadOnlyList<string> Missing
        {
            get { return missing; }
        }

        /// <summary>
        /// Одеть коробку блокаута. Рендерер коробки гаснет, модель садится
        /// внутрь по её габаритам. Коллайдер и слой коробки не трогаются.
        /// </summary>
        internal static GameObject Apply(GameObject box, Kind kind, bool winter, System.Random rng)
        {
            if (box == null || kind == Kind.None)
            {
                return null;
            }

            Dictionary<Kind, Entry[]> table = winter ? Winter : Summer;
            Entry[] entries;
            if (!table.TryGetValue(kind, out entries) || entries.Length == 0)
            {
                return null;
            }

            Entry entry = entries[rng.Next(entries.Length)];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.Prefab);
            if (prefab == null)
            {
                if (!missing.Contains(entry.Prefab))
                {
                    missing.Add(entry.Prefab);
                }

                return null;
            }

            Bounds local = GetBounds(prefab, entry.Prefab);
            if (local.size.x <= Mathf.Epsilon || local.size.y <= Mathf.Epsilon || local.size.z <= Mathf.Epsilon)
            {
                return null;
            }

            var renderer = box.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }

            var holder = new GameObject("Dress");
            holder.transform.SetParent(box.transform, false);
            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.identity;
            holder.transform.localScale = Vector3.one;

            Vector3 boxScale = box.transform.lossyScale;
            if (entry.Fit == Fit.Row)
            {
                PlaceRow(holder.transform, prefab, entry, local, boxScale, rng);
            }
            else
            {
                PlaceSingle(holder.transform, prefab, entry, local, boxScale);
            }

            return holder;
        }

        /// <summary>Одна копия, растянутая точно в габарит коробки.</summary>
        private static void PlaceSingle(Transform parent, GameObject prefab, Entry entry, Bounds local, Vector3 boxScale)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            StripColliders(go);

            // В оси модели перекладывается габарит коробки, а не наоборот.
            // Масштаб применяется до доворота, поэтому посчитанный в осях
            // коробки он после поворота на 90° уходит не на ту ось: стог,
            // одевавший полосу провала, выходил 21 метр в длину при коробке
            // в 4.32 и ложился поперёк соседних кусков перекрытия.
            Vector3 target = SwapForYaw(boxScale, entry.YawSteps);
            Vector3 world = new Vector3(
                Mathf.Abs(target.x) / local.size.x,
                Mathf.Abs(target.y) / local.size.y,
                Mathf.Abs(target.z) / local.size.z);

            ApplyTransform(go.transform, entry.YawSteps, local, boxScale, world, Vector3.zero);
        }

        /// <summary>
        /// Ряд копий вдоль длинной горизонтальной оси коробки. Масштаб задаёт
        /// высота, остаток по длине добирается растяжением, но не больше
        /// <see cref="StretchLimit"/> — дальше ставится ещё одна копия.
        /// </summary>
        private static void PlaceRow(Transform parent, GameObject prefab, Entry entry, Bounds local,
            Vector3 boxScale, System.Random rng)
        {
            Vector3 size = SwapForYaw(local.size, entry.YawSteps);
            bool alongX = Mathf.Abs(boxScale.x) >= Mathf.Abs(boxScale.z);
            float runLength = Mathf.Abs(alongX ? boxScale.x : boxScale.z);
            float crossLength = Mathf.Abs(alongX ? boxScale.z : boxScale.x);
            float height = Mathf.Abs(boxScale.y);

            // Масштаб по высоте: укрытие обязано прятать ровно на свою высоту,
            // это его геймплейный смысл, а не пропорция модели.
            float uniform = height / size.y;
            float runPiece = (alongX ? size.x : size.z) * uniform;
            int count = Mathf.Max(1, Mathf.RoundToInt(runLength / Mathf.Max(0.0001f, runPiece)));
            float fill = runLength / (count * Mathf.Max(0.0001f, runPiece));
            if (fill > StretchLimit)
            {
                count += 1;
                fill = runLength / (count * runPiece);
            }

            float crossPiece = (alongX ? size.z : size.x) * uniform;
            float crossFill = crossLength / Mathf.Max(0.0001f, crossPiece);
            crossFill = Mathf.Clamp(crossFill, 1f / StretchLimit, StretchLimit);

            for (int i = 0; i < count; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                StripColliders(go);

                float centre01 = (i + 0.5f) / count - 0.5f;
                Vector3 offset01 = alongX ? new Vector3(centre01, 0f, 0f) : new Vector3(0f, 0f, centre01);

                // Разворот через одну копию: одинаково повёрнутый ряд читается
                // как забор из клонов, а зеркальный — как сложенная кладка.
                // Пол-оборота осей не меняет, четверть — меняет.
                int yaw = entry.YawSteps + (rng.Next(2) == 0 ? 0 : 2);

                Vector3 world = SwapForYaw(alongX
                    ? new Vector3(uniform * fill, uniform, uniform * crossFill)
                    : new Vector3(uniform * crossFill, uniform, uniform * fill), yaw);

                ApplyTransform(go.transform, yaw, local, boxScale, world, offset01);
            }
        }

        /// <summary>
        /// Посадить модель: доворот вокруг Y, мировой масштаб world, центр
        /// габарита — в точку коробки, заданную долями offset01 от её размера.
        /// Всё считается через lossyScale коробки, потому что коробка блокаута
        /// сама растянута до размера, и наследование её масштаба раздавило бы
        /// модель ровно во столько же раз.
        /// </summary>
        private static void ApplyTransform(Transform t, int yawSteps, Bounds local, Vector3 boxScale,
            Vector3 world, Vector3 offset01)
        {
            Quaternion yaw = Quaternion.Euler(0f, 90f * yawSteps, 0f);
            t.localRotation = yaw;

            // Делится на габарит коробки в осях модели: масштаб по оси X модели
            // после доворота на 90° умножается на масштаб коробки по Z, а не X.
            Vector3 parent = SwapForYaw(boxScale, yawSteps);
            t.localScale = new Vector3(
                world.x / Mathf.Max(0.0001f, Mathf.Abs(parent.x)),
                world.y / Mathf.Max(0.0001f, Mathf.Abs(parent.y)),
                world.z / Mathf.Max(0.0001f, Mathf.Abs(parent.z)));

            // Центр габарита модели после масштаба и доворота — в локальных
            // единицах коробки, то есть в долях её размера.
            Vector3 scaled = new Vector3(local.center.x * world.x, local.center.y * world.y, local.center.z * world.z);
            Vector3 turned = yaw * scaled;
            Vector3 centreLocal = new Vector3(
                turned.x / Mathf.Max(0.0001f, Mathf.Abs(boxScale.x)),
                turned.y / Mathf.Max(0.0001f, Mathf.Abs(boxScale.y)),
                turned.z / Mathf.Max(0.0001f, Mathf.Abs(boxScale.z)));

            t.localPosition = offset01 - centreLocal;
        }

        /// <summary>
        /// Переложить вектор между осями коробки и осями модели. Доворот на 90°
        /// меняет длину и глубину местами, на 180° — не меняет ничего, поэтому
        /// смотрится только чётность. Операция обратна самой себе.
        /// </summary>
        private static Vector3 SwapForYaw(Vector3 v, int yawSteps)
        {
            return (Mathf.Abs(yawSteps) % 2 == 0) ? v : new Vector3(v.z, v.y, v.x);
        }

        /// <summary>
        /// Коллайдеры моделей срезаются: столкновения на арене держит коробка
        /// блокаута, а мешевый коллайдер модели дал бы вторую поверхность
        /// другой формы поверх выверенной.
        /// </summary>
        private static void StripColliders(GameObject go)
        {
            var colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Object.DestroyImmediate(colliders[i], true);
            }

            StaticEditorFlags flags = StaticEditorFlags.BatchingStatic
                                      | StaticEditorFlags.OccluderStatic
                                      | StaticEditorFlags.OccludeeStatic;
            GameObjectUtility.SetStaticEditorFlags(go, flags);
        }

        /// <summary>Габариты префаба по его мешам, в локальных координатах корня. Кэшируются на прогон.</summary>
        internal static Bounds GetBounds(GameObject prefab, string key)
        {
            Bounds cached;
            if (boundsCache.TryGetValue(key, out cached))
            {
                return cached;
            }

            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            bool has = false;
            Bounds total = new Bounds();
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                Bounds mb = mesh.bounds;
                Transform t = filters[i].transform;
                Vector3 centre = prefab.transform.InverseTransformPoint(t.TransformPoint(mb.center));
                Vector3 scale = t.lossyScale;
                Vector3 extents = new Vector3(
                    mb.extents.x * Mathf.Abs(scale.x),
                    mb.extents.y * Mathf.Abs(scale.y),
                    mb.extents.z * Mathf.Abs(scale.z));

                var one = new Bounds(centre, extents * 2f);
                if (!has)
                {
                    total = one;
                    has = true;
                }
                else
                {
                    total.Encapsulate(one);
                }
            }

            boundsCache[key] = total;
            return total;
        }
    }
}
