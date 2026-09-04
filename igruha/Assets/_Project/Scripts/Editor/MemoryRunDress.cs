using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.MemoryRun;
using Entry = Igruha.EditorTools.DressKit.Entry;
using Fit = Igruha.EditorTools.DressKit.Fit;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Дресс «Рейса на память» — подфаза 4.1.
    ///
    /// Устроен не так, как дресс остальных мини-игр, и причина одна: здесь
    /// <b>тридцать объектов обязаны быть неразличимы между собой</b>. Это не
    /// пожелание художника, а правило игры: вся механика держится на том, что
    /// по внешнему виду плиты нельзя сказать ничего. Любая примета — своя
    /// модель, свой поворот, своё пятно — превращает «запомни маршрут» в
    /// «найди особую царапину».
    ///
    /// Отсюда три решения, каждое против обычной практики арт-фазы:
    ///
    /// <list type="number">
    /// <item><b>Плита не берётся из пака.</b> Металлической плиты 2.88 × 2.88 в
    /// двенадцати паках нет вовсе (проверено индексом по 8198 префабам и
    /// контакт-листом <c>MemoryRun_Plate.png</c>). Ближайшие — бетонные
    /// перекрытия <c>SM_Bld_Concrete_Floor_01</c> и настилы — требуют
    /// неравномерного масштаба и несут в атласе нарисованные потёки и грязь,
    /// то есть готовую примету. Тридцати одинаковым плитам нужна гарантия,
    /// а не сходство.</item>
    ///
    /// <item><b>Плита — один общий меш-ассет, а не тридцать сборок из
    /// примитивов.</b> Так неразличимость проверяется ссылкой, а не глазом:
    /// аудит сравнивает <c>sharedMesh</c> и <c>sharedMaterial</c> тридцати
    /// рендереров и обязан получить один объект на всех. Собранная на месте
    /// геометрия такой проверки не даёт.</item>
    ///
    /// <item><b>У дресса нет ни одного случайного числа.</b> В остальных играх
    /// дресс держит свой ГПСЧ, чтобы не сдвинуть последовательность билдера;
    /// здесь генератор не нужен вовсе — выбирать не из чего. Отсутствие
    /// <c>System.Random</c> в этом файле намеренное, и добавлять его нельзя:
    /// «разнообразие» на маршруте здесь означает подсказку.</item>
    /// </list>
    ///
    /// <b>Износ и мелочь идут в цех, а не на маршрут.</b> Скилл <c>/mg-art</c>
    /// требует обратного — сыпать плоский мусор именно туда, где игрок проводит
    /// весь раунд. Здесь это требование не выполняется осознанно: наполнение
    /// зоны ожидания, стен и пропасти — работа подфазы 4.3.
    /// </summary>
    internal static class MemoryRunDress
    {
        /// <summary>Что именно одевается моделью пака.</summary>
        internal enum Kind
        {
            None,

            /// <summary>Настил, по которому ходят ногами: площадка ожидания и выходная площадка.</summary>
            Deck,

            /// <summary>Выходная дверь: цель, видимая от первого ряда.</summary>
            ExitDoor
        }

        private const string ConstructionBuildings = "Assets/Synty/PolygonConstruction/Prefabs/Buildings/";
        private const string ConstructionProps = "Assets/Synty/PolygonConstruction/Prefabs/Props/";

        /// <summary>Одевание одного вида: чем закрываем и зачем именно этим.</summary>
        private readonly struct Wear
        {
            public readonly string Title;
            public readonly Entry[] Entries;

            public Wear(string title, params Entry[] entries)
            {
                Title = title;
                Entries = entries;
            }
        }

        private static readonly Dictionary<Kind, Wear> Catalog = new Dictionary<Kind, Wear>
        {
            {
                // Настил стелется плитами перекрытия, а не красится: сетка даёт
                // швы и кромки, которых плоский тон не даёт вовсе. Плита пака
                // 5 × 0.5 × 5 при площадке 20.16 × 14.76 ложится сеткой 4 × 3,
                // подтяжка остаётся у единицы.
                Kind.Deck,
                new Wear("настил",
                    new Entry(ConstructionBuildings + "SM_Bld_Concrete_Floor_01.prefab", Fit.Tile))
            },
            {
                // Дверь — гофрированный стальной лист, а не дверь дома. Дверные
                // панели паков идут 2.50 × 2.90 и 1.00 × 1.97: первая шире
                // проёма, вторая ниже него, и обе тянутся за пределом 1.35.
                // Лист 2.63 × 2.45 садится в 1.73 × 2.16 с подтяжкой 0.66 × 0.88,
                // то есть неравномерность 1.34 — под пределом, и цех получает
                // ворота, а не подъезд.
                Kind.ExitDoor,
                new Wear("выходная дверь",
                    new Entry(ConstructionProps + "SM_Prop_Fence_MetalSheet_01.prefab", Fit.Stretch))
            }
        };

        // ─────────────────────────── плита ───────────────────────────

        private const string ArtFolder = "Assets/_Project/Art/MemoryRun";
        private const string PlateMeshPath = ArtFolder + "/MR_Plate.mesh";

        /// <summary>Толщина настила, по которому ходят. Верх настила — ровно на нуле.</summary>
        private const float DeckThickness = 0.04f;

        /// <summary>
        /// Насколько настил уже плиты с каждой стороны. Разница обнажает фланец
        /// и даёт видимую окантовку, на которой живут болты.
        /// </summary>
        private const float DeckInset = 0.14f;

        /// <summary>Толщина фланца — нижнего листа во всю ширину плиты.</summary>
        private const float FlangeThickness = 0.05f;

        /// <summary>Высота продольного ребра под фланцем: то, чем плита читается сбоку и снизу.</summary>
        private const float RibHeight = 0.20f;

        /// <summary>Ширина ребра.</summary>
        private const float RibWidth = 0.20f;

        /// <summary>Диаметр головки болта.</summary>
        private const float BoltDiameter = 0.10f;

        /// <summary>Высота головки болта над фланцем. Верх головки остаётся <b>ниже</b> настила.</summary>
        private const float BoltHeight = 0.035f;

        private static Mesh plateMesh;
        private static int platesDressed;
        private static int plateTriangles;

        /// <summary>Сбросить кэш габаритов, список ненайденного и счётчики перед пересборкой.</summary>
        internal static void Begin()
        {
            DressKit.Begin();
            plateMesh = null;
            platesDressed = 0;
            plateTriangles = 0;
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
        ///
        /// Генератор случайных чисел не принимается намеренно: в каталоге этой
        /// игры на каждый вид ровно одна модель, и выбор из набора здесь был бы
        /// источником различий там, где различий быть не должно.
        /// </summary>
        internal static GameObject Apply(GameObject box, Kind kind)
        {
            if (kind == Kind.None || !Catalog.TryGetValue(kind, out Wear wear))
            {
                return null;
            }

            // Пустой генератор с постоянным зерном: DressKit просит его в
            // сигнатуре, но выбирать здесь не из чего — на вид одна модель.
            GameObject dress = DressKit.Apply(box, wear.Entries, new System.Random(0), PaintOf(kind));
            ShrinkInsideBox(dress, box);
            return dress;
        }

        /// <summary>
        /// Каким тоном палитры красится модель этого вида.
        ///
        /// Модели пака идут <b>не</b> в родных материалах, и это отличие от
        /// «Переноски предмета». Там пак давал ровно тот цвет, что просил бриф;
        /// здесь атлас Construction тёплый и песочный, и на кадрах 4.1 настил
        /// площадок читался землёй, а не бетоном цеха.
        /// </summary>
        private static Material PaintOf(Kind kind)
        {
            switch (kind)
            {
                case Kind.Deck:
                    return MemoryRunPalette.Get(MemoryRunPalette.Tone.Deck);
                case Kind.ExitDoor:
                    return MemoryRunPalette.Get(MemoryRunPalette.Tone.Door);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Подтянуть одетую модель внутрь коробки, если сетка копий вышла за её
        /// габарит. Масштаб правится по каждой оси отдельно и только вниз.
        ///
        /// Нужно из-за того, как сетка ложится на некруглые размеры: выходная
        /// площадка 5.76 м в глубину при плите настила 5 м даёт 1.15 плиты,
        /// и округление оставляло <b>4 см настила за коллайдером</b>. Наружу
        /// это выглядит козырьком пола ровно там, где приземляются с десятого
        /// ряда: игрок видит опору, а физики под ней нет.
        ///
        /// Правка сделана здесь, а не в <see cref="DressKit"/>, намеренно: кит
        /// общий на все мини-игры и правится сейчас тремя машинами разом,
        /// а поправка на 1.4% нужна ровно этой площадке.
        ///
        /// ⚠️ <b>Габарит считается по мешам и матрицам, а не по
        /// <c>Renderer.bounds</c>.</b> Первая версия брала именно его — и
        /// ужала оба настила до метра в поперечнике: у объекта, созданного тем
        /// же кадром, границы рендерера ещё не посчитаны, и деление на них дало
        /// поправку в двадцать раз. Матрицы трансформов, в отличие от границ
        /// рендерера, обновляются сразу при присваивании.
        /// </summary>
        private static void ShrinkInsideBox(GameObject dress, GameObject box)
        {
            if (dress == null || box == null || !TryLocalBounds(dress, out Bounds art))
            {
                return;
            }

            // Держатель дресса стоит в локальных координатах коробки без
            // собственного масштаба, а коробка — единичный куб. Значит
            // разрешённая область ровно ±0.5 по каждой оси.
            const float half = 0.5f;
            Vector3 taken = art.extents;
            var fix = new Vector3(
                taken.x > half + 0.0001f ? half / taken.x : 1f,
                taken.y > half + 0.0001f ? half / taken.y : 1f,
                taken.z > half + 0.0001f ? half / taken.z : 1f);

            if (Mathf.Approximately(fix.x, 1f) && Mathf.Approximately(fix.y, 1f) &&
                Mathf.Approximately(fix.z, 1f))
            {
                return;
            }

            Vector3 scale = dress.transform.localScale;
            dress.transform.localScale = new Vector3(scale.x * fix.x, scale.y * fix.y, scale.z * fix.z);
        }

        /// <summary>
        /// Габарит одетой модели в системе координат держателя — по мешам,
        /// без обращения к <c>Renderer.bounds</c>. Восемь углов габарита каждого
        /// меша прогоняются через матрицу, и это даёт верный ответ в том же
        /// кадре, в котором объект создан.
        /// </summary>
        private static bool TryLocalBounds(GameObject holder, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            Matrix4x4 toHolder = holder.transform.worldToLocalMatrix;

            foreach (var filter in holder.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                Matrix4x4 matrix = toHolder * filter.transform.localToWorldMatrix;
                Bounds local = mesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3(
                        (corner & 1) == 0 ? local.min.x : local.max.x,
                        (corner & 2) == 0 ? local.min.y : local.max.y,
                        (corner & 4) == 0 ? local.min.z : local.max.z);

                    Vector3 world = matrix.MultiplyPoint3x4(point);
                    if (!any)
                    {
                        bounds = new Bounds(world, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(world);
                    }
                }
            }

            return any;
        }

        /// <summary>
        /// Одеть одну плиту. Все тридцать получают <b>один и тот же меш-ассет и
        /// один и тот же материал</b>, в нулевом повороте и единичном масштабе.
        ///
        /// Поворот выставляется явно, а не наследуется: коробка блокаута стоит
        /// без поворота, но полагаться на это нельзя — доворот, случайно
        /// заведённый в билдере, стал бы приметой ряда, а заметить его на
        /// тридцати серых квадратах почти невозможно.
        /// </summary>
        internal static void ApplyPlate(GameObject box)
        {
            if (box == null)
            {
                return;
            }

            Mesh mesh = GetPlateMesh();
            Material material = MemoryRunPalette.Get(MemoryRunPalette.Tone.Plate);
            if (mesh == null || material == null)
            {
                return;
            }

            var renderer = box.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }

            var holder = new GameObject("Dress", typeof(MeshFilter), typeof(MeshRenderer));
            holder.transform.SetParent(box.transform, false);

            // Коробка плиты растянута масштабом (2.88 × 0.36 × 2.88), а меш
            // построен в метрах. Локальный масштаб гасит масштаб родителя,
            // иначе плита раздулась бы в куб со стороной три метра.
            Vector3 parentScale = box.transform.lossyScale;
            holder.transform.localScale = new Vector3(
                1f / Mathf.Max(0.0001f, parentScale.x),
                1f / Mathf.Max(0.0001f, parentScale.y),
                1f / Mathf.Max(0.0001f, parentScale.z));

            // Пивот коробки — её центр, а меш построен от верхней грани.
            // Полтолщины вверх ставит верх листа ровно на поверхность ходьбы.
            holder.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            holder.transform.localRotation = Quaternion.identity;

            holder.GetComponent<MeshFilter>().sharedMesh = mesh;

            var holderRenderer = holder.GetComponent<MeshRenderer>();
            holderRenderer.sharedMaterial = material;
            holderRenderer.shadowCastingMode = ShadowCastingMode.On;

            holder.layer = LayerMask.NameToLayer("Default");
            platesDressed++;
        }

        /// <summary>
        /// Несущая конструкция под цепочкой: поперечная балка под каждым рядом
        /// и два продольных рельса вдоль стен.
        ///
        /// Существует затем, чтобы плиты не висели в пустоте необъяснимо.
        /// Первым вариантом были тросы от потолочных ферм — по четыре на плиту;
        /// на рендере 04.09 они дали <b>сто двадцать вертикальных линий поперёк
        /// кадра</b>, и маршрут перестал читаться. Балки держат ту же мысль
        /// и не засоряют кадр.
        ///
        /// <b>Балка одна и та же под всеми десятью рядами.</b> Разная — это
        /// метка ряда, то есть та же подсказка, что царапина на плите.
        ///
        /// Коллайдеров у конструкции нет: прыжок между рядами выверен фазой 2,
        /// и лишняя поверхность под ногами сделала бы приземление лотереей.
        /// </summary>
        internal static void BuildRowStructure(Transform parent, MemoryRunConfig config)
        {
            Material material = MemoryRunPalette.Get(MemoryRunPalette.Tone.Structure);
            if (parent == null || config == null || material == null)
            {
                return;
            }

            var root = new GameObject("RowStructure");
            root.transform.SetParent(parent, false);
            root.layer = LayerMask.NameToLayer("Default");

            // Балка проходит ниже настила, но выше нижней грани плиты: сбоку
            // видно, что плита на чём-то лежит, а сверху балка не мешает.
            const float beamTop = -DeckThickness;
            const float beamHeight = 0.24f;
            const float railSection = 0.30f;

            float railX = config.HallWidth * 0.5f - 0.5f;
            float beamLength = railX * 2f;
            float chainFrom = config.ChainStartZ;
            float chainTo = config.ChainStartZ + config.ChainDepth;

            for (int step = 0; step < config.Steps; step++)
            {
                CreateBeam(root.transform, $"Beam_{step:00}",
                    new Vector3(beamLength, beamHeight, 0.34f),
                    new Vector3(0f, beamTop - beamHeight * 0.5f, config.StepZ(step)),
                    material);
            }

            CreateBeam(root.transform, "Rail_Left",
                new Vector3(railSection, railSection, chainTo - chainFrom),
                new Vector3(-railX, beamTop - beamHeight - railSection * 0.5f, (chainFrom + chainTo) * 0.5f),
                material);

            CreateBeam(root.transform, "Rail_Right",
                new Vector3(railSection, railSection, chainTo - chainFrom),
                new Vector3(railX, beamTop - beamHeight - railSection * 0.5f, (chainFrom + chainTo) * 0.5f),
                material);
        }

        private static void CreateBeam(Transform parent, string name, Vector3 size, Vector3 position, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;

            // Коллайдер примитива срезается сразу: конструкция декоративна,
            // столкновения на маршруте держат коробки плит из блокаута.
            Object.DestroyImmediate(go.GetComponent<Collider>());

            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.layer = LayerMask.NameToLayer("Default");

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>
        /// Меш плиты: фланец во всю ширину, настил чуть уже него, четыре ребра
        /// снизу по периметру, два ребра крест-накрест и четыре головки болтов
        /// на обнажённой окантовке фланца.
        ///
        /// <b>Ни одна деталь не поднимается выше поверхности ходьбы.</b> Первая
        /// версия ставила головки болтов на 2.5 см над настилом, и аудит 4.1
        /// назвал это тем, чем оно и было: арт шире физики. Игрок стоит на
        /// верхней грани коробки, коллайдера у головки нет — ступня входила бы
        /// в болт. Окантовка решает то же самое честно: болты сидят на фланце,
        /// на 4 см ниже настила, и видны ровно оттуда, откуда на плиту смотрят.
        ///
        /// Рёбра идут <b>под</b> плитой по той же причине: борт поверх настила
        /// читался бы порожком, через который надо перешагнуть, а перешагнуть
        /// его нельзя — в физике его нет. Снизу ребро видно с соседней плиты
        /// и из пропасти.
        ///
        /// Меш строится один раз и кладётся ассетом: тридцать рендереров
        /// ссылаются на него же, и аудиту достаточно сравнить ссылки.
        /// </summary>
        internal static Mesh GetPlateMesh()
        {
            if (plateMesh != null)
            {
                return plateMesh;
            }

            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            Mesh cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            if (cube == null || cylinder == null)
            {
                Debug.LogError("Встроенные примитивы Unity не найдены — плиту не из чего собрать");
                return null;
            }

            MemoryRunConfig config = FindConfig();
            float side = config != null ? config.PlateSize : 2.88f;
            float thickness = config != null ? config.PlateThickness : 0.36f;

            var parts = new List<CombineInstance>(12);

            // Настил: верхняя грань ровно на нуле, ширина — на окантовку уже плиты.
            float deckSide = side - DeckInset * 2f;
            Add(parts, cube,
                new Vector3(0f, -DeckThickness * 0.5f, 0f),
                Quaternion.identity,
                new Vector3(deckSide, DeckThickness, deckSide));

            // Фланец во всю ширину плиты. Его верх — окантовка, видимая вокруг
            // настила; ниже него начинается всё остальное.
            float flangeTop = -DeckThickness;
            Add(parts, cube,
                new Vector3(0f, flangeTop - FlangeThickness * 0.5f, 0f),
                Quaternion.identity,
                new Vector3(side, FlangeThickness, side));

            // Рёбра по периметру снизу. Вынесены внутрь на полширины ребра,
            // чтобы не выступать за габарит коробки блокаута ни на миллиметр:
            // аудит проверяет именно это.
            float ribTop = flangeTop - FlangeThickness;
            float ribY = ribTop - RibHeight * 0.5f;
            float ribOffset = side * 0.5f - RibWidth * 0.5f;
            Add(parts, cube, new Vector3(0f, ribY, ribOffset), Quaternion.identity,
                new Vector3(side, RibHeight, RibWidth));
            Add(parts, cube, new Vector3(0f, ribY, -ribOffset), Quaternion.identity,
                new Vector3(side, RibHeight, RibWidth));
            Add(parts, cube, new Vector3(ribOffset, ribY, 0f), Quaternion.identity,
                new Vector3(RibWidth, RibHeight, side - RibWidth * 2f));
            Add(parts, cube, new Vector3(-ribOffset, ribY, 0f), Quaternion.identity,
                new Vector3(RibWidth, RibHeight, side - RibWidth * 2f));

            // Крест снизу: видно из пропасти и с соседней плиты.
            float crossHeight = RibHeight * 0.7f;
            float crossY = ribTop - crossHeight * 0.5f;
            Add(parts, cube, new Vector3(0f, crossY, 0f), Quaternion.identity,
                new Vector3(side - RibWidth * 2f, crossHeight, RibWidth * 0.8f));
            Add(parts, cube, new Vector3(0f, crossY, 0f), Quaternion.identity,
                new Vector3(RibWidth * 0.8f, crossHeight, side - RibWidth * 2f));

            // Головки болтов сидят на окантовке фланца, по углам. Верх головки
            // на flangeTop + BoltHeight, то есть на полсантиметра ниже настила:
            // над поверхностью ходьбы не выступает ничего.
            //
            // ⚠️ Встроенный цилиндр Unity — <b>диаметр 2 и высота 2</b>, а не
            // диаметр 1, как подсказывает вид примитива в сцене (замерено:
            // extents 1 × 1 × 1). Отсюда половина и в поперечнике, и в высоте.
            // Первая версия делила только высоту, болты выходили вдвое толще
            // и уводили габарит меша за коробку на 3 см с каждой стороны.
            float boltOffset = side * 0.5f - DeckInset * 0.5f;
            var boltScale = new Vector3(BoltDiameter * 0.5f, BoltHeight * 0.5f, BoltDiameter * 0.5f);
            float boltY = flangeTop + BoltHeight * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                float x = (i < 2 ? -1f : 1f) * boltOffset;
                float z = (i % 2 == 0 ? -1f : 1f) * boltOffset;
                Add(parts, cylinder, new Vector3(x, boltY, z), Quaternion.identity, boltScale);
            }

            var mesh = new Mesh { name = "MR_Plate" };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            mesh.RecalculateBounds();

            // Габарит собранного меша обязан лежать внутри коробки блокаута:
            // выступ означает, что плита стала шире своей физики, и игрок будет
            // проваливаться там, где видит опору.
            Bounds bounds = mesh.bounds;
            if (bounds.size.x > side + 0.001f || bounds.size.z > side + 0.001f ||
                bounds.min.y < -thickness - 0.001f || bounds.max.y > 0.001f)
            {
                Debug.LogWarning(
                    $"Меш плиты вышел за коробку: {bounds.size.x:F3} × {bounds.size.y:F3} × {bounds.size.z:F3}, " +
                    $"верх на {bounds.max.y:F3} при коробке {side:F2} × {thickness:F2} × {side:F2} и верхе на нуле");
            }

            EnsureArtFolder();
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(PlateMeshPath);
            if (existing != null)
            {
                // ⚠️ Ассет переписывается, только если геометрия правда другая.
                //
                // Проверено 04.09: безусловная перезапись меняет байты файла
                // при неизменной геометрии, и каждая пересборка арены помечала
                // меш изменённым. В репозитории, где сходятся три ветки, это
                // лишний конфликт на ровном месте — файл двоичный и слияние
                // его не переживает.
                //
                // Содержимое, а не сам ассет: GUID остаётся тем же, и ссылки
                // из сцены не рвутся.
                if (SameGeometry(existing, mesh))
                {
                    Object.DestroyImmediate(mesh);
                    plateMesh = existing;
                }
                else
                {
                    existing.Clear();
                    existing.SetVertices(new List<Vector3>(mesh.vertices));
                    existing.SetNormals(new List<Vector3>(mesh.normals));
                    existing.SetUVs(0, new List<Vector2>(mesh.uv));
                    existing.SetTriangles(mesh.triangles, 0);
                    existing.RecalculateBounds();
                    EditorUtility.SetDirty(existing);
                    Object.DestroyImmediate(mesh);
                    plateMesh = existing;
                }
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, PlateMeshPath);
                plateMesh = mesh;
            }

            plateTriangles = plateMesh.triangles.Length / 3;
            return plateMesh;
        }

        /// <summary>
        /// Совпадает ли геометрия двух мешей. Сравниваются число вершин,
        /// число треугольников и сами координаты с точностью до десятой доли
        /// миллиметра: разница мельче этого невидима и переписывать ассет
        /// ради неё незачем.
        /// </summary>
        private static bool SameGeometry(Mesh a, Mesh b)
        {
            if (a == null || b == null || a.vertexCount != b.vertexCount)
            {
                return false;
            }

            int[] at = a.triangles;
            int[] bt = b.triangles;
            if (at.Length != bt.Length)
            {
                return false;
            }

            for (int i = 0; i < at.Length; i++)
            {
                if (at[i] != bt[i])
                {
                    return false;
                }
            }

            Vector3[] av = a.vertices;
            Vector3[] bv = b.vertices;
            for (int i = 0; i < av.Length; i++)
            {
                if ((av[i] - bv[i]).sqrMagnitude > 1e-8f)
                {
                    return false;
                }
            }

            return true;
        }

        private static void Add(List<CombineInstance> parts, Mesh mesh, Vector3 position, Quaternion rotation,
            Vector3 scale)
        {
            parts.Add(new CombineInstance
            {
                mesh = mesh,
                transform = Matrix4x4.TRS(position, rotation, scale)
            });
        }

        private static void EnsureArtFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Art"))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "Art");
            }

            if (!AssetDatabase.IsValidFolder(ArtFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Art", "MemoryRun");
            }
        }

        private static MemoryRunConfig FindConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:MemoryRunConfig");
            return guids.Length == 0
                ? null
                : AssetDatabase.LoadAssetAtPath<MemoryRunConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        /// <summary>
        /// Таблица «вид → модель → габарит» и числа плиты. Печатается
        /// пересборкой, потому что подбор модели обязан быть проверяемым
        /// числом, а не словом «подобрал».
        /// </summary>
        internal static string Report()
        {
            var report = new StringBuilder();
            report.Append("🧨 «Рейс на память», дресс 4.1");
            report.Append("\n— плита         MR_Plate.mesh, ").Append(plateTriangles)
                .Append(" тр., одета копий: ").Append(platesDressed)
                .Append(" (меш и материал общие на все — этим и держится неразличимость)");

            foreach (KeyValuePair<Kind, Wear> pair in Catalog)
            {
                Wear wear = pair.Value;
                report.Append("\n— ").Append(wear.Title.PadRight(14));

                for (int i = 0; i < wear.Entries.Length; i++)
                {
                    string path = wear.Entries[i].Prefab;
                    string shortName = path.Substring(path.LastIndexOf('/') + 1).Replace(".prefab", string.Empty);
                    report.Append(i == 0 ? string.Empty : ", ").Append(shortName);

                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
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
    }
}
