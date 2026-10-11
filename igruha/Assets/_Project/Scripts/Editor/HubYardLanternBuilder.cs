using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Igruha.Core.Hub;

namespace Igruha.EditorTools
{
    /// <summary>Unevenly spaced warm garden lanterns, restricted to the exterior lighting layer.</summary>
    public static class HubYardLanternBuilder
    {
        private const float FenceApproach = 13f, LampHeight = 1.53f;
        private const float LightIntensity = 4f, LightRange = 5.6f;
        private static readonly float[][] SideOffsets =
        {
            new[] { -5.05f, 2.25f, 9.1f },
            new[] { -7.6f, 3.35f },
            new[] { -8.2f, -1.3f, 7.5f },
            new[] { -5.1f, 5.6f }
        };

        [MenuItem("Igruha/Хаб/Добавить тёплые фонари во двор")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/_Project/Scenes/Hub.unity")
                throw new InvalidOperationException("Open Hub outside Play Mode.");
            var yard = HubCompactPass.Require(HubCompactPass.RootName).transform.Find("BasementYard");
            if (yard == null) throw new InvalidOperationException("Build the basement yard first.");
            Build(yard, HubBasementYardBuilder.Grade);
            HubBasementYardBuilder.ApplyDuskLighting();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        internal static void Build(Transform yard, float grade)
        {
            var old = yard.Find("GardenLanterns");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var group = HubCompactPass.Group(yard, "GardenLanterns");
            var g = new HubOriginalGeometry();
            var metal = HubOriginalAssets.Mat("YardLanternBronze", "53483C");
            metal.SetFloat("_Metallic", .45f); metal.SetFloat("_Smoothness", .32f);
            var trim = HubOriginalAssets.Mat("YardLanternBrass", "A78351");
            trim.SetFloat("_Metallic", .5f); trim.SetFloat("_Smoothness", .38f);
            var glow = HubOriginalAssets.Mat("YardLanternGlow", "FFC382", 1.35f);
            for (int side = 0; side < SideOffsets.Length; side++)
            {
                var rotation = Quaternion.Euler(0, side * 90, 0);
                for (int i = 0; i < SideOffsets[side].Length; i++)
                {
                    float depth = FenceApproach + (i % 2 == 0 ? .18f : -.22f);
                    var ground = rotation * new Vector3(SideOffsets[side][i], grade, depth);
                    Lantern(g, ground, metal, trim, glow);
                    var lightObject = HubCompactPass.Group(group, "LanternLight_" + side + "_" + i).gameObject;
                    lightObject.transform.localPosition = ground + Vector3.up * LampHeight;
                    var light = lightObject.AddComponent<Light>();
                    light.type = LightType.Point; light.color = HubCozyMaterials.Hex("FFCF96");
                    light.intensity = LightIntensity; light.range = LightRange;
                    light.shadows = LightShadows.None;
                    var data = lightObject.AddComponent<UniversalAdditionalLightData>();
                    data.renderingLayers = HubYardAmbient.RenderingLayer;
                }
            }
            g.Build(group, "GardenLanternGeometry");
            foreach (var filter in group.GetComponentsInChildren<MeshFilter>()) AssetDatabase.SaveAssetIfDirty(filter.sharedMesh);
            foreach (var mat in new[] { metal, trim, glow }) { EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); }
        }

        private static void Lantern(HubOriginalGeometry g, Vector3 p, Material metal, Material trim, Material glow)
        {
            g.Box(p + Vector3.up * .06f, new Vector3(.29f, .12f, .29f), metal);
            g.Box(p + Vector3.up * .18f, new Vector3(.19f, .15f, .19f), trim);
            g.Box(p + Vector3.up * .73f, new Vector3(.09f, 1f, .09f), metal);
            g.Box(p + Vector3.up * 1.27f, new Vector3(.25f, .075f, .25f), trim);
            g.Box(p + Vector3.up * 1.33f, new Vector3(.36f, .07f, .36f), metal);
            g.Box(p + Vector3.up * LampHeight, new Vector3(.23f, .31f, .23f), glow);
            foreach (float x in new[] { -.15f, .15f }) foreach (float z in new[] { -.15f, .15f })
                g.Box(p + new Vector3(x, LampHeight, z), new Vector3(.035f, .40f, .035f), metal);
            g.Box(p + Vector3.up * 1.74f, new Vector3(.41f, .055f, .41f), metal);
            var peak = p + Vector3.up * 1.90f;
            for (int i = 0; i < 4; i++)
            {
                var a = p + Quaternion.Euler(0, i * 90, 0) * new Vector3(-.23f, 1.76f, -.23f);
                var b = p + Quaternion.Euler(0, (i + 1) * 90, 0) * new Vector3(-.23f, 1.76f, -.23f);
                g.Quad(a, b, peak, peak, metal, Vector2.one);
            }
            g.Sphere(peak + Vector3.up * .035f, .035f, trim);
        }
    }
}
