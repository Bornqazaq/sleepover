using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Igruha.Tests
{
    /// <summary>Development-only Host + Client check of the actual OwnerNetworkAnimator path.</summary>
    public sealed class CharacterPolishNetworkProbe : MonoBehaviour
    {
        private static readonly PropertyInfo CrouchHeld = typeof(PlayerInputReader).GetProperty("CrouchHeld");
        private bool sawAlternate, sawReturn, sawCrouch, walked;
        private float minBlend = 1f, maxBlend;
        private Vector3 walkStart;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.HasFlag("--character-polish-check") && !LaunchArguments.HasFlag("--character-recovery-check")
                && !LaunchArguments.HasFlag("--character-roster-check")) return;
            var root = new GameObject("CharacterPolishNetworkProbe");
            DontDestroyOnLoad(root); root.AddComponent<CharacterPolishNetworkProbe>();
        }

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            float timeout = Time.realtimeSinceStartup + 90f;
            while ((NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient ||
                SceneManager.GetActiveScene().name != "Hub" || CharacterSelection.Current == null)
                && Time.realtimeSinceStartup < timeout) yield return null;
            var network = NetworkManager.Singleton;
            if (network == null || CharacterSelection.Current == null) { Fail("no network selection"); yield break; }
            // Boot starts networking before Hub loads; players chosen in Boot are destroyed by that load.
            yield return new WaitForSecondsRealtime(1f);
            bool host = network.IsServer;
            CharacterSelection.Current.ReportReady();
            bool recoveryCheck = LaunchArguments.HasFlag("--character-recovery-check");
            bool rosterCheck = LaunchArguments.HasFlag("--character-roster-check");
            int selected = host ? 6 : (recoveryCheck ? 0 : 1);
            if (rosterCheck)
            {
                string raw;
                if (!LaunchArguments.TryGetValue("--character-index", out raw) || !int.TryParse(raw, out selected))
                { Fail("missing character index"); yield break; }
            }
            CharacterSelection.Current.Choose(selected);
            while ((network.LocalClient == null || network.LocalClient.PlayerObject == null ||
                (host && network.ConnectedClients.Count < (rosterCheck ? LaunchArguments.WaitPlayers : 2)))
                && Time.realtimeSinceStartup < timeout) yield return null;
            if (network.LocalClient == null || network.LocalClient.PlayerObject == null) { Fail("no player object"); yield break; }
            var own = network.LocalClient.PlayerObject.GetComponent<PlayerController>();
            own.GetComponent<PlayerInputReader>().EngageAutopilot();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.layer = LayerMask.NameToLayer("Ground");
            floor.transform.position = new Vector3(1000f, -.5f, 1020f);
            floor.transform.localScale = new Vector3(100f, 1f, 100f);
            var basis = new GameObject("Polish movement basis");
            own.SetCameraReference(basis.transform);
            if (host || recoveryCheck || rosterCheck) own.TeleportTo(new Vector3(1000f + (rosterCheck ? network.LocalClientId * 4f : (host ? 0f : 4f)), .05f, 1000f), Quaternion.identity);
            if (rosterCheck)
            {
                yield return CheckRosterGait(network, own);
                yield break;
            }
            Animator target = null;
            while (target == null && Time.realtimeSinceStartup < timeout)
            {
                foreach (var pair in network.SpawnManager.SpawnedObjects)
                {
                    if (pair.Value.OwnerClientId != NetworkManager.ServerClientId) continue;
                    var animator = pair.Value.GetComponentInChildren<Animator>();
                    if (animator == null) continue;
                    foreach (var parameter in animator.parameters)
                        if (parameter.name == CharacterAnimatorDriver.IdleVariationParameter) { target = animator; break; }
                    if (target != null) break;
                }
                yield return null;
            }
            if (target == null) { Fail("Milez not spawned"); yield break; }
            target.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var targetMotor = target.GetComponentInParent<PlayerController>();
            if (!host && targetMotor.GetComponent<CharacterAnimatorDriver>().enabled) { Fail("remote driver enabled"); yield break; }
            if (recoveryCheck)
            {
                yield return CheckRecoveryAndKarlan(network, own, target);
                yield break;
            }
            float started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < 31f)
            {
                float elapsed = Time.realtimeSinceStartup - started;
                float value = target.GetFloat(CharacterAnimatorDriver.IdleVariationParameter);
                minBlend = Mathf.Min(minBlend, value); maxBlend = Mathf.Max(maxBlend, value);
                sawAlternate |= value > .97f;
                sawReturn |= sawAlternate && value < .03f;
                if (host && !walked && elapsed > 23f)
                {
                    walked = true;
                    walkStart = own.Position;
                    var input = own.GetComponent<PlayerInputReader>();
                    CrouchHeld.SetValue(input, true); input.DriveMove(Vector2.up);
                }
                sawCrouch |= target.GetCurrentAnimatorStateInfo(0).IsName("CrouchWalk");
                yield return null;
            }
            bool trajectory = !host || (Mathf.Abs(own.Position.x - walkStart.x) < .01f && own.Position.z - walkStart.z > 5f);
            bool pass = sawAlternate && sawReturn && sawCrouch && trajectory;
            Debug.Log("CHARACTER_POLISH_NETWORK " + (pass ? "PASS" : "FAIL") + " role=" + (host ? "host" : "client")
                + " blend=" + minBlend + ".." + maxBlend + " alternate=" + sawAlternate + " returned=" + sawReturn
                + " crouch=" + sawCrouch + " trajectory=" + trajectory);
            if (!pass) Debug.LogError("CHARACTER_POLISH_NETWORK FAIL incomplete replicated animation sequence");
            yield return new WaitForSecondsRealtime(host ? 5f : 1f);
            Application.Quit(pass ? 0 : 2);
        }

        private sealed class GaitTrack
        {
            public Animator Animator;
            public Transform Root;
            public readonly Transform[] Feet = new Transform[2], Toes = new Transform[2];
            public readonly Vector3[] Min = { Vector3.positiveInfinity, Vector3.positiveInfinity };
            public readonly Vector3[] Max = { Vector3.negativeInfinity, Vector3.negativeInfinity };
            public bool Forward = true, Crossed;
        }

        private IEnumerator CheckRosterGait(NetworkManager network, PlayerController own)
        {
            var tracks = new Dictionary<ulong, GaitTrack>();
            float timeout = Time.realtimeSinceStartup + 60f;
            while (tracks.Count < LaunchArguments.WaitPlayers && Time.realtimeSinceStartup < timeout)
            {
                foreach (var pair in network.SpawnManager.SpawnedObjects)
                {
                    var obj = pair.Value;
                    if (!obj.IsPlayerObject || tracks.ContainsKey(obj.OwnerClientId)) continue;
                    var a = obj.GetComponentInChildren<Animator>();
                    a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    var track = new GaitTrack { Animator = a, Root = obj.transform };
                    track.Feet[0] = a.GetBoneTransform(HumanBodyBones.LeftFoot); track.Feet[1] = a.GetBoneTransform(HumanBodyBones.RightFoot);
                    track.Toes[0] = a.GetBoneTransform(HumanBodyBones.LeftToes); track.Toes[1] = a.GetBoneTransform(HumanBodyBones.RightToes);
                    tracks.Add(obj.OwnerClientId, track);
                }
                yield return null;
            }
            if (tracks.Count != LaunchArguments.WaitPlayers) { Fail("incomplete roster"); yield break; }
            var input = own.GetComponent<PlayerInputReader>();
            CrouchHeld.SetValue(input, true); input.DriveMove(Vector2.up);
            float started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < 9f)
            {
                foreach (var track in tracks.Values)
                {
                    if (Time.realtimeSinceStartup - started < 2f || !track.Animator.GetCurrentAnimatorStateInfo(0).IsName("CrouchWalk")) continue;
                    for (int i = 0; i < 2; i++)
                    {
                        Vector3 p = track.Root.InverseTransformPoint(track.Feet[i].position);
                        track.Min[i] = Vector3.Min(track.Min[i], p); track.Max[i] = Vector3.Max(track.Max[i], p);
                        Vector3 direction = track.Root.InverseTransformVector(track.Toes[i].position - track.Feet[i].position);
                        track.Forward &= Mathf.Abs(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg) < 3f;
                        track.Crossed |= i == 0 ? p.x >= -.08f : p.x <= .08f;
                    }
                }
                yield return null;
            }
            bool pass = true;
            foreach (var pair in tracks)
            {
                var track = pair.Value;
                bool gait = track.Forward && !track.Crossed;
                for (int i = 0; i < 2; i++) gait &= track.Max[i].x - track.Min[i].x < .02f && track.Max[i].z - track.Min[i].z > .3f;
                pass &= gait;
                Debug.Log("CHARACTER_ROSTER_GAIT " + (gait ? "PASS" : "FAIL") + " observer=" + network.LocalClientId
                    + " owner=" + pair.Key + " avatar=" + track.Root.name);
            }
            Debug.Log("CHARACTER_ROSTER_NETWORK " + (pass ? "PASS" : "FAIL") + " observer=" + network.LocalClientId + " players=" + tracks.Count);
            yield return new WaitForSecondsRealtime(network.IsServer ? 5f : 1f);
            Application.Quit(pass ? 0 : 2);
        }

        private IEnumerator CheckRecoveryAndKarlan(NetworkManager network, PlayerController own, Animator milez)
        {
            Animator karlan = null;
            float timeout = Time.realtimeSinceStartup + 30f;
            while (karlan == null && Time.realtimeSinceStartup < timeout)
            {
                foreach (var pair in network.SpawnManager.SpawnedObjects)
                    if (pair.Value.IsPlayerObject && pair.Value.OwnerClientId != NetworkManager.ServerClientId)
                        karlan = pair.Value.GetComponentInChildren<Animator>();
                yield return null;
            }
            if (karlan == null) { Fail("Karlan not spawned"); yield break; }
            karlan.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var karlanRoot = karlan.GetComponentInParent<PlayerController>().transform;
            var feet = new[] { karlan.GetBoneTransform(HumanBodyBones.LeftFoot), karlan.GetBoneTransform(HumanBodyBones.RightFoot) };
            var toes = new[] { karlan.GetBoneTransform(HumanBodyBones.LeftToes), karlan.GetBoneTransform(HumanBodyBones.RightToes) };
            var min = new[] { Vector3.positiveInfinity, Vector3.positiveInfinity };
            var max = new[] { Vector3.negativeInfinity, Vector3.negativeInfinity };
            bool fromBack = false, fromBelly = false, correctSide = true, forwardFeet = true, firstHit = false, secondHit = false;
            if (!network.IsServer)
            {
                var input = own.GetComponent<PlayerInputReader>();
                CrouchHeld.SetValue(input, true); input.DriveMove(Vector2.up);
            }
            float started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < 11f)
            {
                float elapsed = Time.realtimeSinceStartup - started;
                if (network.IsServer && !firstHit && elapsed > 1f)
                {
                    firstHit = true; ApplyTestImpact(milez, KnockdownType.FlyBack);
                }
                if (network.IsServer && !secondHit && elapsed > 5f)
                {
                    secondHit = true; ApplyTestImpact(milez, KnockdownType.FallForward);
                }
                var state = milez.GetCurrentAnimatorStateInfo(0);
                if (state.normalizedTime < .15f && (state.IsName("StandUpFromBack") || state.IsName("StandUpFromForward")))
                {
                    Vector3 across = milez.GetBoneTransform(HumanBodyBones.RightUpperArm).position - milez.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
                    Vector3 up = milez.GetBoneTransform(HumanBodyBones.Neck).position - milez.GetBoneTransform(HumanBodyBones.Hips).position;
                    float face = Vector3.Cross(across.normalized, up.normalized).y;
                    bool back = state.IsName("StandUpFromBack");
                    correctSide &= face * (back ? 1f : -1f) > .8f;
                    fromBack |= back; fromBelly |= !back;
                }
                if (elapsed > 2f && karlan.GetCurrentAnimatorStateInfo(0).IsName("CrouchWalk"))
                {
                    for (int i = 0; i < 2; i++)
                    {
                        Vector3 p = karlanRoot.InverseTransformPoint(feet[i].position);
                        min[i] = Vector3.Min(min[i], p); max[i] = Vector3.Max(max[i], p);
                        Vector3 direction = karlanRoot.InverseTransformVector(toes[i].position - feet[i].position);
                        forwardFeet &= Mathf.Abs(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg) < 3f;
                    }
                }
                yield return null;
            }
            bool gait = forwardFeet;
            for (int i = 0; i < 2; i++) gait &= max[i].x - min[i].x < .02f && max[i].z - min[i].z > .3f;
            bool pass = fromBack && fromBelly && correctSide && gait;
            Debug.Log("CHARACTER_RECOVERY_NETWORK " + (pass ? "PASS" : "FAIL") + " role=" + (network.IsServer ? "host" : "client")
                + " back=" + fromBack + " belly=" + fromBelly + " correctSide=" + correctSide + " straightFeet=" + gait);
            yield return new WaitForSecondsRealtime(network.IsServer ? 5f : 1f);
            Application.Quit(pass ? 0 : 2);
        }
        private static void ApplyTestImpact(Animator animator, KnockdownType type)
        {
            var motor = animator.GetComponentInParent<PlayerController>();
            float impulse = motor.GetComponent<Rigidbody>().mass * motor.Config.KnockdownVelocityThreshold * 1.01f;
            motor.GetComponent<NetworkPlayerController>().ServerApplyImpulse(
                (type == KnockdownType.FlyBack ? Vector3.back : Vector3.forward) * impulse, type);
        }
        private static void Fail(string why)
        {
            Debug.LogError("CHARACTER_POLISH_NETWORK FAIL " + why); Application.Quit(2);
        }
    }
}
