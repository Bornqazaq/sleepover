using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Igruha.Minigames.MemoryRun;

namespace Igruha.EditorTools
{
    internal static class MemoryRunArtAudit
    {
        [MenuItem("Igruha/Рейс на память/Замеры арта")]
        private static void Measure() { Debug.Log(Report()); }

        internal static string Report()
        {
            var c = AssetDatabase.LoadAssetAtPath<MemoryRunConfig>("Assets/_Project/Settings/Gameplay/Minigames/MemoryRunConfig.asset");
            var arena = GameObject.Find("_Arena");
            if (arena == null || c == null) throw new InvalidOperationException("Open and rebuild MemoryRun first.");
            Physics.SyncTransforms();
            var report = new StringBuilder("Memory Foundry audit\n");
            int errors = 0;
            void Check(bool pass, string message) { report.Append(pass ? "PASS " : "FAIL ").AppendLine(message); if (!pass) errors++; }
            var plates = arena.transform.Find("Plates");
            Mesh firstMesh = null; Material[] firstMaterials = null;
            Vector3 firstScale = Vector3.zero; Quaternion firstRotation = Quaternion.identity;
            int count = 0;
            for (int row = 0; row < c.Steps; row++)
            for (int lane = 0; lane < MemoryRunConfig.LaneCount; lane++)
            {
                var p = plates.Find($"Plate_{row:00}_{lane}");
                if (p == null) { Check(false, "Missing plate " + row + "/" + lane); continue; }
                var filter = p.Find("Dress").GetComponentInChildren<MeshFilter>();
                var renderer = filter.GetComponent<Renderer>();
                var collider = p.GetComponent<BoxCollider>();
                if (firstMesh == null)
                {
                    firstMesh = filter.sharedMesh; firstMaterials = renderer.sharedMaterials;
                    firstScale = filter.transform.lossyScale; firstRotation = filter.transform.rotation;
                }
                Check(filter.sharedMesh == firstMesh && renderer.sharedMaterials.SequenceEqual(firstMaterials)
                    && Vector3.Distance(filter.transform.lossyScale, firstScale) < .0001f
                    && Quaternion.Angle(filter.transform.rotation, firstRotation) < .001f,
                    "Identical plate " + row + "/" + lane);
                var b = renderer.bounds;
                Check(Mathf.Abs(p.position.x - c.LaneX(lane)) < .001f && Mathf.Abs(p.position.z - c.StepZ(row)) < .001f
                    && Mathf.Abs(collider.bounds.max.y - c.PlateSurfaceY) < .001f
                    && b.size.x <= collider.bounds.size.x + .002f && b.size.z <= collider.bounds.size.z + .002f
                    && Mathf.Abs(b.max.y - c.PlateSurfaceY) < .02f, "Visual, physics and server grid " + row + "/" + lane);
                count++;
            }
            Check(count == c.Steps * MemoryRunConfig.LaneCount, "All plates present");
            foreach (string deckName in new[] { "StartZone", "ExitPad" })
            {
                var deck = arena.transform.Find(deckName);
                Check(deck != null && DeckHasSingleWalkingSurface(deck.gameObject),
                    deckName + " has one visible walking surface; no coplanar base");
            }
            var game = GameObject.Find("MinigameManager").GetComponent<MemoryRunMinigame>();
            var gate = new SerializedObject(game).FindProperty("gate").objectReferenceValue as Component;
            Check(gate != null && gate.GetComponent<BoxCollider>() != null
                && gate.gameObject.layer == LayerMask.NameToLayer("Ignore Raycast"), "Queue gate wired; camera passes through");
            Check(gate != null && Mathf.Abs(gate.GetComponent<BoxCollider>().bounds.size.x - c.HallWidth) < .01f, "Gate spans the enlarged hall");
            var panes = gate.GetComponentsInChildren<MeshRenderer>().Where(r => r.name == "ObservationPane").ToArray();
            Check(!gate.GetComponent<Renderer>().enabled && panes.Length == Mathf.CeilToInt(c.HallWidth / 3.6f)
                && gate.GetComponentsInChildren<Collider>().Length == 1
                && panes.All(r => r.sharedMaterial.name == "MF_ObservationGlass"
                    && r.sharedMaterial.GetTexture("_BaseMap") != null && r.sharedMaterial.renderQueue == 3000),
                "Laminated glass is visible, transparent and adds no blocking colliders");
            var marker = new SerializedObject(game).FindProperty("activeMarker").objectReferenceValue as Component;
            var visual = new SerializedObject(marker).FindProperty("visual").objectReferenceValue as Transform;
            Check(visual != null && visual.GetComponentInChildren<MeshFilter>().sharedMesh.name == "MF_TurnSignal"
                && visual.GetComponentsInChildren<Collider>().Length == 0, "Original turn signal replaces the fallback cylinder");
            Check(visual.GetComponent<MemoryFoundryMarkerView>() != null, "Local marker has camera-clearance visibility guard");
            Check(new[] { "Signal", "Opal", "Window", "Exit" }.All(name =>
            {
                var material = MemoryFoundryAssets.Material(name);
                return material != null && material.IsKeywordEnabled("_EMISSION")
                    && (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.AnyEmissive) != 0;
            }), "Emission survives URP material validation");
            var kill = GameObject.Find("KillZone_Bottom").GetComponent<BoxCollider>();
            Check(kill.isTrigger && Mathf.Abs(kill.bounds.max.y + 6.14f) < .01f && kill.bounds.size.x >= c.HallWidth, "Death plane unchanged at -6.14m; full-width coverage");
            Check(GameObject.Find("ExitTrigger").GetComponent<BoxCollider>().isTrigger, "Finish trigger present");
            var effect = new SerializedObject(game.GetComponent<MemoryRunEffects>());
            var bursts = effect.FindProperty("blasts");
            Check(bursts.arraySize == 4 && Enumerable.Range(0, 4).All(i => bursts.GetArrayElementAtIndex(i).objectReferenceValue != null), "Four event-driven blast instances wired");
            var env = arena.transform.Find("_Environment");
            var conveyors = env.GetComponentsInChildren<MemoryFoundryConveyor>();
            Check(conveyors.Length == 3 && conveyors.All(line =>
            {
                var so = new SerializedObject(line);
                bool feeder = line.name.EndsWith("UpperFeeder");
                return so.FindProperty("cargo").arraySize == (feeder ? 8 : 24)
                    && so.FindProperty("links").arraySize > (feeder ? 50 : 200)
                    && so.FindProperty("rollers").arraySize == (feeder ? 8 : 26)
                    && so.FindProperty("topPath").arraySize == 129
                    && line.GetComponentsInChildren<Collider>().Length == 0;
            }), "Three elevated production loops, tread links, rollers and cargo wired");
            foreach (var line in conveyors) Check(ConveyorMotionValid(line), line.name + " continuous path, inclined motion and varied cargo");
            Check(arena.transform.Find("BottomlessShaft") != null && c.PitDepth >= 80
                && arena.GetComponentsInChildren<Transform>().All(t => t.name != "PitFloor"), "Bottomless shaft: no floor mesh or floor collider");
            Check(env.Find("FracturedShell") != null && env.Find("ExteriorFoundryDistrict") != null
                && env.GetComponentsInChildren<MeshFilter>().Count(f => f.sharedMesh.name.StartsWith("MF_Ruin")) >= 4,
                "Original breached walls, damaged roof and outside district wired");
            var wrecks = env.Find("ShaftWreckage");
            var wreckModels = wrecks.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh.name.StartsWith("MF_ShaftWreck")).ToArray();
            Check(wreckModels.Length == 28 && wreckModels.Select(f => f.sharedMesh.name).Distinct().Count() == 4
                && wreckModels.All(f => f.GetComponent<Renderer>().bounds.min.x > 7.5f || f.GetComponent<Renderer>().bounds.max.x < -7.5f)
                && wrecks.GetComponentsInChildren<Collider>().Length == 0,
                "Four kinds of varied shaft wrecks stay outside the central jump corridor, without shortcut colliders");
            var burns = wrecks.GetComponentsInChildren<Transform>().Where(t => t.name == "EnvironmentalSmolder").ToArray();
            Check(burns.Length == 13 && burns.All(t => Mathf.Abs(t.position.x) > 9
                && t.GetComponentsInChildren<ParticleSystem>().Length == 3
                && t.GetComponentsInChildren<ParticleSystem>().All(p => p.main.loop && p.main.prewarm && !p.useAutoRandomSeed)
                && t.GetComponentInChildren<Light>() != null),
                "Thirteen environmental burn sites have persistent flames, smoke, embers and local light; none on game plates");
            Check(env.GetComponentsInChildren<Collider>().All(col => col.bounds.min.z < c.GateZ || col.bounds.max.z > c.ExitPadZ), "No solid shortcut along the chasm");
            var bay = env.Find("Bay_0");
            string Signature(Transform t) => string.Join(";", t.GetComponentsInChildren<Light>().Select(l => l.type + ":" + l.intensity + ":" + l.range + ":" + l.color));
            Check(Enumerable.Range(1, c.Steps - 1).All(i => Signature(env.Find("Bay_" + i)) == Signature(bay)), "Route bay lights repeat identically");
            var deps = AssetDatabase.GetDependencies("Assets/_Project/Scenes/Minigames/MemoryRun.unity", true);
            Check(!deps.Any(p => p.Contains("Synty") || p.Contains("Polygon")), "Saved scene dependency graph has no Synty/Polygon assets");
            Check(arena.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterials.All(m => m != null && m.shader != null && !m.shader.name.Contains("InternalError"))), "All renderer materials and shaders resolve");
            Check(arena.GetComponentsInChildren<Transform>(true).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), "No missing scripts");
            var renderers = arena.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToArray();
            long triangles = renderers.Sum(r => { var mesh = r.GetComponent<MeshFilter>().sharedMesh; long n = 0; for (int i = 0; i < mesh.subMeshCount; i++) n += (long)mesh.GetIndexCount(i) / 3; return n; });
            report.AppendLine("Environment visible renderers=" + renderers.Length + "; triangles=" + triangles);
            report.AppendLine("Hall=" + c.HallWidth + " x " + c.HallDepth + " x " + c.CeilingHeight + "; shaft=" + c.PitDepth);
            report.AppendLine("Required diagonal gap=" + c.LongestRequiredJump.ToString("F3") + "m; unchanged.");
            report.AppendLine("Failures=" + errors);
            if (errors != 0) throw new InvalidOperationException(report.ToString());
            return report.ToString();
        }

        private static bool ConveyorMotionValid(MemoryFoundryConveyor line)
        {
            var so = new SerializedObject(line);
            var points = so.FindProperty("topPath");
            var cargo = so.FindProperty("cargo");
            var first = cargo.GetArrayElementAtIndex(0).objectReferenceValue as Transform;
            var second = cargo.GetArrayElementAtIndex(1).objectReferenceValue as Transform;
            float low = float.MaxValue, high = float.MinValue;
            for (int i = 0; i < points.arraySize; i++)
            {
                float y = points.GetArrayElementAtIndex(i).vector3Value.y;
                low = Mathf.Min(low, y); high = Mathf.Max(high, y);
            }
            line.Pose(0); var start = first.localPosition;
            line.Pose(1); float travelled = Vector3.Distance(first.localPosition, start);
            bool result = high - low > 1.5f && Vector3.Distance(first.localScale, second.localScale) > .01f
                && Mathf.Abs(travelled - so.FindProperty("speed").floatValue) < .01f;
            float cumulative = 0;
            bool testedSlope = false;
            for (int i = 1; i < points.arraySize; i++)
            {
                var from = points.GetArrayElementAtIndex(i - 1).vector3Value;
                var to = points.GetArrayElementAtIndex(i).vector3Value;
                float span = Vector3.Distance(from, to);
                if (!testedSlope && span > .001f && Mathf.Abs(to.y - from.y) / span > .1f)
                {
                    double time = (cumulative + span * .5f) / so.FindProperty("speed").floatValue;
                    line.Pose(time);
                    result &= Vector3.Angle(first.localRotation * Vector3.forward, to - from) < .05f;
                    var position = first.localPosition;
                    line.Pose(time + .01);
                    result &= Mathf.Abs(Vector3.Distance(position, first.localPosition) * 100 - so.FindProperty("speed").floatValue) < .01f;
                    testedSlope = true;
                }
                cumulative += span;
            }
            result &= testedSlope;
            var sample = typeof(MemoryFoundryConveyor).GetMethod("Sample", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            float length = new SerializedObject(line).FindProperty("length").floatValue;
            float arc = Mathf.PI * so.FindProperty("radius").floatValue;
            float perimeter = 2 * length + 2 * arc;
            foreach (float boundary in new[] { 0, length, length + arc, 2 * length + arc })
            {
                object[] before = { Mathf.Repeat(boundary - .001f, perimeter), Vector3.zero, Quaternion.identity };
                object[] after = { boundary + .001f, Vector3.zero, Quaternion.identity };
                sample.Invoke(line, before); sample.Invoke(line, after);
                result &= Vector3.Distance((Vector3)before[1], (Vector3)after[1]) < .004f
                    && Quaternion.Angle((Quaternion)before[2], (Quaternion)after[2]) < 1;
            }
            line.Pose(0);
            return result;
        }

        private static bool DeckHasSingleWalkingSurface(GameObject deck)
        {
            var collider = deck.GetComponent<BoxCollider>();
            if (collider == null || deck.GetComponent<Renderer>().enabled) return false;
            float surfaceY = collider.bounds.max.y;
            var tiles = deck.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh.name == "MF_Deck").ToArray();
            if (tiles.Length == 0) return false;
            foreach (var tile in tiles)
            {
                var mesh = tile.sharedMesh;
                var vertices = mesh.vertices;
                var materials = tile.GetComponent<Renderer>().sharedMaterials;
                int surfaceTriangles = 0;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    var indices = mesh.GetTriangles(submesh);
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        var a = tile.transform.TransformPoint(vertices[indices[i]]);
                        var b = tile.transform.TransformPoint(vertices[indices[i + 1]]);
                        var c = tile.transform.TransformPoint(vertices[indices[i + 2]]);
                        // Ignore bolts, tread ribs and bevels; test the broad walking faces.
                        if (Mathf.Abs(a.y - surfaceY) > .02f || Mathf.Abs(a.y - b.y) > .0001f
                            || Mathf.Abs(a.y - c.y) > .0001f || Vector3.Cross(b - a, c - a).y <= .5f) continue;
                        surfaceTriangles++;
                        if (materials[submesh] == null || materials[submesh].name != "MF_Steel") return false;
                    }
                }
                if (surfaceTriangles != 2) return false;
            }
            return true;
        }
    }
}
