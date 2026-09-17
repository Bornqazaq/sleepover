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
            MeasureEffects(report);
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

                    // Настил, прижатый к верхней грани, — не «висит в воздухе»,
                    // а стоит там, где должен: по нему ходят, и низ коробки под
                    // ним это толща, а не пустота. DressKit прижимает его туда
                    // намеренно. Проверка ловит обратный случай — модель,
                    // оторванную и от низа, и от верха.
                    bool sitsOnTop = Mathf.Abs(cage.max.y - model.max.y) <= Tolerance;
                    if (gap > Tolerance && !sitsOnTop)
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
        /// Коллизии по назначению: твёрдый реквизит, свободные маршруты и фон без физики.
        /// </summary>
        private static void MeasureScenery(List<GameObject> roots, StringBuilder report)
        {
            var arena=GameObject.Find("_Arena");
            var env=arena.transform.Find("Environment");
            var horizon=env.Find("Horizon");
            int backgroundColliders=horizon.GetComponentsInChildren<Collider>(true).Length;
            int wrongLayers=0,missingSolids=0;
            foreach(var c in env.GetComponentsInChildren<Collider>(true))
                if(!c.isTrigger && c.gameObject.layer!=LayerMask.NameToLayer("Ground") && c.gameObject.layer!=LayerMask.NameToLayer("Cover"))wrongLayers++;
            foreach(string group in new[]{"Structure","WorkAreas"})
            foreach(Transform t in env.Find(group))
                if(t.name!="CS_Puddle" && t.name!="CS_CableCoil" && t.GetComponentsInChildren<Collider>().Length==0)missingSolids++;
            int floorMisses=0;int mask=LayerMask.GetMask("Ground","Cover");
            foreach(Transform p in arena.transform.Find("Planks"))
            {
                var b=p.GetComponent<Collider>().bounds;
                for(int i=0;i<=30;i++)foreach(float side in new[]{-.45f,0,.45f})
                {
                    var origin=new Vector3(Mathf.Lerp(b.min.x+.02f,b.max.x-.02f,i/30f),.35f,b.center.z+b.size.z*side);
                    if(!Physics.Raycast(origin,Vector3.down,out var hit,.5f,mask,QueryTriggerInteraction.Ignore) || Mathf.Abs(hit.point.y)>.02f)floorMisses++;
                }
            }
            report.Append("\n\n— Original scenery and route —")
                .Append("\n  background colliders: ").Append(backgroundColliders).Append(Mark(backgroundColliders==0))
                .Append("\n  wrong camera layers: ").Append(wrongLayers).Append(Mark(wrongLayers==0))
                .Append("\n  solid props without collision: ").Append(missingSolids).Append(Mark(missingSolids==0))
                .Append("\n  bridge support failures / 372 probes: ").Append(floorMisses).Append(Mark(floorMisses==0));
            var dependencies=new HashSet<string>(AssetDatabase.GetDependencies("Assets/_Project/Scenes/Minigames/CarryItem.unity",true));
            foreach(var path in new[]{BottlePrefabPath,BrickPrefabPath,"Assets/_Project/Prefabs/Minigames/CarryItem/Tank.prefab","Assets/_Project/Prefabs/Minigames/CarryItem/BottleStack.prefab"})
                foreach(var d in AssetDatabase.GetDependencies(path,true))dependencies.Add(d);
            int oldArt=0;
            foreach(var d in dependencies)if(d.StartsWith("Assets/Synty/") || d.Contains("Art/CarryItem/Polygon"))oldArt++;
            report.Append("\n  Synty dependencies incl. runtime prefabs: ").Append(oldArt).Append(Mark(oldArt==0));
            int routeProbes=0,blocked=0;
            var blockers=new HashSet<string>();
            Physics.SyncTransforms();
            foreach(var route in Object.FindObjectsByType<CarryItemBotRoute>(FindObjectsSortMode.None))
            {
                var points=new List<Transform>();
                foreach(Transform child in route.transform)points.Add(child);
                for(int i=1;i<points.Count;i++)
                {
                    int steps=Mathf.CeilToInt(Vector3.Distance(points[i-1].position,points[i].position)/.15f);
                    for(int j=0;j<=steps;j++)
                    {
                        Vector3 p=Vector3.Lerp(points[i-1].position,points[i].position,(float)j/steps);
                        routeProbes++;
                        foreach(var c in Physics.OverlapCapsule(p+Vector3.up*.40f,p+Vector3.up*1.45f,.36f,mask,QueryTriggerInteraction.Ignore))
                        {
                            // Moving beam is an intentional timed obstacle; scenery must never block the lane.
                            if(c.name=="SwingingBeam")continue;
                            blocked++;blockers.Add(c.name);
                        }
                    }
                }
            }
            report.Append("\n  blocked character capsules / ").Append(routeProbes).Append(" route probes: ")
                .Append(blocked).Append(Mark(blocked==0));
            foreach(var name in blockers)report.Append("\n    blocker: ").Append(name);
            var config=AssetDatabase.LoadAssetAtPath<CarryItemConfig>("Assets/_Project/Settings/Gameplay/Minigames/CarryItemConfig.asset");
            int pickupBlocked=0,cameraBlocked=0;
            foreach(var stack in Object.FindObjectsByType<BottleStack>(FindObjectsSortMode.None))
            {
                Vector3 p=stack.transform.Find("BottleSpawn").position;
                for(int i=0;i<32;i++)
                {
                    float angle=i*Mathf.PI/16;
                    Vector3 station=p+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(config.HandleRadius+config.CarrierStandoff);
                    pickupBlocked+=Physics.OverlapCapsule(station+Vector3.up*.4f,station+Vector3.up*1.45f,.36f,mask,QueryTriggerInteraction.Ignore).Length;
                }
                if(Physics.SphereCast(p+Vector3.up*1.5f,.35f,Vector3.left,out var hit,4.5f,mask,QueryTriggerInteraction.Ignore))cameraBlocked++;
            }
            report.Append("\n  blocked pickup stations / 64 probes: ").Append(pickupBlocked).Append(Mark(pickupBlocked==0));
            report.Append("\n  blocked 4.5 m camera approaches: ").Append(cameraBlocked).Append(Mark(cameraBlocked==0));
        }

        /// <summary>Постоянные эффекты: те, которым автостарт положен по замыслу.</summary>
        private static readonly string[] AlwaysOn =
        {
            "PipeJet", "BeamTrail", "Haze_1", "Haze_2", "Haze_3",
            "ChasmHaze_1", "ChasmHaze_2", "ChasmHaze_3", "ChasmHaze_4"
        };

        /// <summary>
        /// Эффекты: коллайдеров нет, автостарт остался только у постоянных,
        /// и ничто не поднимается выше завала вдоль маршрута.
        ///
        /// Автостарт проверяется поимённо, а не числом: партиклы пака приходят
        /// с <c>playOnAwake</c> и зацикливанием, и забытый один означает арену,
        /// стоящую в брызгах с первого кадра. Постоянных ровно пять — струя
        /// трубы, шлейф балки и три облака пыли.
        /// </summary>
        private static void MeasureEffects(StringBuilder report)
        {
            GameObject arena = GameObject.Find("_Arena");
            Transform group = arena != null ? arena.transform.Find("Effects") : null;

            report.AppendLine().AppendLine().Append("— Эффекты —");
            if (group == null)
            {
                report.AppendLine().Append("  группы Effects нет ✘");
                return;
            }

            var systems = group.GetComponentsInChildren<ParticleSystem>(true);
            int colliders = group.GetComponentsInChildren<Collider>(true).Length;
            int lights = group.GetComponentsInChildren<Light>(true).Length;
            int strayAwake = 0;
            float tallest = 0f;

            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i].main.playOnAwake && !IsAlwaysOn(systems[i].transform))
                {
                    strayAwake++;
                }
            }

            // Высота считается врозь, и это не придирка. Постоянный эффект
            // висит в кадре весь раунд: подниматься выше завала ему нельзя,
            // иначе он закрывает собой то, на что игрок смотрит. Мгновенный
            // живёт полсекунды, и разлёт брызг вверх — это и есть удар; ему
            // допуск шире, но не бесконечный.
            // Габарит партикла пуст, пока тот не сыграл ни кадра. Замер сразу
            // после пересборки честно отдавал ноль по обеим высотам и ставил
            // галочку там, где ничего не мерил, — то есть проверка молча
            // проходила всегда. Поэтому системы прогоняются здесь же: луп на
            // несколько секунд, вспышка на свою длину.
            //
            // Прогоняются только корневые: Simulate идёт по детям сам, и вызов
            // на вложенной системе сдвинул бы её вперёд дважды.
            for (int i = 0; i < systems.Length; i++)
            {
                Transform parent = systems[i].transform.parent;
                if (parent != null && parent.GetComponentInParent<ParticleSystem>() != null)
                {
                    continue;
                }

                systems[i].Simulate(0f, true, true);
                systems[i].Simulate(systems[i].main.loop ? 3.5f : 0.5f, true, false);
            }

            float burst = 0f;
            var renderers = group.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                // Припаркованные под полом копии пула в счёт высоты не идут.
                if (renderers[i].transform.position.y < -10f)
                {
                    continue;
                }

                if (IsAlwaysOn(renderers[i].transform))
                {
                    tallest = Mathf.Max(tallest, renderers[i].bounds.max.y);
                }
                else
                {
                    burst = Mathf.Max(burst, renderers[i].bounds.max.y);
                }
            }

            report.AppendLine().Append("  систем частиц:            ").Append(systems.Length);
            report.AppendLine().Append("  коллайдеров:              ").Append(colliders).Append(Mark(colliders == 0));
            report.AppendLine().Append("  своих источников света:   ").Append(lights).Append(Mark(lights == 0));
            report.AppendLine().Append("  лишних автостартов:       ").Append(strayAwake).Append(Mark(strayAwake == 0));
            report.AppendLine().Append("  верх постоянного:         ").Append(tallest.ToString("F2")).Append(" м")
                .Append(Mark(tallest <= 2.16f + Tolerance));
            report.AppendLine().Append("  верх мгновенного:         ").Append(burst.ToString("F2")).Append(" м")
                .Append(Mark(burst <= 3.2f));
        }

        private static bool IsAlwaysOn(Transform effect)
        {
            for (Transform t = effect; t != null; t = t.parent)
            {
                for (int i = 0; i < AlwaysOn.Length; i++)
                {
                    if (t.name == AlwaysOn[i])
                    {
                        return true;
                    }
                }
            }

            return false;
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
                        // Через GetIndexCount, а не через triangles: меши паков
                        // приходят с выключенным Read/Write, и обращение к
                        // треугольникам роняет в консоль ошибку на каждый такой
                        // меш. Число индексов лежит в описании меша и читается
                        // всегда — замер тот же, а консоль остаётся чистой.
                        for (int sub = 0; sub < mesh.subMeshCount; sub++)
                        {
                            triangles += (long)(mesh.GetIndexCount(sub) / 3);
                        }
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

                var shell=bottle.transform.Find("Body").GetComponentInChildren<Renderer>().sharedMaterial;
                var waterSo=new SerializedObject(water);
                report.Append("\n  transparent shell: ").Append(Mark(shell.GetFloat("_Surface")==1 && shell.GetColor("_BaseColor").a<.3f));
                report.Append("\n  team shoulders/neck/cap: ").Append(Mark(waterSo.FindProperty("teamTint").arraySize==4));
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
