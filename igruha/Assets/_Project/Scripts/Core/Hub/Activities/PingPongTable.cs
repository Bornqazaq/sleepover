using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>Server-owned rally. The client submits one timestamped intention per incoming flight.</summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PingPongTable : NetworkBehaviour
    {
        [SerializeField] private PingPongSeat left;
        [SerializeField] private PingPongSeat right;
        [SerializeField] private PingPongPresentation presentation;
        [SerializeField] private float serveDelay = 1f;
        private readonly NetworkVariable<PingPongState> state = new NetworkVariable<PingPongState>();
        private PingPongState offlineState;
        private PingPongState prediction;
        private bool predicting;
        private double predictionExpires;
        private uint nextFlight;
        private uint leftAttempt, rightAttempt, localAttempt;
        private double nextServeAt;
        private byte nextReceiver;
        private bool stopped;

        public PingPongState State => IsSpawned ? state.Value : offlineState;
        public int LocalSide => left.IsLocal ? 0 : right.IsLocal ? 1 : -1;
        public PingPongSeat Seat(int side) => side == 0 ? left : right;
        private bool HasAuthority => !stopped && (IsSpawned ? IsServer : WorldAuthority.HasAuthority);
        private bool HasPlayers => left.Occupant != HubActivityStation.NoOccupant || right.Occupant != HubActivityStation.NoOccupant;

        public bool AlreadySeated(PlayerController player, byte side)
        {
            NetworkObject body = player.GetComponent<NetworkObject>();
            ulong clientId = body != null && body.IsSpawned ? body.OwnerClientId : 0;
            return Seat(1 - side).Occupant == clientId;
        }

        public PingPongState DisplayState
        {
            get
            {
                if (predicting && NetworkClock.Now <= predictionExpires &&
                    State.Phase == PingPongPhase.Playing && State.Flight.Id == prediction.Previous.Id)
                    return prediction;
                return State;
            }
        }

        public override void OnNetworkSpawn()
        {
            stopped = false;
            base.OnNetworkSpawn();
        }

        public override void OnNetworkDespawn()
        {
            stopped = true;
            predicting = false;
            base.OnNetworkDespawn();
        }

        /// <summary>A composition change starts a fresh serve; no one inherits a ball already at their face.</summary>
        public void SeatsChanged(byte changedSide)
        {
            if (!HasAuthority) return;
            predicting = false;
            if (!HasPlayers)
            {
                Publish(new PingPongState { Phase = PingPongPhase.Idle });
                return;
            }
            nextReceiver = Seat(changedSide).Occupant != HubActivityStation.NoOccupant
                ? changedSide : (byte)(1 - changedSide);
            Serve(NetworkClock.Now + serveDelay);
        }

        public void RequestHit(byte side)
        {
            if (side > 1 || !Seat(side).IsLocal) return;
            PingPongState current = State;
            double stamp = NetworkClock.Now;
            if (current.Phase != PingPongPhase.Playing || current.Flight.Target != side ||
                localAttempt == current.Flight.Id || stamp < current.Flight.StartsAt) return;
            localAttempt = current.Flight.Id;

            bool timely = PingPongRules.InWindow(current.Flight, stamp);
            if (presentation != null) presentation.ShowAttempt(timely, stamp < current.Flight.ContactAt);
            if (timely && IsSpawned && !IsServer)
            {
                prediction = new PingPongState { Phase = PingPongPhase.Playing,
                    Previous = current.Flight, Flight = PingPongRules.Return(current.Flight, current.Flight.Id + 1) };
                predictionExpires = stamp + PingPongRules.MaximumRewind + PingPongRules.NetworkSlack;
                predicting = true;
            }

            if (IsSpawned) HitServerRpc(side, current.Flight.Id, stamp);
            else TryHit(side, Seat(side).Occupant, current.Flight.Id, stamp, stamp, 0);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void HitServerRpc(byte side, uint flightId, double stamp, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            float rtt = sender == NetworkManager.ServerClientId ? 0 :
                NetworkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(sender) * .001f;
            TryHit(side, sender, flightId, stamp, NetworkClock.Now, rtt);
        }

        private bool TryHit(byte side, ulong sender, uint flightId, double stamp, double receivedAt, float rtt)
        {
            if (!HasAuthority || side > 1 || sender == HubActivityStation.NoOccupant ||
                Seat(side).Occupant != sender || Seat(side).Phase != HubActivityPhase.Occupied) return false;
            PingPongState current = State;
            if (current.Phase != PingPongPhase.Playing || current.Flight.Id != flightId ||
                current.Flight.Target != side || !PingPongRules.ValidTimestamp(stamp, receivedAt, PingPongRules.RewindBudget(rtt)))
                return false;
            if ((side == 0 ? leftAttempt : rightAttempt) == flightId) return false;
            if (side == 0) leftAttempt = flightId; else rightAttempt = flightId;
            if (!PingPongRules.InWindow(current.Flight, stamp)) return false;
            Return(current);
            return true;
        }

        private void Update()
        {
            if (!HasAuthority) return;
            PingPongState current = State;
            double now = NetworkClock.Now;
            if (current.Phase == PingPongPhase.Missed)
            {
                if (now >= nextServeAt && HasPlayers) Serve(now);
                return;
            }
            if (current.Phase != PingPongPhase.Playing) return;
            PingPongSeat target = Seat(current.Flight.Target);
            if (target.Occupant == HubActivityStation.NoOccupant)
            {
                // Publish an automatic rebound before contact so clients can interpolate both legs.
                if (now >= current.Flight.ContactAt - PingPongRules.EarlyWindow) Return(current);
            }
            else
            {
                float rtt = IsSpawned && target.Occupant != NetworkManager.ServerClientId
                    ? NetworkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(target.Occupant) * .001f : 0;
                if (now <= current.Flight.ContactAt + PingPongRules.LateWindow + PingPongRules.RewindBudget(rtt)) return;
                current.Phase = PingPongPhase.Missed;
                Publish(current);
                nextReceiver = current.Flight.Target;
                nextServeAt = now + serveDelay;
            }
        }

        private void Serve(double startsAt)
        {
            Publish(new PingPongState { Phase = PingPongPhase.Playing,
                Flight = PingPongRules.Serve(NewFlightId(), nextReceiver, startsAt) });
        }

        private void Return(PingPongState current)
        {
            Publish(new PingPongState { Phase = PingPongPhase.Playing, Previous = current.Flight,
                Flight = PingPongRules.Return(current.Flight, NewFlightId()) });
        }

        private uint NewFlightId()
        {
            nextFlight++;
            if (nextFlight == 0) nextFlight++;
            return nextFlight;
        }

        private void Publish(PingPongState value)
        {
            if (IsSpawned) state.Value = value;
            else offlineState = value;
        }
    }
}
