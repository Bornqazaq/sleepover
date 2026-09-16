using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    /// <summary>Small decorative meshes: rug, pendant shades and a sofa throw. No physics.</summary>
    internal static class HubCozyGeometry
    {
        private const string MeshFolder = "Assets/_Project/Art/Hub/Cozy";

        internal static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return go;
        }

        private static Mesh Store(string name, List<Vector3> vertices, List<int> indices)
        {
            HubCozyMaterials.EnsureFolder(MeshFolder);
            string path = MeshFolder + "/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = name };
                AssetDatabase.CreateAsset(mesh, path);
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static void Quad(List<Vector3> vertices, List<int> triangles,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        private static void Patch(List<Vector3> vertices, List<int> triangles,
            float x, float z, float width, float depth, float y)
        {
            Quad(vertices, triangles, new Vector3(x, y, z), new Vector3(x, y, z + depth),
                new Vector3(x + width, y, z + depth), new Vector3(x + width, y, z));
        }

        internal static void Rug(Transform parent, Vector3 center)
        {
            const float width = 6.7f, depth = 4.5f;
            var colors = new[] { "D7C8AB", "59695A", "B99B65" };
            for (int layer = 0; layer < colors.Length; layer++)
            {
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                float y = layer * .002f;
                if (layer == 0)
                {
                    Patch(vertices, triangles, -width / 2, -depth / 2, width, depth, y);
                    for (int i = 0; i < 56; i++)
                    {
                        float x = -width / 2 + .045f + i * (width - .09f) / 56;
                        Patch(vertices, triangles, x, -depth / 2 - .075f, .025f, .085f, y);
                        Patch(vertices, triangles, x, depth / 2 - .01f, .025f, .085f, y);
                    }
                }
                else
                {
                    float inset = layer == 1 ? .15f : .34f;
                    float stripe = layer == 1 ? .075f : .025f;
                    float w = width - inset * 2, d = depth - inset * 2;
                    Patch(vertices, triangles, -w / 2, -d / 2, w, stripe, y);
                    Patch(vertices, triangles, -w / 2, d / 2 - stripe, w, stripe, y);
                    Patch(vertices, triangles, -w / 2, -d / 2, stripe, d, y);
                    Patch(vertices, triangles, w / 2 - stripe, -d / 2, stripe, d, y);
                    if (layer == 2)
                    {
                        for (int i = 0; i < 7; i++)
                        {
                            float x = -2.25f + .75f * i;
                            Quad(vertices, triangles, new Vector3(x, y, -.22f),
                                new Vector3(x - .075f, y, 0), new Vector3(x, y, .22f),
                                new Vector3(x + .075f, y, 0));
                        }
                    }
                }
                var go = MeshObject("WovenRug_" + layer, parent,
                    Store("HC_Rug_" + layer, vertices, triangles),
                    HubCozyMaterials.Surface("HC_Rug_" + layer, colors[layer], 0));
                go.transform.position = center;
                go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        internal static Mesh Shade()
        {
            const int segments = 48;
            var profile = new[] { new Vector2(.055f, .60f), new Vector2(.13f, .58f),
                new Vector2(.24f, .53f), new Vector2(.34f, .46f), new Vector2(.43f, .37f),
                new Vector2(.50f, .27f), new Vector2(.55f, .17f), new Vector2(.565f, .10f) };
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            var normals = new List<Vector3>();
            for (int ring = 0; ring < profile.Length; ring++)
            for (int segment = 0; segment <= segments; segment++)
            {
                float angle = segment * Mathf.PI * 2 / segments;
                Vector2 p = profile[ring];
                Vector2 tangent = profile[Mathf.Min(ring + 1, profile.Length - 1)] - profile[Mathf.Max(0, ring - 1)];
                vertices.Add(new Vector3(Mathf.Cos(angle) * p.x, p.y, Mathf.Sin(angle) * p.x));
                normals.Add(new Vector3(-tangent.y * Mathf.Cos(angle), tangent.x, -tangent.y * Mathf.Sin(angle)).normalized);
                if (ring == profile.Length - 1 || segment == segments) continue;
                int a = ring * (segments + 1) + segment, b = a + segments + 1;
                triangles.AddRange(new[] { a, a + 1, b + 1, a, b + 1, b });
            }
            Mesh mesh = Store("HC_PendantShade", vertices, triangles);
            mesh.SetNormals(normals);
            return mesh;
        }

        internal static Mesh Globe()
        {
            const int segments = 12, rings = 8;
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            var normals = new List<Vector3>();
            for (int ring = 0; ring <= rings; ring++)
            for (int segment = 0; segment <= segments; segment++)
            {
                float elevation = ring * Mathf.PI / rings, angle = segment * Mathf.PI * 2 / segments;
                var normal = new Vector3(Mathf.Sin(elevation) * Mathf.Cos(angle), Mathf.Cos(elevation),
                    Mathf.Sin(elevation) * Mathf.Sin(angle));
                vertices.Add(normal * .5f); normals.Add(normal);
                if (ring == rings || segment == segments) continue;
                int a = ring * (segments + 1) + segment, b = a + segments + 1;
                if (ring > 0) triangles.AddRange(new[] { a, a + 1, b + 1 });
                if (ring < rings - 1) triangles.AddRange(new[] { a, b + 1, b });
            }
            Mesh mesh = Store("HC_Globe", vertices, triangles);
            mesh.SetNormals(normals);
            return mesh;
        }

        internal static void Throw(Transform parent, Bounds sofa)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            float x = sofa.center.x + .35f, y = sofa.max.y + .015f, z = sofa.min.z - .025f;
            for (int fold = 0; fold < 16; fold++)
            {
                float a = fold / 16f, b = (fold + 1) / 16f;
                float za = z - .015f * Mathf.Sin(a * Mathf.PI * 8), zb = z - .015f * Mathf.Sin(b * Mathf.PI * 8);
                float ya = y - .56f + .018f * Mathf.Cos(a * Mathf.PI * 6), yb = y - .56f + .018f * Mathf.Cos(b * Mathf.PI * 6);
                Quad(vertices, triangles, new Vector3(x + a * .68f, ya, za), new Vector3(x + a * .68f, y, za),
                    new Vector3(x + b * .68f, y, zb), new Vector3(x + b * .68f, yb, zb));
                Quad(vertices, triangles, new Vector3(x + a * .68f, y, za), new Vector3(x + a * .68f, y - .02f, z + .23f),
                    new Vector3(x + b * .68f, y - .02f, z + .23f), new Vector3(x + b * .68f, y, zb));
                Quad(vertices, triangles, new Vector3(x + a * .68f, ya - .04f, za), new Vector3(x + a * .68f, ya, za),
                    new Vector3(x + a * .68f + .02f, ya, za), new Vector3(x + a * .68f + .02f, ya - .04f, za));
            }
            MeshObject("SofaThrow", parent, Store("HC_SofaThrow", vertices, triangles),
                HubCozyMaterials.Surface("HC_Throw", "71877C", 0));
        }
    }
}
