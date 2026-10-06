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
        [TestCase(SumoAttack.Dash, SumoContact.Block)]
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
        public void DashCommitsDirectionAndCannotRefreshOrBypassCooldown()
        {
            var s = Guard();
            Assert.That(SumoCombatRules.Command(ref s, SumoCommand.Dash, 90, 2, config), Is.True);
            double expiry = s.NextDashAt;
            foreach (var command in new[] { SumoCommand.Aim, SumoCommand.Cancel, SumoCommand.AttackUp, SumoCommand.GuardUp, SumoCommand.Dash })
                Assert.That(SumoCombatRules.Command(ref s, command, 180, 2.1, config), Is.False);
            Assert.That(s.Yaw, Is.EqualTo(90)); Assert.That(s.NextDashAt, Is.EqualTo(expiry));
            SumoCombatRules.Advance(ref s, s.DashAt, config); Assert.That(s.Phase, Is.EqualTo(SumoCombatPhase.Dash));
            SumoCombatRules.BlockDash(ref s, 2.6, config);
            Assert.That(s.Until, Is.EqualTo(2.6 + config.DashBlockedRecovery).Within(.0001));
            SumoCombatRules.Advance(ref s, 4, config);
            Assert.That(SumoCombatRules.Command(ref s, SumoCommand.Dash, 0, 4, config), Is.False);
            Assert.That(SumoCombatRules.Command(ref s, SumoCommand.Dash, 0, expiry + .01, config), Is.True);
        }
        [Test]
        public void DashCanBeParriedOrHitFromBehindAndCounterTakesPriorityOverCooldown()
        {
            var a = SumoCombatState.Create(1); a.Attack = SumoAttack.Dash; var d = Guard();
            Assert.That(SumoCombatRules.Contact(a, d, Vector3.back, true, 1.1, config), Is.EqualTo(SumoContact.Parry));
            Assert.That(SumoCombatRules.Contact(a, d, Vector3.forward, true, 1.1, config), Is.EqualTo(SumoContact.Push));
            d.NextDashAt = 10;
            SumoCombatRules.AwardCounter(ref d, 1, 1.1, config);
            Assert.That(SumoCombatRules.Command(ref d, SumoCommand.Dash, 180, 1.2, config), Is.True);
            Assert.That(d.Attack, Is.EqualTo(SumoAttack.Counter)); Assert.That(d.CounterTarget, Is.EqualTo(1));
            Assert.That(d.NextDashAt, Is.EqualTo(10));
        }
        [Test]
        public void DashVisualUsesFullWindupAndMovementWindowButRejectionClearsPrediction()
        {
            var visual = new SumoCombatVisualState(config, 1);
            var state = SumoCombatState.Create(1);
            SumoCombatRules.Command(ref state, SumoCommand.Dash, 0, 1, config);
            visual.Receive(state, .9, false);
            Assert.That(visual.Evaluate(1.1).Phase, Is.EqualTo(SumoCombatPhase.Windup));
            Assert.That(visual.Evaluate(state.DashAt + .01).Phase, Is.EqualTo(SumoCombatPhase.Dash));
            Assert.That(visual.Evaluate(state.DashAt + config.DashSeconds + .01).Phase, Is.EqualTo(SumoCombatPhase.Recovery));
            visual.Reset(SumoCombatState.Create(1)); visual.Predict(1, SumoCommand.Dash, 0, 1);
            var rejected = SumoCombatState.Create(1); rejected.ProcessedInput = rejected.Revision = 1;
            visual.Receive(rejected, 1.2, true);
            Assert.That(visual.Evaluate(1.2).Phase, Is.EqualTo(SumoCombatPhase.Idle));
            Assert.That(visual.Evaluate(1.8).Phase, Is.EqualTo(SumoCombatPhase.Idle));
        }

        [Test]
        public void SectorSeamKeepsFootingButAirAndMissingFloorDoNot()
        {
            var left = new GameObject("left-floor"); var right = new GameObject("right-floor");
            try
            {
                int layer = LayerMask.NameToLayer("Ground"); Assert.That(layer, Is.GreaterThanOrEqualTo(0));
                left.layer = right.layer = layer;
                left.transform.position = new Vector3(-.51f, -.05f, 0); right.transform.position = new Vector3(.51f, -.05f, 0);
                left.AddComponent<BoxCollider>().size = right.AddComponent<BoxCollider>().size = new Vector3(1, .1f, 4);
                Physics.SyncTransforms(); int mask = 1 << layer;
                Assert.That(Physics.Raycast(Vector3.up * .18f, Vector3.down, .38f, mask), Is.False, "centre ray falls through the seam");
                Assert.That(SumoCombat.HasGroundSupport(Vector3.zero, mask), Is.True);
                Assert.That(SumoCombat.HasGroundSupport(Vector3.up * .6f, mask), Is.False);
                Assert.That(SumoCombat.HasGroundSupport(Vector3.right * 2, mask), Is.False);
            }
            finally { Object.DestroyImmediate(left); Object.DestroyImmediate(right); }
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
