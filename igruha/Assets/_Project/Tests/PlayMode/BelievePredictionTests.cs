using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.UI;
using Igruha.Minigames.BelieveOrNot;
using NUnit.Framework;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Igruha.Tests.PlayMode
{
    public sealed class BelievePredictionTests
    {
        private readonly List<BelieveEntry> players = new List<BelieveEntry>();
        private BelievePredictions votes;
        [SetUp] public void Setup()
        {
            players.Clear();
            for (int i = 0; i < 8; i++) players.Add(new BelieveEntry { PlayerId = i, Present = true });
            votes = new BelievePredictions();
            votes.Reset(1, 0, 1);
        }

        [Test] public void OnlyCurrentPresentSpectatorCanChooseAnOpponentOnce()
        {
            Assert.False(votes.TryPick(1, 0, 1, true, players));
            Assert.False(votes.TryPick(1, 1, 0, true, players));
            Assert.False(votes.TryPick(0, 2, 0, true, players));
            Assert.False(votes.TryPick(2, 2, 0, true, players));
            Assert.False(votes.TryPick(1, 2, 4, true, players));
            Assert.False(votes.TryPick(1, 100, 0, true, players));
            Assert.False(votes.TryPick(1, 2, 0, false, players));
            players[3] = new BelieveEntry { PlayerId = 3, Present = false };
            Assert.False(votes.TryPick(1, 3, 0, true, players));
            Assert.True(votes.TryPick(1, 2, 0, true, players));
            Assert.False(votes.TryPick(1, 2, 1, true, players));
            Assert.That(votes.ChoiceOf(2), Is.Zero);
            Assert.That(votes.Reveal(0).Picks.Length, Is.EqualTo(1));
        }

        [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(8)]
        public void SupportsWholeRosterAndResetsBetweenRounds(int count)
        {
            players.RemoveRange(count, players.Count - count);
            for (int i = 2; i < count; i++) Assert.True(votes.TryPick(1, i, i % 2, true, players));
            Assert.That(votes.Reveal(1).Picks.Length, Is.EqualTo(count - 2));
            votes.Reset(2, 0, 1);
            Assert.That(votes.Reveal(1).Picks.Length, Is.Zero);
            Assert.False(votes.TryPick(1, 2, 0, true, players));
            Assert.That(votes.ChoiceOf(2), Is.EqualTo(-1));
        }

        [Test] public void DisconnectRemovesOnlyDepartingVoteAndPublishedSnapshotDoesNotChange()
        {
            votes.TryPick(1, 2, 0, true, players);
            votes.TryPick(1, 3, 1, true, players);
            votes.Remove(2);
            var reveal = votes.Reveal(1);
            Assert.That(reveal.Picks.Length, Is.EqualTo(1));
            Assert.That(reveal.Picks[0].PlayerId, Is.EqualTo(3));
            votes.Reset(2, 0, 1);
            Assert.That(reveal.Picks.Length, Is.EqualTo(1));
        }

        [Test] public void RevealedSnapshotSurvivesNetworkSerialization()
        {
            for (int i = 2; i < 8; i++) votes.TryPick(1, i, i % 2, true, players);
            var expected = votes.Reveal(1);
            using var writer = new FastBufferWriter(256, Allocator.Temp);
            writer.WriteNetworkSerializable(expected);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out BelievePredictionResults actual);
            Assert.That(actual.Equals(expected), Is.True);
        }

        [Test] public void PanelBlocksPauseAndSecondPickWithoutTakingCursor()
        {
            var root = new GameObject("Predictions", typeof(RectTransform));
            var pauseRoot = new GameObject("Pause");
            float savedTime = Time.timeScale;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var cursor = Cursor.lockState;
            bool visible = Cursor.visible;
            try
            {
                var pause = pauseRoot.AddComponent<PauseScreen>();
                var panel = root.AddComponent<BelievePredictionPanel>();
                panel.Initialize(TMP_Settings.defaultFontAsset);
                int count = 0;
                panel.Picked += (round, player) => count++;
                panel.Open(1, 0, "First", 1, "Second");
                Assert.That(Cursor.lockState, Is.EqualTo(cursor));
                Assert.That(Cursor.visible, Is.EqualTo(visible));
                typeof(PauseScreen).GetMethod("Pause", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(pause, null);
                panel.Choose(0);
                Assert.That(count, Is.Zero);
                pause.Resume();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftArrow));
                InputSystem.Update();
                typeof(BelievePredictionPanel).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(panel, null);
                Assert.That(count, Is.Zero, "walking arrows must not submit a prediction");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Digit1));
                InputSystem.Update();
                typeof(BelievePredictionPanel).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(panel, null);
                panel.Choose(1);
                Assert.That(count, Is.EqualTo(1));
                panel.Close();
                panel.Open(2, 0, "First", 1, "Second");
                Assert.That(panel.CanPick, Is.True);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(pauseRoot);
                Time.timeScale = savedTime;
                Cursor.lockState = cursor;
                Cursor.visible = visible;
            }
        }
    }
}
