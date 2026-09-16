using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Fifth Hub art pass: welcoming stairs and a furnished household workshop.</summary>
    public static class HubEntryPass
    {
        internal const string Folder = "Assets/_Project/Art/Hub/Entry";
        internal const string RootName = "_HubEntryCozy";

        [MenuItem("Igruha/Хаб/Уют — лестница и мастерская")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/_Project/Scenes/Hub.unity")
                throw new InvalidOperationException("Open Hub outside Play Mode before dressing the entry.");
            foreach (string path in new[] { "_HubLoungeFinish", "_Zones/Stairs", "_Zones/_Clutter/Workbench",
                "_Zones/_Clutter/ToolBoard", "_Zones/_Clutter/Washer", "_Zones/_Clutter/LaundryBin" }) HubRoomPass.Require(path);
            string physics = HubCozyPass.PhysicsSnapshot();
            var old = GameObject.Find(RootName); if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var root = new GameObject(RootName).transform;
            HubCozyMaterials.EnsureFolder(Folder + "/Meshes"); HubBarAssets.Begin();
            Staircase(root);
            HubEntryWorkshop.Apply(root);
            if (physics != HubCozyPass.PhysicsSnapshot()) throw new InvalidOperationException("Entry art changed gameplay colliders.");
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Hub entry finished. " + Audit());
        }

        internal static Material Surface(string name, string color, float smoothness = .1f)
            => HubCozyMaterials.Surface("HE_" + name, color, smoothness);

        private static void Staircase(Transform root)
        {
            var wood = HubEntrySurfaces.Wood("StairOak");
            var green = Surface("JoinerySage", "506958");
            var dark = Surface("RailGreen", "394F44");
            var cream = Surface("RiserCream", "D1BFA0");
            var brass = Surface("AgedBrass", "B99A60", .3f);
            HubEntrySurfaces.Dress("_Zones/Stairs/Flight", wood, cream, true);
            HubEntrySurfaces.Dress("_Zones/Stairs/Landing", wood, green, true);
            foreach (string name in new[] { "UnderFill", "LandingSupport" })
                HubCozyMaterials.Assign(HubRoomPass.Require("_Zones/Stairs/" + name), green);
            var geo = new HubRoomGeometry();
            foreach (Transform t in HubRoomPass.Require("_Zones/Stairs").transform)
            {
                if (!t.name.StartsWith("Rail")) continue;
                HubCozyMaterials.Assign(t.gameObject, t.name.Contains("Top") ? Surface("HandrailOak", "986F45") : dark);
                if (!t.name.StartsWith("RailPost")) continue;
                Bounds b = t.GetComponent<Renderer>().bounds;
                geo.Box(new Vector3(b.center.x, b.max.y + .005f, b.center.z), new Vector3(.10f, .018f, .10f), brass);
            }
            // Shallow joinery trim lies against the existing landing support, clear of the stair flight.
            var edge = Surface("PanelEdge", "7C8C70");
            foreach (float x in new[] { -11.35f, -10.55f, -9.75f })
            {
                foreach (float dx in new[] { -.34f, .34f }) geo.Box(new Vector3(x + dx, .68f, -9.281f), new Vector3(.035f, 1.05f, .012f), edge);
                foreach (float y in new[] { .155f, 1.205f }) geo.Box(new Vector3(x, y, -9.281f), new Vector3(.71f, .035f, .012f), edge);
            }
            foreach (string name in new[] { "Door", "DoorPost_A", "DoorPost_B", "DoorLintel" })
                HubCozyMaterials.Assign(HubRoomPass.Require("_Zones/Stairs/" + name), name == "Door" ? green : cream);
            geo.Box(new Vector3(-11.547f, 3.05f, -10.55f), new Vector3(.018f, .17f, .73f), dark);
            geo.Build(root, "EntryJoinery", true, Folder);
            HubBarDecor.Lettering(root, "DoorPlaque", "CLUB ROOM", new Vector3(-11.533f, 3.05f, -10.55f), .66f, .14f,
                HubCozyMaterials.Hex("E6D1A8"), 1.0f, 2);
        }

        public static string Audit()
        {
            var root = HubRoomPass.Require(RootName);
            if (root.GetComponentsInChildren<Collider>(true).Length != 0) throw new InvalidOperationException("Decorative collider in entry.");
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                foreach (var material in renderer.sharedMaterials)
                    if (material == null || material.shader == null || !material.shader.isSupported)
                        throw new InvalidOperationException("Unsupported entry material: " + renderer.name);
            int triangles = root.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh.triangles.Length / 3);
            return "Entry renderers: " + root.GetComponentsInChildren<MeshRenderer>().Length + "; triangles: " + triangles + ". " + HubCozyPass.Audit();
        }
    }
}
