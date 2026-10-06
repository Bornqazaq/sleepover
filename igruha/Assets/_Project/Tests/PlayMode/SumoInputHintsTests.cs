using Igruha.Minigames.SumoRing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Igruha.Tests.PlayMode
{
    public sealed class SumoInputHintsTests
    {
        [Test]
        public void HintsFollowUsedDeviceAndIgnoreReleaseAndStickDrift()
        {
            var holder = new GameObject("hints");
            var pad = InputSystem.AddDevice<Gamepad>(); var mouse = InputSystem.AddDevice<Mouse>(); var keyboard = InputSystem.AddDevice<Keyboard>();
            var trigger = new InputAction("audit-trigger", InputActionType.Button, "<Gamepad>/rightTrigger");
            var look = new InputAction("audit-look", InputActionType.PassThrough, "<Gamepad>/rightStick");
            var click = new InputAction("audit-click", InputActionType.Button, "<Mouse>/leftButton");
            var key = new InputAction("audit-key", InputActionType.Button, "<Keyboard>/w");
            try
            {
                var hud = holder.AddComponent<SumoCombatHud>();
                trigger.Enable(); look.Enable(); click.Enable(); key.Enable();
                InputSystem.QueueStateEvent(pad, new GamepadState { rightTrigger = 1 }); InputSystem.Update();
                Assert.That(hud.UsesGamepad, Is.True); StringAssert.Contains("RT/R2", hud.LabelFor(3)); StringAssert.Contains("RT/R2", hud.LabelFor(4));
                InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.Update();
                Assert.That(hud.UsesGamepad, Is.True, "release must not change the device");
                InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left)); InputSystem.Update();
                Assert.That(hud.UsesGamepad, Is.False); StringAssert.Contains("ЛКМ", hud.LabelFor(3));
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = new Vector2(.04f, 0) }); InputSystem.Update();
                Assert.That(hud.UsesGamepad, Is.False, "stick drift must not steal mouse hints");
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = new Vector2(.8f, 0) }); InputSystem.Update();
                Assert.That(hud.UsesGamepad, Is.True);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); InputSystem.Update();
                Assert.That(hud.UsesGamepad, Is.False); StringAssert.Contains("ПКМ", hud.LabelFor(0));
            }
            finally
            {
                trigger.Dispose(); look.Dispose(); click.Dispose(); key.Dispose(); Object.DestroyImmediate(holder);
                InputSystem.RemoveDevice(pad); InputSystem.RemoveDevice(mouse); InputSystem.RemoveDevice(keyboard);
            }
        }

    }
}
