using System.Collections.Generic;
using Igruha.Core.Minigame;
using Igruha.Minigames.BelieveOrNot;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;

namespace Igruha.Tests.PlayMode
{
    public sealed class BelieveTournamentTests
    {
        private static List<BelieveEntry> Players(int count)
        {
            var result = new List<BelieveEntry>();
            for (int i = 0; i < count; i++) result.Add(new BelieveEntry { PlayerId = i, Present = true });
            return result;
        }
        private static void Leave(List<BelieveEntry> players, int id)
        { var e = players[id]; e.Present = false; players[id] = e; }

        [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)]
        public void EveryPlayerGetsFourHandsTwoRolesAndTwoOpponents(int count)
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var game = new BelieveTournament(Players(count), seed);
                var played = new int[count]; var knowing = new int[count];
                var opponents = new HashSet<int>[count];
                for (int i = 0; i < count; i++) opponents[i] = new HashSet<int>();
                foreach (var hand in game.Schedule)
                {
                    Assert.That(hand.A, Is.Not.EqualTo(hand.B));
                    played[hand.A]++; played[hand.B]++; knowing[hand.Knower]++;
                    opponents[hand.A].Add(hand.B); opponents[hand.B].Add(hand.A);
                }
                for (int i = 0; i < count; i++)
                { Assert.That(played[i], Is.EqualTo(4)); Assert.That(knowing[i], Is.EqualTo(2)); Assert.That(opponents[i].Count, Is.EqualTo(count == 2 ? 1 : 2)); }
            }
        }

        [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)]
        public void AllTiedQualificationIsSettledAtTableThenFinalHasOneChampion(int count)
        {
            var players = Players(count); var game = new BelieveTournament(players, 736);
            int playoffs = 0, finals = 0, hands = 0;
            while (game.Next(out var hand))
            {
                Assert.That(++hands, Is.LessThanOrEqualTo(3 * count + 1));
                var phase = game.State.Phase;
                int winner = hand.Knower;
                if (phase == BelieveTournamentPhase.Playoff) { playoffs++; winner = hand.A; }
                if (phase == BelieveTournamentPhase.Final) { finals++; winner = finals == 2 ? hand.B : hand.A; }
                Assert.True(game.Record(winner)); Assert.False(game.Record(winner));
            }
            foreach (var e in players) { Assert.That(e.QualificationPlayed, Is.EqualTo(4)); Assert.That(e.QualificationWins, Is.EqualTo(2)); }
            Assert.That(playoffs, Is.EqualTo(count - 2));
            Assert.That(finals, Is.EqualTo(3));
            Assert.That(game.State.Champion, Is.EqualTo(game.State.FinalA));
            Assert.That(game.State.WinsA, Is.EqualTo(2)); Assert.That(game.State.WinsB, Is.EqualTo(1));
            var results = new MinigameResults(); BelieveRanking.FillTournament(players, game.State, results);
            int champions = 0;
            foreach (var result in results.Entries) if (result.Place == 1) { champions++; Assert.That(result.PlayerId, Is.EqualTo(game.State.Champion)); }
            Assert.That(champions, Is.EqualTo(1));
        }

        [Test] public void AnInvalidWinnerCannotConsumeAHand()
        {
            var game = new BelieveTournament(Players(4), 5);
            Assert.True(game.Next(out var hand)); Assert.False(game.Record(99));
            Assert.Throws<System.InvalidOperationException>(() => game.Next(out _));
            Assert.True(game.Record(hand.A));
        }

        [Test] public void DepartedOpponentGivesWalkoversAndNeverBecomesFinalist()
        {
            var players = Players(8); var game = new BelieveTournament(players, 3);
            game.Next(out var first); Leave(players, first.B); game.Record(first.A, true);
            int guard = 0;
            while (game.Next(out var hand))
            {
                Assert.That(++guard, Is.LessThan(30));
                Assert.That(hand.A, Is.Not.EqualTo(first.B)); Assert.That(hand.B, Is.Not.EqualTo(first.B));
                game.Record(hand.Knower);
            }
            foreach (var e in players) if (e.Present) Assert.That(e.QualificationPlayed, Is.EqualTo(4));
            Assert.That(game.State.FinalA, Is.Not.EqualTo(first.B)); Assert.That(game.State.FinalB, Is.Not.EqualTo(first.B));
            Assert.That(players[first.A].ForfeitWins, Is.GreaterThanOrEqualTo(2));
        }

        [Test] public void FinalistDepartureAwardsSeriesToRemainingFinalist()
        {
            var players = Players(4); var game = new BelieveTournament(players, 3);
            BelieveTournamentHand hand;
            while (game.Next(out hand) && game.State.Phase != BelieveTournamentPhase.Final) game.Record(hand.Knower);
            Leave(players, hand.B); game.Record(hand.A, true);
            Assert.False(game.Next(out _));
            Assert.That(game.State.Champion, Is.EqualTo(hand.A));
            Assert.That(game.State.RunnerUp, Is.EqualTo(hand.B));
        }

        [Test] public void PublicTournamentAndReputationSerializeWithoutLosingFields()
        {
            var expected = new BelieveMatchNetState { RoundNumber = 23, Tournament = new BelieveTournamentState {
                Phase = BelieveTournamentPhase.Final, QualificationRound = 16, QualificationTotal = 16,
                FinalA = 2, FinalB = 5, WinsA = 1, WinsB = 1, Champion = -1, RunnerUp = -1, Contenders = 4, PlacesAvailable = 2 } };
            using var writer = new FastBufferWriter(256, Allocator.Temp);
            writer.WriteNetworkSerializable(expected);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out BelieveMatchNetState actual);
            Assert.That(actual.Equals(expected), Is.True);
            actual.Tournament.WinsA = 2; Assert.That(actual.Equals(expected), Is.False);
            var entry = new BelieveEntryNetState { PlayerId = 2, QualificationWins = 3, QualificationPlayed = 4,
                ForfeitWins = 1, OathHistory = 5, OathCount = 3, PredictionStreak = 3, BestPredictionStreak = 4, CorrectPredictions = 8, PredictionRound = 23 };
            using var ew = new FastBufferWriter(128, Allocator.Temp); ew.WriteNetworkSerializable(entry);
            using var er = new FastBufferReader(ew, Allocator.Temp); er.ReadNetworkSerializable(out BelieveEntryNetState copy);
            Assert.True(copy.Equals(entry));
        }
    }
}
