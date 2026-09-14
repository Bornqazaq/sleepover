using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    /// <summary>Small static trim pieces merged by material within one architectural section.</summary>
    internal sealed class HubRoomGeometry
    {
        internal const string Folder = "Assets/_Project/Art/Hub/Room";
        private sealed class Batch
        {
            internal readonly List<Vector3> Vertices = new();
            internal readonly List<int> Indices = new();
        }
        private readonly Dictionary<Material, Batch> batches = new();

        internal void Box(Vector3 center, Vector3 size, Material material, Quaternion? rotation = null)
        {
            Quaternion q = rotation ?? Quaternion.identity;
            Vector3 x = q * Vector3.right * size.x * .5f;
            Vector3 y = q * Vector3.up * size.y * .5f;
            Vector3 z = q * Vector3.forward * size.z * .5f;
            Quad(center - x - y - z, center - x + y - z, center + x + y - z, center + x - y - z, material);
            Quad(center + x - y + z, center + x + y + z, center - x + y + z, center - x - y + z, material);
            Quad(center - x - y + z, center - x + y + z, center - x + y - z, center - x - y - z, material);
            Quad(center + x - y - z, center + x + y - z, center + x + y + z, center + x - y + z, material);
            Quad(center - x + y - z, center - x + y + z, center + x + y + z, center + x + y - z, material);
            Quad(center - x - y + z, center - x - y - z, center + x - y - z, center + x - y + z, material);
        }

        internal void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Material material)
        {
            var batch = Get(material); int n = batch.Vertices.Count;
            batch.Vertices.AddRange(new[] { a, b, c, d });
            batch.Indices.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
        }

        internal void Disc(Vector3 center, Vector3 right, Vector3 up, Material material, int sides = 40)
        {
            var batch = Get(material); int n = batch.Vertices.Count;
            batch.Vertices.Add(center);
            for (int i = 0; i <= sides; i++)
            {
                float angle = i * Mathf.PI * 2 / sides;
                batch.Vertices.Add(center + right * Mathf.Cos(angle) + up * Mathf.Sin(angle));
                if (i > 0) batch.Indices.AddRange(new[] { n, n + i, n + i + 1 });
            }
        }

        internal void Segment(Vector3 from, Vector3 to, float radius, Material material)
        {
            Vector3 delta = to - from;
            Box((from + to) * .5f, new Vector3(radius, radius, delta.magnitude), material,
                Quaternion.LookRotation(delta));
        }

        internal void Instance(Mesh source, Vector3 position, Vector3 scale, Material material)
        {
            var batch = Get(material); int start = batch.Vertices.Count;
            foreach (var vertex in source.vertices) batch.Vertices.Add(position + Vector3.Scale(vertex, scale));
            foreach (int index in source.triangles) batch.Indices.Add(start + index);
        }

        internal void Build(Transform parent, string name, bool castShadows = true, string folder = Folder)
        {
            HubCozyMaterials.EnsureFolder(folder + "/Meshes");
            foreach (var pair in batches)
            {
                string meshName = name + "_" + pair.Key.name;
                var mesh = new Mesh { name = meshName };
                mesh.SetVertices(pair.Value.Vertices); mesh.SetTriangles(pair.Value.Indices, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                string path = folder + "/Meshes/" + meshName + ".asset";
                var stored = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (stored == null) { AssetDatabase.CreateAsset(mesh, path); stored = mesh; }
                else { EditorUtility.CopySerialized(mesh, stored); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(stored); }
                var go = HubCozyGeometry.MeshObject(meshName, parent, stored, pair.Key);
                go.GetComponent<Renderer>().shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
        }

        private Batch Get(Material material)
        {
            if (!batches.TryGetValue(material, out var batch)) { batch = new Batch(); batches.Add(material, batch); }
            return batch;
        }
    }
}
