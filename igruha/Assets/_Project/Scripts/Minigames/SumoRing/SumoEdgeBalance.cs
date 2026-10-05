using UnityEngine;

namespace Igruha.Minigames.SumoRing
{
    /// <summary>Presentation-only proximity to missing support, including the side of a sector during a collapse sweep.</summary>
    public static class SumoEdgeBalance
    {
        public const float Reach = .7f;
        private const int Directions = 16, Refinements = 5;
        private const float FullBalanceDistance = .08f;
        private static readonly Vector3[] Probes = CreateProbes();

        public static float Evaluate(SumoConfig config, Vector3 feet, double elapsed, out Vector3 outward)
        {
            outward = Vector3.zero;
            if (elapsed < 0 || Mathf.Abs(feet.y - config.Height) > config.FallTolerance || !Supported(config, feet, elapsed)) return 0;
            float nearest = Reach;
            for (int i = 0; i < Directions; i++)
            {
                Vector3 direction = Probes[i];
                if (Supported(config, feet + direction * Reach, elapsed)) continue;
                float low = 0, high = Reach;
                for (int j = 0; j < Refinements; j++)
                {
                    float middle = (low + high) * .5f;
                    if (Supported(config, feet + direction * middle, elapsed)) low = middle;
                    else high = middle;
                }
                nearest = Mathf.Min(nearest, high);
                outward += direction * (1 - high / Reach);
            }
            outward = outward.normalized;
            return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Reach, FullBalanceDistance, nearest));
        }

        private static bool Supported(SumoConfig config, Vector3 point, double elapsed)
        {
            float radius = config.SupportRadius(point, elapsed);
            return point.x * point.x + point.z * point.z <= radius * radius;
        }
        private static Vector3[] CreateProbes()
        {
            var result = new Vector3[Directions];
            for (int i = 0; i < Directions; i++)
            {
                float angle = i * Mathf.PI * 2 / Directions;
                result[i] = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            }
            return result;
        }
    }
}
