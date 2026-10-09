using Igruha.Core.Player;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>One ordinary hub station per end: the existing owner binding and cleanup remain shared.</summary>
    public sealed class PingPongSeat : HubActivityStation
    {
        [SerializeField] private PingPongTable table;
        [SerializeField] private byte side;
        private PlayerController seatedBody;
        private bool requestedKnockdownExit;

        public byte Side => side;
        public bool IsLocal => IsOccupiedByLocalPlayer();
        public Vector3 StandPosition => StandPoint.position;
        public Quaternion StandRotation => StandPoint.rotation;
        protected override bool UsesChargeInput => false;

        public override bool CanInteract(PlayerController player) => base.CanInteract(player) &&
            (Occupant != NoOccupant || (!player.MovementLocked && !player.IsKnockedDown &&
                !table.AlreadySeated(player, side)));

        protected override void OnTaken(PlayerController player)
        {
            seatedBody = player;
            table.SeatsChanged(side);
        }

        protected override void ResetActivity()
        {
            seatedBody = null;
            if (table != null) table.SeatsChanged(side);
        }

        protected override void OnAuthorityUpdate()
        {
            if (seatedBody != null && seatedBody.IsKnockedDown) Release();
        }

        protected override void OnLocalAiming(PlayerController player, PlayerInputReader input)
        {
            if (!player.IsKnockedDown) { requestedKnockdownExit = false; return; }
            if (requestedKnockdownExit) return;
            requestedKnockdownExit = true;
            // Knockdown physics lives on the owner's machine. Leaving is always allowed,
            // but a client can release only its own seat, regardless of the stated reason.
            if (IsSpawned) LeaveAfterKnockdownRpc();
            else Release();
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void LeaveAfterKnockdownRpc(RpcParams rpcParams = default)
        {
            if (Occupant == rpcParams.Receive.SenderClientId) Release();
        }

        public override bool HandlePushButton(PlayerController player)
        {
            if (IsLocal && table != null) table.RequestHit(side);
            return true;
        }

        // Charge/release input is disabled for this station; shots use HandlePushButton above.
        protected override void Launch(Vector3 direction, float power) { }
    }
}
