using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>The simulation and mesh use the same tub geometry and free surface.</summary>
    public static class CartWaterSurface
    {
        // Authored tank: floor at .485 m, rolled rim top at .9935 m.
        // The water pivot is at the floor; these dimensions include the actual lip height.
        public const float Width = 0.78f;
        public const float Length = 1.06f;
        public const float Depth = 0.51f;
        public const float FullHeight = 0.435f;
        public const float LipWidth = 0.05f;
        public const float MaxSlope = 0.85f;
        public const float MaxOverflowRate = 49f;
        private const int EdgeSamples = 17;
        public static Vector2 InHeading(Vector2 world, Quaternion heading)
        {
            Vector3 local = Quaternion.Inverse(heading) * new Vector3(world.x, 0f, world.y);
            return new Vector2(local.x, local.z);
        }

        public static Quaternion BodyRotation(Vector2 worldSlope, Quaternion heading)
        {
            Vector2 local = InHeading(worldSlope, heading);
            return Quaternion.FromToRotation(Vector3.up, new Vector3(-local.x, 1f, -local.y).normalized);
        }

        public static float Height(float load, Vector2 slope, float x, float z, Vector2 bodySlope = default) =>
            Mathf.Clamp01(load) * FullHeight +
            slope.x * Width * 0.5f * Mathf.Sin(x / Width * Mathf.PI) +
            slope.y * Length * 0.5f * Mathf.Sin(z / Length * Mathf.PI) - bodySlope.x * x - bodySlope.y * z;

        // 0/1 are the right/left rim, 2/3 the front/back. Along is metres.
        public static Vector3 RimPoint(byte side, float along) => side < 2
            ? new Vector3(side == 0 ? Width * 0.5f : -Width * 0.5f, Depth, along)
            : new Vector3(along, Depth, side == 2 ? Length * 0.5f : -Length * 0.5f);
        public static Vector3 Outward(byte side) => side < 2
            ? (side == 0 ? Vector3.right : Vector3.left)
            : (side == 2 ? Vector3.forward : Vector3.back);

        public static float Overflow(float load, Vector2 slope, float rate,
            out byte side, out float along, out float width, out float risk, Vector2 bodySlope = default)
        {
            side = 0; along = width = risk = 0f;
            if (load <= 0f) return 0f;
            float best = 0f, maxRise = 0f;
            for (byte edge = 0; edge < 4; edge++)
            {
                float length = edge < 2 ? Length : Width;
                float sum = 0f, wet = 0f, firstWet = 0f, lastWet = 0f;
                for (int i = 0; i < EdgeSamples; i++)
                {
                    float at = ((i + 0.5f) / EdgeSamples - 0.5f) * length;
                    Vector3 p = RimPoint(edge, at);
                    float rise = Height(load, slope, p.x, p.z, bodySlope) - load * FullHeight;
                    maxRise = Mathf.Max(maxRise, rise);
                    float depth = Mathf.Max(0f, Height(load, slope, p.x, p.z, bodySlope) - Depth);
                    sum += depth;
                    if (depth > 0f)
                    {
                        if (wet == 0f) firstWet = at - length / EdgeSamples * 0.5f;
                        lastWet = at + length / EdgeSamples * 0.5f;
                        wet += length / EdgeSamples;
                    }
                }
                float area = sum * length / EdgeSamples;
                if (area <= best) continue;
                best = area; side = edge; along = (firstWet + lastWet) * 0.5f; width = wet;
            }
            risk = maxRise / Mathf.Max(0.01f, Depth - load * FullHeight);
            return Mathf.Min(MaxOverflowRate, best * rate);
        }
    }
}
