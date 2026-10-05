using Igruha.Minigames.SumoRing;
using NUnit.Framework;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class SumoCombatVisualTests
    {
        private SumoConfig config;
        private SumoCombatVisualState visual;
        [SetUp] public void SetUp()
        {
            config = ScriptableObject.CreateInstance<SumoConfig>();
            visual = new SumoCombatVisualState(config, 1);
        }
        [TearDown] public void TearDown() => Object.DestroyImmediate(config);

        [Test]
        public void OwnerStartsGuardAndSwingWithoutRoundTrip()
        {
            visual.Predict(1, SumoCommand.GuardDown, 90, 1);
            Assert.That(visual.Evaluate(1).Phase, Is.EqualTo(SumoCombatPhase.Guard));
            visual.Predict(2, SumoCommand.GuardUp, 90, 1.1);
            Assert.That(visual.Evaluate(1.1).Phase, Is.EqualTo(SumoCombatPhase.Idle));
            visual.Predict(3, SumoCommand.AttackDown, 90, 2);
            visual.Predict(4, SumoCommand.AttackUp, 90, 2.02);
            Assert.That(visual.Evaluate(2.02).Phase, Is.EqualTo(SumoCombatPhase.Windup));
            Assert.That(visual.Evaluate(2.02 + config.QuickWindup + .01).Phase, Is.EqualTo(SumoCombatPhase.Recovery));
        }

        [Test]
        public void PartialAcknowledgementReplaysReleaseAndLateConfirmationDoesNotRestartSwing()
        {
            visual.Predict(1, SumoCommand.AttackDown, 0, 1);
            visual.Predict(2, SumoCommand.AttackUp, 0, 1.02);
            var server = SumoCombatState.Create(1);
            SumoCombatRules.Command(ref server, SumoCommand.AttackDown, 0, 1, config);
            server.ProcessedInput = 1;
            visual.Receive(server, 1.15, true);
            Assert.That(visual.Evaluate(1.15).Since, Is.EqualTo(1.02).Within(.0001));
            SumoCombatRules.Command(ref server, SumoCommand.AttackUp, 0, 1.02, config);
            server.ProcessedInput = 2;
            double late = 1.02 + config.QuickWindup + .1;
            visual.Receive(server, late, true);
            var shown = visual.Evaluate(late);
            Assert.That(shown.Phase, Is.EqualTo(SumoCombatPhase.Recovery));
            Assert.That(shown.Since, Is.EqualTo(server.Until).Within(.0001));
            Assert.That(visual.Evaluate(late + config.RecoverySeconds).Phase, Is.EqualTo(SumoCombatPhase.Idle));
        }

        [Test]
        public void AcknowledgedRejectionClearsPredictedGuard()
        {
            visual.Predict(1, SumoCommand.GuardDown, 0, 1);
            var rejected = SumoCombatState.Create(1);
            rejected.ProcessedInput = 1; rejected.Revision = 1;
            visual.Receive(rejected, 1.2, true);
            Assert.That(visual.Evaluate(1.2).Phase, Is.EqualTo(SumoCombatPhase.Idle));
        }

        [Test]
        public void AuthoritativeStaggerOverridesUnacknowledgedAttack()
        {
            visual.Predict(1, SumoCommand.AttackDown, 0, 1);
            visual.Predict(2, SumoCommand.AttackUp, 0, 1.02);
            var server = SumoCombatState.Create(1);
            SumoCombatRules.Stagger(ref server, 1.03, .4f);
            visual.Receive(server, 1.2, true);
            Assert.That(visual.Evaluate(1.2).Phase, Is.EqualTo(SumoCombatPhase.Stagger));
            Assert.That(visual.Evaluate(1.5).Phase, Is.EqualTo(SumoCombatPhase.Idle));
            SumoCombatRules.Advance(ref server, 1.5, config);
            visual.Receive(server, 1.6, true);
            Assert.That(visual.Evaluate(1.6).Phase, Is.EqualTo(SumoCombatPhase.Idle), "An old input cannot restart after stagger ends");
        }

        [Test]
        public void CoalescedRemoteSnapshotsWaitForPresentationClock()
        {
            var server = SumoCombatState.Create(1);
            SumoCombatRules.Command(ref server, SumoCommand.AttackDown, 0, 1, config);
            visual.Receive(server, .9, false);
            SumoCombatRules.Command(ref server, SumoCommand.AttackUp, 0, 1.02, config);
            visual.Receive(server, .9, false);
            double impact = server.Until;
            SumoCombatRules.Recover(ref server, impact, config);
            visual.Receive(server, 1.1, false);
            var shown = visual.Evaluate(impact - .01);
            Assert.That(shown.Phase, Is.EqualTo(SumoCombatPhase.Windup));
            Assert.That(shown.Until, Is.EqualTo(impact));
            Assert.That(visual.Evaluate(impact + .01).Phase, Is.EqualTo(SumoCombatPhase.Recovery));
        }

        [Test]
        public void RemoteWindupFinishesWhileWaitingForRecoveryPacket()
        {
            var server = SumoCombatState.Create(1);
            SumoCombatRules.Command(ref server, SumoCommand.AttackDown, 0, 1, config);
            SumoCombatRules.Command(ref server, SumoCommand.AttackUp, 0, 1.02, config);
            visual.Receive(server, 1.1, false);
            Assert.That(visual.Evaluate(server.Until + .1).Phase, Is.EqualTo(SumoCombatPhase.Recovery));
            Assert.That(visual.Evaluate(server.Until + config.RecoverySeconds + .1).Phase, Is.EqualTo(SumoCombatPhase.Idle));
        }

        [Test]
        public void CancelAndConnectionTimeoutCannotLeaveGuardOrChargeStuck()
        {
            visual.Predict(1, SumoCommand.AttackDown, 0, 1);
            visual.Predict(2, SumoCommand.Cancel, 0, 1.1);
            Assert.That(visual.Evaluate(1.2).Phase, Is.EqualTo(SumoCombatPhase.Idle));
            visual.Predict(3, SumoCommand.GuardDown, 0, 2);
            Assert.That(visual.Evaluate(2 + SumoCombatVisualState.PredictionTimeout + .01).Phase, Is.EqualTo(SumoCombatPhase.Idle));
        }

        [Test]
        public void ResetAcceptsNewRoundAndOldRevisionCannotOverwriteNewerState()
        {
            var server = SumoCombatState.Create(1);
            SumoCombatRules.Command(ref server, SumoCommand.GuardDown, 0, 1, config);
            var old = server;
            SumoCombatRules.Command(ref server, SumoCommand.GuardUp, 0, 1.1, config);
            visual.Receive(server, 1.2, true);
            visual.Receive(old, 1.2, true);
            Assert.That(visual.Evaluate(1.2).Phase, Is.EqualTo(SumoCombatPhase.Idle));
            visual.Reset(SumoCombatState.Create(1));
            visual.Receive(old, 1.2, true);
            Assert.That(visual.Evaluate(1.2).Phase, Is.EqualTo(SumoCombatPhase.Guard));
        }
    }
}
