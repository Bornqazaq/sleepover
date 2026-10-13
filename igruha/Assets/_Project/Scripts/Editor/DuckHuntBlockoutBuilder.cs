using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Arena;
using Igruha.Core.Traps;
using Igruha.Minigames.DuckHunt;

namespace Igruha.EditorTools
{
    // Current layout. The old art pass is intentionally not part of the rebuild.
    internal static partial class DuckHuntArenaBuilder
    {
        private static bool blockoutOnly = true;

        /// <summary>
        /// Конец фронтальной стены старта в ширинах персонажа. Закрывает проекцию
        /// всех восьми спаунов с любой точки площадки лифта и с любой её высоты.
        /// </summary>
        private const float StartFrontWallEnd = 15f;

        private const int CollapseSectionCount = 6;
        private const float CollapseWarningDuration = .45f;
        private const float CollapseSectionDelay = .12f;

        private const int StairVisualStepCount = 16;
        private const float StairCurbWidth = .16f;
        private const float StairCurbHeight = .22f;

        [MenuItem("Igruha/Minigames/Rebuild Duck Hunt Arena")]
        public static void Rebuild()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (Application.isPlaying || scene.path != "Assets/_Project/Scenes/Minigames/DuckHunt.unity")
                throw new System.InvalidOperationException("Open DuckHunt in Edit Mode before rebuilding.");
            config = AssetDatabase.LoadAssetAtPath<DuckHuntConfig>(ConfigPath);
            var arenaRoot = GameObject.Find("_Arena");
            var spawnsRoot = GameObject.Find("_Spawns");
            if (config == null || arenaRoot == null || spawnsRoot == null)
                throw new System.InvalidOperationException("DuckHunt scene/config is incomplete.");
            if (!ResolveLayers()) throw new System.InvalidOperationException("Duck Hunt layers missing.");
            DuckHuntBarnArt.Preflight();
            blockoutOnly = true;
            builtTraps.Clear(); builtGeysers.Clear(); builtStairs.Clear(); warnings.Clear();
            EnsureBlockoutMaterials();
            BuildOriginalRifle();
            arenaRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            arenaRoot.transform.localScale = Vector3.one;
            while (arenaRoot.transform.childCount > 0)
                Object.DestroyImmediate(arenaRoot.transform.GetChild(0).gameObject);
            Transform tower = ResetGroup(arenaRoot.transform, "Tower");
            for (int floor = 0; floor < config.FloorCount; floor++) BuildBlockoutFloor(tower, floor);
            // A roof is a ceiling, not a sixth playable floor.
            float roofY = config.FloorCount * config.FloorStepWidths + 2;
            Box(tower, "Ceiling", groundLayer, floorMaterial, 0, 48, roofY - 1, roofY, 0, 14);
            var elevator = BuildElevator(ResetGroup(arenaRoot.transform, "ElevatorShaft"));
            var finish = BuildBlockoutFinish(tower);
            BuildSpawns(spawnsRoot.transform, elevator);
            // Eight players along the left inner wall, all facing along +X.
            foreach (Transform spawn in spawnsRoot.transform.Find("Ducks"))
            {
                Vector3 position = spawn.position; position.x = 1.05f;
                spawn.position = position;
            }
            BuildKillZone();
            var arena = EnsureArenaComponent(arenaRoot);
            WireMinigame(arena, elevator, finish);
            Physics.SyncTransforms();
            ValidateBlockout(arenaRoot);
            DuckHuntBarnArt.Apply(arenaRoot);
            ValidateBlockout(arenaRoot);
            MarkSceneDirty();
            AssetDatabase.SaveAssets();
            Debug.Log("Duck Hunt midway: functional prop covers, sheltered controls, sectional floor traps, final parkour over recovery deck.");
        }

        private static void EnsureBlockoutMaterials()
        {
            floorMaterial = EnsureMaterial("DH_BlockoutFloor", new Color(.60f, .64f, .68f));
            wallMaterial = EnsureMaterial("DH_BlockoutWall", new Color(.30f, .35f, .40f));
            metalMaterial = EnsureMaterial("DH_BlockoutMetal", new Color(.22f, .28f, .33f));
            coverHighMaterial = EnsureMaterial("DH_BlockoutCover", new Color(.19f, .57f, .62f));
            platformMaterial = EnsureMaterial("DH_BlockoutRoute", new Color(.82f, .65f, .29f));
            trapMaterial = EnsureMaterial("DH_BlockoutTrap", new Color(.91f, .33f, .18f));
            buttonReadyMaterial = EnsureMaterial("DH_BlockoutReady", new Color(.35f, .90f, .48f));
            buttonBusyMaterial = EnsureMaterial("DH_BlockoutBusy", new Color(.29f, .29f, .29f));
        }

