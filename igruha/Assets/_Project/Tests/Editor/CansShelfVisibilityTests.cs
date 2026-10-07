using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Minigames.CansOrder;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.Tests
{
    public sealed class CansShelfVisibilityTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject root;
        private Transform avatar;
        private Transform shelf;
        private Camera output;
        private Camera other;
        private Renderer visible;
        private Renderer alreadyHidden;
        private Renderer disabled;
        private MinigameCameraController cameras;
        private CansShelfVisibility visibility;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Shelf visibility fixtures");
            avatar = Child("Avatar");
            avatar.gameObject.AddComponent<CapsuleCollider>();
            visible = Child("Body", avatar).gameObject.AddComponent<SkinnedMeshRenderer>();
            alreadyHidden = Child("Hidden accessory", avatar).gameObject.AddComponent<MeshRenderer>();
            alreadyHidden.forceRenderingOff = true;
            disabled = Child("Disabled placeholder", avatar).gameObject.AddComponent<MeshRenderer>();
            disabled.enabled = false;
            output = Child("Output").gameObject.AddComponent<Camera>();
            other = Child("Scene view").gameObject.AddComponent<Camera>();
            shelf = Child("Shelf rig");
            var fixedCamera = shelf.gameObject.AddComponent<CinemachineCamera>();
            var thirdPerson = Child("Third person").gameObject.AddComponent<CinemachineCamera>();
            cameras = Child("Minigame cameras").gameObject.AddComponent<MinigameCameraController>();
            typeof(MinigameCameraController).GetField("fixedRig", Private).SetValue(cameras, fixedCamera);
            typeof(MinigameCameraController).GetField("thirdPersonRig", Private).SetValue(cameras, thirdPerson);
            cameras.Apply(CameraMode.Fixed, shelf);
            visibility = new CansShelfVisibility(cameras, shelf);
            Bind(avatar);
        }

        [TearDown]
        public void TearDown()
        {
            visibility.Dispose();
            Object.DestroyImmediate(root);
        }

        [Test]
        public void OnlyShelfOutputHidesModelAndRestoresExactStateAfterFrame()
        {
            Begin(other);
            Assert.That(visible.forceRenderingOff, Is.False);
            End(other);
            Begin(output);
            Assert.That(visible.forceRenderingOff, Is.True);
            Assert.That(visible.enabled, Is.True);
            Assert.That(disabled.enabled, Is.False);
            Assert.That(avatar.GetComponent<CapsuleCollider>().enabled, Is.True);
            End(output);
            AssertOriginalState();
        }

        [Test]
        public void NestedCameraSeesAvatarAndReturnsToHiddenShelfBeforeRestoring()
        {
            Begin(output);
            Begin(other);
            AssertOriginalState();
            End(other);
            Assert.That(visible.forceRenderingOff, Is.True);
            End(output);
            AssertOriginalState();
        }

        [Test]
        public void ThirdPersonOrDifferentFixedTargetDoesNotHideAvatar()
        {
            cameras.Apply(CameraMode.ThirdPerson, avatar);
            Begin(output);
            AssertOriginalState();
            End(output);
            cameras.Apply(CameraMode.Fixed, avatar);
            Begin(output);
            AssertOriginalState();
            End(output);
        }

        [Test]
        public void DeactivationAndSceneCleanupRestoreInterruptedRender()
        {
            Begin(output);
            visibility.SetActive(false);
            AssertOriginalState();
            Bind(avatar);
            Begin(output);
            visibility.Dispose();
            AssertOriginalState();
        }

        [Test]
        public void RebindingDoesNotLeaveOldAvatarHidden()
        {
            Begin(output);
            var replacement = Child("Replacement avatar");
            var renderer = replacement.gameObject.AddComponent<SkinnedMeshRenderer>();
            Bind(replacement);
            AssertOriginalState();
            Begin(output);
            Assert.That(renderer.forceRenderingOff, Is.True);
            AssertOriginalState();
            End(output);
            Assert.That(renderer.forceRenderingOff, Is.False);
        }

        [Test]
        public void RenderContextEndRecoversMissingCameraEnd()
        {
            Begin(output);
            typeof(CansShelfVisibility).GetMethod("EndContext", Private).Invoke(visibility,
                new object[] { default(ScriptableRenderContext), new List<Camera> { output } });
            AssertOriginalState();
        }

        [Test]
        public void DestroyingAvatarDuringRenderIsSafe()
        {
            Begin(output);
            Object.DestroyImmediate(avatar.gameObject);
            Assert.DoesNotThrow(() => End(output));
            Assert.DoesNotThrow(() => visibility.Dispose());
        }

        [TestCase("Player")]
        [TestCase("Aza")]
        [TestCase("Boss")]
        [TestCase("Fat")]
        [TestCase("Girl")]
        [TestCase("Milez")]
        [TestCase("MyBoy")]
        [TestCase("Shlanga")]
        public void EveryCharacterMeshIsExcludedWithoutChangingEnabledState(string prefabName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Player/" + prefabName + ".prefab");
            Assert.That(prefab, Is.Not.Null);
            var instance = Object.Instantiate(prefab, root.transform);
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers.Length, Is.GreaterThan(0));
            var enabled = new bool[renderers.Length];
            var off = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                enabled[i] = renderers[i].enabled;
                off[i] = renderers[i].forceRenderingOff;
            }
            Bind(instance.transform);
            Begin(output);
            for (int i = 0; i < renderers.Length; i++)
            {
                Assert.That(renderers[i].forceRenderingOff, Is.True, renderers[i].name);
                Assert.That(renderers[i].enabled, Is.EqualTo(enabled[i]), renderers[i].name);
            }
            End(output);
            for (int i = 0; i < renderers.Length; i++)
                Assert.That(renderers[i].forceRenderingOff, Is.EqualTo(off[i]), renderers[i].name);
        }

        private Transform Child(string name, Transform parent = null)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent != null ? parent : root.transform);
            return child;
        }

        private void Bind(Transform target)
        {
            visibility.Bind(target);
            // Render callbacks are driven explicitly: editor tests do not own
            // the open scene's CinemachineBrain or its Game view render loop.
            typeof(CansShelfVisibility).GetField("outputCamera", Private).SetValue(visibility, output);
            visibility.SetActive(true);
        }

        private void Begin(Camera camera) => typeof(CansShelfVisibility).GetMethod("BeginCamera", Private)
            .Invoke(visibility, new object[] { default(ScriptableRenderContext), camera });

        private void End(Camera camera) => typeof(CansShelfVisibility).GetMethod("EndCamera", Private)
            .Invoke(visibility, new object[] { default(ScriptableRenderContext), camera });

        private void AssertOriginalState()
        {
            Assert.That(visible.forceRenderingOff, Is.False);
            Assert.That(alreadyHidden.forceRenderingOff, Is.True);
            Assert.That(disabled.enabled, Is.False);
        }
    }
}
