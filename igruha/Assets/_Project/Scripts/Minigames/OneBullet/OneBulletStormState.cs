using System;

namespace Igruha.Minigames.OneBullet
{
    /// <summary>Deadlines use the shared round clock. Each queued closure gets a full warning.</summary>
    public sealed class OneBulletStormState
    {
        public int Stage { get; private set; }
        public int Target { get; private set; }
        public double ClosesAt { get; private set; }
        public double IdleAt { get; private set; }
        public bool Warning => Target > Stage;
        public int SafeStage => Warning ? Stage + 1 : Stage;
        public void Reset(int players, int finalStage, double begins, double idle)
        {
            Stage = Target = 0;
            ClosesAt = 0; IdleAt = begins + idle;
        }
        public bool Elimination(double now, int finalStage, double warning, double idle)
        {
            return QueueClosure(Target + 1, now, finalStage, warning, idle);
        }
        private bool QueueClosure(int target, double now, int finalStage, double warning, double idle)
        {
            IdleAt = now + idle;
            if (Target >= finalStage) return false;
            if (!Warning) ClosesAt = now + warning;
            Target = Math.Min(target, finalStage); return true;
        }
        public bool Tick(double now, int finalStage, double warning, double idle)
        {
            if (Warning && now >= ClosesAt)
            {
                Stage++; IdleAt = now + idle;
                ClosesAt = Warning ? now + warning : 0;
                return true;
            }
            if (!Warning && Stage < finalStage && now >= IdleAt)
                return QueueClosure(Target + 1, now, finalStage, warning, idle);
            return false;
        }
        public void Apply(int stage, int target, double closes, double idle)
        { Stage = stage; Target = target; ClosesAt = closes; IdleAt = idle; }
    }
}
