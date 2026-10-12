using Igruha.Core.Player;
using Igruha.Minigames.Mosquitoes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class MosquitoesMotionTests
    {
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        public void BedTransitionsKeepSkinOutOfMattress(int index)
        {
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            var clone = Object.Instantiate(roster.Characters[index].Prefab);
            try
            {
                clone.transform.SetPositionAndRotation(MosquitoRoomLayout.BedExit, Quaternion.Euler(0, 180, 0));
                var animator = clone.GetComponentInChildren<Animator>(); animator.Rebind(); animator.Update(0);
                var motion = new MosquitoSleepMotion(animator);
                var standing = motion.Capture();
                var rig = clone.AddComponent<MosquitoGiantRig>(); rig.Initialize(animator); rig.SetSleeping(true);
                var sleeping = motion.Capture(); motion.SetRest();
                foreach (bool down in new[] { true, false })
                {
                    motion.Begin(down ? standing : sleeping, down ? sleeping : standing, down);
                    for (int frame = 0; frame <= 40; frame++)
                    {
                        motion.Sample(frame / 40f);
                        float lowest = LowestInteriorSkin(clone);
                        Assert.That(lowest, Is.GreaterThanOrEqualTo(MosquitoRoomLayout.MattressHeight - .035f),
                            roster.Characters[index].DisplayName + " down=" + down + " frame=" + frame + " skin=" + lowest);
                    }
                }
            }
            finally { Object.DestroyImmediate(clone); }
        }

        private static float LowestInteriorSkin(GameObject root)
        {
            float lowest = float.PositiveInfinity;
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!CharacterSkinProbes.Shared.TryGet(skin.sharedMesh, out var entry)) continue;
                var matrices = new Matrix4x4[skin.bones.Length];
                for (int i = 0; i < matrices.Length; i++) matrices[i] = skin.bones[i].localToWorldMatrix * entry.BindPoses[i];
                foreach (var probe in entry.Probes)
                {
                    Vector3 world = Vector3.zero;
                    for (int i = 0; i < 4; i++) if (probe.Weights[i] > 0)
                        world += matrices[probe.Bone(i)].MultiplyPoint3x4(probe.Position) * probe.Weights[i];
                    // Exclude the soft edge overhang; check the solid interior of the mattress.
                    if (world.x > -2.60f && world.x < -.48f && world.z > -1.65f && world.z < -.445f)
                        lowest = Mathf.Min(lowest, world.y);
                }
            }
            return lowest;
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        public void BiteBendsLimbsWithoutInvertingTheWholeCharacterAndReleases(int index)
        {
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Minigames/Mosquitoes/Characters/" + roster.Characters[index].DisplayName + ".prefab");
            var body = new GameObject("Bite motion test");
            try
            {
                var visual = Object.Instantiate(prefab, body.transform);
                var animator = visual.GetComponentInChildren<Animator>();
                var arm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var leg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                Quaternion armRest = arm.localRotation, legRest = leg.localRotation;
                var motion = new MosquitoFlightVisual(visual.transform, index);
                for (int i = 0; i < 30; i++) motion.Tick(Vector3.zero, 4.8f, true, false, true, 1f / 60, i / 30f);
                Assert.That(Vector3.Dot(visual.transform.up, Vector3.up), Is.GreaterThan(.35f));
                Assert.That(Quaternion.Angle(armRest, arm.localRotation), Is.GreaterThan(10));
                Assert.That(Quaternion.Angle(legRest, leg.localRotation), Is.GreaterThan(10));
                for (int i = 0; i < 40; i++) motion.Tick(Vector3.forward, 4.8f, false, false, true, 1f / 60);
                Assert.That(Quaternion.Angle(armRest, arm.localRotation), Is.LessThan(.1f));
                Assert.That(Quaternion.Angle(legRest, leg.localRotation), Is.LessThan(.1f));
            }
            finally { Object.DestroyImmediate(body); }
        }
    }
}
