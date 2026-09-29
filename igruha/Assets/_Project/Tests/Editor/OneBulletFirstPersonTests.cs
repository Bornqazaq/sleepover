using Igruha.Core.CameraSystems;
using Igruha.Minigames.OneBullet;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Igruha.Tests
{
    public sealed class OneBulletFirstPersonTests
    {
        [Test]
        public void EyeAndShotOriginStayInsideStandingAndCrouchingCapsule()
        {
            var go = new GameObject("Eye test");
            try
            {
                var capsule = go.AddComponent<CapsuleCollider>(); capsule.radius = .36f;
                capsule.height = 1.65f; capsule.center = Vector3.up * .825f;
                float standing = OneBulletMinigame.EyeHeight(capsule);
                Assert.That(standing, Is.EqualTo(1.5f).Within(.001f));
                capsule.height = .9f; capsule.center = Vector3.up * .45f;
                float crouched = OneBulletMinigame.EyeHeight(capsule);
                Assert.That(crouched, Is.EqualTo(.75f).Within(.001f));
                Assert.That(crouched, Is.LessThan(standing));
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test]
        public void OnlyRegisteredOverlayOfEnabledBaseSharesOutput()
        {
            var root = new GameObject("Camera stack test");
            var child = new GameObject("Overlay test");
            try
            {
                var main = root.AddComponent<Camera>();
                var overlay = child.AddComponent<Camera>();
                var mainData = main.GetUniversalAdditionalCameraData();
                overlay.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Overlay;
                var cameras = new[] { main, overlay };
                Assert.That(SceneCameraGuard.IsStackedOverlay(overlay, cameras), Is.False, "Orphan must be reported");
                mainData.cameraStack.Add(overlay);
                Assert.That(SceneCameraGuard.IsStackedOverlay(overlay, cameras), Is.True);
                Assert.That(SceneCameraGuard.IsStackedOverlay(main, cameras), Is.False);
                main.enabled = false;
                Assert.That(SceneCameraGuard.IsStackedOverlay(overlay, cameras), Is.False);
            }
            finally { Object.DestroyImmediate(child); Object.DestroyImmediate(root); }
        }
    }
}
