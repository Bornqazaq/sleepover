using System;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Core.Hub.Activities
{
    public enum StarshipPhase : byte { Attract, Ready, Playing, GameOver }
    public enum StarshipEvent : byte { None, Start, Shot, Hit, LifeLost, Wave, GameOver }

    /// <summary>Fixed-size screen snapshot; no per-bullet NetworkObjects or managed arrays.</summary>
    public struct StarshipState : INetworkSerializable, IEquatable<StarshipState>
    {
        public uint Session, Tick, EventId, Enemies, ShotsFired;
        public StarshipPhase Phase;
        public StarshipEvent Event;
        public int Score, Wave, Lives;
        public float Ship, FormationX, FormationY, Direction, FireCooldown, BombCooldown, Shield;
        public byte Shots, Bombs;
        public Vector2 Shot0, Shot1, Shot2, Bomb0, Bomb1, Bomb2;

        public Vector2 Projectile(bool enemy, int i) => enemy ?
            (i == 0 ? Bomb0 : i == 1 ? Bomb1 : Bomb2) : (i == 0 ? Shot0 : i == 1 ? Shot1 : Shot2);
        public void SetProjectile(bool enemy, int i, Vector2 value)
        {
            if (enemy) { if (i == 0) Bomb0 = value; else if (i == 1) Bomb1 = value; else Bomb2 = value; }
            else { if (i == 0) Shot0 = value; else if (i == 1) Shot1 = value; else Shot2 = value; }
        }
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Session); s.SerializeValue(ref Tick); s.SerializeValue(ref EventId);
            s.SerializeValue(ref Enemies); s.SerializeValue(ref ShotsFired);
            s.SerializeValue(ref Phase); s.SerializeValue(ref Event);
            s.SerializeValue(ref Score); s.SerializeValue(ref Wave); s.SerializeValue(ref Lives);
            s.SerializeValue(ref Ship); s.SerializeValue(ref FormationX); s.SerializeValue(ref FormationY);
            s.SerializeValue(ref Direction); s.SerializeValue(ref FireCooldown); s.SerializeValue(ref BombCooldown);
            s.SerializeValue(ref Shield); s.SerializeValue(ref Shots); s.SerializeValue(ref Bombs);
            s.SerializeValue(ref Shot0); s.SerializeValue(ref Shot1); s.SerializeValue(ref Shot2);
            s.SerializeValue(ref Bomb0); s.SerializeValue(ref Bomb1); s.SerializeValue(ref Bomb2);
        }
        public bool Equals(StarshipState o) => Session == o.Session && Tick == o.Tick && EventId == o.EventId &&
            Enemies == o.Enemies && ShotsFired == o.ShotsFired && Phase == o.Phase && Event == o.Event &&
            Score == o.Score && Wave == o.Wave && Lives == o.Lives && Ship == o.Ship &&
            FormationX == o.FormationX && FormationY == o.FormationY && Direction == o.Direction &&
            FireCooldown == o.FireCooldown && BombCooldown == o.BombCooldown && Shield == o.Shield &&
            Shots == o.Shots && Bombs == o.Bombs && Shot0 == o.Shot0 && Shot1 == o.Shot1 && Shot2 == o.Shot2 &&
            Bomb0 == o.Bomb0 && Bomb1 == o.Bomb1 && Bomb2 == o.Bomb2;
    }
}
