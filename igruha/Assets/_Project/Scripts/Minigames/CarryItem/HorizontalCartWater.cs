using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>A clipped volume inside the tub with a world-horizontal free surface.</summary>
    public sealed class HorizontalCartWater : MonoBehaviour
    {
        [SerializeField] private Vector3 size = new Vector3(0.76f, 0.44f, 1.02f);
        private Mesh mesh;
        private MeshFilter filter;
        private readonly Vector3[] corners = new Vector3[8];
        private readonly Vector3[] polygon = new Vector3[8];
        private readonly List<Vector3> vertices = new List<Vector3>(48);
        private readonly List<int> triangles = new List<int>(90);
        private readonly List<Vector3> cap = new List<Vector3>(12);
        private readonly List<Vector2> uv = new List<Vector2>(48);
        // Outward winding. The upper face is unnecessary: the plane never rises above the lowest rim.
        private static readonly int[,] Faces = { {0, 1, 3, 2}, {0, 4, 5, 1}, {2, 3, 7, 6},
            {0, 2, 6, 4}, {1, 5, 7, 3} };
        public Vector3 SurfacePoint { get; private set; }

        private void Awake()
        {
            filter = GetComponentInChildren<MeshFilter>();
            mesh = new Mesh { name = "Cart water: horizontal clipped surface" };
            mesh.MarkDynamic();
            if (filter != null) filter.sharedMesh = mesh;
        }

        public void SetLevel(float level)
        {
            if (filter == null) return;
            float rimY = float.PositiveInfinity;
            for (int i = 0; i < 8; i++)
            {
                corners[i] = transform.TransformPoint(new Vector3((i & 1) == 0 ? -size.x * 0.5f : size.x * 0.5f,
                    (i & 4) == 0 ? 0f : size.y, (i & 2) == 0 ? -size.z * 0.5f : size.z * 0.5f));
                if ((i & 4) != 0) rimY = Mathf.Min(rimY, corners[i].y);
            }
            float height = Mathf.Min(transform.TransformPoint(Vector3.up * size.y * level).y, rimY - 0.001f);
            SurfacePoint = new Vector3(transform.position.x, height, transform.position.z);
            vertices.Clear(); triangles.Clear(); cap.Clear(); uv.Clear();
            for (int face = 0; face < Faces.GetLength(0); face++)
            {
                int count = 0;
                for (int edge = 0; edge < 4; edge++)
                {
                    Vector3 a = corners[Faces[face, edge]], b = corners[Faces[face, (edge + 1) % 4]];
                    bool insideA = a.y <= height, insideB = b.y <= height;
                    if (insideA) polygon[count++] = a;
                    if (insideA == insideB) continue;
                    Vector3 intersection = Vector3.Lerp(a, b, (height - a.y) / (b.y - a.y));
                    polygon[count++] = intersection;
                    bool duplicate = false;
                    for (int i = 0; i < cap.Count; i++)
                        if ((cap[i] - intersection).sqrMagnitude < 0.000001f) { duplicate = true; break; }
                    if (!duplicate) cap.Add(intersection);
                }
                AddFace(polygon, count);
            }
            Vector3 center = Vector3.zero;
            foreach (var point in cap) center += point;
            if (cap.Count > 0) center /= cap.Count;
            // Clockwise viewed from above gives a world-up normal.
            for (int i = 1; i < cap.Count; i++)
            {
                Vector3 point = cap[i]; int j = i - 1;
                float angle = Mathf.Atan2(point.z - center.z, point.x - center.x);
                while (j >= 0 && Mathf.Atan2(cap[j].z - center.z, cap[j].x - center.x) < angle)
                { cap[j + 1] = cap[j]; j--; }
                cap[j + 1] = point;
            }
            for (int i = 0; i < cap.Count; i++) polygon[i] = cap[i];
            AddFace(polygon, cap.Count);
            mesh.Clear();
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
        }

        private void AddFace(Vector3[] points, int count)
        {
            int start = vertices.Count;
            for (int i = 0; i < count; i++)
            {
                vertices.Add(filter.transform.InverseTransformPoint(points[i]));
                uv.Add(new Vector2(points[i].x, points[i].z));
            }
            for (int i = 1; i + 1 < count; i++)
            { triangles.Add(start); triangles.Add(start + i); triangles.Add(start + i + 1); }
        }

        private void OnDestroy() { if (mesh != null) Destroy(mesh); }
    }
}
