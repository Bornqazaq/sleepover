using System.Collections.Generic;
using Igruha.Core.Player;
using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>Authority-only, staggered glances towards living cage occupants.</summary>
    internal sealed class CircusBearCageAttention
    {
        private const float WatchSeconds = 2f;
        private const float CompanionTargetPenalty = 80f;
        private const float PreviousTargetPenalty = 20f;
        private float delay, watchLeft;
        private PlayerController previous;
        internal PlayerController Target { get; private set; }
        internal bool Watching { get; private set; }

        internal void Reset(int slot)
        {
            Target = null; previous = null; Watching = false; watchLeft = 0;
            delay = 2.5f + slot * 2f;
        }

        internal void Cancel()
        {
            Target = null; Watching = false; watchLeft = 0;
            delay = Mathf.Max(delay, 2.5f);
        }

        internal void BeginWatch() { Watching = true; watchLeft = WatchSeconds; }

        internal void Tick(float dt, IReadOnlyList<PlayerController> occupants, PitBear bear, bool lowCage)
        {
            bool valid = false;
            if (Target != null && occupants != null)
                for (int i = 0; i < occupants.Count; i++)
                    if (occupants[i] == Target) { valid = true; break; }
            if (Target != null && !valid) Cancel();
            if (Watching)
            {
                watchLeft -= dt;
                if (watchLeft > 0) return;
                previous = Target; Target = null; Watching = false;
                delay = Random.Range(4.5f, 7.5f);
                return;
            }
            if (Target != null) return;
            delay -= dt * (lowCage ? 1.25f : 1f);
            if (delay > 0 || occupants == null) return;
            float best = float.PositiveInfinity;
            for (int i = 0; i < occupants.Count; i++)
            {
                PlayerController candidate = occupants[i];
                if (candidate == null || !candidate.gameObject.activeInHierarchy) continue;
                Vector3 delta = candidate.Position - bear.transform.position; delta.y = 0;
                float score = delta.sqrMagnitude;
                if (bear.Companion != null && bear.Companion.CageTarget == candidate) score += CompanionTargetPenalty;
                if (candidate == previous) score += PreviousTargetPenalty;
                if (score >= best) continue;
                best = score; Target = candidate;
            }
        }
    }
}
