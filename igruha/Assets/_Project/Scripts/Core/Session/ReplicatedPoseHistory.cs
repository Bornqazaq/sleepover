using UnityEngine;

namespace Igruha.Core.Session
{
    /// <summary>Bounded simulation history for comparing replicated objects at the same tick.</summary>
    public sealed class ReplicatedPoseHistory
    {
        private struct Sample { public double Time; public Vector3 Position; public Quaternion Rotation; }
        private readonly Sample[] samples = new Sample[128];
        private int next, count;

        public void Clear() { next = count = 0; }

        public void Record(double time, Vector3 position, Quaternion rotation)
        {
            int previous = (next + samples.Length - 1) % samples.Length;
            if (count > 0 && time < samples[previous].Time) Clear();
            if (count > 0 && time == samples[previous].Time)
            {
                samples[previous] = new Sample { Time = time, Position = position, Rotation = rotation };
                return;
            }
            samples[next] = new Sample { Time = time, Position = position, Rotation = rotation };
            next = (next + 1) % samples.Length;
            count = Mathf.Min(count + 1, samples.Length);
        }

        public bool TrySample(double time, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (count == 0) return false;
            Sample newer = samples[(next + samples.Length - 1) % samples.Length];
            for (int i = 2; i <= count && time < newer.Time; i++)
            {
                Sample older = samples[(next + samples.Length - i) % samples.Length];
                if (time >= older.Time)
                {
                    float t = (float)((time - older.Time) / (newer.Time - older.Time));
                    position = Vector3.Lerp(older.Position, newer.Position, t);
                    rotation = Quaternion.Slerp(older.Rotation, newer.Rotation, t);
                    return true;
                }
                newer = older;
            }
            position = newer.Position;
            rotation = newer.Rotation;
            return true;
        }
    }
}
