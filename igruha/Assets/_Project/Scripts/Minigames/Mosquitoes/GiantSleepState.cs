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
        public bool CanBite => ImmunityRemaining <= 0f && Phase == GiantPhase.Sleeping;
        public bool CanSleep => Phase == GiantPhase.Awake;
        public int RecentBites => biteWindowRemaining > 0 ? bites : 0;
        public float BiteWindowRemaining => biteWindowRemaining;
        public bool CanSwat => Phase == GiantPhase.Awake;
        public bool ReachedTarget => Sleep >= config.SleepTarget;

        public GiantSleepState(MosquitoesConfig config) { this.config = config; }

        public bool ToggleBed()
        {
            if (Phase == GiantPhase.Sleeping) { Wake(false); return true; }
            if (!CanSleep) return false;
            Phase = GiantPhase.LyingDown;
            TransitionRemaining = config.LieDownSeconds;
            ImmunityRemaining = config.LieDownSeconds + config.ImmunitySeconds;
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

        // Every waking gives the victim sight of nearby attackers and a chance to react.
        private void Wake(bool forced)
        {
            Phase = GiantPhase.Waking;
            TransitionRemaining = config.WakeSeconds;
            LampOn = true;
            if (forced)
            {
                ImmunityRemaining = config.ImmunitySeconds;
                Sleep = Mathf.Max(0, Sleep - config.WakeSleepLoss);
            }
            bites = 0;
            biteWindowRemaining = 0f;
        }

        public bool RegisterBite(int livingMosquitoes = 2)
        {
            if (!CanBite) return false;
            if (biteWindowRemaining <= 0f) bites = 0;
            if (bites == 0) biteWindowRemaining = config.BiteWindow;
            if (++bites >= (livingMosquitoes == 1 ? 1 : config.BitesToWake)) Wake(true);
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

        public void ApplySnapshot(GiantPhase phase, float sleep, float transition, float immunity, bool lamp, int recentBites = 0, float biteWindow = 0)
        {
            Phase = phase; Sleep = sleep; TransitionRemaining = transition; ImmunityRemaining = immunity; LampOn = lamp;
            bites = recentBites; biteWindowRemaining = biteWindow;
        }
    }
}
