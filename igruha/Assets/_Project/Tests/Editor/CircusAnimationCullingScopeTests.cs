using System.Reflection;
using Igruha.Core.Player;
using Igruha.Minigames.Circus;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class CircusAnimationCullingScopeTests
    {
        private GameObject avatar;
        private PlayerController player;
        private Animator animator;

        [SetUp] public void CreateShippingPlayer()
        {
            avatar=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Player.prefab"));
            foreach(var behaviour in avatar.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
            player=avatar.GetComponent<PlayerController>();animator=avatar.GetComponentInChildren<Animator>();
            Assert.That(animator,Is.Not.Null);
        }

        [TearDown] public void Clean()
        { if(avatar!=null)Object.DestroyImmediate(avatar); }

        [TestCase(AnimatorCullingMode.CullUpdateTransforms)]
        [TestCase(AnimatorCullingMode.CullCompletely)]
        [TestCase(AnimatorCullingMode.AlwaysAnimate)]
        public void RepeatedPitEntryKeepsOriginalModeUntilRoundEnd(AnimatorCullingMode original)
        {
            animator.cullingMode=original;
            CircusAnimationCullingScope.Bind(player);
            CircusAnimationCullingScope.Bind(player);
            var scope=player.GetComponent<CircusAnimationCullingScope>();
            Assert.That(scope.IsActive,Is.True);
            Assert.That(animator.cullingMode,Is.EqualTo(AnimatorCullingMode.AlwaysAnimate),
                "Current body geometry must not depend on which player the host camera sees.");
            Assert.That(avatar.GetComponents<CircusAnimationCullingScope>().Length,Is.EqualTo(1));
            CircusAnimationCullingScope.Release(player);
            CircusAnimationCullingScope.Release(player);
            Assert.That(scope.IsActive,Is.False);
            Assert.That(animator.cullingMode,Is.EqualTo(original),"Release must restore the saved mode, not a guessed default.");
        }

        [Test] public void DisableRestoresAndNextRoundCapturesTheNewPolicy()
        {
            animator.cullingMode=AnimatorCullingMode.CullUpdateTransforms;
            CircusAnimationCullingScope.Bind(player);
            var scope=player.GetComponent<CircusAnimationCullingScope>();
            scope.enabled=false;
            Lifecycle(scope,"OnDisable");
            Assert.That(animator.cullingMode,Is.EqualTo(AnimatorCullingMode.CullUpdateTransforms));
            Assert.That(scope.IsActive,Is.False);
            animator.cullingMode=AnimatorCullingMode.CullCompletely;
            CircusAnimationCullingScope.Bind(player);
            Assert.That(scope.enabled,Is.True);
            Assert.That(animator.cullingMode,Is.EqualTo(AnimatorCullingMode.AlwaysAnimate));
            avatar.SetActive(false);
            Lifecycle(scope,"OnDisable");
            Assert.That(animator.cullingMode,Is.EqualTo(AnimatorCullingMode.CullCompletely));
        }

        [Test] public void DestroyingOnlyTheCircusScopeRestoresTheSurvivingCharacter()
        {
            animator.cullingMode=AnimatorCullingMode.CullUpdateTransforms;
            CircusAnimationCullingScope.Bind(player);
            var scope=player.GetComponent<CircusAnimationCullingScope>();
            Lifecycle(scope,"OnDestroy");
            Object.DestroyImmediate(scope);
            Assert.That(animator.cullingMode,Is.EqualTo(AnimatorCullingMode.CullUpdateTransforms));
        }
        // EditMode does not run the play-loop callbacks of a regular MonoBehaviour.
        // Invoke the same lifetime boundary explicitly, as the contact tests do for LateUpdate.
        private static void Lifecycle(CircusAnimationCullingScope scope,string method)
        { typeof(CircusAnimationCullingScope).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(scope,null); }
    }
}
