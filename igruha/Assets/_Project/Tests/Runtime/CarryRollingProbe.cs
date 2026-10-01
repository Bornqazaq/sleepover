using System.Collections;
using System.Collections.Generic;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.CarryItem;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>Opt-in real input + real floor regression: 1–4 holders, full cart, host/client.</summary>
    public sealed class CarryRollingProbe : MonoBehaviour
    {
        private CarryItemMinigame game;
        private WaterCart cart;
        private PlayerController local;
        private PlayerInputReader reader;
        private readonly List<SessionPlayer> team = new List<SessionPlayer>();
        private bool driving, holding, failed;
        private bool rawForward;
        private Vector3 driveDirection = Vector3.right;
        private int monitoredCount, contactSamples;
        private float worstContact;
        private float worstBodyOffset;
        private int localSlot = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--carry-rolling-check", out _)) return;
            var root = new GameObject(nameof(CarryRollingProbe)); DontDestroyOnLoad(root);
            root.AddComponent<CarryRollingProbe>();
        }

        private void Update()
        {
            if (reader == null) return;
            reader.DriveMove(driving ? (rawForward ? Vector2.up : local.WorldToMoveInput(driveDirection)) : Vector2.zero);
        }

        private void LateUpdate()
        {
            for (int i = 0; i < monitoredCount; i++)
            {
                var pose = team[i].Avatar.GetComponent<WaterCartGripPose>();
                // Ordinary movement and turns must keep the attachment.
                if (!cart.Carry.IsCarriedBy(team[i].Avatar)) { worstContact = float.PositiveInfinity; continue; }
                if (pose == null) { worstContact = float.PositiveInfinity; continue; }
                if (pose.Weight < 0.99f) continue;
                if (pose.MaxPalmError > Mathf.Max(0.06f, worstContact * 1.5f))
                {
                    game.TryGetRoundTime(out float remaining, out float duration);
                    int slot = -1;
                    for (int s = 0; s < cart.Carry.HandleCount; s++) if (cart.Carry.CarrierAt(s) == team[i].Avatar) slot = s;
                    Debug.Log("CARRY_ROLLING_CHECK contact peak t=" + (duration - remaining) +
                        " player=" + team[i].Id + " gap=" + pose.MaxPalmError + " stretch=" + cart.Carry.StretchOf(slot) +
                        " root=" + team[i].Avatar.transform.position + " cart=" + cart.transform.position +
                        " station=" + cart.Carry.StationOf(slot));
                }
                worstContact = Mathf.Max(worstContact, pose.MaxPalmError);
                if (team[i].Avatar == local) worstBodyOffset = Mathf.Max(worstBodyOffset, pose.BodyOffset);
                contactSamples++;
            }
        }

        private IEnumerator Start()
        {
            float deadline = Time.realtimeSinceStartup + 100f;
            bool ready = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                var nm = NetworkManager.Singleton;
                var selection = CharacterSelection.Current;
                if (nm != null && nm.IsConnectedClient && selection != null && !selection.HasChosen &&
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Hub")
                {
                    selection.ReportReady();
                    for (int i = 0; i < 8; i++)
                    {
                        int slot = ((int)nm.LocalClientId + i) % 8;
                        if (!selection.IsTaken(slot)) { selection.Choose(slot); break; }
                    }
                }
                game = MinigameControllerBase.Current as CarryItemMinigame;
                if (game != null && game.AwaitingTutorialReady && !ready)
                { ready = true; game.ToggleTutorialReady(); }
                if (game != null && game.Phase == MinigamePhase.Round && !game.StartCountdownActive) break;
                yield return null;
            }
            if (game == null || game.Phase != MinigamePhase.Round)
            { Check(false, "round timeout"); Application.Quit(1); yield break; }
            yield return new WaitForSeconds(2f);
            var manager = NetworkManager.Singleton;
            foreach (var entry in SessionScoreboard.Current.Players)
            {
                if (game.TeamOfPlayer(entry.Id) == TeamSide.A) team.Add(entry);
                if (entry.Avatar != null && entry.Avatar.GetComponent<NetworkObject>().IsOwner) local = entry.Avatar;
            }
            team.Sort((a, b) => a.Id.CompareTo(b.Id));
            for (int i = 0; i < team.Count; i++) if (team[i].Avatar == local) localSlot = i;
            Check(local != null && team.Count >= 4, "eight players and four teammates");
            if (failed) { Application.Quit(1); yield break; }
            reader = local.GetComponent<PlayerInputReader>(); reader.EngageAutopilot();
            cart = game.CartOf(TeamSide.A);
            WatchCart(cart);
            for (int pass = 0; pass < 8; pass++)
            {
                TeamSide side = pass < 4 ? TeamSide.A : TeamSide.B;
                int count = pass % 4 + 1;
                if (pass == 4)
                {
                    team.Clear(); localSlot = -1;
                    foreach (var entry in SessionScoreboard.Current.Players)
                        if (game.TeamOfPlayer(entry.Id) == side) team.Add(entry);
                    team.Sort((a, b) => a.Id.CompareTo(b.Id));
                    for (int i = 0; i < team.Count; i++) if (team[i].Avatar == local) localSlot = i;
                    cart = game.CartOf(side);
                    WatchCart(cart);
                }
                // Round timer is replicated, so all owners drive the same time window.
                float start = 12f + pass * 17f;
                yield return WaitElapsed(start);
                holding = driving = false;
                if (manager.IsServer)
                {
                    // The preceding team's cart and released players otherwise remain
                    // in this turn's path. Their collisions test obstruction, not free rolling.
                    TeamSide otherSide = side == TeamSide.A ? TeamSide.B : TeamSide.A;
                    game.CartOf(otherSide).Carry.ResetPose(new Vector3(-24f, 0.03f, -6f), Quaternion.identity);
                    int parkedIndex = 0;
                    foreach (var entry in SessionScoreboard.Current.Players)
                        if (game.TeamOfPlayer(entry.Id) != side)
                            entry.Avatar.RequestTeleport(new Vector3(-24f + parkedIndex++ * 1.1f, 0f, -10f), Quaternion.identity);
                    // Leave room for direct-control acceleration, coasting and the front holders
                    // before the pit. The cabin at x=-25 remains outside the rear stations.
                    cart.Carry.ResetPose(new Vector3(count == 1 ? -17.5f : -23f, 0.03f, 4.8f), Quaternion.Euler(0f, 90f, 0f));
                    cart.SetHandleCount(count);
                    cart.ChangeWater(game.Config.CartCapacity, WaterLossReason.Filled);
                    for (int i = 0; i < team.Count; i++)
                        team[i].Avatar.RequestTeleport(i < count ? cart.Carry.StationOf(i) : new Vector3(-25f, 0f, 8f + i), Quaternion.Euler(0f, 90f, 0f));
                }
                yield return WaitElapsed(start + 1.5f);
                holding = localSlot >= 0 && localSlot < count;
                if (holding) yield return Tap();
                yield return WaitElapsed(start + 2.6f);
                Check(cart.Carry.CarrierCount == count, "E grab count=" + count + " actual=" + cart.Carry.CarrierCount);
                CheckCarrierCollisions(count, true);
                CheckPose(count, "stationary " + side);
                CheckInputHud(count, false, false);
                // Peers observe server time with a small offset. Finish the idle check
                // everywhere before the first owner begins moving.
                yield return WaitElapsed(start + 3f);
                Vector3 before = cart.transform.position;
                driveDirection = Vector3.right;
                rawForward = count == 1;
                worstContact = 0f; worstBodyOffset = 0f; contactSamples = 0; monitoredCount = count;
                driving = holding;
                yield return WaitElapsed(start + 5f);
                CheckInputHud(count, true, false);
                yield return WaitElapsed(start + 5.5f);
                driving = false;
                CheckPose(count, "moving " + side);
                Check(Vector3.Distance(cart.transform.position, before) > 1f, "roll count=" + count + " moved=" + Vector3.Distance(cart.transform.position, before));
                Check(cart.Carry.CarrierCount == count, "retain count=" + count);
                Check(cart.Water == game.Config.CartCapacity, "straight movement preserves water count=" + count + " water=" + cart.Water);
                Debug.Log("CARRY_ROLLING_CHECK steady tilt=" + cart.Carry.TiltAngle + " cause=" + cart.Stability.State.Cause);
                for (int s = 0; s < count; s++) Debug.Log("CARRY_ROLLING_CHECK tension id=" + team[s].Id +
                    " owner=" + (team[s].Avatar == local) + " slot=" + s + " stretch=" + cart.Carry.TensionAt(s).ToString("F3") +
                    " input=" + cart.Carry.CarrierIntentAt(s) + " velocity=" + cart.Carry.CarrierVelocityAt(s));
                monitoredCount = 0;
                // Server time trails on clients: separate observations from the next input.
                yield return WaitElapsed(start + 7f);
                if (holding) yield return Tap();
                yield return WaitElapsed(start + 7.7f);
                Check(cart.Carry.CarrierCount == 0, "second E releases count=" + count);
                CheckInputHud(0, false, false);
                CheckCarrierCollisions(count, false);
                yield return WaitElapsed(start + 8.2f);
                if (holding) yield return Tap();
                yield return WaitElapsed(start + 9f);
                Check(cart.Carry.CarrierCount == count, "third E regrabs count=" + count);
                monitoredCount = count;
                yield return WaitElapsed(start + 9.5f);
                rawForward = false;
                driveDirection = Vector3.forward; driving = holding;
                yield return WaitElapsed(start + 10.5f);
                driveDirection = Vector3.left;
                yield return WaitElapsed(start + 11.5f);
                driving = false;
                yield return WaitElapsed(start + 12f);
                monitoredCount = 0;
                Check(contactSamples > 30 && worstContact < 0.06f,
                    "continuous grip " + side + " count=" + count + " samples=" + contactSamples + " worst=" + worstContact);
                Check(worstBodyOffset < 0.39f, "body stays at camera target " + side + " offset=" + worstBodyOffset);
                yield return WaitElapsed(start + 12.5f);
                if (holding && cart.Carry.IsCarriedBy(local)) yield return Tap();
                holding = false;
                yield return WaitElapsed(start + 14f);
                Check(cart.Carry.CarrierCount == 0, "release count=" + count);
                for (int i = 0; i < count; i++)
                {
                    var pose = team[i].Avatar.GetComponent<WaterCartGripPose>();
                    Check(pose != null && pose.Weight == 0f, "relaxed hands " + team[i].Id);
                }
            }
            // Opposed commands cancel translation but excite the suspended tank and its water.
            yield return WaitElapsed(151f);
            if (manager.IsServer)
            {
                cart.Carry.ResetPose(new Vector3(-20f, 0.03f, 4.8f), Quaternion.Euler(0f, 90f, 0f));
                cart.SetHandleCount(2);
                cart.ChangeWater(game.Config.CartCapacity, WaterLossReason.Filled);
                cart.Stability.ResetTrip();
                for (int i = 0; i < team.Count; i++)
                    team[i].Avatar.RequestTeleport(i < 2 ? cart.Carry.StationOf(i) : new Vector3(-25f, 0f, 8f + i), Quaternion.identity);
            }
            yield return WaitElapsed(152.5f);
            holding = localSlot >= 0 && localSlot < 2;
            if (holding) yield return Tap();
            yield return WaitElapsed(154f);
            Check(cart.Carry.CarrierCount == 2, "opposite input grab");
            Vector3 parked = cart.transform.position;
            int beforeDisagreement = cart.Water;
            rawForward = false;
            driveDirection = localSlot == 0 ? Vector3.right : Vector3.left;
            driving = holding;
            yield return WaitElapsed(155.2f);
            CheckInputHud(2, true, true);
            bool sawRock = false, sawDisagreementFlow = false, sawDisagreementSheet = false;
            float untilRock = Time.time + 2.8f;
            while (Time.time < untilRock)
            {
                var state = cart.Stability.State;
                sawRock |= state.BodySlope.magnitude > 0.04f;
                sawDisagreementFlow |= state.Outflow > 0 && state.Cause == CartTiltCause.Disagreement;
                var sheet = cart.GetComponent<CartOverflowVisual>();
                sawDisagreementSheet |= sheet != null && sheet.HasFallingWater;
                yield return null;
            }
            Check(sawRock && sawDisagreementFlow && sawDisagreementSheet, "replicated disagreement rocking, overflow and falling water");
            Check(cart.Water < beforeDisagreement && cart.Water > beforeDisagreement - 80, "gradual disagreement loss water=" + cart.Water);
            yield return WaitElapsed(159f);
            driving = false;
            Check(cart.Carry.CarrierCount == 2, "opposed commands keep both grips");
            Check(Vector3.Distance(cart.transform.position, parked) < 0.4f, "opposed commands stop the cart");
            CheckPose(2, "rocking keeps hands on fixed grips");
            yield return WaitElapsed(160f);
            if (holding && cart.Carry.IsCarriedBy(local)) yield return Tap();
            yield return WaitElapsed(161f);
            Check(cart.Carry.CarrierCount == 0, "opposite input release");
            CheckInputHud(0, false, false);
            CheckCarrierCollisions(2, false);
            if (manager.IsServer)
            {
                cart.ChangeWater(game.Config.CartCapacity, WaterLossReason.Filled);
                cart.Stability.ResetTrip();
            }
            yield return WaitElapsed(162f);
            int beforeImpact = cart.Water;
            if (manager.IsServer) cart.Stability.Impact(cart.transform.right);
            bool sawWave = false, sawFlow = false, sawSheet = false;
            float until = Time.time + 3f;
            while (Time.time < until)
            {
                var state = cart.Stability.State;
                sawWave |= state.Wave.sqrMagnitude > 0.01f;
                sawFlow |= state.Outflow > 0f;
                var sheet = cart.transform.Find("OverflowSheet");
                sawSheet |= sheet != null && sheet.GetComponent<Renderer>().enabled;
                yield return null;
            }
            Check(sawWave && sawFlow && sawSheet, "replicated wave, rim flow and visible sheet");
            Check(cart.Water < beforeImpact && cart.Water > beforeImpact - 35, "impact loses visible overflow water=" + cart.Water);
            yield return WaitElapsed(168f);
            Check(cart.Stability.State.Outflow == 0f, "overflow settles on every peer");
            // Drive all four actual owners over the authored central shortcut.
            yield return WaitElapsed(172f);
            if (manager.IsServer)
            {
                cart.Carry.ResetPose(new Vector3(-3.2f,.03f,.4f),Quaternion.Euler(0,90,0));
                cart.SetHandleCount(4);cart.ChangeWater(game.Config.CartCapacity,WaterLossReason.Filled);cart.Stability.ResetTrip();
                for(int i=0;i<team.Count;i++)team[i].Avatar.RequestTeleport(cart.Carry.StationOf(i),Quaternion.Euler(0,90,0));
            }
            yield return WaitElapsed(174f);
            holding=localSlot>=0;rawForward=false;driveDirection=Vector3.right;
            if(holding)yield return Tap();
            yield return WaitElapsed(176f);
            Check(cart.Carry.CarrierCount==4,"road four owners attached");
            monitoredCount=4;worstContact=0;worstBodyOffset=0;driving=holding;
            bool roadHop=false,roadSlope=false,roadFlow=false,roadSheet=false;
            while(RoundElapsed()<181f)
            {
                var state=cart.Stability.State;
                roadHop|=state.RoadHop>.006f;roadSlope|=state.BodySlope.magnitude>.025f;
                roadFlow|=state.Cause==CartTiltCause.Road&&state.Outflow>0;
                roadSheet|=cart.GetComponent<CartOverflowVisual>().HasFallingWater;
                yield return null;
            }
            driving=false;monitoredCount=0;
            Check(cart.transform.position.x>5.8f,"road shortcut crossed x="+cart.transform.position.x);
            Check(roadHop&&roadSlope&&roadFlow&&roadSheet,"replicated road suspension, cause and falling water");
            Check(cart.Water<145&&cart.Water>70,"road gradual loss water="+cart.Water);
            Check(cart.Carry.CarrierCount==4&&worstContact<.06f,"road grips stable gap="+worstContact);
            yield return WaitElapsed(187f);
            Check(cart.Stability.State.Outflow==0f&&cart.Stability.State.BodySlope.magnitude<.01f,"road settles after shortcut");
            if (!failed) Debug.Log("CARRY_ROLLING_CHECK PASS id=" + manager.LocalClientId);
            yield return new WaitForSeconds(2f);
            Application.Quit(failed ? 1 : 0);
        }

        private float RoundElapsed()
        { game.TryGetRoundTime(out float remaining,out float duration);return duration-remaining; }

        private IEnumerator Tap()
        {
            // Shorter than the shared InputAction's Hold threshold: the actual raw E path.
            reader.DriveInteractHold(true);
            yield return new WaitForSeconds(0.08f);
            reader.DriveInteractHold(false);
        }

        private void WatchCart(WaterCart value)
        {
            value.Carry.HandleReleased += (slot, player, reason) =>
                Debug.Log("CARRY_ROLLING_CHECK released slot=" + slot + " reason=" + reason);
        }

        private void CheckInputHud(int count, bool moving, bool opposed)
        {
            var hud = game.CoordinationHud;
            bool carrying = cart.Carry.IsCarriedBy(local);
            Check(hud != null && hud.Visible == carrying, "input HUD visibility count=" + count);
            for (int slot = 0; slot < count; slot++)
            {
                var input = cart.Carry.CarrierInputAt(slot);
                Check(moving ? input.World.magnitude > 0.9f && input.Move.magnitude > 0.9f :
                    input.World.magnitude < 0.1f && input.Move.magnitude < 0.1f, "replicated controls slot=" + slot + " moving=" + moving);
                if (opposed)
                    Check(Vector3.Dot(input.World, slot == 0 ? Vector3.right : Vector3.left) > 0.9f,
                        "opposing world direction slot=" + slot);
            }
            if (!carrying || hud == null) return;
            Check(hud.MemberCount == count, "HUD members count=" + count);
            Check(opposed ? hud.MeanDirection.magnitude < 0.12f : !moving || hud.MeanDirection.magnitude > 0.9f,
                "HUD mean direction matches commands");
            int localMembers = 0;
            for (int i = 0; i < hud.MemberCount; i++)
            {
                var member = hud.MemberAt(i);
                if (member.IsLocal) { localMembers++; Check(member.Player == local, "HUD YOU is the local owner"); }
                Check(member.Relation == (opposed ? CartInputRelation.Opposing : moving ? CartInputRelation.Together : CartInputRelation.Idle),
                    "HUD direction relation member=" + i + " relation=" + member.Relation);
            }
            Check(localMembers == 1, "one local carrier on HUD");
        }

        private void CheckCarrierCollisions(int count, bool attached)
        {
            for (int i = 0; i < count; i++)
            {
                var detector = team[i].Avatar.GetComponent<StuckDetector>();
                Check(detector != null && detector.enabled != attached,
                    "stuck detector " + i + " attached=" + attached);
            }
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    Check(Physics.GetIgnoreCollision(team[i].Avatar.GetComponent<CapsuleCollider>(),
                        team[j].Avatar.GetComponent<CapsuleCollider>()) == attached,
                        "carrier collisions " + i + "/" + j + " attached=" + attached);
        }

        private void CheckPose(int count, string phase)
        {
            for (int i = 0; i < count; i++)
            {
                var pose = team[i].Avatar.GetComponent<WaterCartGripPose>();
                Check(pose != null && pose.Weight > 0.99f && pose.MaxPalmError < 0.06f,
                    "grip " + phase + " player=" + team[i].Id + " error=" + (pose != null ? pose.MaxPalmError : -1f));
            }
        }

        private IEnumerator WaitElapsed(float target)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (game.TryGetRoundTime(out float remaining, out float duration) && duration - remaining >= target) yield break;
                yield return null;
            }
            Check(false, "timer timeout");
        }

        private void Check(bool success, string message)
        {
            if (success) Debug.Log("CARRY_ROLLING_CHECK " + message);
            else { failed = true; Debug.LogError("CARRY_ROLLING_CHECK FAIL " + message); }
        }
    }
}
