using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Minigames.OneBullet;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>Opt-in development-only integration probe: real contacts, RPC, raycasts and replicated results.</summary>
    public sealed class OneBulletNetworkProbe : MonoBehaviour
    {
        private OneBulletMinigame game;
        private int shots, deaths, pickups;
        private string scenario;
        private OneBulletNetwork relay;
        private bool ready, missed, hit, movedToStage, loggedFinal, loggedReturn;
        private Igruha.Core.CameraSystems.FirstPersonCameraRig firstPersonRig;
        private OneBulletFirstPerson firstPerson;
        private bool checkedView;
        private OneBulletHandsProbe hands;
        private float createdAt, nextLog;
        private double moveUntil;
        private Vector3 moveTarget;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--onebullet-check",out _)) return;
            var go=new GameObject("OneBulletNetworkProbe");DontDestroyOnLoad(go);go.AddComponent<OneBulletNetworkProbe>();
        }
        private void Awake() { createdAt=Time.realtimeSinceStartup; LaunchArguments.TryGetValue("--onebullet-check",out scenario); }
        private void Update()
        {
            var current=MinigameControllerBase.Current as OneBulletMinigame;
            if(Time.realtimeSinceStartup>nextLog)
            {
                nextLog=Time.realtimeSinceStartup+5;
                var board=SessionScoreboard.Current; string avatars="";
                if(board!=null)foreach(var p in board.Players)avatars+=p.Id+":"+(p.Avatar!=null)+",";
                Debug.Log("ONE_BULLET TRACE scene="+UnityEngine.SceneManagement.SceneManager.GetActiveScene().name+" game="+(current!=null?current.Phase.ToString():"null")+" participants="+(current!=null?current.Participants.Count:0)+" avatars="+avatars);
            }
            if(current!=game && current!=null)
            {
                game=current; relay=game.GetComponent<OneBulletNetwork>();
                firstPersonRig=Object.FindFirstObjectByType<Igruha.Core.CameraSystems.FirstPersonCameraRig>(FindObjectsInactive.Include);
                firstPerson=Object.FindFirstObjectByType<OneBulletFirstPerson>();
                if(scenario=="hands") { hands=game.gameObject.AddComponent<OneBulletHandsProbe>(); hands.Initialize(game,firstPerson,firstPersonRig); }
                game.Shot+=(a,b,c)=>{shots++;Debug.Log("ONE_BULLET shot="+shots+" hit="+c);};
                game.Died+=(id,p)=>{deaths++;Debug.Log("ONE_BULLET death="+id);};
                game.PickedUp+=id=>{pickups++;Debug.Log("ONE_BULLET pickup="+id);};
            }
            if(game==null)
            {
                if(loggedFinal&&!loggedReturn && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="Hub")
                {loggedReturn=true;var p=SessionScoreboard.Current?.LocalPlayer;Debug.Log("ONE_BULLET RETURN lock="+(p?.Avatar!=null&&p.Avatar.MovementLocked));if(Object.FindFirstObjectByType<OneBulletFirstPerson>()!=null)Debug.LogError("ONE_BULLET FAIL first person leaked into hub");Application.Quit();}
                else if(!loggedFinal&&Time.realtimeSinceStartup-createdAt>150) {Debug.LogError("ONE_BULLET FAIL no scene");Application.Quit(2);}
                return;
            }
            if(game.Phase==MinigamePhase.Practice && game.LocalRosterReady && !ready)
            {ready=true;game.ToggleTutorialReady();}
            if(game.Phase==MinigamePhase.Results&&!loggedFinal)
            {
                loggedFinal=true;
                hands?.Report();
                Debug.Log("ONE_BULLET FINAL alive="+game.Round.AliveCount+" winner="+game.Round.Winner+" shots="+shots+" deaths="+deaths+" pickups="+pickups);
                if(scenario=="disconnect")
                {
                    if(shots!=1||deaths!=2||pickups!=2||game.Round.Find(1)?.Alive!=false||game.Round.Winner!=0)Debug.LogError("ONE_BULLET FAIL disconnect recovery");
                }
                else if(scenario=="timeout")
                {
                    if(shots!=1||deaths!=0||pickups!=2||game.Round.Winner!=0||game.Round.AliveCount!=2)Debug.LogError("ONE_BULLET FAIL timeout ranking");
                }
                else if(shots!=2||deaths!=1||pickups!=2)Debug.LogError("ONE_BULLET FAIL event totals");
            }
            if(Time.realtimeSinceStartup-createdAt>150&&!loggedFinal){Debug.LogError("ONE_BULLET FAIL timeout");Application.Quit(2);}
        }
        private void FixedUpdate()
        {
            if(game==null||game.Phase!=MinigamePhase.Round||!game.LocalRosterReady)return;
            var local=game.Find(game.LocalId);if(local?.Motor==null||local.Dead)return;
            local.Input.EngageAutopilot();local.Input.DriveMove(Vector2.zero);
            double now=NetworkClock.Now, t=now-game.Round.BeginsAt;
            int id=game.LocalId;
            if(hands!=null && hands.Step(t,local)) return;
            // Keep idle test players away from all random weapon locations.
            if((id==0&&t<17)||(id>1&&t<20))
                local.Motor.TeleportTo(new Vector3(-14.4f, .1f, id==0?-19.2f:-14.4f),Quaternion.identity);
            if(game.Round.Pickup>=0 && ((!missed && id==1 && t<17) || (id==0 && t>=17 && !movedToStage)))
            {
                moveTarget=game.PickupPosition;moveUntil=now+.4;
                local.Motor.TeleportTo(moveTarget,Quaternion.identity);
            }
            if(now<moveUntil)local.Motor.TeleportTo(moveTarget,Quaternion.identity);
            if(scenario=="disconnect"&&id==1&&t>13&&game.Round.Holder==1)
            { Debug.Log("ONE_BULLET DISCONNECT holding weapon"); Application.Quit(); return; }
            if(scenario!="disconnect"&&id==1&&!missed&&t>13&&game.Round.Holder==1)
            {missed=true;relay.RequestShot(Vector3.up);}
            // After the second pickup, both peers stand in the same known corridor.
            if(t>20 && (game.Round.Holder==0||movedToStage))
            {
                if(!movedToStage && id==0)
                {
                    var mouse=UnityEngine.InputSystem.Mouse.current;
                    if(mouse==null)mouse=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse,new UnityEngine.InputSystem.LowLevel.MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Right));
                }
                movedToStage=true;
                local.Motor.TeleportTo(new Vector3(-19.2f,0.1f,-19.2f+(id==0?0:1.4f)),Quaternion.identity);
                if(id==0)
                    foreach(var target in game.Participants)
                    {
                        if(target.Player.Id==id || target.Dead || target.Motor==null)continue;
                        Vector3 direction=target.Capsule.bounds.center-OneBulletMinigame.ShotOrigin(local);
                        float yaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
                        float pitch=-Mathf.Atan2(direction.y,new Vector2(direction.x,direction.z).magnitude)*Mathf.Rad2Deg;
                        firstPersonRig.SetView(yaw,pitch);
                        break;
                    }
            }
            if(t>22 && id==0 && !checkedView && game.Round.Holder==0)
            {
                checkedView=true;
                bool valid=firstPersonRig.isActiveAndEnabled && firstPerson.IsAiming && firstPerson.WeaponVisible && firstPerson.AimBlend>.99f;
                Debug.Log("ONE_BULLET ADS valid="+valid);
                if(!valid)Debug.LogError("ONE_BULLET FAIL first-person ADS");
            }
            if(scenario=="timeout"&&id==0&&t>23&&!hit&&game.Round.Holder==0)
            {
                hit=true;var r=game.Round;
                // Accelerate only this opted-in test; exercise the normal deadline path.
                r.ApplyHeader(r.Holder,r.Pickup,r.PreviousPickup,r.BeginsAt,now+1,r.SpawnAt,false,r.Winner);
                relay.Publish();Debug.Log("ONE_BULLET TEST deadline accelerated");
            }
            if(scenario!="timeout"&&id==0&&t>23&&!hit&&game.Round.Holder==0)
            {hit=true;game.HandlePushButton(local.Motor);}
        }
    }
}
