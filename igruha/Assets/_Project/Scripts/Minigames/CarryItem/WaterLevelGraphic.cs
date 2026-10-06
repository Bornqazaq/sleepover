using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Resolution independent tank with a continuous liquid level and a gentle surface wave.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WaterLevelGraphic : MaskableGraphic
    {
        private const int WaveSegments = 24, CornerSegments = 6;
        private const float AnimationRate = 24f, WaveSpeed = 1.8f, SmoothSpeed = 9f;
        private float target, shown, nextFrame;
        private Color tint = new Color(.13f, .81f, 1f);
        public float Level => target;
        public Color Tint { set { if (tint == value) return; tint = value; SetVerticesDirty(); } }
        public void SetLevel(float value) { target = Mathf.Clamp01(value); }

        private void Update()
        {
            if (Time.unscaledTime < nextFrame) return;
            nextFrame = Time.unscaledTime + 1f / AnimationRate;
            shown = Mathf.Lerp(shown, target, 1f - Mathf.Exp(-SmoothSpeed / AnimationRate));
            if (Mathf.Abs(shown - target) < .001f) shown = target;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            // Coordinates are expressed in an 82 x 132 unit vessel, rooted at its bottom left.
            Vector2 origin = rectTransform.rect.min;
            Rounded(mesh, origin + new Vector2(9, 17), new Vector2(64, 101), 12, new Color(.55f, .76f, .83f));
            Rounded(mesh, origin + new Vector2(12, 20), new Vector2(58, 95), 9, new Color(.06f, .15f, .21f));
            float level = 24 + shown * 86;
            float wave = Mathf.Min(1.8f, Mathf.Min(shown * 15, (1 - shown) * 15));
            if (shown > 0)
            {
                for (int i = 0; i < WaveSegments; i++)
                {
                    float x0 = 16 + 50f * i / WaveSegments, x1 = 16 + 50f * (i + 1) / WaveSegments;
                    float y0 = level + Mathf.Sin(i * .32f + Time.unscaledTime * WaveSpeed) * wave;
                    float y1 = level + Mathf.Sin((i + 1) * .32f + Time.unscaledTime * WaveSpeed) * wave;
                    int n = mesh.currentVertCount;
                    mesh.AddVert(origin + new Vector2(x0, 24), tint * new Color(.35f, .65f, .85f, 1), Vector2.zero);
                    mesh.AddVert(origin + new Vector2(x0, y0), tint, Vector2.zero);
                    mesh.AddVert(origin + new Vector2(x1, y1), tint, Vector2.zero);
                    mesh.AddVert(origin + new Vector2(x1, 24), tint * new Color(.35f, .65f, .85f, 1), Vector2.zero);
                    mesh.AddTriangle(n, n + 1, n + 2); mesh.AddTriangle(n, n + 2, n + 3);
                    if (shown < .995f) Rounded(mesh, origin + new Vector2(x0, y0 - 1), new Vector2(x1 - x0 + .1f, 1.4f), 0, Color.Lerp(tint, Color.white, .65f));
                }
            }
            for (int i = 1; i < 5; i++) Rounded(mesh, origin + new Vector2(17, 24 + i * 17.2f), new Vector2(i % 2 == 0 ? 11 : 7, 1.3f), 0, new Color(.77f, .91f, .96f, .75f));
            Rounded(mesh, origin + new Vector2(58, 35), new Vector2(3, 63), 1.5f, new Color(1, 1, 1, .18f));
            Rounded(mesh, origin + new Vector2(29, 119), new Vector2(24, 7), 3, new Color(.55f, .76f, .83f));
            Rounded(mesh, origin + new Vector2(7, 12), new Vector2(68, 4), 2, new Color(.55f, .76f, .83f));
            Rounded(mesh, origin + new Vector2(17, 2), new Vector2(11, 11), 5.5f, new Color(.55f, .76f, .83f));
            Rounded(mesh, origin + new Vector2(54, 2), new Vector2(11, 11), 5.5f, new Color(.55f, .76f, .83f));
        }

        private static void Rounded(VertexHelper mesh, Vector2 min, Vector2 size, float radius, Color color)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(min + size * .5f, color, Vector2.zero);
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 center = min + new Vector2(corner < 2 ? size.x - radius : radius,
                    corner == 0 || corner == 3 ? size.y - radius : radius);
                for (int step = 0; step <= CornerSegments; step++)
                {
                    float angle = (90 - corner * 90 - step * 90f / CornerSegments) * Mathf.Deg2Rad;
                    mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
                }
            }
            const int count = 4 * (CornerSegments + 1);
            for (int i = 0; i < count; i++) mesh.AddTriangle(first, first + i + 1, first + (i + 1) % count + 1);
        }
    }
}
