using System.Reflection;
using Igruha.Core.CameraSystems;
using Igruha.Minigames.DuckHunt;
using NUnit.Framework;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class HunterSceneReloadTests
    {
        [Test]
        public void FormerHunterCanDetachAfterItsSceneCameraWasDestroyed()
        {
            var avatar = new GameObject("Persistent avatar");
            var camera = new GameObject("Old scene camera");
            avatar.SetActive(false);
            camera.SetActive(false);
            try
            {
                var hunter = avatar.AddComponent<HunterController>();
                var rig = camera.AddComponent<FirstPersonCameraRig>();
                typeof(HunterController).GetField("cameraRig", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(hunter, rig);
                Object.DestroyImmediate(camera);
                Assert.DoesNotThrow(hunter.Detach);
                Assert.DoesNotThrow(hunter.Detach);
                Assert.That(hunter.Active, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(avatar);
                if (camera != null) Object.DestroyImmediate(camera);
            }
        }
    }
}
