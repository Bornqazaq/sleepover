using System.Collections;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Minigames.CarryItem;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>Real owner input on the authored roads, with replicated chassis and crane contacts.</summary>
    public sealed class CarrySlopeProbe : MonoBehaviour
    {
        private CarryItemMinigame game;
        private NetworkManager net;
        private PlayerController local;
        private PlayerInputReader input;
        private WaterCart cart;
        private Vector3[] path;
        private int next, stage;
        private bool driving, failed, arrived;
        private float maxPitch, minHeight, maxHeight, maxGripError, maxWheelGap, maxSupportError;
        private float nextTelemetry;
        private int heightSamples;
        private float pauseUntil;
        private bool pausedOnRamp, parkedSampled;
        private Vector3 parkedPosition;
        private string screenshots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--carry-slope-check", out _)) return;
            var go = new GameObject(nameof(CarrySlopeProbe)); DontDestroyOnLoad(go);
            go.AddComponent<CarrySlopeProbe>();
        }

        private void Update()
        {
            if (input == null) return;
            Vector2 move = Vector2.zero;
            if (driving && !arrived)
            {
                Vector3 delta = path[next] - cart.transform.position; delta.y = 0;
                while (next < path.Length - 1 && delta.magnitude < .7f)
                { next++; delta = path[next] - cart.transform.position; delta.y = 0; }
                arrived = next == path.Length - 1 && delta.magnitude < .6f;
                float strength = 1f;
                if (next == path.Length - 1) strength = Mathf.Clamp(delta.magnitude / 2, .3f, 1f);
                else if (delta.magnitude < 2.8f)
                {
                    Vector3 after = path[next + 1] - path[next]; after.y = 0;
                    if (Vector3.Angle(delta, after) > 20) strength = .4f;
                }
                if (!arrived) move = local.WorldToMoveInput(delta) * strength;
                if (!cart.Carry.IsCarriedBy(local)) { Check(false, "grip lost stage=" + stage); driving = false; }
                float x = Mathf.Abs(cart.transform.position.x);
                if (stage < 2 && !pausedOnRamp && x < 12.5f && x > 11.5f)
                {
                    pausedOnRamp = true; pauseUntil = Time.realtimeSinceStartup + 2f;
                    if (!string.IsNullOrEmpty(screenshots))
                        ScreenCapture.CaptureScreenshot(screenshots + "-slope-" + stage + ".png");
                }
                if (pausedOnRamp && Time.realtimeSinceStartup < pauseUntil)
                {
                    move = Vector2.zero;
                    if (!parkedSampled && Time.realtimeSinceStartup > pauseUntil - .5f)
                    { parkedSampled = true; parkedPosition = cart.transform.position; }
                }
                else if (parkedSampled)
                {
                    Check(Vector3.Distance(parkedPosition, cart.transform.position) < .05f, "parked on slope stage=" + stage);
                    parkedSampled = false;
                }
                if (stage < 2 && x > 8 && x < 16)
                {
                    maxPitch = Mathf.Max(maxPitch, Vector3.Angle(cart.transform.up, Vector3.up));
                    float grade = CarryRouteLayout.UpperHeight / (CarryRouteLayout.UpperEnd - CarryRouteLayout.UpperFlat);
                    Vector3 normal = new Vector3(cart.transform.position.x < 0 ? -grade : grade, 1, 0).normalized;
                    maxSupportError = Mathf.Max(maxSupportError, Vector3.Angle(cart.transform.up, normal));
                    Vector3 roadPoint = cart.transform.position; roadPoint.y = CarryRouteLayout.UpperY(roadPoint.x);
                    for (int wheel = 0; wheel < 4; wheel++)
                    {
                        Vector3 center = cart.transform.TransformPoint(new Vector3(
                            wheel % 2 == 0 ? -CartWheelSupport.HalfTrack : CartWheelSupport.HalfTrack,
                            CartWheelSupport.Radius, wheel < 2 ? CartWheelSupport.HalfBase : -CartWheelSupport.HalfBase));
                        maxWheelGap = Mathf.Max(maxWheelGap, Mathf.Abs(Vector3.Dot(center - roadPoint, normal) - CartWheelSupport.Radius));
                    }
                    // Height residual on the straight ramp reveals alternating lift/drop
                    // even when the whole body is legitimately gaining height.
                    float residual = local.transform.position.y - CarryRouteLayout.UpperY(local.transform.position.x);
                    minHeight = Mathf.Min(minHeight, residual); maxHeight = Mathf.Max(maxHeight, residual); heightSamples++;
                    var pose = local.GetComponent<WaterCartGripPose>();
                    if (pose != null) maxGripError = Mathf.Max(maxGripError, pose.MaxPalmError);
                }
                if (Time.realtimeSinceStartup > nextTelemetry)
                {
                    nextTelemetry = Time.realtimeSinceStartup + 5;
                    var rb = local.GetComponent<Rigidbody>();
                    Debug.Log("CARRY_SLOPE progress stage=" + stage + " node=" + next + " pos=" + cart.transform.position +
                        " tilt=" + Vector3.Angle(cart.transform.up, Vector3.up) + " player=" + local.transform.position +
                        " velocity=" + rb.linearVelocity + " grounded=" + local.IsGrounded + "/" + local.IsStandingOnGround +
                        " gravity=" + rb.useGravity + " cartV=" + cart.Carry.FlatVelocity);
                }
            }
            input.DriveMove(move);
        }

        private IEnumerator Start()
        {
            LaunchArguments.TryGetValue("--carry-slope-screenshots", out screenshots);
            float deadline = Time.realtimeSinceStartup + 100;
            bool practiceChecked = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                net = NetworkManager.Singleton;
                var selection = CharacterSelection.Current;
                if (net != null && net.IsConnectedClient && selection != null && !selection.HasChosen &&
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Hub")
                { selection.ReportReady(); selection.Choose((int)net.LocalClientId); }
                game = MinigameControllerBase.Current as CarryItemMinigame;
                if (game != null && game.Phase == MinigamePhase.Practice && !game.StartCountdownActive && !practiceChecked)
                {
                    yield return new WaitForFixedUpdate();
                    CheckCrane("practice"); practiceChecked = true; game.ToggleTutorialReady();
                }
                if (game != null && game.Phase == MinigamePhase.Round && !game.StartCountdownActive) break;
                yield return null;
            }
            Check(practiceChecked && game != null && game.Phase == MinigamePhase.Round, "practice to round");
            if (failed) yield break;
            yield return new WaitForFixedUpdate();
            CheckCrane("round");
            foreach (var p in SessionScoreboard.Current.Players)
                if (p.Avatar != null && p.Avatar.GetComponent<NetworkObject>().IsOwner) local = p.Avatar;
            Check(local != null && SessionScoreboard.Current.Players.Count == 2, "two owners");
            if (failed) yield break;
            input = local.GetComponent<PlayerInputReader>(); input.EngageAutopilot();
            TeamSide side = game.TeamOfAvatar(local);
            cart = game.CartOf(side);
            int sign = side == TeamSide.A ? 1 : -1;
            // Full shortcut/gallery crews are covered separately by CarryRouteChoiceProbe.
            float[] begins = { 6, 28 };
            float[] ends = { 27, 49 };
            for (stage = 0; stage < begins.Length; stage++)
            {
                yield return At(begins[stage]);
                driving = false; arrived = false; maxPitch = maxGripError = maxWheelGap = maxSupportError = 0;
                pausedOnRamp = parkedSampled = false; pauseUntil = 0;
                minHeight = float.MaxValue; maxHeight = float.MinValue; heightSamples = 0;
                path = Route(stage, sign); next = 1;
                // Release replication must reach the owner before teleporting it.
                // Otherwise its previous tether pulls it through the new cart pose.
                if (net.IsServer)
                    foreach (var p in SessionScoreboard.Current.Players)
                        game.CartOf(game.TeamOfPlayer(p.Id)).Carry.ReleaseAll(Igruha.Core.Items.CarryReleaseReason.RoundEnded);
                yield return At(begins[stage] + .5f);
                if (net.IsServer)
                    foreach (var p in SessionScoreboard.Current.Players)
                    {
                        TeamSide team = game.TeamOfPlayer(p.Id);
                        var c = game.CartOf(team);
                        var route = Route(stage, team == TeamSide.A ? 1 : -1);
                        Vector3 heading = route[1] - route[0]; heading.y = 0;
                        c.SetHandleCount(1);
                        var settings = c.Carry.Settings;
                        Vector3 station = route[0] - heading.normalized * (settings.cartHandleBack + settings.carrierStandoff);
                        if (stage < 2) station.y = CarryRouteLayout.UpperY(station.x);
                        p.Avatar.RequestTeleport(station + Vector3.up * .04f, Quaternion.LookRotation(heading));
                    }
                // Let the remote kinematic avatar finish its teleport before putting
                // a dynamic cart beside it; its old interpolation sweep can kick a cart.
                yield return At(begins[stage] + 1f);
                if (net.IsServer)
                    foreach (var p in SessionScoreboard.Current.Players)
                    {
                        TeamSide team = game.TeamOfPlayer(p.Id);
                        var c = game.CartOf(team);
                        var route = Route(stage, team == TeamSide.A ? 1 : -1);
                        Vector3 heading = route[1] - route[0]; heading.y = 0;
                        c.Carry.ResetPose(route[0] + Vector3.up * .04f, Quaternion.LookRotation(heading));
                        c.ChangeWater(-c.Water, WaterLossReason.Poured); c.ChangeWater(150, WaterLossReason.Filled);
                        c.Stability.ResetTrip();
                    }
                yield return At(begins[stage] + 1.5f);
                Debug.Log("CARRY_SLOPE before grab stage=" + stage + " cart=" + cart.transform.position + " player=" + local.transform.position);
                input.DriveInteractHold(true); yield return new WaitForSeconds(.08f); input.DriveInteractHold(false);
                yield return At(begins[stage] + 2.5f);
                Check(cart.Carry.IsCarriedBy(local), "grab stage=" + stage);
                driving = true;
                yield return At(ends[stage]);
                driving = false;
                Check(arrived, "route completed stage=" + stage + " position=" + cart.transform.position);
                if (stage < 2)
                {
                    Check(heightSamples > 20 && maxPitch > 14 && maxPitch < 22, "replicated slope pitch=" + maxPitch);
                    Check(heightSamples > 20 && maxHeight - minHeight < .02f, "ramp vertical jitter=" + (maxHeight - minHeight));
                    Check(maxSupportError < .5f && maxWheelGap < .03f,
                        "wheel contacts gap=" + maxWheelGap + " slope error=" + maxSupportError);
                    Check(maxGripError < .12f, "ramp grip error=" + maxGripError);
                }
                Debug.Log("CARRY_SLOPE stage=" + stage + " arrived=" + arrived + " tilt=" + maxPitch +
                    " heightRange=" + (maxHeight - minHeight) + " water=" + cart.Water);
            }
            // Wheel support must not become an invisible bridge across the pit.
            if (net.IsServer)
                foreach (var p in SessionScoreboard.Current.Players)
                {
                    var c = game.CartOf(game.TeamOfPlayer(p.Id));
                    c.Carry.ResetPose(new Vector3(-9, 0, p.Id * 2 - 1), Quaternion.identity);
                }
            yield return At(51);
            Check(cart.transform.position.y < -1, "unsupported cart falls y=" + cart.transform.position.y);
            if (!failed) Debug.Log("CARRY_SLOPE PASS id=" + net.LocalClientId);
        }

        private static Vector3[] Route(int stage, int side)
        {
            float from = stage == 0 ? (side > 0 ? -19.5f : 5.2f) : (side > 0 ? -5.2f : 19.5f);
            float to = stage == 0 ? (side > 0 ? -5.2f : 19.5f) : (side > 0 ? -19.5f : 5.2f);
            return new[] { new Vector3(from, CarryRouteLayout.UpperY(from), side * .95f),
                new Vector3(to, CarryRouteLayout.UpperY(to), side * .95f) };
        }

        private void CheckCrane(string phase)
        {
            var crane = FindFirstObjectByType<CarryCraneHazard>();
            bool solid = false, trigger = false;
            if (crane != null) foreach (var c in crane.GetComponentsInChildren<Collider>())
            { if (c.isTrigger) trigger |= c.enabled; else solid |= c.enabled; }
            Check(solid && trigger, "crane contacts in " + phase + " solid=" + solid + " strike=" + trigger);
        }
        private float Elapsed() { game.TryGetRoundTime(out float remaining, out float duration); return duration - remaining; }
        private IEnumerator At(float target)
        {
            float deadline = Time.realtimeSinceStartup + 100;
            while (Elapsed() < target && Time.realtimeSinceStartup < deadline) yield return null;
            Check(Elapsed() >= target, "timer " + target);
        }
        private void Check(bool pass, string message)
        {
            if (pass) Debug.Log("CARRY_SLOPE " + message);
            else { failed = true; Debug.LogError("CARRY_SLOPE FAIL " + message); }
        }
    }
}
