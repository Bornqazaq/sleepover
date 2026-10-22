using Unity.Netcode;

namespace Igruha.Minigames.CansOrder
{
    public sealed partial class CansOrderNetwork
    {
        public void RequestPracticePreview(ulong packed, byte count, int circle)
        {
            if (IsSpawned && !IsServer) PracticePreviewRpc(packed, count, circle);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void PracticePreviewRpc(ulong packed, byte count, int circle, RpcParams rpc = default)
        {
            if (game == null) return;
            ulong sender = rpc.Receive.SenderClientId;
            if (game.TryGetPracticeMatches((int)sender, packed, count, circle, out int matches))
                PracticePreviewReplyRpc(packed, count, circle, matches, RpcTarget.Single(sender, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void PracticePreviewReplyRpc(ulong packed, byte count, int circle, int matches, RpcParams rpc = default)
        {
            game?.ReceivePracticePreview(packed, count, circle, matches);
        }
    }
}
