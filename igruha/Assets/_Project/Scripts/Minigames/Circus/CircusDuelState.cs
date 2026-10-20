using System;
using Unity.Netcode;

namespace Igruha.Minigames.Circus
{
    /// <summary>Durable part of the stage snapshot: identical finalists and cue time on every machine.</summary>
    public struct CircusDuelState : INetworkSerializable, IEquatable<CircusDuelState>
    {
        public bool Active;
        public int FirstCage, SecondCage;
        public double StartedAt;

        public const float BlackoutSeconds = .4f;
        public const float SecondSpotAt = 2.65f;
        public const float BothSpotsAt = 4.4f;
        public const float ReturnAt = 5.65f;
        public const float IntroSeconds = 6.4f;
        public const float LaunchTailSeconds = 1.2f;

        public int Cue(double now)
        {
            double elapsed = now - StartedAt;
            if (!Active || elapsed < 0) return -1;
            if (elapsed < BlackoutSeconds) return 0;
            if (elapsed < SecondSpotAt) return 1;
            if (elapsed < BothSpotsAt) return 2;
            if (elapsed < IntroSeconds) return 3;
            return 4;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Active);
            serializer.SerializeValue(ref FirstCage);
            serializer.SerializeValue(ref SecondCage);
            serializer.SerializeValue(ref StartedAt);
        }

        public bool Equals(CircusDuelState other) => Active == other.Active &&
            FirstCage == other.FirstCage && SecondCage == other.SecondCage && StartedAt.Equals(other.StartedAt);
    }
}
