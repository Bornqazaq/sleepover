using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Core.UI;
using Igruha.Minigames.MemoryRun;

namespace Igruha.EditorTools
{
    internal static class MemoryFoundryProduction
    {
        internal static void DressGate(GameObject gate, MemoryRunConfig config)
        {
            gate.GetComponent<Renderer>().enabled = false;
            var root = new GameObject("LaminatedGlass").transform;
            root.SetParent(gate.transform, false);
            var scale = gate.transform.lossyScale;
            root.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
            root.position = new Vector3(0, config.StartZoneLift, config.GateZ);
            string path = MemoryFoundryAssets.Materials + "/MF_ObservationGlass.mat";
            var glass = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (glass == null)
            {
                glass = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(glass, path);
            }
            Igruha.Minigames.HoleInWall.HoleInWallMaterials.ConfigureTransparent(glass,
                new Color(.47f, .78f, .86f, .72f), .93f);
            glass.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(MemoryFoundryAssets.Art + "/Textures/MF_Glass.png"));
            glass.SetFloat("_Cull", (float)CullMode.Off);
            glass.SetFloat("_Metallic", .05f);
            EditorUtility.SetDirty(glass);
            int panels = Mathf.CeilToInt(config.HallWidth / 3.6f);
            float width = config.HallWidth / panels;
            float height = config.GateHeight;
            for (int i = 0; i < panels; i++)
            {
                float x = -config.HallWidth / 2 + (i + .5f) * width;
                var pane = GameObject.CreatePrimitive(PrimitiveType.Quad); pane.name = "ObservationPane";
                Object.DestroyImmediate(pane.GetComponent<Collider>());
                pane.transform.SetParent(root, false);
                pane.transform.localPosition = new Vector3(x, height / 2, -.025f);
                pane.transform.localScale = new Vector3(width - .065f, height - .10f, 1);
                pane.GetComponent<Renderer>().sharedMaterial = glass;
                pane.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                foreach (float y in new[] { .08f, height - .06f })
                    MemoryFoundryBuilder.Box(root, "GlassEdge", new Vector3(x, y, -.01f), new Vector3(width - .025f, .035f, .07f), "Steel");
                foreach (float side in new[] { -1f, 1f })
                {
                    float postX = x + side * (width / 2 - .08f);
                    MemoryFoundryBuilder.Box(root, "GlassClamp", new Vector3(postX, .16f, 0), new Vector3(.17f, .32f, .18f), "Iron");
                    MemoryFoundryBuilder.Box(root, "GlassEdgeLight", new Vector3(postX, .4f, -.11f), new Vector3(.024f, .20f, .024f), "Signal");
                }
            }
            foreach (var child in root.GetComponentsInChildren<Transform>()) child.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        }

