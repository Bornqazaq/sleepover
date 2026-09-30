using UnityEngine;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Local hose and motor animation driven by the replicated pouring flag.</summary>
    [RequireComponent(typeof(WaterTank))]
    public sealed class WaterPumpPresentation : MonoBehaviour
    {
        [SerializeField] private Transform intake;
        [SerializeField] private Transform parkedNozzle;
        [SerializeField] private Transform nozzle;
        [SerializeField] private Transform rotor;
        [SerializeField] private MeshFilter hose;
        private const int Rings = 33;
        private const int Sides = 10;
        private const float DeploySpeed = 3f;
        private const float NozzleSpeed = 6f;
        private const float NozzleWaterOffset = 0.035f;
        private const float RotorDegreesPerSecond = 900f;
        private const float HoseRadius = 0.042f;
        private const float RibRadius = 0.046f;
        private const float ParkedArcHeight = 0.45f;
        private const float DeployedArcHeight = 0.8f;
        private static readonly Vector3 IntakeTangent = new Vector3(0f, 0.6f, -0.35f);
        private WaterTank tank;
        private WaterCart cart;
        private Mesh mesh;
        private Vector3[] vertices;
        private Vector3[] normals;
        private Vector3 end;
        private float deployment;

        public bool IsPumping => cart != null && cart.IsPouring;

        private void Awake()
        {
            tank = GetComponent<WaterTank>();
            end = parkedNozzle.position;
            mesh = new Mesh { name = "Pump suction hose" };
            mesh.MarkDynamic();
            vertices = new Vector3[Rings * Sides];
            normals = new Vector3[vertices.Length];
            var triangles = new int[(Rings - 1) * Sides * 6];
            int at = 0;
            for (int r = 0; r < Rings - 1; r++)
                for (int s = 0; s < Sides; s++)
                {
                    int a = r * Sides + s, b = r * Sides + (s + 1) % Sides;
                    triangles[at++] = a; triangles[at++] = a + Sides; triangles[at++] = b;
                    triangles[at++] = b; triangles[at++] = a + Sides; triangles[at++] = b + Sides;
                }
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            hose.sharedMesh = mesh;
            DrawHose();
        }

        private void LateUpdate()
        {
            if (cart == null && MinigameControllerBase.Current is CarryItemMinigame game)
                cart = game.CartOf(tank.Team);
            bool active = IsPumping;
            deployment = Mathf.MoveTowards(deployment, active ? 1f : 0f, Time.deltaTime * DeploySpeed);
            Vector3 target = active ? cart.WaterSurfacePoint + Vector3.up * NozzleWaterOffset : parkedNozzle.position;
            end = Vector3.MoveTowards(end, target, Time.deltaTime * NozzleSpeed);
            nozzle.position = end;
            if (active) rotor.Rotate(0f, 0f, RotorDegreesPerSecond * Time.deltaTime, Space.Self);
            DrawHose();
        }

        private void DrawHose()
        {
            Vector3 a = intake.position;
            Vector3 b = a + transform.TransformDirection(IntakeTangent);
            Vector3 c = end + Vector3.up * Mathf.Lerp(ParkedArcHeight, DeployedArcHeight, deployment);
            for (int r = 0; r < Rings; r++)
            {
                float t = r / (float)(Rings - 1), u = 1f - t;
                Vector3 point = u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * end;
                Vector3 tangent = (3f * u * u * (b - a) + 6f * u * t * (c - b) + 3f * t * t * (end - c)).normalized;
                Vector3 side = Vector3.Cross(tangent, transform.right).normalized;
                Vector3 up = Vector3.Cross(tangent, side).normalized;
                float radius = r % 2 == 0 ? RibRadius : HoseRadius;
                for (int s = 0; s < Sides; s++)
                {
                    float angle = 2f * Mathf.PI * s / Sides;
                    Vector3 normal = side * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                    int index = r * Sides + s;
                    vertices[index] = hose.transform.InverseTransformPoint(point + normal * radius);
                    normals[index] = hose.transform.InverseTransformDirection(normal);
                }
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
