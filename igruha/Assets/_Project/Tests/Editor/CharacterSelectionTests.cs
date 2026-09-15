using System;
using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Igruha.Tests
{
    public sealed class CharacterSelectionTests
    {
        private GameObject root, panel, prefab;
        private CharacterSelectScreen screen;
        private CharacterRoster roster;
        private int callbacks, result;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("SelectionTest");
            screen = root.AddComponent<CharacterSelectScreen>();
            panel = new GameObject("Panel"); panel.transform.SetParent(root.transform); panel.SetActive(false);
            prefab = new GameObject("AvailableCharacter");
            roster = ScriptableObject.CreateInstance<CharacterRoster>();
            var data = new SerializedObject(roster);
            var entries = data.FindProperty("characters"); entries.arraySize = 3;
            for (int i = 0; i < 3; i++)
            {
                entries.GetArrayElementAtIndex(i).FindPropertyRelative("displayName").stringValue = "Character" + i;
                entries.GetArrayElementAtIndex(i).FindPropertyRelative("prefab").objectReferenceValue = i == 1 ? null : prefab;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            Set("panel", panel); Set("slots", Array.Empty<CharacterSlotButton>());
            callbacks = 0; result = -100;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(prefab);
            UnityEngine.Object.DestroyImmediate(roster);
        }

        private void Show(FakeSelection selection = null)
        {
            // This isolated state-machine fixture deliberately has no camera rig or presentation.
            LogAssert.Expect(LogType.Error, "SelectionTest: не назначен cameraRig — курсор останется залоченным и по слотам нельзя будет кликнуть");
            screen.Show(roster, selection, i => { callbacks++; result = i; });
        }
        private void Set(string name, object value) => typeof(CharacterSelectScreen).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(screen, value);
        private void Tick() => typeof(CharacterSelectScreen).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(screen, null);

        [Test]
        public void LocalTimeoutChoosesAvailableCharacterAndCallsBackOnce()
        {
            Show(); Set("localDeadline", Time.realtimeSinceStartup - 1); Tick(); Tick();
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(result, Is.EqualTo(0).Or.EqualTo(2));
            Assert.That(screen.IsOpen, Is.False);
        }

        [Test]
        public void InvalidAndUnavailableSelectionsDoNotCloseScreen()
        {
            Show(); screen.OnSlotChosen(-1); screen.OnSlotChosen(20); screen.OnSlotChosen(1);
            Assert.That(screen.IsOpen, Is.True); Assert.That(callbacks, Is.Zero);
            screen.OnSlotChosen(2);
            Assert.That(result, Is.EqualTo(2)); Assert.That(callbacks, Is.EqualTo(1));
        }

        [Test]
        public void ReopeningStartsANewLocalDeadline()
        {
            Show(); Set("localDeadline", Time.realtimeSinceStartup - 1); Tick();
            Show(); Tick();
            Assert.That(screen.IsOpen, Is.True); Assert.That(callbacks, Is.EqualTo(1));
        }

        [Test]
        public void NetworkTimeoutWaitsForServerAndNeverRollsLocally()
        {
            var service = new FakeSelection(); Show(service); service.SecondsLeft = 0; Tick();
            Assert.That(screen.IsOpen, Is.True); Assert.That(callbacks, Is.Zero); Assert.That(service.Requests, Is.Empty);
            service.Confirm();
            Assert.That(screen.IsOpen, Is.False); Assert.That(result, Is.EqualTo(-1));
        }

        [Test]
        public void PendingRequestRejectsDuplicateClicksAndRecoversWhenSlotIsTaken()
        {
            var service = new FakeSelection(); Show(service);
            screen.OnSlotChosen(0); screen.OnSlotChosen(2);
            Assert.That(service.Requests, Is.EqualTo(new[] { 0 })); Assert.That(callbacks, Is.Zero);
            service.Take(0); screen.OnSlotChosen(0); screen.OnSlotChosen(2);
            Assert.That(service.Requests, Is.EqualTo(new[] { 0, 2 }));
            service.Confirm(); Assert.That(callbacks, Is.EqualTo(1));
        }

        [Test]
        public void SynchronousHostConfirmationClosesOnlyOnce()
        {
            var service = new FakeSelection { ConfirmDuringChoose = true }; Show(service);
            screen.OnSlotChosen(0); Tick();
            Assert.That(callbacks, Is.EqualTo(1)); Assert.That(screen.IsOpen, Is.False);
        }

        [Test]
        public void ReopeningUnsubscribesPreviousNetworkService()
        {
            var oldService = new FakeSelection(); var newService = new FakeSelection();
            Show(oldService); Show(newService); oldService.Confirm();
            Assert.That(screen.IsOpen, Is.True); Assert.That(callbacks, Is.Zero);
            newService.Confirm(); Assert.That(callbacks, Is.EqualTo(1));
        }

        private sealed class FakeSelection : ICharacterSelection
        {
            public readonly List<int> Requests = new List<int>();
            private readonly HashSet<int> taken = new HashSet<int>();
            public bool ConfirmDuringChoose;
            public bool HasChosen { get; private set; }
            public float SecondsLeft { get; set; } = 30;
            public event Action Changed;
            public bool IsTaken(int index) => taken.Contains(index);
            public bool HasCharacter(int id) => HasChosen;
            public void ReportReady() { SecondsLeft = 30; }
            public void Choose(int index) { Requests.Add(index); if (ConfirmDuringChoose) Confirm(); }
            public void Take(int index) { taken.Add(index); Changed?.Invoke(); }
            public void Confirm() { HasChosen = true; Changed?.Invoke(); }
        }
    }
}
