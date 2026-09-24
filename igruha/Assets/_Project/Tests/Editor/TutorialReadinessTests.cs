using Igruha.Core.Minigame;
using NUnit.Framework;

namespace Igruha.Tests
{
    public sealed class TutorialReadinessTests
    {
        [Test]
        public void HostCannotStartForAnotherPlayer()
        {
            var state = new TutorialReadiness();
            state.Add(0); state.Add(37);
            Assert.That(state.SetReady(0, true), Is.True);
            Assert.That(state.AllReady, Is.False);
            state.SetReady(37, true);
            Assert.That(state.AllReady, Is.True);
        }

        [Test]
        public void UnknownSenderAndDuplicateClicksCannotCompleteReadiness()
        {
            var state = new TutorialReadiness();
            state.Add(12); state.Add(91);
            state.SetReady(12, true);
            Assert.That(state.SetReady(12, true), Is.False);
            Assert.That(state.SetReady(1234, true), Is.False);
            Assert.That(state.AllReady, Is.False);
        }

        [Test]
        public void ReadinessCanBeCancelledWhileWaiting()
        {
            var state = new TutorialReadiness();
            state.Add(0); state.Add(1);
            state.SetReady(0, true);
            state.SetReady(0, false);
            state.SetReady(1, true);
            Assert.That(state.AllReady, Is.False);
            Assert.That(state.IsReady(0), Is.False);
        }

        [Test]
        public void DisconnectedPlayerStopsBlockingRemainingReadyPlayers()
        {
            var state = new TutorialReadiness();
            state.Add(0); state.Add(5); state.Add(9);
            state.SetReady(0, true); state.SetReady(9, true);
            Assert.That(state.AllReady, Is.False);
            Assert.That(state.Remove(5), Is.True);
            Assert.That(state.AllReady, Is.True);
        }

        [Test]
        public void EmptyLobbyNeverStartsAndNewGameResetsVotes()
        {
            var state = new TutorialReadiness();
            Assert.That(state.AllReady, Is.False);
            state.Add(20, true);
            state.Reset();
            state.Add(20);
            Assert.That(state.IsReady(20), Is.False);
            state.Remove(20);
            Assert.That(state.AllReady, Is.False);
        }

        [Test]
        public void EightPlayersWithSparseIdsMustAllConfirm()
        {
            var state = new TutorialReadiness();
            for (int i = 0; i < 8; i++) state.Add(100+i*13);
            for (int i = 0; i < 7; i++) state.SetReady(100+i*13, true);
            Assert.That(state.AllReady, Is.False);
            state.SetReady(191,true);
            Assert.That(state.AllReady, Is.True);
        }

        [Test]
        public void LateSnapshotReplacesRosterWithoutKeepingStaleReadiness()
        {
            var state = new TutorialReadiness();
            state.Add(33, true);
            state.Apply(new[] {new TutorialParticipant(71,true), new TutorialParticipant(93)});
            Assert.That(state.Participants.Count, Is.EqualTo(2));
            Assert.That(state.IsReady(33), Is.False);
            Assert.That(state.IsReady(71), Is.True);
            Assert.That(state.AllReady, Is.False);
        }
    }
}
