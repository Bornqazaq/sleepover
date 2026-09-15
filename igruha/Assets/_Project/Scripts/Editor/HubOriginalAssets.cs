using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    /// <summary>Imports our Blender-evaluated geometry. No meshes or textures are read from store packs.</summary>
    internal static class HubOriginalAssets
    {
        internal const string Folder = "Assets/_Project/Art/Hub/Original";
        [Serializable] private sealed class Pack { public Model[] models; }
        [Serializable] private sealed class Model { public string name; public Part[] parts; }
        [Serializable] private sealed class Part { public string material; public float[] vertices, normals, uv; public int[] triangles; }
        private static readonly Dictionary<string, string> Palette = new()
        {
            {"Oak","B3834E"},{"Walnut","785039"},{"Cream","DBCBAC"},{"Linen","BEBD9A"},
            {"Sage","4E7764"},{"Terracotta","B96142"},{"Ochre","C79648"},{"Ink","232F32"},
            {"Brass","AD894D"},{"Paper","E9D9B5"},{"Red","B64637"},{"Blue","427A91"},
            {"Leaf","466B3E"},{"LeafLight","73924D"},{"Screen","487D86"},{"Glow","FFCE89"},{"White","EFE7D3"}
        };

        internal static Material Mat(string name, string hex = null, float emission = 0)
        {
            HubCozyMaterials.EnsureFolder(Folder + "/Materials");
            string path = Folder + "/Materials/HO_" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null && hex == null && emission == 0) return mat;
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", HubCozyMaterials.Hex(hex ?? Palette[name]));
            mat.SetFloat("_Smoothness", name == "Brass" ? .45f : .19f);
            mat.SetFloat("_Metallic", name == "Brass" ? .65f : 0);
            if (emission > 0) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", HubCozyMaterials.Hex(hex ?? Palette[name]) * emission); }
            else { mat.DisableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", Color.black); }
            mat.globalIlluminationFlags = emission > 0 ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            if(name=="Screen"){mat.SetFloat("_SpecularHighlights",0);mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");}
            mat.enableInstancing = true; EditorUtility.SetDirty(mat); return mat;
        }

        internal static void Import()
        {
            HubCozyMaterials.EnsureFolder(Folder + "/Meshes"); HubCozyMaterials.EnsureFolder(Folder + "/Prefabs");
            foreach (var pair in Palette) Mat(pair.Key, pair.Value, emission: pair.Key == "Glow" ? 2.2f : pair.Key == "Screen" ? .18f : 0);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Textures/HoneyOak.png");
            var importer = (TextureImporter)AssetImporter.GetAtPath(Folder + "/Textures/HoneyOak.png");
            if (importer == null || texture == null) throw new InvalidOperationException("Original HoneyOak texture is missing.");
            importer.wrapMode = TextureWrapMode.Repeat; importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 8; importer.maxTextureSize = 1024; importer.mipmapEnabled = true; importer.SaveAndReimport();
            var wood = Mat("FloorOak", "C4C2BE"); wood.SetTexture("_BaseMap", texture);
            var wall = Mat("WallOak", "BCA17B"); wall.SetTexture("_BaseMap", texture);
            var oak = Mat("Oak"); oak.SetTexture("_BaseMap", texture); oak.SetColor("_BaseColor", new Color(.85f,.77f,.65f));
            using var stream = new GZipStream(File.OpenRead(Folder + "/Models/OriginalModels.json.gz"), CompressionMode.Decompress);
            using var reader = new StreamReader(stream);
            var pack = JsonUtility.FromJson<Pack>(reader.ReadToEnd());
            foreach (var model in pack.models)
            {
                var root = new GameObject(model.name);
                try
                {
                    foreach (var group in model.parts.GroupBy(p => p.material))
                    {
                        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>(); var indices = new List<int>();
                        foreach (var part in group)
                        {
                            int offset = vertices.Count;
                            for (int i = 0; i < part.vertices.Length; i += 3) { vertices.Add(new Vector3(part.vertices[i], part.vertices[i+1], part.vertices[i+2])); normals.Add(new Vector3(part.normals[i], part.normals[i+1], part.normals[i+2])); }
                            for (int i = 0; i < part.uv.Length; i += 2) uv.Add(new Vector2(part.uv[i], part.uv[i+1]));
                            foreach (int index in part.triangles) indices.Add(offset + index);
                        }
                        var mesh = new Mesh { name = model.name + "_" + group.Key, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                        mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
                        string path = Folder + "/Meshes/" + mesh.name + ".asset";
                        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                        if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
                        else { EditorUtility.CopySerialized(mesh, saved); UnityEngine.Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
                        var child = new GameObject(group.Key); child.transform.SetParent(root.transform, false);
                        child.AddComponent<MeshFilter>().sharedMesh = saved;
                        child.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/" + group.Key + ".mat");
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, Folder + "/Prefabs/" + model.name + ".prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
        }

        internal static GameObject Prop(Transform parent, string model, Vector3 position, float yaw = 0)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Prefabs/" + model + ".prefab");
            if (prefab == null) throw new InvalidOperationException("Missing original model: " + model);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent); go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            foreach (var t in go.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            return go;
        }
    }
}
