using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Metre-sized fillets for the Carry construction kit; physics stays separate.</summary>
    internal static class CarryRoundedEdges
    {
        private const int Segments = 3;
        private const string Folder = CarrySiteFinish.Art + "/Meshes/Rounded";

        internal static float Radius(Vector3 size) => Mathf.Min(.10f, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * .18f);

        internal static void Apply(Transform arena)
        {
            Directory.CreateDirectory(Folder);
            foreach (var filter in arena.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || (filter.sharedMesh.name != "Cube" && !AssetDatabase.GetAssetPath(filter.sharedMesh).StartsWith(Folder + "/"))) continue;
                // Painted markings are flat graphics, not solid blocks.
                if (filter.name == "RoadPaint" || filter.name == "Expansion paint" || filter.name == "Stripe") continue;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || renderer.sharedMaterial == null || renderer.sharedMaterial.shader.name.Contains("Cloud")) continue;
                var scale = filter.transform.lossyScale;
                var size = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                if (Mathf.Min(size.x, Mathf.Min(size.y, size.z)) < .02f) continue;
                string key = size.x.ToString("F4", CultureInfo.InvariantCulture) + "_" + size.y.ToString("F4", CultureInfo.InvariantCulture) + "_" + size.z.ToString("F4", CultureInfo.InvariantCulture);
                string path = Folder + "/Box_" + key + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null)
                {
                    var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
                    AppendBox(v, n, uv, tri, Vector3.zero, size, Quaternion.identity);
                    // Keep the original transform and collider, with fillets measured in world metres.
                    for (int i = 0; i < v.Count; i++)
                    {
                        v[i] = new Vector3(v[i].x / size.x, v[i].y / size.y, v[i].z / size.z);
                        n[i] = Vector3.Scale(n[i], size).normalized;
                    }
                    mesh = new Mesh { name = "CarryRounded_" + key };
                    mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0); mesh.RecalculateBounds();
                    AssetDatabase.CreateAsset(mesh, path);
                }
                filter.sharedMesh = mesh;
            }
        }

        internal static void AppendBox(List<Vector3> v, List<Vector3> normals, List<Vector2> uv, List<int> tri,
            Vector3 center, Vector3 size, Quaternion rotation)
        {
            var h = size * .5f;
            float radius = Radius(size);
            var core = h - Vector3.one * radius;
            // Six flat faces, twelve cylindrical edges, eight spherical corner patches.
            for (int axis = 0; axis < 3; axis++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int u = (axis + 1) % 3, w = (axis + 2) % 3;
                var normal = Vector3.zero; normal[axis] = sign;
                var a = Vector3.zero; a[axis] = h[axis]; a[axis] *= sign; a[u] = -core[u]; a[w] = -core[w];
                var b = a; b[u] = core[u]; var c = b; c[w] = core[w]; var d = a; d[w] = core[w];
                Face(v, normals, uv, tri, center, rotation, a, b, c, normal, normal, normal);
                Face(v, normals, uv, tri, center, rotation, a, c, d, normal, normal, normal);
            }
            for (int axis = 0; axis < 3; axis++)
            for (int su = -1; su <= 1; su += 2)
            for (int sw = -1; sw <= 1; sw += 2)
            {
                int u = (axis + 1) % 3, w = (axis + 2) % 3;
                var basePoint = Vector3.zero; basePoint[u] = su * core[u]; basePoint[w] = sw * core[w];
                for (int j = 0; j < Segments; j++)
                {
                    var n0 = Vector3.zero; n0[u] = su * (Segments - j); n0[w] = sw * j; n0.Normalize();
                    var n1 = Vector3.zero; n1[u] = su * (Segments - j - 1); n1[w] = sw * (j + 1); n1.Normalize();
                    var a = basePoint + n0 * radius; a[axis] = -core[axis];
                    var b = basePoint + n1 * radius; b[axis] = -core[axis];
                    var c = b; c[axis] = core[axis]; var d = a; d[axis] = core[axis];
                    Face(v, normals, uv, tri, center, rotation, a, b, c, n0, n1, n1);
                    Face(v, normals, uv, tri, center, rotation, a, c, d, n0, n1, n0);
                }
            }
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                var signs = new Vector3(sx, sy, sz); var origin = Vector3.Scale(core, signs);
                for (int i = 0; i < Segments; i++)
                for (int j = 0; j < Segments - i; j++)
                {
                    var a = CornerNormal(i, j, signs); var b = CornerNormal(i + 1, j, signs); var c = CornerNormal(i, j + 1, signs);
                    Face(v, normals, uv, tri, center, rotation, origin + a * radius, origin + b * radius, origin + c * radius, a, b, c);
                    if (i + j < Segments - 1)
                    {
                        var d = CornerNormal(i + 1, j + 1, signs);
                        Face(v, normals, uv, tri, center, rotation, origin + b * radius, origin + d * radius, origin + c * radius, b, d, c);
                    }
                }
            }
        }

        private static Vector3 CornerNormal(int i, int j, Vector3 signs) => Vector3.Scale(new Vector3(i, j, Segments - i - j), signs).normalized;

        private static void Face(List<Vector3> v, List<Vector3> normals, List<Vector2> uv, List<int> tri,
            Vector3 center, Quaternion rotation, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), na + nb + nc) < 0)
            {
                var point = b; b = c; c = point; var normal = nb; nb = nc; nc = normal;
            }
            int start = v.Count;
            v.Add(center + rotation * a); v.Add(center + rotation * b); v.Add(center + rotation * c);
            normals.Add(rotation * na); normals.Add(rotation * nb); normals.Add(rotation * nc);
            var face = na + nb + nc;
            int axis = Mathf.Abs(face.x) > Mathf.Abs(face.y) ? 0 : 1;
            if (Mathf.Abs(face.z) > Mathf.Abs(face[axis])) axis = 2;
            int u = (axis + 1) % 3, w = (axis + 2) % 3;
            uv.Add(new Vector2(a[u], a[w])); uv.Add(new Vector2(b[u], b[w])); uv.Add(new Vector2(c[u], c[w]));
            tri.Add(start); tri.Add(start + 1); tri.Add(start + 2);
        }
    }
}
