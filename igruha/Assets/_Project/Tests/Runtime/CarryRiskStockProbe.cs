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
    /// <summary>Opt-in real network owners: solo fast/careful driving, finite fleet, stealing and exhaustion results.</summary>
    public sealed class CarryRiskStockProbe : MonoBehaviour
    {
        private CarryItemMinigame game;
        private NetworkManager net;
        private PlayerController local;
        private PlayerInputReader reader;
        private TeamSide localTeam;
        private readonly List<SessionPlayer> a=new List<SessionPlayer>(),b=new List<SessionPlayer>();
        private bool driving,failed;
        private readonly bool[] gripLost=new bool[2];
        private readonly float[] flow=new float[2];
        private bool Driver => local==(localTeam==TeamSide.A?a[0]:b[0]).Avatar;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if(!LaunchArguments.TryGetValue("--carry-risk-check",out _))return;
            var root=new GameObject(nameof(CarryRiskStockProbe));DontDestroyOnLoad(root);root.AddComponent<CarryRiskStockProbe>();
        }
        private void Update()
        {
            if(reader==null||game==null)return;
            Vector2 input=Vector2.zero;
            if(driving)
            {
                for(int i=0;i<2;i++)
                {
                    var c=game.CartOf(i==0?TeamSide.A:TeamSide.B);if(c==null)continue;
                    gripLost[i]|=c.Carry.CarrierCount!=1;flow[i]=Mathf.Max(flow[i],c.Stability.State.Outflow);
                }
                var cart=game.CartOf(localTeam);
                if(Driver&&cart!=null&&cart.transform.position.x<7.15f)
                    input=local.WorldToMoveInput(Vector3.right)*(localTeam==TeamSide.A?1:.30f);
            }
            reader.DriveMove(input);
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
                if(a.Count>0&&b.Count>0&&local!=null)break;yield return null;
            }
            Check(a.Count>0&&b.Count>0&&local!=null,"network crews ready");if(failed){Application.Quit(1);yield break;}
            a.Sort((x,y)=>x.Id.CompareTo(y.Id));b.Sort((x,y)=>x.Id.CompareTo(y.Id));
            reader=local.GetComponent<PlayerInputReader>();reader.EngageAutopilot();
            yield return At(8);if(net.IsServer)
            {
                Park();SetCart(game.CartOf(TeamSide.A),new Vector3(-3.2f,.03f,1.35f),150);
                SetCart(game.CartOf(TeamSide.B),new Vector3(-3.2f,.03f,-1.35f),150);
                Place(a[0].Avatar,game.CartOf(TeamSide.A));Place(b[0].Avatar,game.CartOf(TeamSide.B));
            }
            yield return At(10);if(Driver)yield return Tap();
            yield return At(12);driving=true;
            yield return At(39);driving=false;
            var ca=game.CartOf(TeamSide.A);var cb=game.CartOf(TeamSide.B);
            Debug.Log("CARRY_RISK_CHECK road water="+ca.Water+"/"+cb.Water+" flow="+flow[0]+"/"+flow[1]+" x="+ca.transform.position.x+"/"+cb.transform.position.x);
            Check(ca.Water<=120&&ca.Water>35&&cb.Water>=145,"solo W spills while careful drive saves water");
            Check(!gripLost[0]&&!gripLost[1]&&ca.transform.position.x>6.9f&&cb.transform.position.x>6.9f,"both drivers finish with their grip");
            yield return At(41);if(Driver)yield return Tap();
            yield return At(43);if(net.IsServer)Fall(ca);
            yield return At(45);Check(ca.IsLost&&ca.RemainingCarts==1&&ca.Water==0,"first fall consumes one vessel");
            yield return At(50);Check(!ca.IsLost&&ca.RemainingCarts==1&&Vector3.Distance(ca.transform.position,ca.HomePosition)<.25f,"one replacement returns home");
            yield return At(52);if(net.IsServer){Park();SetCart(ca,new Vector3(-20.5f,.03f,5.1f),150);Place(b[0].Avatar,ca);}
            yield return At(54);if(local==b[0].Avatar)yield return Tap();
            yield return At(56);Check(ca.ControlTeam==TeamSide.B&&ca.Carry.CarrierCount==1&&ca.RemainingCarts==1,"theft preserves original fleet stock");
            yield return At(57);
            if(local==b[0].Avatar)yield return Tap();
            yield return At(58);if(net.IsServer){Park();SetCart(ca,new Vector3(19.59f,.03f,-11),150);}
            yield return At(60);Check(ca.IsPouring&&ca.Water>0&&game.TankOf(TeamSide.B).Water>0,"stolen cart pumps gradually into thief tank");
            yield return At(65);Check(game.TankOf(TeamSide.B).Water==150&&ca.Water==0,"stolen water credited once");
            yield return At(67);if(net.IsServer)Fall(ca);
            yield return At(70);Check(ca.IsDepleted&&ca.RemainingCarts==0&&!ca.Carry.enabled,"second fall exhausts original fleet");
            yield return At(73);if(net.IsServer){Park();SetCart(cb,new Vector3(-20.5f,.03f,5.1f),100);Place(a[0].Avatar,cb);}
            yield return At(75);if(local==a[0].Avatar)yield return Tap();
            yield return At(77);Check(ca.IsDepleted&&cb.ControlTeam==TeamSide.A&&cb.Carry.CarrierCount==1,"team with zero stock can still steal");
            yield return At(78);
            if(local==a[0].Avatar)yield return Tap();
            yield return At(79);if(net.IsServer)Fall(cb);
            yield return At(81);Check(cb.IsLost&&cb.RemainingCarts==1&&ca.RemainingCarts==0,"stolen fall debits vessel original team only");
            yield return At(86);Check(!cb.IsLost&&cb.ControlTeam==TeamSide.B&&cb.RemainingCarts==1,"stolen replacement restores original owner");
            yield return At(88);if(net.IsServer)Fall(cb);
            deadline=Time.realtimeSinceStartup+5;
            while(game.Phase==MinigamePhase.Round&&Time.realtimeSinceStartup<deadline)yield return null;
            Check(game.Phase!=MinigamePhase.Round&&game.State.TeamB.Water==150&&game.State.TeamA.Water==0,"no vessels ends round with delivered score");
            if(!failed)Debug.Log("CARRY_RISK_CHECK PASS id="+net.LocalClientId+" players="+(a.Count+b.Count));
            yield return new WaitForSeconds(2);Application.Quit(failed?1:0);
        }
        private void Park()
        {
            foreach(var crew in new[]{a,b}) for(int i=0;i<crew.Count;i++)
                crew[i].Avatar.RequestTeleport(new Vector3(-23f,.03f,(crew==a?1:-1)*(8.5f+i*.7f)),Quaternion.identity);
        }
        private static void SetCart(WaterCart c,Vector3 p,int water)
        {c.Carry.ResetPose(p,Quaternion.Euler(0,90,0));c.ChangeWater(-c.Water,WaterLossReason.Poured);c.ChangeWater(water,WaterLossReason.Filled);c.Stability.ResetTrip();}
        private static void Place(PlayerController p,WaterCart c)=>p.RequestTeleport(c.Carry.StationOf(0)+Vector3.up*.03f,Quaternion.Euler(0,90,0));
        private static void Fall(WaterCart c)=>c.Carry.ResetPose(new Vector3(-9,-8,0),Quaternion.identity);
        private float Elapsed(){game.TryGetRoundTime(out float remaining,out float duration);return duration-remaining;}
        private IEnumerator Tap(){reader.DriveInteractHold(true);yield return new WaitForSeconds(.08f);reader.DriveInteractHold(false);}
        private IEnumerator At(float time){float deadline=Time.realtimeSinceStartup+65;while(Elapsed()<time&&Time.realtimeSinceStartup<deadline)yield return null;Check(Elapsed()>=time,"phase "+time);}
        private void Check(bool pass,string message){if(pass)Debug.Log("CARRY_RISK_CHECK "+message);else{failed=true;Debug.LogError("CARRY_RISK_CHECK FAIL "+message);}}
    }
}
