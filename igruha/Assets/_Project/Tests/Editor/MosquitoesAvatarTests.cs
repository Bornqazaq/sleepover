using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Tests
{
    public sealed class MosquitoesAvatarTests
    {
        [Test]
        public void CoincidentImportedWingPivotsStillFlapInOppositeDirections()
        {
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Minigames/Mosquitoes/Characters/" + roster.Characters[0].DisplayName + ".prefab");
            var body = new GameObject("wing test");
            try
            {
                var visual = Object.Instantiate(prefab, body.transform);
                Transform left = null, right = null;
                foreach (var t in visual.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "WingPivot_-1_0") left = t;
                    if (t.name == "WingPivot_1_0") right = t;
                }
                Assert.That(left, Is.Not.Null); Assert.That(right, Is.Not.Null);
                Quaternion l = left.localRotation, r = right.localRotation;
                var pose = new Igruha.Minigames.Mosquitoes.MosquitoFlightVisual(visual.transform, 0);
                pose.Tick(Vector3.forward * 4.8f, 4.8f, false, false, true, Vector3.zero, .013f);
                Quaternion dl = left.localRotation * Quaternion.Inverse(l), dr = right.localRotation * Quaternion.Inverse(r);
                Assert.That(Quaternion.Angle(dl, Quaternion.identity), Is.GreaterThan(10));
                Assert.That(Quaternion.Angle(dl * dr, Quaternion.identity), Is.LessThan(.05f), "left and right must mirror even when both hinges are at zero");
            }
            finally { Object.DestroyImmediate(body); }
        }
        [Test]
        public void EveryGiantFitsMattressAndRestoresOriginalModelTransform()
        {
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            foreach (var character in roster.Characters)
            {
                var clone = Object.Instantiate(character.Prefab);
                try
                {
                    clone.transform.SetPositionAndRotation(new Vector3(-2.4f, .68f, -1.1f), Quaternion.Euler(0,90,0));
                    var animator = clone.GetComponentInChildren<Animator>(); animator.Rebind(); animator.Update(0);
                    var position = animator.transform.localPosition; var rotation = animator.transform.localRotation;
                    var rig = clone.AddComponent<Igruha.Minigames.Mosquitoes.MosquitoGiantRig>(); rig.Initialize(animator); rig.SetSleeping(true);
                    var bounds = rig.SkinBounds;
                    Assert.That(bounds.min.x, Is.GreaterThan(-3.02f), character.DisplayName + " headboard clearance");
                    Assert.That(bounds.max.x, Is.LessThan(-.10f), character.DisplayName + " feet stay on mattress");
                    Assert.That(bounds.min.z, Is.GreaterThan(-2.01f), character.DisplayName);
                    Assert.That(bounds.max.z, Is.LessThan(-.19f), character.DisplayName);
                    Assert.That(rig.BackContactHeight, Is.InRange(.665f,.680f), character.DisplayName + " back must touch the mattress, independently of the lowest hand/heel");
                    Assert.That(bounds.min.y, Is.GreaterThan(.645f), character.DisplayName + " skin cannot sink into mattress");
                    Assert.That(rig.ContactCount, Is.EqualTo(10), character.DisplayName);
                    rig.SetSleeping(false); rig.Restore();
                    Assert.That(animator.transform.localPosition, Is.EqualTo(position));
                    Assert.That(Quaternion.Angle(animator.transform.localRotation, rotation), Is.LessThan(.01f));
                    Assert.That(animator.enabled);
                }
                finally { Object.DestroyImmediate(clone); }
            }
        }
        [Test]
        public void AllEightVariantsKeepOriginalMeshesMaterialsAndIsolateGameplay()
        {
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            Assert.That(roster.Characters.Count, Is.EqualTo(8));
            foreach (var character in roster.Characters)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Minigames/Mosquitoes/Characters/" + character.DisplayName + ".prefab");
                Assert.That(prefab, Is.Not.Null, character.DisplayName);
                var original = character.Prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var copies = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Assert.That(copies.Length, Is.EqualTo(original.Length), character.DisplayName);
                for (int i = 0; i < copies.Length; i++)
                {
                    Assert.That(copies[i].sharedMesh, Is.SameAs(original[i].sharedMesh), character.DisplayName);
                    CollectionAssert.AreEqual(original[i].sharedMaterials, copies[i].sharedMaterials, character.DisplayName);
                }
                Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty, "Visual variants cannot run player/network logic");
                Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
                int wings = 0;
                foreach (var t in prefab.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("WingPivot_")) wings++;
                Assert.That(wings, Is.EqualTo(4), character.DisplayName);
                Assert.That(prefab.transform.localScale.x, Is.InRange(.07f, .20f), "Only the minigame visual is miniaturized");
            }
        }
    }
}
