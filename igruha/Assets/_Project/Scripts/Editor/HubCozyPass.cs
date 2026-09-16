using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    /// <summary>First cozy hub pass. Rebuilds only its own decoration; preserves gameplay geometry.</summary>
    public static class HubCozyPass
    {
        private const string ScenePath = "Assets/_Project/Scenes/Hub.unity";
        private const string RootName = "_HubCozy";
        private static GameObject Require(string path)
        {
            var go = GameObject.Find(path);
            if (go == null) throw new InvalidOperationException("Hub object missing: " + path);
            return go;
        }

        [MenuItem("Igruha/Хаб/Уют — пол, свет и гостиная")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != ScenePath)
                throw new InvalidOperationException("Open Hub outside Play Mode before applying the art pass.");
            string physics = PhysicsSnapshot();
            foreach (string path in new[] { "_Room", "_Pit/Couch", "_Pit/Rug", "_Lighting", "_Spawns", "HubManager" }) Require(path);
            HubCozyMaterials.PrepareTextures();
            var previous = GameObject.Find(RootName);
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
            var root = new GameObject(RootName).transform;
            Surfaces();
            Lounge(root);
            Lighting(root);
            if (physics != PhysicsSnapshot()) throw new InvalidOperationException("The cozy pass changed gameplay colliders.");
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Hub cozy pass: floor, lounge, 8 pendants. Collider geometry unchanged. " + Audit());
        }

        private static void Surfaces()
        {
            foreach (Transform floor in Require("_Room").transform)
            {
                if (!floor.name.StartsWith("Floor_")) continue;
                Vector3 size = floor.GetComponent<Renderer>().bounds.size;
                var material = HubCozyMaterials.Surface("HC_" + floor.name, "F3E9DA", .02f,
                    "PolygonShops_Building_Carpet_03", new Vector2(size.x / 3f, size.z / 3f));
                HubCozyMaterials.Assign(floor.gameObject, material);
            }
            HubCozyMaterials.Assign(Require("_Pit/PitFloor"), HubCozyMaterials.Surface("HC_PitFloor", "A79479", .03f));
            HubCozyMaterials.Assign(Require("_Room/Ceiling"), HubCozyMaterials.Surface("HC_Ceiling", "BAAF98", .02f));
            Material wood = HubCozyMaterials.Surface("HC_WalnutTrim", "73503B", .12f);
            foreach (Transform item in Require("_Zones/_Structure").transform)
                if (item.name.StartsWith("Pillar_")) HubCozyMaterials.Assign(item.gameObject, wood);
        }

        private static void Lounge(Transform parent)
        {
            Material ivory = HubCozyMaterials.Surface("HC_Linen", "CFC2A5", .04f);
            HubCozyMaterials.Assign(Require("_Pit/Couch"), ivory);
            HubCozyMaterials.Assign(Require("_Pit/Armchair_L"), HubCozyMaterials.Surface("HC_Sage", "708778", .04f));
            HubCozyMaterials.Assign(Require("_Pit/Armchair_R"), HubCozyMaterials.Surface("HC_Terracotta", "B97859", .04f));
            HubCozyMaterials.Assign(Require("_Pit/Cushion_L"), HubCozyMaterials.Surface("HC_Ochre", "CCA762", .03f));
            HubCozyMaterials.Assign(Require("_Pit/Cushion_R"), HubCozyMaterials.Surface("HC_CushionGreen", "547263", .03f));
            // Keep the original rug and its collider exactly where they are; the larger textile is visual only.
            Require("_Pit/Rug").GetComponent<Renderer>().enabled = false;
            HubCozyGeometry.Rug(parent, new Vector3(0, -.628f, -.20f));
            HubCozyGeometry.Throw(parent, Require("_Pit/Couch").GetComponent<Renderer>().bounds);
            LightAt(parent, "LoungeBounce", new Vector3(0, 1.25f, -.8f), "FFE2B5", 2.2f, 6f);
            // A restrained cool bounce at the existing screen. No overlay over the interactive TV canvas.
            LightAt(parent, "ScreenBounce", new Vector3(0, .75f, 2.38f), "8BC5DE", 1.8f, 4f);
        }

        private static void Lighting(Transform parent)
        {
            Mesh shadeMesh = HubCozyGeometry.Shade();
            Mesh globeMesh = HubCozyGeometry.Globe();
            Material shadeMaterial = HubCozyMaterials.Surface("HC_PendantCream", "D2AC71", .2f, glow: .08f);
            Material cordMaterial = HubCozyMaterials.Surface("HC_Cord", "342F27", .1f);
            Material bulbMaterial = HubCozyMaterials.Surface("HC_Bulb", "FFD07B", .1f, glow: 1.3f);
            Material diffuserMaterial = HubCozyMaterials.Surface("HC_Diffuser", "FFD28C", .1f, glow: .7f);
            for (int i = 1; i <= 8; i++)
            {
                var fixture = Require("_Lighting/PendantLamp_" + i);
                var light = fixture.GetComponentInChildren<Light>();
                light.type = LightType.Spot;
                light.transform.rotation = Quaternion.Euler(90, 0, 0);
                light.color = HubCozyMaterials.Hex("FFE2B8");
                light.intensity = i == 1 ? 34f : 26f;
                light.range = 10f;
                light.spotAngle = 135f;
                light.innerSpotAngle = 100f;
                light.shadows = i == 1 || i == 2 || i == 5 || i == 6 ? LightShadows.Soft : LightShadows.None;
                light.shadowStrength = .65f;
                light.shadowBias = .025f;
                light.shadowNormalBias = .2f;
                foreach (var renderer in fixture.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
                Vector3 position = fixture.transform.position;
                var shade = HubCozyGeometry.MeshObject("Pendant_" + i, parent, shadeMesh, shadeMaterial);
                shade.transform.position = new Vector3(position.x, 2.75f, position.z);
                Primitive("Cord_" + i, PrimitiveType.Cylinder, parent,
                    new Vector3(position.x, 3.675f, position.z), new Vector3(.014f, .325f, .014f), cordMaterial);
                Globe("Bulb_" + i, parent, globeMesh, bulbMaterial,
                    new Vector3(position.x, 2.88f, position.z), new Vector3(.18f, .16f, .18f));
                Primitive("Diffuser_" + i, PrimitiveType.Cylinder, parent,
                    new Vector3(position.x, 2.87f, position.z), new Vector3(1.10f, .006f, 1.10f), diffuserMaterial);
            }
            var fill = Require("_Lighting/FillLight").GetComponent<Light>();
            fill.intensity = .45f;
            fill.color = HubCozyMaterials.Hex("D7DFE0");
            fill.shadows = LightShadows.None;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = HubCozyMaterials.Hex("8C8476");
            RenderSettings.ambientEquatorColor = HubCozyMaterials.Hex("79756B");
            RenderSettings.ambientGroundColor = HubCozyMaterials.Hex("57544D");
            foreach (Transform item in Require("_Zones/_Structure").transform)
            {
                if (!item.name.StartsWith("Bulb_")) continue;
                item.GetComponent<Renderer>().enabled = false;
                Primitive("GarlandSocket_" + item.name, PrimitiveType.Cylinder, parent,
                    item.position + Vector3.down * .06f, new Vector3(.045f, .06f, .045f), cordMaterial);
                Globe("GarlandGlow_" + item.name, parent, globeMesh, bulbMaterial,
                    item.position + Vector3.down * .16f, new Vector3(.12f, .14f, .12f));
            }
        }

        private static void Globe(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var go = HubCozyGeometry.MeshObject(name, parent, mesh, material);
            go.transform.position = position; go.transform.localScale = scale;
            go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        private static Light LightAt(Transform parent, string name, Vector3 position, string hex, float power, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point; light.color = HubCozyMaterials.Hex(hex);
            light.intensity = power; light.range = range; light.shadows = LightShadows.None;
            return light;
        }

        private static void Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name; go.transform.SetParent(parent, false);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = position; go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        public static string PhysicsSnapshot()
        {
            var rows = new List<string>();
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(collider.transform, null);
                rows.Add(path + "|" + collider.gameObject.layer + "|" + collider.enabled + "|" + collider.isTrigger + "|" +
                    collider.transform.localToWorldMatrix.ToString("F5") + "|" + EditorJsonUtility.ToJson(collider));
            }
            rows.Sort(StringComparer.Ordinal);
            return string.Join("\n", rows);
        }

        public static string Audit()
        {
            var root = Require(RootName);
            if (root.GetComponentsInChildren<Collider>(true).Length != 0) throw new InvalidOperationException("Decorative colliders found.");
            int missing = EditorSceneManager.GetActiveScene().GetRootGameObjects()
                .Sum(go => go.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)));
            if (missing != 0) throw new InvalidOperationException("Missing scripts: " + missing);
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            return "Decoration colliders: 0; missing scripts: 0; lights: " + lights.Length +
                "; shadow lights: " + lights.Count(l => l.enabled && l.shadows != LightShadows.None) + ".";
        }
    }
}
