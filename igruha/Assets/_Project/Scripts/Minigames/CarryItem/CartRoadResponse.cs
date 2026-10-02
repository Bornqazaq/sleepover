using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Impulse-driven suspension: isolated slow joints settle, a fast series stores energy.</summary>
    public sealed class CartRoadResponse
    {
        private const float CarefulSpeed=.8f, SpeedEnergyGain=.52f, MaxHit=1.5f, MinHit=.006f;
        private const float EnergyPerHit=.28f, MaxEnergy=1.6f, EnergyDecay=.7f, WaveBuildGain=.45f;
        private const float SlopeImpulse=1.6f, MaxVelocity=2.6f, Spring=32f, Damping=4.6f, MaxSlope=.29f;
        private const float HopImpulse=.18f, MaxHopVelocity=.65f, HopSpring=90f, HopDamping=7f;
        private const float MinHop=-.015f, MaxHop=.055f, CauseHold=1.1f, WheelWaveGain=.22f;
        private const float EmptyWaveGain=.2f, FullWaveGain=1.15f;
        public Vector2 Slope { get; private set; }
        public Vector2 Velocity { get; private set; }
        public float Hop { get; private set; }
        public float Energy { get; private set; }
        public float Hold { get; private set; }
        private float hopVelocity;
        public void Reset() { Slope = Velocity = Vector2.zero; Hop = Energy = Hold = hopVelocity = 0f; }
        public Vector2 Strike(Vector2 wheelOffset, Vector2 travel, float speed, float severity, float load)
        {
            // Quadratic impact energy makes creeping over a seam a useful choice.
            // Even the full solo cart (1.6 m/s) must respect a broken joint.
            // Below a deliberate walking pace the wheel rolls over it without a kick.
            float hit = Mathf.Clamp((speed * speed - CarefulSpeed * CarefulSpeed) * SpeedEnergyGain * severity, 0f, MaxHit);
            if (hit < MinHit) return Vector2.zero;
            Energy = Mathf.Min(MaxEnergy, Energy + hit * EnergyPerHit);
            Hold = CauseHold;
            Velocity += wheelOffset.normalized * hit * SlopeImpulse;
            Velocity = Vector2.ClampMagnitude(Velocity, MaxVelocity);
            hopVelocity = Mathf.Min(MaxHopVelocity, hopVelocity + hit * HopImpulse);
            // A wheel hitting the lip tips the tub and sends water forward; the
            // following axle arrives while that wave is still alive.
            return (travel.normalized + wheelOffset.normalized * WheelWaveGain) *
                (hit * Mathf.Lerp(EmptyWaveGain, FullWaveGain, load * load) * (1f + Energy * WaveBuildGain));
        }
        public void Step(float dt)
        {
            Energy = Mathf.Max(0f, Energy - dt * EnergyDecay); Hold = Mathf.Max(0f, Hold - dt);
            Velocity += (-Slope * Spring - Velocity * Damping) * dt;
            Slope = Vector2.ClampMagnitude(Slope + Velocity * dt, MaxSlope);
            hopVelocity += (-Hop * HopSpring - hopVelocity * HopDamping) * dt;
            Hop = Mathf.Clamp(Hop + hopVelocity * dt, MinHop, MaxHop);
        }
    }
}
