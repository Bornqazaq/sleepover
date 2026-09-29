using Igruha.Minigames.SumoRing;
using NUnit.Framework;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class SumoCombatTests
    {
        private SumoConfig config;
        [SetUp] public void SetUp() => config = ScriptableObject.CreateInstance<SumoConfig>();
        [TearDown] public void TearDown() => Object.DestroyImmediate(config);
        private SumoCombatState Guard(double at = 1)
        { var s = SumoCombatState.Create(2); SumoCombatRules.Command(ref s, SumoCommand.GuardDown, 180, at, config); return s; }
        [Test]
        public void HoldingGuardDoesNotRefreshParryAndRepressCannotBypassCooldown()
        {
            var s = Guard();
            Assert.That(SumoCombatRules.Command(ref s, SumoCommand.GuardDown, 180, 1.1, config), Is.False);
            Assert.That(s.ParryUntil, Is.EqualTo(1.2).Within(.001));
            SumoCombatRules.Command(ref s, SumoCommand.GuardUp, 180, 1.11, config);
            SumoCombatRules.Command(ref s, SumoCommand.GuardDown, 180, 1.12, config);
            Assert.That(s.ParryUntil, Is.Zero);
            SumoCombatRules.Command(ref s, SumoCommand.GuardUp, 180, 1.7, config);
            SumoCombatRules.Command(ref s, SumoCommand.GuardDown, 180, 1.71, config);
            Assert.That(s.ParryUntil, Is.GreaterThan(1.71));
        }
        [TestCase(SumoAttack.Quick, SumoContact.Block)]
        [TestCase(SumoAttack.Heavy, SumoContact.GuardBreak)]
        [TestCase(SumoAttack.Counter, SumoContact.GuardBreak)]
        public void HeldGuardHasDifferentAnswersToQuickAndCommittedAttacks(SumoAttack attack, SumoContact result)
        {
            var a = SumoCombatState.Create(1); a.Attack = attack; var d = Guard();
            Assert.That(SumoCombatRules.Contact(a, d, Vector3.back, true, 1.3, config), Is.EqualTo(result));
        }
        [Test]
        public void ParryStopsHeavyButCannotProtectBackOrAirborneDefender()
        {
            var a = SumoCombatState.Create(1); a.Attack = SumoAttack.Heavy; var d = Guard();
            Assert.That(SumoCombatRules.Contact(a, d, Vector3.back, true, 1.1, config), Is.EqualTo(SumoContact.Parry));
            Assert.That(SumoCombatRules.Contact(a, d, Vector3.forward, true, 1.1, config), Is.EqualTo(SumoContact.Push));
            Assert.That(SumoCombatRules.Contact(a, d, Vector3.back, false, 1.1, config), Is.EqualTo(SumoContact.Push));
        }
        [Test]
        public void CounterIsManualExpiresAndCannotBeReused()
        {
            var s = Guard(); SumoCombatRules.AwardCounter(ref s, 7, 1.1, config);
            Assert.That(s.Phase, Is.EqualTo(SumoCombatPhase.Guard));
            SumoCombatRules.Command(ref s, SumoCommand.AttackDown, 180, 1.4, config);
            Assert.That(s.Attack, Is.EqualTo(SumoAttack.Counter)); Assert.That(s.CounterTarget, Is.EqualTo(7));
            Assert.That(SumoCombatRules.Command(ref s, SumoCommand.AttackDown, 180, 1.41, config), Is.False);
            SumoCombatRules.Recover(ref s, 1.6, config); Assert.That(s.CounterTarget, Is.EqualTo(-1));
            s = Guard(); SumoCombatRules.AwardCounter(ref s, 7, 1.1, config);
            SumoCombatRules.Command(ref s, SumoCommand.AttackDown, 180, 1.7, config);
            Assert.That(s.Phase, Is.EqualTo(SumoCombatPhase.Charge)); Assert.That(s.HasCounter(1.7), Is.False);
        }
        [Test]
        public void ServerChargeTimeCannotBeSuppliedByClientOrExtendedByRepeatedPress()
        {
            var s = SumoCombatState.Create(1);
            SumoCombatRules.Command(ref s, SumoCommand.AttackDown, 0, 2, config);
            Assert.That(SumoCombatRules.Command(ref s, SumoCommand.AttackDown, 0, 2.3, config), Is.False);
            SumoCombatRules.Command(ref s, SumoCommand.AttackUp, 0, 2.4, config); Assert.That(s.Attack, Is.EqualTo(SumoAttack.Quick));
            s = SumoCombatState.Create(1); SumoCombatRules.Command(ref s, SumoCommand.AttackDown, 0, 2, config);
            SumoCombatRules.Command(ref s, SumoCommand.AttackUp, 0, 2.8, config); Assert.That(s.Attack, Is.EqualTo(SumoAttack.Heavy));
        }
        [Test]
        public void FocusCancellationDiscardsChargeInsteadOfFiringIt()
        {
            var s = SumoCombatState.Create(1); SumoCombatRules.Command(ref s, SumoCommand.AttackDown, 0, 1, config);
            SumoCombatRules.Command(ref s, SumoCommand.Cancel, 0, 1.8, config);
            Assert.That(s.Phase, Is.EqualTo(SumoCombatPhase.Idle));
            Assert.That(SumoCombatRules.Command(ref s, SumoCommand.AttackUp, 0, 2, config), Is.False);
        }
        [Test]
        public void MissRecoveryAndStaggerCannotBeCancelledIntoNewAttack()
        {
            var s = SumoCombatState.Create(1); SumoCombatRules.Recover(ref s, 1, config);
            Assert.That(SumoCombatRules.Command(ref s, SumoCommand.AttackDown, 0, 1.1, config), Is.False);
            Assert.That(SumoCombatRules.Command(ref s, SumoCommand.GuardDown, 0, 1.1, config), Is.False);
            SumoCombatRules.Stagger(ref s, 1.2, config.ParriedStaggerSeconds);
            Assert.That(SumoCombatRules.Command(ref s, SumoCommand.Cancel, 0, 1.3, config), Is.False);
            SumoCombatRules.Advance(ref s, 2, config);
            Assert.That(SumoCombatRules.Command(ref s, SumoCommand.AttackDown, 0, 2, config), Is.True);
        }
        [Test]
        public void InvalidAimCannotPoisonStateAndAimDoesNotExtendParry()
        {
            var s = Guard(); int revision = s.Revision;
            Assert.That(SumoCombatRules.Command(ref s, SumoCommand.Aim, float.NaN, 1.1, config), Is.False);
            Assert.That(s.Revision, Is.EqualTo(revision));
            SumoCombatRules.Command(ref s, SumoCommand.Aim, 170, 1.15, config);
            Assert.That(s.ParryUntil, Is.EqualTo(1.2).Within(.001)); Assert.That(s.Yaw, Is.EqualTo(170));
        }
        [Test]
        public void AdjacentCapsulesAreReachableButDifferentFloorsAreNot()
        {
            var a = new GameObject("a"); var b = new GameObject("b");
            try
            {
                var ac = a.AddComponent<CapsuleCollider>(); var bc = b.AddComponent<CapsuleCollider>();
                ac.height = bc.height = 1.8f; ac.radius = bc.radius = .36f;
                b.transform.position = Vector3.forward * 1.1f; Physics.SyncTransforms();
                Assert.That(SumoCombat.BodyGap(ac, bc), Is.LessThan(config.CombatReach));
                b.transform.position += Vector3.up * 3; Physics.SyncTransforms();
                Assert.That(SumoCombat.BodyGap(ac, bc), Is.GreaterThan(config.CombatReach));
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }
    }
}
