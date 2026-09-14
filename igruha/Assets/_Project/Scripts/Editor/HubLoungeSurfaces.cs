using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Repeatable textile maps and render-only UV copies. Source meshes and colliders stay intact.</summary>
    internal static class HubLoungeSurfaces
    {
        internal const string Folder = "Assets/_Project/Art/Hub/Lounge";
        private const int TextureSize = 512, ThreadsPerTile = 16;
        private static Texture2D fabric, normal, oak;

        internal static void Prepare()
        {
            HubCozyMaterials.EnsureFolder(Folder + "/Textures");
            HubCozyMaterials.EnsureFolder(Folder + "/Meshes");
            fabric = Texture("HL_Linen", false, false);
            normal = Texture("HL_LinenNormal", true, false);
            oak = Texture("HL_OakGrain", false, true);
        }

        private static float Height(int x, int y)
        {
            x = (x + TextureSize) % TextureSize; y = (y + TextureSize) % TextureSize;
            const float pitch = TextureSize / (float)ThreadsPerTile;
            float u = (x + .5f) / pitch, v = (y + .5f) / pitch;
            float a = Mathf.Pow(Mathf.Sin(Mathf.PI * (u - Mathf.Floor(u))), .55f);
            float b = Mathf.Pow(Mathf.Sin(Mathf.PI * (v - Mathf.Floor(v))), .55f);
            bool horizontal = ((Mathf.FloorToInt(u) + Mathf.FloorToInt(v)) & 1) == 0;
            float thread = horizontal ? a * (.76f + .24f * b) : b * (.76f + .24f * a);
            float fine = Mathf.Sin((horizontal ? x : y) * Mathf.PI * .5f) * .018f;
            return thread + fine;
        }

        private static Texture2D Texture(string name, bool isNormal, bool isWood)
        {
            string path = Folder + "/Textures/" + name + ".png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            // Periodic analytic thread profiles; no image editing or dependency on an external atlas.
            var pixels = new Color[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++) for (int x = 0; x < TextureSize; x++)
            {
                if (isNormal)
                {
                    float dx = (Height(x + 1, y) - Height(x - 1, y)) * 2.4f;
                    float dy = (Height(x, y + 1) - Height(x, y - 1)) * 2.4f;
                    Vector3 n = new Vector3(-dx, -dy, 1).normalized;
                    pixels[y * TextureSize + x] = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1);
                }
                else if (isWood)
                {
                    float u = (x + .5f) / TextureSize, v = (y + .5f) / TextureSize;
                    float bend = .20f * Mathf.Sin(v * Mathf.PI * 2) + .05f * Mathf.Sin(v * Mathf.PI * 6);
                    float grain = Mathf.Sin((u * 13 + bend) * Mathf.PI * 2);
                    float fine = Mathf.Sin((u * 61 + bend * 2) * Mathf.PI * 2);
                    float value = .90f + grain * .055f + fine * .018f;
                    pixels[y * TextureSize + x] = new Color(value, value, value, 1);
                }
                else
                {
                    float value = .83f + .17f * Height(x, y);
                    pixels[y * TextureSize + x] = new Color(value, value, value, 1);
                }
            }
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false, isNormal);
            texture.SetPixels(pixels); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !isNormal; importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat; importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8; importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        internal static Material Fabric(string name, string color, float relief = .45f)
        {
            var m = HubCozyMaterials.Surface("HL_" + name, color, .035f);
            m.SetTexture("_BaseMap", fabric); m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", relief); m.EnableKeyword("_NORMALMAP");
            return m;
        }

        internal static Material Wood(string name, string color)
        {
            var m = HubCozyMaterials.Surface("HL_" + name, color, .22f);
            m.SetTexture("_BaseMap", oak); m.SetFloat("_SpecularHighlights", 1);
            return m;
        }

        internal static void Dress(string path, Material material, float metresPerTile)
        {
            var go = HubRoomPass.Require(path);
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
            {
                filter.sharedMesh = Project(filter, "HL_" + go.name + "_" + filter.name, metresPerTile, false);
                filter.GetComponent<Renderer>().sharedMaterial = material;
            }
        }

        internal static void Table()
        {
            var filter = HubRoomPass.Require("_Pit/CoffeeTable").GetComponent<MeshFilter>();
            filter.sharedMesh = Project(filter, "HL_CoffeeTable", .6f, true);
            var frame = HubCozyMaterials.Surface("HL_TableFrame", "3B4941", .30f);
            frame.SetFloat("_Metallic", .15f); frame.SetFloat("_SpecularHighlights", 1);
            filter.GetComponent<Renderer>().sharedMaterials = new[] { Wood("TableOak", "AA8057"), frame };
        }

        private static Mesh Project(MeshFilter filter, string name, float metresPerTile, bool splitTable)
        {
            string path = Folder + "/Meshes/" + name + ".asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved != null) return saved;
            var source = filter.sharedMesh;
            var vertices = source.vertices; var normals = source.normals; var sourceFaces = source.triangles;
            var output = new List<Vector3>(); var outputNormals = new List<Vector3>(); var uv = new List<Vector2>();
            var faces = new[] { new List<int>(), new List<int>() };
            Bounds worldBounds = filter.GetComponent<Renderer>().bounds;
            for (int i = 0; i < sourceFaces.Length; i += 3)
            {
                Vector3 a = vertices[sourceFaces[i]], b = vertices[sourceFaces[i + 1]], c = vertices[sourceFaces[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                var abs = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                int axis = abs.y >= abs.x && abs.y >= abs.z ? 1 : abs.x >= abs.z ? 0 : 2;
                int material = splitTable && filter.transform.TransformPoint((a + b + c) / 3).y < worldBounds.max.y - .10f ? 1 : 0;
                for (int corner = 0; corner < 3; corner++)
                {
                    int index = sourceFaces[i + corner]; Vector3 v = vertices[index];
                    Vector3 scaled = Vector3.Scale(v, filter.transform.lossyScale) / metresPerTile;
                    faces[material].Add(output.Count); output.Add(v); outputNormals.Add(normals.Length == vertices.Length ? normals[index] : n);
                    uv.Add(axis == 1 ? new Vector2(scaled.x, scaled.z) : axis == 0 ? new Vector2(scaled.z, scaled.y) : new Vector2(scaled.x, scaled.y));
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(output); mesh.SetNormals(outputNormals); mesh.SetUVs(0, uv);
            mesh.subMeshCount = splitTable ? 2 : 1;
            mesh.SetTriangles(faces[0], 0); if (splitTable) mesh.SetTriangles(faces[1], 1);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            if ((mesh.bounds.size - source.bounds.size).sqrMagnitude > .000001f)
                throw new InvalidOperationException("UV projection changed the mesh bounds: " + filter.name);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
