using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Imports the original Blender kit and maps its palette to URP.</summary>
    internal static class ExamHallAssets
    {
        internal const string Art = "Assets/_Project/Art/ExamHall";
        internal const string Materials = "Assets/_Project/Materials/ExamHall";
        [Serializable] private sealed class Palette { public Entry[] materials; }
        [Serializable] private sealed class Entry
        {
            public string name; public float[] color; public float roughness;
            public float metallic; public string texture; public float emission;
        }

        [MenuItem("Igruha/Экзамен/Импортировать собственный зал")]
        internal static void Import()
        {
            Directory.CreateDirectory(Materials);
            AssetDatabase.Refresh();
            var palette = JsonUtility.FromJson<Palette>(File.ReadAllText(Art + "/palette.json"));
            var map = new Dictionary<string, Material>();
            foreach (var e in palette.materials)
            {
                string path = Materials + "/" + e.name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
                var color = new Color(e.color[0], e.color[1], e.color[2], 1);
                m.SetColor("_BaseColor", color); m.SetFloat("_Metallic", e.metallic);
                m.SetFloat("_Smoothness", 1 - e.roughness); m.enableInstancing = true;
                m.SetTexture("_BaseMap", string.IsNullOrEmpty(e.texture) ? null :
                    AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/Textures/EH_" + e.texture + ".png"));
                if (e.emission > 0)
                {
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                    m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * e.emission);
                }
                else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
                EditorUtility.SetDirty(m); map[e.name] = m;
            }
            foreach (string path in Directory.GetFiles(Art + "/Models", "*.fbx"))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.bakeAxisConversion = true; importer.addCollider = false;
                importer.importCameras = false; importer.importLights = false;
                importer.importAnimation = false; importer.animationType = ModelImporterAnimationType.None;
                importer.isReadable = false;
                foreach (var e in map) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), e.Key), e.Value);
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
        }
        internal static TMPro.TMP_FontAsset Font()
        {
            string path=Materials+"/EH_Cyrillic.asset";
            var font=AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(path);
            if(font==null)
            {
                AssetDatabase.CopyAsset("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset",path);
                font=AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(path);
            }
            return font;
        }
        internal static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>(Materials + "/EH_" + name + ".mat");
        internal static Transform Place(Transform parent, string model, Vector3 position, float yaw = 0)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Models/EH_" + model + ".fbx");
            if (asset == null) throw new InvalidOperationException("Import original Exam Hall model first: " + model);
            var go = new GameObject("EH_" + model);
            go.transform.SetParent(parent, false);
            PrefabUtility.InstantiatePrefab(asset, go.transform);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0); go.transform.localScale = Vector3.one;
            return go.transform;
        }
    }
}
