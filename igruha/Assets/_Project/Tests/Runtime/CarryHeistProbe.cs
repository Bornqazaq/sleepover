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
    /// <summary>Actual owner E/WASD, replicated theft/delivery, four-person slopes and crane contact.</summary>
    public sealed class CarryHeistProbe : MonoBehaviour
    {
        private CarryItemMinigame game;
        private WaterCart cart;
        private PlayerController local;
        private PlayerInputReader reader;
        private readonly List<SessionPlayer> players=new List<SessionPlayer>();
        private readonly List<SessionPlayer> crew=new List<SessionPlayer>();
        private NetworkManager manager;
        private bool failed,driving;
        private Vector3 direction;
        private float stopX=float.PositiveInfinity;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if(!LaunchArguments.TryGetValue("--carry-heist-check",out _))return;
            var root=new GameObject(nameof(CarryHeistProbe));DontDestroyOnLoad(root);root.AddComponent<CarryHeistProbe>();
        }
        private void Update()
        {
            if(reader==null)return;
            bool reached=cart!=null&&(direction.x>0?cart.transform.position.x>=stopX:cart.transform.position.x<=stopX);
            reader.DriveMove(driving&&!reached?local.WorldToMoveInput(direction):Vector2.zero);
        }
        private IEnumerator Start()
        {
            float deadline=Time.realtimeSinceStartup+100;bool ready=false;
            while(Time.realtimeSinceStartup<deadline)
            {
                manager=NetworkManager.Singleton;var selection=CharacterSelection.Current;
                if(manager!=null&&manager.IsConnectedClient&&selection!=null&&!selection.HasChosen&&UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="Hub")
                {
                    selection.ReportReady();for(int i=0;i<8;i++){int slot=((int)manager.LocalClientId+i)%8;if(!selection.IsTaken(slot)){selection.Choose(slot);break;}}
                }
                game=MinigameControllerBase.Current as CarryItemMinigame;
                if(game!=null&&game.AwaitingTutorialReady&&!ready){ready=true;game.ToggleTutorialReady();}
                if(game!=null&&game.Phase==MinigamePhase.Round&&!game.StartCountdownActive)break;
                yield return null;
            }
            if(game==null||game.Phase!=MinigamePhase.Round){Check(false,"round timeout");Application.Quit(1);yield break;}
            yield return new WaitForSeconds(2);
            // Round state can arrive before the last roster/avatar packet on a late peer.
            // Wait for the current round's actual network avatars instead of sampling once.
            deadline=Time.realtimeSinceStartup+8;
            while(Time.realtimeSinceStartup<deadline)
            {
                players.Clear();crew.Clear();local=null;
                foreach(var p in SessionScoreboard.Current.Players)
                {
                    TeamSide side=game.TeamOfPlayer(p.Id);
                    if(side==TeamSide.None||p.Avatar==null)continue;
                    var actor=p.Avatar.GetComponent<NetworkObject>();if(actor==null||!actor.IsSpawned)continue;
                    players.Add(p);if(side==TeamSide.A)crew.Add(p);if(actor.IsOwner)local=p.Avatar;
                }
                if(players.Count==8&&crew.Count==4&&local!=null)break;
                yield return null;
            }
            Check(players.Count==8&&crew.Count==4&&local!=null,"eight owners ready: players="+players.Count+" crew="+crew.Count);
            if(failed){Application.Quit(1);yield break;}
            players.Sort((a,b)=>a.Id.CompareTo(b.Id));crew.Sort((a,b)=>a.Id.CompareTo(b.Id));
            reader=local.GetComponent<PlayerInputReader>();reader.EngageAutopilot();cart=game.CartOf(TeamSide.A);
            var defender=crew[0].Avatar;PlayerController thief=null;
            foreach(var p in players)if(game.TeamOfPlayer(p.Id)==TeamSide.B){thief=p.Avatar;break;}
            yield return At(8);
            if(manager.IsServer)
            {
                ParkAll();cart.Carry.ResetPose(new Vector3(-22,.03f,5.1f),Quaternion.Euler(0,90,0));cart.ChangeWater(150,WaterLossReason.Filled);
                defender.RequestTeleport(cart.Carry.StationOf(0),Quaternion.Euler(0,90,0));
                thief.RequestTeleport(cart.Carry.StationOf(1),Quaternion.Euler(0,90,0));
            }
            yield return At(10);if(local==defender)yield return Tap();
            yield return At(12);if(local==thief)yield return Tap();
            yield return At(14);Check(cart.Carry.CarrierCount==1&&cart.Carry.IsCarriedBy(defender)&&cart.ControlTeam==TeamSide.A,"occupied enemy cart rejects E");
            yield return At(15);if(local==defender)yield return Tap();
            yield return At(16);if(local==thief)yield return Tap();
            yield return At(18);Check(cart.ControlTeam==TeamSide.B&&cart.Team==TeamSide.A&&cart.Carry.IsCarriedBy(thief),"released cart captured by B on all peers");
            if(local==thief)
            {
                var hud=Object.FindFirstObjectByType<CartCoordinationHud>();Check(hud!=null&&hud.Visible,"stolen cart HUD visible to thief");
                direction=Vector3.right;stopX=-19;driving=true;
            }
            yield return At(21);driving=false;Check(cart.transform.position.x>-21,"thief actually drives using WASD");
            if(local==thief)yield return Tap();
            yield return At(23);if(manager.IsServer)cart.Carry.ResetPose(game.TankOf(TeamSide.A).DockPoint+Vector3.up*.03f,Quaternion.identity);
            yield return At(26);Check(game.TankOf(TeamSide.A).Water==0&&!cart.IsPouring&&cart.Water==150,"wrong receiving bay never drains captured cart");
            yield return At(27);if(manager.IsServer)cart.Carry.ResetPose(game.TankOf(TeamSide.B).DockPoint+Vector3.up*.03f,Quaternion.identity);
            yield return At(29);Check(cart.IsPouring&&cart.Water>0&&cart.Water<150,"stolen delivery gradual, no hands required");
            var pump=game.TankOf(TeamSide.B).GetComponent<WaterPumpPresentation>();Check(pump!=null&&pump.IsPumping,"hose follows captured cart");
            yield return At(34);Check(cart.Water==0&&game.TankOf(TeamSide.B).Water==150&&game.TankOf(TeamSide.A).Water==0,"stolen 150 credited only to B");
            yield return At(36);if(manager.IsServer)
            {cart.Carry.ResetPose(new Vector3(-22,.03f,5.1f),Quaternion.Euler(0,90,0));defender.RequestTeleport(cart.Carry.StationOf(0),Quaternion.Euler(0,90,0));}
            yield return At(38);if(local==defender)yield return Tap();
            yield return At(40);Check(cart.ControlTeam==TeamSide.A&&cart.Carry.IsCarriedBy(defender),"A can reclaim the vessel");
            yield return At(43);if(manager.IsServer)SetupSlope(-20.8f);
            yield return At(46);if(IsCrew(local))yield return Tap();
            yield return At(48);Check(cart.Carry.CarrierCount==4,"four hands on ramp");direction=Vector3.right;stopX=-5.8f;driving=IsCrew(local);
            yield return At(59);driving=false;
            Check(cart.transform.position.y>4.4f&&cart.transform.position.x>-7f,"cart climbs real slope onto upper deck pos="+cart.transform.position);
            Check(cart.Carry.CarrierCount==4,"four grips survive ascent");
            yield return At(61);direction=Vector3.left;stopX=-19f;driving=IsCrew(local);
            yield return At(72);driving=false;
            Check(cart.transform.position.y<.65f&&cart.transform.position.x<-21,"cart returns down slope pos="+cart.transform.position);
            Check(cart.Carry.CarrierCount==4,"four grips survive descent");
            yield return At(75);if(manager.IsServer)SetupSlope(9f);
            yield return At(78);if(IsCrew(local))yield return Tap();
            yield return At(80);direction=Vector3.right;stopX=19f;driving=IsCrew(local);
            yield return At(88);driving=false;
            Check(cart.transform.position.y<.65f&&cart.transform.position.x>21,"opposite slope descends pos="+cart.transform.position);
            Check(cart.Carry.CarrierCount==4,"four holders remain after second descent");
            // A held cart must survive a real client leaving, with the other three still attached.
            yield return At(91);
            if(local==crew[3].Avatar)
            {if(!failed)Debug.Log("CARRY_HEIST_CHECK PASS leaving owner="+manager.LocalClientId);Application.Quit(failed?1:0);yield break;}
            yield return At(95);Check(cart.Carry.CarrierCount==3&&cart.Carry.HandleCount==3,"disconnect shrinks captured crew without dropping others");
            yield return At(96);if(manager.IsServer)
            {
                var hazard=Object.FindFirstObjectByType<CarryCraneHazard>();hazard.enabled=false;hazard.GetComponent<Collider>().enabled=false;
                cart.Carry.ReleaseAll(CarryReleaseReason.RoundEnded);ParkAll();
                cart.Carry.ResetPose(new Vector3(.9f,4.53f,0),Quaternion.Euler(0,90,0));
                defender.RequestTeleport(cart.Carry.StationOf(0),Quaternion.Euler(0,90,0));
            }
            yield return At(98);if(local==defender)yield return Tap();
            yield return At(99);
            bool knocked=false,held=cart.Carry.IsCarriedBy(defender);
            bool knockedGrip=false;
            cart.Carry.HandleReleased+=(slot,player,reason)=>{if(player==defender&&reason==CarryReleaseReason.Knockdown)knockedGrip=true;};
            yield return At(101);
            if(manager.IsServer)Object.FindFirstObjectByType<CarryCraneHazard>().enabled=true;
            float end=Time.time+10;
            while(Time.time<end){held|=cart.Carry.IsCarriedBy(defender);knocked|=defender.IsKnockedDown;yield return null;}
            Check(held&&knockedGrip&&(local!=defender||knocked)&&!cart.Carry.IsCarriedBy(defender),"visible rotating load knocks player down and breaks grip held="+held);
            if(!failed)Debug.Log("CARRY_HEIST_CHECK PASS id="+manager.LocalClientId);
            yield return new WaitForSeconds(2);Application.Quit(failed?1:0);
        }
        private bool IsCrew(PlayerController player){foreach(var p in crew)if(p.Avatar==player)return true;return false;}
        private void ParkAll()
        {
            game.CartOf(TeamSide.B).Carry.ResetPose(new Vector3(-23,0,-6),Quaternion.identity);
            int i=0;foreach(var p in players)if(p.Avatar!=null)p.Avatar.RequestTeleport(new Vector3(-22+i++*.8f,0,-3.8f),Quaternion.identity);
        }
        private void SetupSlope(float x)
        {
            cart.Carry.ReleaseAll(CarryReleaseReason.RoundEnded);ParkAll();
            float y=x< -7.2f?(x+22)*4.5f/14.8f:x>7.2f?(22-x)*4.5f/14.8f:4.5f;
            cart.Carry.ResetPose(new Vector3(x,y+.03f,0),Quaternion.Euler(0,90,0));cart.ChangeWater(-150,WaterLossReason.Poured);cart.SetHandleCount(4);
            for(int i=0;i<crew.Count;i++)
            {
                Vector3 p=cart.Carry.StationOf(i);
                if(Physics.Raycast(p+Vector3.up*1.5f,Vector3.down,out RaycastHit hit,3,LayerMask.GetMask("Ground"),QueryTriggerInteraction.Ignore))p.y=hit.point.y+.03f;
                crew[i].Avatar.RequestTeleport(p,Quaternion.Euler(0,90,0));
            }
        }
        private IEnumerator Tap(){reader.DriveInteractHold(true);yield return new WaitForSeconds(.08f);reader.DriveInteractHold(false);}
        private IEnumerator At(float target)
        {
            float deadline=Time.realtimeSinceStartup+25;
            while(Time.realtimeSinceStartup<deadline){if(game.TryGetRoundTime(out float left,out float total)&&total-left>=target)yield break;yield return null;}
            Check(false,"timer timeout");
        }
        private void Check(bool ok,string message)
        {if(ok)Debug.Log("CARRY_HEIST_CHECK "+message);else{failed=true;Debug.LogError("CARRY_HEIST_CHECK FAIL "+message);}}
    }
}
