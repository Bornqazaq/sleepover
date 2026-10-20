using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>Winner role, server-simulated flight, private-shelf tricks and local presentation lifecycle.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class CansGhostController : MonoBehaviour
    {
        [SerializeField] private Material ghostMaterial;
        [SerializeField] private Material ribbonMaterial;
        [SerializeField] private Material puffMaterial;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private AudioClip trickSound;
        private const float LookSpeed = .13f, CameraDistance = 2.4f, CameraHeight = .55f, FlightFieldOfView = 60f;
        private const float ResponseTimeout = 3f, RemoteSmooth = 15f;
        private CansOrderMinigame game;
        private CansOrderNetwork network;
        private MinigameCameraController cameras;
        private Transform cameraRig;
        private CinemachineCamera cineCamera;
        private PlayerController avatar;
        private PlayerInputReader reader;
        private CansGhostVisual visual;
        private CansGhostHud hud;
        private Vector3 serverInput, predictedPosition, visualPosition;
        private float yaw, pitch, nextInputAt, lastInputAt, requestAt;
        private byte appliedFirst, appliedSecond;
        private bool local, pendingRequest, presenting, bodyWasLocked;
        private Rigidbody body;
        private bool motorWasEnabled, bodyWasKinematic, bodyUsedGravity;
        private bool[] colliderStates;
        private Collider[] colliders;
        private bool hudWasEnabled;
        private CansOrderLocalHud regularHud;
        private Vector3 previousRigPosition;
        private Quaternion previousRigRotation;
        private float previousFieldOfView;
        private System.Random random;
        private readonly RaycastHit[] cameraHits = new RaycastHit[32];
        public CansGhostState State { get; private set; }
        public CansGhostPose Pose { get; private set; }
        public bool HasAwarded { get; private set; }
        public bool IsLocalGhost => presenting && local;
        public Transform VisualRoot => visual != null ? visual.transform : null;

        public void Bind(CansOrderMinigame rules, CansOrderNetwork transport, MinigameCameraController views, Transform rig, CansOrderLocalHud normalHud)
        {
            game = rules; network = transport; cameras = views; cameraRig = rig; regularHud = normalHud;
            cineCamera = rig != null ? rig.GetComponent<CinemachineCamera>() : null;
            random = new System.Random();
        }

        public void Award(int owner, Vector3 position, float heading)
        {
            if (game == null || !game.GhostAuthority || HasAwarded) return;
            HasAwarded = true;
            State = new CansGhostState { Active = true, OwnerId = owner, StartedAt = NetworkClock.Now };
            Pose = new CansGhostPose { Position = position, Yaw = heading };
            TryPresent();
        }

        public void Receive(CansGhostState state, CansGhostPose pose)
        {
            if (game == null || game.GhostAuthority || game.Phase == MinigamePhase.Results) return;
            State = state; Pose = pose;
            HasAwarded |= state.Active;
        }

        private void TryPresent()
        {
            if (presenting || !State.Active || !game.GhostGameplay ||
                !game.TryGetGhostPlayer(State.OwnerId, out avatar, out _, out local)) return;
            game.PrepareGhostBody(State.OwnerId);
            reader = avatar.GetComponent<PlayerInputReader>();
            bodyWasLocked = avatar.MovementLocked;
            avatar.MovementLocked = true;
            motorWasEnabled = avatar.enabled;
            body = avatar.GetComponent<Rigidbody>();
            if (body != null)
            {
                bodyWasKinematic = body.isKinematic; bodyUsedGravity = body.useGravity;
                if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            }
            avatar.enabled = false;
            if (body != null) { body.isKinematic = true; body.useGravity = false; }
            colliders = avatar.GetComponentsInChildren<Collider>(true);
            colliderStates = new bool[colliders.Length];
            for (int i = 0; i < colliders.Length; i++)
            { colliderStates[i] = colliders[i].enabled; colliders[i].enabled = false; }
            var go = new GameObject("Winner ghost — " + State.OwnerId);
            go.transform.SetParent(transform, false);
            visual = go.AddComponent<CansGhostVisual>();
            visual.Build(avatar, ghostMaterial, ribbonMaterial, puffMaterial, trickSound);
            yaw = Pose.Yaw; pitch = 8f;
            predictedPosition = visualPosition = Pose.Position;
            presenting = true;
            if (!local) return;
            previousRigPosition = cameraRig != null ? cameraRig.position : Vector3.zero;
            previousRigRotation = cameraRig != null ? cameraRig.rotation : Quaternion.identity;
            hudWasEnabled = regularHud != null && regularHud.enabled;
            if (regularHud != null) regularHud.enabled = false;
            EnsureHud();
            if (cameraRig != null) cameras?.Apply(CameraMode.Fixed, cameraRig);
            if (cineCamera != null)
            {
                previousFieldOfView = cineCamera.Lens.FieldOfView;
                var lens = cineCamera.Lens; lens.FieldOfView = FlightFieldOfView; cineCamera.Lens = lens;
                cineCamera.PreviousStateIsValid = false;
            }
        }

        private void EnsureHud()
        {
            if (hud != null) return;
            hud = gameObject.AddComponent<CansGhostHud>();
            hud.Build(font);
        }

        public void NotifyVictim()
        {
            EnsureHud(); hud.ShowVictim();
        }

        public byte AppliedRevision(int playerId)
        {
            if (appliedSecond > 0 && State.Second.TargetId == playerId) return appliedSecond;
            return appliedFirst > 0 && State.First.TargetId == playerId ? appliedFirst : (byte)0;
        }

        private void ApplyTricks()
        {
            if (State.First.Serial > appliedFirst)
            {
                if (!game.ApplyGhostTrick(State.First)) return;
                appliedFirst = State.First.Serial;
                visual?.Trick(State.First.TargetId);
            }
            if (State.Second.Serial > appliedSecond)
            {
                if (!game.ApplyGhostTrick(State.Second)) return;
                appliedSecond = State.Second.Serial;
                visual?.Trick(State.Second.TargetId);
            }
        }

        public void AcceptInput(int sender, Vector3 direction, float heading)
        {
            if (game == null || !game.GhostAuthority || !game.GhostGameplay || !State.Active ||
                State.OwnerId != sender || !CansGhostRules.Finite(direction) || !CansGhostRules.Finite(heading)) return;
            serverInput = Vector3.ClampMagnitude(direction, 1f);
            lastInputAt = Time.unscaledTime;
            var pose = Pose; pose.Yaw = Mathf.Repeat(heading, 360f); Pose = pose;
        }

        public bool TryTrick(int sender, int target)
        {
            if (game == null || !game.GhostAuthority ||
                !game.CanGhostTrick(sender, target, Pose.Position, NetworkClock.Now)) return false;
            int count = game.Round.CanCount;
            int first = random.Next(count), second = random.Next(count - 1);
            if (second >= first) second++;
            var next = State;
            var trick = new CansGhostTrick { Serial = (byte)(next.Used + 1), TargetId = target,
                FirstSlot = (byte)first, SecondSlot = (byte)second, Circle = game.Round.Circle, StartedAt = NetworkClock.Now };
            if (next.Used == 0) next.First = trick; else next.Second = trick;
            next.Used++;
            next.ReadyAt = NetworkClock.Now + CansGhostRules.Cooldown;
            State = next;
            ApplyTricks();
            return true;
        }

        public void ReceiveReply(bool accepted)
        {
            pendingRequest = false;
            if (!accepted) hud?.Rejected();
        }

        private void Update()
        {
            if (game == null) return;
            // A committed trick survives the ghost owner's disconnect.
            if (game.GhostGameplay) ApplyTricks();
            if (!State.Active || !game.GhostGameplay)
            {
                if (presenting) ReleasePresentation();
                hud?.RenderGhost(false, default, -1, string.Empty, false, false);
                return;
            }
            if (game.GhostAuthority && !game.IsGhostSolverPresent(State.OwnerId))
            {
                var stopped = State; stopped.Active = false; State = stopped;
                ReleasePresentation(); return;
            }
            TryPresent();
            ApplyTricks();
            if (!presenting) return;
            if (local) ReadInput();
            if (game.GhostAuthority)
            {
                Vector3 input = Time.unscaledTime - lastInputAt <= CansGhostRules.InputTimeout ? serverInput : Vector3.zero;
                var pose = Pose;
                pose.Position = CansGhostRules.Constrain(pose.Position + input * (CansGhostRules.FlightSpeed * Time.deltaTime),
                    game.GhostFlightRadius, game.GhostFlightCeiling);
                Pose = pose;
                predictedPosition = pose.Position;
            }
        }

        private void ReadInput()
        {
            bool canInput = reader != null && reader.enabled && !reader.Suspended && !reader.EmoteHeld;
            Vector3 direction = Vector3.zero;
            if (canInput)
            {
                Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
                Vector2 look = reader.Autopilot ? Vector2.zero :
                    Mouse.current != null ? Mouse.current.delta.ReadValue() : reader.LookDelta;
                yaw += Mathf.Clamp(look.x, -150f, 150f) * LookSpeed;
                pitch = Mathf.Clamp(pitch - Mathf.Clamp(look.y, -150f, 150f) * LookSpeed, -65f, 65f);
                Quaternion heading = Quaternion.Euler(pitch, yaw, 0);
                Vector2 move = reader.MoveInput;
                direction = Vector3.ClampMagnitude(heading * new Vector3(move.x, 0, move.y), 1f);
            }
            if (game.GhostAuthority) AcceptInput(State.OwnerId, direction, yaw);
            else
            {
                predictedPosition = CansGhostRules.Constrain(predictedPosition + direction *
                    (CansGhostRules.FlightSpeed * Time.deltaTime), game.GhostFlightRadius, game.GhostFlightCeiling);
                predictedPosition = Vector3.Lerp(predictedPosition, Pose.Position, 1f - Mathf.Exp(-4f * Time.deltaTime));
                if (Time.unscaledTime >= nextInputAt)
                {
                    nextInputAt = Time.unscaledTime + CansGhostRules.NetworkInterval;
                    network?.SendGhostInput(direction, yaw);
                }
            }
            int target = game.GhostTarget(predictedPosition, Quaternion.Euler(pitch, yaw, 0) * Vector3.forward);
            bool available = target >= 0 && game.CanGhostTrick(State.OwnerId, target, predictedPosition, NetworkClock.Now);
            if (pendingRequest && Time.unscaledTime - requestAt > ResponseTimeout) pendingRequest = false;
            bool pressed = canInput && (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame || reader.PushPressed);
            if (reader != null && reader.PushPressed) reader.ConsumePush();
            if (pressed && available && !pendingRequest)
            {
                if (game.GhostAuthority) ReceiveReply(TryTrick(State.OwnerId, target));
                else { pendingRequest = true; requestAt = Time.unscaledTime; network?.RequestGhostTrick(target); }
            }
            hud?.RenderGhost(true, State, target, game.GhostTargetName(target), available, pendingRequest, game.Round.Circle);
        }

        private void LateUpdate()
        {
            if (!presenting || visual == null) return;
            if (avatar != null) avatar.MovementLocked = true;
            Vector3 goal = local ? predictedPosition : Pose.Position;
            visualPosition = Vector3.Lerp(visualPosition, goal, 1f - Mathf.Exp(-RemoteSmooth * Time.deltaTime));
            visual.Present(visualPosition, local ? yaw : Pose.Yaw);
            if (!local || cameraRig == null || cameras == null) return;
            Quaternion look = Quaternion.Euler(pitch, yaw, 0);
            Vector3 pivot = visualPosition + Vector3.up * CameraHeight;
            Vector3 backward = look * Vector3.back;
            float distance = CameraDistance;
            int count = Physics.SphereCastNonAlloc(pivot, .18f, backward, cameraHits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = cameraHits[i];
                if (hit.collider == null || hit.collider.GetComponentInParent<PlayerController>() != null) continue;
                distance = Mathf.Min(distance, Mathf.Max(.25f, hit.distance - .12f));
            }
            cameraRig.SetPositionAndRotation(pivot + backward * distance, look);
            if (cameras.CurrentMode != CameraMode.Fixed || cameras.CurrentTarget != cameraRig)
                cameras.Apply(CameraMode.Fixed, cameraRig);
        }

        private void ReleasePresentation()
        {
            if (!presenting) return;
            presenting = false;
            if (body != null) { body.isKinematic = bodyWasKinematic; body.useGravity = bodyUsedGravity; }
            if (avatar != null) { avatar.MovementLocked = bodyWasLocked; avatar.enabled = motorWasEnabled; }
            for (int i = 0; colliders != null && i < colliders.Length; i++)
                if (colliders[i] != null) colliders[i].enabled = colliderStates[i];
            if (visual != null) { visual.Release(); Destroy(visual.gameObject); visual = null; }
            if (local)
            {
                if (regularHud != null) regularHud.enabled = hudWasEnabled;
                if (cameraRig != null) cameraRig.SetPositionAndRotation(previousRigPosition, previousRigRotation);
                if (cineCamera != null)
                {
                    var lens = cineCamera.Lens; lens.FieldOfView = previousFieldOfView; cineCamera.Lens = lens;
                    cineCamera.PreviousStateIsValid = false;
                }
                if (avatar != null) cameras?.Apply(CameraMode.ThirdPerson, avatar.transform);
            }
            hud?.RenderGhost(false, default, -1, string.Empty, false, false);
            local = false; avatar = null; reader = null; body = null; colliders = null;
        }

        public void EndRound()
        {
            ReleasePresentation();
            var ended = State; ended.Active = false; State = ended;
            serverInput = Vector3.zero; pendingRequest = false;
            hud?.HideAll();
        }
        public void ResetRound()
        {
            EndRound(); State = default; Pose = default; HasAwarded = false;
            appliedFirst = appliedSecond = 0; nextInputAt = 0;
        }
        private void OnDisable() => EndRound();
    }
}
