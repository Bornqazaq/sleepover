using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Igruha.Minigames.CansOrder;
using Igruha.Minigames.Circus;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class CircusInterfacePrivacyTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private GameObject root;
        private CansOrderMinigame game;
        private object contestant;
        private readonly List<int> order = new List<int>();

        [SetUp] public void SetUp()
        {
            root = new GameObject("HUD privacy fixture"); root.SetActive(false);
            game = root.AddComponent<CansOrderMinigame>();
            Set("config", AssetDatabase.LoadAssetAtPath<CansOrderConfig>("Assets/_Project/Settings/Gameplay/Minigames/CansOrderConfig.asset"));
            Set("arenaConfig", AssetDatabase.LoadAssetAtPath<CircusArenaConfig>("Assets/_Project/Settings/Gameplay/Minigames/CircusArenaConfig.asset"));
            Set("round", new CansOrderRoundState { Round=1, Circle=2, CanCount=5 });
            var type = typeof(CansOrderMinigame).GetNestedType("Contestant", BindingFlags.NonPublic);
            contestant = Activator.CreateInstance(type, true);
            type.GetField("LocallyControlled").SetValue(contestant, true);
            ((List<int>)type.GetField("Submitted").GetValue(contestant)).AddRange(new[]{4,2,0,3,1});
            ((IList)typeof(CansOrderMinigame).GetField("contestants", Private).GetValue(game)).Add(contestant);
        }
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(root);
        private void Set(string name, object value) => typeof(CansOrderMinigame).GetField(name, Private).SetValue(game,value);
        private void Entry(bool confirmed, bool solved=false)
            => contestant.GetType().GetField("Entry").SetValue(contestant,new CansOrderEntry { Alive=true, Confirmed=confirmed, Solved=solved, SolvedThisCircle=solved, Matches=solved?5:3 });

        [Test] public void CurrentScoreAndOrderRemainHiddenUntilReveal()
        {
            Entry(true); order.Add(99);
            Assert.That(game.TryGetLocalHudState(out var entry,out _,out _,order,false,out _,out bool confirmed,out int matches), Is.True);
            Assert.That(entry.Matches, Is.Zero); Assert.That(matches, Is.Zero);
            Assert.That(confirmed, Is.False); Assert.That(order, Is.Empty);
        }
        [Test] public void ConfirmedUnsolvedAttemptRevealsOnlyItsOwnSnapshot()
        {
            Entry(true); Set("resultsRevealed",true);
            game.TryGetLocalHudState(out _,out _,out _,order,false,out _,out bool confirmed,out int matches);
            Assert.That(confirmed, Is.True); Assert.That(matches, Is.EqualTo(3));
            Assert.That(order, Is.EqualTo(new[]{4,2,0,3,1}));
        }
        [Test] public void WinningOrderIsNeverPrintedInResultTicket()
        {
            Entry(true,true); Set("resultsRevealed",true);
            game.TryGetLocalHudState(out _,out _,out _,order,false,out _,out _,out _);
            Assert.That(order, Is.Empty);
        }
        [Test] public void MissingConfirmationDoesNotBecomeZeroMatches()
        {
            Entry(false); Set("resultsRevealed",true);
            game.TryGetLocalHudState(out _,out _,out _,order,false,out _,out bool confirmed,out _);
            Assert.That(confirmed, Is.False); Assert.That(order, Is.Empty);
        }
        [Test] public void PreviousRoundNeverLeaksIntoNewRoundTicket()
        {
            Entry(true); Set("lastCircleRound",0); Set("lastCircleNumber",4); Set("lastCircleConfirmed",true); Set("lastCircleMatches",3);
            ((List<int>)typeof(CansOrderMinigame).GetField("lastCircleArrangement",Private).GetValue(game)).Add(2);
            game.TryGetLocalHudState(out _,out _,out _,order,true,out bool previous,out bool confirmed,out int matches);
            Assert.That(previous, Is.False); Assert.That(confirmed, Is.False); Assert.That(matches, Is.Zero); Assert.That(order, Is.Empty);
        }
    }
}
