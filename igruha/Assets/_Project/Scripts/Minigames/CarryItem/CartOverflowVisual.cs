using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>A sheet of water attached to the wet rim, followed by falling droplets and a floor splash.</summary>
    [RequireComponent(typeof(WaterCart)), DefaultExecutionOrder(30)]
    public sealed class CartOverflowVisual : MonoBehaviour
    {
        private const int Segments = 18;
        private WaterCart cart;
        private HorizontalCartWater water;
        private Mesh mesh;
        private MeshRenderer sheet;
        private Material material;
        private ParticleSystem splash;
        private readonly Vector3[] vertices = new Vector3[(Segments + 1) * 2];
        private readonly Color[] colors = new Color[(Segments + 1) * 2];
        private int floorMask;
        private Vector3 source, outward, tangent;
        private float width, landingTime;

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
            var triangles = new int[Segments * 6];
            for (int i = 0; i < Segments; i++)
            { int a = i * 2, t = i * 6; triangles[t] = a; triangles[t+1] = a+2; triangles[t+2] = a+1;
              triangles[t+3] = a+1; triangles[t+4] = a+2; triangles[t+5] = a+3; }
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
            var emission = splash.emission; emission.rateOverTime = 55f;
            var template = GetComponentInChildren<ParticleSystemRenderer>();
            if (template != null) splash.GetComponent<ParticleSystemRenderer>().sharedMaterial = template.sharedMaterial;
            splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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
        }
        private void LateUpdate()
        {
            if (sheet == null) return;
            bool active = cart.Stability.IsSpilling;
            sheet.enabled = active;
            if (!active) { if (splash.isEmitting) splash.Stop(true, ParticleSystemStopBehavior.StopEmitting); return; }
            UpdateSource();
            float speed = Mathf.Lerp(0.18f, 0.62f, cart.Stability.State.Outflow / 35f);
            for (int i = 0; i <= Segments; i++)
            {
                float f = Mathf.Max(0f, (i - 1f) / (Segments - 1)), t = f * landingTime;
                // First span crosses the rolled metal lip; falling starts at its outside edge.
                float lip = i == 0 ? 0f : CartWaterSurface.LipWidth;
                Vector3 point = source + outward * (lip + speed * t) + Vector3.down * (4.905f * t * t);
                float spread = width * Mathf.Lerp(1f, 0.65f, f);
                float flutter = Mathf.Sin(Time.time * 17f - f * 15f) * 0.009f * f;
                vertices[i*2] = sheet.transform.InverseTransformPoint(point - tangent * spread * 0.5f + outward * flutter);
                vertices[i*2+1] = sheet.transform.InverseTransformPoint(point + tangent * spread * 0.5f + outward * flutter);
                colors[i*2] = colors[i*2+1] = new Color(1f, 1f, 1f, Mathf.Lerp(0.9f, 0.15f, f * f));
            }
            mesh.vertices = vertices; mesh.colors = colors; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            splash.transform.SetPositionAndRotation(source + outward * (CartWaterSurface.LipWidth + speed * landingTime) + Vector3.down * 4.905f * landingTime * landingTime,
                Quaternion.LookRotation(Vector3.up));
            var emission = splash.emission; emission.rateOverTime = 12f + cart.Stability.State.Outflow * 3f;
            if (!splash.isEmitting) splash.Play();
        }
        private void OnDestroy() { if (mesh != null) Destroy(mesh); if (material != null) Destroy(material); }
    }
}
