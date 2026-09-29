using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.Audio;
using Igruha.Core.CameraSystems;
using Igruha.Core.Items;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.UI;
using Igruha.Minigames.CansOrder;
using Igruha.Minigames.CarryItem;
using Igruha.Minigames.CryingAngels;
using Igruha.Minigames.Exam;
using Igruha.Minigames.MemoryRun;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>Opt-in checks through the real scene and transport, isolated from release builds.</summary>
    public sealed class PlaytestSeptemberProbe : MonoBehaviour
    {
        private bool failed;
        private float nextChoice;
        private MinigameControllerBase game;
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--playtest-check", out _)) return;
            var go = new GameObject(nameof(PlaytestSeptemberProbe)); DontDestroyOnLoad(go); go.AddComponent<PlaytestSeptemberProbe>();
        }
        private void Update()
        {
            var selection = CharacterSelection.Current;
            if (selection == null || selection.HasChosen || Time.realtimeSinceStartup < nextChoice ||
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Hub") return;
            nextChoice = Time.realtimeSinceStartup + 1;
            selection.ReportReady();
            int id = NetworkManager.Singleton != null ? (int)NetworkManager.Singleton.LocalClientId : 0;
            for (int i = 0; i < 8; i++) if (!selection.IsTaken((id+i)%8)) { selection.Choose((id+i)%8); break; }
        }
        private IEnumerator Start()
        {
            LaunchArguments.TryGetValue("--playtest-check", out string mode);
            if (mode.StartsWith("countdown-"))
            {
                yield return Countdown();
                yield return Finish(mode);
                yield break;
            }
            if (mode == "footsteps")
            {
                yield return HubFootsteps();
                yield return Finish(mode);
                yield break;
            }
            float deadline = Time.realtimeSinceStartup + 150;
            while (Time.realtimeSinceStartup < deadline)
            {
                game = MinigameControllerBase.Current;
                if (game != null && game.Phase == MinigamePhase.Round) break;
                yield return null;
            }
            if (game == null || game.Phase != MinigamePhase.Round) { Fail("round timeout"); Application.Quit(1); yield break; }
            if (mode == "carry") yield return Carry();
            else if (mode == "infection") yield return Infection();
            else if (mode == "exam") yield return Exam();
            else if (mode == "cans") yield return Cans();
            else if (mode == "memory") yield return Memory();
            else if (mode == "angels") yield return AngelsHuntCheck.Run((CryingAngelsMinigame)game, Fail);
            else if (mode == "tutorial") yield return new WaitForSeconds(1);
            else Fail("unknown mode");
            yield return Finish(mode);
        }
        private IEnumerator Finish(string mode)
        {
            if (!failed) Debug.Log("PLAYTEST_CHECK PASS mode=" + mode + " id=" + NetworkManager.Singleton.LocalClientId);
            yield return new WaitForSecondsRealtime(NetworkManager.Singleton.IsServer ? 12 : 4);
            Application.Quit(failed ? 1 : 0);
        }
        private IEnumerator Countdown()
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var phase = pass == 0 ? MinigamePhase.Practice : MinigamePhase.Round;
                float deadline = Time.realtimeSinceStartup + 120;
                while (Time.realtimeSinceStartup < deadline)
                {
                    game = MinigameControllerBase.Current;
                    if (game != null && game.Phase == phase &&
                        (game.StartCountdownActive || (pass == 0 && game is CryingAngelsMinigame)) &&
                        SessionScoreboard.Current?.LocalPlayer?.Avatar != null) break;
                    yield return null;
                }
                if (game == null || game.Phase != phase)
                { Fail("countdown not observed in " + phase); yield break; }
                foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    if (behaviour.GetType().Name.Contains("DebugBot")) behaviour.enabled = false;
                if (pass == 0 && game is CryingAngelsMinigame)
                {
                    yield return AngelsPractice();
                    if (failed) yield break;
                    continue;
                }
                if (!game.StartCountdownActive)
                { Fail("countdown not observed in " + phase); yield break; }
                var avatar = SessionScoreboard.Current.LocalPlayer.Avatar;
                var input = avatar.GetComponent<PlayerInputReader>(); input.EngageAutopilot();
                var push = avatar.GetComponent<PlayerPushAbility>();
                int punches = 0;
                Action onPunch = () => punches++;
                push.PunchStarted += onPunch;
                yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
                var start = avatar.Position;
                float lockedAt = Time.time;
                int frames = 0;
                var countdownTimer = FindFirstObjectByType<RoundTimer>();
                var countdownHud = FindFirstObjectByType<RoundHud>();
                var countdownPlate = (GameObject)typeof(RoundHud).GetField("timerPlate", Private).GetValue(countdownHud);
                while (game != null && game.Phase == phase && game.StartCountdownActive && Time.realtimeSinceStartup < deadline)
                {
                    frames++;
                    if (game is CryingAngelsMinigame &&
                        (countdownPlate.activeInHierarchy ||
                         (NetworkManager.Singleton.IsServer && countdownTimer.IsRunning) ||
                         (Time.time - lockedAt > .25f && countdownTimer.Duration <= 0f) ||
                         countdownTimer.Remaining < countdownTimer.Duration - .05f))
                    { Fail($"angels spent round time during 3–2–1: {countdownTimer.Remaining:F2}/{countdownTimer.Duration:F2}"); break; }
                    input.DriveMove(Vector2.up); input.DriveJump(); input.DrivePushHold(true);
                    input.DriveInteract(); input.DriveInteractHold(true); input.DrivePose(2); push.RequestPush();
                    if (!input.Suspended || input.MoveInput != Vector2.zero || input.JumpPressed || input.PushPressed ||
                        input.InteractPressed || input.InteractHeld || input.PushHeld || input.PoseRequest != 0)
                    { Fail("countdown accepted input in " + phase); break; }
                    var delta = avatar.Position - start; delta.y = 0;
                    if (delta.magnitude > .12f) { Fail("countdown moved " + delta + " in " + phase); break; }
                    yield return null;
                }
                push.PunchStarted -= onPunch;
                if (frames < 5 || punches != 0 || game == null || game.Phase != phase || game.StartCountdownActive)
                { Fail($"countdown incomplete {phase} frames={frames} punches={punches}"); yield break; }
                float duration = Time.time - lockedAt;
                // A role may keep its body locked (hunter/keeper); the shared
                // input gate must still release without removing that role lock.
                if (input.Suspended || input.JumpPressed || input.PushPressed || input.InteractPressed)
                    Fail("countdown did not release cleanly in " + phase);
                input.DriveJump(); input.DriveInteract();
                if (!input.JumpPressed || !input.InteractPressed) Fail("countdown input not restored");
                input.ConsumeJump(); input.ConsumeInteract();
                if (!avatar.MovementLocked)
                {
                    var before = avatar.Position;
                    input.DriveMove(Vector2.up); yield return new WaitForSeconds(.3f); input.DriveMove(Vector2.zero);
                    if (Vector3.Distance(before,avatar.Position) < .1f)
                    {
                        // A newly active beam can legitimately catch an angel during
                        // this movement sample. The countdown gate was checked above;
                        // do not mistake the independent gameplay freeze for that gate.
                        var runner = avatar.GetComponent<RunnerState>();
                        bool caught = game is CryingAngelsMinigame && runner != null && !runner.IsFree;
                        if (!caught) Fail($"countdown movement not restored in {phase}: before={before} after={avatar.Position} locked={avatar.MovementLocked} suspended={input.Suspended}");
                        else Debug.Log("PLAYTEST_CHECK countdown released input; active beam caught runner during movement sample");
                    }
                }
                Debug.Log($"PLAYTEST_CHECK countdown phase={phase} locked={duration:F2}s frames={frames} punches={punches} roleLocked={avatar.MovementLocked}");
                if (game is CryingAngelsMinigame)
                {
                    var timer = FindFirstObjectByType<RoundTimer>();
                    var hud = FindFirstObjectByType<RoundHud>();
                    var plate = (GameObject)typeof(RoundHud).GetField("timerPlate", Private).GetValue(hud);
                    if (!timer.IsRunning || timer.Remaining <= 0f || !plate.activeInHierarchy)
                        Fail("angels scored round timer missing");
                    if (timer.Remaining < timer.Duration - 1f)
                        Fail($"angels lost round time before play: {timer.Remaining:F2}/{timer.Duration:F2}");
                    float remainingAtStart = timer.Remaining;
                    yield return new WaitForSeconds(1f);
                    float spent = remainingAtStart - timer.Remaining;
                    if (spent < .6f || spent > 1.4f) Fail($"angels timer not ticking after countdown: {spent:F2}");
                    Debug.Log($"PLAYTEST_CHECK angels timer held during countdown, started={remainingAtStart:F2}/{timer.Duration:F2}, spentAfterStart={spent:F2}");
                }
                if (pass == 0) game.ToggleTutorialReady();
            }
        }
        private IEnumerator AngelsPractice()
        {
            var angels = (CryingAngelsMinigame)game;
            var timer = FindFirstObjectByType<RoundTimer>();
            var hud = FindFirstObjectByType<RoundHud>();
            var plate = (GameObject)typeof(RoundHud).GetField("timerPlate", Private).GetValue(hud);
            var countdown = (TMPro.TMP_Text)typeof(RoundHud).GetField("countdownText", Private).GetValue(hud);
            var avatar = SessionScoreboard.Current.LocalPlayer.Avatar;
            var input = avatar.GetComponent<PlayerInputReader>();
            input.EngageAutopilot(); input.DriveMove(Vector2.zero);
            int history = SessionScoreboard.Current.History.Count;
            float start = Time.realtimeSinceStartup;
            bool host = NetworkManager.Singleton.IsServer;
            var keeperView = FindFirstObjectByType<FirstPersonCameraRig>();
            bool ready = false, cancelled = false, reconfirmed = false;
            float wait = host ? 8f : 10f;
            while (Time.realtimeSinceStartup - start < wait)
            {
                float elapsed = Time.realtimeSinceStartup - start;
                // Keep the live beam away from the movement sample. Beam freezes
                // are covered separately by AngelsHuntCheck, not by this input gate test.
                if (avatar == angels.Keeper && keeperView != null)
                {
                    keeperView.SetViewDrivenExternally(true);
                    keeperView.SetView(20f, -55f);
                }
                if (game == null || game.Phase != MinigamePhase.Practice || game.StartCountdownActive ||
                    timer.IsRunning || timer.Remaining != 0f || plate.activeInHierarchy ||
                    countdown.gameObject.activeInHierarchy || input.Suspended ||
                    SessionScoreboard.Current.History.Count != history)
                { Fail("angels practice started countdown/timer, blocked input or ended before ready"); yield break; }
                // Allow the server beam snapshot to arrive, then verify the practice is playable.
                if (elapsed > 1f && !angels.BeamEnabled)
                { Fail("angels practice beam stayed off"); yield break; }
                if (host && !ready && elapsed > 2f) { ready = true; game.ToggleTutorialReady(); }
                if (host && !cancelled && elapsed > 4f) { cancelled = true; game.ToggleTutorialReady(); }
                if (host && !reconfirmed && elapsed > 6f) { reconfirmed = true; game.ToggleTutorialReady(); }
                yield return null;
            }
            if (!avatar.MovementLocked)
            {
                var before = avatar.Position;
                // The spawn now has a sculpture behind it. Test the open inward
                // approach instead of an arbitrary camera-relative direction.
                var reference = new GameObject("Angels practice movement reference");
                avatar.SetCameraReference(reference.transform);
                input.DriveMove(new Vector2(-before.x, -before.z).normalized);
                yield return new WaitForSeconds(.6f); input.DriveMove(Vector2.zero);
                Destroy(reference);
                if (Vector3.Distance(before, avatar.Position) < .1f)
                    Fail($"angels practice movement blocked before={before} after={avatar.Position} locked={avatar.MovementLocked} suspended={input.Suspended} phase={game.Phase}");
            }
            Debug.Log($"PLAYTEST_CHECK angels free practice waited={wait}s timer=off countdown=off beam=on roleLocked={avatar.MovementLocked}");
            if (!host) game.ToggleTutorialReady();
        }
        private IEnumerator HubFootsteps()
        {
            float deadline = Time.realtimeSinceStartup + 120;
            while (Time.realtimeSinceStartup < deadline)
            {
                var board = SessionScoreboard.Current;
                bool ready = board != null && board.Players.Count == 4 && board.LocalPlayer?.Avatar != null;
                if (ready) foreach (var player in board.Players) ready &= player.Avatar != null;
                if (ready) break;
                yield return null;
            }
            var scoreboard = SessionScoreboard.Current;
            if (scoreboard?.LocalPlayer?.Avatar == null || scoreboard.Players.Count != 4)
            { Fail("footsteps Hub roster timeout"); yield break; }
            foreach (var bot in FindObjectsByType<DebugPlayerBot>(FindObjectsSortMode.None)) bot.enabled = false;
            var local = scoreboard.LocalPlayer.Avatar;
            var input = local.GetComponent<PlayerInputReader>();
            input.EngageAutopilot(); input.DriveMove(Vector2.zero);
            var reference = new GameObject("Footsteps test movement reference");
            local.SetCameraReference(reference.transform);
            local.TeleportTo(new Vector3(-1.5f + NetworkManager.Singleton.LocalClientId * 1.2f, .2f, -2), Quaternion.identity);
            yield return new WaitForSeconds(3);
            var sources = new Dictionary<AudioSource, (AudioClip clip, int sample, bool playing)>();
            var counts = new Dictionary<PlayerController, int>();
            foreach (var player in scoreboard.Players)
            {
                if (player.Avatar == null) { Fail("footsteps missing remote avatar"); yield break; }
                counts.Add(player.Avatar, 0);
                foreach (var source in player.Avatar.GetComponent<MinigameAudioPlayer>().GetComponentsInChildren<AudioSource>(true))
                    sources.Add(source, (null, 0, false));
            }
            float start = Time.time;
            while (Time.time - start < 8)
            {
                input.DriveMove(Mathf.FloorToInt((Time.time-start) / .8f) % 2 == 0 ? Vector2.up : Vector2.down);
                foreach (var player in scoreboard.Players)
                {
                    foreach (var source in player.Avatar.GetComponent<MinigameAudioPlayer>().GetComponentsInChildren<AudioSource>(true))
                    {
                        var previous = sources[source];
                        bool step = source.isPlaying && source.clip != null && source.clip.name.StartsWith("SFX_CHR_Step_");
                        if (step && (!previous.playing || previous.clip != source.clip || source.timeSamples < previous.sample))
                        {
                            counts[player.Avatar]++;
                            if (source.mute || source.volume <= 0) Fail("footsteps muted source");
                        }
                        sources[source] = (source.clip, source.clip != null ? source.timeSamples : 0, step);
                    }
                }
                yield return null;
            }
            input.DriveMove(Vector2.zero);
            foreach (var pair in counts)
            {
                var animator = pair.Key.GetComponentInChildren<Animator>();
                Debug.Log($"PLAYTEST_CHECK footsteps avatar={pair.Key.name} local={pair.Key == local} sounds={pair.Value} culling={animator.cullingMode} ground={pair.Key.GroundCollider?.name} position={pair.Key.transform.position}");
                if (pair.Value < 4) Fail("footsteps too few sounds: " + pair.Key.name + "=" + pair.Value);
            }
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Hub") Fail("footsteps left Hub");
            Destroy(reference);
        }
        private IEnumerator Carry()
        {
            foreach (var bot in FindObjectsByType<CarryItemDebugBot>(FindObjectsSortMode.None)) bot.enabled = false;
            yield return new WaitForSeconds(3);
            yield return CarryFall(false);
            yield return new WaitForSeconds(8);
            int id = (int)NetworkManager.Singleton.LocalClientId;
            yield return new WaitForSeconds(id * 6);
            yield return CarryFall(true);
            // Keep the host and earlier clients present while the last client tests.
            yield return new WaitForSeconds(24 - id * 6);
        }
        private IEnumerator CarryFall(bool expectLive)
        {
            var avatar = SessionScoreboard.Current.LocalPlayer.Avatar;
            var input = avatar.GetComponent<PlayerInputReader>(); input.EngageAutopilot(); input.DriveMove(Vector2.zero);
            var view = game.GetComponent<CarryItemRespawnPresentation>();
            var camera = FindFirstObjectByType<MinigameCameraController>();
            if (view == null || camera == null) { Fail("carry presentation missing"); yield break; }
            var renderers = avatar.GetComponentsInChildren<Renderer>(true);
            var hiddenBefore = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) hiddenBefore[i] = renderers[i].forceRenderingOff;
            var initial = avatar.Position;
            avatar.TeleportTo(new Vector3(initial.x,-8,initial.z),avatar.transform.rotation);
            float start = Time.realtimeSinceStartup;
            bool waited = false, sawView = false, sawLive = false, sawFallback = false;
            var seconds = new HashSet<int>();
            while (Time.realtimeSinceStartup-start < 10 && (avatar.Position.y < -4 || !avatar.enabled || avatar.MovementLocked || view.IsWaiting))
            {
                waited |= !avatar.enabled && avatar.MovementLocked;
                if (view.IsWaiting)
                {
                    sawView = true; sawLive |= view.HasLiveTarget;
                    sawFallback |= camera.CurrentTarget != null && camera.CurrentTarget.name == "RespawnArenaView";
                    seconds.Add(view.SecondsLeft);
                    if (camera.CurrentTarget == avatar.CameraTarget) Fail("carry still watches fallen self");
                    if (input.enabled) Fail("carry spectator input not blocked");
                    for (int i = 0; i < renderers.Length; i++)
                        if (!renderers[i].forceRenderingOff) { Fail("carry fallen renderer visible"); break; }
                    var panel = (GameObject)typeof(CarryItemRespawnPresentation).GetField("panel", Private).GetValue(view);
                    if (panel == null || !panel.activeInHierarchy) Fail("carry countdown panel hidden");
                }
                yield return null;
            }
            float duration = Time.realtimeSinceStartup-start;
            if (!waited || duration < 4.8f || duration > 7.5f || !avatar.enabled || avatar.MovementLocked)
                Fail($"carry recovery duration={duration:F2} waited={waited} enabled={avatar.enabled} locked={avatar.MovementLocked} y={avatar.Position.y}");
            if (!sawView || seconds.Count < 3 || (expectLive && !sawLive) || (!expectLive && !sawFallback))
                Fail($"carry view={sawView} countdownChanges={seconds.Count} live={sawLive} fallback={sawFallback} expectLive={expectLive}");
            if (view.IsWaiting || camera.CurrentTarget != avatar.CameraTarget || !input.enabled) Fail("carry camera/input did not return");
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i].forceRenderingOff != hiddenBefore[i]) { Fail("carry renderer not restored"); break; }
            Debug.Log($"PLAYTEST_CHECK carry returned in {duration:F2}s live={sawLive} fallback={sawFallback} countdownChanges={seconds.Count}");
            var before = avatar.Position; input.DriveMove(Vector2.up); yield return new WaitForSeconds(1); input.DriveMove(Vector2.zero);
            if (Vector3.Distance(before,avatar.Position)<1)
                Fail($"carry movement not restored from={before} to={avatar.Position} kinematic={avatar.GetComponent<Rigidbody>().isKinematic} input={input.MoveInput} suspended={input.Suspended} motor={avatar.enabled} lock={avatar.MovementLocked} bot={avatar.GetComponent<CarryItemDebugBot>().enabled}");
        }
        private IEnumerator Infection()
        {
            foreach (var bot in FindObjectsByType<DebugPlayerBot>(FindObjectsSortMode.None)) bot.enabled = false;
            var local = SessionScoreboard.Current.LocalPlayer.Avatar;
            var input = local.GetComponent<PlayerInputReader>(); input.EngageAutopilot(); input.DriveMove(Vector2.zero);
            yield return new WaitForSeconds(3);
            var items = FindObjectsByType<PickupItem>(FindObjectsSortMode.None);
            Array.Sort(items,(a,b)=>string.CompareOrdinal(a.name,b.name));
            if(items.Length!=6){Fail("infection props count="+items.Length);yield break;}
            int index=(int)NetworkManager.Singleton.LocalClientId%items.Length;
            var item=items[index]; var target=item.transform.position;
            local.TeleportTo(target+new Vector3(0,.2f,-.65f),Quaternion.identity);
            yield return new WaitForSeconds(1);
            input.DriveInteract(); yield return new WaitForSeconds(2);
            var carry=local.GetComponent<PlayerCarryAbility>();
            if(!item.IsHeld || !carry.IsCarrying) Fail("infection pickup failed id="+index);
            else Debug.Log("PLAYTEST_CHECK infection held "+item.name);
            yield return new WaitForSeconds(2);
            carry.Throw(); yield return new WaitForSeconds(2);
            if(item.IsHeld || carry.IsCarrying) Fail("infection throw failed");
            if(NetworkManager.Singleton.IsServer && item.GetComponent<Rigidbody>().isKinematic) Fail("server prop is not physical");
            if(!NetworkManager.Singleton.IsServer && !item.GetComponent<Rigidbody>().isKinematic) Fail("client prop simulates physics");
        }
        private IEnumerator Exam()
        {
            var exam=(ExamMinigame)game;
            var stage=game.GetComponent<MinigameStageState>();
            var camera=FindFirstObjectByType<MinigameCameraController>();
            int maxQuestion=0, samples=0;
            float deadline=Time.realtimeSinceStartup+180;
            while(game!=null && game.Phase==MinigamePhase.Round && Time.realtimeSinceStartup<deadline)
            {
                var match=typeof(ExamMinigame).GetField("match",Private).GetValue(exam);
                int host=(int)match.GetType().GetField("HostPlayerId").GetValue(match);
                int question=(int)match.GetType().GetField("QuestionNumber").GetValue(match);
                maxQuestion=Mathf.Max(maxQuestion,question);
                if(host>=0 && host!=SessionScoreboard.Current.LocalPlayer.Id && stage.Stage>=2 && stage.Stage<=4)
                {
                    samples++;
                    if(camera.CurrentMode!=CameraMode.ThirdPerson) {Fail("answering player camera switched to "+camera.CurrentMode);yield break;}
                }
                if(maxQuestion>=3 && samples>60) break;
                yield return null;
            }
            if(maxQuestion<3 || samples<=60) Fail("exam insufficient answering phases");
            else Debug.Log("PLAYTEST_CHECK exam stable samples="+samples+" questions="+maxQuestion);
        }
        private IEnumerator Cans()
        {
            var cans=(CansOrderMinigame)game;
            foreach(var bot in FindObjectsByType<CansOrderDebugBot>(FindObjectsSortMode.None)) bot.enabled=false;
            float readyDeadline=Time.realtimeSinceStartup+10;
            while(cans.Round.CanCount<=0 && Time.realtimeSinceStartup<readyDeadline) yield return null;
            if(cans.Round.CanCount<=0){Fail("cans state not received");yield break;}
            // Known fixture lives only in this opt-in probe; normal clients never receive the answer.
            var solution=new List<int>(); for(int i=0;i<cans.Round.CanCount;i++)solution.Add(i);
            if(NetworkManager.Singleton.IsServer)
            {
                var secret=(List<int>)typeof(CansOrderMinigame).GetField("solution",Private).GetValue(cans);
                secret.Clear();secret.AddRange(solution);
            }
            int localId=SessionScoreboard.Current.LocalPlayer.Id;
            int client=(int)NetworkManager.Singleton.LocalClientId;
            bool submitted=false, results=false;
            cans.ResultsReported += value =>
            {
                results=true;
                for(int i=0;i<value.Entries.Count;i++)
                    Debug.Log("PLAYTEST_CHECK cans result="+value.Entries[i].PlayerId+":"+value.Entries[i].Place);
                for(int i=0;i<cans.ContestantCount;i++)
                    if(cans.TryGetEntry(i,out var e,out _) && e.PlayerId==localId)
                    {
                        int expected=client==1?1:client==0?2:client==3?3:4;
                        int at=value.IndexOf(localId);
                        if(at<0 || value.Entries[at].Place!=expected)Fail("cans place expected="+expected);
                    }
            };
            float deadline=Time.realtimeSinceStartup+190;
            int checkedCircle=-1;
            int loggedStage=-1;
            while(!results && game!=null && Time.realtimeSinceStartup<deadline)
            {
                if(cans.Round.Round>1){Fail("cans second round");break;}
                if(loggedStage!=cans.Stage)
                {
                    loggedStage=cans.Stage;
                    var player=SessionScoreboard.Current.LocalPlayer.Avatar;
                    Debug.Log($"PLAYTEST_CHECK cans stage={cans.Stage} circle={cans.Round.Circle} local={localId} contestants={cans.ContestantCount} pos={player.Position} enabled={player.enabled} locked={player.MovementLocked}");
                }
                if(!submitted && client!=2 && cans.Stage==2)
                {
                    float window=cans.GetComponent<MinigameStageState>().StageDuration-cans.GetComponent<MinigameStageState>().StageRemaining;
                    float delay=client==1?1:client==0?3:5;
                    if(window>delay)
                    {
                        var contestants=(IList)typeof(CansOrderMinigame).GetField("contestants",Private).GetValue(cans);
                        foreach(var entry in contestants)
                        {
                            var field=entry.GetType().GetField("Entry");var e=(CansOrderEntry)field.GetValue(entry);
                            if(e.PlayerId!=localId)continue;
                            var shelf=(CanShelf)entry.GetType().GetField("Shelf").GetValue(entry);
                            var button=(CanConfirmButton)entry.GetType().GetField("Button").GetValue(entry);
                            shelf.SetArrangement(solution);
                            Debug.Log($"PLAYTEST_CHECK cans submit local={localId} window={button.WindowOpen} solved={button.Solved} accepted={button.Accepted} slots={shelf.SlotCount} expected={solution.Count}");
                            button.Interact(SessionScoreboard.Current.LocalPlayer.Avatar);submitted=true;
                        }
                    }
                }
                if(cans.Stage==CansOrderMinigame.StageReveal && checkedCircle!=cans.Round.Circle)
                {
                    checkedCircle=cans.Round.Circle;
                    for(int i=0;i<cans.ContestantCount;i++)
                        if(cans.TryGetEntry(i,out var e,out _) && e.Confirmed && e.Solved && e.Matches!=cans.Round.CanCount)
                            Fail("cans solved matches="+e.Matches);
                }
                yield return null;
            }
            if(!results)Fail("cans did not finish");
        }

        private IEnumerator Memory()
        {
            var memory=(MemoryRunMinigame)game;
            var board=GameObject.Find("_Arena/TurnBoard");
            var labels=board!=null?board.GetComponentsInChildren<TMPro.TextMeshPro>():null;
            if(labels==null || labels.Length!=2){Fail("memory world board missing");yield break;}
            var walkers=new HashSet<int>();
            float until=Time.realtimeSinceStartup+20, nextFall=Time.realtimeSinceStartup+4;
            while(Time.realtimeSinceStartup<until && game!=null && game.Phase==MinigamePhase.Round)
            {
                yield return null;
                int walker=memory.CurrentWalkerId;
                if(walker<0) continue;
                walkers.Add(walker);
                if(labels[0].text!=labels[1].text || !labels[0].text.Contains(memory.DisplayNameOf(walker)))
                {Fail("memory queue differs from replicated turn");yield break;}
                if(NetworkManager.Singleton.IsServer && Time.realtimeSinceStartup>=nextFall && memory.CurrentWalker!=null)
                {
                    nextFall=Time.realtimeSinceStartup+4;
                    var avatar=memory.CurrentWalker;
                    avatar.RequestTeleport(new Vector3(avatar.Position.x,-8,avatar.Position.z),avatar.transform.rotation);
                }
            }
            if(walkers.Count<3)Fail("memory queue did not rotate");
            else Debug.Log("PLAYTEST_CHECK memory queue rotations="+walkers.Count);
        }
        private void Fail(string reason){failed=true;Debug.LogError("PLAYTEST_CHECK FAIL "+reason);}
    }
}