        private static void BuildOriginalRifle()
        {
            const string modelPath = "Assets/_Project/Art/DuckHuntOriginal/Models/DH_OriginalRifle.fbx";
            const string prefabPath = "Assets/_Project/Art/DuckHuntOriginal/DH_OriginalRifle.prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (source == null) throw new System.InvalidOperationException("Export tools/blender/duck_hunt_rifle.py first.");
            var root = new GameObject("DH_OriginalRifle");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                var grip = model.transform.Find("GripMarker");
                var fore = model.transform.Find("ForeMarker");
                var up = model.transform.Find("UpMarker");
                if (grip == null || fore == null || up == null)
                    throw new System.InvalidOperationException("Rifle grip markers missing.");
                model.transform.rotation = Quaternion.Inverse(Quaternion.LookRotation(fore.position - grip.position, up.position - grip.position));
                model.transform.localScale *= .525f / Vector3.Distance(fore.position, grip.position);
                model.transform.position += new Vector3(0, 0, .055f) - grip.position;
                var materials = new Dictionary<string, Material>
                {
                    { "DH_Rifle_Walnut", EnsureMaterial("DH_Rifle_Walnut", new Color(.29f, .12f, .055f)) },
                    { "DH_Rifle_Steel", EnsureMaterial("DH_Rifle_Steel", new Color(.10f, .16f, .19f)) },
                    { "DH_Rifle_Brass", EnsureMaterial("DH_Rifle_Brass", new Color(.68f, .43f, .13f)) },
                    { "DH_Rifle_Rubber", EnsureMaterial("DH_Rifle_Rubber", new Color(.025f, .033f, .039f)) }
                };
                foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                {
                    var slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++)
                        if (slots[i] != null && materials.TryGetValue(slots[i].name, out Material mat)) slots[i] = mat;
                    renderer.sharedMaterials = slots;
                }
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                var so = new SerializedObject(config);
                so.FindProperty("rifleProp").objectReferenceValue = prefab;
                so.FindProperty("rifleScale").floatValue = .75f;
                so.FindProperty("rifleGripPoint").floatValue = .055f;
                so.FindProperty("rifleForePoint").floatValue = .4f;
                so.FindProperty("rifleGripOffset").vector3Value = new Vector3(.06f, -.02f, -.04f);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            finally { Object.DestroyImmediate(root); }
        }

        // All coordinates below are character widths (0.72 m), progress follows the snake.
        private static GameObject RouteBox(Transform root, string name, int floor, Material mat,
            float p0, float p1, float y0, float y1, float z0, float z1, int layer = -1)
        {
            GetXRange(floor, p0, p1, out float x0, out float x1);
            return Box(root, name, layer < 0 ? groundLayer : layer, mat, x0, x1, y0, y1, z0, z1);
        }

        private static void BuildBlockoutFloor(Transform tower, int floor)
        {
            Transform root = ResetGroup(tower, $"Floor_{floor + 1}");
            float y = floor * config.FloorStepWidths;
            if (floor == 0) RouteBox(root, "StartSlab", floor, floorMaterial, 0, 8, y - 1, y, 0, 14);
            else
            {
                RouteBox(root, "ArrivalLanding", floor, floorMaterial, 0, 8, y - 1, y, 11, 14);
                // Floors 2-4 use the arrival landing as a protected control bay.
                // The partition meets the underside of the floor above; its
                // existing rear opening still provides the route out.
                RouteBox(root, "ArrivalGuard", floor, wallMaterial, 7.7f, 8,
                    y, y + (floor >= 1 && floor <= 3 ? 7 : 2), 0, 11);
            }

            if (floor == 4)
            {
                RouteBox(root, "RecoveryDeck", floor, floorMaterial, 8, 48, y - 1, y, 0, 14);
                BuildFinalParkour(root, y);
            }
            else
            {
                // The short permanent gaps are gone. Each of floors 2-4 has one
                // long sectional trap, with a solid approach and a separate lever.
                if (floor >= 1)
                {
                    GetBlockoutCollapseRange(floor, out float start, out float end);
                    RouteBox(root, "ContinuousRun", floor, floorMaterial, 8, start, y - 1, y, 0, 14);
                    if (end < 40)
                        RouteBox(root, "RunAfterCollapse", floor, floorMaterial, end, 40, y - 1, y, 0, 14);
                    BuildBlockoutCollapse(root, floor, y, start, end);
                }
                else RouteBox(root, "ContinuousRun", floor, floorMaterial, 8, 40, y - 1, y, 0, 14);
                RouteBox(root, "TransitionBase", floor, floorMaterial, 40, 48, y - 1, y, 0, 14);
                BuildBlockoutTransition(root, floor, y);
            }
            // Оболочка этажа идёт на ВЕСЬ шаг этажа, а не на высоту потолка.
            //
            // Плита следующего этажа занимает только внутренний объём (p 0..48,
            // z 0..14) и наружу не выходит, а стены стоят снаружи него (z 14..14.4,
            // p -0.4..0 и 48..48.4). При высоте стены 7 ШП на каждом стыке
            // оставалась сквозная щель 0.72 м во всю длину башни: снаружи она
            // читалась тёмной полосой между этажами, местами сквозь неё было видно
            // небо. Тот же разрыв повторяла полосатая обшивка, потому что
            // StripedWall берёт высоту у самой стены.
            //
            // Стены и плита по-прежнему не пересекаются — они стоят рядом по z и x,
            // поэтому полный шаг ничего не загоняет внутрь плиты.
            float height = floor == 4 ? 9 : config.FloorStepWidths;
            // Под первым этажом закрывается такая же полоса до низа плиты.
            float wallBottom = floor == 0 ? y - 1 : y;
            RouteBox(root, "BackWall", floor, wallMaterial, 0, 48, wallBottom, y + height, 14, 14.4f);
            RouteBox(root, "EndWallA", floor, wallMaterial, -.4f, 0, wallBottom, y + height, 0, 14);
            RouteBox(root, "EndWallB", floor, wallMaterial, 48, 48.4f, wallBottom, y + height, 0, 14);
            // Floor five has no outgoing stair room, so it needs its own front
            // arrival wall. Besides closing the shell, this is the real support
            // for the 05 marker instead of leaving the number floating in space.
            if (floor == 4)
                RouteBox(root, "TopArrivalFrontWall", floor, wallMaterial, 0, 8, y, y + height, 0, .4f);
            var barrier = RouteBox(root, "BulletTransparentBoundary", floor, null, 0, 48, y, y + height, -.4f, 0, barrierLayer);
            Object.DestroyImmediate(barrier.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(barrier.GetComponent<MeshFilter>());

            // Functional cover furniture is authored together with the midway art.

            if (floor == 0) BuildStartRoom(root);
            if (floor == 2) BuildBlockoutDoor(root, floor, y);
            var checkpoint = ResetGroup(root, "Checkpoint");
            checkpoint.position = ToWorld(GetX(floor, 8.8f), y + .15f, 12.3f);
            var trigger = checkpoint.gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true; trigger.size = config.ToUnits(1.2f) * Vector3.one;
            checkpoint.gameObject.AddComponent<RespawnCheckpoint>();
        }


        private static void BuildStartRoom(Transform floor)
        {
            var room=ResetGroup(floor,"StartRoom");
            // Одна сплошная фронтальная стена вместо трёх.
            //
            // Раньше старт закрывали фронтальная стена p0..8.1 и две перегородки
            // поперёк выхода, p7.8..8.1 и p10.1..10.4. Перегородки стояли прямо
            // напротив шеренги спаунов, упирались игроку в лицо на первом же кадре
            // и держали косой луч с лифта только потому, что физически перегораживали
            // комнату.
            //
            // Ту же работу делает удлинённая фронтальная стена, и делает её на той
            // грани, откуда вообще стреляют. Худший луч идёт от дальнего угла
            // площадки лифта (18.72, -10.08) к дальнему спауну (1.05, 9.0): плоскость
            // фасада он пересекает на x = 9.4 м. Стена доведена до x = 10.8 м
            // (p 15), то есть с запасом 1.4 м; проверяется лучами в ValidateBlockout.
            RouteBox(room,"StartFrontWall",0,wallMaterial,0,StartFrontWallEnd,0,8,0,.40f,coverLayer);
        }

        private static void BuildFinalParkour(Transform root, float y)
        {
            // The run is 2.16 m above a continuous SAME-floor recovery deck.
            // Too high to jump onto from below; the only normal entry is the start ramp.
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "FinalStartRamp"; ramp.layer = groundLayer; ramp.transform.SetParent(root, false);
            Vector3 from = ToWorld(8, y, 11.5f), to = ToWorld(13, y + 3, 11.5f);
            Quaternion rotation = Quaternion.LookRotation(to - from, Vector3.up);
            ramp.transform.rotation = rotation;
            ramp.transform.position = (from + to) * .5f - rotation * Vector3.up * .1f;
            ramp.transform.localScale = new Vector3(config.ToUnits(3), .2f, Vector3.Distance(from, to));
            ramp.GetComponent<Renderer>().sharedMaterial = platformMaterial;
            // Короткая центральная ветка: меньше прыжков, но два синхронных
            // маятника выбивают зазевавшегося на recovery deck этого же этажа.
            RouteBox(root, "ParkourSplitDeck", 4, platformMaterial, 13, 16,
                y + 2.6f, y + 3, 9.5f, 14);
            RouteBox(root, "SplitDeckSupport", 4, metalMaterial, 14.2f, 14.8f,
                y, y + 2.6f, 11.5f, 12.1f);

            float[] fastP = { 19, 25, 31, 37 };
            float[] fastZ = { 6.5f, 3.5f, 6f, 9f };
            for (int i = 0; i < fastP.Length; i++)
            {
                RouteBox(root, $"FastParkourPad_{i + 1}", 4, platformMaterial, fastP[i], fastP[i] + 3,
                    y + 2.6f, y + 3, fastZ[i], fastZ[i] + 3);
                RouteBox(root, $"FastPadSupport_{i + 1}", 4, metalMaterial, fastP[i] + 1.2f, fastP[i] + 1.8f,
                    y, y + 2.6f, fastZ[i] + 1.2f, fastZ[i] + 1.8f);
            }

            // Длинная ветка у задней стены: площадок больше, зато они шире и
            // без маятников. Восемь игроков могут разойтись по двум маршрутам,
            // а не закупорить одну цепочку.
            float[] safeP = { 17, 21, 25, 29, 33, 37, 41 };
            float[] safeZ = { 10, 11, 10, 11, 10, 11, 10 };
            for (int i = 0; i < safeP.Length; i++)
            {
                RouteBox(root, $"SafeParkourPad_{i + 1}", 4, floorMaterial, safeP[i], safeP[i] + 2.8f,
                    y + 1.8f, y + 2.2f, safeZ[i], Mathf.Min(14, safeZ[i] + 4));
                RouteBox(root, $"SafePadSupport_{i + 1}", 4, metalMaterial, safeP[i] + 1.15f, safeP[i] + 1.65f,
                    y, y + 1.8f, safeZ[i] + 1.7f, safeZ[i] + 2.3f);
            }

            BuildFinalPendulum(root, y, 25.7f, 5.2f, 0f);
            BuildFinalPendulum(root, y, 34.0f, 7.4f, 1.55f);
            RouteBox(root, "RaisedFinishDeck", 4, platformMaterial, 43, 48, y + 2.6f, y + 3, 8, 14);
            // Low paint arrows indicate the walk back without shielding against bullets.
            for (int p0 = 12; p0 <= 42; p0 += 6)
            {
                var stripe = RouteBox(root, $"ReturnLane_{p0}", 4, platformMaterial, p0, p0 + 1,
                    y + .003f, y + .02f, 1.3f, 1.55f);
                Object.DestroyImmediate(stripe.GetComponent<Collider>());
            }
        }

        private static void BuildFinalPendulum(Transform root, float y, float progress, float depth, float phase)
        {
            Transform pivot = ResetGroup(root, $"CandyPendulumPivot_{progress:0}");
            pivot.position = ToWorld(GetX(4, progress), y + 7.1f, depth);

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = $"CandyPendulum_{progress:0}";
            head.layer = groundLayer;
            head.transform.SetParent(root, false);
            head.transform.position = pivot.position - Vector3.up * config.ToUnits(3.05f);
            head.transform.localScale = Vector3.one * config.ToUnits(1.25f);
            var sphere = head.GetComponent<SphereCollider>();
            sphere.isTrigger = true;
            head.GetComponent<Renderer>().sharedMaterial = trapMaterial;

            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "CandyPendulumStem";
            stem.transform.SetParent(head.transform, false);
            stem.transform.localPosition = Vector3.up * config.ToUnits(1.52f);
            stem.transform.localScale = new Vector3(.12f, config.ToUnits(1.52f), .12f);
            Object.DestroyImmediate(stem.GetComponent<Collider>());
            stem.GetComponent<Renderer>().sharedMaterial = metalMaterial;

            var swing = head.AddComponent<PendulumSwing>();
            var so = new SerializedObject(swing);
            so.FindProperty("pivot").objectReferenceValue = pivot;
            so.FindProperty("length").floatValue = config.ToUnits(3.05f);
            so.FindProperty("amplitudeDegrees").floatValue = 52f;
            so.FindProperty("period").floatValue = 3.4f;
            so.FindProperty("phaseOffset").floatValue = phase;
            so.FindProperty("impactForce").floatValue = 15f;
            so.FindProperty("impactUpward").floatValue = .28f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildBlockoutTransition(Transform root, int floor, float y)
        {
            var stairsRoot = ResetGroup(root, "StairRoom");
            RouteBox(stairsRoot, "ProtectedFront", floor, wallMaterial, 40, 48, y, y + 8, 0, .4f);
            // Перегородка и перемычка лестничной комнаты тоже идут на полный шаг:
            // ProtectedFront рядом всегда строился на y..y+8, и на их стыке
            // получалась ступенька в 0.72 м.
            RouteBox(stairsRoot, "ExitPartition", floor, wallMaterial, 39.6f, 40, y, y + 8, 0, 10);
            RouteBox(stairsRoot, "ExitHeader", floor, wallMaterial, 39.6f, 40, y + 3.5f, y + 8, 10, 14);
            var steps = new List<Transform>();
            // Collision uses two continuous slopes; bot markers sample their actual surfaces.
            // TransitionBase already occupies this exact plane. Reuse it as the
            // first route surface instead of laying a second coplanar slab over it.
            Transform transitionBase = root.Find("TransitionBase");
            if (transitionBase == null)
                throw new System.InvalidOperationException("Duck Hunt transition base is missing.");
            steps.Add(transitionBase);
            BuildBlockoutRamp(stairsRoot, floor, y, 40.3f, 43.7f, 11, 2, 0, 4, steps);
            steps.Add(RouteBox(stairsRoot, "TurnLanding", floor, platformMaterial, 40.3f, 47.7f, y + 3.7f, y + 4, .5f, 2).transform);
            BuildBlockoutRamp(stairsRoot, floor, y, 44.3f, 47.7f, 2, 11, 4, 8, steps);
            // Last waypoint lies on the next floor's arrival slab (not duplicate geometry).
            var exit = new GameObject("RampExit").transform;
            exit.SetParent(stairsRoot, false); exit.position = ToWorld(GetX(floor, 46), y + 8, 12);
            exit.localScale = Vector3.zero; steps.Add(exit);
            var stairs = stairsRoot.gameObject.AddComponent<DuckHuntStairs>();
            var so = new SerializedObject(stairs);
            var array = so.FindProperty("steps"); array.arraySize = steps.Count;
            for (int i = 0; i < steps.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = steps[i];
            so.ApplyModifiedPropertiesWithoutUndo(); builtStairs.Add(stairs);
        }

        private static void BuildBlockoutRamp(Transform root, int floor, float y, float p0, float p1,
            float z0, float z1, float h0, float h1, List<Transform> steps)
        {
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = $"Ramp_{h0}"; ramp.layer = groundLayer;
            ramp.AddComponent<Igruha.Core.Player.WalkableRamp>();
            ramp.transform.SetParent(root, false);
            var start = ToWorld(GetX(floor, (p0 + p1) * .5f), y + h0, z0);
            var end = ToWorld(GetX(floor, (p0 + p1) * .5f), y + h1, z1);
            var rotation = Quaternion.LookRotation(end - start, Vector3.up);
            ramp.transform.rotation = rotation;
            float thickness = config.ToUnits(.3f);
            ramp.transform.position = (start + end) * .5f - rotation * Vector3.up * thickness * .5f;
            ramp.transform.localScale = new Vector3(config.ToUnits(p1 - p0), thickness, Vector3.Distance(start, end));
            ramp.GetComponent<Renderer>().sharedMaterial = platformMaterial;

            BuildRampVisualSteps(root, ramp.name, start, end, config.ToUnits(p1 - p0));
            BuildRampCurbs(root, ramp.name, start, end, config.ToUnits(p1 - p0), thickness);
            for (int i = 1; i <= 16; i++)
            {
                var point = new GameObject($"RampWaypoint_{h0}_{i}").transform;
                point.SetParent(root, false); point.position = Vector3.Lerp(start, end, i / 16f);
                point.localScale = Vector3.zero; steps.Add(point);
            }
        }

        private static void BuildRampVisualSteps(Transform root, string rampName, Vector3 start, Vector3 end, float width)
        {
            for (int i = 0; i < StairVisualStepCount; i++)
            {
                float t0 = i / (float)StairVisualStepCount;
                float t1 = (i + 1) / (float)StairVisualStepCount;
                Vector3 from = Vector3.Lerp(start, end, t0);
                Vector3 to = Vector3.Lerp(start, end, t1);
                float top = to.y + .012f;
                float bottom = Mathf.Min(from.y, to.y) - .035f;

                var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.name = $"{rampName}_VisualStep_{i + 1:00}";
                step.transform.SetParent(root, false);
                step.transform.position = new Vector3(start.x, (top + bottom) * .5f, (from.z + to.z) * .5f);
                step.transform.localScale = new Vector3(width, top - bottom, Mathf.Abs(to.z - from.z) + .018f);
                Object.DestroyImmediate(step.GetComponent<Collider>());
                step.GetComponent<Renderer>().sharedMaterial = platformMaterial;
            }
        }

        private static void BuildRampCurbs(Transform root, string rampName, Vector3 start, Vector3 end,
            float width, float rampThickness)
        {
            Vector3 course = end - start;
            Quaternion rotation = Quaternion.LookRotation(course, Vector3.up);
            for (int side = -1; side <= 1; side += 2)
            {
                var curb = GameObject.CreatePrimitive(PrimitiveType.Cube);
                curb.name = $"{rampName}_Curb_{(side < 0 ? "L" : "R")}";
                curb.layer = groundLayer;
                curb.transform.SetParent(root, false);
                curb.transform.rotation = rotation;
                curb.transform.position = (start + end) * .5f + Vector3.right * side * (width - config.ToUnits(StairCurbWidth)) * .5f
                    + rotation * Vector3.up * config.ToUnits(StairCurbHeight) * .5f;
                curb.transform.localScale = new Vector3(config.ToUnits(StairCurbWidth),
                    config.ToUnits(StairCurbHeight), Vector3.Distance(start, end) + rampThickness);
                curb.GetComponent<Renderer>().sharedMaterial = metalMaterial;
            }
        }

        private static void BuildBlockoutDoor(Transform root, int floor, float y)
        {
            var group = ResetGroup(root, "DoorTrap");
            var trap = group.gameObject.AddComponent<DoorTrap>();
            var panel = RouteBox(group, "Panel", floor, trapMaterial, 39.5f, 40, y, y + 3.5f, 10, 14);
            var closed = panel.transform.localPosition;
            // Retracts upward into the header; never invades the floor below.
            var open = closed + Vector3.up * config.ToUnits(3.5f);
            var so = new SerializedObject(trap);
            so.FindProperty("doorBody").objectReferenceValue = panel.transform;
            so.FindProperty("closedLocalPosition").vector3Value = closed;
            so.FindProperty("openLocalPosition").vector3Value = open;
            so.FindProperty("closedDuration").floatValue = config.DoorClosedDuration;
            so.FindProperty("cooldown").floatValue = config.ButtonCooldown;
            so.FindProperty("moveDuration").floatValue = .3f;
            so.ApplyModifiedPropertiesWithoutUndo(); panel.transform.localPosition = open;
            builtTraps.Add(trap);
            BuildBlockoutControl(root, floor, y, "Door", "Закрыть выход", trap, 1.5f, 12.8f);
        }

        private static void GetBlockoutCollapseRange(int floor, out float start, out float end)
        {
            // Floor 3 keeps a short permanent landing before its separate door.
            start = floor == 2 ? 29f : CollapseStart;
            end = floor == 2 ? 37.5f : CollapseEnd;
        }

        private static void BuildBlockoutCollapse(Transform root, int floor, float y, float start, float end)
        {
            var group = ResetGroup(root, "CollapseTrap");
            var trap = group.gameObject.AddComponent<CollapsingFloorTrap>();
            var sections = new GameObject[CollapseSectionCount];
            var warnings = new GameObject[CollapseSectionCount];
            float sectionLength = (end - start) / CollapseSectionCount;
            for (int i = 0; i < CollapseSectionCount; i++)
            {
                float p0 = start + i * sectionLength;
                float p1 = p0 + sectionLength;
                sections[i] = RouteBox(group, $"Section_{i + 1}", floor, trapMaterial, p0, p1, y - 1, y, 0, 14);
                warnings[i] = RouteBox(group, $"CollapseWarning_{i + 1}", floor, buttonReadyMaterial,
                    p0 + .08f, p1 - .08f, y + .01f, y + .055f, .25f, 13.75f);
                Object.DestroyImmediate(warnings[i].GetComponent<Collider>());
                warnings[i].SetActive(false);
            }
            var so = new SerializedObject(trap);
            var cols = so.FindProperty("floorColliders"); cols.arraySize = sections.Length;
            var renderers = so.FindProperty("floorRenderers"); renderers.arraySize = sections.Length;
            var sectionObjects = so.FindProperty("floorSections"); sectionObjects.arraySize = sections.Length;
            var warningObjects = so.FindProperty("warningObjects"); warningObjects.arraySize = warnings.Length;
            for (int i = 0; i < sections.Length; i++)
            {
                cols.GetArrayElementAtIndex(i).objectReferenceValue = sections[i].GetComponent<Collider>();
                renderers.GetArrayElementAtIndex(i).objectReferenceValue = sections[i].GetComponent<Renderer>();
                sectionObjects.GetArrayElementAtIndex(i).objectReferenceValue = sections[i];
                warningObjects.GetArrayElementAtIndex(i).objectReferenceValue = warnings[i];
            }
            so.FindProperty("openDuration").floatValue = config.CollapseDuration;
            so.FindProperty("warningDuration").floatValue = CollapseWarningDuration;
            so.FindProperty("sectionDelay").floatValue = CollapseSectionDelay;
            so.FindProperty("cooldown").floatValue = config.ButtonCooldown;
            so.ApplyModifiedPropertiesWithoutUndo();
            builtTraps.Add(trap);
            BuildBlockoutControl(root, floor, y, "Collapse", "Уронить пол", trap,
                floor == 2 ? 6f : floor == 1 ? 5f : 4.5f, floor == 3 ? 13.3f : 12.8f);
        }

        private static void BuildBlockoutControl(Transform root, int floor, float y, string key, string label,
            TrapBase trap, float progress, float depth)
        {
            // Control lives on the stable arrival landing, behind ArrivalGuard.
            var lever = ResetGroup(root, $"Lever_{key}");
            var pedestal = RouteBox(lever, "Pedestal", floor, wallMaterial,
                progress - .5f, progress + .5f, y, y + 1.2f, depth - .5f, depth + .5f);
            AttachLever(lever, trap, label, pedestal.transform.position + Vector3.up * config.ToUnits(.75f),
                new Vector3(0, 0, -42), new Vector3(0, 0, 42));
        }

        private static DuckHuntFinishZone BuildBlockoutFinish(Transform tower)
        {
            var root = ResetGroup(tower, "FinishZone");
            float y = 4 * config.FloorStepWidths + 3;
            root.position = ToWorld(45.5f, y, 11);
            RouteBox(root, "FinishPad", 4, buttonReadyMaterial, 44, 47, y, y + .05f, 8, 14);
            var box = root.gameObject.AddComponent<BoxCollider>(); box.isTrigger = true;
            box.center = ToWorld(0, 1.5f, 0); box.size = ToWorld(3, 3, 6);
            var finish = root.gameObject.AddComponent<DuckHuntFinishZone>();

            var confettiObject = new GameObject("FinishConfetti");
            confettiObject.transform.SetParent(root, false);
            confettiObject.transform.localPosition = Vector3.up * 1.25f;
            var confetti = confettiObject.AddComponent<ParticleSystem>();
            confetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = confetti.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(.05f, .13f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, .2f, .25f), new Color(.2f, .75f, 1f));
            main.gravityModifier = .55f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 256;
            var emission = confetti.emission;
            emission.rateOverTime = 0;
            var shape = confetti.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 32f;
            shape.radius = 1.3f;
            var particleRenderer = confetti.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            particleRenderer.lengthScale = 1.8f;
            particleRenderer.sharedMaterial = platformMaterial;

            var celebration = root.gameObject.AddComponent<DuckHuntFinishCelebration>();
            var celebrationSo = new SerializedObject(celebration);
            celebrationSo.FindProperty("confetti").objectReferenceValue = confetti;
            celebrationSo.ApplyModifiedPropertiesWithoutUndo();
            return finish;
        }

        private static void ValidateBlockout(GameObject root)
        {
            Physics.SyncTransforms();
            if (builtTraps.Count != 4 || builtStairs.Count != 4 || builtGeysers.Count != 0)
                throw new System.InvalidOperationException("Invalid DuckHunt blockout topology.");
            int mask = (1 << groundLayer) | (1 << coverLayer);
            Transform platform = root.transform.Find("ElevatorShaft/Platform");
            var platformRenderer = platform != null ? platform.GetComponentInChildren<MeshRenderer>() : null;
            if (platformRenderer == null)
                throw new System.InvalidOperationException("Duck Hunt lift platform is missing.");
            Bounds pad = platformRenderer.bounds;
            for (int floor = 1; floor <= 3; floor++)
            {
                var floorRoot = root.transform.Find($"Tower/Floor_{floor + 1}");
                var levers = floorRoot.GetComponentsInChildren<TrapLever>();
                if (levers.Length != (floor == 2 ? 2 : 1))
                    throw new System.InvalidOperationException($"Floor {floor + 1} has the wrong number of trap controls.");
                if (levers.Length == 2 &&
                    Vector3.Distance(levers[0].transform.Find("Pedestal").position,
                        levers[1].transform.Find("Pedestal").position) < config.ToUnits(4f))
                    throw new System.InvalidOperationException("Floor 3 controls are too close together.");
                foreach (var lever in levers)
                {
                    var button = lever.GetComponent<TrapActivationButton>();
                    if (lever.GetComponent<Unity.Netcode.NetworkObject>() == null || button == null)
                        throw new System.InvalidOperationException($"Floor {floor + 1} control lacks network interaction.");
                    var linkedTrap = new SerializedObject(lever).FindProperty("trap").objectReferenceValue;
                    var buttonTraps = new SerializedObject(button).FindProperty("traps");
                    if (linkedTrap == null || buttonTraps.arraySize != 1 ||
                        buttonTraps.GetArrayElementAtIndex(0).objectReferenceValue != linkedTrap)
                        throw new System.InvalidOperationException($"Floor {floor + 1} control {lever.name} points to the wrong trap.");
                    var pedestal = lever.transform.Find("Pedestal");
                    if (pedestal == null)
                        throw new System.InvalidOperationException($"Floor {floor + 1} control has no pedestal.");
                    ValidateControlSafety(floor, lever, pedestal.GetComponent<Collider>(), pad, mask);
                }
            }

            ValidateStartProtection(mask);
            ValidateCollapseLength();
            ValidateBlockoutFloorSurfaces(root);
        }

        private static void ValidateControlSafety(int floor, TrapLever lever, Collider pedestal, Bounds pad, int mask)
        {
            if (pedestal == null)
                throw new System.InvalidOperationException($"Floor {floor + 1} control {lever.name} has no collider.");
            float y = config.ToUnits(floor * config.FloorStepWidths);
            float stanceZ = pedestal.bounds.min.z - .55f;
            float[] padX = { pad.min.x, pad.center.x, pad.max.x };
            float[] padZ = { pad.min.z, pad.center.z, pad.max.z };
            foreach (float side in new[] { -.32f, 0f, .32f })
            {
                Vector3 feet = new Vector3(pedestal.bounds.center.x + side, y, stanceZ);
                Vector3 bottom = feet + Vector3.up * .31f;
                Vector3 top = feet + Vector3.up * 1.45f;
                if (Physics.CheckCapsule(bottom, top, .29f, mask, QueryTriggerInteraction.Ignore))
                {
                    var hits = Physics.OverlapCapsule(bottom, top, .29f, mask, QueryTriggerInteraction.Ignore);
                    throw new System.InvalidOperationException($"Floor {floor + 1} control {lever.name} has no standing space at {feet}: {string.Join(", ", System.Array.ConvertAll(hits, hit => hit.name))}.");
                }
                if (Vector3.Distance(pedestal.ClosestPoint(feet + Vector3.up * .8f), feet + Vector3.up * .8f) > 1.8f)
                    throw new System.InvalidOperationException($"Floor {floor + 1} control {lever.name} is out of interaction reach.");
                foreach (float bodyHeight in new[] { .65f, 1.35f, 1.7f })
                {
                    Vector3 target = feet + Vector3.up * bodyHeight;
                    for (float liftY = config.ElevatorMinHeightUnits; liftY <= config.ElevatorMaxHeightUnits + .01f; liftY += 1.5f)
                    foreach (float x in padX)
                    foreach (float z in padZ)
                    {
                        Vector3 hunter = new Vector3(x, liftY + 1.6f, z);
                        if (!Physics.Linecast(hunter, target, mask, QueryTriggerInteraction.Ignore))
                            throw new System.InvalidOperationException(
                                $"Floor {floor + 1} control {lever.name} is exposed from lift at {hunter}.");
                    }
                }
            }
        }

        private static void ValidateBlockoutFloorSurfaces(GameObject arena)
        {
            int mask = 1 << groundLayer;
            for (int floor = 1; floor <= 3; floor++)
            {
                var floorRoot = arena.transform.Find($"Tower/Floor_{floor + 1}");
                var trap = floorRoot.Find("CollapseTrap").GetComponent<CollapsingFloorTrap>();
                GetBlockoutCollapseRange(floor, out float start, out float end);
                float y = config.ToUnits(floor * config.FloorStepWidths);
                Physics.SyncTransforms();
                for (float p = 8.25f; p < 40f; p += .25f)
                {
                    float x = config.ToUnits(GetX(floor, p));
                    if (!Physics.Raycast(new Vector3(x, y + .35f, config.ToUnits(1)),
                            Vector3.down, .8f, mask, QueryTriggerInteraction.Ignore))
                        throw new System.InvalidOperationException($"Floor {floor + 1} has a permanent gap at p={p:F2}.");
                }
                try
                {
                    trap.SetOpen(true);
                    Physics.SyncTransforms();
                    for (float p = start + .25f; p < end; p += .25f)
                    {
                        float x = config.ToUnits(GetX(floor, p));
                        if (Physics.Raycast(new Vector3(x, y + .35f, config.ToUnits(1)),
                                Vector3.down, .8f, mask, QueryTriggerInteraction.Ignore))
                            throw new System.InvalidOperationException($"Floor {floor + 1} trap has a fixed support at p={p:F2}.");
                    }
                }
                finally
                {
                    trap.SetOpen(false);
                    Physics.SyncTransforms();
                }
            }
        }

        private static void ValidateCollapseLength()
        {
            var character = AssetDatabase.LoadAssetAtPath<Igruha.Core.Player.CharacterConfig>(CharacterConfigPath);
            if (character == null)
            {
                throw new System.InvalidOperationException("CharacterConfig is missing; cannot validate Duck Hunt collapse.");
            }

            float gravity = Mathf.Abs(Physics.gravity.y);
            float riseTime = character.JumpSpeed / (gravity * character.RiseGravityMultiplier);
            float jumpHeight = character.JumpSpeed * character.JumpSpeed /
                (2f * gravity * character.RiseGravityMultiplier);
            float fallTime = Mathf.Sqrt(2f * jumpHeight / (gravity * character.FallGravityMultiplier));
            float maximumJump = character.MaxSpeed * (riseTime + fallTime);
            float requiredMargin = config.ToUnits(1.5f);
            for (int floor = 1; floor <= 3; floor++)
            {
                GetBlockoutCollapseRange(floor, out float start, out float end);
                float trapLength = config.ToUnits(end - start);
                if (trapLength < maximumJump + requiredMargin)
                    throw new System.InvalidOperationException(
                        $"Duck Hunt floor {floor + 1} collapse is jumpable: {trapLength:F2}m, needs at least {maximumJump + requiredMargin:F2}m.");
            }
        }

        /// <summary>
        /// Ни один спаун Уток не простреливается с площадки лифта.
        ///
        /// Проверка вернулась из удалённой легаси-ветки намеренно. Защиту старта
        /// держит одна фронтальная стена, и её длина — расчётная величина, а не
        /// на глаз: стоит кому-нибудь подвинуть шеренгу спаунов, лифт или саму
        /// стену, и Охотник начнёт снимать всех восьмерых с первого кадра, причём
        /// молча — ни один другой валидатор этого не ловит.
        /// </summary>
        private static void ValidateStartProtection(int mask)
        {
            Transform ducks = GameObject.Find("_Spawns")?.transform.Find("Ducks");
            if (ducks == null || ducks.childCount == 0)
                throw new System.InvalidOperationException("Duck Hunt spawns are missing.");
            Transform platform = GameObject.Find("_Arena")?.transform.Find("ElevatorShaft/Platform");
            var platformRenderer = platform != null ? platform.GetComponentInChildren<MeshRenderer>() : null;
            if (platformRenderer == null)
                throw new System.InvalidOperationException("Duck Hunt lift platform is missing.");
            Bounds pad = platformRenderer.bounds;
            // Углы площадки, а не её центр: косой луч из угла проходит дальше всех.
            float[] padX = { pad.min.x, pad.center.x, pad.max.x };
            float[] padZ = { pad.min.z, pad.center.z, pad.max.z };
            float top = config.FloorCount * config.FloorStepUnits + 2f;
            // Рост: ступни, грудь, голова. Ширина тела — половина капсулы.
            float[] eyes = { .35f, 1.0f, 1.6f };
            float[] spread = { -.3f, 0f, .3f };
            foreach (Transform spawn in ducks)
            {
                foreach (float eye in eyes)
                foreach (float dx in spread)
                foreach (float dz in spread)
                {
                    Vector3 target = spawn.position + new Vector3(dx, eye, dz);
                    for (float y = -1.5f; y <= top; y += 1.5f)
                    {
                        foreach (float x in padX)
                        foreach (float z in padZ)
                        {
                            Vector3 hunter = new Vector3(x, y + 1.6f, z);
                            if (!Physics.Linecast(hunter, target, mask, QueryTriggerInteraction.Ignore))
                                throw new System.InvalidOperationException(
                                    $"Start spawn {spawn.name} is exposed from the lift at {hunter}.");
                        }
                    }
                }
            }
        }
    }
}
