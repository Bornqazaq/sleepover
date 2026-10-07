using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Minigames.Circus;

namespace Igruha.EditorTools
{
    /// <summary>Exercises real player get-up states and the complete committed attack.</summary>
    internal static class CircusBearAudit
    {
        [MenuItem("Igruha/Цирк/Проверить фору и удар Бруно")]
        private static void RunMenu()=>Debug.Log(Check());

        internal static string Check()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Run outside Play Mode.");
            var source=UnityEngine.Object.FindFirstObjectByType<PitBear>();
            if(source==null)throw new InvalidOperationException("Open a circus scene.");
            var copy=UnityEngine.Object.Instantiate(source.gameObject);
            var floor=new GameObject("BearAuditFloor");floor.transform.position=new Vector3(0,99.5f,0);
            floor.AddComponent<BoxCollider>().size=new Vector3(40,1,40);
            var bear=copy.GetComponent<PitBear>();var output=new StringBuilder();
            var motion=copy.GetComponentInChildren<CircusBearMotion>();
            Invoke(bear,"Awake");Invoke(motion,"Awake");
            Set(bear,"feedback",null);
            try
            {
                foreach(string name in new[]{"Player","Aza","Boss","Fat","Girl","Milez","MyBoy","Shlanga"})
                {
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/"+name+".prefab");
                    var avatar=UnityEngine.Object.Instantiate(prefab);
                    try
                    {
                        var player=avatar.GetComponent<PlayerController>();player.enabled=false;
                        foreach(var collider in avatar.GetComponentsInChildren<Collider>())collider.enabled=false;
                        var animator=avatar.GetComponentInChildren<Animator>();animator.Rebind();
                        Reset(bear);player.transform.position=new Vector3(0,104,2);bear.RegisterFallen(player);
                        for(int i=0;i<120;i++)
                        {Require(!bear.CanChase(player,1f/30),name+" is protected while falling");bear.Tick(1f/30,null,false);}
                        Require(Vector3.Distance(bear.transform.position,new Vector3(0,100,0))<.001f,"No approach during fall");
                        player.transform.position=new Vector3(0,100.05f,2);Physics.SyncTransforms();
                        foreach(string state in new[]{"FlyBack","FallForward","StandUpFromBack","StandUpFromForward"})
                        {
                            Require(animator.HasState(0,Animator.StringToHash(state)),name+" has "+state);
                            animator.Play(state,0,0);animator.Update(.001f);
                            for(int i=0;i<40;i++)Require(!bear.CanChase(player,.05f),name+" protected throughout "+state);
                        }
                        animator.Play("Idle",0,0);animator.Update(.001f);
                        Require(bear.CanChase(player,.01f),name+" becomes chaseable immediately after getting up");
                        bear.Tick(.33f,player,false);
                        for(int i=0;i<28;i++)bear.Tick(.1f,player,false);
                        Require(bear.State==PitBear.BearState.WindUp,"Full three-second head start");
                        Require(Vector3.Distance(bear.transform.position,new Vector3(0,100,0))<.001f,"No movement during head start");
                        // Readiness is latched: subsequent jumps cannot refresh protection.
                        player.transform.position+=Vector3.up*2;
                        Require(bear.CanChase(player,.1f),"Jump cannot renew protection");
                        output.AppendLine(name+": falling, four recovery states, grounded readiness and full head start PASS.");
                    }
                    finally{UnityEngine.Object.DestroyImmediate(avatar);}
                }
                var target=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Player.prefab"));
                try
                {
                    var player=target.GetComponent<PlayerController>();player.enabled=false;
                    int hits=0;bear.Caught+=(p,impulse)=>hits++;
                    Reset(bear,0);player.transform.position=new Vector3(0,100.05f,2.65f);
                    player.transform.rotation=Quaternion.Euler(0,180,0);
                    var playerAnimator=target.GetComponentInChildren<Animator>();
                    playerAnimator.Rebind();playerAnimator.Play("Idle",0,.25f);playerAnimator.Update(0);
                    bear.Tick(.01f,player,false);Require(bear.State==PitBear.BearState.Attack,"Attack starts before impact");
                    var animator=copy.GetComponentInChildren<Animator>();animator.Rebind();
                    bear.ApplyNetworkState(PitBear.BearState.Patrol,0);bear.ApplyNetworkState(PitBear.BearState.Attack,0);animator.Update(.04f);
                    int strike=Animator.StringToHash("Strike");
                    Require(animator.GetCurrentAnimatorStateInfo(0).shortNameHash==strike || animator.GetNextAnimatorStateInfo(0).shortNameHash==strike,"Replicated Attack plays the swipe before the hit");
                    float elapsed=0;
                    Step(bear,motion,animator,ref elapsed,PitBear.ContactSeconds-.01f,player);Require(hits==0,"No damage during anticipation");
                    Step(bear,motion,animator,ref elapsed,.03f,player);Require(hits==1,"One impact from the current solved paw at authored contact");
                    Step(bear,motion,animator,ref elapsed,.5f,null);Require(bear.State==PitBear.BearState.Attack && hits==1,"Follow-through survives target removal and taunt");
                    Step(bear,motion,animator,ref elapsed,.6f,null);Require(bear.State==PitBear.BearState.Recovery,"Attack enters recovery");
                    Vector3 at=bear.transform.position;bear.Tick(.2f,null,false);
                    Require(bear.State==PitBear.BearState.Recovery && bear.transform.position==at,"Planted recovery pause");
                    Reset(bear,0);player.transform.position=new Vector3(0,100.05f,2.65f);bear.Tick(.01f,player,false);
                    elapsed=0;player.transform.position=new Vector3(4,100.05f,0);Step(bear,motion,animator,ref elapsed,.75f,player);
                    Require(hits==1,"Sideways dodge avoids a committed attack");
                    Require(Quaternion.Angle(bear.transform.rotation,Quaternion.identity)<.001f,"Swipe does not home after the dodge");
                    bear.ApplyNetworkState(PitBear.BearState.Patrol,0);
                    bear.ApplyNetworkState(PitBear.BearState.Attack,0,.45f);
                    animator.Update(.08f);
                    Require(animator.GetCurrentAnimatorStateInfo(0).shortNameHash==strike,"Late phase snapshot still displays Strike");
                    Require(animator.GetCurrentAnimatorStateInfo(0).normalizedTime>.24f,"Late snapshot seeks into anticipation");
                    bear.ShowImpact(player.transform.position,true);animator.Update(0);
                    Require(animator.GetCurrentAnimatorStateInfo(0).normalizedTime>=PitBear.ContactSeconds/PitBear.StrikeSeconds-.01f,"Contact cue cannot arrive while the bear is visually idle");
                    output.AppendLine("Replicated swipe, delayed single hit, uninterrupted follow-through, recovery and dodge PASS.");
                }
                finally{UnityEngine.Object.DestroyImmediate(target);}
            }
            finally{UnityEngine.Object.DestroyImmediate(copy);UnityEngine.Object.DestroyImmediate(floor);Physics.SyncTransforms();}
            return output.ToString();
        }

        private static void Reset(PitBear bear,float delay=3)
        {bear.Configure(5.5f,2.1f,2.35f,delay,8,8.64f);bear.transform.SetPositionAndRotation(new Vector3(0,100,0),Quaternion.identity);}
        private static void Step(PitBear bear,CircusBearMotion motion,Animator animator,ref float elapsed,float dt,PlayerController target)
        {
            elapsed+=dt;Set(bear,"visualAttackStartedAt",Time.time-elapsed);
            bear.Tick(dt,target,true);
            if(bear.State==PitBear.BearState.Attack){animator.Play("Strike",0,elapsed/PitBear.StrikeSeconds);animator.Update(0);}
            Invoke(motion,"EvaluatePose",dt);
            Invoke(bear,"LateUpdate");
        }
        private const BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.NonPublic;
        private static void Set(object target,string field,object value)=>target.GetType().GetField(field,PrivateInstance).SetValue(target,value);
        private static void Invoke(object target,string method,params object[] arguments)=>target.GetType().GetMethod(method,PrivateInstance).Invoke(target,arguments);
        private static void Require(bool value,string message)
        {if(!value)throw new InvalidOperationException("Bruno audit: "+message);}
    }
}
