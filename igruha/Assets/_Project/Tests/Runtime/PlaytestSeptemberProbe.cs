using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.CameraSystems;
using Igruha.Core.Items;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.CansOrder;
using Igruha.Minigames.CarryItem;
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
            float deadline = Time.realtimeSinceStartup + 150;
            while (Time.realtimeSinceStartup < deadline)
            {
                game = MinigameControllerBase.Current;
                if (game != null && game.Phase == MinigamePhase.Round) break;
                yield return null;
            }
            if (game == null || game.Phase != MinigamePhase.Round) { Fail("round timeout"); Application.Quit(1); yield break; }
            LaunchArguments.TryGetValue("--playtest-check", out string mode);
            if (mode == "carry") yield return Carry();
            else if (mode == "infection") yield return Infection();
            else if (mode == "exam") yield return Exam();
            else if (mode == "cans") yield return Cans();
            else if (mode == "memory") yield return Memory();
            else if (mode == "tutorial") yield return new WaitForSeconds(1);
            else Fail("unknown mode");
            if (!failed) Debug.Log("PLAYTEST_CHECK PASS mode=" + mode + " id=" + NetworkManager.Singleton.LocalClientId);
            yield return new WaitForSecondsRealtime(NetworkManager.Singleton.IsServer ? 12 : 4);
            Application.Quit(failed ? 1 : 0);
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
