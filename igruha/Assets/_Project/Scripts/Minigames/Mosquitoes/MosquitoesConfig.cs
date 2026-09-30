using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    [CreateAssetMenu(fileName = "MosquitoesConfig", menuName = "Igruha/Minigames/Mosquitoes Config")]
    public sealed class MosquitoesConfig : ScriptableObject
    {
        [Header("Сон и раунд")]
        [SerializeField, Min(0.1f)] private float sleepTarget = 60f;
        [SerializeField, Min(0f)] private float countdownSeconds = 3f;
        [SerializeField, Min(0.01f)] private float wakeSeconds = 0.5f;
        [SerializeField, Min(0.01f)] private float lieDownSeconds = 1f;
        [SerializeField, Min(0f)] private float immunitySeconds = 2f;
        [SerializeField] private bool idleMosquitoPenalty;
        [Header("Укус и шлепок")]
        [SerializeField, Min(0.01f)] private float biteDistance = 0.55f;
        [SerializeField, Min(0.01f)] private float biteHoldSeconds = 0.5f;
        [SerializeField, Min(0f)] private float biteCooldown = 5f;
        [SerializeField, Min(0.01f)] private float biteWindow = 4f;
        [SerializeField, Min(1)] private int bitesToWake = 2;
        [SerializeField, Min(0.01f)] private float swatRadius = 1.3f;
        [SerializeField, Range(1, 360)] private float swatArc = 140f;
        [Header("Полёт")]
        [SerializeField, Min(0.01f)] private float maxSpeed = 8f;
        [SerializeField, Min(0f)] private float quietSpeed = 2.2f;
        [SerializeField, Min(0.01f)] private float acceleration = 24f;
        [SerializeField, Min(0.01f)] private float braking = 20f;
        [SerializeField, Min(0.01f)] private float flightCeiling = 2.16f;
        [SerializeField, Min(0.01f)] private float bodyRadius = 0.12f;
        [SerializeField, Min(0.1f)] private float stuckSeconds = 3f;
        [SerializeField, Min(0.01f)] private float cameraRadius = 1.2f;
        [Header("Свет и звук")]
        [SerializeField, Min(0.1f)] private float lightRadius = 2.88f;
        [SerializeField, Min(0f)] private float windowIntensity = 0.15f;
        [SerializeField, Min(0f)] private float quietThreshold = 2.2f;
        [SerializeField, Min(0.01f)] private float loudThreshold = 7f;
        [SerializeField, Min(0.1f)] private float hearingRadius = 12f;
        [SerializeField, Range(0, 1)] private float sleepVignette = 0.9f;

        public float SleepTarget => sleepTarget;
        public float CountdownSeconds => countdownSeconds;
        public float WakeSeconds => wakeSeconds;
        public float LieDownSeconds => lieDownSeconds;
        public float ImmunitySeconds => immunitySeconds;
        public bool IdleMosquitoPenalty => idleMosquitoPenalty;
        public float BiteDistance => biteDistance;
        public float BiteHoldSeconds => biteHoldSeconds;
        public float BiteCooldown => biteCooldown;
        public float BiteWindow => biteWindow;
        public int BitesToWake => bitesToWake;
        public float SwatRadius => swatRadius;
        public float SwatArc => swatArc;
        public float MaxSpeed => maxSpeed;
        public float QuietSpeed => quietSpeed;
        public float Acceleration => acceleration;
        public float Braking => braking;
        public float FlightCeiling => flightCeiling;
        public float BodyRadius => bodyRadius;
        public float StuckSeconds => stuckSeconds;
        public float CameraRadius => cameraRadius;
        public float LightRadius => lightRadius;
        public float WindowIntensity => windowIntensity;
        public float QuietThreshold => quietThreshold;
        public float LoudThreshold => loudThreshold;
        public float HearingRadius => hearingRadius;
        public float SleepVignette => sleepVignette;
    }
}
