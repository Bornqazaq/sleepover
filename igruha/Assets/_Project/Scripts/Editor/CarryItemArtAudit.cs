using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Items;
using Igruha.Minigames.CarryItem;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Замеры арта «Переноски предмета» — приёмка подфаз 4.1–4.6 числами.
    ///
    /// Существует потому, что ровно этих чисел не хватило Duck Hunt: лёд лежал
    /// над пропастями, стог стоял поперёк этажа, декор висел в воздухе — всё
    /// ловится замером до того, как геймдизайнер откроет сцену.
    ///
    /// У этой игры к общему списку добавлена <b>проверка читаемости</b>, и она
    /// здесь главная. Вся индикация — прозрачный корпус, столбик воды, крышка
    /// цветом команды, четыре держалки, струя — с 01.09 жила руками в префабе,
    /// а пересборка арены пересоздаёт префабы целиком. Одно нажатие пункта меню
    /// снимало с игры и читаемость, и весь сетевой слой бутыли. Разошлось молча
    /// и держалось только тем, что пересборку никто не запускал. Теперь это
    /// проверяется, а не помнится.
    /// </summary>
    internal static class CarryItemArtAudit
    {
        /// <summary>Имя, которым <see cref="DressKit"/> называет корень надетой модели.</summary>
        private const string DressRoot = "Dress";

        /// <summary>Допуск на «стоит на полу» и «сидит в коробке», м: два сантиметра не видно.</summary>
        private const float Tolerance = 0.02f;

        private const string BottlePrefabPath = "Assets/_Project/Prefabs/Minigames/CarryItem/Bottle.prefab";
        private const string BrickPrefabPath = "Assets/_Project/Prefabs/Minigames/CarryItem/Brick.prefab";

        [MenuItem("Igruha/Переноска предмета/Замеры арта")]
        private static void Measure()
        {
            var report = new StringBuilder();
            report.Append("📏 «Переноска предмета» — замеры арта");

            var roots = new List<GameObject>();
            foreach (string name in new[] { "_Arena", "_Traps", "_Pickups", "_Bounds" })
            {
                GameObject go = GameObject.Find(name);
                if (go != null)
                {
                    roots.Add(go);
                }
            }

            if (roots.Count == 0)
            {
                Debug.LogError("Замеры арта: открой сцену CarryItem — не найдено ни одного корня арены.");
                return;
            }

            MeasureDress(roots, report);
            MeasureScenery(roots, report);
            MeasureColliders(report);
            MeasureMeshes(roots, report);
            MeasureReadability(report);

            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Дресс: коллайдеров в нём быть не должно вовсе, за габариты своей
        /// коробки он выходить не имеет права, и висеть над её дном тоже.
        ///
        /// По горизонтали спрос строгий: коллайдер держит коробка, и модель
        /// шире неё означает, что игрок упирается в воздух, а уже коробки —
        /// что проходит сквозь видимую преграду. По высоте допуск свободнее:
        /// <see cref="DressKit"/> намеренно не ужимает вверх, высота коробки —
        /// её геймплейный смысл.
        /// </summary>
        private static void MeasureDress(List<GameObject> roots, StringBuilder report)
        {
            int dressed = 0;
            int colliders = 0;
            int floating = 0;
            int overflowing = 0;
            float worstOverflow = 0f;
            float worstGap = 0f;
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

                    if (!TryWorldBounds(all[i].gameObject, out Bounds model))
                    {
                        continue;
                    }

                    Transform box = all[i].parent;
                    Bounds cage = BoxBounds(box);

                    float overflowX = Mathf.Max(0f,
                        Mathf.Max(model.max.x - cage.max.x, cage.min.x - model.min.x));
                    float overflowZ = Mathf.Max(0f,
                        Mathf.Max(model.max.z - cage.max.z, cage.min.z - model.min.z));
                    float overflow = Mathf.Max(overflowX, overflowZ);

                    float gap = model.min.y - cage.min.y;

                    if (overflow > Tolerance)
                    {
                        overflowing++;
                        if (overflow > worstOverflow)
                        {
                            worstOverflow = overflow;
                            worstName = box.name;
                        }
                    }

                    if (gap > Tolerance)
                    {
                        floating++;
                        worstGap = Mathf.Max(worstGap, gap);
                    }
                }
            }

            report.Append("\n\n— Дресс —");
            report.Append("\n  коробок одето:            ").Append(dressed);
            report.Append("\n  коллайдеров в дрессе:     ").Append(colliders).Append(colliders == 0 ? " ✔" : " ✘");
            report.Append("\n  вылезает за коробку:      ").Append(overflowing)
                .Append(overflowing == 0 ? " ✔" : $" ✘ (худший {worstName}, {worstOverflow:F2} м)");
            report.Append("\n  висит над дном коробки:   ").Append(floating)
                .Append(floating == 0 ? " ✔" : $" ✘ (зазор до {worstGap:F2} м)");
            report.Append("\n  ненайденных моделей:      ").Append(CarryItemDress.Missing.Count)
                .Append(CarryItemDress.Missing.Count == 0 ? " ✔" : " ✘");
        }

        /// <summary>
        /// Декор, поставленный мимо коробок блокаута: тачки-ловушки и стояк
        /// трубы. Коллайдеров у него быть не должно — иначе он ловит броски
        /// бутыли, кирпичи и струю, — и стоять он обязан на полу, а не в нём
        /// и не над ним.
        /// </summary>
        private static void MeasureScenery(List<GameObject> roots, StringBuilder report)
        {
            var props = new List<GameObject>();
            for (int r = 0; r < roots.Count; r++)
            {
                Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (IsProp(all[i].name))
                    {
                        props.Add(all[i].gameObject);
                    }
                }
            }

            int colliders = 0;
            int offFloor = 0;
            float worst = 0f;

            for (int i = 0; i < props.Count; i++)
            {
                colliders += props[i].GetComponentsInChildren<Collider>(true).Length;
                if (!StandsOnFloor(props[i].name) || !TryWorldBounds(props[i], out Bounds bounds))
                {
                    continue;
                }

                float gap = Mathf.Abs(bounds.min.y);
                if (gap > Tolerance)
                {
                    offFloor++;
                    worst = Mathf.Max(worst, gap);
                }
            }

            report.Append("\n\n— Декор мимо коробок —");
            report.Append("\n  предметов:                ").Append(props.Count);
            report.Append("\n  коллайдеров в них:        ").Append(colliders).Append(colliders == 0 ? " ✔" : " ✘");
            report.Append("\n  не стоит на полу:         ").Append(offFloor)
                .Append(offFloor == 0 ? " ✔" : $" ✘ (до {worst:F2} м)");
        }

        /// <summary>
        /// Коллайдеры на слоях сплошной геометрии. Их число обязано совпадать
        /// с блокаутом: арт не имеет права ни добавить преграду, ни убрать её.
        /// Отдельной строкой триггеры — зоны выбывания и ловушки.
        /// </summary>
        private static void MeasureColliders(StringBuilder report)
        {
            int ground = LayerMask.NameToLayer("Ground");
            int cover = LayerMask.NameToLayer("Cover");

            int solid = 0;
            int triggers = 0;
            var all = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].isTrigger)
                {
                    triggers++;
                    continue;
                }

                if (all[i].gameObject.layer == ground || all[i].gameObject.layer == cover)
                {
                    solid++;
                }
            }

            report.Append("\n\n— Коллайдеры —");
            report.Append("\n  сплошных на Ground/Cover: ").Append(solid);
            report.Append("\n  триггеров всего:          ").Append(triggers);
        }

        /// <summary>Меши и треугольники: за ростом надо следить, а не узнавать о нём по FPS.</summary>
        private static void MeasureMeshes(List<GameObject> roots, StringBuilder report)
        {
            int total = 0;
            int visible = 0;
            long triangles = 0;

            for (int r = 0; r < roots.Count; r++)
            {
                var filters = roots[r].GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < filters.Length; i++)
                {
                    var renderer = filters[i].GetComponent<MeshRenderer>();
                    if (renderer == null)
                    {
                        continue;
                    }

                    total++;
                    if (!renderer.enabled)
                    {
                        continue;
                    }

                    visible++;
                    Mesh mesh = filters[i].sharedMesh;
                    if (mesh != null)
                    {
                        triangles += mesh.triangles.Length / 3;
                    }
                }
            }

            report.Append("\n\n— Меши —");
            report.Append("\n  рендереров всего:         ").Append(total);
            report.Append("\n  видимых:                  ").Append(visible);
            report.Append("\n  погашено под дресс:       ").Append(total - visible);
            report.Append("\n  треугольников видимых:    ").Append(triangles);
        }

        /// <summary>
        /// Читаемость и сетевой слой префабов — то, ради чего этот замер и
        /// заведён. Каждая строка здесь однажды жила руками и однажды была бы
        /// потеряна первой же пересборкой.
        /// </summary>
        private static void MeasureReadability(StringBuilder report)
        {
            report.Append("\n\n— Читаемость и сеть (префабы) —");

            var bottle = AssetDatabase.LoadAssetAtPath<GameObject>(BottlePrefabPath);
            if (bottle == null)
            {
                report.Append("\n  бутыль: префаб не найден ✘");
            }
            else
            {
                var water = bottle.GetComponent<WaterBottle>();
                var markers = bottle.GetComponent<MultiCarryHandleMarkers>();
                Transform cap = bottle.transform.Find("Cap");
                Transform pivot = bottle.transform.Find("WaterPivot");
                Transform jet = bottle.transform.Find("PourJet");
                Transform handles = bottle.transform.Find("Handles");

                report.Append("\n  бутыль: столбик воды      ").Append(Mark(pivot != null));
                report.Append("\n          крышка (цвет команды) ").Append(Mark(cap != null));
                report.Append("\n          струя из горлышка ").Append(Mark(jet != null));
                report.Append("\n          держалок          ")
                    .Append(handles != null ? handles.childCount : 0)
                    .Append(Mark(handles != null && handles.childCount == MultiCarryObject.MaxHandles));
                report.Append("\n          показ занятости   ").Append(Mark(markers != null));
                report.Append("\n          WaterBottle       ").Append(Mark(water != null));
                report.Append("\n          NetworkObject     ")
                    .Append(Mark(bottle.GetComponent<Unity.Netcode.NetworkObject>() != null));
                report.Append("\n          NetworkTransform  ")
                    .Append(Mark(bottle.GetComponent<Unity.Netcode.Components.NetworkTransform>() != null));
            }

            var brick = AssetDatabase.LoadAssetAtPath<GameObject>(BrickPrefabPath);
            if (brick == null)
            {
                report.Append("\n  кирпич: префаб не найден ✘");
                return;
            }

            report.Append("\n  кирпич: PickupItem        ").Append(Mark(brick.GetComponent<PickupItem>() != null));
            report.Append("\n          NetworkObject     ")
                .Append(Mark(brick.GetComponent<Unity.Netcode.NetworkObject>() != null));
            report.Append("\n          NetworkTransform  ")
                .Append(Mark(brick.GetComponent<Unity.Netcode.Components.NetworkTransform>() != null));
        }

        /// <summary>
        /// Модели пака, поставленные мимо коробок блокаута. Список именной, а не
        /// по признаку: декор ставится в разные группы и разными методами, и
        /// пропущенное имя означает непроверенный коллайдер посреди арены.
        /// </summary>
        private static bool IsProp(string name)
        {
            return name.StartsWith("Wheelbarrow_") || name == "Standpipe" || name == "PipeSpout"
                   || name == "Pallet" || name == "Ladder" || name == "Outlet";
        }

        /// <summary>
        /// Кому положено стоять на полу. Исключение ровно одно и осмысленное:
        /// излом прорванной трубы висит на высоте струи — труба, лежащая на
        /// полу, не объясняла бы, откуда бьёт.
        /// </summary>
        private static bool StandsOnFloor(string name)
        {
            return name != "PipeSpout";
        }

        private static string Mark(bool ok)
        {
            return ok ? " ✔" : " ✘";
        }

        /// <summary>Габарит коробки блокаута: она сама растянута, поэтому считается по масштабу.</summary>
        private static Bounds BoxBounds(Transform box)
        {
            Vector3 size = box.lossyScale;
            return new Bounds(box.position,
                new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
        }

        private static bool TryWorldBounds(GameObject go, out Bounds bounds)
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
    }
}
