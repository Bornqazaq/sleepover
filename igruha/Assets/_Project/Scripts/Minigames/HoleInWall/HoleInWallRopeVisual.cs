using Igruha.Core.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>Three-strand cord following the existing tether simulation; no physics or network state.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class HoleInWallRopeVisual : MonoBehaviour
    {
        private const int Rings = 121;
        private const int Sides = 6;
        private const int Strands = 3;
        private const float StrandRadius = .023f;
        private const float BraidRadius = .017f;
        private const float Turns = 18;
        private const int CollarRings = 6;
        private const int CollarSides = 12;
        private const int CordVertices = Rings * Sides * Strands;
        private PlayerTether tether;
        private LineRenderer source;
        private Mesh mesh;
        private MeshRenderer cordRenderer;
        private Vector3[] points;
        private float[] distances;
        private readonly Vector3[] vertices = new Vector3[CordVertices + 2 * CollarRings * CollarSides];
        private readonly Vector3[] normals = new Vector3[CordVertices + 2 * CollarRings * CollarSides];
        private Material originalLineMaterial;
        private Matrix4x4 worldToLocal;
        private readonly Vector2[] circle = new Vector2[Sides];

        public void Initialize(PlayerTether owner, Material cord, Material tracer, Material collar)
        {
            for (int side = 0; side < Sides; side++)
            {
                float angle = side * (Mathf.PI * 2 / Sides);
                circle[side] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            tether = owner;
            source = owner.GetComponent<LineRenderer>();
            points = new Vector3[source.positionCount];
            distances = new float[points.Length];
            originalLineMaterial = source.sharedMaterial;
            // Keep enabled/positions under PlayerTether control; suppress only its flat ribbon.
            source.forceRenderingOff = true;
            var child = new GameObject("Braided cord");
            child.transform.SetParent(transform, false);
            mesh = new Mesh { name = "HoleInWall braided cord" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.subMeshCount = 3;
            mesh.SetTriangles(CordIndices(0, 2), 0);
            mesh.SetTriangles(CordIndices(2, 1), 1);
            int[] collars = new int[2 * (CollarRings - 1) * CollarSides * 6];
            int cursor = 0;
            for (int end = 0; end < 2; end++)
                Stitch(collars, ref cursor, CordVertices + end * CollarRings * CollarSides,
                    CollarRings, CollarSides);
            mesh.SetTriangles(collars, 2);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            cordRenderer = child.AddComponent<MeshRenderer>();
            cordRenderer.sharedMaterials = new[] { cord, tracer, collar };
            cordRenderer.shadowCastingMode = ShadowCastingMode.On;
            cordRenderer.receiveShadows = true;
            cordRenderer.enabled = false;
        }

        private static int[] CordIndices(int first, int count)
        {
            int[] indices = new int[count * (Rings - 1) * Sides * 6];
            int cursor = 0;
            for (int strand = first; strand < first + count; strand++)
                Stitch(indices, ref cursor, strand * Rings * Sides, Rings, Sides);
            return indices;
        }

        private static void Stitch(int[] indices, ref int cursor, int offset, int rings, int sides)
        {
            for (int ring = 0; ring < rings - 1; ring++)
                for (int side = 0; side < sides; side++)
                {
                    int a = offset + ring * sides + side;
                    int b = offset + ring * sides + (side + 1) % sides;
                    indices[cursor++] = a; indices[cursor++] = b; indices[cursor++] = a + sides;
                    indices[cursor++] = b; indices[cursor++] = b + sides; indices[cursor++] = a + sides;
                }
        }

        private void LateUpdate()
        {
            if (cordRenderer == null) return;
            bool visible = tether != null && tether.Bound && source.enabled;
            cordRenderer.enabled = visible;
            if (!visible) return;
            worldToLocal = transform.worldToLocalMatrix;
            source.GetPositions(points);
            for (int i = 1; i < points.Length; i++)
                distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            Vector3 previousNormal = Vector3.up;
            for (int ring = 0; ring < Rings; ring++)
            {
                float t = ring / (float)(Rings - 1);
                Vector3 center = Sample(t);
                Vector3 tangent = (Sample(Mathf.Min(1, t + .002f)) -
                    Sample(Mathf.Max(0, t - .002f))).normalized;
                if (tangent.sqrMagnitude < .5f) tangent = Vector3.right;
                Vector3 normal = Vector3.ProjectOnPlane(previousNormal, tangent).normalized;
                if (normal.sqrMagnitude < .5f) normal = Vector3.Cross(tangent, Vector3.forward).normalized;
                if (normal.sqrMagnitude < .5f) normal = Vector3.right;
                Vector3 binormal = Vector3.Cross(tangent, normal);
                previousNormal = normal;
                for (int strand = 0; strand < Strands; strand++)
                {
                    float twist = (t * Turns + strand / (float)Strands) * Mathf.PI * 2;
                    float cos = Mathf.Cos(twist), sin = Mathf.Sin(twist);
                    Vector3 strandNormal = normal * cos + binormal * sin;
                    Vector3 strandBinormal = -normal * sin + binormal * cos;
                    Vector3 strandCenter = center + BraidRadius * strandNormal;
                    for (int side = 0; side < Sides; side++)
                    {
                        Vector3 radial = strandNormal * circle[side].x + strandBinormal * circle[side].y;
                        int index = strand * Rings * Sides + ring * Sides + side;
                        vertices[index] = worldToLocal.MultiplyPoint3x4(strandCenter + radial * StrandRadius);
                        normals[index] = worldToLocal.MultiplyVector(radial);
                    }
                }
                if (ring == 0 || ring == Rings - 1)
                    Collar(ring == 0 ? 0 : 1, center, ring == 0 ? tangent : -tangent, normal);
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.RecalculateBounds();
        }

        private void Collar(int end, Vector3 center, Vector3 tangent, Vector3 normal)
        {
            Vector3 binormal = Vector3.Cross(tangent, normal);
            for (int ring = 0; ring < CollarRings; ring++)
            {
                float along = ring * .022f;
                float radius = ring == 0 || ring == CollarRings - 1 ? .038f : .050f;
                for (int side = 0; side < CollarSides; side++)
                {
                    float a = side * (Mathf.PI * 2 / CollarSides);
                    Vector3 radial = normal * Mathf.Cos(a) + binormal * Mathf.Sin(a);
                    int index = CordVertices + end * CollarRings * CollarSides + ring * CollarSides + side;
                    vertices[index] = worldToLocal.MultiplyPoint3x4(center + tangent * along + radial * radius);
                    normals[index] = worldToLocal.MultiplyVector(radial);
                }
            }
        }

        private Vector3 Sample(float t)
        {
            float distance = t * distances[distances.Length - 1];
            int i = 0;
            while (i < points.Length - 2 && distances[i + 1] < distance) i++;
            float segment = distances[i + 1] - distances[i];
            float u = segment > .00001f ? (distance - distances[i]) / segment : 0;
            Vector3 a = points[Mathf.Max(0, i - 1)], b = points[i];
            Vector3 c = points[i + 1], d = points[Mathf.Min(points.Length - 1, i + 2)];
            return .5f * ((2 * b) + (-a + c) * u + (2 * a - 5 * b + 4 * c - d) * u * u +
                (-a + 3 * b - 3 * c + d) * u * u * u);
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            // PlayerTether creates this unique fallback material in Awake.
            if (originalLineMaterial != null) Destroy(originalLineMaterial);
        }
    }
}
