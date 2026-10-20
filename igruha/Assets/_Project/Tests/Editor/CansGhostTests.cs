using Igruha.Minigames.CansOrder;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class CansGhostTests
    {
        private static CansGhostState Winner => new CansGhostState { Active = true, OwnerId = 4 };
        private static bool Allowed(CansGhostState state, int sender = 4, int target = 2, int circle = 3,
            bool placement = true, bool alive = true, bool solved = false, bool confirmed = false,
            float seconds = 9, float distance = 2, double now = 20) =>
            CansGhostRules.CanTrick(state, sender, target, circle, placement, alive, solved, confirmed, 5, seconds, distance, now);

        [Test]
        public void OnlyWinnerCanInterruptAnUnconfirmedLivingOpponent()
        {
            Assert.That(Allowed(Winner), Is.True);
            Assert.That(Allowed(Winner, sender: 2), Is.False);
            Assert.That(Allowed(Winner, target: 4), Is.False);
            Assert.That(Allowed(Winner, placement: false), Is.False);
            Assert.That(Allowed(Winner, alive: false), Is.False);
            Assert.That(Allowed(Winner, solved: true), Is.False);
            Assert.That(Allowed(Winner, confirmed: true), Is.False);
            Assert.That(Allowed(default), Is.False);
        }

        [Test]
        public void TwoChargesCannotHitTwiceInOneTurnOrInTheLastSeconds()
        {
            var ghost = Winner; ghost.Used = 1;
            ghost.First = new CansGhostTrick { Serial = 1, Circle = 3, TargetId = 2 };
            ghost.ReadyAt = 25;
            Assert.That(Allowed(ghost, circle: 4, now: 24), Is.False);
            Assert.That(Allowed(ghost, circle: 3, now: 30), Is.False);
            Assert.That(Allowed(ghost, circle: 4, now: 30), Is.True);
            Assert.That(Allowed(ghost, circle: 4, now: 30, seconds: 3), Is.False);
            Assert.That(Allowed(ghost, circle: 4, now: 30, distance: 7), Is.False);
            ghost.Used = 2;
            Assert.That(Allowed(ghost, circle: 5, now: 40), Is.False);
        }

        [Test]
        public void BothTricksSurviveSnapshotSerializationAndRevisionsArePrivatePerTarget()
        {
            var ghost = Winner; ghost.Used = 2; ghost.ReadyAt = 35;
            ghost.First = new CansGhostTrick { Serial = 1, TargetId = 2, Circle = 3, FirstSlot = 0, SecondSlot = 4, StartedAt = 20 };
            ghost.Second = new CansGhostTrick { Serial = 2, TargetId = 7, Circle = 4, FirstSlot = 1, SecondSlot = 3, StartedAt = 30 };
            using var writer = new FastBufferWriter(256, Allocator.Temp);
            writer.WriteNetworkSerializable(ghost);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out CansGhostState copy);
            Assert.That(copy.Equals(ghost), Is.True);
            Assert.That(copy.RequiredRevision(2, 2), Is.Zero);
            Assert.That(copy.RequiredRevision(2, 3), Is.EqualTo(1));
            Assert.That(copy.RequiredRevision(2, 8), Is.EqualTo(1), "A delayed swap still changes the persistent arrangement.");
            Assert.That(copy.RequiredRevision(7, 4), Is.EqualTo(2));
            Assert.That(copy.RequiredRevision(4, 4), Is.Zero, "Winner answer never enters the trick payload.");
        }

        [Test]
        public void FlightRejectsNonFiniteInputAndStaysInsideTheArena()
        {
            Assert.That(CansGhostRules.Finite(new Vector3(float.NaN, 0, 0)), Is.False);
            Assert.That(CansGhostRules.Finite(new Vector3(0, float.PositiveInfinity, 0)), Is.False);
            Vector3 point = CansGhostRules.Constrain(new Vector3(1000, 1000, 1000), 12, 16);
            Assert.That(new Vector2(point.x, point.z).magnitude, Is.EqualTo(12).Within(.001));
            Assert.That(point.y, Is.EqualTo(16));
            Assert.That(CansGhostRules.Constrain(Vector3.down * 100, 12, 16).y, Is.EqualTo(1));
        }
    }
}
