using System.Reflection;
using Igruha.Core.Player;
using Igruha.Minigames.Circus;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class CircusActionRestrictionTests
    {
        private GameObject root;
        private PlayerController player;
        private PlayerPushAbility push;
        private PlayerInputReader input;
        private CircusActionRestrictions scope;
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        [SetUp] public void Setup()
        {
            root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Player.prefab"));
            player = root.GetComponent<PlayerController>(); push = root.GetComponent<PlayerPushAbility>(); input = root.GetComponent<PlayerInputReader>();
            typeof(PlayerController).GetMethod("Awake", Private).Invoke(player, null);
            typeof(PlayerPushAbility).GetMethod("Awake", Private).Invoke(push, null);
            scope = new CircusActionRestrictions();
        }
        [TearDown] public void Cleanup() { scope.Dispose(); Object.DestroyImmediate(root); }
        private void Set(string field, float value) => typeof(PlayerController).GetField(field, Private).SetValue(player, value);
        private void Jump() => typeof(PlayerController).GetMethod("TryJump", Private).Invoke(player, null);

        [Test] public void BlocksRealJumpAndPunchAndRestoresThemOnExit()
        {
            int jumps = 0, punches = 0;
            player.Jumped += () => jumps++;
            push.PunchStarted += () => punches++;
            scope.Bind(player); scope.Bind(player);
            Set("jumpBufferTimer", 1); Set("coyoteTimer", 1);
            Jump(); push.RequestPush();
            Assert.That(jumps, Is.Zero); Assert.That(punches, Is.Zero);
            Assert.That(root.GetComponent<Rigidbody>().linearVelocity.y, Is.Zero);
            Assert.That(player.MovementLocked, Is.False, "Restriction must preserve walking");
            Assert.That(player.CrouchInputSuppressed, Is.False);
            scope.Dispose(); scope.Dispose();
            Jump(); Assert.That(jumps, Is.Zero, "A blocked buffered press must not jump on return to Hub");
            Set("jumpBufferTimer", 1); Set("coyoteTimer", 1);
            Jump(); push.RequestPush();
            Assert.That(jumps, Is.EqualTo(1)); Assert.That(punches, Is.EqualTo(1));
        }

        [Test] public void RestoresPreExistingRestrictionsAndCancelsPendingPunch()
        {
            player.JumpInputSuppressed = true; push.InputSuppressed = true;
            scope.Bind(player); scope.Dispose();
            Assert.That(player.JumpInputSuppressed, Is.True); Assert.That(push.InputSuppressed, Is.True);
            push.InputSuppressed = false; push.RequestPush();
            var pending = typeof(PlayerPushAbility).GetField("impactPending", Private);
            Assert.That(pending.GetValue(push), Is.True);
            scope.Bind(player);
            Assert.That(pending.GetValue(push), Is.False);
        }
    }
}
