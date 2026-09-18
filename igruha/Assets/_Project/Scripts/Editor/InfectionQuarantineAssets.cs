using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    internal static class InfectionQuarantineAssets
    {
        internal const string Art = "Assets/_Project/Art/Minigames/Infection/Quarantine";
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        internal static void Prepare()
        {
            Directory.CreateDirectory(Art + "/Materials");
            Directory.CreateDirectory(Art + "/Textures");
            Directory.CreateDirectory(Art + "/Meshes");
            AssetDatabase.Refresh();
            Materials.Clear();
            foreach (string path in Directory.GetFiles(Art, "*.fbx"))
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) throw new System.InvalidOperationException("Missing model: " + path);
                foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                foreach (var source in renderer.sharedMaterials)
                {
                    if (source == null || Materials.ContainsKey(source.name)) continue;
                    string name = source.name;
                    var color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color;
                    var material = Material(name, color);
                    Materials[name] = material;
                }
            }
        }
        internal static Material Material(string name, Color color)
        {
            string path = Art + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", .13f);
            m.SetFloat("_Metallic", name.Contains("Iron") || name.Contains("Steel") ? .35f : .02f);
            m.SetFloat("_Cull", 0); m.enableInstancing = true;
            if (name.StartsWith("INF_")) m.SetTexture("_BaseMap", SurfaceTexture());
            EditorUtility.SetDirty(m); return m;
        }
        private static Texture2D SurfaceTexture()
        {
            string path = Art + "/Textures/DrySurface.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var random = new System.Random(218);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float n = Mathf.PerlinNoise(x / 33f, y / 33f);
                float grain = (float)random.NextDouble();
                float value = Mathf.Lerp(.97f, 1f, n) * Mathf.Lerp(.93f, 1f, grain);
                texture.SetPixel(x, y, new Color(value, value * .99f, value * .96f, 1));
            }
            texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path); return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        internal static GameObject Place(string model, Transform parent, Vector3 position, float yaw = 0, float scale = 1)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/" + model + ".fbx");
            if (source == null) throw new System.InvalidOperationException("Missing original model: " + model);
            // A single-mesh FBX carries its axis/unit conversion on its root.
            // Keep that imported transform below our metre-space placement root.
            var go = new GameObject("Art_" + model); go.transform.SetParent(parent, false);
            Object.Instantiate(source, go.transform, false);
            go.transform.localPosition = position; go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * scale;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var sourceMaterials = r.sharedMaterials;
                for (int i = 0; i < sourceMaterials.Length; i++) sourceMaterials[i] = Materials[sourceMaterials[i].name];
                r.sharedMaterials = sourceMaterials;
            }
            return go;
        }
        internal static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = size;
            Object.DestroyImmediate(go.GetComponent<Collider>()); go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }
        internal static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
        }
        // One mesh per material in each scenery zone; moving props and see-through tubes stay separate.
        internal static void Combine(Transform root)
        {
            var buckets = new Dictionary<Material, List<CombineInstance>>();
            var renderers = root.GetComponentsInChildren<MeshRenderer>();
            var originals = new List<GameObject>();
            foreach(Transform child in root) originals.Add(child.gameObject);
            foreach (var r in renderers)
            {
                var filter = r.GetComponent<MeshFilter>(); if (filter == null || !r.enabled) continue;
                for (int i = 0; i < r.sharedMaterials.Length; i++)
                {
                    var mat = r.sharedMaterials[i];
                    if (!buckets.TryGetValue(mat, out var list)) { list = new List<CombineInstance>(); buckets.Add(mat, list); }
                    list.Add(new CombineInstance { mesh = filter.sharedMesh, subMeshIndex = i, transform = root.worldToLocalMatrix * filter.transform.localToWorldMatrix });
                }
            }
            foreach (var pair in buckets)
            {
                string path = Art + "/Meshes/" + root.name + "_" + pair.Key.name + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); } else mesh.Clear();
                mesh.name = root.name + "_" + pair.Key.name; mesh.indexFormat = IndexFormat.UInt32;
                mesh.CombineMeshes(pair.Value.ToArray(), true, true); EditorUtility.SetDirty(mesh);
                var go = new GameObject("Combined_" + pair.Key.name); go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = pair.Key;
            }
            foreach (var original in originals) Object.DestroyImmediate(original);
        }
    }
}