        internal static void DressMarker(GameObject manager)
        {
            var game = new SerializedObject(manager.GetComponent<MemoryRunMinigame>());
            var marker = game.FindProperty("activeMarker").objectReferenceValue as ActivePlayerMarker;
            if (marker == null) throw new System.InvalidOperationException("MemoryRun active marker is missing.");
            var so = new SerializedObject(marker);
            var previous = so.FindProperty("visual").objectReferenceValue as Transform;
            if (previous != null && previous.IsChildOf(marker.transform)) Object.DestroyImmediate(previous.gameObject);
            var visual = MemoryFoundryAssets.Place(marker.transform, "TurnSignal", Vector3.zero);
            visual.gameObject.AddComponent<MemoryFoundryMarkerView>();
            so.FindProperty("visual").objectReferenceValue = visual;
            so.ApplyModifiedPropertiesWithoutUndo();
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        internal static void BuildLines(Transform parent, MemoryRunConfig config)
        {
            const int bays = 12;
            float length = config.StepPitch * bays;
            float centreZ = config.StepZ(0) + 3.5f * config.StepPitch;
            foreach (int side in new[] { -1, 1, 0 })
            {
                bool feeder = side == 0;
                float run = feeder ? 12.96f : length;
                int modules = feeder ? 6 : bays * 2;
                var root = new GameObject(feeder ? "ProductionLine_UpperFeeder" : side < 0 ? "ProductionLine_Left" : "ProductionLine_Right").transform;
                root.SetParent(parent, false);
                root.localPosition = feeder ? new Vector3(13.8f, 7.4f, 3) : new Vector3(side * (config.HallWidth / 2 - 5.1f), 1.22f, centreZ);
                root.localRotation = Quaternion.Euler(0, feeder ? 18 : side < 0 ? 0 : 180, 0);
                var path = new Vector3[129];
                float pathLength = 0;
                for (int i = 0; i < path.Length; i++)
                {
                    path[i] = BeltPoint(i / (float)(path.Length - 1), run, side);
                    if (i > 0) pathLength += Vector3.Distance(path[i - 1], path[i]);
                }
                var rollers = new List<Transform>();
                for (int i = 0; i < modules; i++)
                {
                    float t = (i + .5f) / modules;
                    var point = BeltPoint(t, run, side);
                    var delta = BeltPoint((i + 1f) / modules, run, side) - BeltPoint(i / (float)modules, run, side);
                    var bed = MemoryFoundryAssets.Place(root, "ConveyorBed", point);
                    bed.localRotation = Quaternion.LookRotation(delta);
                    bed.localScale = new Vector3(1, 1, delta.magnitude / config.StepPitch);
                    rollers.Add(MemoryFoundryAssets.Place(root, "ConveyorRoller", point + Vector3.down * .32f));
                    if (i % 2 != 0) continue;
                    Vector3 world = root.TransformPoint(point);
                    float wallX = (side < 0 ? -1 : 1) * (config.HallWidth / 2 - .7f);
                    float lineX = world.x;
                    MemoryFoundryBuilder.Box(parent, "ConveyorCantilever",
                        new Vector3((wallX + lineX) / 2, world.y - .85f, world.z),
                        new Vector3(Mathf.Abs(wallX - lineX) + 1.6f, .20f, .25f), "Iron");
                    var from = new Vector3(wallX, world.y - 3.2f, world.z);
                    var to = new Vector3(lineX, world.y - .85f, world.z);
                    var brace = MemoryFoundryBuilder.Box(parent, "ConveyorDiagonal", (from + to) / 2,
                        new Vector3(.18f, Vector3.Distance(from, to), .18f), "Steel");
                    brace.transform.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
                }
                foreach (int end in new[] { -1, 1 })
                {
                    var point = BeltPoint(end < 0 ? 0 : 1, run, side);
                    MemoryFoundryAssets.Place(root, "LoadingHood", point);
                    rollers.Add(MemoryFoundryAssets.Place(root, "ConveyorRoller", point + Vector3.down * .32f));
                }
                int linkCount = Mathf.CeilToInt((2 * pathLength + 2 * Mathf.PI * .32f) / .46f);
                var links = new Transform[linkCount];
                for (int i = 0; i < links.Length; i++) links[i] = MemoryFoundryAssets.Place(root, "BeltLink", Vector3.zero);
                var cargo = new Transform[feeder ? 8 : bays * 2];
                var random = new System.Random(972 + side);
                for (int i = 0; i < cargo.Length; i++)
                {
                    bool heavy = i % 3 == 1;
                    cargo[i] = MemoryFoundryAssets.Place(root, heavy ? "DynamiteHeavy" : "DynamiteBundle", Vector3.zero);
                    float scale = .65f + (float)random.NextDouble() * (heavy ? .38f : .65f);
                    cargo[i].localScale = new Vector3(scale, scale * (.85f + (float)random.NextDouble() * .35f), scale);
                }
                var conveyor = root.gameObject.AddComponent<MemoryFoundryConveyor>();
                var so = new SerializedObject(conveyor);
                Bind(so, "links", links); Bind(so, "cargo", cargo); Bind(so, "rollers", rollers.ToArray());
                so.FindProperty("length").floatValue = pathLength;
                so.FindProperty("speed").floatValue = feeder ? .48f : side < 0 ? .70f : .58f;
                var points = so.FindProperty("topPath"); points.arraySize = path.Length;
                for (int i = 0; i < path.Length; i++) points.GetArrayElementAtIndex(i).vector3Value = path[i];
                so.ApplyModifiedPropertiesWithoutUndo();
                conveyor.Pose(0);
                if (feeder) continue;
                // Only the parts over solid platforms obstruct players; the pit has no shortcut colliders.
                foreach (bool exit in new[] { false, true })
                {
                    float from = exit ? config.ExitPadZ : centreZ - length / 2 - 1.15f;
                    float to = exit ? centreZ + length / 2 + 1.15f : config.GateZ - .03f;
                    if (to <= from) continue;
                    var block = new GameObject("ProductionPlatformCollision"); block.transform.SetParent(parent, false);
                    block.transform.localPosition = new Vector3(root.localPosition.x, 1.15f, (from + to) / 2);
                    block.layer = LayerMask.NameToLayer("Ground");
                    var collider = block.AddComponent<BoxCollider>(); collider.size = new Vector3(2.35f, 2.3f, to - from);
                }
                var light = new GameObject("ProductionSpot").AddComponent<Light>();
                light.transform.SetParent(parent, false);
                light.transform.localPosition = new Vector3(root.localPosition.x, 7, config.ExitPadZ - 2);
                light.transform.rotation = Quaternion.LookRotation(new Vector3(root.localPosition.x, 1, config.ExitPadZ - 5) - light.transform.position);
                light.type = LightType.Spot; light.color = new Color(1, .76f, .46f);
                light.intensity = 12; light.range = 19; light.spotAngle = 80;
                light.shadows = LightShadows.Soft; light.shadowBias = .035f; light.shadowNormalBias = .15f;
            }
        }

        private static Vector3 BeltPoint(float t, float run, int side)
        {
            float rise = SmoothRange(t, side < 0 ? .20f : .12f, side < 0 ? .36f : .34f);
            float fall = SmoothRange(t, side < 0 ? .65f : .58f, side < 0 ? .82f : .84f);
            float height = side == 0 ? SmoothRange(t, .2f, .7f) * 1.8f : (rise - fall) * (side < 0 ? 2.8f : 5.2f);
            return new Vector3(0, height, (t - .5f) * run);
        }

        private static float SmoothRange(float value, float from, float to) =>
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(from, to, value));

        private static void Bind(SerializedObject so, string name, Transform[] values)
        {
            var array = so.FindProperty(name); array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
