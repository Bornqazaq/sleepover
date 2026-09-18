using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    internal static class MemoryFoundryAssets
    {
        internal const string Art = "Assets/_Project/Art/MemoryFoundry";
        internal const string Materials = "Assets/_Project/Materials/MemoryFoundry";
        [Serializable] private sealed class Palette { public Entry[] materials; }
        [Serializable] private sealed class Entry
        {
            public string name; public float[] color; public float roughness;
            public float metallic; public string texture; public float emission;
        }

        [MenuItem("Igruha/Рейс на память/Импортировать собственный цех")]
        internal static void Import()
        {
            Directory.CreateDirectory(Materials);
            AssetDatabase.Refresh();
            var palette = JsonUtility.FromJson<Palette>(File.ReadAllText(Art + "/palette.json"));
            var map = new Dictionary<string, Material>();
            foreach (var e in palette.materials)
            {
                string path = Materials + "/" + e.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    AssetDatabase.CreateAsset(material, path);
                }
                var color = new Color(e.color[0], e.color[1], e.color[2], 1);
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Metallic", e.metallic);
                material.SetFloat("_Smoothness", 1 - e.roughness);
                material.enableInstancing = true;
                material.SetTexture("_BaseMap", string.IsNullOrEmpty(e.texture) ? null :
                    AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/Textures/MF_" + e.texture + ".png"));
                material.globalIlluminationFlags = e.emission > 0
                    ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                if (e.emission > 0) material.EnableKeyword("_EMISSION");
                else material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color.linear * e.emission);
                EditorUtility.SetDirty(material); map[e.name] = material;
            }
            foreach (string path in Directory.GetFiles(Art + "/Models", "*.fbx"))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.bakeAxisConversion = true;
                importer.addCollider = false;
                importer.importCameras = false; importer.importLights = false;
                importer.importAnimation = false; importer.animationType = ModelImporterAnimationType.None;
                importer.isReadable = false;
                foreach (var e in map)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), e.Key), e.Value);
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
        }

        internal static Material Material(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>(Materials + "/MF_" + name + ".mat");

        internal static TMPro.TMP_FontAsset Font()
        {
            string path = Materials + "/MF_SignFont.asset";
            var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(path);
            if (font == null)
            {
                AssetDatabase.CopyAsset("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset", path);
                font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(path);
            }
            return font;
        }

        internal static Transform Place(Transform parent, string model, Vector3 position, float yaw = 0)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Models/MF_" + model + ".fbx");
            if (asset == null) throw new InvalidOperationException("Missing original foundry model: " + model);
            var go = new GameObject("MF_" + model);
            go.transform.SetParent(parent, false);
            PrefabUtility.InstantiatePrefab(asset, go.transform);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            return go.transform;
        }
    }
}
