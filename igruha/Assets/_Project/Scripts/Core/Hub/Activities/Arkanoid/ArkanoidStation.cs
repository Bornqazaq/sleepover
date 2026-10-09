using Igruha.Core.Player;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>One cabinet, one owner. Clients send paddle intent; outcomes remain on the host.</summary>
    public sealed class ArkanoidStation : HubActivityStation
    {
        [SerializeField] private ArkanoidCamera localCamera;
        [SerializeField] private float mouseSensitivity = .003f;
        private const float InputInterval = 1f / 30f, SnapshotInterval = 1f / 30f;
        private readonly NetworkVariable<ArkanoidState> replicated = new NetworkVariable<ArkanoidState>();
        private ArkanoidState simulation = ArkanoidRules.NewGame(0, false);
        private PlayerController seatedBody, localBody;
        private PlayerInputReader localInput;
        private float target, nextInput, nextSnapshot, nextLaunch;
        private uint localSession, sentSequence, acceptedSequence;
        private bool requestedKnockdownExit;

        public ArkanoidState State => IsSpawned ? replicated.Value : simulation;
        public float LocalPaddle { get; private set; }
        public bool IsLocal => IsOccupiedByLocalPlayer();
        public PlayerController LocalPlayer => IsLocal ? localBody != null ? localBody : seatedBody : null;
        public override string InteractionPrompt => IsLocal ? string.Empty : base.InteractionPrompt;
        public Vector3 StandPosition => StandPoint.position;
        public Quaternion StandRotation => StandPoint.rotation;
        protected override bool UsesChargeInput => false;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer) replicated.Value = simulation;
        }

        public override bool CanInteract(PlayerController player) => base.CanInteract(player) &&
            (Occupant != NoOccupant || !player.MovementLocked && !player.IsKnockedDown);

        protected override void OnTaken(PlayerController player)
        {
            seatedBody = player;
            simulation = ArkanoidRules.NewGame(simulation.Session + 1, true);
            target = 0;
            acceptedSequence = 0;
            nextLaunch = 0;
            Publish();
        }

        protected override void ResetActivity()
        {
            seatedBody = null;
            simulation = ArkanoidRules.NewGame(simulation.Session + 1, false);
            target = 0;
            Publish();
        }

        protected override void OnAuthorityUpdate()
        {
            if (seatedBody != null && seatedBody.IsKnockedDown) Release();
        }

        private void FixedUpdate()
        {
            if (!HasAuthority || Occupant == NoOccupant) return;
            uint eventId = simulation.EventId;
            ArkanoidRules.Step(ref simulation, target, Time.fixedDeltaTime);
            simulation.Tick++;
            if (Time.unscaledTime >= nextSnapshot || eventId != simulation.EventId) Publish();
        }

        private void Publish()
        {
            if (IsSpawned && IsServer) replicated.Value = simulation;
            nextSnapshot = Time.unscaledTime + SnapshotInterval;
        }

        protected override void OnLocalAiming(PlayerController player, PlayerInputReader input)
        {
            localInput = input;
            localBody = player;
            if (player.IsKnockedDown)
            {
                if (requestedKnockdownExit) return;
                requestedKnockdownExit = true;
                if (IsSpawned) LeaveRpc(); else Release();
                return;
            }
            requestedKnockdownExit = false;
            if (localSession != State.Session)
            {
                localSession = State.Session;
                LocalPaddle = State.Paddle;
                sentSequence = 0;
            }
            if (input.Suspended || localCamera != null && !localCamera.ReadyForInput) return;
            float movement = input.LookDelta.x * mouseSensitivity + input.MoveInput.x * ArkanoidRules.PaddleSpeed * Time.deltaTime;
            LocalPaddle = Mathf.MoveTowards(LocalPaddle,
                Mathf.Clamp(LocalPaddle + movement, -ArkanoidRules.PaddleLimit, ArkanoidRules.PaddleLimit),
                ArkanoidRules.PaddleSpeed * Time.deltaTime);
            if (Time.unscaledTime < nextInput) return;
            nextInput = Time.unscaledTime + InputInterval;
            sentSequence++;
            if (IsSpawned) MovePaddleRpc(LocalPaddle, localSession, sentSequence);
            else AcceptInput(LocalPaddle, localSession, sentSequence, Occupant);
        }

        public override bool HandlePushButton(PlayerController player)
        {
            if (!IsLocal || localInput == null || localInput.Suspended ||
                localCamera != null && !localCamera.ReadyForInput) return true;
            if (IsSpawned) PlayRpc(LocalPaddle, State.Session);
            else AcceptPlay(LocalPaddle, State.Session, Occupant);
            return true;
        }

        [Rpc(SendTo.Server, RequireOwnership = false, Delivery = RpcDelivery.Unreliable)]
        private void MovePaddleRpc(float paddle, uint session, uint sequence, RpcParams rpc = default) =>
            AcceptInput(paddle, session, sequence, rpc.Receive.SenderClientId);

        private void AcceptInput(float paddle, uint session, uint sequence, ulong sender)
        {
            if (!ValidInput(paddle, session, sender) || sequence <= acceptedSequence) return;
            acceptedSequence = sequence;
            target = Mathf.Clamp(paddle, -ArkanoidRules.PaddleLimit, ArkanoidRules.PaddleLimit);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void PlayRpc(float paddle, uint session, RpcParams rpc = default) =>
            AcceptPlay(paddle, session, rpc.Receive.SenderClientId);

        private void AcceptPlay(float paddle, uint session, ulong sender)
        {
            if (!ValidInput(paddle, session, sender) || Time.unscaledTime < nextLaunch) return;
            nextLaunch = Time.unscaledTime + .2f;
            target = Mathf.Clamp(paddle, -ArkanoidRules.PaddleLimit, ArkanoidRules.PaddleLimit);
            ArkanoidRules.Launch(ref simulation);
            Publish();
        }

        private bool ValidInput(float paddle, uint session, ulong sender) => HasAuthority &&
            Occupant != NoOccupant && Occupant == sender && session == simulation.Session && ArkanoidRules.IsFinite(paddle);

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void LeaveRpc(RpcParams rpc = default)
        {
            if (Occupant == rpc.Receive.SenderClientId) Release();
        }

        // The existing push button starts/serves; this station has no charge mechanic.
        protected override void Launch(Vector3 direction, float power) { }
    }
}
