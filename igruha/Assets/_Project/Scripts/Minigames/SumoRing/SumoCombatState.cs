using System;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Minigames.SumoRing
{
    public enum SumoCombatPhase : byte { Idle, Guard, Charge, Windup, Recovery, Stagger }
    public enum SumoAttack : byte { Quick, Heavy, Counter }
    public enum SumoCommand : byte { AttackDown, AttackUp, GuardDown, GuardUp, Aim, Cancel }
    public enum SumoContact : byte { Miss, Push, Block, Parry, GuardBreak, Counter }

    /// <summary>Server-owned combat snapshot. Times use NetworkClock; no frame timers are replicated.</summary>
    public struct SumoCombatState : INetworkSerializable, IEquatable<SumoCombatState>
    {
        public int Id, CounterTarget, Revision, ProcessedInput;
        public SumoCombatPhase Phase;
        public SumoAttack Attack;
        public double Since, Until, ParryUntil, NextParryAt, CounterUntil;
        public float Yaw;
        public bool HasCounter(double now) => CounterTarget >= 0 && now <= CounterUntil;
        public Vector3 Forward => Quaternion.Euler(0, Yaw, 0) * Vector3.forward;
        public static SumoCombatState Create(int id) => new SumoCombatState { Id = id, CounterTarget = -1 };
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Id); s.SerializeValue(ref CounterTarget); s.SerializeValue(ref Revision); s.SerializeValue(ref ProcessedInput);
            s.SerializeValue(ref Phase); s.SerializeValue(ref Attack); s.SerializeValue(ref Since); s.SerializeValue(ref Until);
            s.SerializeValue(ref ParryUntil); s.SerializeValue(ref NextParryAt); s.SerializeValue(ref CounterUntil); s.SerializeValue(ref Yaw);
        }
        public bool Equals(SumoCombatState other) => Id == other.Id && Revision == other.Revision;
    }

    /// <summary>Deterministic rules, shared by solo and server. Positions/grounding are validated by the adapter.</summary>
    public static class SumoCombatRules
    {
        public static bool Command(ref SumoCombatState s, SumoCommand command, float yaw, double now, SumoConfig c)
        {
            if (float.IsNaN(yaw) || float.IsInfinity(yaw)) return false;
            if (command == SumoCommand.Cancel)
            {
                if (s.Phase == SumoCombatPhase.Guard || s.Phase == SumoCombatPhase.Charge)
                { SetPhase(ref s, SumoCombatPhase.Idle, now, 0); return true; }
                return false;
            }
            if (command == SumoCommand.Aim)
            {
                if (s.Phase != SumoCombatPhase.Guard && s.Phase != SumoCombatPhase.Charge) return false;
                s.Yaw = Mathf.Repeat(yaw, 360); s.Revision++; return true;
            }
            if (command == SumoCommand.GuardUp)
            {
                if (s.Phase != SumoCombatPhase.Guard) return false;
                SetPhase(ref s, SumoCombatPhase.Idle, now, 0); return true;
            }
            if (command == SumoCommand.AttackUp)
            {
                if (s.Phase != SumoCombatPhase.Charge) return false;
                var attack = now - s.Since >= c.ChargeSeconds ? SumoAttack.Heavy : SumoAttack.Quick;
                BeginAttack(ref s, attack, yaw, now, c); return true;
            }
            if (s.Phase != SumoCombatPhase.Idle && s.Phase != SumoCombatPhase.Guard) return false;
            if (command == SumoCommand.AttackDown)
            {
                if (s.HasCounter(now)) BeginAttack(ref s, SumoAttack.Counter, yaw, now, c);
                else { s.CounterTarget = -1; s.Yaw = Mathf.Repeat(yaw, 360); SetPhase(ref s, SumoCombatPhase.Charge, now, now + c.MaximumChargeSeconds); }
                return true;
            }
            if (command != SumoCommand.GuardDown || s.Phase == SumoCombatPhase.Guard) return false;
            s.Yaw = Mathf.Repeat(yaw, 360);
            s.ParryUntil = now >= s.NextParryAt ? now + c.ParryWindow : 0;
            if (s.ParryUntil > 0) s.NextParryAt = now + c.ParryCooldown;
            SetPhase(ref s, SumoCombatPhase.Guard, now, 0); return true;
        }
        private static void BeginAttack(ref SumoCombatState s, SumoAttack attack, float yaw, double now, SumoConfig c)
        {
            s.Attack = attack; s.Yaw = Mathf.Repeat(yaw, 360);
            if (attack != SumoAttack.Counter) s.CounterTarget = -1;
            s.CounterUntil = 0;
            double delay = attack == SumoAttack.Heavy ? c.HeavyWindup : attack == SumoAttack.Counter ? c.CounterWindup : c.QuickWindup;
            SetPhase(ref s, SumoCombatPhase.Windup, now, now + delay);
        }
        public static bool Advance(ref SumoCombatState s, double now, SumoConfig c)
        {
            if (s.Phase == SumoCombatPhase.Charge && now >= s.Until)
            { BeginAttack(ref s, SumoAttack.Heavy, s.Yaw, now, c); return true; }
            if ((s.Phase == SumoCombatPhase.Recovery || s.Phase == SumoCombatPhase.Stagger) && now >= s.Until)
            { SetPhase(ref s, SumoCombatPhase.Idle, now, 0); return true; }
            if (s.Phase != SumoCombatPhase.Windup && s.CounterTarget >= 0 && now > s.CounterUntil)
            { s.CounterTarget = -1; s.Revision++; return true; }
            return false;
        }
        public static bool IsFrontal(Vector3 facing, Vector3 towardsAttacker, float arc)
        {
            facing.y = towardsAttacker.y = 0;
            return towardsAttacker.sqrMagnitude > .0001f && Vector3.Dot(facing.normalized, towardsAttacker.normalized) >= Mathf.Cos(arc * .5f * Mathf.Deg2Rad);
        }
        public static SumoContact Contact(in SumoCombatState attacker, in SumoCombatState target, Vector3 towardsAttacker, bool grounded, double now, SumoConfig c)
        {
            bool guard = grounded && target.Phase == SumoCombatPhase.Guard && IsFrontal(target.Forward, towardsAttacker, c.GuardArc);
            if (guard && now <= target.ParryUntil) return SumoContact.Parry;
            if (guard) return attacker.Attack == SumoAttack.Quick ? SumoContact.Block : SumoContact.GuardBreak;
            return attacker.Attack == SumoAttack.Counter ? SumoContact.Counter : SumoContact.Push;
        }
        public static void Recover(ref SumoCombatState s, double now, SumoConfig c)
        {
            s.CounterTarget = -1;
            SetPhase(ref s, SumoCombatPhase.Recovery, now, now + (s.Attack == SumoAttack.Heavy ? c.HeavyRecoverySeconds : c.RecoverySeconds));
        }
        public static void Stagger(ref SumoCombatState s, double now, float duration)
        { s.CounterTarget = -1; s.CounterUntil = 0; s.ParryUntil = 0; SetPhase(ref s, SumoCombatPhase.Stagger, now, now + duration); }
        public static void AwardCounter(ref SumoCombatState defender, int attackerId, double now, SumoConfig c)
        {
            defender.CounterTarget = attackerId; defender.CounterUntil = now + c.CounterWindow;
            defender.ParryUntil = 0; defender.Revision++;
        }
        private static void SetPhase(ref SumoCombatState s, SumoCombatPhase phase, double since, double until)
        { s.Phase = phase; s.Since = since; s.Until = until; s.Revision++; }
    }
}
