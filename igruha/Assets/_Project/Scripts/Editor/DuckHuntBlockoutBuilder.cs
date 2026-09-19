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
            Debug.Log("Duck Hunt midway: functional prop covers, exposed levers, single jumps on floors 2-4, final parkour over recovery deck.");
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
                RouteBox(root, "ArrivalGuard", floor, wallMaterial, 7.7f, 8, y, y + 2, 0, 11);
            }

            if (floor == 4)
            {
                RouteBox(root, "RecoveryDeck", floor, floorMaterial, 8, 48, y - 1, y, 0, 14);
                BuildFinalParkour(root, y);
            }
            else
            {
                float end = floor == 3 ? 36 : 40;
                // Floor 4's collapse maps to p9..12 on floor 3: leave a solid landing below it.
                float jumpFrom = floor == 1 ? 34 : floor == 2 ? 13 : floor == 3 ? 10 : -1;
                if (jumpFrom < 0) RouteBox(root, "ContinuousRun", floor, floorMaterial, 8, end, y - 1, y, 0, 14);
                else
                {
                    RouteBox(root, "RunBeforeJump", floor, floorMaterial, 8, jumpFrom, y - 1, y, 0, 14);
                    RouteBox(root, "RunAfterJump", floor, floorMaterial, jumpFrom + 2.2f, end, y - 1, y, 0, 14);
                    var takeoffStripe = RouteBox(root, "TakeoffStripe", floor, platformMaterial, jumpFrom - .3f, jumpFrom,
                        y - .06f, y + .015f, 0, 14);
                    var stripeCollider = takeoffStripe.GetComponent<BoxCollider>();
                    if (stripeCollider != null) Object.DestroyImmediate(stripeCollider, true);
                }
                if (floor == 3)
                {
                    RouteBox(root, "ExitLip", floor, floorMaterial, 39, 40, y - 1, y, 0, 14);
                    BuildBlockoutCollapse(root, floor, y);
                }
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
            float[] p = { 13, 18, 23, 28, 33, 38 };
            float[] z = { 10, 8, 5.5f, 4, 6, 8 };
            for (int i = 0; i < p.Length; i++)
            {
                RouteBox(root, $"ParkourPad_{i + 1}", 4, platformMaterial, p[i], p[i] + 3,
                    y + 2.6f, y + 3, z[i], z[i] + 3);
                RouteBox(root, $"PadSupport_{i + 1}", 4, metalMaterial, p[i] + 1.2f, p[i] + 1.8f,
                    y, y + 2.6f, z[i] + 1.2f, z[i] + 1.8f);
            }
            RouteBox(root, "RaisedFinishDeck", 4, platformMaterial, 43, 48, y + 2.6f, y + 3, 8, 14);
            // Low paint arrows indicate the walk back without shielding against bullets.
            for (int p0 = 12; p0 <= 42; p0 += 6)
            {
                var stripe = RouteBox(root, $"ReturnLane_{p0}", 4, platformMaterial, p0, p0 + 1,
                    y + .003f, y + .02f, 1.3f, 1.55f);
                Object.DestroyImmediate(stripe.GetComponent<Collider>());
            }
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
            for (int i = 1; i <= 16; i++)
            {
                var point = new GameObject($"RampWaypoint_{h0}_{i}").transform;
                point.SetParent(root, false); point.position = Vector3.Lerp(start, end, i / 16f);
                point.localScale = Vector3.zero; steps.Add(point);
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
            builtTraps.Add(trap); BuildBlockoutControl(root, floor, y, "Door", "Закрыть выход", trap);
        }

        private static void BuildBlockoutCollapse(Transform root, int floor, float y)
        {
            var group = ResetGroup(root, "CollapseTrap");
            var trap = group.gameObject.AddComponent<CollapsingFloorTrap>();
            var panel = RouteBox(group, "Section", floor, trapMaterial, 36, 39, y - 1, y, 0, 14);
            var so = new SerializedObject(trap);
            var cols = so.FindProperty("floorColliders"); cols.arraySize = 1;
            cols.GetArrayElementAtIndex(0).objectReferenceValue = panel.GetComponent<Collider>();
            var renderers = so.FindProperty("floorRenderers"); renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = panel.GetComponent<Renderer>();
            so.FindProperty("openDuration").floatValue = config.CollapseDuration;
            so.FindProperty("cooldown").floatValue = config.ButtonCooldown;
            so.ApplyModifiedPropertiesWithoutUndo(); builtTraps.Add(trap);
            BuildBlockoutControl(root, floor, y, "Collapse", "Уронить пол у выхода", trap);
        }

        private static void BuildBlockoutControl(Transform root, int floor, float y, string key, string label, TrapBase trap)
        {
            // Latest design: activating the remote trap is a deliberate exposed stop.
            var lever = ResetGroup(root, $"Lever_{key}");
            var pedestal = RouteBox(lever, "Pedestal", floor, wallMaterial, 23.5f, 24.5f, y, y + 1.2f, 6.2f, 7.2f);
            AttachLever(lever, trap, label, pedestal.transform.position + Vector3.up * config.ToUnits(.75f),
                new Vector3(0, 0, -42), new Vector3(0, 0, 42));
            // Ground stripe communicates remote link without adding shot-blocking decoration.
            var stripe = RouteBox(root, $"Link_{key}", floor, trapMaterial, 25, 38.5f, y + .005f, y + .025f, 12.8f, 13);
            Object.DestroyImmediate(stripe.GetComponent<Collider>());
        }

        private static DuckHuntFinishZone BuildBlockoutFinish(Transform tower)
        {
            var root = ResetGroup(tower, "FinishZone");
            float y = 4 * config.FloorStepWidths + 3;
            root.position = ToWorld(45.5f, y, 11);
            RouteBox(root, "FinishPad", 4, buttonReadyMaterial, 44, 47, y, y + .05f, 8, 14);
            var box = root.gameObject.AddComponent<BoxCollider>(); box.isTrigger = true;
            box.center = ToWorld(0, 1.5f, 0); box.size = ToWorld(3, 3, 6);
            return root.gameObject.AddComponent<DuckHuntFinishZone>();
        }

        private static void ValidateBlockout(GameObject root)
        {
            if (builtTraps.Count != 2 || builtStairs.Count != 4 || builtGeysers.Count != 0)
                throw new System.InvalidOperationException("Invalid DuckHunt blockout topology.");
            int mask = (1 << groundLayer) | (1 << coverLayer);
            for (int floor = 2; floor <= 3; floor++)
            {
                float y = floor * config.FloorStepWidths;
                Vector3 buttonFeet = ToWorld(GetX(floor, 24), y, 8);
                for (float height = y; height <= y + 6; height += 2)
                {
                    Vector3 hunter = ToWorld(24, height + 2, -16);
                    Vector3 target = buttonFeet + Vector3.up * 1.5f;
                    if (Physics.Linecast(hunter, target, mask, QueryTriggerInteraction.Ignore))
                        throw new System.InvalidOperationException($"Floor {floor + 1} control must remain exposed.");
                }
            }

            ValidateStartProtection(mask);
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
