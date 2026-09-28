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
