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
            while ((!game.LocalRosterReady || game.LocalParticipant.Motor.MovementLocked) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(game.LocalRosterReady, Is.True);
            player = game.LocalParticipant;
            Assert.That(player.Motor.MovementLocked, Is.False);
            rig = Object.FindFirstObjectByType<FirstPersonCameraRig>();
            // Exercise the real motor/animation/camera with held crouch, without editor focus affecting input.
            player.Motor.CrouchInputSuppressed = true;
            // Camera regression needs a held weapon, independent of random spawns/dummy pickups.
            var round = game.Round;
            round.ApplyHeader(OneBulletRound.Nobody, 0, 0, round.BeginsAt, round.EndsAt,
                round.SpawnAt, false, OneBulletRound.Nobody);
            Assert.That(round.Take(game.LocalId, NetworkClock.Now), Is.True);
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

        [UnityTest]
        public IEnumerator RaisedCrouchViewDoesNotShootOverLowCover()
        {
            player.Motor.TeleportTo(game.Storm.Layout.StandingPoint(50), Quaternion.identity);
            rig.SetView(0, 0);
            yield return SetCrouch(true);
            var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cover.layer = 8;
            cover.transform.localScale = new Vector3(1.4f, .95f, .1f);
            cover.transform.position = player.Motor.Position + Vector3.forward * .9f + Vector3.up * .475f;
            bool fired = false; Vector3 impact = Vector3.zero;
            System.Action<Vector3, Vector3, bool> onShot = (origin, hit, lethal) => { fired = true; impact = hit; };
            game.Shot += onShot;
            try
            {
                yield return new WaitForFixedUpdate(); yield return null;
                Assert.Greater(Camera.main.transform.position.y, cover.GetComponent<Collider>().bounds.max.y);
                Assert.True(game.HandlePushButton(player.Motor));
                yield return new WaitForFixedUpdate(); yield return null;
                Assert.True(fired);
                Assert.That(impact.z, Is.EqualTo(cover.transform.position.z - .05f).Within(.03f),
                    "Seeing above cover must not move the authoritative bullet above it.");
            }
            finally { game.Shot -= onShot; Object.Destroy(cover); }
        }

        [UnityTest]
        public IEnumerator ThrownCanIsVisibleFromFirstPerson()
        {
            // Aim down a real open corridor, independent of the randomized player spawn.
            player.Motor.TeleportTo(game.Storm.Layout.StandingPoint(50), Quaternion.identity);
            rig.SetView(0, 0);
            yield return new WaitForSeconds(PoseSettleSeconds);
            int before = game.Round.Find(game.LocalId).Cans;
            game.Decoys.QueueThrow(game.LocalId, Vector3.forward);
            yield return new WaitForFixedUpdate(); yield return null;
            Assert.AreEqual(before - 1, game.Round.Find(game.LocalId).Cans);
            Transform can = null;
            foreach (Transform child in game.Decoys.transform)
                if (child.name.StartsWith("DecoyCan") && child.gameObject.activeSelf) { can = child; break; }
            Assert.NotNull(can, "The accepted throw needs a visible world model.");
            var renderers = can.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var camera = Camera.main;
            Assert.True(GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), bounds));
            Vector3 top = camera.WorldToViewportPoint(bounds.center + camera.transform.up * bounds.extents.magnitude);
            Vector3 bottom = camera.WorldToViewportPoint(bounds.center - camera.transform.up * bounds.extents.magnitude);
            Assert.Greater(top.y - bottom.y, .03f, "The can should visibly occupy the frame, not be a millimetre-sized speck.");
        }

        private void AssertEyeLevel()
        {
            float expected = player.Motor.transform.position.y + OneBulletFirstPerson.ViewEyeHeight(player.Capsule);
            Assert.That(Camera.main.transform.position.y - player.Motor.transform.position.y,
                Is.GreaterThanOrEqualTo(1.03f), "Crouching must not put the viewpoint near the floor.");
            Assert.That(rig.transform.position.y, Is.EqualTo(expected).Within(EyeTolerance),
                "View must apply game-local crouch framing exactly once, including after rebinding.");
            Assert.That(Camera.main.transform.position.y, Is.EqualTo(expected).Within(EyeTolerance),
                "The rendered camera must use the minigame's crouch framing.");
        }
    }
}
