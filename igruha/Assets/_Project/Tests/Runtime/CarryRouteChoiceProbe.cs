using System.Collections;
using System.Collections.Generic;
using Igruha.Core.Items;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.CarryItem;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>Opt-in two real crews compare complete deliveries, then the same seams at two speeds.</summary>
    public sealed class CarryRouteChoiceProbe : MonoBehaviour
    {
        private CarryItemMinigame game;
        private NetworkManager net;
        private PlayerController local;
        private PlayerInputReader reader;
        private TeamSide localTeam;
        private readonly List<SessionPlayer> a=new List<SessionPlayer>(),b=new List<SessionPlayer>();
        private readonly Vector3[][] paths=new Vector3[2][];
        private readonly int[] next=new int[2];
        private readonly float[] arrived=new float[2],maxHop=new float[2],maxFlow=new float[2];
        private readonly bool[] lostGrip=new bool[2];
        private bool driving,failed,slowSecond;
        private float began, nextTelemetry;
        private float driveStrength;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if(!LaunchArguments.TryGetValue("--carry-route-check",out _))return;
            var root=new GameObject(nameof(CarryRouteChoiceProbe));DontDestroyOnLoad(root);root.AddComponent<CarryRouteChoiceProbe>();
        }
        private void Update()
        {
            if(reader==null||game==null)return;
            Vector2 input=Vector2.zero;
            if(driving)for(int side=0;side<2;side++)
            {
                var cart=game.CartOf(side==0?TeamSide.A:TeamSide.B);Vector3 p=cart.transform.position;p.y=0;
                var target=paths[side][next[side]];target.y=0;Vector3 delta=target-p;
                while(next[side]<paths[side].Length-1&&delta.magnitude<.5f)
                {next[side]++;target=paths[side][next[side]];target.y=0;delta=target-p;}
                bool finished=next[side]==paths[side].Length-1&&delta.magnitude<.45f;
                if(finished&&arrived[side]==0)arrived[side]=Elapsed()-began;
                if(cart.Carry.CarrierCount!=4)lostGrip[side]=true;
                maxHop[side]=Mathf.Max(maxHop[side],cart.Stability.State.RoadHop);
                maxFlow[side]=Mathf.Max(maxFlow[side],cart.Stability.State.Outflow);
                if(localTeam!=(side==0?TeamSide.A:TeamSide.B))continue;
                if(finished||arrived[side]>0)continue;
                float strength=slowSecond&&side==1?.30f:1;
                if(next[side]==paths[side].Length-1)strength=Mathf.Min(strength,Mathf.Clamp(delta.magnitude/2,.18f,1));
                if(!slowSecond&&next[side]<paths[side].Length-1&&delta.magnitude<3.8f)
                {
                    Vector3 following=paths[side][next[side]+1]-target;following.y=0;
                    if(Vector3.Angle(delta,following)>25)strength=Mathf.Lerp(.30f,1f,Mathf.InverseLerp(1.8f,3.8f,delta.magnitude));
                    // A rounded road has many short tangent segments. Read its curvature
                    // over a physical look-ahead distance so tessellation cannot defeat braking.
                    Vector3 lookAhead=target;float remaining=3.8f;
                    for(int node=next[side]+1;node<paths[side].Length&&remaining>0;node++)
                    {
                        Vector3 segment=paths[side][node]-lookAhead;segment.y=0;
                        float distance=Mathf.Min(segment.magnitude,remaining);
                        lookAhead+=segment.normalized*distance;remaining-=distance;
                    }
                    Vector3 upcoming=lookAhead-target;upcoming.y=0;
                    if(Vector3.Angle(delta,upcoming)>15)
                        strength=Mathf.Min(strength,Mathf.Lerp(.30f,1f,Mathf.InverseLerp(1.8f,3.8f,delta.magnitude)));
                }
                if(!slowSecond&&cart.Carry.FlatVelocity.sqrMagnitude>.04f&&Vector3.Angle(cart.Carry.FlatVelocity,delta)>12)strength=Mathf.Min(strength,.30f);
                driveStrength=Mathf.MoveTowards(driveStrength,strength,Time.deltaTime*.75f);
                input=local.WorldToMoveInput(delta)*driveStrength;
            }
            reader.DriveMove(input);
            if(driving&&net.IsServer&&Elapsed()>=nextTelemetry)
            {
                nextTelemetry=Elapsed()+10;
                Debug.Log("CARRY_ROUTE_CHECK progress t="+Elapsed()+" next="+next[0]+"/"+next[1]+" grips="+game.CartOf(TeamSide.A).Carry.CarrierCount+"/"+game.CartOf(TeamSide.B).Carry.CarrierCount+" positions="+game.CartOf(TeamSide.A).transform.position+"/"+game.CartOf(TeamSide.B).transform.position);
            }
        }
        private IEnumerator Start()
        {
            float deadline=Time.realtimeSinceStartup+110;bool ready=false;
            while(Time.realtimeSinceStartup<deadline)
            {
                net=NetworkManager.Singleton;var selection=CharacterSelection.Current;
                if(net!=null&&net.IsConnectedClient&&selection!=null&&!selection.HasChosen&&UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="Hub")
                {selection.ReportReady();for(int i=0;i<8;i++){int slot=((int)net.LocalClientId+i)%8;if(!selection.IsTaken(slot)){selection.Choose(slot);break;}}}
                game=MinigameControllerBase.Current as CarryItemMinigame;
                if(game!=null&&game.AwaitingTutorialReady&&!ready){ready=true;game.ToggleTutorialReady();}
                if(game!=null&&game.Phase==MinigamePhase.Round&&!game.StartCountdownActive)break;yield return null;
            }
            if(game==null||game.Phase!=MinigamePhase.Round){Check(false,"round timeout");Application.Quit(1);yield break;}
            deadline=Time.realtimeSinceStartup+10;
            while(Time.realtimeSinceStartup<deadline)
            {
                a.Clear();b.Clear();local=null;
                foreach(var p in SessionScoreboard.Current.Players)
                {
                    if(p.Avatar==null||!p.Avatar.GetComponent<NetworkObject>().IsSpawned)continue;
                    var team=game.TeamOfPlayer(p.Id);if(team==TeamSide.A)a.Add(p);else if(team==TeamSide.B)b.Add(p);
                    if(p.Avatar.GetComponent<NetworkObject>().IsOwner){local=p.Avatar;localTeam=team;}
                }
                if(a.Count==4&&b.Count==4&&local!=null)break;yield return null;
            }
            Check(a.Count==4&&b.Count==4&&local!=null,"eight network owners ready");if(failed){Application.Quit(1);yield break;}
            a.Sort((x,y)=>x.Id.CompareTo(y.Id));b.Sort((x,y)=>x.Id.CompareTo(y.Id));
            reader=local.GetComponent<PlayerInputReader>();reader.EngageAutopilot();
            paths[0]=CarryRouteLayout.Delivery(0,1);paths[1]=CarryRouteLayout.Delivery(2,-1);
            yield return At(8);if(net.IsServer)Setup();
            yield return At(10);yield return Tap();
            yield return At(12);Begin();
            yield return At(68);driving=false;
            float roughTotal=game.CartOf(TeamSide.A).Water+game.TankOf(TeamSide.A).Water;
            float safeTotal=game.CartOf(TeamSide.B).Water+game.TankOf(TeamSide.B).Water;
            Report("delivery",roughTotal,safeTotal);
            Check(arrived[0]>0&&arrived[1]>0&&!lostGrip[0]&&!lostGrip[1],"both full routes finish with four grips");
            Check(arrived[0]>0&&arrived[0]+3<arrived[1],"rough shortcut saves actual driving time");
            Check(safeTotal>=135&&roughTotal<=safeTotal-15,"safe detour preserves visibly more water");
            Check(maxHop[0]>.008f&&maxFlow[0]>1,"rough road physically hops and visibly spills");
            // Equal geometry/load, only speed differs: crawling is a real alternative to accepting loss.
            paths[0]=new[]{new Vector3(-3.2f,0,1.35f),new Vector3(7.2f,0,1.35f)};
            paths[1]=new[]{new Vector3(-3.2f,0,-1.35f),new Vector3(7.2f,0,-1.35f)};
            yield return At(70);yield return Tap();
            yield return At(72);if(net.IsServer)Setup();
            yield return At(75);yield return Tap();
            yield return At(77);slowSecond=true;Begin();
            yield return At(107);driving=false;
            float fast=game.CartOf(TeamSide.A).Water,slow=game.CartOf(TeamSide.B).Water;Report("same seams",fast,slow);
            Check(arrived[0]>0&&arrived[1]>0&&!lostGrip[0]&&!lostGrip[1],"both seam speeds retain four grips");
            Check(slow>=145&&fast<=slow-15,"slowing down prevents the road penalty");
            if(!failed)Debug.Log("CARRY_ROUTE_CHECK PASS id="+net.LocalClientId);
            yield return new WaitForSeconds(2);Application.Quit(failed?1:0);
        }
        private void Setup()
        {
            for(int side=0;side<2;side++)
            {
                var cart=game.CartOf(side==0?TeamSide.A:TeamSide.B);var crew=side==0?a:b;
                if(cart.GetComponent<CarryRouteContactProbe>()==null)cart.gameObject.AddComponent<CarryRouteContactProbe>();
                cart.Carry.ReleaseAll(CarryReleaseReason.RoundEnded);Vector3 d=paths[side][1]-paths[side][0];d.y=0;
                cart.Carry.ResetPose(paths[side][0]+Vector3.up*.03f,Quaternion.LookRotation(d));
                cart.ChangeWater(-cart.Water,WaterLossReason.Poured);cart.ChangeWater(150,WaterLossReason.Filled);cart.SetHandleCount(4);cart.Stability.ResetTrip();
                for(int i=0;i<crew.Count;i++)crew[i].Avatar.RequestTeleport(cart.Carry.StationOf(i)+Vector3.up*.03f,Quaternion.LookRotation(d));
            }
        }
        private void Begin()
        {for(int i=0;i<2;i++){next[i]=1;arrived[i]=maxFlow[i]=maxHop[i]=0;lostGrip[i]=false;}began=Elapsed();nextTelemetry=began+10;driving=true;}
        private void Report(string phase,float first,float second)
        {Debug.Log("CARRY_ROUTE_CHECK "+phase+" A="+first+" B="+second+" seconds="+arrived[0]+"/"+arrived[1]+" hop="+maxHop[0]+"/"+maxHop[1]+" flow="+maxFlow[0]+"/"+maxFlow[1]+" pos="+game.CartOf(TeamSide.A).transform.position+"/"+game.CartOf(TeamSide.B).transform.position);}
        private float Elapsed(){game.TryGetRoundTime(out float remaining,out float duration);return duration-remaining;}
        private IEnumerator Tap(){reader.DriveInteractHold(true);yield return new WaitForSeconds(.08f);reader.DriveInteractHold(false);}
        private IEnumerator At(float time){float deadline=Time.realtimeSinceStartup+65;while(Elapsed()<time&&Time.realtimeSinceStartup<deadline)yield return null;Check(Elapsed()>=time,"phase "+time);}
        private void Check(bool pass,string message){if(pass)Debug.Log("CARRY_ROUTE_CHECK "+message);else{failed=true;Debug.LogError("CARRY_ROUTE_CHECK FAIL "+message);}}
    }
    public sealed class CarryRouteContactProbe : MonoBehaviour
    {
        private float nextReport;
        private void OnCollisionStay(Collision collision)
        {
            if(Time.time<nextReport||GetComponent<MultiCarryObject>().FlatVelocity.sqrMagnitude>.01f)return;
            for(int i=0;i<collision.contactCount;i++)
            {
                var point=collision.GetContact(i);
                if(Mathf.Abs(point.normal.y)>.8f)continue;
                Debug.Log("CARRY_ROUTE_CHECK side contact "+name+" "+collision.collider.name+" "+point.point+" normal="+point.normal);
                nextReport=Time.time+5;break;
            }
        }
    }
}
