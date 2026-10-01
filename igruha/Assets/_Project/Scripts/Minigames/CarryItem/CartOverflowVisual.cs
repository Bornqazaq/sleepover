using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>A sheet of water attached to the wet rim, followed by falling droplets and a floor splash.</summary>
    [RequireComponent(typeof(WaterCart)), DefaultExecutionOrder(30)]
    public sealed class CartOverflowVisual : MonoBehaviour
    {
        private const int Slices = 48;
        private const float SampleInterval = 0.016f;
        private const float Gravity = 4.905f;
        private struct Slice
        {
            public Vector3 Source, Tangent, Velocity;
            public float Born, Lifetime, Width, FloorY;
            public bool Connected, Landed;
        }
        private WaterCart cart;
        private HorizontalCartWater water;
        private Mesh mesh;
        private MeshRenderer sheet;
        private Material material;
        private ParticleSystem splash;
        private readonly Slice[] slices = new Slice[Slices];
        private readonly Vector3[] vertices = new Vector3[Slices * 2];
        private readonly Color[] colors = new Color[Slices * 2];
        private readonly int[] triangles = new int[(Slices - 1) * 6];
        private int floorMask;
        private Vector3 source, outward, tangent;
        private float width, landingTime, floorHeight;
        private int head, count;
        private byte lastSide;
        private float emittedAt = float.NegativeInfinity;
        private Vector3 lastSource;
        private bool wasEmitting;
        public bool HasFallingWater => sheet != null && sheet.enabled;

        private void Awake()
        {
            cart = GetComponent<WaterCart>(); water = GetComponentInChildren<HorizontalCartWater>();
            if (water == null) return;
            var surface = water.GetComponentInChildren<Renderer>();
            material = new Material(surface.sharedMaterial) { name = "Overflow water sheet" };
            material.SetFloat("_Flow", 1f); material.SetFloat("_Opacity", 0.64f); material.SetFloat("_Cull", 0f);
            var go = new GameObject("OverflowSheet", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            sheet = go.GetComponent<MeshRenderer>(); sheet.sharedMaterial = material; sheet.enabled = false;
            mesh = new Mesh { name = "Falling water sheet" }; mesh.MarkDynamic();
            mesh.vertices = vertices; mesh.triangles = triangles; go.GetComponent<MeshFilter>().sharedMesh = mesh;
            floorMask = LayerMask.GetMask("Ground", "Cover");
            var splashObject = new GameObject("OverflowFloorSplash"); splashObject.transform.SetParent(transform, false);
            splash = splashObject.AddComponent<ParticleSystem>();
            var main = splash.main; main.playOnAwake = false; main.startLifetime = 0.35f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.045f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.9f); main.gravityModifier = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 150;
            main.startColor = new Color(0.72f, 0.87f, 0.88f, 0.65f);
            var shape = splash.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 70f; shape.radius = 0.08f;
            var emission = splash.emission; emission.rateOverTime = 0f;
            var template = GetComponentInChildren<ParticleSystemRenderer>();
            if (template != null) splash.GetComponent<ParticleSystemRenderer>().sharedMaterial = template.sharedMaterial;
            splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            splash.Play();
        }
        public void PositionDroplets(ParticleSystem droplets)
        {
            if (water == null) return;
            UpdateSource();
            droplets.transform.SetPositionAndRotation(source + outward * CartWaterSurface.LipWidth,
                Quaternion.LookRotation(outward + Vector3.down * 0.35f));
            var emission = droplets.emission; emission.rateOverTime = Mathf.Lerp(15f, 85f, cart.Stability.State.Outflow / 35f);
            var shape = droplets.shape; shape.scale = new Vector3(width, 0.02f, 0.01f);
        }
        private void UpdateSource()
        {
            var state = cart.Stability.State;
            source = water.transform.TransformPoint(CartWaterSurface.RimPoint(state.SpillSide, state.SpillAlong));
            outward = water.transform.TransformDirection(CartWaterSurface.Outward(state.SpillSide));
            tangent = Vector3.Cross(Vector3.up, outward).normalized;
            width = Mathf.Clamp(state.SpillWidth, 0.035f, state.SpillSide < 2 ? CartWaterSurface.Length : CartWaterSurface.Width);
            float floorY = source.y - 3f;
            if (Physics.Raycast(source + outward * 0.28f, Vector3.down, out RaycastHit hit, 6f, floorMask, QueryTriggerInteraction.Ignore))
                floorY = hit.point.y + 0.015f;
            landingTime = Mathf.Sqrt(Mathf.Max(0.02f, source.y - floorY) / 4.905f);
            floorHeight = floorY;
        }
        private void LateUpdate()
        {
            if (sheet == null) return;
            bool active = cart.Stability.IsSpilling;
            float now = Time.time;
            if (active && now - emittedAt >= SampleInterval)
            {
                UpdateSource();
                float speed = Mathf.Lerp(0.18f, 0.62f, cart.Stability.State.Outflow / 35f);
                // Emitted water inherits cart speed, then falls independently of the next swing.
                Vector3 velocity = outward * speed + cart.Carry.FlatVelocity;
                slices[head] = new Slice
                {
                    Source = source, Tangent = tangent, Velocity = velocity,
                    Born = now, Lifetime = Mathf.Min(landingTime, SampleInterval * (Slices - 2)), Width = width,
                    FloorY = floorHeight,
                    Connected = wasEmitting && cart.Stability.State.SpillSide == lastSide &&
                        now - emittedAt < 0.1f && (source - lastSource).sqrMagnitude < 0.36f
                };
                head = (head + 1) % Slices; count = Mathf.Min(count + 1, Slices);
                lastSource = source; lastSide = cart.Stability.State.SpillSide; emittedAt = now;
            }
            wasEmitting = active;
            BuildFallingSheet(now);
        }

        private void BuildFallingSheet(float now)
        {
            System.Array.Clear(triangles, 0, triangles.Length);
            int valid = 0, previous = -1, triangle = 0;
            for (int i = 0; i < count; i++)
            {
                int slot = (head - count + i + Slices) % Slices;
                Slice slice = slices[slot];
                float age = now - slice.Born;
                if (age > slice.Lifetime)
                {
                    if (!slice.Landed)
                    {
                        Vector3 landing = slice.Source + slice.Velocity * slice.Lifetime;
                        landing.y = slice.FloorY;
                        splash.transform.SetPositionAndRotation(landing, Quaternion.LookRotation(Vector3.up));
                        var shape = splash.shape; shape.radius = slice.Width * 0.35f;
                        splash.Emit(3);
                        slice.Landed = true; slices[slot] = slice;
                    }
                    previous = -1; continue;
                }
                float f = age / Mathf.Max(0.01f, slice.Lifetime);
                Vector3 lip = Vector3.Cross(slice.Tangent, Vector3.up) * CartWaterSurface.LipWidth;
                // Cross the rounded lip during the first few centimetres; the tail is ballistic.
                Vector3 point = slice.Source + lip * Mathf.Clamp01(age / 0.045f) +
                    slice.Velocity * age + Vector3.down * Gravity * age * age;
                float spread = slice.Width * Mathf.Lerp(1f, 0.65f, f);
                int v = valid++ * 2;
                vertices[v] = sheet.transform.InverseTransformPoint(point - slice.Tangent * spread * 0.5f);
                vertices[v + 1] = sheet.transform.InverseTransformPoint(point + slice.Tangent * spread * 0.5f);
                colors[v] = colors[v + 1] = new Color(1f, 1f, 1f, Mathf.Lerp(0.9f, 0.2f, f * f));
                if (previous >= 0 && slice.Connected)
                {
                    triangles[triangle++] = previous; triangles[triangle++] = v; triangles[triangle++] = previous + 1;
                    triangles[triangle++] = previous + 1; triangles[triangle++] = v; triangles[triangle++] = v + 1;
                }
                previous = v;
            }
            sheet.enabled = triangle > 0;
            // Unused vertices must not leave enormous stale bounds after a respawn.
            for (int i = valid * 2; i < vertices.Length; i++) vertices[i] = Vector3.zero;
            mesh.vertices = vertices; mesh.colors = colors; mesh.triangles = triangles;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
        }
        private void OnDestroy() { if (mesh != null) Destroy(mesh); if (material != null) Destroy(material); }
    }
}
