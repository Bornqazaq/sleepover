using System;
using Igruha.Core.Minigame;
using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>Decorative follow spots. Uses the shared clock, never a round timer or hidden answer.</summary>
    [DisallowMultipleComponent]
    public sealed class CircusSpotlight : MonoBehaviour
    {
        [SerializeField] private Transform head;
        [SerializeField] private Light spotlight;
        [SerializeField] private PitBear[] bears = Array.Empty<PitBear>();
        [SerializeField] private CageStation[] cages = Array.Empty<CageStation>();
        [SerializeField] private int fixtureIndex;
        [SerializeField] private bool followBear;
        [SerializeField] private float phraseSeconds = 7.13f;
        [SerializeField] private float phaseOffset;
        [SerializeField] private float turnDegreesPerSecond = 32f;
        private const float BearAimHeight = 1.05f;
        private const float PlayerAimHeight = 1.0f;
        private const float CelebrationNearRadius = 2.8f, CelebrationFarRadius = 5.4f;
        private const float CelebrationAngleSpeed = .09f, CelebrationRadiusSpeed = .13f;
        private float baseIntensity;
        private float outerConeCos;
        private float innerConeCos;
        private Color baseColor;
        private Texture baseCookie;
        private Renderer beam;
        private MaterialPropertyBlock beamProperties;
        private float beamDensity;
        private float showDimmer = 1f, showSpeed = 1f;
        private Transform showFocus;
        private Color showColor;
        private static readonly int DensityId = Shader.PropertyToID("_Density");
        private static readonly int ColorId = Shader.PropertyToID("_BaseColor");

        public Transform Head => head;
        public bool FollowsBear => followBear;

        private void Awake()
        {
            if (spotlight == null) spotlight = GetComponentInChildren<Light>();
            if (spotlight == null) return;
            baseIntensity = spotlight.intensity;
            baseColor = spotlight.color;
            baseCookie = spotlight.cookie;
            var beamTransform = spotlight.transform.Find("HazeBeam");
            if (beamTransform != null) beam = beamTransform.GetComponent<Renderer>();
            if (beam != null)
            {
                beamDensity = beam.sharedMaterial.GetFloat(DensityId);
                beamProperties = new MaterialPropertyBlock();
            }
            outerConeCos = Mathf.Cos(spotlight.spotAngle * .5f * Mathf.Deg2Rad);
            innerConeCos = Mathf.Cos(spotlight.innerSpotAngle * .5f * Mathf.Deg2Rad);
        }

        private void LateUpdate()
        {
            if (head == null) return;
            double clock = NetworkClock.Now;
            Vector3 aim = showFocus != null ? showFocus.position + Vector3.up * PlayerAimHeight :
                celebration && fixtureIndex % 4 < 2 ? CelebrationAim(clock) : EvaluateAim(clock * showSpeed);
            Vector3 direction = aim - head.position;
            if (direction.sqrMagnitude < .01f) return;
            float roll = followBear ? 0f : (float)(clock * (fixtureIndex % 2 == 0 ? 4.7 : -6.1));
            head.rotation = showFocus != null ? Quaternion.LookRotation(direction) :
                Quaternion.RotateTowards(head.rotation, Quaternion.LookRotation(direction) * Quaternion.Euler(0, 0, roll),
                    turnDegreesPerSecond * showSpeed * Time.deltaTime);
            if (spotlight != null)
            {
                float variation = followBear || showFocus != null ? 1f :
                    .87f + .13f * Mathf.PerlinNoise((float)clock * .19f, fixtureIndex * 3.17f);
                spotlight.intensity = baseIntensity * showDimmer * variation;
                spotlight.color = showFocus != null || celebration ? showColor : baseColor;
                spotlight.cookie = showFocus != null ? null : baseCookie;
                if (beam != null)
                {
                    beam.GetPropertyBlock(beamProperties);
                    beamProperties.SetFloat(DensityId, beamDensity * Mathf.Min(showDimmer, 1.1f));
                    beamProperties.SetColor(ColorId, spotlight.color);
                    beam.SetPropertyBlock(beamProperties);
                }
            }
        }

        public void SetShowLighting(float dimmer, float speed, Transform focus, Color tint)
        {
            showDimmer = Mathf.Max(0, dimmer);
            showSpeed = speed;
            showFocus = focus;
            showColor = tint;
            celebration = false;
        }

        private bool celebration;
        public bool IsCelebrating => celebration;
        public void SetCelebrationLighting(float strength, float speed, Color tint)
        {
            SetShowLighting(Mathf.Max(1f, strength), speed, null, tint);
            celebration = true;
        }

        private Vector3 CelebrationAim(double clock)
        {
            // Half of the decorative lamps cross the pit itself; the rest still tour the tent.
            // Unequal noise curves avoid giving the hidden stopwatch a rhythmic counting aid.
            float drift = Mathf.PerlinNoise((float)clock * CelebrationAngleSpeed, fixtureIndex * 3.17f);
            float angle = (fixtureIndex * 120f + drift * 180f) * Mathf.Deg2Rad;
            float radius = Mathf.Lerp(CelebrationNearRadius, CelebrationFarRadius,
                Mathf.PerlinNoise((float)clock * CelebrationRadiusSpeed, fixtureIndex * 5.31f));
            return new Vector3(Mathf.Sin(angle) * radius, .25f, Mathf.Cos(angle) * radius);
        }

        public void ResetShowLighting()
        {
            showDimmer = showSpeed = 1f;
            showFocus = null;
            celebration = false;
            if (spotlight != null) { spotlight.intensity = baseIntensity; spotlight.color = baseColor; spotlight.cookie = baseCookie; }
            if (beam != null) beam.SetPropertyBlock(null);
        }

        public Vector3 EvaluateAim(double clock)
        {
            if (followBear && bears.Length > 0)
            {
                var bear = bears[fixtureIndex % bears.Length];
                if (bear != null) return bear.PresentationRoot.position + Vector3.up * BearAimHeight;
            }
            double time = (clock + phaseOffset) / Math.Max(1f, phraseSeconds);
            int phrase = (int)Math.Floor(time);
            float t = (float)(time - Math.Floor(time));
            // Unequal phrases and broad curved passes avoid synchronised pendulums
            // or a metronome that could help players count the hidden stopwatch.
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.035f, .965f, t));
            Vector3 from = Target(phrase), to = Target(phrase + 1);
            Vector3 arc = Vector3.Cross((to - from).normalized, Vector3.up);
            float bend = Mathf.Sin(t * Mathf.PI) * (fixtureIndex % 2 == 0 ? 1.8f : -1.8f);
            return Vector3.Lerp(from, to, blend) + arc * bend;
        }

        private Vector3 Target(int phrase)
        {
            uint hash = unchecked((uint)(phrase * 747796405 + fixtureIndex * 2891336453L + 277803737));
            hash = (hash ^ (hash >> 16)) * 2246822519u;
            hash ^= hash >> 13;
            // Each circuit visits the pit, players, stalls, curtains and cupola.
            int zone = (phrase + fixtureIndex * 3) % 7;
            if (zone < 0) zone += 7;
            if (zone == 2 && cages.Length > 0)
            {
                int start = (int)(hash % (uint)cages.Length);
                for (int i = 0; i < cages.Length; i++)
                {
                    var cage = cages[(start + i) % cages.Length];
                    if (cage != null && cage.Occupant != null && !cage.DoorsOpen)
                        return cage.Occupant.transform.position + Vector3.up * PlayerAimHeight;
                }
                // An authored preview still has meaningful targets without players.
                if (!Application.isPlaying && cages[start] != null)
                    return cages[start].transform.position + Vector3.up * PlayerAimHeight;
            }
            if (zone == 0 && bears.Length > 0)
            {
                var bear = bears[hash % (uint)bears.Length];
                if (bear != null) return bear.PresentationRoot.position + Vector3.up * BearAimHeight;
            }
            // Outer targets stay opposite their fixture, sending shafts through
            // the tent instead of immediately lighting the wall behind the lamp.
            float angle = ((fixtureIndex - 4) * 45f + 180f + (hash % 1100) * .1f - 55f) * Mathf.Deg2Rad;
            float radius = zone <= 1 ? 5.8f : zone == 3 ? 11.8f : zone == 6 ? 7.5f : 15f;
            float height = zone <= 1 ? .25f : zone == 3 ? 1.7f : zone == 6 ? 16f : 4f + (hash % 400) * .01f;
            return new Vector3(Mathf.Sin(angle) * radius, height, Mathf.Cos(angle) * radius);
        }

        /// <summary>Cheap local scattering for airborne dust, without extra lights or raycasts.</summary>
        public Color DustRadiance(Vector3 position)
        {
            if (spotlight == null || !spotlight.isActiveAndEnabled) return Color.clear;
            Vector3 delta = position - spotlight.transform.position;
            float distanceSquared = delta.sqrMagnitude;
            if (distanceSquared < .01f || distanceSquared > spotlight.range * spotlight.range) return Color.clear;
            float angle = Vector3.Dot(delta / Mathf.Sqrt(distanceSquared), head.forward);
            float cone = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(outerConeCos, innerConeCos, angle));
            return spotlight.color * (cone * spotlight.intensity / (32f + distanceSquared) * .23f);
        }
    }
}
