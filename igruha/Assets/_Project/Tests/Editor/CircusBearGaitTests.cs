using System.Linq;
using System.Reflection;
using Igruha.Minigames.Circus;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Igruha.Tests
{
    public sealed class CircusBearGaitTests
    {
        private Scene preview;
        private GameObject copy;
        private PitBear bear;
        private Animator animator;
        private CircusBearMotion motion;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp] public void Load()
        {
            preview = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Minigames/CansOrder.unity");
            var source = preview.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PitBear>(true)).Single();
            copy = Object.Instantiate(source.gameObject);
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            bear = copy.GetComponent<PitBear>();
            animator = copy.GetComponentInChildren<Animator>();
            motion = copy.GetComponentInChildren<CircusBearMotion>();
            typeof(PitBear).GetMethod("Awake", Private).Invoke(bear, null);
            typeof(CircusBearMotion).GetMethod("Awake", Private).Invoke(motion, null);
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
        }

        [TearDown] public void Clean()
        {
            if (copy != null) Object.DestroyImmediate(copy);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }

        [TestCase(2.1f, 0f)] [TestCase(2.1f, 90f)]
        [TestCase(4.5f, 45f)] [TestCase(8.2f, 0f)]
        public void AnimatedPawsRemainContinuousThroughStepsAndTurns(float speed, float turn)
        {
            const float dt = 1f / 60;
            var paws = copy.GetComponentsInChildren<Transform>().Where(t => t.name.Contains("Paw.")).ToArray();
            Assert.That(paws.Length, Is.EqualTo(4));
            var previous = new Vector3[4];
            float largestStep = 0;
            var evaluate = typeof(CircusBearMotion).GetMethod("EvaluatePose", Private);
            bear.ApplyNetworkState(PitBear.BearState.Chase, speed);
            animator.SetFloat("Speed", speed);
            animator.Play("Locomotion", 0, 0);
            for (int frame = 0; frame < 240; frame++)
            {
                copy.transform.rotation *= Quaternion.Euler(0, turn * dt, 0);
                copy.transform.position += copy.transform.forward * (speed * dt);
                animator.Update(dt);
                evaluate.Invoke(motion, new object[] { dt });
                for (int i = 0; i < paws.Length; i++)
                {
                    if (frame > 2) largestStep = Mathf.Max(largestStep, Vector3.Distance(previous[i], paws[i].position));
                    previous[i] = paws[i].position;
                }
            }
            TestContext.Out.WriteLine("Maximum paw travel in one 60 Hz frame: " + largestStep);
            Assert.That(largestStep, Is.LessThan(.12f + speed * dt * 1.8f),
                "A paw must swing continuously, without an IK anchor release or knee flip.");
        }

        [Test] public void EveryAuthoredPoseFitsInsideTheWallClearance()
        {
            var skin = copy.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = new Mesh();
            float radius = 0;
            try
            {
                foreach (var clip in animator.runtimeAnimatorController.animationClips.Distinct())
                    for (int frame = 0; frame < 60; frame++)
                    {
                        clip.SampleAnimation(animator.gameObject, clip.length * frame / 60);
                        skin.BakeMesh(mesh);
                        var toBear = copy.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                        foreach (var vertex in mesh.vertices)
                        {
                            var point = toBear.MultiplyPoint3x4(vertex);
                            radius = Mathf.Max(radius, new Vector2(point.x, point.z).magnitude);
                        }
                    }
            }
            finally { Object.DestroyImmediate(mesh); }
            TestContext.Out.WriteLine("Animated fur/muzzle/paw envelope: " + radius);
            Assert.That(radius + .12f, Is.LessThanOrEqualTo(PitBear.BodyWallClearance),
                "The entire turning bear must fit inside the visible masonry, including the strike.");
        }

        [Test] public void PatrolStaysInsideTheSafeRegionForAFullLap()
        {
            bear.Configure(8.2f, 2.1f, 2.35f, 0, 8, 8.64f);
            copy.transform.position = new Vector3(0, 0, 5.5f);
            for (int frame = 0; frame < 2400; frame++)
            {
                bear.Tick(1f / 60, null, false);
                var p = copy.transform.position;
                Assert.That(new Vector2(p.x, p.z).magnitude, Is.LessThanOrEqualTo(8.64f - PitBear.BodyWallClearance + .001f));
            }
        }
    }
}
