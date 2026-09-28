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
            Box(new Vector3(500,-.25f,500),new Vector3(50,.5f,50));
            var fat=Player("Fat",new Vector3(498,.1f,490));
            var small=Player("Player",new Vector3(502,.1f,490));
            yield return new WaitForSeconds(.4f);
            fat.GetComponent<PlayerInputReader>().DriveMove(Vector2.up);
            small.GetComponent<PlayerInputReader>().DriveMove(Vector2.up);
            var step=typeof(CharacterFootsteps).GetField("previousStep",BindingFlags.Instance|BindingFlags.NonPublic);
            int fatCount=0,smallCount=0,fp=-1,sp=-1;
            float until=Time.time+3f;
            while(Time.time<until)
            {
                int f=(int)step.GetValue(fat.GetComponent<CharacterFootsteps>());
                int s=(int)step.GetValue(small.GetComponent<CharacterFootsteps>());
                if(fp>=0 && f>fp)fatCount++; if(sp>=0 && s>sp)smallCount++; fp=f;sp=s;
                yield return null;
            }
            Assert.That(fatCount,Is.GreaterThanOrEqualTo(5));
            Assert.That(smallCount,Is.GreaterThan(fatCount),$"Karlan={smallCount}, Fat={fatCount}");
            Debug.Log($"PLAYTEST_STEPS PASS Karlan={smallCount} Fat={fatCount}");
        }
        [UnityTest]
        public IEnumerator RemoteFootstepsUseVisibleGroundInsteadOfDisabledMotor()
        {
            Box(new Vector3(600,-.25f,600),new Vector3(50,.5f,50));
            var motor=Player("Player",new Vector3(600,.1f,590));
            yield return new WaitForSeconds(.3f);
            motor.enabled=false;
            motor.GetComponent<Rigidbody>().isKinematic=true;
            var animator=motor.GetComponentInChildren<Animator>();
            foreach(var component in motor.GetComponents<MonoBehaviour>())
                if(component is CharacterAnimatorDriver)component.enabled=false;
            animator.SetFloat("Speed",1f);
            animator.Play("Run",0,0);
            var footsteps=motor.GetComponent<CharacterFootsteps>();
            var tracking=typeof(CharacterFootsteps).GetField("trackingCycle",BindingFlags.Instance|BindingFlags.NonPublic);
            var step=typeof(CharacterFootsteps).GetField("previousStep",BindingFlags.Instance|BindingFlags.NonPublic);
            int previous=-1,count=0;
            float until=Time.time+2;
            while(Time.time<until)
            {
                motor.transform.position+=Vector3.forward*3*Time.deltaTime;
                yield return null;
                int current=(int)step.GetValue(footsteps);
                if(previous>=0 && current>previous)count++;
                previous=current;
            }
            Assert.That(count,Is.GreaterThanOrEqualTo(4),"Remote runner must have steps");
            motor.transform.position+=Vector3.up*3;
            for(int i=0;i<4;i++) { motor.transform.position+=Vector3.forward*.1f;yield return null; }
            Assert.That((bool)tracking.GetValue(footsteps),Is.False,"Remote airborne copy must be silent");
        }
        [UnityTearDown]
        public IEnumerator Cleanup(){foreach(var go in created)if(go!=null)Object.Destroy(go);created.Clear();yield return null;}
    }
}
#endif
