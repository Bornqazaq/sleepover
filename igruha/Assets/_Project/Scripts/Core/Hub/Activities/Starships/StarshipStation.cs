using Igruha.Core.Player;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Core.Hub.Activities
{
    public sealed class StarshipStation : ArcadeStation
    {
        [SerializeField] private ArcadeScreenCamera localCamera;
        [SerializeField] private float mouseSensitivity = .003f;
        private const float InputInterval = 1f / 30f, SnapshotInterval = 1f / 30f, InputTimeout = .25f;
        private readonly NetworkVariable<StarshipState> replicated = new NetworkVariable<StarshipState>();
        private StarshipState simulation = StarshipRules.NewGame(0, false);
        private PlayerController seatedBody;
        private PlayerInputReader localInput;
        private float target, nextInput, nextSnapshot, lastInputAt, nextStart;
        private uint localSession, sentSequence, acceptedSequence;
        private bool fireHeld, requestedExit;
        public StarshipState State => IsSpawned ? replicated.Value : simulation;
        public float LocalShip { get; private set; }
        public bool IsLocal => IsLocalOccupant;
        public Vector3 StandPosition => StandPoint.position;
        public Quaternion StandRotation => StandPoint.rotation;
        public override string InteractionPrompt => IsLocal ? string.Empty : base.InteractionPrompt;
        protected override bool UsesChargeInput => false;
        protected override uint ArcadeSession => State.Session;

        public override void OnNetworkSpawn() { base.OnNetworkSpawn(); if (IsServer) replicated.Value = simulation; }
        public override bool CanInteract(PlayerController player) => base.CanInteract(player) &&
            (Occupant != NoOccupant || !player.MovementLocked && !player.IsKnockedDown);
        protected override void OnTaken(PlayerController player)
        {
            ResetArcadePause();
            seatedBody = player; simulation = StarshipRules.NewGame(simulation.Session + 1, true);
            target = 0; acceptedSequence = 0; nextStart = 0; fireHeld = false; Publish();
        }
        protected override void ResetActivity()
        {
            ResetArcadePause();
            seatedBody = null; simulation = StarshipRules.NewGame(simulation.Session + 1, false);
            target = 0; fireHeld = false; Publish();
        }
        protected override void OnAuthorityUpdate() { if (seatedBody != null && seatedBody.IsKnockedDown) Release(); }
        private void FixedUpdate()
        {
            if (!HasAuthority || Occupant == NoOccupant) return;
            if (IsArcadePaused) { fireHeld = false; return; }
            uint eventId = simulation.EventId;
            StarshipRules.Step(ref simulation, target, fireHeld && Time.unscaledTime - lastInputAt < InputTimeout, Time.fixedDeltaTime);
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
            bool menuPaused = UpdateLocalPause();
            if (player.IsKnockedDown)
            {
                if (requestedExit) return;
                requestedExit = true; if (IsSpawned) LeaveServerRpc(); else Release(); return;
            }
            requestedExit = false;
            if (localSession != State.Session)
            { localSession = State.Session; LocalShip = State.Ship; sentSequence = 0; }
            bool ready = !menuPaused && !input.Suspended && (localCamera == null || localCamera.ReadyForInput);
            if (ready)
            {
                float move = input.LookDelta.x * mouseSensitivity + input.MoveInput.x * StarshipRules.ShipSpeed * Time.deltaTime;
                LocalShip = Mathf.MoveTowards(LocalShip, Mathf.Clamp(LocalShip + move, -StarshipRules.ShipLimit, StarshipRules.ShipLimit),
                    StarshipRules.ShipSpeed * Time.deltaTime);
            }
            if (Time.unscaledTime < nextInput) return;
            nextInput = Time.unscaledTime + InputInterval; sentSequence++;
            if (IsSpawned) InputServerRpc(LocalShip, ready && input.PushHeld, localSession, sentSequence);
            else AcceptInput(LocalShip, ready && input.PushHeld, localSession, sentSequence, Occupant);
        }
        public override bool HandlePushButton(PlayerController player)
        {
            if (IsArcadePaused || !IsLocal || localInput == null || localInput.Suspended ||
                localCamera != null && !localCamera.ReadyForInput) return true;
            if (IsSpawned) StartServerRpc(State.Session); else AcceptStart(State.Session, Occupant);
            return true;
        }
        [Rpc(SendTo.Server, RequireOwnership = false, Delivery = RpcDelivery.Unreliable)]
        private void InputServerRpc(float ship, bool fire, uint session, uint sequence, RpcParams rpc = default) =>
            AcceptInput(ship, fire, session, sequence, rpc.Receive.SenderClientId);
        private void AcceptInput(float ship, bool fire, uint session, uint sequence, ulong sender)
        {
            if (IsArcadePaused || !ValidSession(session, sender) || !StarshipRules.IsFinite(ship) || sequence <= acceptedSequence) return;
            acceptedSequence = sequence; target = Mathf.Clamp(ship, -StarshipRules.ShipLimit, StarshipRules.ShipLimit);
            fireHeld = fire; lastInputAt = Time.unscaledTime;
        }
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void StartServerRpc(uint session, RpcParams rpc = default) => AcceptStart(session, rpc.Receive.SenderClientId);
        private void AcceptStart(uint session, ulong sender)
        {
            if (IsArcadePaused || !ValidSession(session, sender) || Time.unscaledTime < nextStart || simulation.Phase == StarshipPhase.Playing) return;
            nextStart = Time.unscaledTime + .25f;
            uint previousSession = simulation.Session;
            StarshipRules.Start(ref simulation);
            if (previousSession != simulation.Session) { acceptedSequence = 0; target = 0; fireHeld = false; }
            Publish();
        }
        private bool ValidSession(uint session, ulong sender) => HasAuthority && Occupant != NoOccupant &&
            Occupant == sender && session == simulation.Session;
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void LeaveServerRpc(RpcParams rpc = default) { if (Occupant == rpc.Receive.SenderClientId) Release(); }
        protected override void Launch(Vector3 direction, float power) { }
    }
}
