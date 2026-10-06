using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Minigames.BelieveOrNot;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests.PlayMode
{
    public sealed class BelieveRematchTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject root;
        private BelieveOrNotConfig config;
        private BelieveOrNotMinigame game;
        private MinigameStageState stage;
        private Random.State randomState;

        [SetUp] public void SetUp()
        {
            randomState = Random.state;
            Random.InitState(700);
        }

        [TearDown] public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (config != null) Object.DestroyImmediate(config);
            Random.state = randomState;
        }

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, Private).SetValue(target, value);
        private static T Get<T>(object target, string field) =>
            (T)target.GetType().GetField(field, Private).GetValue(target);
        private static void Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Private).Invoke(target, args);
        private BelieveMatchState Match => Get<BelieveMatchState>(game, "match");

        private void CreateGame(int count)
        {
            root = new GameObject("Rematch test");
            root.SetActive(false);
            stage = root.AddComponent<MinigameStageState>();
            var table = root.AddComponent<BelieveTable>();
            game = root.AddComponent<BelieveOrNotMinigame>();
            config = ScriptableObject.CreateInstance<BelieveOrNotConfig>();
            Set(game, "config", config);
            Set(game, "stageState", stage);
            Set(game, "table", table);
            root.SetActive(true);
            var players = (List<SessionPlayer>)typeof(MinigameControllerBase).GetField("playerList", Private).GetValue(game);
            for (int i = 0; i < count; i++) players.Add(new SessionPlayer(i, "Player " + i));
            Call(game, "OnPlayersReady");
            Call(game, "BeginRound", 1);
        }

        private void Resolve()
        {
            int before = 0;
            foreach (var entry in game.TournamentEntries) before += entry.RoundsWon;
            stage.EnterStage(BelieveStage.Persuasion, config.PersuasionSeconds);
            game.HandleDecision(game.DeciderPlayerId, Decision.Keep);
            Assert.That(Match.Resolved, Is.True);
            int whileClosed = 0;
            foreach (var entry in game.TournamentEntries) whileClosed += entry.RoundsWon;
            Assert.That(whileClosed, Is.EqualTo(before), "score must not expose the result while boxes are closed");
            Call(game, "CommitDuelResult");
        }

        [TestCase(2, 4)] [TestCase(3, 6)] [TestCase(4, 8)] [TestCase(5, 10)]
        [TestCase(6, 12)] [TestCase(7, 14)] [TestCase(8, 16)]
        public void WholeMatchGivesBothRolesAndOnePointPerHand(int count, int rounds)
        {
            CreateGame(count);
            Assert.That(Match.TotalRounds, Is.EqualTo(rounds));
            var knowing = new int[count];
            var deciding = new int[count];
            var distinctPairs = new HashSet<string>();
            BelieveMatchState previous = default;
            for (int round = 1; round <= rounds; round++)
            {
                if (round > 1) Call(game, "BeginRound", round);
                var current = Match;
                Assert.That(current.IsRematch, Is.EqualTo(round % 2 == 0));
                Assert.That(current.Resolved || current.Cancelled, Is.False);
                Assert.That(current.Decision, Is.EqualTo(Decision.None));
                Assert.That(game.PredictionResults.Round, Is.Zero);
                Assert.That(Get<BelieveCard>(game, "localCard"), Is.EqualTo(BelieveCard.Unknown));
                if (current.IsRematch)
                {
                    Assert.That(current.Seat0PlayerId, Is.EqualTo(previous.Seat0PlayerId));
                    Assert.That(current.Seat1PlayerId, Is.EqualTo(previous.Seat1PlayerId));
                    Assert.That(current.KnowerPlayerId, Is.EqualTo(previous.DeciderPlayerId));
                    Assert.That(current.DeciderPlayerId, Is.EqualTo(previous.KnowerPlayerId));
                }
                else distinctPairs.Add(current.Seat0PlayerId + ":" + current.Seat1PlayerId);
                knowing[current.KnowerPlayerId]++;
                deciding[current.DeciderPlayerId]++;
                Resolve();
                int points = 0;
                foreach (var entry in Get<List<BelieveEntry>>(game, "entries")) points += entry.RoundsWon;
                Assert.That(points, Is.EqualTo(round));
                Assert.That(Match.TeamAWins + Match.TeamBWins, Is.Zero, "personal tournament");
                previous = current;
            }
            for (int id = 0; id < count; id++)
            {
                Assert.That(knowing[id], Is.GreaterThanOrEqualTo(1), "every player knows at least once");
                Assert.That(deciding[id], Is.EqualTo(knowing[id]), "equal chances in both roles");
            }
            if (count == 3) Assert.That(distinctPairs.Count, Is.EqualTo(3), "each pair plays exactly two hands");
        }

        [TestCase(true)] [TestCase(false)]
        public void OpponentLeavingAfterOutcomeReplacesPairWithoutRematchLabel(bool knowerLeaves)
        {
            CreateGame(4);
            Resolve();
            int departed = knowerLeaves ? game.KnowerPlayerId : game.DeciderPlayerId;
            game.HandlePlayerLeft(departed);
            Call(game, "BeginRound", 2);
            Assert.That(Match.IsRematch, Is.False);
            Assert.That(Match.Seat0PlayerId, Is.Not.EqualTo(departed));
            Assert.That(Match.Seat1PlayerId, Is.Not.EqualTo(departed));
            Resolve();
            Assert.That(Match.TeamAWins + Match.TeamBWins, Is.Zero);
        }

        [Test] public void SpectatorLeavingDoesNotBreakTheRematch()
        {
            CreateGame(3);
            Resolve();
            var previous = Match;
            int spectator = 3 - previous.Seat0PlayerId - previous.Seat1PlayerId;
            game.HandlePlayerLeft(spectator);
            Call(game, "BeginRound", 2);
            Assert.That(Match.IsRematch, Is.True);
            Assert.That(Match.KnowerPlayerId, Is.EqualTo(previous.DeciderPlayerId));
            Assert.That(Match.DeciderPlayerId, Is.EqualTo(previous.KnowerPlayerId));
        }

        [Test] public void RematchFlagSurvivesNetworkSerializationAndAffectsEquality()
        {
            var expected = new BelieveMatchNetState
            {
                RoundNumber = 2, TotalRounds = 4, Seat0PlayerId = 3, Seat1PlayerId = 5,
                KnowerPlayerId = 5, DeciderPlayerId = 3, TeamAWins = 1, IsRematch = true
            };
            using var writer = new FastBufferWriter(128, Allocator.Temp);
            writer.WriteNetworkSerializable(expected);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out BelieveMatchNetState actual);
            Assert.That(actual.Equals(expected), Is.True);
            actual.IsRematch = false;
            Assert.That(actual.Equals(expected), Is.False);
        }
    }
}
