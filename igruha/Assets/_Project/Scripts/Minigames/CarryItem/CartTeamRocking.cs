using Igruha.Core.Items;
using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Disagreeing hands excite a damped suspension. Releasing the keys removes forcing, not inertia.</summary>
    public sealed class CartTeamRocking
    {
        private Vector2 axis = Vector2.right;
        private float phase;
        public float Strain { get; private set; }
        public Vector2 Slope { get; private set; }
        public Vector2 Velocity { get; private set; }

        public void Reset()
        {
            Strain = phase = 0f; Slope = Velocity = Vector2.zero; axis = Vector2.right;
        }

        public void Step(MultiCarryObject carry, CarryItemConfig config, float dt)
        {
            float worst = 0f;
            Vector2 nextAxis = axis;
            for (int a = 0; a < carry.HandleCount; a++)
            {
                Vector3 first = carry.CarrierIntentAt(a);
                for (int b = a + 1; b < carry.HandleCount; b++)
                {
                    Vector3 second = carry.CarrierIntentAt(b);
                    float conflict = CartCoordinationMath.Disagreement(first, second);
                    if (conflict <= worst) continue;
                    worst = conflict;
                    Vector3 difference = first - second;
                    nextAxis = new Vector2(difference.x, difference.z).normalized;
                }
            }
            // The same pair may exchange slots or change which hand is strongest.
            // A sign change must not flip the oscillation halfway through a swing.
            if (Vector2.Dot(axis, nextAxis) < 0f) nextAxis = -nextAxis;
            axis = Vector2.Lerp(axis, nextAxis, 1f - Mathf.Exp(-dt * config.TeamRockAxisResponse)).normalized;
            Strain = Mathf.MoveTowards(Strain, worst, dt * (worst > Strain ? config.TeamRockBuild : config.TeamRockRelease));
            if (Strain == 0f && Slope.sqrMagnitude < 0.000001f && Velocity.sqrMagnitude < 0.000001f)
            { Reset(); return; }
            phase = Mathf.Repeat(phase + dt * config.TeamRockFrequency * Mathf.PI * 2f, Mathf.PI * 2f);
            Vector2 target = axis * (Mathf.Sin(phase) * config.TeamRockSlope * Strain * Mathf.Lerp(0.7f, 1f, carry.Load));
            Velocity += ((target - Slope) * config.TeamRockResponse - Velocity * config.TeamRockDamping) * dt;
            Slope = Vector2.ClampMagnitude(Slope + Velocity * dt, config.TeamRockLimit);
        }
    }
}
