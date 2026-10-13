using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Minigames.Mosquitoes;

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
                pose.Tick(Vector3.forward * 4.8f, 4.8f, false, false, true, .013f);
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
                    Assert.That(bounds.min.x, Is.GreaterThan(-2.63f), character.DisplayName + " headboard clearance");
                    Assert.That(bounds.max.x, Is.LessThan(-.38f), character.DisplayName + " feet stay on mattress");
                    Assert.That(bounds.min.z, Is.GreaterThan(-1.73f), character.DisplayName);
                    Assert.That(bounds.max.z, Is.LessThan(-.37f), character.DisplayName);
                    Assert.That(rig.BackContactHeight, Is.InRange(.665f,.680f), character.DisplayName + " back must touch the mattress, independently of the lowest hand/heel");
                    Assert.That(bounds.min.y, Is.GreaterThan(.645f), character.DisplayName + " skin cannot sink into mattress");
                    var shell = clone.transform.Find("MosquitoGiantContacts");
                    Assert.That(shell, Is.Not.Null, "collision shell must belong to the avatar");
                    Assert.That(shell.gameObject.hideFlags & HideFlags.DontSaveInEditor, Is.Not.Zero);
                    var probe = new GameObject("bite landing probe");
                    try
                    {
                        var sphere = probe.AddComponent<SphereCollider>(); sphere.radius = .12f;
                        for (int slot = 0; slot < 7; slot++)
                        {
                            Vector3 target = rig.LandingPoint(slot);
                            bool skinBelow = false;
                            foreach (var contact in shell.GetComponentsInChildren<Collider>())
                            {
                                if (!contact.enabled) continue;
                                Assert.That(Physics.ComputePenetration(sphere, target, Quaternion.identity,
                                    contact, contact.transform.position, contact.transform.rotation, out _, out _),
                                    Is.False, character.DisplayName + " landing slot " + slot + " intersects skin physics");
                                skinBelow |= Physics.ComputePenetration(sphere, target + Vector3.down * .04f, Quaternion.identity,
                                    contact, contact.transform.position, contact.transform.rotation, out _, out _);
                            }
                            Assert.That(skinBelow, Is.True, character.DisplayName + " landing must actually touch skin, not hover over a hole");
                            Vector3 swatCenter = MosquitoRoomLayout.BedExit + Vector3.up * .9f;
                            Assert.That(Vector3.Distance(swatCenter, target), Is.LessThan(1.42f),
                                character.DisplayName + " wake-up counterattack must reach a lingering attacker");
                        }
                    }
                    finally { Object.DestroyImmediate(probe); }
                    rig.SetSleeping(false); rig.Restore();
                    Assert.That(clone.transform.Find("MosquitoGiantContacts"), Is.Null);
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
