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
            var stage = game.GetComponent<MinigameStageState>();
            float deadline = Time.realtimeSinceStartup + 155;
            if(game is CansOrderMinigame) yield return CheckInitialShelfRush(local,input,stage,fail);
            while (stage.Stage != 2 && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSeconds(.5f);
            bool cansPlacement=game is CansOrderMinigame;
            if (!local.enabled || local.MovementLocked != cansPlacement || input.Suspended || !input.enabled)
                fail("circus local movement lock differs from placement rules");
            if (!local.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.enabled && !r.forceRenderingOff))
                fail("circus own character hidden");
            using var shelfProbe = cansPlacement ? new ShelfRenderProbe((CansOrderMinigame)game, local, players, fail) : null;
            Vector3 before = local.Position;
            input.DriveMove(Vector2.right); yield return new WaitForSeconds(.3f); input.DriveMove(Vector2.zero);
            float moved=Vector3.Distance(before,local.Position);
            if(cansPlacement ? moved>.04f : moved<.08f)fail("circus placement movement differs from rules: "+moved);
            yield return barrier.Wait(0);
            if (shelfProbe != null) yield return CheckShelfOcclusion((CansOrderMinigame)game, shelfProbe, barrier, fail);
            Capture("character");
            // CaptureScreenshot renders at the end of the frame. Do not switch
            // the host to the hatch camera before this character frame is written.
            yield return new WaitForSeconds(.4f);
            var bears = UnityEngine.Object.FindObjectsByType<PitBear>(FindObjectsSortMode.None).OrderBy(b=>b.name).ToArray();
            if(bears.Length!=2){fail("circus must have two bears");yield break;}
            if(bears.Select(b=>b.GetComponent<NetworkObject>().NetworkObjectId).Distinct().Count()!=2)
                fail("bears do not have independent network identities");
            var bear = bears[0];
            using var bearProbe = new BearPresentationProbe(game,bear,local,fail);
            using var secondProbe = new BearPresentationProbe(game,bears[1],local,fail,false);
            Vector3 secondBefore=bears[1].transform.position;
            float secondTravel=0,minimumSeparation=float.MaxValue;
            bool secondChased=false;
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
            float nextPursuitReport=Time.realtimeSinceStartup+3;
            float liftMin=float.MaxValue,liftMax=float.MinValue;
            var localCage=UnityEngine.Object.FindObjectsByType<CageStation>(FindObjectsSortMode.None).FirstOrDefault(c=>c.Occupant==local);
            while (Time.realtimeSinceStartup<deadline && !caught.All(v=>v))
            {
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
                if(liftMax-liftMin>.05f)fail("lift passenger jitters on descent/ascent");
            }
            if(!opened.All(v=>v))fail("trapdoor collision/animation incomplete");
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
            var network=cans.GetComponent<CansOrderNetwork>();
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
                if(cans.Stage==2 && stage.Subround==cans.Round.Circle && sent!=cans.Round.Circle && stage.StageDuration-stage.StageRemaining>.9f)
                {
                    var order=Enumerable.Range(0,count).Select(i=>(i+1)%count).ToList();
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
                        if(maxOffset-minOffset>.04f)fail("cage passenger jitter="+(maxOffset-minOffset));
                        if(victim)Debug.Log("PLAYTEST_CHECK cage relativeOffsetRange="+(maxOffset-minOffset));
                        yield break;
                    }
                }
                yield return null;
            }
            fail("five cans turns did not complete");
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
