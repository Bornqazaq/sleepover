using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Igruha.Core.Minigame;
using Igruha.Core.CameraSystems;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.Circus;
using Igruha.Minigames.CansOrder;
using Igruha.Minigames.Stopwatch;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

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
            using var barrier = new ProbeBarrier(players.Length,fail);
            var local = SessionScoreboard.Current.LocalPlayer.Avatar;
            var input = local.GetComponent<PlayerInputReader>();
            input.EngageAutopilot(); input.DriveMove(Vector2.zero);
            using var travelProbe = new CageTravelProbe(local, input, fail);
            using var actionProbe = new ActionRestrictionProbe(local, input, fail);
            using var mascotProbe = new MascotProbe(game, local, fail);
            var stage = game.GetComponent<MinigameStageState>();
            float deadline = Time.realtimeSinceStartup + 155;
            if(game is CansOrderMinigame) yield return CheckInitialShelfRush(local,input,stage,fail);
            while (stage.Stage != 2 && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSeconds(.5f);
            bool cansPlacement=game is CansOrderMinigame;
            if (!local.enabled || !local.MovementLocked || input.Suspended || !input.enabled)
                fail("circus local movement lock differs from placement rules");
            if (!local.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.enabled && !r.forceRenderingOff))
                fail("circus own character hidden");
            using var shelfProbe = cansPlacement ? new ShelfRenderProbe((CansOrderMinigame)game, local, players, fail) : null;
            Vector3 before = local.Position;
            input.DriveMove(Vector2.right); yield return new WaitForSeconds(.3f); input.DriveMove(Vector2.zero);
            float moved=Vector3.Distance(before,local.Position);
            if(moved>.04f)fail("circus close-up movement is not held: "+moved);
            yield return barrier.Wait(0);
            if (shelfProbe != null) yield return CheckShelfOcclusion((CansOrderMinigame)game, shelfProbe, barrier, fail);
            if(game is StopwatchMinigame stopwatch) yield return CheckButtonHands(stopwatch,local,input,players,barrier,fail);
            var roundHud=UnityEngine.Object.FindFirstObjectByType<Igruha.Core.UI.RoundHud>();
            var clock=roundHud!=null?Get(roundHud,"timerText") as TMPro.TMP_Text:null;
            bool clockVisible=clock!=null && clock.isActiveAndEnabled;
            if(!cansPlacement && clockVisible)fail("Stopwatch exposes the round clock while measuring");
            Debug.Log("PLAYTEST_CHECK interface roundClockVisible="+clockVisible+" cans="+cansPlacement);
            Capture("character");
            // CaptureScreenshot renders at the end of the frame. Do not switch
            // the host to the hatch camera before this character frame is written.
            yield return new WaitForSeconds(.4f);
            var bears = UnityEngine.Object.FindObjectsByType<PitBear>(FindObjectsSortMode.None).OrderBy(b=>b.name).ToArray();
            if(bears.Length!=2){fail("circus must have two bears");yield break;}
            if(bears.Select(b=>b.GetComponent<NetworkObject>().NetworkObjectId).Distinct().Count()!=2)
                fail("bears do not have independent network identities");
            // Give this opt-in observation its own time in the current stage;
            // otherwise the normal round timer can open hatches before the fall probe starts.
            if (NetworkManager.Singleton.IsServer) stage.EnterStage(stage.Stage, stage.StageRemaining + 21);
            yield return CheckCageAttention(bears, players, fail);
            var bear = bears[0];
            using var bearProbe = new BearPresentationProbe(game,bear,local,fail);
            using var secondProbe = new BearPresentationProbe(game,bears[1],local,fail,false);
            Vector3 secondBefore=bears[1].transform.position;
            float secondTravel=0,minimumSeparation=float.MaxValue;
            bool secondChased=false;
            var victims = players.Take(2).Select(p=>p.Avatar).ToArray();
            var cages = victims.Select(v=>UnityEngine.Object.FindObjectsByType<CageStation>(FindObjectsSortMode.None).Single(c=>c.Occupant==v)).ToArray();
            bool[] fell = new bool[2], caught = new bool[2], opened = new bool[2];
            var cageHeights=cages.Select(c=>c.transform.position.y).ToArray();
            float maximumCageRise=0;
            bool floorCaptured=false, fallCaptured=false;
            if(game is CansOrderMinigame cans)
                yield return CheckCanTurns(cans,local,victims,travelProbe,fail);
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
            float nextPursuitReport=Time.realtimeSinceStartup+3;
            float liftMin=float.MaxValue,liftMax=float.MinValue;
            var localCage=UnityEngine.Object.FindObjectsByType<CageStation>(FindObjectsSortMode.None).FirstOrDefault(c=>c.Occupant==local);
            while (Time.realtimeSinceStartup<deadline && !caught.All(v=>v))
            {
                actionProbe.Sample(game.Phase == MinigamePhase.Round);
                travelProbe.Sample();
                bearProbe.Sample();secondProbe.Sample();
                if(bears[0]!=null && bears[1]!=null)
                {
                    secondTravel+=Vector3.Distance(secondBefore,bears[1].transform.position);
                    secondBefore=bears[1].transform.position;
                    secondChased|=bears[1].State==PitBear.BearState.Chase;
                    minimumSeparation=Mathf.Min(minimumSeparation,Vector3.Distance(bears[0].transform.position,bears[1].transform.position));
                }
                if(Time.realtimeSinceStartup>=nextPursuitReport && bear.Target!=null)
                {
                    nextPursuitReport=Time.realtimeSinceStartup+3;
                    Debug.Log("PLAYTEST_CHECK pursuit state="+bear.State+" bear="+bear.transform.position.ToString("F2")+
                        " yaw="+bear.transform.eulerAngles.y.ToString("F1")+" target="+bear.Target.Position.ToString("F2")+
                        " targetTransform="+bear.Target.transform.position.ToString("F2"));
                }
                if(localCage!=null && localCage.Descending && !localCage.DoorsOpen)
                {
                    float offset=local.transform.position.y-localCage.transform.position.y;
                    liftMin=Mathf.Min(liftMin,offset);liftMax=Mathf.Max(liftMax,offset);
                }
                for(int i=0;i<2;i++)
                {
                    if(cages[i]==null || victims[i]==null) continue;
                    float cageY=cages[i].transform.position.y;
                    maximumCageRise=Mathf.Max(maximumCageRise,cageY-cageHeights[i]);
                    cageHeights[i]=cageY;
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
            // Keep the last impact alive long enough to observe its complete
            // two-shot, collapse and spectator handoff before requesting Results.
            float presentationDeadline=Time.realtimeSinceStartup+CircusKnockout.PresentationSeconds+.6f;
            while(game!=null && game.Phase==MinigamePhase.Round && Time.realtimeSinceStartup<presentationDeadline)
            {
                bearProbe.Sample();secondProbe.Sample();
                if(bears[0]!=null && bears[1]!=null)
                {
                    secondTravel+=Vector3.Distance(secondBefore,bears[1].transform.position);
                    secondBefore=bears[1].transform.position;
                    secondChased|=bears[1].State==PitBear.BearState.Chase;
                    minimumSeparation=Mathf.Min(minimumSeparation,Vector3.Distance(bears[0].transform.position,bears[1].transform.position));
                }
                yield return null;
            }
            if(liftMax>=liftMin)
            {
                Debug.Log("PLAYTEST_CHECK lift relativeOffsetRange="+(liftMax-liftMin));
                if(liftMax-liftMin>.05f)fail("walking passenger loses the moving cage floor");
            }
            if(!opened.All(v=>v))fail("trapdoor collision/animation incomplete");
            if(maximumCageRise>.03f)fail("elimination raised a cage instead of opening its floor: "+maximumCageRise);
            Debug.Log("PLAYTEST_CHECK hatch maximumUpwardStep="+maximumCageRise+" finalHeights="+string.Join(",",cageHeights));
            if(!fell.All(v=>v))fail("airborne pose not seen on both victims");
            if(!caught.All(v=>v))fail("bear did not eliminate stationary and fleeing players within timeout");
            Debug.Log("PLAYTEST_CHECK circus floors="+string.Join(",",opened)+" fall="+string.Join(",",fell)+
                " caught="+string.Join(",",caught)+" chasePeak="+maxSpeed.ToString("F2"));
            if(!secondChased || secondTravel<2f)fail("second bear did not independently pursue players");
            if(NetworkManager.Singleton.IsServer && minimumSeparation<PitBear.CompanionClearance-.02f)
                fail("bear bodies crossed each other");
            Debug.Log("PLAYTEST_CHECK bear pair secondChased="+secondChased+" travel="+secondTravel.ToString("F2")+" separation="+minimumSeparation.ToString("F3"));
            Capture("bear");
            if(NetworkManager.Singleton.IsServer && game!=null && game.Phase==MinigamePhase.Round)game.EndMinigame();
            float returnDeadline=Time.realtimeSinceStartup+25;
            bool resultsSeen=false, resultsInputBlocked=true;
            while(Time.realtimeSinceStartup<returnDeadline && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="Hub")
            {
                if(game!=null && game.Phase==MinigamePhase.Results)
                {
                    resultsSeen=true;
                    if(input!=null && input.enabled && resultsInputBlocked)
                    {
                        resultsInputBlocked=false;
                        fail("circus cleanup re-enabled local input during Results");
                    }
                }
                yield return null;
            }
            if(!resultsSeen)fail("circus Results input lock was not observed before returning to hub");
            Debug.Log("PLAYTEST_CHECK circus Results observed="+resultsSeen+" inputBlocked="+resultsInputBlocked);
            yield return new WaitForSeconds(1);
            local=SessionScoreboard.Current.LocalPlayer.Avatar;
            if(local==null || !local.enabled || local.MovementLocked ||
                !local.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.enabled && !r.forceRenderingOff))fail("circus left invisible/locked avatar after returning to hub");
            if(local!=null && local.TryGetComponent(out PlayerInputReader hubInput) && hubInput.LocallyControlled && !hubInput.enabled)
                fail("circus Results input lock survived return to hub");
            shelfProbe?.CheckFinished();
            bearProbe.CheckFinished();secondProbe.CheckFinished();
            travelProbe.CheckFinished(victims.Contains(SessionScoreboard.Current.LocalPlayer.Avatar));
            mascotProbe.CheckFinished();
            actionProbe.CheckFinished();
        }

        private sealed class ActionRestrictionProbe : IDisposable
        {
            private readonly PlayerController player;
            private readonly PlayerInputReader input;
            private readonly PlayerPushAbility push;
            private readonly Action<string> fail;
            private int groundedAttempts, jumps, punches;
            public ActionRestrictionProbe(PlayerController player, PlayerInputReader input, Action<string> fail)
            {
                this.player=player; this.input=input; this.fail=fail;
                push=player.GetComponent<PlayerPushAbility>();
                player.Jumped+=OnJump; push.PunchStarted+=OnPunch;
                if(!player.JumpInputSuppressed || !push.InputSuppressed) fail("circus actions were not restricted at setup");
            }
            private void OnJump() { jumps++; fail("jump fired during circus"); }
            private void OnPunch() { punches++; fail("punch fired during circus"); }
            public void Sample(bool inRound)
            {
                if(!inRound || player==null || player.MovementLocked || player.IsKnockedDown) return;
                if(!player.JumpInputSuppressed || !push.InputSuppressed) fail("circus action restriction disappeared in the pit");
                if(player.IsGrounded) groundedAttempts++;
                input.DriveJump(); push.RequestPush();
            }
            public void CheckFinished()
            {
                if(player!=null && (player.JumpInputSuppressed || push.InputSuppressed)) fail("circus action restriction survived return to Hub");
                Debug.Log("PLAYTEST_CHECK circus actions groundedAttempts="+groundedAttempts+" jumps="+jumps+" punches="+punches+" restored=True");
            }
            public void Dispose()
            { if(player!=null)player.Jumped-=OnJump; if(push!=null)push.PunchStarted-=OnPunch; }
        }

        private sealed class MascotProbe : IDisposable
        {
            private readonly MinigameControllerBase game;
            private readonly PlayerController local;
            private readonly Action<string> fail;
            private readonly CageStation[] cages;
            private readonly CircusCageMascot[] reactions;
            private readonly bool[] wasPresenting;
            private int completedLaughs;
            private float shortestPresentation = float.MaxValue;
            private readonly CircusMascotProbeSampler sampling;
            private int events, frames, hotFrames, steamFrames, screenshots;
            private int motionFrame, fullLengthFrames, exitCloudFrames;
            private CircusCageMascot motionReaction;
            private double motionStartedAt;
            private float latestAudioTime;
            private float nextMotion;
            private readonly bool captureMotion = LaunchArguments.HasFlag("--circus-motion-review");
            private float blinkMin = 100, blinkMax, worstAim = 1, smileMin = 100, smileMax;
            private bool failed;

            public MascotProbe(MinigameControllerBase game, PlayerController local, Action<string> fail)
            {
                this.game = game; this.local = local; this.fail = fail;
                cages = UnityEngine.Object.FindObjectsByType<CageStation>(FindObjectsSortMode.None);
                reactions = cages.Select(c => c.GetComponent<CircusCageMascot>()).ToArray();
                wasPresenting = new bool[reactions.Length];
                if (reactions.Any(r => r == null)) Fail("circus cage is missing mascot presentation");
                foreach (var cage in cages) cage.DescentStarted += OnDescent;
                sampling = game.gameObject.AddComponent<CircusMascotProbeSampler>();
                sampling.Tick = Sample;
            }

            private void OnDescent(CageStation cage, double time, float duration)
            {
                events++;
                var reaction = cage.GetComponent<CircusCageMascot>();
                if (reaction == null || !reaction.IsPresenting || reaction.Target != cage.Occupant || Math.Abs(reaction.StartedAt - time) > .001)
                    Fail("mascot did not follow the replicated cage descent and its actual occupant");
                Debug.Log("PLAYTEST_CHECK mascot begin cage=" + cage.name + " owner=" + cage.Occupant.name + " start=" + time.ToString("F3"));
            }

            private void Sample()
            {
                if (game != null)
                {
                    for (int i = 0; i < reactions.Length; i++)
                    {
                        var reaction = reactions[i]; if (reaction == null) continue;
                        if (motionReaction == null && reaction.Target == local) { motionReaction = reaction; motionStartedAt = reaction.StartedAt; }
                        if (captureMotion && reaction == motionReaction && Math.Abs(reaction.StartedAt - motionStartedAt) < .001 && reaction.Actor != null && reaction.Actor.gameObject.activeSelf && motionFrame < 72 && Time.time >= nextMotion)
                        {
                            nextMotion = Time.time + 1f / 12f;
                            string frame = "mascot-motion-" + (motionFrame++).ToString("D4");
                            Capture(frame);
                            if (LaunchArguments.TryGetValue("--playtest-screenshots", out string path))
                                System.IO.File.AppendAllText(path + "-mascot-motion.csv", frame + "," + reaction.Elapsed.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "," + reaction.IsPresenting + "\n");
                        }
                        if (!reaction.IsPresenting)
                        {
                            if (wasPresenting[i])
                            {
                                wasPresenting[i] = false;
                                shortestPresentation = Mathf.Min(shortestPresentation, reaction.Elapsed);
                                if (reaction.Elapsed < CircusCageMascot.ShowSeconds - .02f)
                                    Fail("mascot ended early at " + reaction.Elapsed.ToString("F3") + " seconds");
                                else completedLaughs++;
                            }
                            if (reaction.Actor != null && !reaction.Actor.IsVisible && reaction.Actor.PuffParticleCount > 0) exitCloudFrames++;
                            if (reaction.Anger != null && (reaction.Anger.Heat > 0 || reaction.Anger.OverlayCount > 0 || reaction.Anger.ParticleCount > 0))
                                Fail("anger survived the end of its mascot reaction");
                            continue;
                        }
                        wasPresenting[i] = true;
                        frames++;
                        if (reaction.Target != cages[i].Occupant || cages[i].DoorsOpen) Fail("mascot mocked a released occupant");
                        if (reaction.Actor == null || reaction.Anger == null) { Fail("mascot presentation has missing actors"); continue; }
                        if (!reaction.Actor.IsVisible || Mathf.Abs(reaction.Actor.transform.Find("Model").localScale.x - .42f) > .001f)
                            Fail("mascot grew or shrank instead of popping at full size");
                        var laugh = reaction.Actor.GetComponent<AudioSource>();
                        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                        {
                            latestAudioTime = Mathf.Max(latestAudioTime, laugh.time);
                            if (reaction.Elapsed > 4.6f && reaction.Elapsed < 4.85f && !laugh.isPlaying)
                                Fail("mascot laugh ended before the supplied recording finished");
                        }
                        if (reaction.Elapsed > 4.9f) fullLengthFrames++;
                        blinkMin = Mathf.Min(blinkMin, reaction.Actor.BlinkWeight); blinkMax = Mathf.Max(blinkMax, reaction.Actor.BlinkWeight);
                        smileMin = Mathf.Min(smileMin, reaction.Actor.SmileWeight); smileMax = Mathf.Max(smileMax, reaction.Actor.SmileWeight);
                        Vector3 anchor = cages[i].transform.InverseTransformPoint(reaction.Actor.transform.position);
                        if (Mathf.Abs(anchor.x) > .05f || anchor.z < 1.65f) Fail("mascot is not in front of the open cage window");
                        if (reaction.Elapsed > 1.2f && reaction.Elapsed < 2.9f)
                        {
                            if (reaction.Anger.Heat > .9f) hotFrames++;
                            if (reaction.Anger.ParticleCount > 0) steamFrames++;
                            if (reaction.Anger.OverlayCount == 0) Fail("loser has no head tint skin");
                            if (reaction.Anger.SteamSourceCount != 3) Fail("anger is missing ear or nose steam emitters");
                            float dot = Vector3.Dot(reaction.Actor.PointDirection, (reaction.Anger.HeadPosition - reaction.Actor.PointOrigin).normalized);
                            worstAim = Mathf.Min(worstAim, dot);
                            if (dot < .8f) Fail("mascot finger lost its moving target: " + dot);
                            if (reaction.Target == local && screenshots < 2)
                            {
                                Capture("mascot-" + screenshots++);
                            }
                        }
                    }
                }
            }

            public void CheckFinished()
            {
                bool rendered = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
                if (completedLaughs < 1 || (rendered && (exitCloudFrames < 1 || latestAudioTime < 4.8f)))
                    Fail("mascot did not finish its five-second laugh and exit cloud");
                if (events < 2 || frames < 10 || hotFrames < 5 || (rendered && steamFrames < 5) || blinkMin > 5 || blinkMax < 50 || smileMin > 50 || smileMax < 60)
                    Fail("mascot laughter, blinking and anger were not exercised on this peer");
                Debug.Log("PLAYTEST_CHECK mascot events=" + events + " frames=" + frames + " hot=" + hotFrames + " steam=" + steamFrames +
                    " blink=" + blinkMin + ".." + blinkMax + " smile=" + smileMin + ".." + smileMax + " pointDot=" + worstAim + " completedLaughs=" + completedLaughs + " shortestPresentation=" + shortestPresentation + " fullLengthFrames=" + fullLengthFrames + " exitCloudFrames=" + exitCloudFrames + " audioTime=" + latestAudioTime);
            }
            private void Fail(string message) { if (failed) return; failed = true; fail(message); }
            public void Dispose()
            {
                if (sampling != null) { sampling.Tick = null; UnityEngine.Object.Destroy(sampling); }
                foreach (var cage in cages) if (cage != null) cage.DescentStarted -= OnDescent;
            }
        }

        private static IEnumerator CheckButtonHands(StopwatchMinigame game,PlayerController local,
            PlayerInputReader input,SessionPlayer[] players,ProbeBarrier barrier,Action<string> fail)
        {
            var presentation=game.GetComponent<StopwatchButtonPresentation>();
            var button=game.LocalButton;
            var cameras=UnityEngine.Object.FindFirstObjectByType<MinigameCameraController>();
            if(presentation==null || !presentation.IsActive || presentation.Hand==null)
            { fail("Stopwatch owner has no first-person hand");yield break; }
            int rendered=0;bool privacy=true,framed=true;
            var body=local.GetComponentsInChildren<Renderer>();
            var arm=presentation.Hand.GetComponentsInChildren<Renderer>();
            var output=Camera.main;
            void Observe(ScriptableRenderContext context,Camera camera)
            {
                if(camera!=output || !presentation.IsCloseUp)return;
                rendered++;
                privacy &= body.All(r=>r.forceRenderingOff) && arm.All(r=>!r.forceRenderingOff);
                privacy &= players.Where(p=>p.Avatar!=local).All(p=>p.Avatar.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>!r.forceRenderingOff));
                if(presentation.PressAmount>.99f)
                {
                    var point=camera.WorldToViewportPoint(presentation.Hand.ContactPosition);
                    framed &= point.z>0 && point.x>.15f && point.x<.85f && point.y>.2f && point.y<.8f;
                }
            }
            RenderPipelineManager.beginCameraRendering+=Observe;
            try
            {
                Capture("button-ready");
                yield return new WaitForSeconds(.2f);
                double start=Time.realtimeSinceStartupAsDouble;
                input.DriveInteractHold(true);
                yield return new WaitForSeconds(.45f);
                if(button.State!=CageButton.ButtonState.Running || presentation.PressAmount<.99f)
                    fail("Stopwatch E does not hold the finger down");
                Vector3 surface=button.PressSurface.bounds.center+Vector3.up*button.PressSurface.bounds.extents.y;
                if(Vector3.Distance(surface,presentation.Hand.ContactPosition)>.003f)
                    fail("Stopwatch finger does not contact the cap");
                Capture("button-held");
                yield return new WaitForSeconds(.8f);
                // One player keeps holding so early completion cannot end this observation.
                bool release=local!=players[players.Length-1].Avatar;
                if(release)
                {
                    input.DriveInteractHold(false);
                    double held=Time.realtimeSinceStartupAsDouble-start;
                    yield return new WaitForSeconds(.08f);
                    if(!button.Completed)fail("Stopwatch release did not stop measurement");
                    // Clients predict Stopped but receive the numeric result only at reveal.
                    if(NetworkManager.Singleton.IsServer && Math.Abs(button.Measured-held)>.10)
                        fail("Stopwatch animation delayed the measured interval");
                    Capture("button-release");
                    yield return new WaitForSeconds(.9f);
                    if(presentation.IsActive || local.MovementLocked || cameras.CurrentMode!=CameraMode.ThirdPerson ||
                        local.GetComponent<Igruha.Core.Interaction.PlayerInteractor>().ButtonOverride!=null)
                        fail("Stopwatch release did not restore camera/movement/interaction");
                    Vector3 before=local.Position;
                    input.DriveMove(Vector2.right);yield return new WaitForSeconds(.2f);input.DriveMove(Vector2.zero);
                    if(Vector3.Distance(before,local.Position)<.05f)fail("Stopwatch cannot walk after releasing E");
                    Capture("button-returned");
                }
                else yield return new WaitForSeconds(1.2f);
                if(!framed)fail("Stopwatch held button is outside the owner's camera frame");
                if(!privacy || (output!=null && SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null && rendered==0))
                    fail("Stopwatch owner-only hand visibility failed");
                Debug.Log("PLAYTEST_CHECK stopwatch hand contact, held input, privacy="+privacy+" framed="+framed+" rendered="+rendered+" released="+release);
            }
            finally { RenderPipelineManager.beginCameraRendering-=Observe; }
            yield return barrier.Wait(1);
        }

        private static IEnumerator CheckCageAttention(PitBear[] bears, SessionPlayer[] players, Action<string> fail)
        {
            var seen = new bool[bears.Length];
            var watchingFrames = new int[bears.Length];
            var animators = bears.Select(b => b.GetComponentInChildren<Animator>()).ToArray();
            int roarHash = Animator.StringToHash("Roar");
            float until = Time.realtimeSinceStartup + 20;
            while (Time.realtimeSinceStartup < until)
            {
                for (int i = 0; i < bears.Length; i++)
                {
                    PitBear source = bears[i];
                    if (source.State == PitBear.BearState.Taunt)
                        fail("removed rear state reached: " + source.name);
                    Animator animator = animators[i];
                    if (animator != null && (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == roarHash ||
                        (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).shortNameHash == roarHash)))
                        fail("removed rear animation played: " + source.name);
                    if (source.State != PitBear.BearState.CageApproach) continue;
                    var target = players.FirstOrDefault(p => p.Id == source.PresentationTargetId);
                    if (target == null || target.Avatar == null)
                        fail("cage attention has no replicated occupant target");
                    else
                    {
                        Vector3 direction = target.Avatar.Position - source.PresentationRoot.position; direction.y = 0;
                        if (Vector3.Angle(source.PresentationRoot.forward, direction) < 18)
                        {
                            seen[i] = true; watchingFrames[i]++;
                        }
                    }
                }
                yield return null;
            }
            for (int i = 0; i < bears.Length; i++)
                if (!seen[i]) fail("bear never faced an occupied cage on four paws: " + bears[i].name);
            Debug.Log("PLAYTEST_CHECK quadruped cage attention frames=" + string.Join(",", watchingFrames) + " rears=0");
        }

        private sealed class BearPresentationProbe : IDisposable
        {
            private readonly PitBear bear;
            private readonly CircusBearMotion motion;
            private readonly CircusAttackPresentation presentation;
            private readonly CircusKnockout knockout;
            private readonly SpectatorCamera spectator;
            private readonly MinigameCameraController cameras;
            private readonly Transform shotView;
            private readonly Action<string> fail;
            private readonly bool reviewMotion, captureImages;
            private readonly string reviewPrefix;
            private readonly System.Text.StringBuilder reviewFrames = new System.Text.StringBuilder(8192);
            private bool failed, trackingAttack, contact, localHit, localShot, spectatorSeen;
            private int attackNumber, localHitFrame, captureIndex;
            private float lastAge, minContact, maxContact, localHitAt;
            private float shotStartedAt=-1, longestShotSeconds;
            private Vector3 firstPaw, lastPaw;
            private int reviewFrame;
            private float reviewStartedAt=-1, reviewUntil, nextReviewFrame;
            private bool reviewSaved;
            private static readonly float[] CaptureTimes={0,.15f,.35f};

            public BearPresentationProbe(MinigameControllerBase game,PitBear bear,PlayerController local,Action<string> fail,bool captureImages=true)
            {
                this.bear=bear;this.fail=fail;this.captureImages=captureImages;
                motion=bear.GetComponentInChildren<CircusBearMotion>(true);
                presentation=(CircusAttackPresentation)Get(game,"attackPresentation");
                knockout=local.GetComponent<CircusKnockout>();
                spectator=(SpectatorCamera)Get(game,"spectator");
                cameras=(MinigameCameraController)Get(presentation,"cameras");
                shotView=(Transform)Get(presentation,"attackView");
                bear.Caught+=Caught;
                bear.ImpactShown+=Impact;
                reviewMotion=captureImages && LaunchArguments.HasFlag("--circus-motion-review") &&
                    SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null &&
                    LaunchArguments.TryGetValue("--playtest-screenshots",out reviewPrefix);
                if(reviewMotion)reviewFrames.AppendLine("frame,time,attackAge,state,pawX,pawY,pawZ,contactDistance,contactView");
                if(motion==null)Fail("bear has no paw contact motion component");
                if(Mathf.Abs((float)Get(bear,"attackContactTime")-PitBear.ContactSeconds)>.001f ||
                    Mathf.Abs((float)Get(bear,"attackDuration")-PitBear.StrikeSeconds)>.001f)
                    Fail("scene bear timing differs from authored contact/strike constants");
            }

            public void Sample()
            {
                if(bear!=null)
                {
                    bool attacking=bear.State==PitBear.BearState.Attack;
                    if(trackingAttack && (!attacking || bear.AttackAge<lastAge-.05f))FinishAttack();
                    if(attacking)
                    {
                        if(!trackingAttack)
                        {
                            trackingAttack=true;attackNumber++;contact=bear.HasContact;
                            minContact=float.PositiveInfinity;maxContact=0;
                            firstPaw=motion!=null?motion.StrikePawCenter:Vector3.zero;
                        }
                        lastAge=bear.AttackAge;
                        if(motion!=null)
                        {
                            lastPaw=motion.StrikePawCenter;
                            float distance=motion.ContactDistance;
                            if(!float.IsInfinity(distance) && !float.IsNaN(distance))
                            {minContact=Mathf.Min(minContact,distance);maxContact=Mathf.Max(maxContact,distance);}
                        }
                    }
                }
                if(presentation==null || knockout==null)return;
                bool presenting=knockout.IsPresenting;
                bool watching=spectator!=null && spectator.IsActive;
                if(!presenting && presentation.OwnsCamera)
                    Fail("bear view took camera before confirmed local contact or survived collapse");
                if(watching)
                {
                    spectatorSeen=true;
                    if(presentation.OwnsCamera || cameras!=null &&
                        (cameras.CurrentMode==CameraMode.TopDown || cameras.CurrentTarget==shotView))
                        Fail("bear view retained camera after spectator handoff; owns="+presentation.OwnsCamera+
                            " mode="+(cameras!=null?cameras.CurrentMode.ToString():"missing")+
                            " spectatorTarget="+(spectator.Target!=null?spectator.Target.DisplayName:"none"));
                }
                if(presenting && !localHit)
                {
                    localHit=true;localHitFrame=Time.frameCount;localHitAt=Time.time;
                    if(reviewMotion && reviewStartedAt<0){reviewStartedAt=Time.time;nextReviewFrame=Time.time;}
                    reviewUntil=Mathf.Max(reviewUntil,Time.time+CircusKnockout.PresentationSeconds+.2f);
                }
                if(presenting && !watching && presentation.OwnsCamera)
                {
                    localShot=true;
                    // Readability is wall-clock time. Unity caps deltaTime on a
                    // stalled/background client, so its gameplay clock can lag.
                    if(shotStartedAt<0)shotStartedAt=Time.realtimeSinceStartup;
                    longestShotSeconds=Mathf.Max(longestShotSeconds,Time.realtimeSinceStartup-shotStartedAt);
                }
                else shotStartedAt=-1;
                if(localShot && presenting && !watching && !presentation.OwnsCamera)
                    Fail("bear contact view returned to the fallen body's camera before spectator handoff");
                if(presenting && !watching && Time.frameCount>localHitFrame && !localShot)
                    Fail("confirmed bear hit did not enter the contact view on the following frame");
                if(captureImages && localHit && captureIndex<CaptureTimes.Length && Time.time-localHitAt>=CaptureTimes[captureIndex] &&
                    (captureIndex>0 || presentation.OwnsCamera))
                {
                    Capture("strike-"+Mathf.RoundToInt(CaptureTimes[captureIndex]*1000).ToString("D3"));
                    captureIndex++;
                }
                else CaptureMotion();
            }

            private void CaptureMotion()
            {
                if(!reviewMotion || bear==null)return;
                if(reviewStartedAt<0 && bear.State==PitBear.BearState.Chase)
                {reviewStartedAt=Time.time;reviewUntil=Time.time+8f;nextReviewFrame=Time.time;}
                if(reviewStartedAt<0 || Time.time>reviewUntil || Time.time<nextReviewFrame)return;
                nextReviewFrame=Time.time+1f/12f;
                Capture("motion-"+reviewFrame.ToString("D4"));
                Vector3 paw=motion!=null?motion.StrikePawCenter:Vector3.zero;
                var culture=System.Globalization.CultureInfo.InvariantCulture;
                reviewFrames.Append(reviewFrame++).Append(',').Append((Time.time-reviewStartedAt).ToString("F3",culture)).Append(',')
                    .Append(bear.AttackAge.ToString("F3",culture)).Append(',').Append(bear.State).Append(',')
                    .Append(paw.x.ToString("F3",culture)).Append(',').Append(paw.y.ToString("F3",culture)).Append(',')
                    .Append(paw.z.ToString("F3",culture)).Append(',').Append(motion!=null?motion.ContactDistance.ToString("F4",culture):"missing")
                    .Append(',').Append(presentation!=null && presentation.OwnsCamera).AppendLine();
            }

            private void Caught(PlayerController victim,Vector3 impulse)
            {
                contact=true;
                if(motion==null || motion.ContactDistance>.1201f || float.IsNaN(motion.ContactDistance))
                    Fail("authoritative bear caught player without measured paw contact");
                Debug.Log("PLAYTEST_CHECK bear CONTACT source="+bear.name+" target="+victim.name+" age="+bear.AttackAge.ToString("F3")+
                    " paw="+(motion!=null?motion.StrikePawCenter.ToString("F3"):"missing")+
                    " contactDistance="+(motion!=null?motion.ContactDistance.ToString("F4"):"missing"));
            }

            private void Impact(Vector3 point) { contact=true; }

            private void FinishAttack()
            {
                Debug.Log("PLAYTEST_CHECK bear source="+bear.name+" attack="+attackNumber+" contact="+contact+" age="+lastAge.ToString("F3")+
                    " contactMin="+minContact.ToString("F4")+" contactMax="+maxContact.ToString("F4")+
                    " pawFrom="+firstPaw.ToString("F3")+" pawTo="+lastPaw.ToString("F3"));
                trackingAttack=false;
            }

            public void CheckFinished()
            {
                Sample();
                if(trackingAttack)FinishAttack();
                if(presentation!=null && presentation.OwnsCamera)Fail("bear contact view survived return to hub");
                if(localHit && !localShot)Fail("local bear knockout never showed contact view");
                if(localHit && longestShotSeconds<.65f)
                    Fail("bear contact view ended before a readable continuous .65 s shot; longest="+longestShotSeconds.ToString("F3"));
                foreach(var hubCamera in UnityEngine.Object.FindObjectsByType<MinigameCameraController>(FindObjectsSortMode.None))
                    if(hubCamera.gameObject.scene.name=="Hub" && hubCamera.CurrentMode==CameraMode.TopDown)
                        Fail("hub retained circus contact camera mode");
                Debug.Log("PLAYTEST_CHECK bear view localHit="+localHit+" contactShot="+localShot+" spectator="+spectatorSeen+
                    " longestShot="+longestShotSeconds.ToString("F3"));
                SaveMotionReview();
            }

            private void Fail(string message)
            {
                if(failed)return;failed=true;fail(message);
            }

            public void Dispose()
            {
                if(bear!=null){bear.Caught-=Caught;bear.ImpactShown-=Impact;}
                if(trackingAttack)FinishAttack();
                SaveMotionReview();
            }

            private void SaveMotionReview()
            {
                if(!reviewMotion || reviewSaved)return;
                reviewSaved=true;
                System.IO.File.WriteAllText(reviewPrefix+"-motion.csv",reviewFrames.ToString());
                Debug.Log("PLAYTEST_CHECK circus motionReview frames="+reviewFrame+" manifest="+reviewPrefix+"-motion.csv");
            }
        }

        private static IEnumerator CheckInitialShelfRush(PlayerController local,PlayerInputReader input,MinigameStageState stage,Action<string> fail)
        {
            var cage=UnityEngine.Object.FindObjectsByType<CageStation>(FindObjectsSortMode.None).Single(c=>c.Occupant==local);
            var shelf=cage.GetComponentInChildren<CanShelf>();
            var grounding=local.GetComponent<CharacterFootGrounding>();
            float low=float.MaxValue,skinLow=float.MaxValue,seconds=0;int movingFrames=0,crouchedFrames=0;
            float unlockDeadline=Time.realtimeSinceStartup+5;
            while(local.MovementLocked && stage.Stage!=2 && Time.realtimeSinceStartup<unlockDeadline)yield return null;
            bool wasSuppressed=local.CrouchInputSuppressed;
            local.CrouchInputSuppressed=true;
            while(seconds<2f && stage.Stage!=2)
            {
                local.SetCrouched(seconds<.65f);
                Vector3 direction=shelf.Board.position-local.Position;direction.y=0;
                direction.Normalize();
                if(seconds>.65f && seconds<1f)direction=-direction;
                local.SetCameraReference(null);input.DriveMove(new Vector2(direction.x,direction.z));
                if(!local.MovementLocked)movingFrames++;
                if(local.IsCrouched)crouchedFrames++;
                float offset=local.Position.y-cage.transform.position.y;
                if(offset<low-.01f && offset<-.06f)
                {
                    var capsule=local.GetComponent<CapsuleCollider>();
                    Debug.Log("PLAYTEST_CHECK shelf penetration t="+seconds+" offset="+offset+
                        " body="+local.Position+" visual="+local.transform.position+" cage="+cage.transform.position+
                        " capsule="+capsule.center+" height="+capsule.height+" floor="+
                        cage.transform.Find("Floor/ClosedFloorSupport").GetComponent<Collider>().bounds);
                }
                low=Mathf.Min(low,offset);
                if(seconds>.15f && grounding!=null)skinLow=Mathf.Min(skinLow,grounding.LowestSkinHeight-cage.transform.position.y);
                seconds+=Time.deltaTime;yield return null;
            }
            input.DriveMove(Vector2.zero);
            local.SetCrouched(false);local.CrouchInputSuppressed=wasSuppressed;
            if(movingFrames==0)fail("initial shelf rush never exercised unlocked movement");
            if(crouchedFrames==0)fail("initial shelf rush never exercised crouching");
            if(low<-.06f)fail("initial shelf rush sank under the closed cage floor: "+low);
            if(skinLow<-.06f)fail("initial shelf rush put the visible skin under the closed floor: "+skinLow);
            Debug.Log("PLAYTEST_CHECK shelf rush movingFrames="+movingFrames+" crouchedFrames="+crouchedFrames+" minimumFloorOffset="+low+" minimumSkinOffset="+skinLow);
            Capture("shelf-rush");
        }

        private static IEnumerator CheckShelfOcclusion(CansOrderMinigame cans, ShelfRenderProbe probe, ProbeBarrier barrier, Action<string> fail)
        {
            var participants = ((IList)Get(cans,"contestants")).Cast<object>().ToArray();
            var avatars = participants.Select(c=>((SessionPlayer)c.GetType().GetField("Session").GetValue(c)).Avatar).ToArray();
            Vector3[] positions = avatars.Select(p=>p.Position).ToArray();
            Quaternion[] rotations = avatars.Select(p=>p.transform.rotation).ToArray();
            bool server = NetworkManager.Singleton.IsServer;
            if (server)
            {
                float distance = (float)Get(cans,"shelfCameraDistance");
                for (int i=0;i<participants.Length;i++)
                {
                    var shelf=(CanShelf)participants[i].GetType().GetField("Shelf").GetValue(participants[i]);
                    Vector3 close=shelf.Board.position-shelf.Board.forward*(distance-.25f);
                    close.y=positions[i].y;
                    avatars[i].RequestTeleport(close,Quaternion.LookRotation(-shelf.Board.forward));
                }
            }
            try
            {
                yield return new WaitForSeconds(.8f);
                probe.CheckHeadlessCallbacks();
                Capture("shelf-occlusion");
                yield return new WaitForSeconds(.35f);
                if (SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null && probe.ShelfFrames==0)
                    fail("shelf visibility was not exercised by the real output camera");
                if (SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null && !probe.BlockingGeometrySeen)
                    fail("shelf occlusion fixture did not place the avatar between camera and cans");
                Debug.Log("PLAYTEST_CHECK shelf renderFrames="+probe.ShelfFrames+" blockingGeometry="+probe.BlockingGeometrySeen+
                    " headless="+(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null));
                yield return barrier.Wait(1);
            }
            finally
            {
                if(server)
                    for(int i=0;i<avatars.Length;i++)
                        if(avatars[i]!=null)avatars[i].RequestTeleport(positions[i],rotations[i]);
            }
            yield return new WaitForSeconds(.25f);
        }

        /// <summary>Test-only rendezvous: a faster host must not teleport an
        /// avatar while that client is measuring normal movement or taking its frame.</summary>
        private sealed class ProbeBarrier : IDisposable
        {
            private const string ReadyMessage="CircusPlaytest.Ready";
            private const string ReleasedMessage="CircusPlaytest.Released";
            private readonly NetworkManager network;
            private readonly int expected;
            private readonly Action<string> fail;
            private readonly HashSet<ulong>[] ready={new HashSet<ulong>(),new HashSet<ulong>()};
            private readonly bool[] released=new bool[2];

            public ProbeBarrier(int expected,Action<string> fail)
            {
                this.expected=expected;this.fail=fail;network=NetworkManager.Singleton;
                network.CustomMessagingManager.RegisterNamedMessageHandler(ReadyMessage,ReceiveReady);
                network.CustomMessagingManager.RegisterNamedMessageHandler(ReleasedMessage,ReceiveReleased);
            }

            public IEnumerator Wait(byte phase)
            {
                float deadline=Time.realtimeSinceStartup+15f;
                float nextSend=0;
                while(!released[phase] && Time.realtimeSinceStartup<deadline)
                {
                    if(network.IsServer)
                    {
                        ready[phase].Add(network.LocalClientId);
                        if(ready[phase].Count>=expected)
                        {
                            released[phase]=true;
                            using var writer=new FastBufferWriter(1,Allocator.Temp);
                            writer.WriteValueSafe(phase);
                            network.CustomMessagingManager.SendNamedMessageToAll(ReleasedMessage,writer,NetworkDelivery.ReliableSequenced);
                        }
                    }
                    else if(Time.realtimeSinceStartup>=nextSend)
                    {
                        nextSend=Time.realtimeSinceStartup+.3f;
                        using var writer=new FastBufferWriter(1,Allocator.Temp);
                        writer.WriteValueSafe(phase);
                        network.CustomMessagingManager.SendNamedMessage(ReadyMessage,NetworkManager.ServerClientId,writer,NetworkDelivery.ReliableSequenced);
                    }
                    yield return null;
                }
                if(!released[phase])fail("circus test barrier timed out at phase "+phase);
            }

            private void ReceiveReady(ulong sender,FastBufferReader reader)
            {
                if(!network.IsServer || !network.ConnectedClientsIds.Contains(sender))return;
                reader.ReadValueSafe(out byte phase);
                if(phase<ready.Length)ready[phase].Add(sender);
            }

            private void ReceiveReleased(ulong sender,FastBufferReader reader)
            {
                if(sender!=NetworkManager.ServerClientId)return;
                reader.ReadValueSafe(out byte phase);
                if(phase<released.Length)released[phase]=true;
            }

            public void Dispose()
            {
                if(network==null || network.CustomMessagingManager==null)return;
                network.CustomMessagingManager.UnregisterNamedMessageHandler(ReadyMessage);
                network.CustomMessagingManager.UnregisterNamedMessageHandler(ReleasedMessage);
            }
        }

        private sealed class ShelfRenderProbe : IDisposable
        {
            private readonly MinigameStageState stage;
            private readonly MinigameCameraController cameras;
            private readonly Transform shelfRig;
            private readonly Camera output;
            private readonly CansShelfVisibility visibility;
            private readonly Renderer[] own, others;
            private readonly bool[] ownOff, othersOff, ownEnabled;
            private readonly Action<string> fail;
            private bool failed;
            private int restoredFrames;
            public int ShelfFrames { get; private set; }
            public bool BlockingGeometrySeen { get; private set; }

            public ShelfRenderProbe(CansOrderMinigame game,PlayerController local,SessionPlayer[] players,Action<string> fail)
            {
                this.fail=fail;
                stage=game.GetComponent<MinigameStageState>();
                cameras=(MinigameCameraController)Get(game,"cameraController");
                shelfRig=(Transform)Get(game,"shelfCameraRig");
                visibility=(CansShelfVisibility)Get(game,"shelfVisibility");
                output=(Camera)Get(visibility,"outputCamera");
                own=local.GetComponentsInChildren<Renderer>(true);
                others=players.Where(p=>p.Avatar!=null && p.Avatar!=local)
                    .SelectMany(p=>p.Avatar.GetComponentsInChildren<Renderer>(true)).ToArray();
                ownOff=own.Select(r=>r.forceRenderingOff).ToArray();
                ownEnabled=own.Select(r=>r.enabled).ToArray();
                othersOff=others.Select(r=>r.forceRenderingOff).ToArray();
                stage.StageStarted+=RefreshCallbackOrder;
                RefreshCallbackOrder(0);
            }

            private void RefreshCallbackOrder(byte unused)
            {
                RenderPipelineManager.beginCameraRendering-=Begin;
                RenderPipelineManager.endCameraRendering-=End;
                RenderPipelineManager.beginCameraRendering+=Begin;
                RenderPipelineManager.endCameraRendering+=End;
            }

            private void Begin(ScriptableRenderContext context,Camera camera)
            {
                if(camera!=output || cameras==null)return;
                bool shelf=cameras.CurrentMode==CameraMode.Fixed && cameras.CurrentTarget==shelfRig;
                if(shelf)ShelfFrames++;else restoredFrames++;
                for(int i=0;i<own.Length;i++)
                {
                    if(own[i]==null)continue;
                    if(own[i].forceRenderingOff!=(shelf || ownOff[i]))Fail("own avatar render visibility escaped shelf camera scope");
                    if(shelf && own[i].enabled!=ownEnabled[i])Fail("shelf visibility changed renderer.enabled");
                    if(shelf && camera!=null && own[i].enabled && own[i].bounds.IntersectRay(
                        new Ray(camera.transform.position,camera.transform.forward),out float depth) && depth<1.1f)
                        BlockingGeometrySeen=true;
                }
                for(int i=0;i<others.Length;i++)
                    if(others[i]!=null && others[i].forceRenderingOff!=othersOff[i])Fail("shelf camera hid another player's model");
            }

            private void End(ScriptableRenderContext context,Camera camera)
            {
                if(camera!=output)return;
                for(int i=0;i<own.Length;i++)
                    if(own[i]!=null && own[i].forceRenderingOff!=ownOff[i])Fail("shelf render left avatar hidden after frame");
            }

            public void CheckHeadlessCallbacks()
            {
                if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null)return;
                if(output==null)
                {
                    Debug.Log("PLAYTEST_CHECK shelf headless render callbacks skipped: no output camera");
                    return;
                }
                typeof(CansShelfVisibility).GetMethod("BeginCamera",Private).Invoke(visibility,
                    new object[]{default(ScriptableRenderContext),output});
                Begin(default,output);
                typeof(CansShelfVisibility).GetMethod("EndCamera",Private).Invoke(visibility,
                    new object[]{default(ScriptableRenderContext),output});
                End(default,output);
            }

            public void CheckFinished()
            {
                if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null && restoredFrames==0)
                    Fail("shelf test never observed restored third-person output");
                End(default,output);
            }

            private void Fail(string message)
            {
                if(failed)return;
                failed=true;fail(message);
            }

            public void Dispose()
            {
                if(stage!=null)stage.StageStarted-=RefreshCallbackOrder;
                RenderPipelineManager.beginCameraRendering-=Begin;
                RenderPipelineManager.endCameraRendering-=End;
            }
        }
        private static IEnumerator CheckCanTurns(CansOrderMinigame cans,PlayerController local,PlayerController[] victims,CageTravelProbe travelProbe,Action<string> fail)
        {
            int count=cans.Round.CanCount;
            var solution=CansSolutionFixture.Known(SessionScoreboard.Current.LocalPlayer.Id,count);
            if(NetworkManager.Singleton.IsServer) CansSolutionFixture.SetKnown(cans);
            var mine=((IList)Get(cans,"contestants")).Cast<object>().Single(c=>((SessionPlayer)c.GetType().GetField("Session").GetValue(c)).Avatar==local);
            var shelf=(CanShelf)mine.GetType().GetField("Shelf").GetValue(mine);
            var button=(CanConfirmButton)mine.GetType().GetField("Button").GetValue(mine);
            var cage=(CageStation)mine.GetType().GetField("Cage").GetValue(mine);
            var stage=cans.GetComponent<MinigameStageState>();
            var network=cans.GetComponent<CansOrderNetwork>();
            int sent=0,checkedCircle=0;
            float limit=Time.realtimeSinceStartup+85,minOffset=float.MaxValue,maxOffset=float.MinValue;
            bool victim=victims.Contains(local);
            while(cans!=null && Time.realtimeSinceStartup<limit)
            {
                travelProbe.Sample();
                if(cage.Descending && victim && cans.Stage==3)
                {
                    float offset=local.transform.position.y-cage.transform.position.y;
                    minOffset=Mathf.Min(minOffset,offset);maxOffset=Mathf.Max(maxOffset,offset);
                }
                if(cans.Stage==2 && stage.Subround==cans.Round.Circle && sent!=cans.Round.Circle && stage.StageDuration-stage.StageRemaining>.9f)
                {
                    if(cans.Round.Circle==2)Capture("interface-board");
                    var order=Enumerable.Range(0,count).Select(i=>solution[(i+1)%count]).ToList();
                    if(!victim){order=solution.ToList();int t=order[0];order[0]=order[1];order[1]=t;}
                    shelf.SetArrangement(order);button.Interact(local);sent=cans.Round.Circle;
                }
                if(cans.Stage==3 && stage.Subround==cans.Round.Circle && checkedCircle!=cans.Round.Circle &&
                    stage.StageDuration-stage.StageRemaining>.35f && RevealSnapshotCommitted(network))
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
                        if(maxOffset-minOffset>.04f)fail("walking cage passenger loses support="+(maxOffset-minOffset));
                        if(victim)Debug.Log("PLAYTEST_CHECK cage relativeOffsetRange="+(maxOffset-minOffset));
                        yield break;
                    }
                }
                yield return null;
            }
            fail("five cans turns did not complete");
        }

        /// <summary>Exercises live input and animation while the game's own replicated descent runs.</summary>
        private sealed class CageTravelProbe : IDisposable
        {
            private readonly PlayerController player;
            private readonly PlayerInputReader input;
            private readonly CageStation cage;
            private readonly Rigidbody body;
            private readonly Animator animator;
            private readonly Action<string> fail;
            private readonly bool previousCrouchSuppressed;
            private Vector3 previousPosition;
            private float seconds, travel, minFloor=float.MaxValue, maxFloor=float.MinValue;
            private int frames, animatedFrames, crouchedFrames;
            private bool driving, failed;

            public CageTravelProbe(PlayerController player,PlayerInputReader input,Action<string> fail)
            {
                this.player=player;this.input=input;this.fail=fail;
                cage=UnityEngine.Object.FindObjectsByType<CageStation>(FindObjectsSortMode.None).Single(c=>c.Occupant==player);
                body=player.GetComponent<Rigidbody>();animator=player.GetComponentInChildren<Animator>();
                previousCrouchSuppressed=player.CrouchInputSuppressed;
            }

            public void Sample()
            {
                if(cage==null || player==null || !cage.Descending || cage.DoorsOpen)
                { StopInput();return; }
                if(!driving)previousPosition=player.Position;
                driving=true;frames++;seconds+=Time.deltaTime;
                if(!player.enabled || player.MovementLocked || body.isKinematic)
                    Fail("cage descent freezes the live player motor");
                player.SetCameraReference(null);
                player.CrouchInputSuppressed=true;
                player.SetCrouched(seconds%1.6f>.9f);
                if(player.IsCrouched)crouchedFrames++;
                Vector3 direction=cage.transform.right*(seconds%.7f<.35f?1f:-1f);
                input.DriveMove(new Vector2(direction.x,direction.z));
                Vector3 step=player.Position-previousPosition;step.y=0;travel+=step.magnitude;previousPosition=player.Position;
                float floor=player.transform.position.y-cage.transform.position.y;
                minFloor=Mathf.Min(minFloor,floor);maxFloor=Mathf.Max(maxFloor,floor);
                if(animator!=null && animator.enabled && animator.speed>0 && animator.GetFloat("Speed")>.1f)animatedFrames++;
            }

            public void CheckFinished(bool expectedToDescend)
            {
                if(expectedToDescend && (frames<10 || travel<.35f || animatedFrames<5 || crouchedFrames<3))
                    Fail("descent never exercised walking, crouching and animation: frames="+frames+" travel="+travel+" animated="+animatedFrames+" crouched="+crouchedFrames);
                if(frames>0 && (minFloor<-.04f || maxFloor>.04f))Fail("walking on descending cage lost floor contact: "+minFloor+".."+maxFloor);
                Debug.Log("PLAYTEST_CHECK free cage movement frames="+frames+" metres="+travel.ToString("F2")+
                    " animated="+animatedFrames+" crouched="+crouchedFrames+" floor="+minFloor+".."+maxFloor);
            }

            private void StopInput()
            {
                if(!driving)return;
                driving=false;
                if(input!=null)input.DriveMove(Vector2.zero);
                if(player!=null){player.SetCrouched(false);player.CrouchInputSuppressed=previousCrouchSuppressed;}
            }
            private void Fail(string message){if(failed)return;failed=true;fail(message);}
            public void Dispose()=>StopInput();
        }

        private static bool RevealSnapshotCommitted(CansOrderNetwork network)
        {
            if(network==null || !network.IsSpawned || network.IsServer)return true;
            if((bool)Get(network,"roundDirty") || (bool)Get(network,"stageDirty") || (bool)Get(network,"entriesDirty"))return false;
            var entries=(NetworkList<CansOrderEntryNetState>)Get(network,"entries");
            int playerId=SessionScoreboard.Current.LocalPlayer.Id;
            for(int i=0;i<entries.Count;i++)
                if(entries[i].PlayerId==playerId)return entries[i].Revealed;
            return false;
        }

        private static void Capture(string suffix)
        {
            if(!LaunchArguments.TryGetValue("--playtest-screenshots",out string path) ||
                SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
            ScreenCapture.CaptureScreenshot(path+"-"+suffix+".png");
        }
    }
}
