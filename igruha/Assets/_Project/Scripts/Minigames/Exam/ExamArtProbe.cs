#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using TMPro;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.Exam
{
    /// <summary>Opt-in development capture/observation. Never changes game state or reads the secret answer.</summary>
    public sealed class ExamArtProbe : MonoBehaviour
    {
        private ExamMinigame game;
        private MinigameStageState stage;
        private ExamAnswerPlatform a, b;
        private PlayerController[] players;
        private TMP_Text question, optionA, optionB;
        private string folder;
        private int lastStage = -1;
        private int lastQuestion = -1;
        private bool captured, results;
        private float minY;
        private double sumFrames;
        private int frames;
        private float sampleAt;
        private string role;
        private int openingFrame;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (LaunchArguments.TryGetValue("--exam-art-check", out _)) SceneManager.sceneLoaded += Loaded;
        }
        private static void Loaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Exam") return;
            if (LaunchArguments.TryGetValue("--exam-art-check", out var path))
                new GameObject("ExamArtProbe").AddComponent<ExamArtProbe>().Initialize(path);
        }
        public void Initialize(string path)
        {
            folder=path; Directory.CreateDirectory(folder);
            role=NetworkManager.Singleton!=null && NetworkManager.Singleton.IsListening
                ? (NetworkManager.Singleton.IsHost?"host":"client") : "editor";
            game=FindFirstObjectByType<ExamMinigame>();stage=game.GetComponent<MinigameStageState>();
            a=GameObject.Find("Platform_A").GetComponent<ExamAnswerPlatform>();
            b=GameObject.Find("Platform_B").GetComponent<ExamAnswerPlatform>();
            var board=GameObject.Find("BoardCanvas").transform;
            question=board.Find("Text_Question").GetComponent<TMP_Text>();
            optionA=board.Find("Text_OptionA").GetComponent<TMP_Text>();
            optionB=board.Find("Text_OptionB").GetComponent<TMP_Text>();
            File.WriteAllText(Path.Combine(folder,role+"-probe.txt"),"Exam original hall observation\n");
        }
        private void Update()
        {
            if (game==null || stage==null || folder==null) return;
            if(players==null && game.ContestantCount>0) players=FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            if (lastStage!=stage.Stage || lastQuestion!=stage.Subround)
            {
                if(frames>0) Write("frame-sample stage="+lastStage+" n="+frames+" meanMs="+(sumFrames/frames*1000).ToString("F2"));
                lastStage=stage.Stage;lastQuestion=stage.Subround;captured=false;openingFrame=0;sumFrames=0;frames=0;minY=0;
                Snapshot("transition");
            }
            if (stage.Stage==3 && stage.StageRemaining<stage.StageDuration-1)
            {sumFrames+=Time.unscaledDeltaTime;frames++;}
            if(players!=null)foreach(var p in players)if(p!=null)minY=Mathf.Min(minY,p.transform.position.y);
            float elapsed=stage.StageDuration-stage.StageRemaining;
            if(!captured && stage.Stage>0 && elapsed> (stage.Stage==5?1.1f:1.0f))
            {
                captured=true;Snapshot("settled minY="+minY.ToString("F2"));
                if(lastQuestion<=3 && (stage.Stage==1 || stage.Stage==2 || stage.Stage==3 || stage.Stage==5 || stage.Stage==6))
                    Capture(role+"-q"+lastQuestion+"-stage"+stage.Stage);
            }
            if(stage.Stage==5 && lastQuestion<=3 && openingFrame<5 && elapsed>=.10f+openingFrame*.10f)
            {
                var mech=(a.DoorsOpen?a:b).GetComponent<ExamHatchMechanism>();
                Write("mechanism q="+lastQuestion+" elapsed="+elapsed.ToString("F3")+" core="+Mathf.Abs(Mathf.DeltaAngle(0,mech.LeftPivot.localEulerAngles.z)).ToString("F1")+
                    " visual="+Mathf.Abs(Mathf.DeltaAngle(0,(mech.LeftPivot.localRotation*mech.LeftVisual.localRotation).eulerAngles.z)).ToString("F1")+
                    " papers="+mech.Papers.particleCount+" steam="+mech.Steam.particleCount);
                Capture(role+"-q"+lastQuestion+"-opening-"+openingFrame);openingFrame++;
            }
            if(stage.Stage==5 && Time.unscaledTime>sampleAt)
            {sampleAt=Time.unscaledTime+.5f;Snapshot("fall minY="+minY.ToString("F2"));}
            if(!results && game.Phase==MinigamePhase.Results)
            {results=true;Snapshot("RESULTS");Capture(role+"-results");}
        }
        private void Snapshot(string label)
        {
            var s=new StringBuilder(label).Append(" q=").Append(stage.Subround).Append(" stage=").Append(stage.Stage)
                .Append(" Aopen=").Append(a.DoorsOpen).Append(" Bopen=").Append(b.DoorsOpen)
                .Append(" board=").Append(question.text.Replace('\n',' ')).Append(" | ").Append(optionA.text).Append(" | ").Append(optionB.text);
            if (stage.Stage==1 && (!string.IsNullOrEmpty(optionA.text)||!string.IsNullOrEmpty(optionB.text)))s.Append(" ERROR_EARLY_OPTIONS");
            if(players!=null)foreach(var p in players)if(p!=null)s.Append("\n  ").Append(p.name).Append(" pos=").Append(p.transform.position.ToString("F2"));
            var c=Camera.main;if(c!=null)s.Append("\n  camera=").Append(c.transform.position.ToString("F2")).Append(" rot=").Append(c.transform.eulerAngles.ToString("F2")).Append(" fov=").Append(c.fieldOfView);
            Write(s.ToString());
        }
        private void Write(string value) { File.AppendAllText(Path.Combine(folder,role+"-probe.txt"),value+"\n"); Debug.Log("[ExamArtProbe] "+value); }
        private void Capture(string name)
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
            var camera=Camera.main;if(camera==null)return;
            var previous=camera.targetTexture;var active=RenderTexture.active;float aspect=camera.aspect;
            var rt=RenderTexture.GetTemporary(1600,900,24,RenderTextureFormat.ARGB32);
            var tex=new Texture2D(1600,900,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=rt;camera.aspect=16f/9f;camera.Render();RenderTexture.active=rt;
                tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),tex.EncodeToPNG());
            }
            finally{camera.targetTexture=previous;camera.aspect=aspect;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);Destroy(tex);}
        }
    }
}
#endif
