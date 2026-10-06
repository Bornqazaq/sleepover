using System;

namespace Igruha.Minigames.SumoRing
{
    /// <summary>
    /// Presentation only: replays the owner's unacknowledged intentions, or buffers remote
    /// phases until their server timestamp. Never resolves contacts, impulses or results.
    /// </summary>
    public sealed class SumoCombatVisualState
    {
        private const int Capacity = 64;
        public const double PredictionTimeout = 1.5;
        private struct Input
        {
            public int Sequence;
            public SumoCommand Command;
            public float Yaw;
            public double At;
        }

        private readonly SumoConfig config;
        private readonly Input[] inputs = new Input[Capacity];
        private readonly SumoCombatState[] snapshots = new SumoCombatState[Capacity];
        private SumoCombatState authority, visual;
        private int inputCount, snapshotCount, receivedRevision;

        public SumoCombatVisualState(SumoConfig config, int id)
        {
            this.config = config;
            Reset(SumoCombatState.Create(id));
        }

        public void Reset(SumoCombatState state)
        {
            authority = visual = state;
            receivedRevision = state.Revision;
            inputCount = snapshotCount = 0;
        }

        public void Predict(int sequence, SumoCommand command, float yaw, double now)
        {
            Evaluate(now);
            if (inputCount == Capacity) return;
            inputs[inputCount++] = new Input { Sequence = sequence, Command = command, Yaw = yaw, At = now };
            SumoCombatRules.Command(ref visual, command, yaw, now, config);
        }

        public void Receive(SumoCombatState state, double now, bool owner)
        {
            if (state.Revision <= receivedRevision) return;
            receivedRevision = state.Revision;
            if (owner)
            {
                authority = state;
                int remaining = 0;
                for (int i = 0; i < inputCount; i++)
                {
                    var input = inputs[i];
                    if (input.Sequence <= state.ProcessedInput) continue;
                    // Discard interrupted intentions permanently; a later Idle snapshot
                    // must not resurrect them while their RPC is still in flight.
                    if (state.Phase == SumoCombatPhase.Stagger && input.At < state.Since) continue;
                    inputs[remaining++] = input;
                }
                inputCount = remaining;
                Reconcile(now);
                return;
            }

            // A list delta can contain several phases in one frame. Keep each one until
            // presentation time catches up, rather than cutting a windup short on receipt.
            if (snapshotCount == Capacity)
            {
                Array.Copy(snapshots, 1, snapshots, 0, Capacity - 1);
                snapshotCount--;
            }
            snapshots[snapshotCount++] = state;
            Evaluate(now);
        }

        public SumoCombatState Evaluate(double now)
        {
            if (inputCount > 0 && now - inputs[0].At > PredictionTimeout)
            {
                inputCount = 0;
                visual = authority;
            }
            int consumed = 0;
            while (consumed < snapshotCount && snapshots[consumed].Since <= now)
                authority = visual = snapshots[consumed++];
            if (consumed > 0)
            {
                Array.Copy(snapshots, consumed, snapshots, 0, snapshotCount - consumed);
                snapshotCount -= consumed;
            }
            Advance(ref visual, now);
            return visual;
        }

        private void Reconcile(double now)
        {
            visual = authority;
            for (int i = 0; i < inputCount; i++)
            {
                var input = inputs[i];
                double at = Math.Max(input.At, authority.Since);
                Advance(ref visual, at);
                SumoCombatRules.Command(ref visual, input.Command, input.Yaw, at, config);
            }
            Advance(ref visual, now);
        }

        private void Advance(ref SumoCombatState state, double now)
        {
            // At most Charge -> Windup -> Recovery -> Idle. Advance using phase deadlines
            // so a late confirmation cannot restart a locally displayed swing.
            for (int i = 0; i < 3; i++)
            {
                if (state.Phase == SumoCombatPhase.Windup && now >= state.Until)
                    SumoCombatRules.Recover(ref state, state.Until, config);
                else if (state.Until > 0 && now >= state.Until)
                    SumoCombatRules.Advance(ref state, state.Until, config);
                else break;
            }
            SumoCombatRules.Advance(ref state, now, config);
        }
    }
}
