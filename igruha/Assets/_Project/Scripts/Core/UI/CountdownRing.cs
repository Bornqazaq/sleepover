using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Core.UI
{
    /// <summary>Resolution-independent countdown arc, with no texture or runtime asset generation.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CountdownRing : MaskableGraphic
    {
        [SerializeField, Range(0, 1)] private float remaining = 1;
        [SerializeField] private float thickness = 5;

        public void SetProgress(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(remaining, value)) return;
            remaining = value;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            float outer = Mathf.Min(rect.width, rect.height) * .5f;
            float inner = Mathf.Max(0, outer - thickness);
            int steps = Mathf.CeilToInt(96 * remaining);
            if (steps == 0) return;
            for (int i = 0; i <= steps; i++)
            {
                float angle = Mathf.PI * .5f - Mathf.PI * 2 * remaining * i / steps;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vh.AddVert(rect.center + direction * outer, color, Vector2.zero);
                vh.AddVert(rect.center + direction * inner, color, Vector2.zero);
                if (i == 0) continue;
                int n = i * 2;
                vh.AddTriangle(n - 2, n, n - 1);
                vh.AddTriangle(n, n + 1, n - 1);
            }
        }
    }
}
