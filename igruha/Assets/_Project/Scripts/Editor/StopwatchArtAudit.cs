using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Igruha.Minigames.Circus;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Замеры арта цирковой арены — приёмка подфаз 4.1–4.6 числами.
    ///
    /// Существует потому, что ровно этих чисел не хватило Duck Hunt: лёд лежал
    /// над пропастями, стог стоял поперёк этажа, декор висел в воздухе — всё
    /// ловится замером до того, как геймдизайнер откроет сцену.
    ///
    /// <b>Гоняется на обеих сценах.</b> Арена общая: она лежит и в
    /// <c>Stopwatch.unity</c>, и в <c>CansOrder.unity</c>. Проверки арены
    /// одинаковы для обеих, а реквизит клетки — свой у каждой, и его наличие
    /// проверяется по тому, что в сцене нашлось. Зелёный отчёт на одной сцене
    /// ничего не говорит о второй, и именно так `DuckHunt.unity` уехала
    /// в сборку с 73 ошибками.
    ///
    /// <b>Главная проверка здесь — читаемость высот.</b> Восемь клеток на
    /// цепях разной длины и есть весь интерфейс этой игры: другого способа
    /// понять, кто у края, у игрока нет. Поэтому просвет между клетками и
    /// просвет между клеткой и ямой проверяются числом, а не глазом.
    /// </summary>
    internal static class StopwatchArtAudit
    {
        private const string ConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/CircusArenaConfig.asset";

        /// <summary>Имя, которым <see cref="DressKit"/> называет корень надетой модели.</summary>
        private const string DressRoot = "Dress";

        /// <summary>Допуск на «стоит на полу» и «сидит в коробке», м: два сантиметра не видно.</summary>
        private const float Tolerance = 0.02f;

        /// <summary>Корни, которые вообще составляют арену. Ищем по ним, а не по всей сцене.</summary>
        private static readonly string[] Roots = { "_Arena", "_Spawns", "_Bounds", "_Traps", "_Pickups" };

        [MenuItem("Igruha/Цирк/Замеры арта")]
        private static void Measure()
        {
            var config = AssetDatabase.LoadAssetAtPath<CircusArenaConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogError("Замеры арта: не найден " + ConfigPath);
                return;
            }

            var roots = new List<GameObject>();
            for (int i = 0; i < Roots.Length; i++)
            {
                GameObject go = GameObject.Find(Roots[i]);
                if (go != null)
                {
                    roots.Add(go);
                }
            }

            if (roots.Count == 0)
            {
                Debug.LogError("Замеры арта: открой Stopwatch.unity или CansOrder.unity — корней арены не найдено.");
                return;
            }

            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            var report = new StringBuilder();
            report.Append("📏 Цирковая арена — замеры арта, сцена «").Append(scene).Append('»');

            MeasureDress(roots, report);
            MeasureScenery(roots, report);
            MeasureColliders(roots, report);
            MeasurePackMaterials(roots, report);
            MeasureMeshes(roots, report);
            MeasureReadability(config, report);
            MeasureGrounding(config, report);
            MeasureCansOrder(report);

            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Реквизит «Порядка банок». Молчит в сцене «Секундомера»: там нет
        /// ни одной полки, и блок не печатается вовсе.
        ///
        /// <b>Банки в сохранённой сцене нет — и не будет.</b> Их число берётся
        /// из таблицы конфига и меняется от круга к кругу, поэтому полка
        /// создаёт их в рантайме. Значит замерять надо не сцену, а то, из чего
        /// они родятся: ссылку на префаб, знаки в нём и отсутствие коллайдера.
        /// </summary>
        private static void MeasureCansOrder(StringBuilder report)
        {
            var shelves = Object.FindObjectsByType<Igruha.Minigames.CansOrder.CanShelf>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (shelves.Length == 0)
            {
                return;
            }

            int wired = 0;
            GameObject canPrefab = null;
            for (int i = 0; i < shelves.Length; i++)
            {
                var serialized = new SerializedObject(shelves[i]);
                var prefab = serialized.FindProperty("canPrefab").objectReferenceValue as GameObject;
                if (prefab != null)
                {
                    wired++;
                    canPrefab = prefab;
                }
            }

            report.Append("\n— полок ").Append(shelves.Length)
                .Append(", с префабом банки ").Append(wired)
                .Append(wired == shelves.Length ? " ✅" : " ❌ (банка родится серым цилиндром)");

            if (canPrefab == null)
            {
                return;
            }

            int symbols = 0;
            var canSerialized = new SerializedObject(canPrefab.GetComponent<Igruha.Minigames.CansOrder.Can>());
            SerializedProperty array = canSerialized.FindProperty("symbols");
            for (int i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).objectReferenceValue != null)
                {
                    symbols++;
                }
            }

            var config = AssetDatabase.LoadAssetAtPath<Igruha.Minigames.CansOrder.CansOrderConfig>(
                "Assets/_Project/Settings/Gameplay/Minigames/CansOrderConfig.asset");
            int expected = config != null ? config.PaletteSize : symbols;

            // Знак — второй канал различения банок: по цвету дальтоник их
            // не различает вовсе (спека 14.2). Недостающий знак — это не
            // косметика, это неиграбельная банка.
            report.Append("\n— знаков на банке ").Append(symbols).Append(" из ").Append(expected)
                .Append(symbols == expected ? " ✅" : " ❌");

            // Коллайдера у банки быть не должно: буфер PlayerInteractor на 16,
            // в клетке уже семь своих, пять банок вытеснили бы кнопку.
            int canColliders = canPrefab.GetComponentsInChildren<Collider>(true).Length;
            report.Append("\n— коллайдеров у банки ").Append(canColliders)
                .Append(canColliders == 0 ? " ✅" : " ❌ (вытеснят кнопку из выборки интерактива)");

            var panel = Object.FindAnyObjectByType<Igruha.Minigames.CansOrder.CanOrderArrangementPanel>(
                FindObjectsInactive.Include);
            report.Append("\n— строки расстановок на табло: ")
                .Append(panel != null ? "есть, по " + panel.Capacity + " на грань ✅" : "НЕТ ❌");

            var board = Object.FindAnyObjectByType<Igruha.Minigames.CansOrder.CanOrderBoard>(
                FindObjectsInactive.Include);
            report.Append("\n— табло подключено к игре: ")
                .Append(board != null && board.HasBoard ? "да ✅" : "НЕТ ❌ (не покажет ни задания, ни результатов)");
        }

        /// <summary>
        /// Дресс: коллайдеров в нём быть не должно вовсе, за габариты своей
        /// коробки он выходить не имеет права.
        /// </summary>
        private static void MeasureDress(List<GameObject> roots, StringBuilder report)
        {
            int dressed = 0;
            int colliders = 0;
            int overflowing = 0;
            float worstOverflow = 0f;
            string worstName = "—";

            for (int r = 0; r < roots.Count; r++)
            {
                Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].name != DressRoot || all[i].parent == null)
                    {
                        continue;
                    }

                    dressed++;
                    colliders += all[i].GetComponentsInChildren<Collider>(true).Length;

                    if (!TryBounds(all[i].gameObject, out Bounds model))
                    {
                        continue;
                    }

                    Transform box = all[i].parent;
                    var boxBounds = new Bounds(box.position, box.lossyScale);
                    float overflow = Mathf.Max(
                        Mathf.Max(boxBounds.min.x - model.min.x, model.max.x - boxBounds.max.x),
                        Mathf.Max(boxBounds.min.z - model.min.z, model.max.z - boxBounds.max.z));

                    if (overflow > Tolerance)
                    {
                        overflowing++;
                        if (overflow > worstOverflow)
                        {
                            worstOverflow = overflow;
                            worstName = box.name;
                        }
                    }
                }
            }

            report.Append("\n— дресс: одето коробок ").Append(dressed)
                .Append(", коллайдеров ").Append(Verdict(colliders == 0, colliders))
                .Append(", вылезает за коробку ").Append(Verdict(overflowing == 0, overflowing));

            if (overflowing > 0)
            {
                report.Append(" (худший ").Append(worstName).Append(' ')
                    .Append(worstOverflow.ToString("F2")).Append(" м)");
            }
        }

        /// <summary>
        /// Декор, поставленный мимо коробки: опилки, звенья цепи, бочки кнопок,
        /// гирлянды табло, модель медведя. Коллайдеров у него быть не должно —
        /// иначе выпавший приземлится на опилки вместо дна ямы, а цепь поймает
        /// бросок.
        /// </summary>
        private static void MeasureScenery(List<GameObject> roots, StringBuilder report)
        {
            int props = 0;
            int colliders = 0;
            int offDefault = 0;

            int defaultLayer = LayerMask.NameToLayer("Default");
            for (int r = 0; r < roots.Count; r++)
            {
                Renderer[] all = roots[r].GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (!IsPackModel(all[i].transform))
                    {
                        continue;
                    }

                    props++;
                    if (all[i].GetComponent<Collider>() != null)
                    {
                        colliders++;
                    }

                    if (all[i].gameObject.layer != defaultLayer)
                    {
                        offDefault++;
                    }
                }
            }

            report.Append("\n— модели пака: рендереров ").Append(props)
                .Append(", коллайдеров ").Append(Verdict(colliders == 0, colliders))
                .Append(", не на Default ").Append(Verdict(offDefault == 0, offDefault));
        }

        /// <summary>
        /// Коллайдеры на слоях сплошной геометрии. Их число обязано совпасть
        /// с блокаутом: арт не имеет права ни добавить преграду, ни убрать её.
        ///
        /// Числа блокаута замерены 04.09 до дресса: 48 сегментов борта,
        /// 48 пола шатра, 32 стены, 8 клеток по 8 коллайдеров, дно ямы.
        /// </summary>
        private static void MeasureColliders(List<GameObject> roots, StringBuilder report)
        {
            int ground = LayerMask.NameToLayer("Ground");
            int cover = LayerMask.NameToLayer("Cover");
            int solid = 0;
            int triggers = 0;

            for (int r = 0; r < roots.Count; r++)
            {
                Collider[] all = roots[r].GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].isTrigger)
                    {
                        triggers++;
                        continue;
                    }

                    int layer = all[i].gameObject.layer;
                    if (layer == ground || layer == cover)
                    {
                        solid++;
                    }
                }
            }

            // Замерено на блокауте 04.09, до дресса: 48 сегментов борта ямы,
            // 48 пола шатра, 32 стены шатра, дно ямы и по 6 на клетку (крыша,
            // три стены с прутьями и две створки пола). Внешняя стена клетки
            // не считается — она намеренно уведена на Ignore Raycast, чтобы
            // деоклюдер камеры не вжимал кадр внутрь клетки.
            const int BlockoutSolid = 177;
            report.Append("\n— коллайдеры Ground/Cover: ").Append(solid)
                .Append(" при блокауте ").Append(BlockoutSolid).Append(' ')
                .Append(Verdict(solid == BlockoutSolid, Mathf.Abs(solid - BlockoutSolid)))
                .Append(", триггеров ").Append(triggers);
        }

        /// <summary>
        /// Слоты с материалами из <c>Assets/Synty</c>. На 4.1–4.5 их быть
        /// должно много — это и есть дресс; на 4.6 после запекания обязан
        /// остаться ноль, иначе сцена разваливается у всех, кто не импортировал
        /// те же паки, и выпадает из сборки.
        /// </summary>
        private static void MeasurePackMaterials(List<GameObject> roots, StringBuilder report)
        {
            int packSlots = 0;
            for (int r = 0; r < roots.Count; r++)
            {
                Renderer[] all = roots[r].GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    Material[] materials = all[i].sharedMaterials;
                    for (int m = 0; m < materials.Length; m++)
                    {
                        if (materials[m] == null)
                        {
                            continue;
                        }

                        string path = AssetDatabase.GetAssetPath(materials[m]);
                        if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/Synty"))
                        {
                            packSlots++;
                        }
                    }
                }
            }

            report.Append("\n— слотов с материалами пака: ").Append(packSlots)
                .Append(packSlots == 0 ? " ✅ (запечено)" : " — норма до 4.6, после запекания обязан быть 0");
        }

        private static void MeasureMeshes(List<GameObject> roots, StringBuilder report)
        {
            int renderers = 0;
            int triangles = 0;

            for (int r = 0; r < roots.Count; r++)
            {
                renderers += roots[r].GetComponentsInChildren<MeshRenderer>(true).Length;
                MeshFilter[] filters = roots[r].GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < filters.Length; i++)
                {
                    if (filters[i].sharedMesh != null)
                    {
                        triangles += filters[i].sharedMesh.triangles.Length / 3;
                    }
                }
            }

            report.Append("\n— видимых мешей ").Append(renderers)
                .Append(", треугольников ").Append(triangles.ToString("N0"));
        }

        /// <summary>
        /// Читаемость высот — главная проверка этой игры.
        ///
        /// Просвет между соседними клетками и просвет между клеткой и ямой
        /// обязаны остаться пустыми: по ним игрок сравнивает свою высоту
        /// с чужой, и любой декор поперёк отменяет весь интерфейс. Проверяется
        /// числом, потому что глазом на восьми клетках это не ловится —
        /// поставить одну растяжку между двумя лучами фермы кажется безобидным.
        /// </summary>
        private static void MeasureReadability(CircusArenaConfig config, StringBuilder report)
        {
            GameObject cages = GameObject.Find("_Arena/Cages");
            if (cages == null)
            {
                report.Append("\n— ⚠️ читаемость: не найден _Arena/Cages");
                return;
            }

            var stations = cages.GetComponentsInChildren<CageStation>(true);
            int withProp = 0;
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i].PropSlot != null && stations[i].PropSlot.childCount > 0)
                {
                    withProp++;
                }
            }

            report.Append("\n— клеток ").Append(stations.Length)
                .Append(", с реквизитом ").Append(withProp)
                .Append(withProp == stations.Length ? " ✅" : " ❌");

            // Что стоит в просветах между клетками. Полоса берётся ровно по
            // пятну самих клеток (кольцо ± половина клетки), а не шире:
            // с запасом в целую клетку в неё попадал кирпичный борт ямы —
            // он стоит на своём месте и ничего не загораживает.
            float innerR = config.CageRingRadius - config.CageInnerSize * 0.5f;
            float outerR = config.CageRingRadius + config.CageInnerSize * 0.5f;
            float lowY = config.GetCageBottomHeight(0);
            float highY = config.GetCageBottomHeight(config.MaxLevelSteps) + config.CageInnerHeight;

            GameObject arena = GameObject.Find("_Arena");
            int blockers = 0;
            string firstBlocker = "—";
            Renderer[] all = arena.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i].transform;
                if (IsUnder(t, "Cages") || IsUnder(t, "Chains") || IsUnder(t, "Scoreboard"))
                {
                    continue;
                }

                Vector3 p = all[i].bounds.center;
                float radius = new Vector2(p.x, p.z).magnitude;
                if (radius < innerR || radius > outerR || p.y < lowY || p.y > highY)
                {
                    continue;
                }

                blockers++;
                if (firstBlocker == "—")
                {
                    firstBlocker = t.name;
                }
            }

            report.Append("\n— в просветах кольца клеток (R ").Append(innerR.ToString("F1")).Append('–')
                .Append(outerR.ToString("F1")).Append(" м, Y ").Append(lowY.ToString("F1")).Append('–')
                .Append(highY.ToString("F1")).Append(" м): ").Append(Verdict(blockers == 0, blockers));

            if (blockers > 0)
            {
                report.Append(" (первый — ").Append(firstBlocker).Append(')');
            }
        }

        /// <summary>
        /// Ничего не висит в воздухе и ничто не мешает падению.
        ///
        /// Опилки обязаны лежать на дне ямы, а не над ним: диск в двадцати
        /// сантиметрах над дном — это ровно тот лёд над пропастями, которым
        /// прославился Duck Hunt. Дно ямы обязано быть выше зоны выбывания,
        /// иначе выпавший умирает вместо забега от медведя.
        /// </summary>
        private static void MeasureGrounding(CircusArenaConfig config, StringBuilder report)
        {
            GameObject sawdust = GameObject.Find("_Arena/Sawdust");
            int floating = 0;
            float worstGap = 0f;

            if (sawdust != null)
            {
                foreach (Transform disc in sawdust.transform)
                {
                    if (!TryBounds(disc.gameObject, out Bounds b))
                    {
                        continue;
                    }

                    float gap = Mathf.Abs(b.min.y);
                    if (gap > Tolerance + 0.02f)
                    {
                        floating++;
                        worstGap = Mathf.Max(worstGap, gap);
                    }
                }
            }

            report.Append("\n— опилки: дисков ").Append(sawdust == null ? 0 : sawdust.transform.childCount)
                .Append(", висит в воздухе ").Append(Verdict(floating == 0, floating));
            if (floating > 0)
            {
                report.Append(" (худший зазор ").Append(worstGap.ToString("F2")).Append(" м)");
            }

            GameObject bounds = GameObject.Find("_Bounds");
            if (bounds != null)
            {
                var zones = bounds.GetComponentsInChildren<Igruha.Core.Arena.KillZone>(true);
                float highest = float.NegativeInfinity;
                for (int i = 0; i < zones.Length; i++)
                {
                    var col = zones[i].GetComponent<Collider>();
                    if (col != null)
                    {
                        highest = Mathf.Max(highest, col.bounds.max.y);
                    }
                }

                bool safe = highest < -Tolerance;
                report.Append("\n— верх зоны выбывания ").Append(highest.ToString("F2"))
                    .Append(" м при дне ямы 0.00 — ").Append(safe ? "✅" : "❌ дно ниже зоны выбывания");
            }

            GameObject bear = GameObject.Find("_Arena/PitBear");
            if (bear == null)
            {
                return;
            }

            var capsule = bear.GetComponent<CapsuleCollider>();
            bool hasModel = bear.transform.Find("Visual/BearModel") != null;
            if (capsule != null && TryBounds(bear, out Bounds bearBounds))
            {
                float visual = bearBounds.size.y;
                float physical = capsule.height * bear.transform.lossyScale.y;
                bool matches = Mathf.Abs(visual - physical) < 0.35f;
                report.Append("\n— медведь: вид ").Append(hasModel ? "модель" : "коробки")
                    .Append(", рост ").Append(visual.ToString("F2"))
                    .Append(" м, капсула ").Append(physical.ToString("F2"))
                    .Append(" м — ").Append(matches ? "✅ совпадают" : "❌ коллайдер не по фигуре");
            }
        }

        /// <summary>Модель пака узнаётся по префабу-источнику: наши примитивы источника не имеют.</summary>
        private static bool IsPackModel(Transform t)
        {
            Object source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject);
            if (source == null)
            {
                return false;
            }

            string path = AssetDatabase.GetAssetPath(source);
            return !string.IsNullOrEmpty(path) && path.StartsWith("Assets/Synty");
        }

        private static bool IsUnder(Transform t, string groupName)
        {
            for (Transform p = t; p != null; p = p.parent)
            {
                if (p.name == groupName)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryBounds(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds();
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return false;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        private static string Verdict(bool ok, int count)
        {
            return ok ? "0 ✅" : count + " ❌";
        }
    }
}
