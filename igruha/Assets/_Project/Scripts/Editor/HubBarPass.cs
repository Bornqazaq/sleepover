using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    /// <summary>Second hub art pass: stocked snack bar. Leaves the first pass and all gameplay objects in place.</summary>
    public static class HubBarPass
    {
        private const string ScenePath = "Assets/_Project/Scenes/Hub.unity";
        private const string RootName = "_HubBarCozy";
        private const string BarPath = "_Zones/MiniBar/";
        private const float WallX = -11.80f;

        [MenuItem("Igruha/Хаб/Уют — бар и снеки")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != ScenePath)
                throw new InvalidOperationException("Open Hub outside Play Mode before dressing the bar.");
            foreach (string name in new[] { "Counter_1", "Counter_2", "Counter_3", "Fridge", "IceCreamMachine" }) Require(name);
            string physics = HubCozyPass.PhysicsSnapshot();
            var previous = GameObject.Find(RootName);
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
            var root = new GameObject(RootName).transform;
            HubBarAssets.Begin();
            CounterFront(root);
            Shelves(root);
            Fridge(root);
            CounterSnacks(root);
            Signage(root);
            HubBarDecor.Garland(root);
            Lighting(root);
            if (physics != HubCozyPass.PhysicsSnapshot()) throw new InvalidOperationException("Bar dressing changed gameplay colliders.");
            string audit = Audit();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Hub snack bar applied. " + audit);
        }

        private static GameObject Require(string name) => GameObject.Find(BarPath + name)
            ?? throw new InvalidOperationException("Missing bar object: " + name);

        private static Material Surface(string name, string color, float smoothness = .1f)
            => HubCozyMaterials.Surface("HB_" + name, color, smoothness);

        private static void CounterFront(Transform parent)
        {
            var wood = Surface("Oak", "A16B43");
            var woodAlt = Surface("OakAlt", "AD7950");
            var trim = Surface("DarkWood", "65432E", .18f);
            var combined = HubBarAssets.BoundsOf(Require("Counter_1"));
            combined.Encapsulate(HubBarAssets.BoundsOf(Require("Counter_3")));
            const int slats = 24;
            float pitch = combined.size.z / slats, x = combined.max.x + .01f;
            for (int i = 0; i < slats; i++)
                HubBarDecor.Box(parent, "Counter_OakSlat_" + i,
                    new Vector3(x, .48f, combined.min.z + pitch * (i + .5f)),
                    new Vector3(.023f, .65f, pitch - .014f), i % 3 == 0 ? woodAlt : wood);
            foreach (float y in new[] { .15f, .84f })
                HubBarDecor.Box(parent, "Counter_Trim", new Vector3(x + .01f, y, combined.center.z),
                    new Vector3(.035f, .05f, combined.size.z), trim);
        }

        private static void Shelves(Transform parent)
        {
            var wood = Surface("ShelfWood", "865737", .16f);
            var brass = Surface("Brackets", "98744A", .3f);
            var backing = Surface("CabinetGreen", "344F43");
            HubBarDecor.Box(parent, "Shelf_Back", new Vector3(WallX + .035f, 2.53f, 2.17f), new Vector3(.06f, 1.92f, 3.86f), backing);
            foreach (float z in new[] { .20f, 4.14f })
                HubBarDecor.Box(parent, "Shelf_Side", new Vector3(WallX + .16f, 2.53f, z), new Vector3(.32f, 1.96f, .065f), wood);
            foreach (float y in new[] { 1.98f, 2.51f, 3.46f })
            {
                HubBarDecor.Box(parent, "Shelf_Plank", new Vector3(WallX + .18f, y, 2.17f), new Vector3(.36f, .075f, 4.02f), wood);
                if (y > 3) continue;
                foreach (float z in new[] { .38f, 3.96f })
                    HubBarDecor.Box(parent, "Shelf_Bracket", new Vector3(WallX + .21f, y - .10f, z), new Vector3(.26f, .15f, .03f), brass);
            }
            const float lower = 2.02f, upper = 2.55f, shelfX = WallX + .19f;
            for (int i = 0; i < 8; i++)
                Product(parent, i % 2 == 0 ? 4 : 26, new Vector3(shelfX, lower, .48f + i * .145f), .9f);
            for (int i = 0; i < 5; i++)
                Product(parent, new[] { 7, 12, 37, 7, 12 }[i], new Vector3(shelfX, lower, 1.88f + i * .40f), .9f);
            for (int i = 0; i < 3; i++) Product(parent, 21, new Vector3(shelfX, lower, 3.74f + i * .105f), .8f);
            var radio = HubBarAssets.Prop("Props/SM_Prop_Computer_Radio_01.prefab", parent, "Shelf_Radio");
            HubBarAssets.Place(radio, new Vector3(shelfX, upper, .79f), .8f);
            foreach (var z in new[] { 1.52f, 1.86f, 2.20f }) Product(parent, 7, new Vector3(shelfX, upper, z), .85f);
            foreach (var z in new[] { 2.65f, 2.90f }) Product(parent, 37, new Vector3(shelfX, upper, z), .85f);
            for (int i = 0; i < 4; i++)
            {
                var bottle = HubBarAssets.Prop("Props/SM_Prop_Bar_Bottle_0" + (i % 2 == 0 ? "2" : "4") + ".prefab", parent, "Shelf_Bottle_" + i);
                HubBarAssets.Place(bottle, new Vector3(shelfX, upper, 3.27f + i * .18f), .85f);
            }
        }

        private static void Product(Transform parent, int index, Vector3 basePoint, float scale)
        {
            var go = HubBarAssets.Prop("Products/SM_Prop_Product_" + index.ToString("D2") + ".prefab", parent, "Snack_" + index);
            HubBarAssets.Place(go, basePoint, scale);
        }

        private static void Fridge(Transform parent)
        {
            var existing = Require("Fridge").transform;
            var insert = HubBarAssets.Prop("Props/SM_Prop_Market_Drinks_Fridge_01_Insert_01.prefab", parent, "Fridge_Drinks");
            // This insert is authored for the exact refrigerator already present in the scene.
            insert.transform.SetPositionAndRotation(existing.position, existing.rotation);
            insert.transform.localScale = existing.lossyScale;
        }

        private static void CounterSnacks(Transform parent)
        {
            float top = HubBarAssets.BoundsOf(Require("Counter_2")).max.y;
            var tray = Surface("Tray", "705135", .12f);
            HubBarDecor.Box(parent, "Serving_Board", new Vector3(-9.44f, top + .016f, 2.57f), new Vector3(.48f, .032f, .91f), tray);
            var plate = HubBarAssets.Prop("Food/SM_Prop_Food_Plate_01.prefab", parent, "Cookie_Plate");
            HubBarAssets.Place(plate, new Vector3(-9.44f, top + .034f, 2.64f), .9f);
            float plateTop = HubBarAssets.BoundsOf(plate).max.y;
            for (int i = 0; i < 5; i++)
            {
                var cookie = HubBarAssets.Prop("Food/SM_Prop_Food_Cookie_01.prefab", parent, "Cookie_" + i);
                float angle = i * Mathf.PI * .4f;
                HubBarAssets.Place(cookie, new Vector3(-9.44f + Mathf.Cos(angle) * .075f, plateTop + i * .012f, 2.64f + Mathf.Sin(angle) * .075f), .8f, i * 65);
            }
            for (int i = 0; i < 2; i++)
            {
                var cup = HubBarAssets.Prop("Food/SM_Prop_Food_Cup_03.prefab", parent, "Coffee_Cup_" + i);
                HubBarAssets.Place(cup, new Vector3(-9.35f - .25f * i, top + .003f, 1.47f + i * .19f), 1.1f, 90 + i * 60);
            }
            foreach (var renderer in Require("IceCreamMachine").GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            var machine = new GameObject("Popcorn_Machine"); machine.transform.SetParent(parent, false);
            HubBarDecor.Popcorn(machine.transform);
            machine.transform.position = Vector3.up * (top - .99f);
            HubBarDecor.PopcornCup(parent, new Vector3(-9.38f, top + .002f, 3.35f));
        }

        private static void Signage(Transform parent)
        {
            var darkWood = Surface("SignWood", "6D442F", .12f);
            var cream = HubCozyMaterials.Hex("EAD7AA");
            HubBarDecor.Box(parent, "SnackClub_Sign", new Vector3(WallX + .07f, 3.23f, 2.17f), new Vector3(.10f, .36f, 3.18f), darkWood);
            HubBarDecor.Lettering(parent, "SnackClub_Title", "SNACK CLUB", new Vector3(WallX + .128f, 3.24f, 2.17f), 2.9f, .34f, cream, 3.2f, 12);
            var green = Surface("PosterGreen", "49654D");
            HubBarDecor.Box(parent, "Break_PosterFrame", new Vector3(WallX + .045f, 2.73f, 5.32f), new Vector3(.075f, 1.26f, .95f), darkWood);
            HubBarDecor.Box(parent, "Break_Poster", new Vector3(WallX + .087f, 2.73f, 5.32f), new Vector3(.012f, 1.13f, .82f), green);
            HubBarDecor.Lettering(parent, "Break_Title", "TAKE A\nBREAK", new Vector3(WallX + .10f, 2.92f, 5.32f), .72f, .58f, cream, 2.1f, 1);
            HubBarDecor.Box(parent, "Break_Rule", new Vector3(WallX + .105f, 2.57f, 5.32f), new Vector3(.005f, .017f, .48f), Surface("SignInk", "EAD7AA"));
            HubBarDecor.Lettering(parent, "Break_Footer", "ONE MORE ROUND?", new Vector3(WallX + .10f, 2.39f, 5.32f), .73f, .14f, cream, .63f, 2);
        }

        private static void Lighting(Transform parent)
        {
            var go = new GameObject("Bar_WallLight"); go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(-10.35f, 3.27f, 2.14f);
            go.transform.rotation = Quaternion.LookRotation(new Vector3(-11.73f, 2.35f, 2.14f) - go.transform.position);
            var light = go.AddComponent<Light>(); light.type = LightType.Spot;
            light.color = HubCozyMaterials.Hex("FFE1B2"); light.intensity = 7; light.range = 4;
            light.spotAngle = 85; light.innerSpotAngle = 65; light.shadows = LightShadows.Soft;
            light.shadowStrength = .65f; light.shadowBias = .02f; light.shadowNormalBias = .10f;
            var fridgeGlow = new GameObject("Fridge_CoolLight"); fridgeGlow.transform.SetParent(parent, false);
            fridgeGlow.transform.position = new Vector3(-9.26f, 1.34f, 4.99f);
            var cool = fridgeGlow.AddComponent<Light>(); cool.type = LightType.Point;
            cool.color = HubCozyMaterials.Hex("BAE8F5"); cool.intensity = .65f; cool.range = 1.25f; cool.shadows = LightShadows.None;
        }

        public static string Audit()
        {
            var root = GameObject.Find(RootName) ?? throw new InvalidOperationException("Bar decoration missing");
            int colliders = root.GetComponentsInChildren<Collider>(true).Length;
            if (colliders != 0) throw new InvalidOperationException("Decorative bar colliders: " + colliders);
            int triangles = root.GetComponentsInChildren<MeshFilter>().Sum(f => (int)Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(s => f.sharedMesh.GetIndexCount(s)) / 3);
            int missing = root.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            if (missing != 0) throw new InvalidOperationException("Missing bar scripts: " + missing);
            return "Bar renderers=" + root.GetComponentsInChildren<Renderer>().Length + "; triangles=" + triangles + "; colliders=0; missing scripts=0.";
        }
    }
}
