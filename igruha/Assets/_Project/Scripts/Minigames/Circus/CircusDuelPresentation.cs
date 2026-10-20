using System;
using Igruha.Core.Audio;
using Igruha.Core.Minigame;
using UnityEngine;

namespace Igruha.Minigames.Circus
{
    public interface ICircusDuelAudience
    {
        bool CanWatchDuel(CageStation first, CageStation second);
        string DuelName(CageStation cage);
    }

    /// <summary>A short arena reveal, followed by irregular show lighting. Never reads a hidden timer or answer.</summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class CircusDuelPresentation : MonoBehaviour
    {
        [SerializeField] private CageStation[] cages = Array.Empty<CageStation>();
        [SerializeField] private CircusSpotlight[] spots = Array.Empty<CircusSpotlight>();
        [SerializeField] private Light[] houseLights = Array.Empty<Light>();
        [SerializeField] private CircusDuelPracticalLights practicalLights;
        private ICircusDuelAudience audience;
        [SerializeField] private MinigameAudioPlayer audioPlayer;
        [SerializeField] private CircusDuelCamera cameraShow;
        [SerializeField] private CircusDuelComicView comicView;
        private string firstName, secondName;
        private bool pitLighting;
        public bool PitLightingActive => pitLighting;

        private const float PitStrength = 1.65f, PitStrengthVariation = .35f, PitSpeed = 1.85f;
        private const float PitColorSpeed = .23f, PitColorSeedStep = 4.17f;
        private static readonly Color PitWarmStart = new Color(1, .18f, .025f), PitWarmEnd = new Color(1, .65f, .2f);
        private static readonly Color PitCoolStart = new Color(.03f, .3f, 1), PitCoolEnd = new Color(.15f, .9f, 1);
        private static readonly Color Warm = new Color(1f, .9f, .73f);
        private static readonly Color Cool = new Color(.64f, .83f, 1f);
        private const float BlackoutLevel = .025f;
        private const float IntroBulbLevel = .008f;
        private const float DuelHouseLevel = .44f;
        private const float SpotStrength = .32f;
        private const float RevealAttackSeconds = .045f;
        private const float RevealPunchSeconds = .28f;
        private const float RevealPunch = .45f;
        private const float ReferenceSpotDistanceSquared = 36f;
        private const float FrontLightOffset = 3f;
        private const float SweepStrength = 1.25f;
        private const float SweepSpeed = 1.35f;
        private const float RestoreSeconds = .6f;
        private const float BlackoutFadeSeconds = .3f;

        private float[] intensities;
        private Color ambientSky, ambientEquator, ambientGround;
        private float ambientIntensity, reflectionIntensity;
        private bool captured;
        private int firstSpot = -1, secondSpot = -1, previousCue = -1;
        public CircusDuelState State { get; private set; }
        public bool IsVisible => captured;
        public bool CanWatchLocally => State.Active && (audience != null
            ? audience.CanWatchDuel(cages[State.FirstCage], cages[State.SecondCage])
            : !Application.isPlaying);
        public bool IntroPlaying => State.Active && State.Cue(NetworkClock.Now) < 4;

        private void Awake()
        {
            audience = GetComponent<ICircusDuelAudience>();
            intensities = new float[houseLights.Length];
        }

        /// <summary>Called only at a safe turn boundary by the authoritative rules.</summary>
        public bool TryBegin(CageStation first, CageStation second)
        {
            if (State.Active || first == null || second == null || first == second) return false;
            int a = Array.IndexOf(cages, first), b = Array.IndexOf(cages, second);
            if (a < 0 || b < 0) return false;
            // Stable scene order, even when the roster arrived in a different order.
            ApplyState(new CircusDuelState { Active = true, FirstCage = Math.Min(a, b),
                SecondCage = Math.Max(a, b), StartedAt = NetworkClock.Now });
            return true;
        }

        public void ApplyState(CircusDuelState state)
        {
            if (State.Equals(state)) return;
            if (!state.Active) { ResetShow(); return; }
            if (state.FirstCage < 0 || state.FirstCage >= cages.Length ||
                state.SecondCage < 0 || state.SecondCage >= cages.Length || state.FirstCage == state.SecondCage) return;
            State = state;
            firstSpot = NearestSpot(cages[state.FirstCage], -1);
            secondSpot = NearestSpot(cages[state.SecondCage], firstSpot);
            // A late snapshot joins the current cue; it does not replay old sound hits.
            previousCue = state.Cue(NetworkClock.Now);

        }

        private void CaptureLighting()
        {
            if (captured) return;
            if (intensities == null || intensities.Length != houseLights.Length) intensities = new float[houseLights.Length];
            for (int i = 0; i < houseLights.Length; i++)
                if (houseLights[i] != null) intensities[i] = houseLights[i].intensity;
            ambientSky = RenderSettings.ambientSkyColor;
            ambientEquator = RenderSettings.ambientEquatorColor;
            ambientGround = RenderSettings.ambientGroundColor;
            ambientIntensity = RenderSettings.ambientIntensity;
            reflectionIntensity = RenderSettings.reflectionIntensity;
            captured = true;
        }

        private int NearestSpot(CageStation cage, int except)
        {
            int best = -1;
            float distance = float.PositiveInfinity;
            Vector3 radial = new Vector3(cage.transform.position.x, 0, cage.transform.position.z).normalized;
            for (int i = 0; i < spots.Length; i++)
            {
                if (i == except || spots[i] == null || spots[i].FollowsBear) continue;
                // Light faces from across the ring; a lamp behind a finalist only outlines their head.
                if (Vector3.Dot(spots[i].transform.position, radial) > -FrontLightOffset) continue;
                float d = (spots[i].transform.position - cage.transform.position).sqrMagnitude;
                if (d < distance) { best = i; distance = d; }
            }
            return best;
        }

        private void LateUpdate()
        {
            if (State.Active) RenderAt(NetworkClock.Now);
        }

        /// <summary>Absolute-clock evaluation also supports editor review without playing game logic.</summary>
        public void RenderAt(double now)
        {
            if (!State.Active) return;
            if (!CanWatchLocally)
            {
                // The snapshot survives: a player who becomes a spectator joins the current cue.
                RestorePresentation();
                previousCue = State.Cue(now);
                RenderPitLights((float)(now - State.StartedAt));
                return;
            }
            if (!captured)
            {
                StopPitLights();
                CaptureLighting();
                firstName = audience?.DuelName(cages[State.FirstCage]) ?? "ФИНАЛИСТ";
                secondName = audience?.DuelName(cages[State.SecondCage]) ?? "ФИНАЛИСТ";
            }
            int cue = State.Cue(now);
            float elapsed = Mathf.Max(0, (float)(now - State.StartedAt));
            bool live = cue >= 4;
            cameraShow?.Render(cages[State.FirstCage], cages[State.SecondCage], cue, elapsed);
            if (Application.isPlaying)
                comicView?.Render(cages[State.FirstCage], cages[State.SecondCage], firstName, secondName, cue, elapsed);
            float opening = Mathf.SmoothStep(0, 1, elapsed / BlackoutFadeSeconds);
            float release = Mathf.SmoothStep(0, 1, (elapsed - CircusDuelState.IntroSeconds) / RestoreSeconds);
            float house = live ? Mathf.Lerp(BlackoutLevel, DuelHouseLevel, release) : Mathf.Lerp(1, BlackoutLevel, opening);
            practicalLights?.Dim(live ? Mathf.Lerp(IntroBulbLevel, 1f, release) : Mathf.Lerp(1f, IntroBulbLevel, opening));
            for (int i = 0; i < houseLights.Length; i++)
                if (houseLights[i] != null) houseLights[i].intensity = intensities[i] * house;
            RenderSettings.ambientSkyColor = ambientSky * house;
            RenderSettings.ambientEquatorColor = ambientEquator * house;
            RenderSettings.ambientGroundColor = ambientGround * house;
            RenderSettings.ambientIntensity = ambientIntensity * house;
            RenderSettings.reflectionIntensity = reflectionIntensity * house;

            for (int i = 0; i < spots.Length; i++)
            {
                var spot = spots[i];
                if (spot == null) continue;
                bool first = i == firstSpot, second = i == secondSpot;
                bool lit = first && (cue == 1 || cue >= 3) || second && cue >= 2;
                var cage = first ? cages[State.FirstCage] : second ? cages[State.SecondCage] : null;
                // Keep the pit readable once play resumes, including a fallen finalist.
                bool focus = cage != null && !cage.DoorsOpen;
                float focusStrength = focus ? SpotStrength *
                    (spot.Head.position - (cage.transform.position + Vector3.up)).sqrMagnitude / ReferenceSpotDistanceSquared : 0f;
                float revealAt = cue == 1 ? CircusDuelState.BlackoutSeconds :
                    cue == 2 ? CircusDuelState.SecondSpotAt : CircusDuelState.BothSpotsAt;
                float revealAge = Mathf.Max(0, elapsed - revealAt);
                float reveal = live ? 1f : Mathf.Clamp01(revealAge / RevealAttackSeconds) *
                    (1f + RevealPunch * (1f - Mathf.Clamp01(revealAge / RevealPunchSeconds)));
                float strength = focus ? (lit ? focusStrength * reveal : 0f) : live ?
                    (spot.FollowsBear ? 1f : SweepStrength) * release : 0f;
                spot.SetShowLighting(strength, live && !focus ? SweepSpeed : 1f,
                    focus ? cage.transform : null, first ? Warm : Cool);
            }

            if (cue != previousCue)
            {
                previousCue = cue;
                // Only the cue entered now sounds; no queued replay after a stall.
                if (audioPlayer != null)
                {
                    if (cue == 1 || cue == 2) audioPlayer.Play("SFX_CIRCUS_Relay_Click");
                    else if (cue == 4 && elapsed < CircusDuelState.IntroSeconds + CircusDuelState.LaunchTailSeconds)
                        audioPlayer.Play("SFX_CIRCUS_Results_Hit");
                }
            }
        }

        private void RenderPitLights(float elapsed)
        {
            pitLighting = true;
            for (int i = 0; i < spots.Length; i++)
            {
                var spot = spots[i];
                if (spot == null || spot.FollowsBear) continue;
                float shift = Mathf.PerlinNoise(elapsed * PitColorSpeed, i * PitColorSeedStep);
                Color tint = i % 2 == 0
                    ? Color.Lerp(PitWarmStart, PitWarmEnd, shift)
                    : Color.Lerp(PitCoolStart, PitCoolEnd, shift);
                spot.SetCelebrationLighting(PitStrength + PitStrengthVariation * shift, PitSpeed, tint);
            }
        }

        private void StopPitLights()
        {
            if (!pitLighting) return;
            for (int i = 0; i < spots.Length; i++) if (spots[i] != null && !spots[i].FollowsBear) spots[i].ResetShowLighting();
            pitLighting = false;
        }

        public void ResetShow()
        {
            RestorePresentation();
            StopPitLights();
            State = default;
            previousCue = -1;
        }

        private void RestorePresentation()
        {
            cameraShow?.Restore();
            comicView?.Hide();
            practicalLights?.Restore();
            if (!captured) return;
            for (int i = 0; i < spots.Length; i++) if (spots[i] != null) spots[i].ResetShowLighting();
            for (int i = 0; i < houseLights.Length; i++) if (houseLights[i] != null) houseLights[i].intensity = intensities[i];
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.reflectionIntensity = reflectionIntensity;
            captured = false;
        }

        private void OnDisable() => ResetShow();
    }
}
