using System.Collections.Generic;
using System.Reflection;
using Igruha.Minigames.CansOrder;
using Igruha.Minigames.MemoryRun;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class PlaytestSeptemberTests
    {
        [Test]
        public void GeneratedMemoryRoutesIncludeAllLaneTransitionsWithoutTripleRepeats()
        {
            var config = ScriptableObject.CreateInstance<MemoryRunConfig>();
            try
            {
                var route = new MemoryRunRoute();
                var transitions = new bool[MemoryRunConfig.LaneCount, MemoryRunConfig.LaneCount];
                for (int seed = 0; seed < 256; seed++)
                {
                    route.Generate(config, seed);
                    Assert.That(route.Steps, Is.EqualTo(config.Steps));
                    int previous = -1, run = 0;
                    for (int step = 0; step < route.Steps; step++)
                    {
                        int safeCount = 0, chosen = -1;
                        for (int lane = 0; lane < MemoryRunConfig.LaneCount; lane++)
                        {
                            if (!route.IsSafe(step, lane)) continue;
                            safeCount++;
                            chosen = lane;
                        }
                        Assert.That(safeCount, Is.EqualTo(1), $"seed {seed}, step {step}");
                        run = chosen == previous ? run + 1 : 1;
                        Assert.That(run, Is.LessThanOrEqualTo(config.MaxSameLaneRun));
                        if (previous >= 0) transitions[previous, chosen] = true;
                        previous = chosen;
                    }
                }
                for (int from = 0; from < MemoryRunConfig.LaneCount; from++)
                    for (int to = 0; to < MemoryRunConfig.LaneCount; to++)
                        Assert.That(transitions[from, to], Is.True, $"Missing transition {from + 1}->{to + 1}");
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void MemoryRouteValidationAcceptsExtremeJumpsAndStillRejectsInvalidRoutes()
        {
            Assert.That(MemoryRunRoute.IsValidSequence(new[] { 0, 2, 0, 2, 2, 0, 1, 1, 0, 2 }, 2), Is.True);
            for (int lane = 0; lane < MemoryRunConfig.LaneCount; lane++)
                Assert.That(MemoryRunRoute.IsValidSequence(new[] { lane, lane, lane }, 2), Is.False);
            Assert.That(MemoryRunRoute.IsValidSequence(new[] { 0, 3 }, 2), Is.False);
            Assert.That(MemoryRunRoute.IsValidSequence(new[] { -1, 2 }, 2), Is.False);
            Assert.That(MemoryRunRoute.IsValidSequence(new int[0], 2), Is.False);
        }

        [TestCase(0, 2)]
        [TestCase(2, 0)]
        public void MemoryBotCanChooseOppositeLaneWhenSafeOrLastUnknown(int from, int to)
        {
            var go = new GameObject("Memory bot extreme lane regression");
            go.SetActive(false);
            try
            {
                var bot = go.AddComponent<MemoryRunDebugBot>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var safe = new bool[1, MemoryRunConfig.LaneCount];
                var mines = new bool[1, MemoryRunConfig.LaneCount];
                typeof(MemoryRunDebugBot).GetField("provedSafe", flags).SetValue(bot, safe);
                typeof(MemoryRunDebugBot).GetField("provedMine", flags).SetValue(bot, mines);
                var choose = typeof(MemoryRunDebugBot).GetMethod("ChooseLane", flags);
                safe[0, to] = true;
                Assert.That(choose.Invoke(bot, new object[] { 0, from }), Is.EqualTo(to));
                safe[0, to] = false;
                for (int lane = 0; lane < MemoryRunConfig.LaneCount; lane++) mines[0, lane] = lane != to;
                Assert.That(choose.Invoke(bot, new object[] { 0, from }), Is.EqualTo(to));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void FinishTimeWinsRegardlessOfAttemptsAndNonFinishersShareLastPlace()
        {
            var early = new CansOrderEntry { Solved = true, ConfirmTime = 12, Attempts = 4 };
            var late = new CansOrderEntry { Solved = true, ConfirmTime = 20, Attempts = 1 };
            Assert.That(CanOrderRanking.CompareFinish(early, late), Is.LessThan(0));
            Assert.That(CanOrderRanking.CompareFinish(late, new CansOrderEntry()), Is.LessThan(0));
            Assert.That(CanOrderRanking.CompareFinish(new CansOrderEntry { Matches = 4 }, new CansOrderEntry()), Is.Zero);
        }

        [Test]
        public void MatchesCountPositionsNotColorsPresent()
        {
            var go = new GameObject("CountMatches regression"); go.SetActive(false);
            try
            {
                var game = go.AddComponent<CansOrderMinigame>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var answer = (List<int>)typeof(CansOrderMinigame).GetField("solution", flags).GetValue(game);
                answer.AddRange(new[] { 0, 1, 2, 3, 4 });
                var count = typeof(CansOrderMinigame).GetMethod("CountMatches", flags);
                Assert.That(count.Invoke(game, new object[] { new List<int>{0,1,2,3,4} }), Is.EqualTo(5));
                Assert.That(count.Invoke(game, new object[] { new List<int>{1,2,3,4,0} }), Is.EqualTo(0));
                Assert.That(count.Invoke(game, new object[] { new List<int>{0,2,1,4,3} }), Is.EqualTo(1));
                Assert.That(count.Invoke(game, new object[] { new List<int>{1,0,2,3,4} }), Is.EqualTo(3));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AllMemoryLaneTransitionsHaveJumpMarginAndGapsAreNotPlates()
        {
            var config = AssetDatabase.LoadAssetAtPath<MemoryRunConfig>("Assets/_Project/Settings/Gameplay/Minigames/MemoryRunConfig.asset");
            var character = AssetDatabase.LoadAssetAtPath<Igruha.Core.Player.CharacterConfig>("Assets/_Project/Settings/Gameplay/CharacterConfig.asset");
            float g = Mathf.Abs(Physics.gravity.y);
            float rise = character.JumpSpeed / (g * character.RiseGravityMultiplier);
            float apex = character.JumpSpeed * rise / 2;
            float reach = character.MaxSpeed * (rise + Mathf.Sqrt(2 * apex / (g * character.FallGravityMultiplier)));
            Assert.That(reach / config.LongestRequiredJump, Is.GreaterThan(1.5f));
            for (int lane = 0; lane < 3; lane++)
            {
                Assert.That(config.TryGetCell(config.CellCenter(0,lane), out int row, out int found), Is.True);
                Assert.That(found, Is.EqualTo(lane)); Assert.That(row, Is.Zero);
            }
            var gap = config.CellCenter(0,0); gap.x += config.LanePitch * .5f;
            Assert.That(config.TryGetCell(gap, out _, out _), Is.False);
        }

        [Test]
        public void EmptyInitialNetworkSnapshotDoesNotRemoveCansContestants()
        {
            var go = new GameObject("Initial cans roster"); go.SetActive(false);
            try
            {
                var game = go.AddComponent<CansOrderMinigame>();
                var network = go.AddComponent<CansOrderNetwork>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var contestants = (System.Collections.IList)typeof(CansOrderMinigame).GetField("contestants", flags).GetValue(game);
                var type = typeof(CansOrderMinigame).GetNestedType("Contestant", BindingFlags.NonPublic);
                for (int i=0;i<4;i++) contestants.Add(System.Activator.CreateInstance(type, true));
                typeof(CansOrderNetwork).GetField("game", flags).SetValue(network,game);
                typeof(CansOrderNetwork).GetMethod("ApplyEntries", flags).Invoke(network,null);
                Assert.That(game.ContestantCount,Is.EqualTo(4));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
