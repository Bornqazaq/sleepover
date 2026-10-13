using System;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Core.Hub.Activities
{
    public enum ArkanoidPhase : byte { Attract, Ready, Playing, GameOver }
    public enum ArkanoidEvent : byte { None, Launch, Bounce, Brick, LifeLost, Level, GameOver }

    public struct ArkanoidState : INetworkSerializable, IEquatable<ArkanoidState>
    {
        public uint Session, Tick, EventId;
        public ArkanoidPhase Phase;
        public ArkanoidEvent Event;
        public Vector2 Ball, Velocity;
        public float Paddle;
        public ulong Bricks;
        public int Score, Level, Lives, Hits;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Session); s.SerializeValue(ref Tick); s.SerializeValue(ref EventId);
            s.SerializeValue(ref Phase); s.SerializeValue(ref Event);
            s.SerializeValue(ref Ball); s.SerializeValue(ref Velocity); s.SerializeValue(ref Paddle);
            s.SerializeValue(ref Bricks); s.SerializeValue(ref Score); s.SerializeValue(ref Level);
            s.SerializeValue(ref Lives); s.SerializeValue(ref Hits);
        }

        public bool Equals(ArkanoidState other) => Session == other.Session && Tick == other.Tick &&
            EventId == other.EventId && Phase == other.Phase && Event == other.Event &&
            Ball == other.Ball && Velocity == other.Velocity && Paddle == other.Paddle &&
            Bricks == other.Bricks && Score == other.Score && Level == other.Level &&
            Lives == other.Lives && Hits == other.Hits;
    }
}
