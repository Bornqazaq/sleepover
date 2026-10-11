#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Igruha.Tests.PlayMode
{
    public sealed class CharacterPolishTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private bool background;
        private static readonly string[] Names = { "Player", "Boss", "Shlanga", "Fat", "Milez", "MyBoy", "Aza", "Girl" };

        [SetUp]
        public void SetUp()
        {
            background = Application.runInBackground;
            Application.runInBackground = true;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            created.Add(floor); floor.layer = LayerMask.NameToLayer("Ground");
            floor.transform.position = new Vector3(1015f, -.5f, 1020f);
            floor.transform.localScale = new Vector3(100f, 1f, 100f);
        }

        private PlayerController Spawn(string name, int lane = 0)
        {
            var body = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/" + name + ".prefab"));
            created.Add(body);
            var motor = body.GetComponent<PlayerController>();
            motor.TeleportTo(new Vector3(1000 + lane * 4, .05f, 1000), Quaternion.identity);
            body.GetComponent<PlayerInputReader>().EngageAutopilot();
            body.GetComponentInChildren<Animator>().cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var basis = new GameObject("Movement basis"); created.Add(basis);
            motor.SetCameraReference(basis.transform);
            return motor;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in created) if (go != null) Object.Destroy(go);
            created.Clear(); Application.runInBackground = background;
        }

        [UnityTest]
        public IEnumerator CrouchForwardKeepsAllBodiesAndVisualRootsInTheirLane()
        {
            var motors = new List<PlayerController>();
            for (int i = 0; i < Names.Length; i++) motors.Add(Spawn(Names[i], i));
            yield return new WaitForSeconds(.5f);
            var initial = new Vector3[Names.Length];
            var visualX = new float[Names.Length];
            for (int i = 0; i < motors.Count; i++)
            {
                var input = motors[i].GetComponent<PlayerInputReader>();
                typeof(PlayerInputReader).GetProperty("CrouchHeld").SetValue(input, true);
                input.DriveMove(Vector2.up);
                initial[i] = motors[i].Position;
                visualX[i] = motors[i].GetComponentInChildren<Animator>().transform.localPosition.x;
            }
            yield return new WaitForSeconds(4f);
            for (int i = 0; i < motors.Count; i++)
            {
                var a = motors[i].GetComponentInChildren<Animator>();
                Assert.That(motors[i].IsCrouched, Is.True, Names[i]);
                Assert.That(a.GetCurrentAnimatorStateInfo(0).IsName("CrouchWalk"), Is.True, Names[i]);
                Assert.That(motors[i].Position.z - initial[i].z, Is.GreaterThan(3f), Names[i] + " must actually walk");
                Assert.That(Mathf.Abs(motors[i].Position.x - initial[i].x), Is.LessThan(.01f), Names[i] + " physical drift");
                Assert.That(Mathf.Abs(a.transform.localPosition.x - visualX[i]), Is.LessThan(.005f), Names[i] + " visual-root drift");
            }
        }

        [UnityTest]
        public IEnumerator KarlanNeckStaysUprightAtRestAndDuringCrouchWalk()
        {
            var motor = Spawn("Player"); var a = motor.GetComponentInChildren<Animator>();
            var input = motor.GetComponent<PlayerInputReader>();
            typeof(PlayerInputReader).GetProperty("CrouchHeld").SetValue(input, true);
            yield return new WaitForSeconds(.5f);
            Assert.That(a.GetCurrentAnimatorStateInfo(0).IsName("CrouchIdle"), Is.True);
            var head = a.GetBoneTransform(HumanBodyBones.Head);
            var neck = a.GetBoneTransform(HumanBodyBones.Neck);
            Assert.That(head.position.y - neck.position.y, Is.GreaterThan(.1f));
            input.DriveMove(Vector2.up);
            yield return new WaitForSeconds(.3f);
            float until = Time.time + 4f;
            while (Time.time < until)
            {
                Assert.That(head.position.y - neck.position.y, Is.GreaterThan(.1f), "Neck collapses into the collar");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator KarlanRecoversAfterBothDirectionalKnockdowns()
        {
            var motor = Spawn("Player"); var a = motor.GetComponentInChildren<Animator>();
            yield return new WaitForSeconds(.3f);
            foreach (var type in new[] { KnockdownType.FlyBack, KnockdownType.FallForward })
            {
                motor.Knockdown(type);
                yield return new WaitForSeconds(.15f);
                Assert.That(a.GetCurrentAnimatorStateInfo(0).IsName(type == KnockdownType.FlyBack ? "FlyBack" : "FallForward"), Is.True);
                float timeout = Time.time + 7f;
                while (motor.IsKnockedDown && Time.time < timeout) yield return null;
                Assert.That(motor.IsKnockedDown, Is.False, "Control remains locked after recovery");
                yield return new WaitForSeconds(.2f);
                Assert.That(a.GetCurrentAnimatorStateInfo(0).IsName("Idle"), Is.True, "Recovery and motor duration disagree");
            }
        }

        [UnityTest]
        public IEnumerator AllCharactersFeetFollowParallelForwardTracks()
        {
            var motors = new PlayerController[Names.Length];
            var feet = new Transform[Names.Length, 2];
            var toes = new Transform[Names.Length, 2];
            var min = new Vector3[Names.Length, 2];
            var max = new Vector3[Names.Length, 2];
            var scale = new float[Names.Length];
            for (int character = 0; character < Names.Length; character++)
            {
                var motor = motors[character] = Spawn(Names[character], character);
                scale[character] = motor.GetComponent<CapsuleCollider>().height / 1.65f;
                var a = motor.GetComponentInChildren<Animator>();
                feet[character, 0] = a.GetBoneTransform(HumanBodyBones.LeftFoot);
                feet[character, 1] = a.GetBoneTransform(HumanBodyBones.RightFoot);
                toes[character, 0] = a.GetBoneTransform(HumanBodyBones.LeftToes);
                toes[character, 1] = a.GetBoneTransform(HumanBodyBones.RightToes);
                for (int i = 0; i < 2; i++) { min[character, i] = Vector3.positiveInfinity; max[character, i] = Vector3.negativeInfinity; }
                var input = motor.GetComponent<PlayerInputReader>();
                typeof(PlayerInputReader).GetProperty("CrouchHeld").SetValue(input, true);
                input.DriveMove(Vector2.up);
            }
            yield return new WaitForSeconds(.7f);
            float until = Time.time + 3.5f;
            while (Time.time < until)
            {
                for (int character = 0; character < Names.Length; character++)
                for (int i = 0; i < 2; i++)
                {
                    Vector3 p = motors[character].transform.InverseTransformPoint(feet[character, i].position);
                    min[character, i] = Vector3.Min(min[character, i], p); max[character, i] = Vector3.Max(max[character, i], p);
                    Vector3 direction = motors[character].transform.InverseTransformVector(toes[character, i].position - feet[character, i].position);
                    Assert.That(Mathf.Abs(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg), Is.LessThan(3f), Names[character] + " toe points sideways");
                    Assert.That(i == 0 ? p.x < -.08f * scale[character] : p.x > .08f * scale[character], Is.True, Names[character] + " feet cross their lanes");
                }
                yield return null;
            }
            for (int character = 0; character < Names.Length; character++)
            for (int i = 0; i < 2; i++)
            {
                Assert.That(max[character, i].x - min[character, i].x, Is.LessThan(.02f), Names[character] + " lateral foot slide");
                Assert.That(max[character, i].z - min[character, i].z, Is.GreaterThan(.3f * scale[character]), Names[character] + " must retain the forward stride");
            }
        }

        [UnityTest]
        public IEnumerator MilezRecoveryStartsOnTheSameSideAsItsFall()
        {
            var motor = Spawn("Milez"); var a = motor.GetComponentInChildren<Animator>();
            yield return new WaitForSeconds(.3f);
            foreach (var type in new[] { KnockdownType.FlyBack, KnockdownType.FallForward })
            {
                string recovery = type == KnockdownType.FlyBack ? "StandUpFromBack" : "StandUpFromForward";
                float expected = type == KnockdownType.FlyBack ? 1f : -1f;
                motor.Knockdown(type);
                float timeout = Time.time + 6f;
                bool sawRecovery = false;
                while (motor.IsKnockedDown && Time.time < timeout)
                {
                    var state = a.GetCurrentAnimatorStateInfo(0);
                    if (state.IsName(recovery) && state.normalizedTime < .15f)
                    {
                        Vector3 across = a.GetBoneTransform(HumanBodyBones.RightUpperArm).position - a.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
                        Vector3 up = a.GetBoneTransform(HumanBodyBones.Neck).position - a.GetBoneTransform(HumanBodyBones.Hips).position;
                        float face = Vector3.Cross(across.normalized, up.normalized).y;
                        Assert.That(face * expected, Is.GreaterThan(.8f), "Recovery starts on the opposite side of the body");
                        sawRecovery = true;
                    }
                    yield return null;
                }
                Assert.That(sawRecovery, Is.True, recovery);
                Assert.That(motor.IsKnockedDown, Is.False, "Recovery duration disagrees with the animation");
                yield return new WaitForSeconds(.3f);
                Assert.That(a.GetCurrentAnimatorStateInfo(0).IsName("Idle"), Is.True);
            }
        }

        [UnityTest]
        public IEnumerator MilezUsesBothIdlesWithContinuousBlendAndPausesWhileCrouched()
        {
            var motor = Spawn("Milez"); var a = motor.GetComponentInChildren<Animator>();
            yield return new WaitForSeconds(.3f);
            bool sawAlternate = false, returned = false;
            float previous = a.GetFloat(CharacterAnimatorDriver.IdleVariationParameter);
            float until = Time.time + 22f;
            while (Time.time < until)
            {
                float value = a.GetFloat(CharacterAnimatorDriver.IdleVariationParameter);
                Assert.That(Mathf.Abs(value - previous), Is.LessThanOrEqualTo(Time.deltaTime * 1.4f + .002f), "Idle abruptly switches");
                sawAlternate |= value > .99f;
                returned |= sawAlternate && value < .01f;
                previous = value;
                if (returned) break;
                yield return null;
            }
            Assert.That(sawAlternate && returned, Is.True, "Both idle clips must be used");
            typeof(PlayerInputReader).GetProperty("CrouchHeld").SetValue(motor.GetComponent<PlayerInputReader>(), true);
            yield return new WaitForSeconds(.5f);
            float stopped = a.GetFloat(CharacterAnimatorDriver.IdleVariationParameter);
            yield return new WaitForSeconds(1f);
            Assert.That(a.GetFloat(CharacterAnimatorDriver.IdleVariationParameter), Is.EqualTo(stopped));
        }
    }
}
#endif
