#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Igruha.Core.Player;
using Igruha.Core.Minigame;
using Igruha.Core.Session;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Opt-in observation only: real camera, replicated scores, visible effects and frame times.</summary>
    public sealed class CarryItemArtProbe : MonoBehaviour
    {
        private CarryItemMinigame game;
        private BottleStack[] stacks;
        private readonly WaterBottle[] watched=new WaterBottle[2];
        private PlayerController[] players;
        private PlayerController local;
        private ParticleSystem[] particles;
        private string folder,role;
        private float nextSample;
        private int phase=-1,frames,lossEvents;
        private double totalTime;
        private bool start,bridge,neck,tank,results;
        private int waterA=-1,waterB=-1;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if(LaunchArguments.TryGetValue("--carry-art-check",out _))SceneManager.sceneLoaded+=Loaded;
        }
        private static void Loaded(Scene scene,LoadSceneMode mode)
        {
            if(scene.name=="CarryItem" && LaunchArguments.TryGetValue("--carry-art-check",out var path))
                new GameObject("CarryItemArtProbe").AddComponent<CarryItemArtProbe>().Initialize(path);
        }
        public void Initialize(string path)
        {
            folder=path;Directory.CreateDirectory(folder);
            var net=NetworkManager.Singleton;
            role=net!=null && net.IsListening?(net.IsHost?"host":"client-"+net.LocalClientId):"editor";
            game=FindFirstObjectByType<CarryItemMinigame>();
            stacks=FindObjectsByType<BottleStack>(FindObjectsSortMode.None);
            particles=FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
            File.WriteAllText(Path.Combine(folder,role+"-probe.txt"),"CarryItem original skyscraper observation\n");
        }
        private void Update()
        {
            if(game==null || folder==null)return;
            if(players==null && game.RosterCount>0)
            {
                players=FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
                foreach(var p in players)
                {
                    var n=p.GetComponent<NetworkObject>();
                    if(n!=null && n.IsOwner)local=p;
                }
                if(local==null && players.Length>0)local=players[0];
                Write("ROSTER="+game.RosterCount+" duration="+game.Definition.RoundDuration+" local="+(local==null?"none":local.name));
            }
            for(int i=0;i<stacks.Length && i<2;i++)
            {
                var b=stacks[i].LiveBottle;
                if(watched[i]==b)continue;
                if(watched[i]!=null)watched[i].WaterSpent-=OnWater;
                watched[i]=b;
                if(b!=null){b.WaterSpent+=OnWater;Write("BOTTLE team="+b.Team+" handles="+b.Carry.HandleCount+" water="+b.Water);}
            }
            if(phase!=(int)game.Phase){phase=(int)game.Phase;Snapshot("PHASE "+game.Phase);}
            if(game.Phase==MinigamePhase.Round)
            {
                frames++;totalTime+=Time.unscaledDeltaTime;
                if(local!=null)
                {
                    float x=local.transform.position.x;
                    if(!start && x<-15){start=true;Capture("01-start");}
                    if(!bridge && x>-12 && x<-7){bridge=true;Capture("02-bridge");}
                    if(!neck && x>-.4f && x<5){neck=true;Capture("03-neck");}
                    if(!tank && x>17){tank=true;Capture("04-tank");}
                }
                if(game.State.TeamA.Water!=waterA || game.State.TeamB.Water!=waterB)
                {
                    waterA=game.State.TeamA.Water;waterB=game.State.TeamB.Water;Snapshot("SCORE");
                }
                if(Time.unscaledTime>nextSample){nextSample=Time.unscaledTime+5;Snapshot("SAMPLE");}
            }
            if(!results && game.Phase==MinigamePhase.Results)
            {
                results=true;Snapshot("RESULTS");Write("PERF frames="+frames+" meanMs="+(totalTime/Math.Max(frames,1)*1000).ToString("F2")+" waterEvents="+lossEvents);Capture("05-results");
            }
        }
        private void OnWater(int amount,WaterLossReason reason)
        {
            lossEvents++;Write("WATER amount="+amount+" reason="+reason);
        }
        private void Snapshot(string label)
        {
            var s=new StringBuilder(label).Append(" A=").Append(game.State.TeamA.Water).Append(" B=").Append(game.State.TeamB.Water)
                .Append(" deliveries=").Append(game.State.TeamA.Deliveries).Append('/').Append(game.State.TeamB.Deliveries);
            if(local!=null)s.Append(" local=").Append(local.transform.position.ToString("F2"));
            int live=0;foreach(var ps in particles)if(ps!=null)live+=ps.particleCount;s.Append(" particles=").Append(live);
            foreach(var b in watched)if(b!=null)s.Append(" bottle=").Append(b.Team).Append(':').Append(b.Water).Append('@').Append(b.transform.position.ToString("F2"));
            Write(s.ToString());
        }
        private void Write(string line){File.AppendAllText(Path.Combine(folder,role+"-probe.txt"),line+"\n");}
        private void Capture(string label)
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,role+"-"+label+".png"));
        }
        private void OnDestroy()
        {
            foreach(var b in watched)if(b!=null)b.WaterSpent-=OnWater;
            if(folder!=null)Write("END scene unloaded");
        }
    }
}
#endif
