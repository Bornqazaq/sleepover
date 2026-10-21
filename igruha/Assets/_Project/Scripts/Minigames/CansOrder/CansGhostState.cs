using System;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>Two persistent, ordered tricks: late snapshots cannot skip the first swap.</summary>
    public struct CansGhostTrick : INetworkSerializable, IEquatable<CansGhostTrick>
    {
        public byte Serial, FirstSlot, SecondSlot;
        public int OwnerId, TargetId, Circle;
        public double StartedAt;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Serial); s.SerializeValue(ref FirstSlot); s.SerializeValue(ref SecondSlot);
            s.SerializeValue(ref OwnerId); s.SerializeValue(ref TargetId); s.SerializeValue(ref Circle); s.SerializeValue(ref StartedAt);
        }
        public bool Equals(CansGhostTrick v) => Serial == v.Serial && FirstSlot == v.FirstSlot &&
            SecondSlot == v.SecondSlot && TargetId == v.TargetId && Circle == v.Circle && StartedAt == v.StartedAt;
    }

    public struct CansGhostState : INetworkSerializable, IEquatable<CansGhostState>
    {
        public bool Active;
        public int OwnerId;
        public byte Used;
        public double StartedAt, ReadyAt;
        public CansGhostTrick First, Second;
        public int Charges => CansGhostRules.TrickLimit - Used;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Active); s.SerializeValue(ref OwnerId); s.SerializeValue(ref Used);
            s.SerializeValue(ref StartedAt); s.SerializeValue(ref ReadyAt);
            s.SerializeValue(ref First); s.SerializeValue(ref Second);
        }
        public bool Equals(CansGhostState v) => Active == v.Active && OwnerId == v.OwnerId && Used == v.Used &&
            StartedAt == v.StartedAt && ReadyAt == v.ReadyAt && First.Equals(v.First) && Second.Equals(v.Second);
        public byte RequiredRevision(int playerId, int circle)
        {
            if (Second.Serial > 0 && Second.TargetId == playerId && Second.Circle <= circle) return Second.Serial;
            return First.Serial > 0 && First.TargetId == playerId && First.Circle <= circle ? First.Serial : (byte)0;
        }
    }

    public struct CansGhostPose : INetworkSerializable, IEquatable<CansGhostPose>
    {
        public Vector3 Position;
        public float Yaw;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        { s.SerializeValue(ref Position); s.SerializeValue(ref Yaw); }
        public bool Equals(CansGhostPose v) => Position.Equals(v.Position) && Yaw.Equals(v.Yaw);
    }

    public struct CansGhostSnapshot : INetworkSerializable, IEquatable<CansGhostSnapshot>
    {
        public CansGhostState State;
        public CansGhostPose Pose;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        { s.SerializeValue(ref State); s.SerializeValue(ref Pose); }
        public bool Equals(CansGhostSnapshot other) => State.Equals(other.State) && Pose.Equals(other.Pose);
    }

    /// <summary>One server order across all ghosts; swaps sharing a slot do not commute.</summary>
    public sealed class CansGhostTrickHistory
    {
        private const int Capacity = 8 * CansGhostRules.TrickLimit;
        private readonly CansGhostTrick[] records = new CansGhostTrick[Capacity + 1];
        private byte latest, applied;
        public byte NextSerial => (byte)(latest + 1);
        public void Record(CansGhostTrick trick)
        {
            if (trick.Serial == 0 || trick.Serial > Capacity) return;
            records[trick.Serial] = trick;
            if (trick.Serial > latest) latest = trick.Serial;
        }
        public bool TryPeekNext(out CansGhostTrick trick)
        {
            trick = applied < Capacity ? records[applied + 1] : default;
            return trick.Serial != 0;
        }
        public void Consume(CansGhostTrick trick)
        {
            if (trick.Serial == applied + 1) applied = trick.Serial;
        }
        public byte Revision(int playerId, int circle, bool appliedOnly)
        {
            for (int i = appliedOnly ? applied : latest; i > 0; i--)
                if (records[i].Serial != 0 && records[i].TargetId == playerId && records[i].Circle <= circle)
                    return (byte)i;
            return 0;
        }
        public void Clear()
        {
            Array.Clear(records, 0, records.Length);
            latest = applied = 0;
        }
    }

    public static class CansGhostRules
    {
        public const int TrickLimit = 2;
        public const float Cooldown = 8f, ReactionWindow = 3f, Reach = 6f, FlightSpeed = 7f;
        public const float InputTimeout = .4f, NetworkInterval = .05f, SwapSeconds = .7f;

        public static bool CanTrick(CansGhostState ghost, int sender, int target, int circle,
            bool placement, bool alive, bool solved, bool confirmed, int canCount,
            float secondsLeft, float distance, double now)
        {
            int lastCircle = ghost.Used == 2 ? ghost.Second.Circle : ghost.First.Circle;
            return ghost.Active && ghost.OwnerId == sender && target != sender && ghost.Used < TrickLimit &&
                placement && alive && !solved && !confirmed && canCount >= 2 &&
                secondsLeft > ReactionWindow && now >= ghost.ReadyAt && distance <= Reach &&
                (ghost.Used == 0 || lastCircle != circle);
        }

        public static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        public static Vector3 Constrain(Vector3 position, float radius, float ceiling)
        {
            Vector2 xz = Vector2.ClampMagnitude(new Vector2(position.x, position.z), radius);
            return new Vector3(xz.x, Mathf.Clamp(position.y, 1f, ceiling), xz.y);
        }
    }
}
