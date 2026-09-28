#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.Audio;
using Igruha.Core.Player;
using Igruha.Minigames.MemoryRun;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Igruha.Tests
{
    public sealed class PlaytestMovementTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private float originalFixedDelta;
        private int originalFrameRate, originalVSync;
        private bool originalBackground;

        [SetUp]
        public void SaveTiming()
        {
            originalFixedDelta = Time.fixedDeltaTime;
            originalFrameRate = Application.targetFrameRate;
            originalVSync = QualitySettings.vSyncCount;
            originalBackground = Application.runInBackground;
        }

        private sealed class StepObserver
        {
            private readonly AudioSource[] sources;
            private readonly AudioClip[] clips;
            private readonly int[] samples;
            private readonly bool[] playing;
            public int Count { get; private set; }
            public readonly List<float> Phases = new List<float>();
            public readonly HashSet<string> ClipNames = new HashSet<string>();
            public float MaximumVolume { get; private set; }
            public StepObserver(PlayerController player)
            {
                sources = player.GetComponent<MinigameAudioPlayer>().GetComponentsInChildren<AudioSource>(true);
                clips = new AudioClip[sources.Length]; samples = new int[sources.Length]; playing = new bool[sources.Length];
            }
            public void Poll(Animator animator)
            {
                for (int i = 0; i < sources.Length; i++)
                {
                    var source = sources[i];
                    bool step = source.isPlaying && source.clip != null && source.clip.name.StartsWith("SFX_CHR_Step_");
                    if (step && (!playing[i] || source.clip != clips[i] || source.timeSamples < samples[i]))
                    {
                        Assert.That(source.mute, Is.False);
                        Assert.That(source.volume, Is.GreaterThan(0));
                        Count++;
                        ClipNames.Add(source.clip.name);
                        MaximumVolume = Mathf.Max(MaximumVolume, source.volume);
                        Phases.Add(Mathf.Repeat(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f));
                    }
                    clips[i] = source.clip; samples[i] = source.clip != null ? source.timeSamples : 0; playing[i] = step;
                }
            }
        }

        private void Listener(Vector3 position)
        {
            var go = new GameObject("Footstep listener", typeof(AudioListener));
            go.transform.position = position; created.Add(go);
        }
        private GameObject Box(Vector3 position, Vector3 size)
        {
            var box=GameObject.CreatePrimitive(PrimitiveType.Cube);created.Add(box);
            box.layer=LayerMask.NameToLayer("Ground");box.transform.position=position;box.transform.localScale=size;return box;
        }
        private PlayerController Player(string name,Vector3 position)
        {
            var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/"+name+".prefab"));created.Add(go);
            var input=go.GetComponent<PlayerInputReader>();input.EngageAutopilot();
            var animator=go.GetComponentInChildren<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var motor=go.GetComponent<PlayerController>();motor.TeleportTo(position,Quaternion.identity);
            var reference=new GameObject("Movement reference");created.Add(reference);motor.SetCameraReference(reference.transform);
            return motor;
        }
        [UnityTest]
        public IEnumerator MemoryExtremeLanesAreReachableWithActualMotor()
        {
            Application.runInBackground=true;
            var c=AssetDatabase.LoadAssetAtPath<MemoryRunConfig>("Assets/_Project/Settings/Gameplay/Minigames/MemoryRunConfig.asset");
            var offset=new Vector3(300,0,300);
            for(int row=0;row<2;row++)for(int lane=0;lane<3;lane++)
                Box(offset+new Vector3(c.LaneX(lane),-.25f,row*c.StepPitch),new Vector3(c.PlateWidth,.5f,c.PlateSize));
            foreach(string name in new[]{"Fat","Player"})
            {
                var motor=Player(name,offset+Vector3.up*.1f);var input=motor.GetComponent<PlayerInputReader>();
                foreach(int from in new[]{0,2})foreach(int to in new[]{0,1,2})
                {
                    float sign=Mathf.Sign(to-from);
                    var takeoff=offset+new Vector3(c.LaneX(from)+sign*(c.PlateWidth*.5f-.3f),.1f,c.PlateSize*.5f-.3f);
                    var aim=offset+new Vector3(c.LaneX(to),.1f,c.StepPitch-.3f);
                    var direction=(aim-takeoff).normalized;
                    // Accelerate on the source platform before the edge; input and jump use the real motor.
                    var start=takeoff-direction*.85f;
                    motor.TeleportTo(start,Quaternion.LookRotation(direction));input.DriveMove(Vector2.zero);
                    yield return new WaitForSeconds(.25f);
                    input.DriveMove(new Vector2(direction.x,direction.z));
                    float timeout=Time.time+1f;
                    while(Vector3.Dot(motor.Position-takeoff,direction)<0 && Time.time<timeout)yield return new WaitForFixedUpdate();
                    input.DriveJump();
                    yield return new WaitForSeconds(.08f);
                    bool airborne=!motor.IsGrounded;
                    timeout=Time.time+1.1f;
                    while(Time.time<timeout && (!motor.IsGrounded || !airborne))
                    {airborne|=!motor.IsGrounded;yield return new WaitForFixedUpdate();}
                    input.DriveMove(Vector2.zero);
                    var landing=motor.Position-offset;
                    Assert.That(airborne,Is.True,name+" never jumped");
                    Assert.That(motor.IsGrounded,Is.True,$"{name} {from}->{to} missed, landing {landing}");
                    Assert.That(Mathf.Abs(landing.x-c.LaneX(to)),Is.LessThan(c.PlateWidth*.5f+.25f),$"{name} wrong lane {from}->{to}");
                    Assert.That(Mathf.Abs(landing.z-c.StepPitch),Is.LessThan(c.PlateSize*.5f+.25f),$"{name} wrong row {from}->{to}");
                    Debug.Log($"PLAYTEST_JUMP PASS {name} {from}->{to} landing={landing}");
                }
                Object.Destroy(motor.gameObject);yield return null;
            }
        }
        [UnityTest]
        public IEnumerator FootstepsFollowDifferentLiveRunCycles()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 144;
            // Deliberately separate physics and render rates. The former code
            // discarded gait tracking on every frame with unchanged rb.position.
            Time.fixedDeltaTime = .08f;
            Box(new Vector3(500,-.25f,500),new Vector3(50,.5f,50));
            Listener(new Vector3(500,2,500));
            var fat=Player("Fat",new Vector3(498,.1f,490));
            var small=Player("Player",new Vector3(502,.1f,490));
            yield return new WaitForSeconds(.4f);
            var fatAnimator = fat.GetComponentInChildren<Animator>();
            var smallAnimator = small.GetComponentInChildren<Animator>();
            var fatSounds = new StepObserver(fat);
            var smallSounds = new StepObserver(small);
            fat.GetComponent<PlayerInputReader>().DriveMove(Vector2.up);
            small.GetComponent<PlayerInputReader>().DriveMove(Vector2.up);
            int unchangedPhysicsFrames = 0;
            var previousPosition = fat.Position;
            float until=Time.time+3f;
            while(Time.time<until)
            {
                if (fat.Position == previousPosition) unchangedPhysicsFrames++;
                previousPosition = fat.Position;
                fatSounds.Poll(fatAnimator); smallSounds.Poll(smallAnimator);
                yield return null;
            }
            Assert.That(unchangedPhysicsFrames, Is.GreaterThan(10), "Must exercise render frames between physics ticks");
            Assert.That(fatSounds.Count,Is.GreaterThanOrEqualTo(5));
            Assert.That(smallSounds.Count,Is.GreaterThan(fatSounds.Count),$"Karlan={smallSounds.Count}, Fat={fatSounds.Count}");
            foreach (float phase in fatSounds.Phases)
                Assert.That(Mathf.Min(Mathf.Abs(Mathf.DeltaAngle(phase*360,.25f*360)),Mathf.Abs(Mathf.DeltaAngle(phase*360,.733f*360))), Is.LessThan(55f), "Fat sound must follow a foot plant");
            Debug.Log($"PLAYTEST_STEPS PASS actual audio Karlan={smallSounds.Count} Fat={fatSounds.Count} unchangedPhysicsFrames={unchangedPhysicsFrames}");
            fat.GetComponent<PlayerInputReader>().DriveMove(Vector2.zero);
            small.GetComponent<PlayerInputReader>().DriveMove(Vector2.zero);
            yield return new WaitForSeconds(.5f);
            fatSounds.Poll(fatAnimator); smallSounds.Poll(smallAnimator);
            int stoppedCount = fatSounds.Count + smallSounds.Count;
            until = Time.time + .7f;
            while (Time.time < until) { fatSounds.Poll(fatAnimator); smallSounds.Poll(smallAnimator); yield return null; }
            Assert.That(fatSounds.Count + smallSounds.Count, Is.EqualTo(stoppedCount), "Standing players must not step");
        }
        [UnityTest]
        public IEnumerator AllCharactersPlaySurfaceStepsAndFourCrouchContacts()
        {
            Application.runInBackground = true;
            var wood = Box(new Vector3(700,-.25f,700),new Vector3(30,.5f,80)).AddComponent<SurfaceAudio>();
            var carpet = Box(new Vector3(800,-.25f,700),new Vector3(30,.5f,80)).AddComponent<SurfaceAudio>();
            var kind = typeof(SurfaceAudio).GetField("kind",BindingFlags.Instance|BindingFlags.NonPublic);
            kind.SetValue(wood,SurfaceKind.Wood); kind.SetValue(carpet,SurfaceKind.Carpet);
            Listener(new Vector3(750,2,700));
            foreach (string name in new[]{"Player","Boss","Shlanga","Fat","MyBoy","Girl","Milez","Aza"})
            {
                var motor = Player(name,new Vector3(700,.1f,680));
                var input = motor.GetComponent<PlayerInputReader>();
                var animator = motor.GetComponentInChildren<Animator>();
                yield return new WaitForSeconds(.3f);
                var run = new StepObserver(motor);
                input.DriveMove(Vector2.up);
                float until = Time.time + 2.5f;
                while(Time.time < until) { run.Poll(animator); yield return null; }
                Assert.That(run.Count,Is.GreaterThanOrEqualTo(3),name+" running is silent");
                foreach(string clip in run.ClipNames) Assert.That(clip,Does.StartWith("SFX_CHR_Step_Wood"));
                input.DriveMove(Vector2.zero);
                motor.GetComponent<MinigameAudioPlayer>().StopAll();
                motor.TeleportTo(new Vector3(800,.1f,680),Quaternion.identity);
                typeof(PlayerInputReader).GetProperty("CrouchHeld").SetValue(input,true);
                input.DriveMove(Vector2.up);
                until = Time.time + 2f;
                while (!animator.GetCurrentAnimatorStateInfo(0).IsName("CrouchWalk") && Time.time < until) yield return null;
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("CrouchWalk"),Is.True,
                    $"{name} crouched={motor.IsCrouched} input={input.CrouchHeld} speed={motor.NormalizedSpeed} grounded={motor.IsGrounded}");
                var crouch = new StepObserver(motor); crouch.Poll(animator);
                int baseline = crouch.Count;
                float endPhase = animator.GetCurrentAnimatorStateInfo(0).normalizedTime + 1.1f;
                until = Time.time + 12;
                while(animator.GetCurrentAnimatorStateInfo(0).normalizedTime < endPhase && Time.time < until)
                { crouch.Poll(animator); yield return null; }
                Assert.That(crouch.Count-baseline,Is.InRange(4,6),name+" crouch must sound all four contacts in a cycle");
                foreach(string clip in crouch.ClipNames) Assert.That(clip,Does.StartWith("SFX_CHR_Step_Carpet"));
                Assert.That(crouch.MaximumVolume,Is.LessThanOrEqualTo(.41f));
                Debug.Log($"PLAYTEST_STEPS ROSTER {name} run={run.Count} crouch={crouch.Count-baseline}");
                Object.Destroy(motor.gameObject); yield return null;
            }
        }

        [UnityTest]
        public IEnumerator RemoteFootstepsUseVisibleGroundInsteadOfDisabledMotor()
        {
            Box(new Vector3(600,-.25f,600),new Vector3(50,.5f,50));
            Listener(new Vector3(600,2,600));
            var motor=Player("Player",new Vector3(600,.1f,590));
            yield return new WaitForSeconds(.3f);
            motor.enabled=false;
            motor.GetComponent<Rigidbody>().isKinematic=true;
            var animator=motor.GetComponentInChildren<Animator>();
            // No camera in this fixture: exercise the actual prefab's offscreen
            // culling as well as the disabled remote motor.
            animator.cullingMode=AnimatorCullingMode.CullUpdateTransforms;
            foreach(var component in motor.GetComponents<MonoBehaviour>())
                if(component is CharacterAnimatorDriver)component.enabled=false;
            animator.SetFloat("Speed",1f);
            animator.Play("Run",0,0);
            var footsteps=motor.GetComponent<CharacterFootsteps>();
            var tracking=typeof(CharacterFootsteps).GetField("trackingCycle",BindingFlags.Instance|BindingFlags.NonPublic);
            var sounds = new StepObserver(motor);
            float until=Time.time+2;
            while(Time.time<until)
            {
                motor.transform.position+=Vector3.forward*3*Time.deltaTime;
                yield return null;
                sounds.Poll(animator);
            }
            Assert.That(sounds.Count,Is.GreaterThanOrEqualTo(4),"Remote runner must actually play steps");
            motor.transform.position+=Vector3.up*3;
            for(int i=0;i<4;i++) { motor.transform.position+=Vector3.forward*.1f;yield return null; }
            Assert.That((bool)tracking.GetValue(footsteps),Is.False,"Remote airborne copy must be silent");
            sounds.Poll(animator);
            int airborneCount = sounds.Count;
            until = Time.time + .7f;
            while (Time.time < until)
            {
                motor.transform.position += Vector3.forward * 3 * Time.deltaTime;
                sounds.Poll(animator); yield return null;
            }
            Assert.That(sounds.Count, Is.EqualTo(airborneCount), "Airborne copy must not start footstep audio");
        }
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach(var go in created)if(go!=null)Object.Destroy(go);created.Clear();
            Time.fixedDeltaTime=originalFixedDelta; Application.targetFrameRate=originalFrameRate;
            QualitySettings.vSyncCount=originalVSync; Application.runInBackground=originalBackground;
            yield return null;
        }
    }
}
#endif
