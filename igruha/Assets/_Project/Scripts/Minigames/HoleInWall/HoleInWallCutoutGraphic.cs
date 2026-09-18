using System.Collections.Generic;
using Igruha.Core.Session;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>Briefing opening, from exactly the same per-character contours as the wall.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HoleInWallCutoutGraphic : MaskableGraphic
    {
        [SerializeField] private HoleInWallConfig config;
        [SerializeField] private HoleInWallPose pose;
        private GameObject avatar;
        private CutoutShapes shapes;
        private readonly List<Vector2> polygon = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<int> working = new List<int>();

        protected override void OnEnable()
        {
            base.OnEnable();
            shapes = null;
            RefreshShape();
        }

        private void LateUpdate() => RefreshShape();

        private void RefreshShape()
        {
            var player = SessionScoreboard.Current?.LocalPlayer?.Avatar;
            GameObject current = player != null ? player.gameObject : null;
            if (shapes != null && current == avatar) return;
            avatar = current;
            shapes = config != null ? new CutoutShapes(config,
                current != null ? CutoutShapes.KeyOf(current) : "PlayerAnimator") : null;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (shapes == null) RefreshShape();
            var outline = shapes?.Outline(pose);
            if (outline == null || outline.Length < 3) return;
            polygon.Clear();
            polygon.AddRange(outline);
            Vector2 min = polygon[0], max = min;
            float area = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y;
                min = Vector2.Min(min, a); max = Vector2.Max(max, a);
            }
            if (area < 0) polygon.Reverse();
            if (!PolygonTriangulator.Triangulate(polygon, triangles, working)) return;
            Rect rect = GetPixelAdjustedRect();
            float scale = .84f * Mathf.Min(rect.width / Mathf.Max(.01f, max.x - min.x),
                rect.height / Mathf.Max(.01f, max.y - min.y));
            foreach (Vector2 point in polygon)
                mesh.AddVert(new Vector3(rect.center.x + (point.x - (min.x + max.x) * .5f) * scale,
                    rect.yMin + rect.height * .04f + (point.y - min.y) * scale), color, Vector2.zero);
            for (int i = 0; i < triangles.Count; i += 3)
                mesh.AddTriangle(triangles[i + 2], triangles[i + 1], triangles[i]);
        }
    }
}
