using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Igruha.EditorTools
{
    /// <summary>Fourth cozy pass: tactile surfaces, furniture details and local shadow/contact tuning.</summary>
    public static class HubLoungeFinishPass
    {
        private const string ScenePath = "Assets/_Project/Scenes/Hub.unity";
        private const string RootName = "_HubLoungeFinish";

        [MenuItem("Igruha/Хаб/Уют — материалы и детали гостиной")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != ScenePath)
                throw new InvalidOperationException("Open Hub outside Play Mode before finishing the lounge.");
            foreach (string path in new[] { "_HubCozy", "_HubBarCozy", "_HubRoomCozy", "_Pit/Couch", "_Pit/CoffeeTable" }) HubRoomPass.Require(path);
            string physics = HubCozyPass.PhysicsSnapshot();
            var previous = GameObject.Find(RootName);
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
            var root = new GameObject(RootName).transform;
            HubLoungeSurfaces.Prepare();
            Surfaces();
            HubLoungeDetails.Apply(root);
            Lighting();
            if (physics != HubCozyPass.PhysicsSnapshot()) throw new InvalidOperationException("Lounge finish changed gameplay colliders.");
            string audit = Audit(); AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Hub lounge finish applied. " + audit);
        }

        private static void Surfaces()
        {
            HubLoungeSurfaces.Dress("_Pit/Couch", HubLoungeSurfaces.Fabric("Linen", "D6C7AD"), .125f);
            HubLoungeSurfaces.Dress("_Pit/Armchair_L", HubLoungeSurfaces.Fabric("Sage", "7F9684"), .125f);
            HubLoungeSurfaces.Dress("_Pit/Armchair_R", HubLoungeSurfaces.Fabric("Terracotta", "BD805F"), .125f);
            HubLoungeSurfaces.Dress("_Pit/Cushion_L", HubLoungeSurfaces.Fabric("Ochre", "C4A16D"), .12f);
            HubLoungeSurfaces.Dress("_Pit/Cushion_R", HubLoungeSurfaces.Fabric("GreenPillow", "566F60"), .12f);
            string[] colors = { "D8CAB0", "6A7B63", "B79C6A" };
            for (int i = 0; i < colors.Length; i++)
                HubLoungeSurfaces.Dress("_HubCozy/WovenRug_" + i, HubLoungeSurfaces.Fabric("Rug_" + i, colors[i], .75f), .18f);
            HubLoungeSurfaces.Table();
        }

        private static void Lighting()
        {
            // Preserve all light positions and their count. Give the lounge priority through intensity and falloff.
            for (int i = 1; i <= 8; i++)
            {
                var light = HubRoomPass.Require("_Lighting/PendantLamp_" + i).GetComponentInChildren<Light>();
                light.intensity = i == 1 ? 28f : 22f;
                if (light.shadows == LightShadows.None) continue;
                var data = light.GetComponent<UniversalAdditionalLightData>();
                data.usePipelineSettings = false;
                light.shadowBias = .045f; light.shadowNormalBias = .12f; light.shadowStrength = .78f;
                EditorUtility.SetDirty(data); EditorUtility.SetDirty(light);
            }
            var bar = HubRoomPass.Require("_HubBarCozy/Bar_WallLight").GetComponent<Light>();
            bar.GetComponent<UniversalAdditionalLightData>().usePipelineSettings = false;
            bar.shadowBias = .045f; bar.shadowNormalBias = .12f;
            HubRoomPass.Require("_Lighting/FillLight").GetComponent<Light>().intensity = .32f;
            HubRoomPass.Require("_HubCozy/LoungeBounce").GetComponent<Light>().intensity = 1.55f;
        }

        public static string Audit()
        {
            var root = HubRoomPass.Require(RootName);
            if (root.GetComponentsInChildren<Collider>(true).Length != 0) throw new InvalidOperationException("Decorative colliders found.");
            int triangles = root.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh.triangles.Length / 3);
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { HubCozyMaterials.Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!System.IO.Path.GetFileName(path).StartsWith("HL_")) continue;
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader == null || !material.shader.isSupported)
                    throw new InvalidOperationException("Invalid lounge material: " + path);
            }
            return "New decoration renderers: " + root.GetComponentsInChildren<MeshRenderer>().Length + "; triangles: " + triangles + ". " + HubCozyPass.Audit();
        }
    }
}
