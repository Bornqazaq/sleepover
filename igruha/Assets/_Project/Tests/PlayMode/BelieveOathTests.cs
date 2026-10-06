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
    public sealed class BelieveOathTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject root;
        private BelieveOrNotMinigame game;
        private MinigameStageState stage;
        private BelieveOrNotConfig config;
        private Random.State savedRandom;
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
        private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
        private BelieveMatchState Match => Get<BelieveMatchState>(game, "match");

        [SetUp] public void Setup() { savedRandom = Random.state; Random.InitState(735); }
        [TearDown] public void Cleanup()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (config != null) Object.DestroyImmediate(config);
            Random.state = savedRandom;
        }

        private void Create(int count = 4)
        {
            root = new GameObject("Oath test"); root.SetActive(false);
            stage = root.AddComponent<MinigameStageState>();
            var table = root.AddComponent<BelieveTable>();
            game = root.AddComponent<BelieveOrNotMinigame>();
            config = ScriptableObject.CreateInstance<BelieveOrNotConfig>();
            Set(game, "config", config); Set(game, "table", table); Set(game, "stageState", stage);
            root.SetActive(true);
            var list = (List<SessionPlayer>)typeof(MinigameControllerBase).GetField("playerList", Private).GetValue(game);
            for (int i = 0; i < count; i++) list.Add(new SessionPlayer(i, "Player " + i));
            typeof(MinigameControllerBase).GetField("phase", Private).SetValue(game, MinigamePhase.Round);
            Call(game, "OnPlayersReady"); Call(game, "BeginRound", 1);
            stage.EnterStage(BelieveStage.Oath, config.OathSeconds);
        }

        [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(8)]
        public void OnlyPresentKnowerInCurrentHandCanCommitOnce(int count)
        {
            Create(count);
            int knower = game.KnowerPlayerId;
            Assert.False(game.HandleOath(game.DeciderPlayerId, 1, BelieveOath.Mine));
            Assert.False(game.HandleOath(999, 1, BelieveOath.Mine));
            Assert.False(game.HandleOath(knower, 0, BelieveOath.Mine));
            Assert.False(game.HandleOath(knower, 2, BelieveOath.Mine));
            Assert.False(game.HandleOath(knower, 1, BelieveOath.Declined));
            Assert.False(game.HandleOath(knower, 1, (BelieveOath)255));
            var entries = Get<List<BelieveEntry>>(game, "entries");
            int index = entries.FindIndex(e => e.PlayerId == knower);
            var entry = entries[index]; entry.Present = false; entries[index] = entry;
            Assert.False(game.HandleOath(knower, 1, BelieveOath.Mine));
            entry.Present = true; entries[index] = entry;
            Assert.True(game.HandleOath(knower, 1, BelieveOath.Mine));
            Assert.That(game.Stage, Is.EqualTo(BelieveStage.Persuasion));
            Assert.False(game.HandleOath(knower, 1, BelieveOath.Yours));
            Assert.That(game.Oath, Is.EqualTo(BelieveOath.Mine));
            Assert.False(game.OathRevealed);
            Assert.That(Match.TeamAWins + Match.TeamBWins, Is.Zero);
        }

        [Test] public void DeadlineCannotBeExtendedAndSilenceIsNotALie()
        {
            Create();
            stage.EnterStage(BelieveStage.Oath, 0f);
            Assert.False(game.HandleOath(game.KnowerPlayerId, 1, BelieveOath.Mine));
            Call(game, "AdvanceStage", BelieveStage.Oath);
            Assert.That(game.Oath, Is.EqualTo(BelieveOath.Declined));
            Assert.That(game.Stage, Is.EqualTo(BelieveStage.Persuasion));
            Call(game, "RevealOath", BelieveCard.Win);
            Assert.False(game.OathRevealed);
            Assert.That(Get<BelieveDuelHud>(game, "duelHud").VerdictText, Does.Not.Contain("СОЛГАЛ"));
        }

        [TestCase(true)] [TestCase(false)]
        public void DisconnectDuringOathAwardsOpponentWithoutRevealingCards(bool knowerLeaves)
        {
            Create();
            game.HandlePlayerLeft(knowerLeaves ? game.KnowerPlayerId : game.DeciderPlayerId);
            Assert.That(Match.Cancelled, Is.True);
            Assert.That(Match.Resolved, Is.True);
            Assert.That(game.Stage, Is.EqualTo(BelieveStage.Cancelled));
            Assert.That(Match.ForfeitWinner, Is.EqualTo(knowerLeaves ? game.DeciderPlayerId : game.KnowerPlayerId));
            Assert.False(Get<BelieveDuelHud>(game, "duelHud").Choosing);
        }

        [TestCase(BelieveOath.Mine, true, true)] [TestCase(BelieveOath.Mine, false, false)]
        [TestCase(BelieveOath.Yours, true, false)] [TestCase(BelieveOath.Yours, false, true)]
        public void VerdictUsesOriginalKnowerBox(BelieveOath oath, bool originalWin, bool truth)
        {
            Create();
            Assert.True(game.HandleOath(game.KnowerPlayerId, 1, oath));
            Assert.False(game.OathRevealed);
            int knowerSeat = Match.Seat0PlayerId == game.KnowerPlayerId ? 0 : 1;
            var card0 = (knowerSeat == 0) == originalWin ? BelieveCard.Win : BelieveCard.Lose;
            Set(game, "winningSeat", card0 == BelieveCard.Win ? 0 : 1);
            var entries = Get<List<BelieveEntry>>(game, "entries");
            int index = entries.FindIndex(e => e.PlayerId == game.KnowerPlayerId);
            var entry = entries[index]; entry.OathCount = 3; entry.OathHistory = 6; entries[index] = entry;
            // A swap changes final positions only; the promise still refers to the original box.
            var state = Match; state.Decision = Decision.Swap; Set(game, "match", state);
            Call(game, "RevealOath", card0);
            Assert.True(game.OathRevealed);
            Assert.That(game.OathTruth, Is.EqualTo(truth));
            Call(game, "ResolveRound");
            Assert.That(entries[index].OathHistory, Is.EqualTo(6), "history stays private until lids open");
            Call(game, "CommitDuelResult");
            Assert.That(entries[index].OathHistory, Is.EqualTo(truth ? 5 : 4), "retain only the last three promises");
            Assert.That(entries[index].OathCount, Is.EqualTo(3));
            Call(game, "BeginRound", 2);
            Assert.False(game.OathRevealed);
            Assert.That(game.Oath, Is.EqualTo(BelieveOath.Pending));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void SpectatorStreakTracksCorrectWrongAndSkippedPredictions(int choice)
        {
            Create(4);
            var entries = Get<List<BelieveEntry>>(game, "entries");
            int spectator = entries.FindIndex(e => e.PlayerId != game.KnowerPlayerId && e.PlayerId != game.DeciderPlayerId);
            for (int i = 0; i < entries.Count; i++)
            { var e = entries[i]; e.PredictionStreak = e.BestPredictionStreak = 2; entries[i] = e; }
            stage.EnterStage(BelieveStage.Persuasion, config.PersuasionSeconds);
            int winner = game.KnowerPlayerId;
            if (choice != 2) Assert.True(game.HandlePrediction(entries[spectator].PlayerId, 1, choice == 1 ? winner : game.DeciderPlayerId));
            Assert.That(entries[spectator].PredictionStreak, Is.EqualTo(2), "locked picks never expose the outcome");
            Call(game, "PredictionBoxesOpened", winner);
            Assert.That(entries[spectator].PredictionStreak, Is.EqualTo(choice == 1 ? 3 : 0));
            Assert.That(entries[spectator].BestPredictionStreak, Is.EqualTo(choice == 1 ? 3 : 2));
            Assert.That(entries[spectator].CorrectPredictions, Is.EqualTo(choice == 1 ? 1 : 0));
            foreach (var e in entries) if (e.PlayerId == game.KnowerPlayerId || e.PlayerId == game.DeciderPlayerId)
                Assert.That(e.PredictionStreak, Is.EqualTo(2), "playing at the table preserves a spectator streak");
        }

        [Test] public void ForfeitPreservesHistoryAndPredictionStreaks()
        {
            Create(4);
            var entries = Get<List<BelieveEntry>>(game, "entries");
            for (int i = 0; i < entries.Count; i++)
            { var e = entries[i]; e.PredictionStreak = 2; entries[i] = e; }
            game.HandlePlayerLeft(game.KnowerPlayerId);
            foreach (var e in entries)
            { Assert.That(e.PredictionStreak, Is.EqualTo(2)); Assert.That(e.OathCount, Is.Zero); }
        }

        [Test] public void PublicClaimSurvivesNetworkSerialization()
        {
            var expected = new BelieveMatchNetState { RoundNumber = 2, Oath = (byte)BelieveOath.Yours, IsRematch = true };
            using var writer = new FastBufferWriter(128, Allocator.Temp);
            writer.WriteNetworkSerializable(expected);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out BelieveMatchNetState actual);
            Assert.That(actual.Equals(expected), Is.True);
            actual.Oath = (byte)BelieveOath.Mine;
            Assert.False(actual.Equals(expected));
        }
    }
}
