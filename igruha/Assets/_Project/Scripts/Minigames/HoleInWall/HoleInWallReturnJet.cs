using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>Soft water wake below a returning player. Entirely absent between returns.</summary>
    public sealed class HoleInWallReturnJet : MonoBehaviour
    {
        private const int Segments = 32;
        private const int Rows = 8;
        private const int TubeVertices = (Segments + 1) * (Rows + 1);
        private const float WakeHeight = .85f;
        private static readonly int WaterLevelId = Shader.PropertyToID("_WaterLevel");

        [SerializeField] private MeshFilter surface;
        [SerializeField] private MeshRenderer surfaceRenderer;
        [SerializeField] private ParticleSystem spray;

        private readonly Vector3[] vertices = new Vector3[TubeVertices + (Segments + 1) * 2];
        private readonly Color[] colors = new Color[TubeVertices + (Segments + 1) * 2];
        private Mesh mesh;
        private ParticleSystemRenderer sprayRenderer;
        private MaterialPropertyBlock properties;
        private float waterY, surfacedAt = -1;

        private void Awake()
        {
            mesh = new Mesh { name = "Return water wake (pooled)" };
            mesh.MarkDynamic();
            var uv = new Vector2[vertices.Length];
            var indices = new int[Segments * (Rows + 1) * 6];
            int cursor = 0;
            for (int i = 0; i <= Segments; i++)
            {
                for (int row = 0; row <= Rows; row++)
                {
                    int v = i * (Rows + 1) + row;
                    uv[v] = new Vector2(i / (float)Segments, row / (float)Rows);
                    if (i < Segments && row < Rows)
                        Quad(indices, ref cursor, v, v + 1, v + Rows + 1, v + Rows + 2);
                }
                int r = TubeVertices + i * 2;
                uv[r] = new Vector2(i / (float)Segments, 0);
                uv[r + 1] = new Vector2(i / (float)Segments, 1);
                if (i < Segments) Quad(indices, ref cursor, r, r + 1, r + 2, r + 3);
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.uv = uv;
            mesh.triangles = indices;
            surface.sharedMesh = mesh;
            sprayRenderer = spray.GetComponent<ParticleSystemRenderer>();
            properties = new MaterialPropertyBlock();
            Clear();
        }

        private static void Quad(int[] indices, ref int cursor, int a, int b, int c, int d)
        {
            indices[cursor++] = a; indices[cursor++] = b; indices[cursor++] = c;
            indices[cursor++] = c; indices[cursor++] = b; indices[cursor++] = d;
        }

        public void Begin(Vector3 source, float level)
        {
            Clear();
            waterY = level;
            surfacedAt = -1;
            transform.position = new Vector3(source.x, level + .04f, source.z);
            properties.SetFloat(WaterLevelId, level);
            sprayRenderer.SetPropertyBlock(properties);
            surfaceRenderer.enabled = true;
        }

        public void Draw(Vector3 feet, float progress)
        {
            Vector3 tip = feet - transform.position - Vector3.up * .06f;
            bool aboveWater = feet.y > waterY + .08f;
            if (aboveWater && surfacedAt < 0) surfacedAt = progress;
            float pulse = Mathf.SmoothStep(0, 1, progress / .18f) *
                (1 - Mathf.SmoothStep(.65f, .88f, progress));
            // A short wake while submerged, then a tapering jet from the surface.
            Vector3 foot = aboveWater ? Vector3.zero : tip - Vector3.up * WakeHeight;
            for (int i = 0; i <= Segments; i++)
            {
                float angle = i * Mathf.PI * 2 / Segments;
                float flutes = .8f + .2f * Mathf.Sin(angle * 5 + progress * 12);
                for (int row = 0; row <= Rows; row++)
                {
                    float v = row / (float)Rows;
                    float radius = .16f + Mathf.Sin(v * Mathf.PI) * .18f + v * v * .24f;
                    float twist = angle + v * .8f + progress * 4;
                    Vector3 center = Vector3.Lerp(foot, tip, v);
                    int index = i * (Rows + 1) + row;
                    vertices[index] = center + new Vector3(Mathf.Cos(twist), 0, Mathf.Sin(twist)) * radius;
                    colors[index] = new Color(.64f, .93f, .98f, pulse * flutes * .55f);
                }
                float age = surfacedAt < 0 ? 0 : progress - surfacedAt;
                float ringRadius = .35f + age * 3;
                float opacity = surfacedAt < 0 ? 0 : Mathf.Sin(Mathf.Clamp01(age / .5f) * Mathf.PI) * .35f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                int ring = TubeVertices + i * 2;
                vertices[ring] = direction * (ringRadius - .05f);
                vertices[ring + 1] = direction * (ringRadius + .05f);
                colors[ring] = colors[ring + 1] = new Color(.7f, .96f, 1, opacity);
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.RecalculateBounds();
            spray.transform.position = feet;
            bool emitting = aboveWater && progress < .74f;
            if (emitting && !spray.isPlaying) spray.Play();
            else if (!emitting && spray.isPlaying) spray.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        public void Clear()
        {
            if (surfaceRenderer != null) surfaceRenderer.enabled = false;
            if (spray != null) spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnDisable() => Clear();
        private void OnDestroy() { if (mesh != null) Destroy(mesh); }
    }
}
