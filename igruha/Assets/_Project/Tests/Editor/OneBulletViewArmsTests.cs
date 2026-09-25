using Igruha.Core.Player;
using Igruha.Minigames.OneBullet;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class OneBulletViewArmsTests
    {
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        public void EveryCharacterHasStandaloneArmsWithValidSkin(int index)
        {
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Minigames/OneBullet/ViewArms/Arms_" + roster.Characters[index].DisplayName + ".prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true), Has.Length.EqualTo(1), "No gameplay or network behaviours in the local copy");
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Animator>(true), Is.Empty);
            var fields = new SerializedObject(prefab.GetComponent<OneBulletViewArms>());
            foreach (string side in new[] { "left", "right" })
            {
                var arm = fields.FindProperty(side);
                foreach (string bone in new[] { "upper", "lower", "hand" })
                    Assert.That(arm.FindPropertyRelative(bone).objectReferenceValue, Is.Not.Null);
                Assert.That(arm.FindPropertyRelative("handReach").floatValue, Is.GreaterThan(0));
            }
            var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            foreach (var renderer in renderers)
            {
                Assert.That(renderer.sharedMesh, Is.Not.Null, "Native mesh replacement must retain prefab references");
                Assert.That(renderer.sharedMesh.vertexCount, Is.GreaterThan(1000));
                Assert.That(renderer.bones, Has.None.Null);
                Assert.That(renderer.sharedMesh.bindposes.Length, Is.EqualTo(renderer.bones.Length));
                foreach (var w in renderer.sharedMesh.boneWeights)
                    Assert.That(w.weight0 + w.weight1 + w.weight2 + w.weight3, Is.EqualTo(1).Within(.002f));
            }
        }
        [TestCase(.15f)] [TestCase(.25f)] [TestCase(.4f)]
        public void PunchPeakMatchesCharactersImpactDelay(float delay)
        {
            Assert.That(OneBulletFirstPerson.PunchReach(0, delay), Is.Zero);
            Assert.That(OneBulletFirstPerson.PunchReach(delay, delay), Is.EqualTo(1));
            Assert.That(OneBulletFirstPerson.PunchReach(delay + .3f, delay), Is.Zero);
        }
    }
}
