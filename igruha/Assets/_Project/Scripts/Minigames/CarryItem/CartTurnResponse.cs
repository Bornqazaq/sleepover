using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Cornering loads the suspension. A quick countersteer meets the previous wave.</summary>
    public sealed class CartTurnResponse
    {
        private const float QuietAcceleration = .6f, Build = .35f, Decay = .55f;
        private const float Spring = 22f, Damping = 4.2f, SlopeGain = .055f, MaxSlope = .16f;
        private float energy;
        private Vector2 velocity;
        public Vector2 Slope { get; private set; }
        public Vector3 Acceleration { get; private set; }
        public float WaveGain => 1f + energy * .8f;
        public void Reset() { energy = 0f; Slope = velocity = Vector2.zero; Acceleration = Vector3.zero; }
        public void Step(Vector3 lateralAcceleration, float speed, float dt)
        {
            float force = Mathf.Max(0f, lateralAcceleration.magnitude - QuietAcceleration);
            // A broad curve or a walking-speed correction is safe. The stronger
            // response belongs to an actual sharp turn, not every steering input.
            Acceleration = lateralAcceleration.normalized * force * Mathf.InverseLerp(.55f, 1.3f, speed);
            force = Acceleration.magnitude;
            energy = Mathf.Clamp01(energy + (force * Mathf.Clamp01(speed) * Build - Decay) * dt);
            // The outside edge lowers while the surface lags behind: both show the same danger.
            var target = Vector2.ClampMagnitude(new Vector2(Acceleration.x, Acceleration.z) * SlopeGain, MaxSlope);
            velocity += ((target - Slope) * Spring - velocity * Damping) * dt;
            Slope = Vector2.ClampMagnitude(Slope + velocity * dt, MaxSlope);
        }
    }
}
