using Unity.Netcode;
using UnityEngine;

namespace Igruha.Minigames.CansOrder
{
    public sealed partial class CansOrderNetwork
    {
        private readonly NetworkVariable<CansGhostState> ghostState = new NetworkVariable<CansGhostState>();
        private readonly NetworkVariable<CansGhostPose> ghostPose = new NetworkVariable<CansGhostPose>();
        private double nextGhostPose;

        private void SyncGhost()
        {
            if (game?.Ghost == null) return;
            if (!IsServer)
            {
                game.Ghost.Receive(ghostState.Value, ghostPose.Value);
                return;
            }
            // Publish the spawn pose with the award, even between movement ticks.
            if (game.Ghost.State.Active && !ghostState.Value.Active)
                ghostPose.Value = game.Ghost.Pose;
            if (!ghostState.Value.Equals(game.Ghost.State)) ghostState.Value = game.Ghost.State;
            double now = NetworkManager.ServerTime.Time;
            if (now < nextGhostPose) return;
            nextGhostPose = now + CansGhostRules.NetworkInterval;
            if (!ghostPose.Value.Equals(game.Ghost.Pose)) ghostPose.Value = game.Ghost.Pose;
        }

        public void SendGhostInput(Vector3 direction, float yaw)
        {
            if (IsSpawned && !IsServer) GhostFlightRpc(direction, yaw);
        }

        [Rpc(SendTo.Server, RequireOwnership = false, Delivery = RpcDelivery.Unreliable)]
        private void GhostFlightRpc(Vector3 direction, float yaw, RpcParams p = default)
        {
            game?.Ghost?.AcceptInput((int)p.Receive.SenderClientId, direction, yaw);
        }

        public void RequestGhostTrick(int target)
        {
            if (IsSpawned && !IsServer) GhostTrickRpc(target);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void GhostTrickRpc(int target, RpcParams p = default)
        {
            bool accepted = game?.Ghost != null && game.Ghost.TryTrick((int)p.Receive.SenderClientId, target);
            GhostReplyRpc(accepted, RpcTarget.Single(p.Receive.SenderClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void GhostReplyRpc(bool accepted, RpcParams p = default) => game?.Ghost?.ReceiveReply(accepted);
    }
}
