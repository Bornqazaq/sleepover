using System;
using Unity.Netcode;

namespace Igruha.Minigames.Circus
{
    /// <summary>Phase and its start share one network value, so a late packet
    /// seeks the authored action instead of restarting its anticipation.</summary>
    public struct CircusBearSnapshot : INetworkSerializable, IEquatable<CircusBearSnapshot>
    {
        public byte State;
        public double StartedAt;
        public int TargetId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref State);
            serializer.SerializeValue(ref StartedAt);
            serializer.SerializeValue(ref TargetId);
        }

        public bool Equals(CircusBearSnapshot other) => State == other.State && StartedAt.Equals(other.StartedAt) && TargetId == other.TargetId;
    }
}
