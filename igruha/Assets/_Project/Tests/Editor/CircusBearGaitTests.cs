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
            var source = preview.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PitBear>(true)).First(b => b.name == "PitBear");
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

        [TestCase(.85f, 0f)] [TestCase(.85f, 90f)]
        [TestCase(2.1f, 0f)]
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

        [Test] public void ProductionControllerHasNoRearingStateTriggerOrClip()
        {
            var controller = (UnityEditor.Animations.AnimatorController)animator.runtimeAnimatorController;
            Assert.That(controller.parameters.Any(p => p.name == "Roar"), Is.False);
            Assert.That(controller.layers.SelectMany(l => l.stateMachine.states).Any(s => s.state.name == "Roar"), Is.False);
            Assert.That(controller.animationClips.Any(c => c.name == "Bruno_Roar"), Is.False);
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

        [Test] public void CageAttentionKeepsAllFourPawsLowWithTheActualAnimator()
        {
            var playerObject = new GameObject("Occupied cage");
            try
            {
                var player = playerObject.AddComponent<Igruha.Core.Player.PlayerController>();
                player.transform.position = new Vector3(0, 8.64f, -7.2f);
                var occupants = new[] { player };
                bear.Configure(8.2f,.85f,2.35f,3,8,8.64f);
                animator.Play("Locomotion",0,0);animator.Update(0);
                var pose = typeof(CircusBearMotion).GetMethod("EvaluatePose",Private);
                var paws = copy.GetComponentsInChildren<Transform>().Where(t=>t.name.Contains("Paw.")).ToArray();
                var floor = paws.Select(p=>p.position.y).ToArray();
                float maxLift = 0; int watchingFrames = 0;
                Assert.That(paws.Length, Is.EqualTo(4));
                for(int frame=0;frame<1200;frame++)
                {
                    bear.Tick(1f/60,null,false,occupants);
                    animator.SetFloat("Speed",bear.AnimatorSpeed,.13f,1f/60);
                    animator.Update(1f/60);pose.Invoke(motion,new object[]{1f/60});
                    Assert.That(bear.State,Is.Not.EqualTo(PitBear.BearState.Taunt));
                    Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash,Is.EqualTo(Animator.StringToHash("Locomotion")));
                    for(int i=0;i<paws.Length;i++)maxLift=Mathf.Max(maxLift,paws[i].position.y-floor[i]);
                    if(bear.State==PitBear.BearState.CageApproach && bear.AnimatorSpeed<.03f)watchingFrames++;
                }
                Assert.That(watchingFrames,Is.GreaterThan(60),"Attention must still include a quadruped pause towards the cage.");
                Assert.That(maxLift,Is.LessThan(.35f),"Paws may take steps but the body must never rear up.");
            }
            finally { Object.DestroyImmediate(playerObject); }
        }

        [TestCase(180f,60)] [TestCase(-179f,60)] [TestCase(180f,30)]
        public void HalfTurnLiftsEveryPawAndKeepsSupportingPawsStill(float degrees,int fps)
        {
            float dt = 1f / fps;
            var paws = copy.GetComponentsInChildren<Transform>().Where(t => t.name.Contains("Paw.")).ToArray();
            var evaluate = typeof(CircusBearMotion).GetMethod("EvaluatePose", Private);
            var face = typeof(PitBear).GetMethod("FaceTowards", Private);
            bear.ApplyNetworkState(PitBear.BearState.Watching, 0);
            animator.SetFloat("Speed", 0); animator.Play("Locomotion", 0, 0); animator.Update(0);
            var floor = paws.Select(p => p.position.y).ToArray();
            var previous = paws.Select(p => p.position).ToArray();
            var lifted = new float[4]; var supports = new int[4];
            float largestSlide = 0, largestStep = 0, largestYawStep = 0;
            Vector3 direction = Quaternion.Euler(0, degrees, 0) * Vector3.forward;
            for (int frame = 0; frame < fps*5; frame++)
            {
                Quaternion before = copy.transform.rotation;
                face.Invoke(bear, new object[] { direction, dt });
                largestYawStep = Mathf.Max(largestYawStep, Quaternion.Angle(before, copy.transform.rotation));
                animator.Update(dt); evaluate.Invoke(motion, new object[] { dt });
                for (int i = 0; i < paws.Length; i++)
                {
                    Vector3 current = paws[i].position;
                    lifted[i] = Mathf.Max(lifted[i], current.y - floor[i]);
                    largestStep = Mathf.Max(largestStep, Vector3.Distance(previous[i], current));
                    if (frame*dt > .34f && frame*dt < 3.25f && current.y < floor[i] + .012f && previous[i].y < floor[i] + .012f)
                    {
                        supports[i]++;
                        Vector3 drift = current - previous[i]; drift.y = 0;
                        largestSlide = Mathf.Max(largestSlide, drift.magnitude);
                    }
                    previous[i] = current;
                }
            }
            TestContext.Out.WriteLine($"Turn {degrees}: slide={largestSlide:F5}, step={largestStep:F4}, yaw/frame={largestYawStep:F3}; lift={string.Join(",", lifted)}; supports={string.Join(",", supports)}");
            Assert.That(Vector3.Angle(copy.transform.forward, direction), Is.LessThan(.1f));
            Assert.That(largestYawStep, Is.LessThan(60f/fps), "A heavy stationary bear must step through the turn.");
            Assert.That(largestSlide, Is.LessThan(.008f), "A support paw must not sweep sideways across the floor.");
            Assert.That(largestStep, Is.LessThan(.09f*60/fps), "No foot may snap loose from its planted position.");
            for (int i = 0; i < 4; i++)
            {
                Assert.That(lifted[i], Is.GreaterThan(.05f), "All four paws must take visible steps.");
                Assert.That(supports[i], Is.GreaterThan(fps*.4f), "Each paw must have a stable support phase.");
            }
        }

        [Test] public void PivotCanResumeWalkingWithoutAnAnchorSnapOrLimbStretch()
        {
            const float dt=1f/60;
            var bones=copy.GetComponentsInChildren<Transform>();
            var paws=bones.Where(t=>t.name.Contains("Paw.")).ToArray();
            var lower=bones.Where(t=>t.name.Contains("Lower.")).ToArray();
            var originalLengths=lower.Select(t=>Vector3.Distance(t.position,t.GetChild(0).position)).ToArray();
            var previous=paws.Select(t=>t.position).ToArray();
            var evaluate=typeof(CircusBearMotion).GetMethod("EvaluatePose",Private);
            bear.ApplyNetworkState(PitBear.BearState.Patrol,0);
            animator.SetFloat("Speed",0);animator.Play("Locomotion",0,0);animator.Update(0);
            float largestStep=0,largestLengthError=0;
            for(int frame=0;frame<240;frame++)
            {
                float speed=frame<90?0:Mathf.Min(1.2f,(frame-90)*dt*2);
                copy.transform.rotation*=Quaternion.Euler(0,(frame<120?55:0)*dt,0);
                copy.transform.position+=copy.transform.forward*speed*dt;
                animator.SetFloat("Speed",speed);animator.Update(dt);
                evaluate.Invoke(motion,new object[]{dt});
                for(int i=0;i<paws.Length;i++)
                {
                    if(frame>2)largestStep=Mathf.Max(largestStep,Vector3.Distance(previous[i],paws[i].position));
                    previous[i]=paws[i].position;
                }
                // Compare against the rigid bind lengths, not the breathing clip's
                // animated scales: those were part of the rubber-leg regression.
                for(int i=0;frame>15 && i<lower.Length;i++)
                    largestLengthError=Mathf.Max(largestLengthError,Mathf.Abs(Vector3.Distance(lower[i].position,lower[i].GetChild(0).position)-originalLengths[i])/originalLengths[i]);
            }
            TestContext.Out.WriteLine("Pivot to walk: max paw step="+largestStep+", segment length fraction="+largestLengthError);
            // Allow import precision, but never growth/shrinkage of a support limb.
            Assert.That(largestLengthError,Is.LessThan(.01f));
            Assert.That(largestStep,Is.LessThan(.13f),"Turning support must blend back into the walking gait continuously.");
        }

        [TestCase(0f,55f,60)][TestCase(1.05f,46f,60)][TestCase(.55f,-37f,30)]
        public void TurningLimbsKeepRigidLengthsAndDoNotFlipTheirJoints(float speed,float turn,int fps)
        {
            var bones=copy.GetComponentsInChildren<Transform>();
            var joints=bones.Where(t=>t.name.Contains("Upper.")||t.name.Contains("Lower.")).ToArray();
            var lengths=joints.Select(t=>Vector3.Distance(t.position,t.GetChild(0).position)).ToArray();
            var previous=joints.Select(t=>t.localRotation).ToArray();
            var evaluate=typeof(CircusBearMotion).GetMethod("EvaluatePose",Private);
            bear.ApplyNetworkState(PitBear.BearState.Patrol,speed);
            animator.SetFloat("Speed",speed);animator.Play("Locomotion",0,0);animator.Update(0);
            float maxLengthError=0,maxJointStep=0,dt=1f/fps;
            for(int frame=0;frame<fps*5;frame++)
            {
                copy.transform.rotation*=Quaternion.Euler(0,turn*dt,0);
                copy.transform.position+=copy.transform.forward*speed*dt;
                animator.Update(dt);evaluate.Invoke(motion,new object[]{dt});
                for(int i=0;i<joints.Length;i++)
                {
                    if(frame>fps/2)
                    {
                        maxLengthError=Mathf.Max(maxLengthError,Mathf.Abs(Vector3.Distance(joints[i].position,joints[i].GetChild(0).position)/lengths[i]-1));
                        maxJointStep=Mathf.Max(maxJointStep,Quaternion.Angle(previous[i],joints[i].localRotation));
                    }
                    previous[i]=joints[i].localRotation;
                }
            }
            TestContext.Out.WriteLine($"Rigid turn speed={speed}: length error={maxLengthError}, joint/frame={maxJointStep}");
            Assert.That(maxLengthError,Is.LessThan(.01f),"Weight-bearing limbs must keep their bind lengths while turning.");
            Assert.That(maxJointStep,Is.LessThan(20f*60/fps),"The knee/elbow must not twist abruptly during a smooth step.");
        }
    }
}
