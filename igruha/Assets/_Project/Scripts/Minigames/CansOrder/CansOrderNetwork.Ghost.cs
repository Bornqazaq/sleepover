using Unity.Netcode;
using UnityEngine;

namespace Igruha.Minigames.CansOrder
{
    public sealed partial class CansOrderNetwork
    {
        // Entries persist after departure so committed swaps cannot disappear.
        private readonly NetworkList<CansGhostSnapshot> ghostStates = new NetworkList<CansGhostSnapshot>();
        private double nextGhostPose;

        private void SyncGhost()
        {
            if (game == null) return;
            if (!IsServer)
            {
                for (int i = 0; i < ghostStates.Count; i++) game.ReceiveGhost(ghostStates[i]);
                return;
            }
            if (ghostStates.Count > game.Ghosts.Count) ghostStates.Clear();
            double now = NetworkManager.ServerTime.Time;
            bool publishPose = now >= nextGhostPose;
            if (publishPose) nextGhostPose = now + CansGhostRules.NetworkInterval;
            for (int i = 0; i < game.Ghosts.Count; i++)
            {
                var role = game.Ghosts[i];
                var snapshot = new CansGhostSnapshot { State = role.State, Pose = role.Pose };
                if (i >= ghostStates.Count) { ghostStates.Add(snapshot); continue; }
                if (!publishPose) snapshot.Pose = ghostStates[i].Pose;
                if (!ghostStates[i].Equals(snapshot)) ghostStates[i] = snapshot;
            }
        }

        public void SendGhostInput(Vector3 direction, float yaw)
        {
            if (IsSpawned && !IsServer) GhostFlightRpc(direction, yaw);
        }

        [Rpc(SendTo.Server, RequireOwnership = false, Delivery = RpcDelivery.Unreliable)]
        private void GhostFlightRpc(Vector3 direction, float yaw, RpcParams p = default)
        {
            game?.GetGhost((int)p.Receive.SenderClientId)?.AcceptInput((int)p.Receive.SenderClientId, direction, yaw);
        }

        public void RequestGhostTrick(int target)
        {
            if (IsSpawned && !IsServer) GhostTrickRpc(target);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void GhostTrickRpc(int target, RpcParams p = default)
        {
            var role = game?.GetGhost((int)p.Receive.SenderClientId);
            bool accepted = role != null && role.TryTrick((int)p.Receive.SenderClientId, target);
            GhostReplyRpc(accepted, RpcTarget.Single(p.Receive.SenderClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void GhostReplyRpc(bool accepted, RpcParams p = default) => game?.LocalGhost?.ReceiveReply(accepted);
    }
}
