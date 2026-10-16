using Igruha.Core.UI;
using Unity.Netcode;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>Pauses only the owner's cabinet. The shared room and other cabinets keep running.</summary>
    public abstract class ArcadeStation : HubActivityStation
    {
        private readonly NetworkVariable<bool> paused = new NetworkVariable<bool>();
        private bool offlinePaused, sentPause;
        private uint sentPauseSession;
        private bool pauseInitialized;

        protected abstract uint ArcadeSession { get; }
        public bool IsArcadePaused => IsSpawned ? paused.Value : offlinePaused;

        /// <summary>Called even while gameplay input is disabled by Esc. Reliable transitions cannot be lost.</summary>
        protected bool UpdateLocalPause()
        {
            bool localPause = PauseScreen.Current != null && PauseScreen.Current.IsPaused;
            if (!pauseInitialized || sentPause != localPause || sentPauseSession != ArcadeSession)
            {
                pauseInitialized = true; sentPause = localPause; sentPauseSession = ArcadeSession;
                if (IsSpawned) PauseArcadeServerRpc(localPause, ArcadeSession);
                else SetPaused(localPause, ArcadeSession, Occupant);
            }
            return localPause || IsArcadePaused;
        }

        protected void ResetArcadePause()
        {
            if (IsSpawned && IsServer) paused.Value = false;
            else offlinePaused = false;
            pauseInitialized = false;
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void PauseArcadeServerRpc(bool value, uint session, RpcParams rpc = default) =>
            SetPaused(value, session, rpc.Receive.SenderClientId);

        private void SetPaused(bool value, uint session, ulong sender)
        {
            if (!HasAuthority || Occupant == NoOccupant || sender != Occupant || session != ArcadeSession) return;
            if (IsSpawned) paused.Value = value;
            else offlinePaused = value;
        }
    }
}
