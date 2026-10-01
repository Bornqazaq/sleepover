using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Closed water volume. Its large wave is the same surface used by the server.</summary>
    [DefaultExecutionOrder(25)]
    public sealed class HorizontalCartWater : MonoBehaviour
    {
        private const int Columns = 16, Rows = 22;
        private const int Layer = (Columns + 1) * (Rows + 1);
        private Mesh mesh;
        private MeshFilter filter;
        private WaterCart cart;
        private WaterCartPresentation presentation;
        private readonly Vector3[] vertices = new Vector3[Layer * 2];
        private readonly Color[] colors = new Color[Layer * 2];
        private Vector3 suction;
        private float suctionStrength;
        private float displayedLevel;
        public Vector3 SurfacePoint { get; private set; }
        public WaterCart Cart => cart;

        private void Awake()
        {
            filter = GetComponentInChildren<MeshFilter>();
            cart = GetComponentInParent<WaterCart>();
            presentation = GetComponentInParent<WaterCartPresentation>();
            mesh = new Mesh { name = "Cart water: rolling wave and closed volume" };
            mesh.MarkDynamic();
            var triangles = new int[Columns * Rows * 12 + (Columns + Rows) * 12];
            int at = 0;
            for (int z = 0; z < Rows; z++) for (int x = 0; x < Columns; x++)
            {
                int a = z * (Columns + 1) + x, b = a + Columns + 1;
                Quad(triangles, ref at, a, b, b + 1, a + 1);
                Quad(triangles, ref at, a + Layer, a + 1 + Layer, b + 1 + Layer, b + Layer);
            }
            for (int x = 0; x < Columns; x++)
            {
                Quad(triangles, ref at, x, x + 1, x + 1 + Layer, x + Layer);
                int a = Rows * (Columns + 1) + x;
                Quad(triangles, ref at, a + 1, a, a + Layer, a + 1 + Layer);
            }
            for (int z = 0; z < Rows; z++)
            {
                int a = z * (Columns + 1), b = a + Columns + 1;
                Quad(triangles, ref at, b, a, a + Layer, b + Layer);
                Quad(triangles, ref at, a + Columns, b + Columns, b + Columns + Layer, a + Columns + Layer);
            }
            mesh.vertices = vertices; mesh.triangles = triangles;
            if (filter != null)
            {
                filter.sharedMesh = mesh;
                var block = new MaterialPropertyBlock(); block.SetFloat("_WaveMesh", 1f);
                filter.GetComponent<Renderer>().SetPropertyBlock(block);
            }
        }
        private static void Quad(int[] indices, ref int at, int a, int b, int c, int d)
        { indices[at++] = a; indices[at++] = b; indices[at++] = c; indices[at++] = a; indices[at++] = c; indices[at++] = d; }

        public void SetSuction(Vector3 point, float strength) { suction = point; suctionStrength = strength; }

        public void SetLevel(float level)
        {
            displayedLevel = level;
            if (filter == null) return;
            Vector2 wave = cart != null ? cart.Stability.State.Wave : Vector2.zero;
            Quaternion heading = presentation != null ? presentation.Heading : cart != null ? cart.transform.rotation : Quaternion.identity;
            Vector2 slope = CartWaterSurface.InHeading(wave, heading);
            Vector2 bodySlope = cart != null ? CartWaterSurface.InHeading(cart.Stability.State.BodySlope, heading) : Vector2.zero;
            Vector3 inlet = transform.InverseTransformPoint(suction);
            SurfacePoint = transform.TransformPoint(Vector3.up * CartWaterSurface.Height(level, slope, 0f, 0f));
            for (int z = 0; z <= Rows; z++) for (int x = 0; x <= Columns; x++)
            {
                float px = (x / (float)Columns - 0.5f) * CartWaterSurface.Width;
                float pz = (z / (float)Rows - 0.5f) * CartWaterSurface.Length;
                float height = CartWaterSurface.Height(level, slope, px, pz, bodySlope);
                float distance = (px - inlet.x) * (px - inlet.x) + (pz - inlet.z) * (pz - inlet.z);
                height -= suctionStrength * 0.035f * Mathf.Exp(-distance * 90f);
                int i = z * (Columns + 1) + x;
                float foam = Mathf.Clamp01((height - CartWaterSurface.Depth + 0.012f) / 0.025f);
                float edgeDistance = Mathf.Min(CartWaterSurface.Width * 0.5f - Mathf.Abs(px),
                    CartWaterSurface.Length * 0.5f - Mathf.Abs(pz));
                foam *= 1f - Mathf.Clamp01(edgeDistance / 0.045f);
                // R: wet-rim foam. G: depth below surface. B: full column depth.
                // The shader absorbs more light through a deep column, without a camera depth texture.
                float depth = Mathf.Clamp01(height / CartWaterSurface.FullHeight);
                colors[i] = new Color(foam, 0f, depth, 1f);
                colors[i + Layer] = new Color(0f, depth, depth, 1f);
                vertices[i] = filter.transform.InverseTransformPoint(transform.TransformPoint(
                    new Vector3(px, Mathf.Clamp(height, 0.001f, CartWaterSurface.Depth), pz)));
                vertices[i + Layer] = filter.transform.InverseTransformPoint(transform.TransformPoint(new Vector3(px, 0f, pz)));
            }
            mesh.vertices = vertices; mesh.colors = colors; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        }
        // Waves and the pump inlet move even while the integer water count is unchanged.
        private void LateUpdate() => SetLevel(cart != null ? cart.Load : displayedLevel);
        private void OnDestroy() { if (mesh != null) Destroy(mesh); }
    }
}
