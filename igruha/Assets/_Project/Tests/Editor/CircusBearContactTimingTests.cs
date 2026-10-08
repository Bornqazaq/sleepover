using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Igruha.Core.Player;
using Igruha.Minigames.Circus;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Igruha.Tests
{
    /// <summary>Steps the production pursuit, imported Strike and real contact IK.
    /// Only the editor clock is supplied manually; contact is never mocked.</summary>
    public sealed class CircusBearContactTimingTests
    {
        private static readonly string[] Characters={"Player","Boss","Shlanga","Fat","MyBoy","Girl","Milez","Aza"};
        private static readonly int[] FrameRates={15,30,60};
        private const BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.NonPublic;
        private Scene preview;
        private PitBear source, bear;
        private CircusBearMotion motion;
        private GameObject bearObject, avatar, ground;
        private CharacterArmClearance armClearance;
        private CharacterFootGrounding footGrounding;
        private float idlePhase=.25f;
        private PlayerController player;
        private Animator animator, playerAnimator;
        private float elapsed;
        private int hits;
        private string closestSkinDetails;
        private readonly StringBuilder contactTrace=new StringBuilder();

        public static IEnumerable RosterAndRates()
        {
            foreach(string character in Characters)
                foreach(int fps in FrameRates) yield return new TestCaseData(character,fps);
        }

        public static IEnumerable RosterAndFacings()
        {
            foreach(string character in Characters)
                foreach(float facing in new[]{0f,90f,180f,270f})yield return new TestCaseData(character,facing);
        }

        [OneTimeSetUp] public void LoadConfiguredBear()
        {
            preview=EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Minigames/CansOrder.unity");
            foreach(var root in preview.GetRootGameObjects())
            {
                source=root.GetComponentInChildren<PitBear>(true);
                if(source!=null)break;
            }
            Assert.That(source,Is.Not.Null,"The shipping circus scene must contain its configured bear.");
        }

        [OneTimeTearDown] public void ClosePreview()
        { if(preview.IsValid())EditorSceneManager.ClosePreviewScene(preview); }

        [TearDown] public void Clean()
        {
            if(bearObject!=null)Object.DestroyImmediate(bearObject);
            if(avatar!=null)Object.DestroyImmediate(avatar);
            if(ground!=null)Object.DestroyImmediate(ground);
        }

        [TestCaseSource(nameof(RosterAndRates))]
        public void FirstSwipeTouchesAStandingCharacterExactlyOnce(string character,int fps)
        { CheckFirstSwipe(character,fps,180); }

        [TestCaseSource(nameof(RosterAndFacings))]
        public void FirstSwipeReachesAllStandingFacings(string character,float facing)
        { CheckFirstSwipe(character,30,facing); }

        private void CheckFirstSwipe(string character,int fps,float facing)
        {
            Build(character,facing);
            float hitAge=0, hitGap=float.PositiveInfinity;
            bear.Caught+=(victim,impulse)=>
            {
                Assert.That(victim,Is.SameAs(player));
                hitAge=elapsed;hitGap=motion.ContactDistance;
                float skinGap=DistanceToWholePlayerSkin(motion.StrikePawCenter);
                Assert.That(skinGap,Is.GreaterThanOrEqualTo(CircusBearContactShape.PawRadius-.035f),
                    character+": solved paw penetrates the real animated body/clothes/arms (skin gap "+skinGap.ToString("F3")+"). "+closestSkinDetails);
                Assert.That(skinGap,Is.LessThanOrEqualTo(CircusBearContactShape.PawRadius+.12f+.035f),
                    character+": envelope reports a hit while the actual animated skin is still too far from the paw. "+closestSkinDetails);
            };
            for(int i=0;i<fps;i++) Step(1f/fps);
            if(character=="MyBoy")TestContext.Out.WriteLine(character+"/"+fps+"\n"+contactTrace);
            Assert.That(hits,Is.EqualTo(1),character+" facing "+facing+" at "+fps+" FPS must hit on the first committed swipe.\n"+contactTrace);
            Assert.That(hitAge,Is.InRange(PitBear.ContactSeconds, PitBear.ContactSeconds+.09f));
            Assert.That(hitGap,Is.LessThanOrEqualTo(.12f),"The actual solved paw must touch, without widening the damage threshold.");
        }

        [TestCase(0f)][TestCase(90f)][TestCase(180f)][TestCase(270f)]
        public void FirstSwipeAfterDiagonalPursuitReachesStationaryPlayerNearPitWall(float facing)
        {
            Build("Player",facing,false);
            // Stopwatch host + seven clients: the bear comes from the fleeing
            // Boss at this contact position, then approaches the stationary player
            // under the northern cage after the initial move-right input.
            bear.transform.SetPositionAndRotation(Vector3.ClampMagnitude(new Vector3(5.093f,0,4.204f),8.64f-PitBear.BodyWallClearance),Quaternion.Euler(0,-1.9f,0));
            MovePlayer(new Vector3(-.6f,0,7.92f));
            bear.Caught+=(victim,impulse)=>
            {
                float skinGap=DistanceToWholePlayerSkin(motion.StrikePawCenter);
                Assert.That(skinGap,Is.InRange(.145f,.335f),closestSkinDetails);
            };
            bool began=false;const float dt=1f/30;
            for(int frame=0;frame<240;frame++)
            {
                bool wasAttack=bear.State==PitBear.BearState.Attack;
                if(wasAttack)elapsed+=dt;
                Set(bear,"visualAttackStartedAt",Time.time-elapsed);
                bear.Tick(dt,player,false);
                if(bear.State==PitBear.BearState.Attack)
                {
                    if(!wasAttack){began=true;elapsed=0;Set(bear,"visualAttackStartedAt",Time.time);}
                    animator.Play("Strike",0,elapsed/PitBear.StrikeSeconds);animator.Update(0);
                }
                else{animator.SetFloat("Speed",bear.AnimatorSpeed);animator.Update(dt);}
                SamplePlayerPose(dt);
                Invoke(motion,"EvaluatePose",dt);Invoke(bear,"LateUpdate");
                if(hits>0 || began && bear.State!=PitBear.BearState.Attack)break;
            }
            Assert.That(hits,Is.EqualTo(1),"Facing "+facing+"; root="+bear.transform.position.ToString("F3")+
                " yaw="+bear.transform.eulerAngles.y.ToString("F3")+"; "+motion.DescribeContact());
        }

        [TestCase(.643f,false)][TestCase(.643f,true)]
        [TestCase(.032f,false)][TestCase(.032f,true)]
        public void ExactStopwatchMissPoseMustReach(float phase,bool usePosePipeline)
        {
            Build("Player",276.17f,false);
            idlePhase=phase;
            // The recorded root was inside the masonry with the old .8 m
            // margin. Keep the victim/pose, start at the new safe boundary.
            bear.transform.SetPositionAndRotation(Vector3.ClampMagnitude(new Vector3(.661f,0,7.060f),8.64f-PitBear.BodyWallClearance),Quaternion.Euler(0,299.61f,0));
            MovePlayer(new Vector3(-1.080f,0,8.050f));
            if(usePosePipeline)EnablePlayerPosePipeline();
            SamplePlayerPose(1f/30);
            contactTrace.Append("phase=").Append(phase).Append(" pipeline=").Append(usePosePipeline)
                .Append(" standOff=").Append(motion.ApproachDistance(player).ToString("F4")).AppendLine();
            for(int frame=0;frame<300 && bear.State!=PitBear.BearState.Attack;frame++)
            {
                bear.Tick(1f/30,player,false);
                animator.SetFloat("Speed",bear.AnimatorSpeed);animator.Update(1f/30);
                Invoke(motion,"EvaluatePose",1f/30);
                Assert.That(new Vector2(bear.transform.position.x,bear.transform.position.z).magnitude,
                    Is.LessThanOrEqualTo(8.64f-PitBear.BodyWallClearance+.001f));
            }
            Assert.That(bear.State,Is.EqualTo(PitBear.BearState.Attack));
            bear.Caught+=(victim,impulse)=>
            {
                float skinGap=DistanceToWholePlayerSkin(motion.StrikePawCenter);
                Assert.That(skinGap,Is.InRange(.145f,.335f),closestSkinDetails);
            };
            for(int i=0;i<30;i++)Step(1f/30);
            TestContext.Out.WriteLine(contactTrace+"\n"+motion.DescribeContact());
            Assert.That(hits,Is.EqualTo(1),motion.DescribeContact()+"\n"+contactTrace);
        }

        [TestCase("Player")][TestCase("Boss")][TestCase("Shlanga")][TestCase("Fat")]
        [TestCase("MyBoy")][TestCase("Girl")][TestCase("Milez")][TestCase("Aza")]
        public void CloseRunnerGetsABackstepBeforeRealPawContact(string character)
        {
            Build(character,180,false);
            MovePlayer(Vector3.forward*.75f);
            SamplePlayerPose(1f/30);
            bear.Caught+=(victim,impulse)=>
            {
                Assert.That(Vector3.Distance(bear.transform.position,player.Position),Is.GreaterThan(1.7f),
                    "The old zero-clamped lunge hit with the player's head buried in the chest.");
                float skinGap=DistanceToWholePlayerSkin(motion.StrikePawCenter);
                Assert.That(skinGap,Is.InRange(.145f,.335f),closestSkinDetails);
                AssertTrunkMeshesAreSeparated();
            };
            bear.Tick(.001f,player,false);
            Assert.That(bear.State,Is.EqualTo(PitBear.BearState.Attack));
            for(int frame=0;frame<30;frame++)Step(1f/30);
            Assert.That(bear.transform.position.z,Is.LessThan(-.8f),"The bear must physically step back, not just suppress damage.");
            Assert.That(hits,Is.EqualTo(1),character+" must still receive a reachable first swipe after making room.\n"+contactTrace);
        }

        [TestCase(15)][TestCase(30)][TestCase(60)]
        public void RunnerChargingInsideAfterWindupCannotBeHitInsideTheChest(int fps)
        {
            Build("Boss");
            Step(.34f);
            // The runner changes direction after the committed approach, reaching
            // the old failure's .756 m separation while the paw swings through.
            MovePlayer(bear.transform.position+bear.transform.forward*.756f);
            bool touching=false;
            for(int frame=0;frame<Mathf.CeilToInt(.3f*fps);frame++)
            {
                Step(1f/fps);
                if(elapsed>=PitBear.ContactSeconds && elapsed<=PitBear.ContactSeconds+.09f && motion.ContactDistance<=.12f)touching=true;
            }
            Assert.That(touching,Is.True,"Regression must exercise a real paw touch, not merely an out-of-reach dodge.");
            Assert.That(hits,Is.Zero,"Touching the paw from inside the bear cannot be confirmed as a valid strike pose.");
        }

        [Test] public void WallBlockedRetreatWalksAroundThenReachesTheCloseRunner()
        {
            Build("Boss",0,false);
            float edge=8.64f-PitBear.BodyWallClearance;
            bear.transform.SetPositionAndRotation(new Vector3(0,0,edge),Quaternion.Euler(0,180,0));
            Vector3 beforeReposition=bear.transform.position;
            MovePlayer(new Vector3(0,0,edge-.75f));
            bool sawReposition=false;const float dt=1f/30;
            bear.Caught+=(victim,impulse)=>
            {
                float gap=DistanceToWholePlayerSkin(motion.StrikePawCenter);
                Assert.That(gap,Is.InRange(.145f,.335f),closestSkinDetails);
                AssertTrunkMeshesAreSeparated();
            };
            for(int frame=0;frame<300 && hits==0;frame++)
            {
                bool wasAttack=bear.State==PitBear.BearState.Attack;
                if(wasAttack)elapsed+=dt;
                Set(bear,"visualAttackStartedAt",Time.time-elapsed);
                bear.Tick(dt,player,false);
                if(bear.State==PitBear.BearState.Attack)
                {
                    if(!wasAttack){elapsed=0;Set(bear,"visualAttackStartedAt",Time.time);}
                    animator.Play("Strike",0,elapsed/PitBear.StrikeSeconds);animator.Update(0);
                }
                else
                {
                    sawReposition|=bear.State==PitBear.BearState.Chase && Vector3.Distance(beforeReposition,bear.transform.position)>.2f;
                    animator.SetFloat("Speed",bear.AnimatorSpeed);animator.Update(dt);
                }
                SamplePlayerPose(dt);Invoke(motion,"EvaluatePose",dt);Invoke(bear,"LateUpdate");
                Assert.That(new Vector2(bear.transform.position.x,bear.transform.position.z).magnitude,Is.LessThanOrEqualTo(edge+.001f));
            }
            Assert.That(sawReposition,Is.True,"A blocked retreat must use visible walking to make room.");
            Assert.That(hits,Is.EqualTo(1),"The wall must not create endless guarded misses. "+motion.DescribeContact());
        }

        [TestCase(1f,0f,-4f)] [TestCase(-1f,0f,-4f)]
        [TestCase(1f,45f,5.5f)] [TestCase(-1f,45f,5.5f)]
        [TestCase(1f,0f,5.5f)] [TestCase(-1f,0f,5.5f)]
        public void RunnerFollowingTheRimCanBeInterceptedFromTheInsideLane(float direction,float startAngle,float bearZ)
        {
            Build("Boss",0,false);
            bear.transform.position=new Vector3(0,0,bearZ);
            MovePlayer(new Vector3(Mathf.Sin(startAngle*Mathf.Deg2Rad)*7.8f,0,Mathf.Cos(startAngle*Mathf.Deg2Rad)*7.8f));
            bear.RegisterFallen(player);
            const float dt=1f/60;
            int attacks=0;
            for(int frame=0;frame<1800 && hits==0;frame++)
            {
                float angle=startAngle*Mathf.Deg2Rad+direction*frame*dt*6.5f/7.8f;
                MovePlayer(new Vector3(Mathf.Sin(angle)*7.8f,0,Mathf.Cos(angle)*7.8f));
                avatar.transform.rotation=Quaternion.Euler(0,angle*Mathf.Rad2Deg+direction*90,0);
                bool wasAttack=bear.State==PitBear.BearState.Attack;
                if(wasAttack)elapsed+=dt;
                Set(bear,"visualAttackStartedAt",Time.time-elapsed);
                bear.Tick(dt,player,false);
                if(bear.State==PitBear.BearState.Attack)
                {
                    if(!wasAttack){attacks++;elapsed=0;Set(bear,"visualAttackStartedAt",Time.time);}
                    animator.Play("Strike",0,elapsed/PitBear.StrikeSeconds);animator.Update(0);
                }
                else{animator.SetFloat("Speed",bear.AnimatorSpeed);animator.Update(dt);}
                SamplePlayerPose(dt);Invoke(motion,"EvaluatePose",dt);Invoke(bear,"LateUpdate");
                Assert.That(new Vector2(bear.transform.position.x,bear.transform.position.z).magnitude,
                    Is.LessThanOrEqualTo(8.64f-PitBear.BodyWallClearance+.001f));
            }
            Assert.That(hits,Is.EqualTo(1),"Circling the rim must not trap the bear against its movement limit. Attacks="+attacks);
        }

        private void AssertTrunkMeshesAreSeparated()
        {
            var playerTrunk=new HashSet<Transform>();
            foreach(var bone in new[]{HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,
                HumanBodyBones.UpperChest,HumanBodyBones.Neck,HumanBodyBones.Head})
            {
                var transform=playerAnimator.GetBoneTransform(bone);if(transform!=null)playerTrunk.Add(transform);
            }
            // Independent actual-mesh evidence at the hit, not the runtime contact
            // envelope. Exclude the striking limb; it is checked separately above.
            Vector3 origin=bear.transform.position,forward=bear.transform.forward;
            float bearFront=TrunkProjection(bearObject,origin,forward,true,null);
            float playerBack=TrunkProjection(avatar,origin,forward,false,playerTrunk);
            Assert.That(playerBack-bearFront,Is.GreaterThanOrEqualTo(.025f),
                "Actual bear chest/neck skin overlaps the player's torso/head: gap="+(playerBack-bearFront).ToString("F4"));
        }

        private static float TrunkProjection(GameObject actor,Vector3 origin,Vector3 forward,bool bearTrunk,HashSet<Transform> playerTrunk)
        {
            float result=bearTrunk?float.NegativeInfinity:float.PositiveInfinity;
            var baked=new Mesh();
            try
            {
                foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if(skin.sharedMesh==null)continue;
                    Transform[] bones=skin.bones;BoneWeight[] weights=skin.sharedMesh.boneWeights;
                    var torsoBones=new bool[bones.Length];
                    for(int bone=0;bone<bones.Length;bone++)
                    {
                        string name=bones[bone].name;
                        torsoBones[bone]=bearTrunk ? name=="Pelvis" || name=="Lumbar" || name=="Spine" || name=="Chest" || name=="Neck" : playerTrunk.Contains(bones[bone]);
                    }
                    skin.BakeMesh(baked,true);Vector3[] vertices=baked.vertices;
                    Matrix4x4 world=skin.transform.localToWorldMatrix;
                    for(int index=0;index<vertices.Length;index++)
                    {
                        BoneWeight w=weights[index];int bone=w.boneIndex0;float strongest=w.weight0;
                        if(w.weight1>strongest){bone=w.boneIndex1;strongest=w.weight1;}
                        if(w.weight2>strongest){bone=w.boneIndex2;strongest=w.weight2;}
                        if(w.weight3>strongest)bone=w.boneIndex3;
                        if(!torsoBones[bone])continue;
                        float projection=Vector3.Dot(world.MultiplyPoint3x4(vertices[index])-origin,forward);
                        result=bearTrunk?Mathf.Max(result,projection):Mathf.Min(result,projection);
                    }
                }
            }
            finally{Object.DestroyImmediate(baked);}
            Assert.That(float.IsInfinity(result),Is.False,"The independent geometry check must inspect real torso vertices.");
            return result;
        }

        private void EnablePlayerPosePipeline()
        {
            ground=new GameObject("Bear contact test ground");ground.layer=6;
            ground.transform.position=new Vector3(0,-.1f,0);
            var collider=ground.AddComponent<BoxCollider>();collider.size=new Vector3(40,.2f,40);
            Invoke(avatar.GetComponent<CharacterAnimatorDriver>(),"Awake");
            armClearance=avatar.GetComponent<CharacterArmClearance>();
            footGrounding=avatar.GetComponent<CharacterFootGrounding>();
            Assert.That(armClearance,Is.Not.Null);Assert.That(footGrounding,Is.Not.Null);
            armClearance.enabled=false;footGrounding.enabled=false;
            Physics.SyncTransforms();
        }

        private void SamplePlayerPose(float dt)
        {
            playerAnimator.Play("Idle",0,idlePhase);playerAnimator.Update(0);
            if(armClearance!=null)armClearance.Apply(dt);
            if(footGrounding!=null)footGrounding.Apply();
        }

        [TestCase(15)][TestCase(30)][TestCase(60)]
        public void PlayerLeavingAfterThePreviousPoseDoesNotReceiveAStaleHit(int fps)
        {
            Build("Player");
            Step(.37f);
            Assert.That(motion.ContactDistance,Is.LessThanOrEqualTo(.12f),"Regression precondition: the preceding animated pose already touches.");
            Assert.That(hits,Is.Zero,"Anticipation cannot cause damage.");
            MovePlayer(player.transform.position+Vector3.right*1.2f);
            Vector3 delta=player.transform.position-bear.transform.position;
            Assert.That(delta.magnitude,Is.LessThan(2.85f));
            Assert.That(Vector3.Dot(bear.transform.forward,delta.normalized),Is.GreaterThan(.82f),
                "The old broad range/angle check must still pass, so this test exercises current paw contact.");
            for(int i=0;i<fps;i++)Step(1f/fps);
            Assert.That(hits,Is.Zero,"A stale touching pose must not catch a player who has dodged.");
        }

        [Test] public void MissingFreshIkAndSkippingTheEntireWindowCannotInventContact()
        {
            Build("Player");Step(.37f);
            Advance(.03f);
            // Do not sample IK. This is the exact old-pose read that caused false hits.
            Invoke(bear,"LateUpdate");
            Assert.That(hits,Is.Zero,"A cached contact without this tick's IK is not evidence of a hit.");
            Step(.30f);
            Assert.That(hits,Is.Zero,"Crossing the whole remaining window cannot apply damage from a late released pose.");
        }

        [TestCase("Player",0f)][TestCase("Boss",90f)]
        [TestCase("Fat",180f)][TestCase("MyBoy",270f)]
        public void AdaptedStrikeSkinStaysInsideMasonryAndAboveTheFloor(string character,float heading)
        {
            Build(character,heading+180);
            Vector3 outward=Quaternion.Euler(0,heading,0)*Vector3.forward;
            bear.transform.SetPositionAndRotation(outward*(8.64f-PitBear.BodyWallClearance),Quaternion.Euler(0,heading,0));
            MovePlayer(outward*8.45f);
            var skin=bearObject.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh=new Mesh();float maximumRadius=0,minimumHeight=float.MaxValue;
            try
            {
                for(int frame=0;frame<66;frame++)
                {
                    Step(1f/60);
                    skin.BakeMesh(mesh);
                    foreach(var vertex in mesh.vertices)
                    {
                        Vector3 world=skin.transform.TransformPoint(vertex);
                        maximumRadius=Mathf.Max(maximumRadius,new Vector2(world.x,world.z).magnitude);
                        minimumHeight=Mathf.Min(minimumHeight,world.y);
                    }
                }
                TestContext.Out.WriteLine("Solved skin radius="+maximumRadius+" floor="+minimumHeight);
                Assert.That(maximumRadius,Is.LessThan(8.54f),"Contact IK must keep fur and claws before the visible masonry.");
                Assert.That(minimumHeight,Is.GreaterThan(-.035f),"The complete swiping paw must not enter the floor.");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        private void Build(string character,float facing=180,bool startAttack=true)
        {
            elapsed=0;hits=0;idlePhase=.25f;armClearance=null;footGrounding=null;contactTrace.Clear();
            bearObject=Object.Instantiate(source.gameObject);
            foreach(var behaviour in bearObject.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
            foreach(var effect in bearObject.GetComponentsInChildren<ParticleSystem>(true))effect.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            bear=bearObject.GetComponent<PitBear>();motion=bearObject.GetComponentInChildren<CircusBearMotion>(true);
            Assert.That(motion,Is.Not.Null);
            bearObject.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            animator=bearObject.GetComponentInChildren<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();animator.Update(0);
            Invoke(motion,"Awake");Invoke(motion,"OnEnable");Invoke(bear,"Awake");
            // This regression test evaluates geometry, not audio/particle side effects.
            Set(bear,"feedback",null);
            bear.Configure(8.2f,2.1f,2.35f,0,8,8.64f);
            bear.Caught+=(victim,impulse)=>hits++;

            avatar=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/"+character+".prefab"));
            foreach(var behaviour in avatar.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
            player=avatar.GetComponent<PlayerController>();
            avatar.transform.rotation=Quaternion.Euler(0,facing,0);MovePlayer(Vector3.forward*2.65f);
            playerAnimator=avatar.GetComponentInChildren<Animator>();playerAnimator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            playerAnimator.Rebind();
            Assert.That(playerAnimator.HasState(0,Animator.StringToHash("Idle")),Is.True,"Shipping animator must contain Idle.");
            SamplePlayerPose(0);
            if(startAttack)
            {
                contactTrace.Append("facing=").Append(facing).Append(" standOff=").Append(motion.ApproachDistance(player).ToString("F4")).AppendLine();
                bear.Tick(.001f,player,false);
                Assert.That(bear.State,Is.EqualTo(PitBear.BearState.Attack));
            }
        }

        private void MovePlayer(Vector3 position)
        {
            avatar.transform.position=position;
            var body=avatar.GetComponent<Rigidbody>();if(body!=null){body.isKinematic=true;body.position=position;}
        }

        private float DistanceToWholePlayerSkin(Vector3 point)
        {
            var baked=new Mesh();float nearest=float.PositiveInfinity;
            SkinnedMeshRenderer nearestSkin=null;int nearestIndex=-1;Vector3 nearestPoint=Vector3.zero;
            closestSkinDetails=string.Empty;
            try
            {
                foreach(var skin in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if(skin.sharedMesh==null)continue;
                    // Compensate for the imported renderer scale before applying
                    // its local-to-world transform (the same convention as the prefab builder).
                    skin.BakeMesh(baked,true);
                    Matrix4x4 toWorld=skin.transform.localToWorldMatrix;
                    Vector3[] vertices=baked.vertices;
                    for(int index=0;index<vertices.Length;index++)
                    {
                        Vector3 world=toWorld.MultiplyPoint3x4(vertices[index]);
                        float distance=(world-point).sqrMagnitude;
                        if(distance>=nearest)continue;
                        nearest=distance;nearestSkin=skin;nearestIndex=index;nearestPoint=world;
                    }
                }
            }
            finally{Object.DestroyImmediate(baked);}
            float gap=Mathf.Sqrt(nearest);
            if(nearestSkin!=null && (gap<CircusBearContactShape.PawRadius-.035f || gap>CircusBearContactShape.PawRadius+.155f))
            {
                BoneWeight weight=nearestSkin.sharedMesh.boneWeights[nearestIndex];
                Transform[] bones=nearestSkin.bones;
                closestSkinDetails="Skin="+nearestSkin.name+" vertex="+nearestIndex+" world="+nearestPoint.ToString("F4")+
                    " paw="+point.ToString("F4")+" weights="+
                    bones[weight.boneIndex0].name+":"+weight.weight0.ToString("F3")+", "+
                    bones[weight.boneIndex1].name+":"+weight.weight1.ToString("F3")+", "+
                    bones[weight.boneIndex2].name+":"+weight.weight2.ToString("F3")+", "+
                    bones[weight.boneIndex3].name+":"+weight.weight3.ToString("F3");
            }
            return gap;
        }

        private void Advance(float dt)
        {
            elapsed+=dt;Set(bear,"visualAttackStartedAt",Time.time-elapsed);
            int before=hits;bear.Tick(dt,player,false);
            Assert.That(hits,Is.EqualTo(before),"Update must never decide damage before animation and IK.");
        }

        private void Step(float dt)
        {
            Advance(dt);
            // Sample the imported production clip at the simulated clock. The
            // runtime solver and decision then run in their real LateUpdate order.
            animator.Play("Strike",0,elapsed/PitBear.StrikeSeconds);animator.Update(0);
            SamplePlayerPose(dt);
            Vector3 priorPaw=motion.StrikePawCenter;
            Invoke(motion,"EvaluatePose",dt);
            if(elapsed>=.26f && elapsed<=.7f)
            {
                var shape=(CircusBearContactShape)motion.GetType().GetField("contactShape",PrivateInstance).GetValue(motion);
                Vector3 approach=bear.transform.position+Vector3.up;
                Vector3 surface=shape.SurfaceToward(approach);
                Vector3 sweep=shape.SweepOutside(priorPaw,surface,approach);
                contactTrace.Append("t=").Append(elapsed.ToString("F4")).Append(" visual=").Append(bear.AttackAge.ToString("F4"))
                    .Append(" gap=").Append(motion.ContactDistance.ToString("F4"))
                    .Append(" root=").Append(bear.transform.position.ToString("F4"))
                    .Append(" paw=").Append(motion.StrikePawCenter.ToString("F4"))
                    .Append(" surface=").Append(surface.ToString("F4"))
                    .Append(" targetGap=").Append(Vector3.Distance(motion.StrikePawCenter,surface).ToString("F4"))
                    .Append(" prior=").Append(priorPaw.ToString("F4"))
                    .Append(" sweepGap=").Append(Vector3.Distance(surface,sweep).ToString("F4"))
                    .Append(" pending=").Append(bear.GetType().GetField("contactPending",PrivateInstance).GetValue(bear))
                    .AppendLine();
            }
            Invoke(bear,"LateUpdate");
        }

        private static void Set(object target,string field,object value)
        { target.GetType().GetField(field,PrivateInstance).SetValue(target,value); }
        private static void Invoke(object target,string method,params object[] arguments)
        { target.GetType().GetMethod(method,PrivateInstance).Invoke(target,arguments); }
    }
}
