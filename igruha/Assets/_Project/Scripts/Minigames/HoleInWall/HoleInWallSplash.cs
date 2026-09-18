using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>One pooled water entry: a collapsing sheet, two surface waves and ballistic spray.
    /// Geometry stays at the contact point; neither the avatar nor its return can drag it away.</summary>
    public sealed class HoleInWallSplash : MonoBehaviour
    {
        private const int Segments = 80;
        private const int Bands = 3;
        private const int CrownRows = 4;
        private const float Duration = 1.45f;
        private const float CrownDuration = .56f;
        private const float SurfaceLift = .035f;
        private static readonly int WaterLevelId = Shader.PropertyToID("_WaterLevel");

        [SerializeField] private ParticleSystem droplets;
        [SerializeField] private MeshFilter surface;
        [SerializeField] private MeshRenderer surfaceRenderer;

        private readonly Vector3[] vertices = new Vector3[(Segments + 1) * (CrownRows + 4)];
        private readonly Color[] colors = new Color[(Segments + 1) * (CrownRows + 4)];
        private readonly Vector2[] directions = new Vector2[Segments + 1];
        private Mesh mesh;
        private ParticleSystemRenderer sprayRenderer;
        private MaterialPropertyBlock properties;
        private float elapsed = Duration;

        private void Awake()
        {
            mesh = new Mesh { name = "Splash water sheet (pooled)" };
            mesh.MarkDynamic();
            var uv = new Vector2[vertices.Length];
            var triangles = new int[Segments * 6 * (CrownRows + 1)];
            for (int i = 0; i <= Segments; i++)
            {
                float angle = i * Mathf.PI * 2 / Segments;
                directions[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                for (int band = 0; band < Bands; band++)
                {
                    int rows = band == 0 ? CrownRows : 2;
                    int offset = band == 0 ? 0 : (CrownRows + (band - 1) * 2) * (Segments + 1);
                    for (int row = 0; row < rows; row++)
                    {
                        int v = offset + i * rows + row;
                        uv[v] = new Vector2((float)i / Segments, (float)row / (rows - 1));
                        if (i == Segments || row == rows - 1) continue;
                        int strip = band == 0 ? row : CrownRows + band - 2;
                        int t = (strip * Segments + i) * 6;
                        triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + rows;
                        triangles[t + 3] = v + rows; triangles[t + 4] = v + 1; triangles[t + 5] = v + rows + 1;
                    }
                }
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.up * .7f, new Vector3(7, 2, 7));
            surface.sharedMesh = mesh;
            sprayRenderer = droplets.GetComponent<ParticleSystemRenderer>();
            properties = new MaterialPropertyBlock();
            surfaceRenderer.enabled = false;
            enabled = false;
        }

        public void PlayAt(Vector3 point)
        {
            transform.position = point + Vector3.up * SurfaceLift;
            elapsed = 0;
            properties.SetFloat(WaterLevelId, point.y);
            sprayRenderer.SetPropertyBlock(properties);
            droplets.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            droplets.Play(true);
            surfaceRenderer.enabled = true;
            enabled = true;
            DrawSurface();
        }

        public void Clear()
        {
            droplets.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            surfaceRenderer.enabled = false;
            enabled = false;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed >= Duration) { Clear(); return; }
            DrawSurface();
        }

        private void DrawSurface()
        {
            float crown = Mathf.Clamp01(elapsed / CrownDuration);
            float height = Mathf.Pow(Mathf.Max(0, Mathf.Sin(crown * Mathf.PI)), .7f) * .85f;
            float radius = .30f + crown * .48f;
            float alpha = (1 - crown) * .7f;
            for (int i = 0; i <= Segments; i++)
            {
                Vector2 d = directions[i];
                float angle = i * Mathf.PI * 2 / Segments;
                float lobe = Mathf.Pow(.5f + .5f * Mathf.Sin(angle * 7 + .6f * Mathf.Sin(angle * 3)), 1.8f);
                float lip = radius + .14f + crown * .4f + lobe * .14f;
                for (int row = 0; row < CrownRows; row++)
                {
                    float t = (float)row / (CrownRows - 1);
                    float r = Mathf.Lerp(radius, lip, t * t);
                    int v = i * CrownRows + row;
                    vertices[v] = new Vector3(d.x * r, height * (.3f + .7f * lobe) * t, d.y * r);
                    colors[v] = Color.Lerp(new Color(.14f, .68f, .73f, alpha * .4f), new Color(.64f, .93f, .97f, alpha), t);
                }
            }
            DrawRing(1, elapsed, 0, 2.05f, .44f);
            DrawRing(2, elapsed, .16f, 1.65f, .28f);
            mesh.vertices = vertices;
            mesh.colors = colors;
        }

        private void DrawRing(int band, float time, float delay, float speed, float opacity)
        {
            float age = Mathf.Max(0, time - delay);
            float fade = Mathf.Clamp01(age / .09f) * Mathf.Pow(Mathf.Clamp01(1 - age / (Duration - delay)), 1.5f);
            float radius = .38f + age * speed;
            float width = .065f + .08f * age;
            for (int i = 0; i <= Segments; i++)
            {
                Vector2 d = directions[i];
                float angle = i * Mathf.PI * 2 / Segments;
                float brokenFoam = .65f + .35f * Mathf.Sin(angle * 13 + band);
                float r = radius + .025f * Mathf.Sin(angle * 7 + band);
                int v = (CrownRows + (band - 1) * 2) * (Segments + 1) + i * 2;
                vertices[v] = new Vector3(d.x * (r - width), band * .006f, d.y * (r - width));
                vertices[v + 1] = new Vector3(d.x * (r + width), band * .006f, d.y * (r + width));
                colors[v] = colors[v + 1] = new Color(.69f, .95f, .95f, fade * opacity * brokenFoam);
            }
        }

        private void OnDisable()
        {
            if (surfaceRenderer != null) surfaceRenderer.enabled = false;
            if (droplets != null) droplets.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
