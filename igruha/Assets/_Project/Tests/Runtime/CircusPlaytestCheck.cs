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
            float deadline = Time.realtimeSinceStartup + 60;
            while (stage.Stage != 2 && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSeconds(.5f);
            if (!local.enabled || local.MovementLocked || input.Suspended || !input.enabled)
                fail("circus local movement disabled before failure");
            if (!local.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.enabled && !r.forceRenderingOff))
                fail("circus own character hidden");
            Vector3 before = local.Position;
            input.DriveMove(Vector2.right); yield return new WaitForSeconds(.3f); input.DriveMove(Vector2.zero);
            if (Vector3.Distance(before,local.Position)<.08f) fail("circus input did not move local player");
            Capture("character");
            // CaptureScreenshot renders at the end of the frame. Do not switch
            // the host to the hatch camera before this character frame is written.
            yield return new WaitForSeconds(.4f);
            var bear = UnityEngine.Object.FindFirstObjectByType<PitBear>();
            var victims = players.Take(2).Select(p=>p.Avatar).ToArray();
            var cages = victims.Select(v=>UnityEngine.Object.FindObjectsByType<CageStation>(FindObjectsSortMode.None).Single(c=>c.Occupant==v)).ToArray();
            bool[] fell = new bool[2], caught = new bool[2], opened = new bool[2];
            bool floorCaptured=false, fallCaptured=false;
            if (NetworkManager.Singleton.IsServer)
            {
                var contestants = (IList)Get(game,"contestants");
                foreach (var contestant in contestants)
                {
                    var session=(SessionPlayer)contestant.GetType().GetField("Session").GetValue(contestant);
                    if (game is CansOrderMinigame)
                    {
                        var entry=(CansOrderEntry)contestant.GetType().GetField("Entry").GetValue(contestant);
                        entry.Solved=!victims.Contains(session.Avatar);
                        Set(contestant,"Entry",entry);
                    }
                    else Set(contestant,"Errors",victims.Contains(session.Avatar)?(int)Get(game,"errorLimit"):0);
                }
                var method=game.GetType().GetMethod("EnterHatch",Private);
                method.Invoke(game,game is CansOrderMinigame ? new object[]{false}:null);
            }
            Vector3 bearBefore=bear.transform.position; float maxSpeed=0;
            while (Time.realtimeSinceStartup<deadline && !caught.All(v=>v))
            {
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
        private static void Capture(string suffix)
        {
            if(!LaunchArguments.TryGetValue("--playtest-screenshots",out string path) ||
                SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
            ScreenCapture.CaptureScreenshot(path+"-"+suffix+".png");
        }
    }
}
