using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Общая механика одевания блокаута в модели паков — ядро подфазы 4.1.
    ///
    /// Уникальным у мини-игры остаётся только каталог «вид объекта → модели».
    /// Всё остальное — замер габаритов, посадка модели в коробку, ряд копий,
    /// срезание коллайдеров, ограничение вылезания — одно на все игры и живёт
    /// здесь. Первым каталогом был <see cref="DuckHuntDress"/>: механика
    /// выверялась на нём, и все оговорки ниже — его уроки.
    ///
    /// <b>Дресс — слой ПОВЕРХ блокаута, а не замена.</b> Коробка остаётся на
    /// месте со своим коллайдером и слоем, гасится только её рендерер, а внутрь
    /// садится модель, растянутая ровно по габаритам коробки. Смысл ровно один:
    /// выверенная фазами 2–3 геометрия держится на коллайдерах, и арт не имеет
    /// права сдвинуть её ни на сантиметр.
    /// </summary>
    internal static class DressKit
    {
        /// <summary>Как модель садится в коробку.</summary>
        internal enum Fit
        {
            /// <summary>Одна копия, растянутая по всем трём осям точно в габарит.</summary>
            Stretch,

            /// <summary>
            /// Копии в ряд вдоль длинной горизонтальной оси. Масштаб берётся
            /// по высоте коробки, число копий — по её длине. Так тюк остаётся
            /// тюком, а не растянутым по коридору бревном.
            /// </summary>
            Row,

            /// <summary>
            /// Сетка копий по полу коробки: повтор по X и Z, высота — своя.
            /// Для настилов и площадок. Масштаб берётся <b>из самой модели</b>,
            /// а не из высоты коробки, как в <see cref="Row"/>: настил толщиной
            /// 0.1 м, отмасштабированный по высоте коробки, вырастает в плиту
            /// шириной с дорожку.
            /// </summary>
            Tile,

            /// <summary>
            /// Стенка: повтор вдоль длинной горизонтальной оси и по высоте.
            /// Тонкая ось модели сама разворачивается к тонкой оси коробки,
            /// поэтому один и тот же каталог годится и для бортика вдоль X,
            /// и для бортика вдоль Z.
            /// </summary>
            Wall,

            /// <summary>
            /// Столбик копий по высоте. Для вертикальных предметов, которые
            /// выше своей модели: лесенка в 7 ШП собирается из трёхметровых,
            /// а не растягивается втрое вместе с перекладинами.
            /// </summary>
            Column
        }

        /// <summary>Одна модель каталога: что ставим и как сажаем.</summary>
        internal readonly struct Entry
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

        /// <summary>
        /// Предел неравномерного растяжения модели, доля. За ним тюк читается
        /// как размазанный блин, и лучше поставить лишнюю копию.
        /// </summary>
        internal const float StretchLimit = 1.35f;

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
        /// Загрузить модель пака, записав ненайденную в общий список.
        ///
        /// Нужен тем, кто ставит модель мимо коробки блокаута — окружению
        /// подфазы 4.3, где трибуна и телекамера садятся на пол студии
        /// по замеру, а не в габарит коробки. Список ненайденного при этом
        /// обязан остаться одним: пересборка печатает его целиком, и модель,
        /// потерянная в декорациях, не имеет права молчать только потому, что
        /// её ставили другим методом.
        /// </summary>
        internal static bool TryLoad(string path, out GameObject prefab)
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                return true;
            }

            if (!missing.Contains(path))
            {
                missing.Add(path);
            }

            return false;
        }

        /// <summary>
        /// Одеть коробку блокаута одной из моделей набора. Рендерер коробки
        /// гаснет, модель садится внутрь по её габаритам. Коллайдер и слой
        /// коробки не трогаются.
        ///
        /// <paramref name="rng"/> обязан быть <b>собственным</b> генератором
        /// дресса: общий с билдером сдвинет последовательность, по которой
        /// раскладываются геймплейные объекты, и проверенная планировка поедет
        /// от одной лишь смены модели.
        ///
        /// <paramref name="paint"/> — материал палитры мини-игры (подфаза 4.2).
        /// Пусто — модель остаётся с родными материалами пака.
        /// </summary>
        internal static GameObject Apply(GameObject box, Entry[] entries, System.Random rng, Material paint = null)
        {
            if (box == null || entries == null || entries.Length == 0)
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
            switch (entry.Fit)
            {
                case Fit.Row:
                    PlaceRow(holder.transform, prefab, entry, local, boxScale, rng);
                    break;
                case Fit.Tile:
                case Fit.Wall:
                case Fit.Column:
                    PlaceRepeat(holder.transform, prefab, entry, local, boxScale, rng);
                    break;
                default:
                    PlaceSingle(holder.transform, prefab, entry, local, boxScale);
                    break;
            }

            ClampToBox(holder.transform, box.transform);
            Repaint(holder, paint);
            return holder;
        }

        /// <summary>
        /// Перекрасить модель пака в материал палитры мини-игры — подфаза 4.2.
        ///
        /// Меняются <b>все слоты всех рендереров</b> копии, а не первый. Модели
        /// Synty собраны из нескольких подмешей с разными материалами атласа:
        /// у сценического подиума это тёмный корпус, шахматный верх и серая
        /// обвязка, у тумбы — ещё и зебра на борту. Покрасить первый слот —
        /// значит оставить узор ровно там, где бриф его запрещает.
        ///
        /// Плоский цвет на всю модель здесь не обеднение, а требование:
        /// «белый глянцевый пластик» из брифа — это отсутствие рисунка, а форму
        /// подиума держат геометрия и свет, а не текстура.
        ///
        /// Тем же красится и декор окружения (подфаза 4.3), который ставится
        /// мимо коробки блокаута: трибуна приезжает из «Карнавала» в своей
        /// ярмарочной раскраске, и в тёмной студии она кричала бы громче арены.
        /// </summary>
        internal static void Repaint(GameObject go, Material paint)
        {
            if (go == null || paint == null)
            {
                return;
            }

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] slots = renderers[i].sharedMaterials;
                bool changed = false;
                for (int slot = 0; slot < slots.Length; slot++)
                {
                    if (slots[slot] == paint)
                    {
                        continue;
                    }

                    slots[slot] = paint;
                    changed = true;
                }

                if (changed)
                {
                    renderers[i].sharedMaterials = slots;
                }
            }
        }

        /// <summary>
        /// Последняя проверка: модель не имеет права торчать за свою коробку
        /// по горизонтали.
        ///
        /// Расчёты ниже считают масштаб из пропорций модели и упираются в
        /// <see cref="StretchLimit"/>, но с полученным габаритом никто не
        /// сверялся — и узкая в своих осях модель выходила кратно шире коробки.
        /// Поймано на корыте четвёртого этажа Duck Hunt: коробка укрытия 1.37 м,
        /// модель 5.32 м, полтора метра её висели над пропастью рядом с
        /// перекрытием. Дальше это читается уже не как дресс, а как забытый
        /// в воздухе кусок.
        ///
        /// По высоте не ужимаем: высота укрытия — его геймплейный смысл
        /// (прячет стоящего или присевшего), и ради неё модель растягивать
        /// как раз можно.
        /// </summary>
        private static void ClampToBox(Transform holder, Transform box)
        {
            var renderers = holder.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            Bounds total = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                total.Encapsulate(renderers[i].bounds);
            }

            Vector3 allowed = box.lossyScale;
            float limitX = Mathf.Abs(allowed.x) * StretchLimit;
            float limitZ = Mathf.Abs(allowed.z) * StretchLimit;
            float scaleX = total.size.x > limitX ? limitX / total.size.x : 1f;
            float scaleZ = total.size.z > limitZ ? limitZ / total.size.z : 1f;
            float shrink = Mathf.Min(scaleX, scaleZ);
            if (shrink >= 0.999f)
            {
                return;
            }

            // Ужимаем равномерно и только по горизонтали: неравномерное сжатие
            // сплющивает модель в блин, а это заметнее, чем лишний сантиметр.
            for (int i = 0; i < holder.childCount; i++)
            {
                Transform child = holder.GetChild(i);
                Vector3 s = child.localScale;
                child.localScale = new Vector3(s.x * shrink, s.y, s.z * shrink);

                Vector3 p = child.localPosition;
                child.localPosition = new Vector3(p.x * shrink, p.y, p.z * shrink);
            }
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
        /// Копии в натуральном масштабе, повторённые по осям, которые задаёт
        /// <see cref="Fit"/>: пол — по X и Z, стенка — вдоль длинной горизонтали
        /// и вверх, столбик — только вверх.
        ///
        /// Отличие от <see cref="PlaceRow"/> в одном, и оно принципиальное:
        /// масштаб здесь берётся из модели, а не из коробки. Ряд задуман для
        /// укрытий, где высота коробки — геймплейный смысл, и ради неё модель
        /// можно тянуть. У настила, бортика и лесенки смысл ровно обратный:
        /// доска обязана остаться доской своего размера, а коробка добирается
        /// числом копий. По осям, где повтора нет, модель не растягивается
        /// никогда — только ужимается, если не влезает.
        /// </summary>
        private static void PlaceRepeat(Transform parent, GameObject prefab, Entry entry, Bounds local,
            Vector3 boxScale, System.Random rng)
        {
            Vector3 box = new Vector3(Mathf.Abs(boxScale.x), Mathf.Abs(boxScale.y), Mathf.Abs(boxScale.z));

            bool alongX = box.x >= box.z;
            bool repeatX = entry.Fit == Fit.Tile || (entry.Fit == Fit.Wall && alongX);
            bool repeatZ = entry.Fit == Fit.Tile || (entry.Fit == Fit.Wall && !alongX);
            bool repeatY = entry.Fit != Fit.Tile;

            // Стенка сама доворачивается тонкой стороной к тонкой стороне
            // коробки. Иначе каталог пришлось бы писать дважды — отдельно для
            // бортика вдоль X и отдельно для того же бортика вдоль Z, и первая
            // же перепутанная четверть ужала бы панель до шестой доли размера.
            int yawSteps = entry.YawSteps + (entry.Fit == Fit.Wall && !alongX ? 1 : 0);
            Vector3 model = SwapForYaw(local.size, yawSteps);
            if (model.x <= Mathf.Epsilon || model.y <= Mathf.Epsilon || model.z <= Mathf.Epsilon)
            {
                return;
            }

            // Масштаб общий на три оси: по отдельности он расплющил бы модель.
            float shrink = 1f;
            if (!repeatX) shrink = Mathf.Min(shrink, box.x / model.x);
            if (!repeatY) shrink = Mathf.Min(shrink, box.y / model.y);
            if (!repeatZ) shrink = Mathf.Min(shrink, box.z / model.z);
            shrink = Mathf.Min(shrink, 1f);

            int countX, countY, countZ;
            float fillX, fillY, fillZ;
            CountPieces(box.x, model.x * shrink, repeatX, out countX, out fillX);
            CountPieces(box.y, model.y * shrink, repeatY, out countY, out fillY);
            CountPieces(box.z, model.z * shrink, repeatZ, out countZ, out fillZ);

            var scale = new Vector3(shrink * fillX, shrink * fillY, shrink * fillZ);

            // По оси без повтора модель прижимается к верхней грани, если она
            // тоньше коробки: настил видно сверху, и висеть ему посреди толщи
            // нельзя — по нему ходят.
            float restY = repeatY ? 0f : 0.5f - model.y * scale.y / (2f * Mathf.Max(0.0001f, box.y));

            for (int x = 0; x < countX; x++)
            {
                for (int y = 0; y < countY; y++)
                {
                    for (int z = 0; z < countZ; z++)
                    {
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                        StripColliders(go);

                        // Разворот через раз — но только на пол-оборота: четверть
                        // поменяла бы оси местами, и посчитанный масштаб ушёл бы
                        // не туда.
                        int yaw = yawSteps + (rng.Next(2) == 0 ? 0 : 2);
                        var offset = new Vector3(
                            repeatX ? (x + 0.5f) / countX - 0.5f : 0f,
                            repeatY ? (y + 0.5f) / countY - 0.5f : restY,
                            repeatZ ? (z + 0.5f) / countZ - 0.5f : 0f);

                        ApplyTransform(go.transform, yaw, local, boxScale, SwapForYaw(scale, yaw), offset);
                    }
                }
            }
        }

        /// <summary>
        /// Сколько копий влезает в длину и насколько их приходится подтянуть.
        /// Подтяжка ограничена <see cref="StretchLimit"/> — за ним ставится
        /// лишняя копия, а не тянется существующая.
        /// </summary>
        private static void CountPieces(float boxLength, float pieceLength, bool repeat, out int count, out float fill)
        {
            if (!repeat || pieceLength <= 0.0001f)
            {
                count = 1;
                fill = 1f;
                return;
            }

            count = Mathf.Max(1, Mathf.RoundToInt(boxLength / pieceLength));
            fill = boxLength / (count * pieceLength);
            if (fill > StretchLimit)
            {
                count += 1;
                fill = boxLength / (count * pieceLength);
            }

            fill = Mathf.Clamp(fill, 1f / StretchLimit, StretchLimit);
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
        /// другой формы поверх выверенной, и приземление стало бы лотереей.
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
