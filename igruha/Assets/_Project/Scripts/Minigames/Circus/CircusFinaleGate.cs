using System;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.Circus
{
    /// <summary>Results are authoritative immediately, but a lagging circus client
    /// may still be showing its confirmed local hit. Finish that short presentation
    /// before delivering either the Results phase or its separate UI payload.</summary>
    public sealed class CircusFinaleGate
    {
        private const float MaximumWaitSeconds = 2f;
        private readonly Action<MinigamePhase> applyPhase;
        private readonly Action<MinigameResults, bool> applyResults;
        private readonly MinigameResults pendingResults = new MinigameResults();
        private bool pending, hasResults, seriesFinal;
        private float deadline;
        public bool HasPending => pending;

        public CircusFinaleGate(Action<MinigamePhase> phase, Action<MinigameResults, bool> results)
        {
            applyPhase = phase;
            applyResults = results;
        }

        public void ReceivePhase(MinigamePhase phase, bool localPresenting, float realtime)
        {
            if (phase != MinigamePhase.Results)
            {
                Cancel();
                applyPhase(phase);
                return;
            }
            if (!localPresenting && !pending)
            {
                applyPhase(phase);
                return;
            }
            BeginWait(realtime);
            Tick(localPresenting, realtime);
        }

        public void ReceiveResults(MinigameResults results, bool isSeriesFinal, bool localPresenting, float realtime)
        {
            if (!localPresenting && !pending)
            {
                applyResults(results, isSeriesFinal);
                return;
            }
            BeginWait(realtime);
            // The network bridge reuses its incoming container. Keep value copies
            // of all entries/metadata, rather than a reference to the next packet.
            pendingResults.CopyFrom(results);
            hasResults = true;
            seriesFinal = isSeriesFinal;
            Tick(localPresenting, realtime);
        }

        public void Tick(bool localPresenting, float realtime)
        {
            if (!pending || localPresenting && realtime < deadline) return;
            bool deliverResults = hasResults, final = seriesFinal;
            pending = hasResults = false;
            // A results payload itself confirms the phase even if its separate
            // NetworkVariable arrives later. Cleanup must precede the results UI.
            applyPhase(MinigamePhase.Results);
            if (deliverResults) applyResults(pendingResults, final);
        }

        private void BeginWait(float realtime)
        {
            if (pending) return;
            pending = true;
            deadline = realtime + MaximumWaitSeconds;
        }

        public void Cancel()
        {
            pending = hasResults = false;
            pendingResults.Reset();
        }
    }
}
