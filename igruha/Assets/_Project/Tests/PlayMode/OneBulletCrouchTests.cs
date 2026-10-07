using System.Collections;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Minigames.OneBullet;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Igruha.Tests.PlayMode
{
    public sealed class OneBulletCrouchTests
    {
        private const float PoseSettleSeconds = .3f;
        private const float EyeTolerance = .02f;
        private OneBulletMinigame game;
        private OneBulletParticipant player;
        private FirstPersonCameraRig rig;
        private bool previousBackground;

        [UnitySetUp]
        public IEnumerator Open()
        {
            previousBackground = Application.runInBackground;
            Application.runInBackground = true;
            yield return SceneManager.LoadSceneAsync("OneBullet", LoadSceneMode.Single);
            game = Object.FindFirstObjectByType<OneBulletMinigame>();
            float deadline = Time.realtimeSinceStartup + 20f;
            while ((!game.LocalRosterReady || game.LocalParticipant.Motor.MovementLocked || game.Round.Pickup < 0) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(game.LocalRosterReady, Is.True);
            player = game.LocalParticipant;
            Assert.That(player.Motor.MovementLocked, Is.False);
            rig = Object.FindFirstObjectByType<FirstPersonCameraRig>();
            // Exercise the real motor/animation/camera with held crouch, without editor focus affecting input.
            player.Motor.CrouchInputSuppressed = true;
            Assert.That(game.Round.Take(game.LocalId, NetworkClock.Now), Is.True);
            Assert.That(game.LocalArmed, Is.True);
        }

        [UnityTearDown]
        public IEnumerator Close()
        {
            if (player?.Motor != null)
            {
                player.Motor.SetCrouched(false);
                player.Motor.CrouchInputSuppressed = false;
            }
            if (game != null && game.GameplayActive) game.EndMinigame();
            Application.runInBackground = previousBackground;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ArmedCrouchStaysAtEyeLevelAfterCameraRebind()
        {
            yield return SetCrouch(true);
            AssertEyeLevel();
            // Camera activation captures the CURRENT capsule height, which may already be crouched.
            rig.enabled = false;
            rig.enabled = true;
            yield return new WaitForSeconds(PoseSettleSeconds);
            AssertEyeLevel();
            yield return SetCrouch(false);
            AssertEyeLevel();
            yield return SetCrouch(true);
            AssertEyeLevel();
        }

        [UnityTest]
        public IEnumerator UnarmedCrouchAndStandingRecoverAfterCameraRebind()
        {
            yield return SetCrouch(true);
            rig.enabled = false;
            rig.enabled = true;
            Assert.That(game.Round.Fire(game.LocalId, OneBulletRound.Nobody, NetworkClock.Now,
                game.Config.RespawnDelay), Is.True);
            yield return new WaitForSeconds(PoseSettleSeconds);
            Assert.That(game.LocalArmed, Is.False);
            AssertEyeLevel();
            yield return SetCrouch(false);
            AssertEyeLevel();
        }

        private IEnumerator SetCrouch(bool crouched)
        {
            player.Motor.SetCrouched(crouched);
            yield return new WaitForSeconds(PoseSettleSeconds);
            Assert.That(player.Motor.IsCrouched, Is.EqualTo(crouched));
            Assert.That(player.Motor.IsKnockedDown, Is.False);
        }

        private void AssertEyeLevel()
        {
            float expected = OneBulletMinigame.ShotOrigin(player).y;
            Assert.That(rig.transform.position.y, Is.EqualTo(expected).Within(EyeTolerance),
                "View must follow the current capsule exactly once, including after rebinding while crouched.");
            Assert.That(Camera.main.transform.position.y, Is.EqualTo(expected).Within(EyeTolerance),
                "The rendered camera must agree with the authoritative shot origin.");
        }
    }
}
