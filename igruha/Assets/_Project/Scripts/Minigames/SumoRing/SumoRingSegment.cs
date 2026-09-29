using UnityEngine;

namespace Igruha.Minigames.SumoRing
{
    /// <summary>Collider changes only in physics. The art falls independently from that fixed surface.</summary>
    public sealed class SumoRingSegment : MonoBehaviour
    {
        [SerializeField] private int ring, sector;
        [SerializeField] private Collider support;
        [SerializeField] private Transform visual;
        [SerializeField] private GameObject cracks;
        [SerializeField] private ParticleSystem dust;
        private Vector3 rest, pivot;
        private Quaternion rotation;
        private bool fell, warned;
        public int Ring => ring;
        public int Sector => sector;
        private const float ShakeSize = .012f, FallAcceleration = 8f, FallSpin = 32f, DisappearAfter = 1.25f;
        private void Awake()
        {
            rest = visual.localPosition; rotation = visual.localRotation;
            pivot = visual.InverseTransformPoint(support.bounds.center);
        }
        public void ResetSegment()
        {
            fell = warned = false; support.enabled = true; visual.gameObject.SetActive(true);
            visual.localPosition = rest; visual.localRotation = rotation;
            if (cracks != null) cracks.SetActive(false);
            if (dust != null) dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        public void TickPhysics(SumoConfig config, double elapsed)
        { support.enabled = elapsed < config.SegmentFallsAt(ring, sector); }
        public void TickVisual(SumoConfig config, double elapsed)
        {
            double at = config.SegmentFallsAt(ring, sector);
            float warning = (float)(elapsed - (config.CollapseAt(ring) - config.Warning));
            if (warning >= 0 && !warned)
            {
                warned = true;
                if (cracks != null) cracks.SetActive(true);
                if (dust != null) dust.Emit(2);
            }
            if (elapsed < at)
            {
                if (warned) visual.localPosition = rest + Vector3.up * (Mathf.Sin(warning * 34 + sector) * ShakeSize);
                return;
            }
            if (!fell)
            {
                fell = true;
                if (dust != null) dust.Play();
            }
            float t = (float)(elapsed - at);
            if (t > DisappearAfter) { if (visual.gameObject.activeSelf) visual.gameObject.SetActive(false); return; }
            var tilt = Quaternion.Euler(t * FallSpin, 0, t * FallSpin * .4f);
            visual.localPosition = rest + Vector3.down * (FallAcceleration * t * t) + rotation * (pivot - tilt * pivot);
            visual.localRotation = rotation * tilt;
        }
    }
}
