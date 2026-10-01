using Igruha.Core.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Vector HUD: stays sharp at any resolution, without imported sprites or per-frame objects.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CartCoordinationGraphic : MaskableGraphic
    {
        public static readonly Color Local = new Color(0.42f, 0.83f, 1f);
        private static readonly Color Together = new Color(0.4f, 0.93f, 0.75f);
        private static readonly Color Diverging = new Color(1f, 0.76f, 0.35f);
        private static readonly Color Opposing = new Color(1f, 0.43f, 0.38f);
        private static readonly Color Neutral = new Color(0.48f, 0.61f, 0.63f);
        private static readonly Color Card = new Color(0.075f, 0.14f, 0.17f, 0.98f);
        private static readonly Color Line = new Color(0.35f, 0.54f, 0.56f, 0.25f);
        private static readonly Color Key = new Color(0.13f, 0.22f, 0.26f);
        private const int CircleSegments = 48;
        private CartCoordinationHud hud;
        public void Bind(CartCoordinationHud view) => hud = view;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if (hud == null || hud.MemberCount == 0) return;
            float width = hud.PanelWidth;
            Box(vh, new Vector2(0, -4), new Vector2(width + 2, 176), new Color(0, 0, 0, 0.25f), 12);
            Box(vh, Vector2.zero, new Vector2(width, 172), MinigameUiStyle.HudSurface, 12);
            for (int i = 0; i < hud.MemberCount; i++)
            {
                var m = hud.MemberAt(i);
                var origin = new Vector2(hud.MemberX(i), 0);
                Color accent = ColorOf(m.Relation);
                Box(vh, origin, new Vector2(124, 156), Card, 8);
                Box(vh, origin + new Vector2(0, 78), new Vector2(108, 2), m.IsLocal ? Local : Line, 0);
                var centre = origin + new Vector2(0, 15);
                Ring(vh, centre, 28, 1, Line);
                if (m.Relation == CartInputRelation.Idle) Pause(vh, centre, Neutral);
                else Arrow(vh, centre, m.Direction, 36, 6, accent);
                DrawKey(vh, origin + new Vector2(0, -30), CartCoordinationHud.KeyActive(m.Input.Move, 0), accent);
                DrawKey(vh, origin + new Vector2(-25, -56), CartCoordinationHud.KeyActive(m.Input.Move, 1), accent);
                DrawKey(vh, origin + new Vector2(0, -56), CartCoordinationHud.KeyActive(m.Input.Move, 2), accent);
                DrawKey(vh, origin + new Vector2(25, -56), CartCoordinationHud.KeyActive(m.Input.Move, 3), accent);
            }
            DrawResult(vh);
        }

        private void DrawResult(VertexHelper vh)
        {
            var centre = new Vector2(hud.ResultX, 7);
            Stroke(vh, new Vector2(hud.ResultX - 83, -64), new Vector2(hud.ResultX - 83, 65), 1, Line);
            Ring(vh, centre, 39, 1.5f, Line);
            Vector2 baseline = hud.MeanDirection;
            if (baseline.sqrMagnitude < 0.01f)
                for (int i = 0; i < hud.MemberCount; i++)
                    if (hud.MemberAt(i).Direction.sqrMagnitude > 0.01f) { baseline = hud.MemberAt(i).Direction; break; }
            bool conflict = false;
            for (int i = 0; i < hud.MemberCount; i++)
            {
                var m = hud.MemberAt(i);
                if (m.Relation == CartInputRelation.Idle) continue;
                Color accent = ColorOf(m.Relation);
                Vector2 direction = m.Direction.normalized;
                Stroke(vh, centre + direction * 28, centre + direction * 45, 3, accent);
                if (m.Relation == CartInputRelation.Opposing || m.Relation == CartInputRelation.Diverging)
                {
                    conflict = true;
                    Arc(vh, centre, 43 + i * 2, baseline, direction, accent);
                }
            }
            // Faint physical cart heading behind the bright command arrow, showing steering lag.
            Vector2 forward = hud.CartDirection.normalized;
            Vector2 right = new Vector2(forward.y, -forward.x);
            for (int side = -1; side <= 1; side += 2)
            {
                Stroke(vh, centre + right * side * 9 - forward * 12, centre + right * side * 9 + forward * 12, 2, Line);
                for (int end = -1; end <= 1; end += 2)
                    Stroke(vh, centre + right * side * 14 + forward * (end * 9 - 3),
                        centre + right * side * 14 + forward * (end * 9 + 3), 3, Line);
            }
            Stroke(vh, centre - right * 9 + forward * 12, centre + right * 9 + forward * 12, 2, Line);
            Stroke(vh, centre - right * 9 - forward * 12, centre + right * 9 - forward * 12, 2, Line);
            float power = Mathf.Clamp01(hud.MeanDirection.magnitude);
            if (power < CartCoordinationMath.DeadZone) Pause(vh, centre, conflict ? Opposing : Neutral);
            else Arrow(vh, centre, hud.MeanDirection, 39, 7, MinigameUiStyle.OnDark);
            // Segments reflect the actual mean magnitude: a passenger or contrary input removes segments.
            for (int i = 0; i < 10; i++)
                Box(vh, new Vector2(hud.ResultX - 49.5f + i * 11, -43), new Vector2(8, 4),
                    power > (i + 0.5f) / 10 ? (conflict ? Diverging : Together) : Line, 1);
        }

        private static Color ColorOf(CartInputRelation relation) => relation switch
        {
            CartInputRelation.Together => Together,
            CartInputRelation.Diverging => Diverging,
            CartInputRelation.Opposing => Opposing,
            _ => Neutral
        };

        private static void DrawKey(VertexHelper vh, Vector2 centre, bool pressed, Color accent)
        {
            Box(vh, centre + Vector2.down * 2, new Vector2(23, 23), new Color(0, 0, 0, 0.3f), 3);
            Box(vh, centre, new Vector2(23, 23), pressed ? accent : Key, 3);
        }

        private static void Pause(VertexHelper vh, Vector2 centre, Color color)
        {
            Box(vh, centre + Vector2.left * 5, new Vector2(4, 14), color, 1);
            Box(vh, centre + Vector2.right * 5, new Vector2(4, 14), color, 1);
        }

        private static void Arrow(VertexHelper vh, Vector2 centre, Vector2 direction, float length, float thickness, Color color)
        {
            Vector2 d = direction.normalized, r = new Vector2(d.y, -d.x);
            Vector2 tip = centre + d * length * 0.65f;
            Stroke(vh, centre - d * length * 0.45f, tip - d * 8, thickness, color);
            Triangle(vh, tip, tip - d * 12 + r * (thickness + 2), tip - d * 12 - r * (thickness + 2), color);
        }

        private static void Ring(VertexHelper vh, Vector2 centre, float radius, float thickness, Color color)
        {
            Vector2 previous = centre + Vector2.right * radius;
            for (int i = 1; i <= CircleSegments; i++)
            {
                float angle = i * Mathf.PI * 2 / CircleSegments;
                Vector2 next = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Stroke(vh, previous, next, thickness, color); previous = next;
            }
        }

        private static void Arc(VertexHelper vh, Vector2 centre, float radius, Vector2 from, Vector2 to, Color color)
        {
            float sweep = Vector2.SignedAngle(from, to) * Mathf.Deg2Rad;
            float start = Mathf.Atan2(from.y, from.x);
            int segments = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(sweep) * 12));
            Vector2 previous = centre + from.normalized * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = start + sweep * i / segments;
                Vector2 next = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Stroke(vh, previous, next, 2, color); previous = next;
            }
        }

        private static void Box(VertexHelper vh, Vector2 centre, Vector2 size, Color color, float corner)
        {
            Vector2 h = size * 0.5f;
            int first = vh.currentVertCount;
            vh.AddVert(centre, color, Vector2.zero);
            vh.AddVert(centre + new Vector2(-h.x + corner, -h.y), color, Vector2.zero);
            vh.AddVert(centre + new Vector2(h.x - corner, -h.y), color, Vector2.zero);
            vh.AddVert(centre + new Vector2(h.x, -h.y + corner), color, Vector2.zero);
            vh.AddVert(centre + new Vector2(h.x, h.y - corner), color, Vector2.zero);
            vh.AddVert(centre + new Vector2(h.x - corner, h.y), color, Vector2.zero);
            vh.AddVert(centre + new Vector2(-h.x + corner, h.y), color, Vector2.zero);
            vh.AddVert(centre + new Vector2(-h.x, h.y - corner), color, Vector2.zero);
            vh.AddVert(centre + new Vector2(-h.x, -h.y + corner), color, Vector2.zero);
            for (int i = 0; i < 8; i++) vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % 8);
        }

        private static void Stroke(VertexHelper vh, Vector2 a, Vector2 b, float thickness, Color color)
        {
            Vector2 delta = (b - a).normalized;
            Vector2 normal = new Vector2(-delta.y, delta.x) * thickness * 0.5f;
            int first = vh.currentVertCount;
            vh.AddVert(a - normal, color, Vector2.zero); vh.AddVert(a + normal, color, Vector2.zero);
            vh.AddVert(b + normal, color, Vector2.zero); vh.AddVert(b - normal, color, Vector2.zero);
            vh.AddTriangle(first, first + 1, first + 2); vh.AddTriangle(first, first + 2, first + 3);
        }

        private static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            int first = vh.currentVertCount;
            vh.AddVert(a, color, Vector2.zero); vh.AddVert(b, color, Vector2.zero); vh.AddVert(c, color, Vector2.zero);
            vh.AddTriangle(first, first + 1, first + 2);
        }
    }
}
