using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    public enum GiantPhase : byte { Sleeping, Waking, Awake, LyingDown }

    /// <summary>Authoritative rules, independent from rendering, input and transport.</summary>
    public sealed class GiantSleepState
    {
        private readonly MosquitoesConfig config;
        private float biteWindowRemaining;
        private int bites;
        public GiantPhase Phase { get; private set; } = GiantPhase.Sleeping;
        public float Sleep { get; private set; }
        public float TransitionRemaining { get; private set; }
        public float ImmunityRemaining { get; private set; }
        public bool LampOn { get; private set; }
        public bool CanBite => ImmunityRemaining <= 0f && (Phase == GiantPhase.Sleeping || Phase == GiantPhase.LyingDown);
        public bool CanSwat => Phase == GiantPhase.Awake;
        public bool ReachedTarget => Sleep >= config.SleepTarget;

        public GiantSleepState(MosquitoesConfig config) { this.config = config; }

        public bool ToggleBed()
        {
            if (Phase == GiantPhase.Sleeping) { Wake(false); return true; }
            if (Phase != GiantPhase.Awake) return false;
            Phase = GiantPhase.LyingDown;
            TransitionRemaining = config.LieDownSeconds;
            LampOn = false;
            bites = 0;
            biteWindowRemaining = 0f;
            return true;
        }
        public bool ToggleLamp()
        {
            if (Phase == GiantPhase.Sleeping) { Wake(false); return true; }
            if (Phase != GiantPhase.Awake) return false;
            LampOn = !LampOn;
            return true;
        }

        // Awake darkness is intentional: forced waking does not switch the lamp.
        // Voluntary E-waking does; this preserves blind swats with the existing E binding.
        private void Wake(bool forced)
        {
            Phase = GiantPhase.Waking;
            TransitionRemaining = config.WakeSeconds;
            if (!forced) LampOn = true;
            if (forced) ImmunityRemaining = config.ImmunitySeconds;
            bites = 0;
            biteWindowRemaining = 0f;
        }

        public bool RegisterBite()
        {
            if (!CanBite) return false;
            // LDD exception: bites while lying down consume the mosquito's cooldown,
            // but cannot wake the giant or carry a preloaded pair into sleep.
            if (Phase == GiantPhase.LyingDown) return true;
            if (biteWindowRemaining <= 0f) bites = 0;
            if (bites == 0) biteWindowRemaining = config.BiteWindow;
            if (++bites >= config.BitesToWake) Wake(true);
            return true;
        }

        public void Tick(float dt)
        {
            dt = Mathf.Max(0f, dt);
            ImmunityRemaining = Mathf.Max(0f, ImmunityRemaining - dt);
            biteWindowRemaining = Mathf.Max(0f, biteWindowRemaining - dt);
            float sleepDelta = dt;
            if (Phase == GiantPhase.Waking || Phase == GiantPhase.LyingDown)
            {
                sleepDelta = Mathf.Max(0f, dt - TransitionRemaining);
                TransitionRemaining = Mathf.Max(0f, TransitionRemaining - dt);
                if (TransitionRemaining <= 0f)
                    Phase = Phase == GiantPhase.Waking ? GiantPhase.Awake : GiantPhase.Sleeping;
            }
            if (Phase == GiantPhase.Sleeping && !LampOn) Sleep = Mathf.Min(config.SleepTarget, Sleep + sleepDelta);
        }

        public void ApplySnapshot(GiantPhase phase, float sleep, float transition, float immunity, bool lamp)
        {
            Phase = phase; Sleep = sleep; TransitionRemaining = transition; ImmunityRemaining = immunity; LampOn = lamp;
        }
    }
}
