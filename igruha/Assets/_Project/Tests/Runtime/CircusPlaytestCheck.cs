using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.Circus;
using Igruha.Minigames.CansOrder;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    internal static class CircusPlaytestCheck
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static object Get(object obj,string field) => obj.GetType().GetField(field,Private).GetValue(obj);
        private static void Set(object obj,string field,object value) => obj.GetType().GetField(field).SetValue(obj,value);
        public static IEnumerator Run(MinigameControllerBase game, Action<string> fail)
        {
            foreach (var bot in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (bot.GetType().Name.Contains("DebugBot")) bot.enabled = false;
            var players = SessionScoreboard.Current.Players.OrderBy(p=>p.Id).ToArray();
            var local = SessionScoreboard.Current.LocalPlayer.Avatar;
            var input = local.GetComponent<PlayerInputReader>();
            input.EngageAutopilot(); input.DriveMove(Vector2.zero);
            var stage = game.GetComponent<MinigameStageState>();
            float deadline = Time.realtimeSinceStartup + 155;
            while (stage.Stage != 2 && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSeconds(.5f);
            bool cansPlacement=game is CansOrderMinigame;
            if (!local.enabled || local.MovementLocked != cansPlacement || input.Suspended || !input.enabled)
                fail("circus local movement lock differs from placement rules");
            if (!local.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.enabled && !r.forceRenderingOff))
                fail("circus own character hidden");
            Vector3 before = local.Position;
            input.DriveMove(Vector2.right); yield return new WaitForSeconds(.3f); input.DriveMove(Vector2.zero);
            float moved=Vector3.Distance(before,local.Position);
            if(cansPlacement ? moved>.04f : moved<.08f)fail("circus placement movement differs from rules: "+moved);
            Capture("character");
            // CaptureScreenshot renders at the end of the frame. Do not switch
            // the host to the hatch camera before this character frame is written.
            yield return new WaitForSeconds(.4f);
            var bear = UnityEngine.Object.FindFirstObjectByType<PitBear>();
            var victims = players.Take(2).Select(p=>p.Avatar).ToArray();
            var cages = victims.Select(v=>UnityEngine.Object.FindObjectsByType<CageStation>(FindObjectsSortMode.None).Single(c=>c.Occupant==v)).ToArray();
            bool[] fell = new bool[2], caught = new bool[2], opened = new bool[2];
            bool floorCaptured=false, fallCaptured=false;
            if(game is CansOrderMinigame cans)
                yield return CheckCanTurns(cans,local,victims,fail);
            else if (NetworkManager.Singleton.IsServer)
            {
                var contestants = (IList)Get(game,"contestants");
                foreach(var contestant in contestants)
                {
                    var session=(SessionPlayer)contestant.GetType().GetField("Session").GetValue(contestant);
                    bool victim= victims.Contains(session.Avatar);
                    Set(contestant,"Errors",victim?(int)Get(game,"errorLimit"):0);
                    Set(contestant,"FaultedThisSubround",victim);
                }
                game.GetType().GetMethod("EnterDescend",Private).Invoke(game,null);
            }
            Vector3 bearBefore=bear.transform.position; float maxSpeed=0;
            float liftMin=float.MaxValue,liftMax=float.MinValue;
            var localCage=UnityEngine.Object.FindObjectsByType<CageStation>(FindObjectsSortMode.None).FirstOrDefault(c=>c.Occupant==local);
            while (Time.realtimeSinceStartup<deadline && !caught.All(v=>v))
            {
                if(localCage!=null && localCage.Descending && !localCage.DoorsOpen)
                {
                    float offset=local.transform.position.y-localCage.transform.position.y;
                    liftMin=Mathf.Min(liftMin,offset);liftMax=Mathf.Max(liftMax,offset);
                }
                for(int i=0;i<2;i++)
                {
                    if(cages[i]==null || victims[i]==null) continue;
                    bool doors=cages[i].DoorsOpen;
                    if(doors)
                    {
                        var door=cages[i].transform.Find("Floor/DoorLeft");
                        opened[i] |= Quaternion.Angle(Quaternion.identity,door.localRotation)>80 &&
                            !door.GetComponentInChildren<Collider>().enabled;
                        if(!floorCaptured){Capture("hatch");floorCaptured=true;}
                    }
                    var pose=victims[i].GetComponent<CircusFallPose>();
                    fell[i] |= pose!=null && pose.IsPresenting && victims[i].Position.y<cages[i].transform.position.y-.3f;
                    if(fell[i] && !fallCaptured){Capture("fall");fallCaptured=true;}
                    var knockout=victims[i].GetComponent<CircusKnockout>();
                    caught[i] |= knockout!=null && knockout.IsEliminated;
                }
                if(game is CansOrderMinigame cansGame && NetworkManager.Singleton.IsServer && fell.All(v=>v) && cansGame.Stage==3)
                    cansGame.GetType().GetMethod("FinishPuzzle",Private).Invoke(cansGame,null);
                float speed=Vector3.Distance(bearBefore,bear.transform.position)/Mathf.Max(.001f,Time.deltaTime);
                if(bear.State==PitBear.BearState.Chase)maxSpeed=Mathf.Max(maxSpeed,speed);
                bearBefore=bear.transform.position;
                // The second victim actively flees around the pit using the normal motor.
                if(local==victims[1] && local.Position.y<.25f && !caught[1])
                {
                    var p=local.Position;
                    var tangent=new Vector3(p.z,0,-p.x).normalized;
                    local.SetCameraReference(null);
                    input.DriveMove(new Vector2(tangent.x,tangent.z));
                }
                yield return null;
            }
            input.DriveMove(Vector2.zero);
            if(liftMax>=liftMin)
            {
                Debug.Log("PLAYTEST_CHECK lift relativeOffsetRange="+(liftMax-liftMin));
                if(liftMax-liftMin>.05f)fail("lift passenger jitters on descent/ascent");
            }
            if(!opened.All(v=>v))fail("trapdoor collision/animation incomplete");
            if(!fell.All(v=>v))fail("airborne pose not seen on both victims");
            if(!caught.All(v=>v))fail("bear did not eliminate stationary and fleeing players within timeout");
            Debug.Log("PLAYTEST_CHECK circus floors="+string.Join(",",opened)+" fall="+string.Join(",",fell)+
                " caught="+string.Join(",",caught)+" chasePeak="+maxSpeed.ToString("F2"));
            Capture("bear");
            if(NetworkManager.Singleton.IsServer && game!=null && game.Phase==MinigamePhase.Round)game.EndMinigame();
            float returnDeadline=Time.realtimeSinceStartup+25;
            while(Time.realtimeSinceStartup<returnDeadline && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="Hub")yield return null;
            yield return new WaitForSeconds(1);
            local=SessionScoreboard.Current.LocalPlayer.Avatar;
            if(local==null || !local.enabled || local.MovementLocked ||
                !local.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.enabled))fail("circus left invisible/locked avatar after returning to hub");
        }
        private static IEnumerator CheckCanTurns(CansOrderMinigame cans,PlayerController local,PlayerController[] victims,Action<string> fail)
        {
            int count=cans.Round.CanCount;
            var solution=Enumerable.Range(0,count).ToList();
            if(NetworkManager.Singleton.IsServer)
            {
                var secret=(System.Collections.Generic.List<int>)Get(cans,"solution");
                secret.Clear();secret.AddRange(solution);
            }
            var mine=((IList)Get(cans,"contestants")).Cast<object>().Single(c=>((SessionPlayer)c.GetType().GetField("Session").GetValue(c)).Avatar==local);
            var shelf=(CanShelf)mine.GetType().GetField("Shelf").GetValue(mine);
            var button=(CanConfirmButton)mine.GetType().GetField("Button").GetValue(mine);
            var cage=(CageStation)mine.GetType().GetField("Cage").GetValue(mine);
            var stage=cans.GetComponent<MinigameStageState>();
            int sent=0,checkedCircle=0;
            float limit=Time.realtimeSinceStartup+85,minOffset=float.MaxValue,maxOffset=float.MinValue;
            bool victim=victims.Contains(local);
            while(cans!=null && Time.realtimeSinceStartup<limit)
            {
                if(cage.Descending && victim && cans.Stage==3)
                {
                    float offset=local.transform.position.y-cage.transform.position.y;
                    minOffset=Mathf.Min(minOffset,offset);maxOffset=Mathf.Max(maxOffset,offset);
                }
                if(cans.Stage==2 && sent!=cans.Round.Circle && stage.StageDuration-stage.StageRemaining>.9f)
                {
                    var order=Enumerable.Range(0,count).Select(i=>(i+1)%count).ToList();
                    if(!victim){order=solution.ToList();int t=order[0];order[0]=order[1];order[1]=t;}
                    shelf.SetArrangement(order);button.Interact(local);sent=cans.Round.Circle;
                }
                if(cans.Stage==3 && checkedCircle!=cans.Round.Circle && stage.StageDuration-stage.StageRemaining>.35f)
                {
                    int circle=cans.Round.Circle;checkedCircle=circle;
                    if(Mathf.Abs(cans.GetComponent<RoundTimer>().Remaining-cans.PuzzleSecondsLeft)>.15f)
                        fail("cans shared clock differs from puzzle deadline");
                    var entry=(CansOrderEntry)mine.GetType().GetField("Entry").GetValue(mine);
                    float expected=victim?Mathf.Max(0,1-circle/3f):1;
                    int chances=victim?Mathf.Clamp(5-circle,0,2):2;
                    if(Mathf.Abs(entry.HeightFraction-expected)>.001f || entry.BottomChances!=chances || entry.Alive!=(circle<5 || !victim))
                        fail("cans descent/chances circle="+circle+" height="+entry.HeightFraction+" chances="+entry.BottomChances+" alive="+entry.Alive);
                    if(circle==3 || circle==4)Capture("chances-"+circle);
                    Debug.Log("PLAYTEST_CHECK cans circle="+circle+" height="+entry.HeightFraction+" chances="+entry.BottomChances);
                    if(circle==5)
                    {
                        if(maxOffset-minOffset>.04f)fail("cage passenger jitter="+(maxOffset-minOffset));
                        if(victim)Debug.Log("PLAYTEST_CHECK cage relativeOffsetRange="+(maxOffset-minOffset));
                        yield break;
                    }
                }
                yield return null;
            }
            fail("five cans turns did not complete");
        }

        private static void Capture(string suffix)
        {
            if(!LaunchArguments.TryGetValue("--playtest-screenshots",out string path) ||
                SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
            ScreenCapture.CaptureScreenshot(path+"-"+suffix+".png");
        }
    }
}
