using System;
using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>Sawdust appears only as the moving lamps pass through it.</summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class CircusAirDust : MonoBehaviour
    {
        [SerializeField] private CircusSpotlight[] spotlights = Array.Empty<CircusSpotlight>();
        private ParticleSystem dust;
        private ParticleSystem.Particle[] particles;
        private float nextSample;

        private void Awake()
        {
            dust = GetComponent<ParticleSystem>();
            particles = new ParticleSystem.Particle[dust.main.maxParticles];
        }

        private void LateUpdate()
        {
            if (Time.time < nextSample) return;
            nextSample = Time.time + .08f;
            int count = dust.GetParticles(particles);
            for (int i = 0; i < count; i++)
            {
                Color radiance = Color.black;
                foreach (var spot in spotlights)
                    if (spot != null) radiance += spot.DustRadiance(particles[i].position);
                float peak = Mathf.Max(radiance.r, Mathf.Max(radiance.g, radiance.b));
                float life = particles[i].remainingLifetime / particles[i].startLifetime;
                float fade = Mathf.SmoothStep(0, 1, life / .13f)
                    * (1 - Mathf.SmoothStep(0, 1, (life - .85f) / .15f));
                radiance.a = Mathf.Clamp01(peak) * fade * .55f;
                if (peak > 1) { radiance.r /= peak; radiance.g /= peak; radiance.b /= peak; }
                particles[i].startColor = radiance;
            }
            dust.SetParticles(particles, count);
        }
    }
}
