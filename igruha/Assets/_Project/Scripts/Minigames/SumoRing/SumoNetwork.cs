using System;
using Unity.Netcode;

namespace Igruha.Minigames.SumoRing
{
    public struct SumoNetHeader : INetworkSerializable, IEquatable<SumoNetHeader>
    {
        public bool Ready, Finished;
        public double Begins, Ended;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        { s.SerializeValue(ref Ready); s.SerializeValue(ref Finished); s.SerializeValue(ref Begins); s.SerializeValue(ref Ended); }
        public bool Equals(SumoNetHeader other) => Ready == other.Ready && Finished == other.Finished && Begins == other.Begins && Ended == other.Ended;
    }
    public struct SumoNetPlayer : INetworkSerializable, IEquatable<SumoNetPlayer>
    {
        public int Id, Group;
        public double Life;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        { s.SerializeValue(ref Id); s.SerializeValue(ref Group); s.SerializeValue(ref Life); }
        public bool Equals(SumoNetPlayer other) => Id == other.Id && Group == other.Group && Life == other.Life;
    }
    /// <summary>Ring animation needs only the server's start time. Results and deaths remain server-owned.</summary>
    public sealed class SumoNetwork : NetworkBehaviour
    {
        private readonly NetworkVariable<SumoNetHeader> header = new NetworkVariable<SumoNetHeader>();
        private NetworkList<SumoNetPlayer> roster;
        private NetworkList<SumoCombatState> combat;
        private SumoMinigame game;
        private bool dirty;
        private void Awake() { game = GetComponent<SumoMinigame>(); roster = new NetworkList<SumoNetPlayer>(); combat = new NetworkList<SumoCombatState>(); }
        public override void OnNetworkSpawn()
        {
            game.Changed += Publish;
            game.Combat.StateChanged += PublishCombat;
            combat.OnListChanged += CombatChanged;
            header.OnValueChanged += HeaderChanged; roster.OnListChanged += RosterChanged;
            if (IsServer) { NetworkManager.OnClientDisconnectCallback += Disconnected; Publish(); }
            else dirty = true;
        }
        public override void OnNetworkDespawn()
        {
            game.Changed -= Publish;
            game.Combat.StateChanged -= PublishCombat;
            combat.OnListChanged -= CombatChanged;
            header.OnValueChanged -= HeaderChanged; roster.OnListChanged -= RosterChanged;
            if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= Disconnected;
            base.OnNetworkDespawn();
        }
        private void HeaderChanged(SumoNetHeader old, SumoNetHeader value) => dirty = true;
        private void RosterChanged(NetworkListEvent<SumoNetPlayer> change) => dirty = true;
        private void CombatChanged(NetworkListEvent<SumoCombatState> change)
        {
            if (IsServer) return;
            for (int i = 0; i < combat.Count; i++) game.Combat.ApplySnapshot(combat[i]);
        }
        private void Disconnected(ulong id) { if (IsServer && id <= int.MaxValue) game.Leave((int)id); }
        private void FixedUpdate()
        {
            if (!IsSpawned || IsServer || !dirty || !header.Value.Ready || !game.LocalRosterReady) return;
            // List and header deltas may arrive in either order; wait for the full initial roster.
            if (roster.Count < game.Participants.Count) return;
            dirty = false;
            var h = header.Value; game.Round.ApplyHeader(h.Begins, h.Finished, h.Ended);
            for (int i = 0; i < roster.Count; i++)
            { var p = roster[i]; game.Round.ApplyRecord(p.Id, p.Group, p.Life); }
            game.ApplySnapshotPresentation();
            for (int i = 0; i < combat.Count; i++) game.Combat.ApplySnapshot(combat[i]);
        }
        public void SubmitCombat(int id, SumoCommand command, float yaw, int sequence) => CombatCommandServerRpc(id, command, yaw, sequence);
        [ServerRpc(RequireOwnership = false)]
        private void CombatCommandServerRpc(int id, SumoCommand command, float yaw, int sequence, ServerRpcParams rpc = default)
        {
            if (!IsServer || sequence < 0 || !game.Combat.Active || !game.Combat.Owns(id, rpc.Receive.SenderClientId)) return;
            game.Combat.Enqueue(id, command, yaw, sequence);
        }
        private void PublishCombat()
        {
            if (!IsSpawned || !IsServer) return;
            for (int i = 0; i < game.Combat.Count; i++)
            {
                var next = game.Combat.StateAt(i);
                if (i >= combat.Count) combat.Add(next); else if (!combat[i].Equals(next)) combat[i] = next;
            }
        }
        public void BroadcastContact(SumoCombatHit hit)
        { if (IsServer && IsSpawned) ContactClientRpc(hit); }
        [ClientRpc]
        private void ContactClientRpc(SumoCombatHit hit) => game.Combat.PresentContact(hit);
        public void Publish()
        {
            if (!IsSpawned || !IsServer || !game.Round.Ready) return;
            var r = game.Round;
            for (int i = 0; i < r.Records.Count; i++)
            {
                var p = r.Records[i]; var next = new SumoNetPlayer { Id = p.Id, Group = p.Group, Life = p.Life };
                if (i >= roster.Count) roster.Add(next); else if (!roster[i].Equals(next)) roster[i] = next;
            }
            header.Value = new SumoNetHeader { Ready = true, Finished = r.Finished, Begins = r.BeginsAt, Ended = r.FinishedAt };
            PublishCombat();
        }
    }
}
