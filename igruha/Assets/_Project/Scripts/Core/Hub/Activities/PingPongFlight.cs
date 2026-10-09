using System;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Core.Hub.Activities
{
    public enum PingPongPhase : byte { Idle, Playing, Missed }

    /// <summary>A complete, timestamped flight. Rendering never decides whether a hit succeeded.</summary>
    [Serializable]
    public struct PingPongFlight : INetworkSerializable, IEquatable<PingPongFlight>
    {
        public uint Id;
        public double StartsAt;
        public float Duration;
        public Vector3 From;
        public Vector3 To;
        public byte Target;
        public int Rally;
        public double ContactAt => StartsAt + Duration;
        public bool Valid => Id != 0 && Duration > 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref StartsAt);
            serializer.SerializeValue(ref Duration);
            serializer.SerializeValue(ref From);
            serializer.SerializeValue(ref To);
            serializer.SerializeValue(ref Target);
            serializer.SerializeValue(ref Rally);
        }

        public bool Equals(PingPongFlight other) => Id == other.Id && StartsAt.Equals(other.StartsAt) &&
            Duration.Equals(other.Duration) && From.Equals(other.From) && To.Equals(other.To) &&
            Target == other.Target && Rally == other.Rally;
    }

    /// <summary>The previous flight keeps a scheduled rebound continuous until its contact time.</summary>
    public struct PingPongState : INetworkSerializable, IEquatable<PingPongState>
    {
        public PingPongPhase Phase;
        public PingPongFlight Previous;
        public PingPongFlight Flight;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref Previous);
            serializer.SerializeValue(ref Flight);
        }

        public bool Equals(PingPongState other) => Phase == other.Phase &&
            Previous.Equals(other.Previous) && Flight.Equals(other.Flight);
    }

    /// <summary>Shared trajectory and bounded timing validation, also used for presentation prediction.</summary>
    public static class PingPongRules
    {
        public const float EarlyWindow = .28f;
        public const float LateWindow = .18f;
        public const float StartDuration = 1.35f;
        public const float MinimumDuration = .9f;
        public const float SpeedStep = .06f;
        public const int ReturnsPerStep = 4;
        public const float BounceFraction = .72f;
        public const float TableHeight = .87f;
        public const float BallRadius = .035f;
        public const float ContactX = 1.55f;
        public const float ContactY = 1.13f;
        public const float BounceX = .88f;
        public const float MaximumRewind = .55f;
        public const float NetworkSlack = .12f;
        public const float FutureTolerance = .06f;
        private const float LateralRange = .34f;
        private const float FirstArcHeight = .42f;
        private const float SecondArcHeight = .075f;
        private const float FlyoutSpeed = 2.8f;
        private const float FlyoutGravity = 5f;

        public static float Duration(int rally) => Mathf.Max(MinimumDuration,
            StartDuration - Mathf.Max(0, rally) / ReturnsPerStep * SpeedStep);

        public static float RewindBudget(float rttSeconds) =>
            Mathf.Clamp(rttSeconds + NetworkSlack, NetworkSlack, MaximumRewind);

        public static bool ValidTimestamp(double stamp, double receivedAt, float rewindBudget) =>
            !double.IsNaN(stamp) && !double.IsInfinity(stamp) &&
            stamp <= receivedAt + FutureTolerance && stamp >= receivedAt - rewindBudget;

        public static bool InWindow(PingPongFlight flight, double stamp) => flight.Valid &&
            stamp >= flight.ContactAt - EarlyWindow && stamp <= flight.ContactAt + LateWindow;

        public static Vector3 Contact(byte side, float z) => new Vector3(side == 0 ? -ContactX : ContactX, ContactY, z);

        public static PingPongFlight Serve(uint id, byte target, double start) => new PingPongFlight
        {
            Id = id, StartsAt = start, Duration = StartDuration, Target = target,
            From = Contact((byte)(1 - target), 0), To = Contact(target, 0), Rally = 0,
        };

        public static PingPongFlight Return(PingPongFlight incoming, uint id) => new PingPongFlight
        {
            Id = id, StartsAt = incoming.ContactAt, Duration = Duration(incoming.Rally + 1),
            Target = (byte)(1 - incoming.Target), Rally = incoming.Rally + 1,
            From = incoming.To,
            To = Contact((byte)(1 - incoming.Target), Mathf.Sin(id * 2.4f) * LateralRange),
        };

        public static PingPongFlight VisibleFlight(PingPongState state, double now) =>
            now < state.Flight.StartsAt && state.Previous.Valid ? state.Previous : state.Flight;

        public static Vector3 Position(PingPongFlight flight, double now)
        {
            if (!flight.Valid) return new Vector3(0, TableHeight + BallRadius, 0);
            float t = (float)((now - flight.StartsAt) / flight.Duration);
            if (t <= 0) return flight.From;
            if (t > 1)
            {
                float elapsed = (float)(now - flight.ContactAt);
                return flight.To + new Vector3(flight.Target == 0 ? -FlyoutSpeed : FlyoutSpeed,
                    0, 0) * elapsed + Vector3.down * (FlyoutGravity * elapsed * elapsed);
            }
            var bounce = new Vector3(flight.Target == 0 ? -BounceX : BounceX,
                TableHeight + BallRadius, Mathf.Lerp(flight.From.z, flight.To.z, BounceFraction));
            bool first = t < BounceFraction;
            float segment = first ? t / BounceFraction : (t - BounceFraction) / (1 - BounceFraction);
            Vector3 result = Vector3.Lerp(first ? flight.From : bounce, first ? bounce : flight.To, segment);
            result.y += 4 * segment * (1 - segment) * (first ? FirstArcHeight : SecondArcHeight);
            return result;
        }
    }
}
