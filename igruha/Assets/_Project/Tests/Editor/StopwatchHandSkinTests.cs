using System.Linq;
using System.Reflection;
using Igruha.Core.Player;
using Igruha.Minigames.Stopwatch;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class StopwatchHandSkinTests
    {
        [Test]
        public void DescentReleasesButtonMovementLockBeforeTheNextLateUpdate()
        {
            var container=new GameObject("Button stage test");container.SetActive(false);
            var avatar=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Player.prefab"));
            var stage=container.AddComponent<Igruha.Core.Minigame.MinigameStageState>();
            var presentation=container.AddComponent<StopwatchButtonPresentation>();
            var owner=avatar.GetComponent<PlayerController>();
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var type=typeof(StopwatchButtonPresentation);
            try
            {
                type.GetMethod("Awake",flags).Invoke(presentation,null);
                type.GetMethod("OnEnable",flags).Invoke(presentation,null);
                type.GetField("owner",flags).SetValue(presentation,owner);
                type.GetField("active",flags).SetValue(presentation,true);
                type.GetField("movementWasLocked",flags).SetValue(presentation,false);
                owner.MovementLocked=true;
                stage.EnterStage(4,2);
                Assert.That(owner.MovementLocked,Is.False,"The network stage must release movement synchronously.");
                Assert.That(presentation.IsActive,Is.False);
            }
            finally
            {
                type.GetMethod("OnDisable",flags).Invoke(presentation,null);
                Object.DestroyImmediate(container);Object.DestroyImmediate(avatar);
            }
        }

        [TestCase("Karlan")]
        [TestCase("Boss")]
        [TestCase("Shlanga")]
        [TestCase("Fat")]
        [TestCase("MyBoy")]
        [TestCase("Girl")]
        [TestCase("Milez")]
        [TestCase("Aza")]
        public void EachAvatarUsesItsOwnMaterialsAndTouchesWithActualFingerSkin(string name)
        {
            var roster=AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            var library=AssetDatabase.LoadAssetAtPath<StopwatchHandLibrary>("Assets/_Project/Settings/Gameplay/Minigames/StopwatchHandLibrary.asset");
            var character=roster.Characters.Single(c=>c.DisplayName==name);
            var source=character.Prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var prefab=library.Find(character.Prefab.GetComponent<PlayerController>());
            Assert.That(prefab,Is.Not.Null);
            var hand=Object.Instantiate(prefab);
            var baked=new Mesh();
            try
            {
                typeof(StopwatchHandRig).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hand,null);
                var skin=hand.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(skin.sharedMesh,Is.Not.SameAs(source.sharedMesh));
                Assert.That(skin.sharedMaterials,Is.EqualTo(source.sharedMaterials));
                Assert.That(hand.GetComponentsInChildren<Animator>(),Is.Empty);
                Assert.That(hand.GetComponentsInChildren<Collider>(),Is.Empty);
                Assert.That(hand.ContactPosition.magnitude,Is.LessThan(.6f),"FBX centimetres must not be applied twice");
                Vector3 buttonTop=new Vector3(7,9.5f,-2);
                hand.Place(buttonTop,Quaternion.Euler(12,137,0));
                Assert.That(Vector3.Distance(hand.ContactPosition,buttonTop),Is.LessThan(.001f));
                skin.BakeMesh(baked,true);
                float distance=baked.vertices.Min(v=>Vector3.Distance(skin.transform.TransformPoint(v),buttonTop));
                Assert.That(distance,Is.LessThan(.002f),"The contact anchor must lie on the rendered skin, not merely on a bone");
            }
            finally {Object.DestroyImmediate(baked);Object.DestroyImmediate(hand.gameObject);}
        }
    }
}
