using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    /// <summary>Original Blender modules with explicit URP materials and metre-scale meshes.</summary>
    internal static class HoleInWallStudioAssets
    {
        internal const string Art = "Assets/_Project/Art/HoleInWallStudio";
        internal const string Materials = "Assets/_Project/Materials/HoleInWallStudio";
        [Serializable] private sealed class Palette { public Entry[] materials; }
        [Serializable] private sealed class Entry
        {
            public string name;
            public float[] color;
            public float roughness, metallic, emission;
        }
        private static readonly Dictionary<string, Material[]> ModelMaterials = new Dictionary<string, Material[]>();
        private static readonly Dictionary<string, Mesh> FittedPanels = new Dictionary<string, Mesh>();
        private const float SourcePanelBevel = .065f;
        private const float ArchitecturalBevel = .025f;
        private const string PanelLibrary = Art + "/Meshes/HS_FittedPanels.asset";

        internal static void BeginPanels() => FittedPanels.Clear();

        // Preserve a metre-sized bevel instead of stretching a unit cube's corners
        // several metres along a wall. The original Blender topology/normals survive.
        internal static Mesh FittedPanel(Vector3 size)
        {
            string key = string.Format(CultureInfo.InvariantCulture, "Panel_{0:F4}_{1:F4}_{2:F4}", size.x, size.y, size.z);
            if (FittedPanels.TryGetValue(key, out var cached)) return cached;
            var source = Mesh("Panel");
            var fitted = AssetDatabase.LoadAllAssetsAtPath(PanelLibrary).OfType<Mesh>().FirstOrDefault(m => m.name == key);
            if (fitted == null)
            {
                fitted = new Mesh { name = key };
                if (AssetDatabase.LoadMainAssetAtPath(PanelLibrary) == null) AssetDatabase.CreateAsset(fitted, PanelLibrary);
                else AssetDatabase.AddObjectToAsset(fitted, PanelLibrary);
            }
            var vertices = source.vertices;
            float bevel = Mathf.Min(ArchitecturalBevel, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * .2f);
            for (int i = 0; i < vertices.Length; i++)
                for (int axis = 0; axis < 3; axis++)
                {
                    float v = vertices[i][axis];
                    float a = Mathf.Abs(v);
                    float core = .5f - SourcePanelBevel;
                    vertices[i][axis] = Mathf.Sign(v) * (a <= core
                        ? a / core * (size[axis] * .5f - bevel)
                        : size[axis] * .5f - (.5f - a) / SourcePanelBevel * bevel);
                }
            fitted.Clear();
            fitted.vertices = vertices;
            fitted.normals = source.normals;
            fitted.uv = source.uv;
            fitted.triangles = source.triangles;
            fitted.RecalculateBounds();
            fitted.RecalculateTangents();
            EditorUtility.SetDirty(fitted);
            FittedPanels[key] = fitted;
            return fitted;
        }

        internal static void Import(string[] onlyModels = null)
        {
            Directory.CreateDirectory(Materials);
            Directory.CreateDirectory(Art + "/Meshes");
            AssetDatabase.Refresh();
            var palette = JsonUtility.FromJson<Palette>(File.ReadAllText(Art + "/palette.json"));
            foreach (var entry in palette.materials)
                MakeMaterial(entry.name.Substring(3), new Color(entry.color[0], entry.color[1], entry.color[2]),
                    1 - entry.roughness, entry.metallic, entry.emission);
            BuildAudienceAtlas(palette);

            foreach (string file in Directory.GetFiles(Art + "/Models", "*.fbx"))
            {
                if (onlyModels != null && !onlyModels.Contains(Path.GetFileNameWithoutExtension(file).Substring(3))) continue;
                string path = file.Replace('\\', '/');
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.isReadable = true;
                foreach (var entry in palette.materials)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.name),
                        Mat(entry.name.Substring(3)));
                importer.SaveAndReimport();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var source = prefab.GetComponentInChildren<MeshFilter>();
                var mesh = UnityEngine.Object.Instantiate(source.sharedMesh);
                string model = Path.GetFileNameWithoutExtension(path).Substring(3);
                mesh.name = "HS_" + model;
                // Blender -Y faces the Unity player at -Z after this handedness correction.
                Matrix4x4 transform = Matrix4x4.Rotate(Quaternion.Euler(0, 180, 0)) * source.transform.localToWorldMatrix;
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                Matrix4x4 normalTransform = transform.inverse.transpose;
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = transform.MultiplyPoint3x4(vertices[i]);
                    normals[i] = normalTransform.MultiplyVector(normals[i]).normalized;
                }
                mesh.vertices = vertices;
                mesh.normals = normals;
                if (transform.determinant < 0)
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        int[] triangles = mesh.GetTriangles(s);
                        for (int i = 0; i < triangles.Length; i += 3)
                        { int swap = triangles[i]; triangles[i] = triangles[i + 1]; triangles[i + 1] = swap; }
                        mesh.SetTriangles(triangles, s);
                    }
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                if (model.StartsWith("Fan", StringComparison.Ordinal))
                {
                    // One shared palette atlas replaces six material draws per spectator.
                    var mapped = source.GetComponent<Renderer>().sharedMaterials;
                    Vector2[] uv = mesh.uv;
                    var indices = new List<int>();
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        int column = Array.FindIndex(palette.materials, e => e.name == mapped[s].name);
                        if (column < 0) throw new InvalidOperationException("Unknown crowd palette material.");
                        int[] triangles = mesh.GetTriangles(s);
                        foreach (int vertex in triangles) uv[vertex] = new Vector2((column + .5f) / palette.materials.Length, .5f);
                        indices.AddRange(triangles);
                    }
                    mesh.uv = uv;
                    mesh.subMeshCount = 1;
                    mesh.SetTriangles(indices, 0);
                }
                string meshPath = Art + "/Meshes/HS_" + model + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (existing == null) AssetDatabase.CreateAsset(mesh, meshPath);
                else
                {
                    // CopySerialized leaves an already uploaded Mesh's GPU buffer stale.
                    // Explicit mesh setters update both the asset and the live renderer.
                    existing.Clear();
                    existing.vertices = mesh.vertices;
                    existing.normals = mesh.normals;
                    existing.uv = mesh.uv;
                    existing.tangents = mesh.tangents;
                    existing.subMeshCount = mesh.subMeshCount;
                    for (int s = 0; s < mesh.subMeshCount; s++) existing.SetTriangles(mesh.GetTriangles(s), s);
                    existing.RecalculateBounds();
                    existing.UploadMeshData(false);
                    EditorUtility.SetDirty(existing);
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }
            ModelMaterials.Clear();
            AssetDatabase.SaveAssets();
        }

        private static Material BuildAudienceAtlas(Palette palette)
        {
            string path = Art + "/AudiencePalette.png";
            var texture = new Texture2D(palette.materials.Length, 1, TextureFormat.RGB24, false);
            for (int i = 0; i < palette.materials.Length; i++)
            {
                var c = palette.materials[i].color;
                texture.SetPixel(i, 0, new Color(c[0], c[1], c[2]));
            }
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var material = MakeMaterial("Audience", Color.white, .25f);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            EditorUtility.SetDirty(material);
            return material;
        }

        internal static Material MakeMaterial(string name, Color color, float smoothness = .4f,
            float metallic = 0, float emission = 0)
        {
            string path = Materials + "/HS_" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            material.enableInstancing = true;
            if (emission > 0) material.EnableKeyword("_EMISSION");
            else material.DisableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emission);
            // URP validation otherwise treats imported emissive surfaces as black and
            // can drop the emission variant during a later reimport or editor reload.
            material.globalIlluminationFlags = emission > 0
                ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(material);
            return material;
        }

        internal static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>(Materials + "/HS_" + name + ".mat");
        internal static Mesh Mesh(string name) => AssetDatabase.LoadAssetAtPath<Mesh>(Art + "/Meshes/HS_" + name + ".asset");

        internal static Transform Place(Transform parent, string model, Vector3 position, float yaw = 0,
            string objectName = null)
        {
            var mesh = Mesh(model);
            if (mesh == null) throw new InvalidOperationException("Import HoleInWall studio module first: " + model);
            var go = new GameObject(objectName ?? "HS_" + model);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            if (!ModelMaterials.TryGetValue(model, out var materials))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Models/HS_" + model + ".fbx");
                materials = model.StartsWith("Fan", StringComparison.Ordinal) ? new[] { Mat("Audience") } :
                    prefab.GetComponentInChildren<Renderer>().sharedMaterials;
                ModelMaterials[model] = materials;
            }
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return go.transform;
        }

        internal static Transform Panel(Transform parent, string name, Vector3 position, Vector3 size,
            Material material, bool solid = false)
        {
            Transform panel = Place(parent, "Panel", position, 0, name);
            panel.GetComponent<MeshFilter>().sharedMesh = FittedPanel(size);
            panel.GetComponent<Renderer>().sharedMaterial = material;
            if (solid)
            {
                panel.gameObject.layer = LayerMask.NameToLayer("Cover");
                panel.gameObject.AddComponent<BoxCollider>();
            }
            return panel;
        }
    }
}
