#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Igruha.Core.Arena;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.Infection
{
    /// <summary>Opt-in development scenario. Drives only the local owner's ordinary input, never game results.</summary>
    public sealed class InfectionArtProbe : MonoBehaviour
    {
        private InfectionMinigame game;
        private InfectionState[] states;
        private PlayerController local;
        private DebugPlayerBot bot;
        private ParticleSystem[] particles;
        private SeeThroughShell[] shells;
        private string folder,role;
        private float nextSample,roundStarted;
        private int previousPhase=-1,frames,splashFrames,tubeFrames;
        private double frameTime;
        private bool capturedInfection;
        private float maxHeight;
        private int routeStage = -1;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if(LaunchArguments.TryGetValue("--infection-art-check",out _))SceneManager.sceneLoaded+=Loaded;
        }
        private static void Loaded(Scene scene,LoadSceneMode mode)
        {
            if(scene.name=="Infection"&&LaunchArguments.TryGetValue("--infection-art-check",out var path))
                new GameObject("InfectionArtProbe").AddComponent<InfectionArtProbe>().Initialize(path);
        }
        public void Initialize(string path)
        {
            folder=path;Directory.CreateDirectory(folder);var net=NetworkManager.Singleton;
            role=net!=null&&net.IsListening?(net.IsHost?"host":"client-"+net.LocalClientId):"editor";
            game=FindFirstObjectByType<InfectionMinigame>();particles=FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
            shells=FindObjectsByType<SeeThroughShell>(FindObjectsSortMode.None);
            Write("START original quarantine art probe");
        }
        private void Update()
        {
            if(game==null)return;
            if(local==null)
            {
                states=FindObjectsByType<InfectionState>(FindObjectsSortMode.None);
                foreach(var state in states)
                {
                    var p=state.Avatar;if(p==null)continue;
                    var n=p.GetComponent<NetworkObject>();
                    if(n!=null&&n.IsOwner)local=p;
                }
                if(local!=null)
                {
                    local.GetComponent<PlayerInputReader>().EngageAutopilot();
                    bot=local.gameObject.AddComponent<DebugPlayerBot>();bot.Configure(LayerMask.GetMask("Ground","Cover","PlayerBarrier"));
                    particles=FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);Write("ROSTER="+states.Length);
                }
            }
            if(previousPhase!=(int)game.Phase)
            {
                previousPhase=(int)game.Phase;Write("PHASE="+game.Phase);Capture("phase-"+game.Phase);
                if(game.Phase==MinigamePhase.Round)roundStarted=Time.time;
                if(game.Phase==MinigamePhase.Results)
                {
                    if(bot!=null)bot.Stop();
                    var results=(MinigameResults)typeof(MinigameControllerBase).GetField("results",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(game);
                    Write("RESULTS="+string.Join(",",results.Entries.OrderBy(x=>x.PlayerId).Select(x=>x.PlayerId+":"+x.Place)));
                    Write("LIGHTING pipeline="+UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name+" post="+(Camera.main!=null&&Camera.main.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing)+" maxHeight="+maxHeight.ToString("F2"));
                    Write("PERF meanMs="+(frameTime/Math.Max(1,frames)*1000).ToString("F2")+" frames="+frames+" splashFrames="+splashFrames+" tubeFrames="+tubeFrames);
                }
            }
            if(game.Phase!=MinigamePhase.Round)return;
            frames++;frameTime+=Time.unscaledDeltaTime;
            if(local!=null)maxHeight=Mathf.Max(maxHeight,local.Position.y);
            foreach(var p in particles)if(p!=null&&p.name=="Splash"&&p.particleCount>0){splashFrames++;break;}
            foreach(var shell in shells)if(shell.Occupied){tubeFrames++;break;}
            if(local!=null)
            {
                float elapsed=Time.time-roundStarted;
                int lane=(int)(NetworkManager.Singleton.LocalClientId%4);
                string propName=lane==0?"Tubes/Tube_West":lane==1?"Climber":lane==2?"Swings":"Slide";
                var prop=GameObject.Find("_Arena").transform.Find(propName);
                int stage=elapsed<12?0:elapsed<24?1:elapsed<32?2:3;
                Vector3 point=lane==3 ? (stage==0?new Vector3(0,0,7.2f):stage==1?new Vector3(0,3,1.5f):new Vector3(0,0,-4)) : new Vector3(0,0,stage==0?-4:stage==1?4:0);
                Vector3 target=stage==3?new Vector3(0,.4f,.5f):prop.TransformPoint(point);
                bot.SetTarget(target);
                if(stage!=routeStage){routeStage=stage;Write("ROUTE="+propName+" stage="+stage+" maxHeight="+maxHeight.ToString("F2"));Capture("route-"+stage);}
            }
            if(Time.unscaledTime<nextSample)return;
            nextSample=Time.unscaledTime+2;
            Write("SAMPLE "+string.Join(",",states.OrderBy(x=>x.PlayerId).Select(x=>x.PlayerId+":"+x.Phase))+" local="+(local==null?"none":local.Position.ToString("F2"))+" particles="+particles.Where(x=>x!=null).Sum(x=>x.particleCount));
            if(frames>30&&frames<120)Capture("gameplay");
            if(!capturedInfection&&local!=null&&states.Any(x=>x.Avatar==local&&x.Phase==InfectionPhase.Infected))
            { capturedInfection=true;Capture("infected"); }
        }
        private void Write(string line){if(folder!=null)File.AppendAllText(Path.Combine(folder,role+"-probe.txt"),line+"\n");}
        private void Capture(string label)
        {
            if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)ScreenCapture.CaptureScreenshot(Path.Combine(folder,role+"-"+label+".png"));
        }
        private void OnDestroy(){if(bot!=null)Destroy(bot);Write("END scene unloaded");}
    }
}
#endif
