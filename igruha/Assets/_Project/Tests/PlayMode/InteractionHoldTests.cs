#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using Igruha.Core.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Igruha.Tests.PlayMode
{
    public sealed class InteractionHoldTests
    {
        private GameObject root;
        private PlayerInputReader reader;
        private InputActionAsset actions;
        private InputActionReference reference;
        private Keyboard keyboard;
        private Keyboard previousKeyboard;

        [SetUp]
        public void SetUp()
        {
            previousKeyboard = Keyboard.current;
            keyboard = InputSystem.AddDevice<Keyboard>();
            actions = Object.Instantiate(AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                "Assets/InputSystem_Actions.inputactions"));
            actions.devices = new InputDevice[] { keyboard };
            reference = InputActionReference.Create(actions.FindAction("Player/Interact", true));
            root = new GameObject("Interaction hold check");
            root.SetActive(false);
            reader = root.AddComponent<PlayerInputReader>();
            typeof(PlayerInputReader).GetField("interactAction", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(reader, reference);
            root.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(reference);
            Object.DestroyImmediate(actions);
            InputSystem.RemoveDevice(keyboard);
            previousKeyboard?.MakeCurrent();
        }

        [UnityTest]
        public IEnumerator TapIsRejectedAndCompletedHoldActivatesOnlyOnce()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
            yield return new WaitForSecondsRealtime(.08f);
            Assert.That(reader.InteractPressed, Is.False, "A short tap must not bypass the frozen Hold");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(.05f);
            Assert.That(reader.InteractPressed, Is.False, "Releasing early must cancel the action");

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
            yield return new WaitForSecondsRealtime(InputSystem.settings.defaultHoldTime + .15f);
            Assert.That(reader.InteractPressed, Is.True, "Completed Hold must reach PlayerInteractor");
            reader.ConsumeInteract();
            yield return null;
            yield return null;
            Assert.That(reader.InteractPressed, Is.False, "Keeping E down must not repeat the action");
        }

        [UnityTest]
        public IEnumerator ContinuousHoldStillTracksPressAndReleaseWithoutWaitingForThreshold()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
            yield return new WaitForSecondsRealtime(.08f);
            Assert.That(reader.InteractHeld, Is.True, "Stopwatch and bottle stacks need the physical hold immediately");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(.05f);
            Assert.That(reader.InteractHeld, Is.False, "Release must stop a continuous interaction immediately");
        }
    }
}
#endif
