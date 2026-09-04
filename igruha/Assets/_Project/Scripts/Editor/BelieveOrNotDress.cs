using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Igruha.Minigames.BelieveOrNot;
using Tone = Igruha.EditorTools.BelieveOrNotPaletteAssets.Tone;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Одевание стола «Верю / не верю» — подфаза 4.1. Зал, шторы и свет
    /// приезжают отдельно на 4.3, здесь только то, что стоит в круге света:
    /// стол, два стула, две коробки, абажур.
    ///
    /// <b>Дресс — слой поверх блокаута.</b> Коробка блокаута остаётся на месте
    /// со своим коллайдером и слоем, у неё гаснет рендерер, а внутрь садится
    /// модель. Выверенная фазами 2–3 геометрия физически не может сдвинуться
    /// от арта: коллайдер столешницы, барьер над столом и точки посадки
    /// не трогаются вовсе.
    ///
    /// <b>Случайности здесь нет ни одной.</b> В остальных играх дресс тянет
    /// модель из набора своим генератором; здесь набора нет и быть не может:
    /// две коробки обязаны быть неразличимы (спека, 4.4), а неразличимость
    /// проверяется тем, что обе одеваются одним и тем же кодом без единого
    /// случайного числа.
    ///
    /// <b>Почему стол собран из примитивов, а не взят моделью.</b> Круглого
    /// стола ⌀2.88 м в двенадцати паках нет: ближайший круглый — 1.54 м, и
    /// растянуть его в 1.87 раза значит получить ножку толщиной с человека.
    /// Покерный стол пака — овал 3.34 × 1.35, а здесь двое сидят строго
    /// напротив друг друга. Диск сукна, борт, царга и нога дают ровно концепт
    /// геймдизайнера и точный габарит, а низкополигональный вид проекта
    /// держится не текстурой, а плоским цветом и светом.
    ///
    /// <b>Реквизит пака не перекрашивается.</b> На «Дырке в стене» перекраска
    /// была обязательной: ярмарочная трибуна кричала в тёмной студии. Здесь
    /// наоборот — сундук, стул и абажур приезжают ровно в тонах брифа
    /// (тёмное дерево, бордо, матовый металл), и плоская заливка отняла бы
    /// у них окантовку и защёлку, то есть ровно то, ради чего они выбраны.
    /// Свести их с палитрой блокаута — работа подфазы 4.2, и она делается
    /// замером по UV, а не заливкой.
    /// </summary>
    internal static class BelieveOrNotDress
    {
        private const string ChestPath = "Assets/Synty/PolygonGeneric/Prefabs/Props/SM_Gen_Prop_Chest_01.prefab";
        private const string ChairPath = "Assets/Synty/PolygonCasino/Prefabs/Props/SM_Prop_Chair_04.prefab";
        private const string ShadePath = "Assets/Synty/PolygonNightclubs/Prefabs/Props/SM_Prop_Light_03.prefab";

        private const string HolderName = "Dress";

        /// <summary>
        /// Доворот сундука вокруг Y, градусы. Петля модели идёт по её длинной
        /// стороне, петля коробки блокаута — сбоку; совмещает их единственный
        /// доворот на четверть влево.
        /// </summary>
        private const float ChestYaw = -90f;

        /// <summary>
        /// Доворот модели стула вокруг Y, градусы: у <c>SM_Prop_Chair_04</c>
        /// спинка смотрит по собственной оси +Z, а стул обязан стоять спинкой
        /// от стола.
        /// </summary>
        private const float ChairYaw = 180f;

        /// <summary>Толщина шнура подвеса, м. Тоньше сантиметра он пропадает в темноте зала.</summary>
        private const float CordDiameter = 0.03f;

        /// <summary>Ширина деревянного борта столешницы, м: на неё сукно уже, чем стол.</summary>
        private const float RimWidth = 0.09f;

        /// <summary>Толщина борта, царги и базы ноги, м.</summary>
        private const float RimHeight = 0.10f;

        private static readonly List<string> notes = new List<string>(8);

        /// <summary>Сбросить состояние перед пересборкой сцены.</summary>
        internal static void Begin()
        {
            DressKit.Begin();
            BelieveOrNotPaletteAssets.ClearCache();
            notes.Clear();
        }

        /// <summary>Пути моделей, которых не оказалось в проекте: паки Synty ставит каждый себе сам.</summary>
        internal static IReadOnlyList<string> Missing
        {
            get { return DressKit.Missing; }
        }

        // ========== СТОЛ ==========

        /// <summary>
        /// Одеть столешницу: борт, сукно, царга, нога и база.
        ///
        /// Верх сукна ложится ровно на верх коллайдера столешницы: на нём
        /// стоят коробки, и расхождение даже в сантиметр читается как коробка,
        /// висящая в воздухе.
        /// </summary>
        internal static void DressTable(GameObject tableTop, BelieveOrNotConfig config)
        {
            if (tableTop == null || config == null)
            {
                return;
            }

            Transform holder = Holder(tableTop.transform);
            float diameter = config.TableDiameter;
            float top = config.TableHeight;
            float felt = diameter - RimWidth * 2f;

            Disc(holder, "Rim", diameter, RimHeight, top, Tone.Wood);

            // Сукно выступает над бортом на миллиметр: вровень оно тонуло бы
            // внутри борта, а глубже — не читалось бы вовсе.
            Disc(holder, "Felt", felt, 0.03f, top + 0.001f, Tone.Felt);
            Disc(holder, "Apron", felt - RimWidth * 2f, 0.11f, top - RimHeight, Tone.Wood);
            Disc(holder, "Column", diameter * 0.15f, 0.50f, 0.60f, Tone.Wood);
            Disc(holder, "Base", diameter * 0.45f, RimHeight, RimHeight, Tone.Wood);

            notes.Add($"стол: борт ⌀{diameter:F2} м, сукно ⌀{felt:F2} м, верх сукна {top + 0.001f:F3} м " +
                      $"при верхе коллайдера {top:F3} м");
        }

        // ========== СТУЛ ==========

        /// <summary>
        /// Поставить стул в натуральный рост на пол, лицом к столу.
        ///
        /// Модель не ужимается в коробку блокаута: коробка 0.55 × 0.54 м —
        /// это отметка места, а не габарит предмета, и коллайдера у неё нет
        /// (стул с коллайдером подхватывал сидящего и ставил его на сиденье,
        /// замер 25.08). Стул со срезанной до полуметра спинкой читался бы
        /// табуреткой, а дуэль держится в том числе на двух высоких спинках.
        /// </summary>
        internal static void DressChair(GameObject chairBox, Vector3 seatDirection)
        {
            if (chairBox == null || !DressKit.TryLoad(ChairPath, out GameObject prefab))
            {
                return;
            }

            Transform holder = Holder(chairBox.transform);
            var chair = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder);
            chair.name = "Chair";

            // Коробка блокаута стоит на полу и центрирована по высоте, значит
            // пол ровно на её полувысоте ниже центра.
            chair.transform.localPosition = new Vector3(0f, -chairBox.transform.localPosition.y, 0f);
            chair.transform.localRotation =
                Quaternion.LookRotation(-seatDirection, Vector3.up) * Quaternion.Euler(0f, ChairYaw, 0f);
            chair.transform.localScale = Vector3.one;

            Scenery(chair);
            notes.Add($"стул «{chairBox.name}»: {ChairPath.Substring(ChairPath.LastIndexOf('/') + 1)} " +
                      $"в натуральный рост, коробка блокаута погашена");
        }

        // ========== КОРОБКА ==========

        /// <summary>
        /// Одеть коробку сундуком пака и подвинуть петлю на настоящую линию
        /// крышки.
        ///
        /// <b>Почему именно этот сундук.</b> У <c>SM_Gen_Prop_Chest_01</c> три
        /// отдельных меша — корпус, крышка и защёлка, — и у крышки собственная
        /// точка вращения ровно на линии петли. Значит крышка блокаута
        /// анимируется как есть: ни переписывать <see cref="BelieveBox"/>,
        /// ни резать меши не нужно.
        ///
        /// <b>Сундук развёрнут на четверть и сжат по длине.</b> Петля модели
        /// идёт по её длинной стороне, а петля блокаута — сбоку: так решено
        /// в фазе 2, потому что откинутая назад крышка вставала между камерой
        /// сидящего и столом. Совмещает оба требования единственный доворот
        /// на −90°, но сундук при этом встаёт к игроку торцом: 0.97 м длины
        /// уходят от игрока к игроку, и первый же кадр сидящего показал две
        /// бочки вместо двух коробок. Поэтому модель садится точно в габарит
        /// коробки блокаута (0.8 ШП, спека 4.4), а не масштабируется
        /// равномерно: сундук 2:1 становится почти кубическим ларцом, полосы
        /// оковки сходятся плотнее — и это единственная деформация, которую
        /// видно, зато коробка читается коробкой с обоих мест за столом.
        ///
        /// <b>Петля переезжает — и это объявленная правка.</b> В блокауте она
        /// стояла выше и дальше от центра, чем линия крышки настоящего сундука:
        /// у серого ящика этой линии просто не было, и точку взяли от
        /// номинального куба. Крышка на прежнем месте открывалась бы на
        /// невидимом рычаге. Замер до и после печатается пересборкой;
        /// коллайдеров правка не касается — у коробки их нет вовсе, и на
        /// точки посадки, барьер и обмен местами она не влияет.
        /// </summary>
        internal static void DressBox(BelieveBox box, GameObject body, Transform hinge, GameObject lidPlate,
            float boxSize)
        {
            if (box == null || body == null || hinge == null || lidPlate == null)
            {
                return;
            }

            if (!DressKit.TryLoad(ChestPath, out GameObject prefab))
            {
                return;
            }

            Transform lidPivot = FindPart(prefab.transform, "Lid");
            if (lidPivot == null)
            {
                Debug.LogError($"У сундука {ChestPath} нет части «Lid» — крышку коробки нечем одеть");
                return;
            }

            Bounds whole = DressKit.GetBounds(prefab, ChestPath);
            Vector3 lidLocal = lidPivot.localPosition;
            float half = boxSize * 0.5f;

            // Габарит коробки — корпус плюс крышка. Доворот на четверть меняет
            // оси местами, поэтому длина сундука садится в глубину коробки,
            // а его глубина — в ширину.
            Vector3 target = body.transform.localScale;
            target.y += lidPlate.transform.localScale.y;
            var fit = new Vector3(
                target.z / whole.size.x,
                target.y / whole.size.y,
                target.x / whole.size.z);

            // Корпус: низ сундука ложится на столешницу, то есть на нижнюю
            // грань номинального куба коробки.
            Transform bodyHolder = Holder(body.transform);
            GameObject chest = Piece(bodyHolder, "Chest", prefab, fit, ChestYaw,
                new Vector3(0f, -half - body.transform.localPosition.y, 0f), Vector3.zero);
            DestroyPart(chest.transform, "Lid");

            // Петля: точка вращения модели, пересчитанная в оси коробки.
            Vector3 hingeBefore = hinge.localPosition;
            hinge.localPosition = new Vector3(-lidLocal.z * fit.z, lidLocal.y * fit.y - half, 0f);

            // Крышка: держатель возвращается в точку петли, потому что сама
            // плита блокаута смещена от неё на половину коробки.
            Transform lidHolder = Holder(lidPlate.transform);
            lidHolder.localPosition = Divide(-lidPlate.transform.localPosition, lidPlate.transform.localScale);
            GameObject lid = Piece(lidHolder, "ChestLid", prefab, fit, ChestYaw, Vector3.zero, -lidLocal);
            KeepOnlyPart(lid.transform, "Lid");
            MirrorLatch(lid.transform);

            notes.Add($"коробка «{box.name}»: сундук {whole.size.ToString("F2")} → {target.ToString("F3")} м " +
                      $"(масштаб {fit.ToString("F2")}), " +
                      $"петля {hingeBefore.ToString("F3")} → {hinge.localPosition.ToString("F3")}");
        }

        // ========== ЛАМПА ==========

        /// <summary>
        /// Одеть абажур и подвесить его на шнуре к потолку.
        ///
        /// Шнур обязателен: абажур, висящий сам по себе, читается ошибкой
        /// сборки, а на концепте геймдизайнера лампа именно на шнуре. Сам
        /// источник света не трогается — он выставлен в фазе 2 и входит
        /// в механику фокуса (спека, 4.6).
        /// </summary>
        internal static void DressLamp(GameObject shadeBox, BelieveOrNotConfig config)
        {
            if (shadeBox == null || config == null || !DressKit.TryLoad(ShadePath, out GameObject prefab))
            {
                return;
            }

            Transform holder = Holder(shadeBox.transform);
            Bounds local = DressKit.GetBounds(prefab, ShadePath);
            float scale = shadeBox.transform.localScale.x / local.size.x;

            // Модель висит ниже собственной точки подвеса: центр меша уходит
            // вниз, и чтобы абажур встал по центру коробки блокаута, точку
            // подвеса надо поднять ровно на это смещение.
            float lift = -local.center.y * scale;
            Piece(holder, "Shade", prefab, Vector3.one * scale, 0f, new Vector3(0f, lift, 0f), Vector3.zero);

            float shadeTop = shadeBox.transform.position.y + lift;
            float cord = config.CeilingHeight - shadeTop;
            if (cord > 0f)
            {
                Disc(holder, "Cord", CordDiameter, cord, config.CeilingHeight, Tone.Shade);
            }

            notes.Add($"лампа: абажур ⌀{local.size.x * scale:F2} м, низ {shadeTop - local.size.y * scale:F2} м, " +
                      $"шнур {cord:F2} м до потолка {config.CeilingHeight:F2} м");
        }

        // ========== ОТЧЁТ ==========

        /// <summary>
        /// Замеры дресса и числа приёмки подфазы. Печатается пересборкой:
        /// «коллайдеров в дрессе ноль» обязано быть числом, а не впечатлением.
        /// </summary>
        internal static string Report(GameObject arena)
        {
            var report = new StringBuilder("🪑 Дресс стола «Верю / не верю» (подфаза 4.1)");
            for (int i = 0; i < notes.Count; i++)
            {
                report.Append("\n— ").Append(notes[i]);
            }

            int colliders = 0;
            int meshes = 0;
            int triangles = 0;
            if (arena != null)
            {
                foreach (Transform holder in Holders(arena.transform))
                {
                    colliders += holder.GetComponentsInChildren<Collider>(true).Length;
                    MeshFilter[] filters = holder.GetComponentsInChildren<MeshFilter>(true);
                    meshes += filters.Length;
                    for (int i = 0; i < filters.Length; i++)
                    {
                        Mesh mesh = filters[i].sharedMesh;
                        if (mesh != null)
                        {
                            triangles += mesh.triangles.Length / 3;
                        }
                    }
                }
            }

            report.Append("\n— в дрессе: мешей ").Append(meshes)
                .Append(", треугольников ").Append(triangles)
                .Append(", коллайдеров ").Append(colliders).Append(" (норма 0)");

            IReadOnlyList<string> missing = Missing;
            report.Append("\n— замечаний пересборки: ").Append(missing.Count).Append(" (норма 0)");
            for (int i = 0; i < missing.Count; i++)
            {
                report.Append("\n  · модель не найдена: ").Append(missing[i]);
            }

            return report.ToString();
        }

        // ========== ОБЩЕЕ ==========

        /// <summary>
        /// Держатель дресса внутри коробки блокаута: гасит её рендерер и
        /// снимает её масштаб, чтобы модели внутри жили в метрах.
        ///
        /// Без снятия масштаба модель наследовала бы растяжение коробки:
        /// столешница блокаута — цилиндр с масштабом 2.88 × 0.36 × 2.88,
        /// и сундук в таком держателе стал бы блином.
        /// </summary>
        private static Transform Holder(Transform host)
        {
            var renderer = host.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }

            var holder = new GameObject(HolderName);
            holder.transform.SetParent(host, false);
            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.identity;
            holder.transform.localScale = Divide(Vector3.one, host.localScale);
            return holder.transform;
        }

        /// <summary>Все держатели дресса внутри арены.</summary>
        private static IEnumerable<Transform> Holders(Transform root)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == HolderName)
                {
                    yield return all[i];
                }
            }
        }

        /// <summary>
        /// Копия модели пака под держателем: доворот, равномерный масштаб,
        /// положение в метрах.
        ///
        /// Доворот и масштаб живут на промежуточном узле, а не на самой копии:
        /// так смещение <paramref name="modelOffset"/> задаётся в осях модели,
        /// и точка вращения крышки садится в петлю без пересчёта углов.
        ///
        /// Копия распаковывается: из сундука выбрасывается то корпус, то
        /// крышка, а удалять части живого инстанса префаба Unity не даёт.
        /// </summary>
        private static GameObject Piece(Transform holder, string name, GameObject prefab, Vector3 scale, float yaw,
            Vector3 position, Vector3 modelOffset)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(holder, false);
            pivot.transform.localPosition = position;
            pivot.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            pivot.transform.localScale = scale;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pivot.transform);
            instance.transform.localPosition = modelOffset;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            Scenery(instance);
            return instance;
        }

        /// <summary>
        /// Диск палитры: борт, сукно, царга, нога, шнур. Кладётся по верхней
        /// грани — именно она задаёт, на какой высоте стоят коробки.
        /// </summary>
        private static void Disc(Transform holder, string name, float diameter, float height, float topY, Tone tone)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(holder, false);

            // У примитива-цилиндра высота равна двум единицам масштаба.
            go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
            go.transform.localPosition = new Vector3(0f, topY - height * 0.5f - holder.parent.position.y, 0f);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = BelieveOrNotPaletteAssets.Get(tone);
            }

            Scenery(go);
        }

        /// <summary>
        /// Пометить копию декорацией: снять коллайдеры и увести на
        /// <c>Default</c>.
        ///
        /// Коллайдеры срезаются всегда. Зал полон толкающихся зрителей, и
        /// мешевый коллайдер стула или сундука ловил бы их телами; кроме того,
        /// собственный коллайдер поверх коробки блокаута даёт вторую
        /// поверхность другой формы, и приземление становится лотереей.
        /// </summary>
        private static void Scenery(GameObject go)
        {
            Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Object.DestroyImmediate(colliders[i], true);
            }

            Transform[] all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                all[i].gameObject.layer = 0;
            }

            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }

        /// <summary>
        /// Повесить на крышку вторую, зеркальную накладку — и тем сделать
        /// коробки неразличимыми.
        ///
        /// <b>Зачем.</b> Коробки блокаута развёрнуты друг к другу: у них
        /// зеркальные повороты, и это не арт, а фаза 2 — так их крышки
        /// расходятся в разные стороны, а знаки исходов не закрывают друг
        /// друга. У сундука пака защёлка одна и стоит на передней грани,
        /// поэтому зеркальная пара показывала бы залу одну коробку защёлкой,
        /// а вторую — глухой стороной. Спека запрещает это прямым текстом:
        /// «никаких отметок… разного положения защёлок» (4.4), а примета,
        /// которая едет вместе с коробкой при обмене, — это уже способ
        /// следить за коробкой вместо чтения человека.
        ///
        /// <b>Почему копия ложится именно на линию петли.</b> Отражение идёт
        /// через центр самой крышки, и вторая накладка садится ровно на её
        /// заднюю кромку — то есть туда, где у настоящего сундука и стоят
        /// петли. Деталь не выдумана: она читается как вторая половина той же
        /// фурнитуры и вращается вместе с крышкой, оставаясь на оси поворота.
        /// </summary>
        private static void MirrorLatch(Transform lidInstance)
        {
            Transform lidPart = FindPart(lidInstance, "Lid") ?? lidInstance;
            Transform latch = FindPart(lidPart, "Latch");
            var filter = lidPart.GetComponent<MeshFilter>();
            if (latch == null || filter == null || filter.sharedMesh == null)
            {
                return;
            }

            float centre = filter.sharedMesh.bounds.center.z;
            var twin = Object.Instantiate(latch.gameObject, latch.parent);
            twin.name = latch.name + "_Mirror";

            Vector3 local = latch.localPosition;
            twin.transform.localPosition = new Vector3(local.x, local.y, centre * 2f - local.z);
            twin.transform.localRotation = latch.localRotation * Quaternion.Euler(0f, 180f, 0f);
            twin.transform.localScale = latch.localScale;
        }

        /// <summary>Часть модели по куску имени: у Synty это суффикс вроде <c>_Lid_01</c>.</summary>
        private static Transform FindPart(Transform root, string part)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != root && all[i].name.Contains(part))
                {
                    return all[i];
                }
            }

            return null;
        }

        private static void DestroyPart(Transform root, string part)
        {
            Transform found = FindPart(root, part);
            if (found != null)
            {
                Object.DestroyImmediate(found.gameObject);
            }
        }

        /// <summary>
        /// Оставить в копии только названную часть: из сундука — одну крышку.
        /// Меш самого корня при этом гасится, а не удаляется: на корне висит
        /// корпус, и удаление вместе с ним унесло бы весь узел.
        /// </summary>
        private static void KeepOnlyPart(Transform root, string part)
        {
            var doomed = new List<GameObject>(4);
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (!child.name.Contains(part))
                {
                    doomed.Add(child.gameObject);
                }
            }

            for (int i = 0; i < doomed.Count; i++)
            {
                Object.DestroyImmediate(doomed[i]);
            }

            var renderer = root.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }
        }

        private static Vector3 Divide(Vector3 value, Vector3 by)
        {
            return new Vector3(
                Mathf.Approximately(by.x, 0f) ? 0f : value.x / by.x,
                Mathf.Approximately(by.y, 0f) ? 0f : value.y / by.y,
                Mathf.Approximately(by.z, 0f) ? 0f : value.z / by.z);
        }
    }
}
