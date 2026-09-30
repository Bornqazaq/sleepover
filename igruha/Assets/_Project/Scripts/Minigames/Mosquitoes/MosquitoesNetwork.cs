using System;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    public struct MosquitoesSnapshot : INetworkSerializable, IEquatable<MosquitoesSnapshot>
    {
        public int GiantId;
        public byte Phase;
        public bool Lamp;
        public float Sleep, Transition, Immunity, Countdown, EntryRemaining;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref GiantId); s.SerializeValue(ref Phase); s.SerializeValue(ref Lamp);
            s.SerializeValue(ref Sleep); s.SerializeValue(ref Transition); s.SerializeValue(ref Immunity); s.SerializeValue(ref Countdown); s.SerializeValue(ref EntryRemaining);
        }
        public bool Equals(MosquitoesSnapshot o) => GiantId == o.GiantId && Phase == o.Phase && Lamp == o.Lamp &&
            Sleep == o.Sleep && Transition == o.Transition && Immunity == o.Immunity && Countdown == o.Countdown && EntryRemaining == o.EntryRemaining;
    }
    public sealed class MosquitoesNetwork : NetworkBehaviour
    {
        private readonly NetworkVariable<MosquitoesSnapshot> state = new NetworkVariable<MosquitoesSnapshot>(new MosquitoesSnapshot { GiantId = -1 });
        public NetworkList<int> Living;
        private MosquitoesMinigame game;
        private float publishIn;
        private readonly System.Collections.Generic.List<int> pendingDepartures = new System.Collections.Generic.List<int>(8);
        private void Awake()
        {
            Living = new NetworkList<int>();
            game = GetComponent<MosquitoesMinigame>();
        }
        public override void OnNetworkSpawn()
        {
            if (IsServer) NetworkManager.OnClientDisconnectCallback += Disconnected;
        }
        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= Disconnected;
        }
        private void Update()
        {
            if (!IsSpawned || game == null || !game.RosterReady) return;
            if (!IsServer) { if (state.Value.GiantId >= 0) game.Receive(state.Value); return; }
            if (NetworkManager.ShutdownInProgress || !NetworkManager.IsListening) return;
            foreach (int id in pendingDepartures) game.PlayerGone(id);
            pendingDepartures.Clear();
            publishIn -= Time.unscaledDeltaTime;
            if (publishIn <= 0) { publishIn = .1f; Publish(); }
        }
        public void Publish()
        {
            if (!IsSpawned || !IsServer || game.Sleep == null) return;
            state.Value = new MosquitoesSnapshot { GiantId = game.GiantId, Phase = (byte)game.Sleep.Phase,
                Lamp = game.Sleep.LampOn, Sleep = game.Sleep.Sleep, Transition = game.Sleep.TransitionRemaining,
                Immunity = game.Sleep.ImmunityRemaining, Countdown = game.Countdown, EntryRemaining = game.EntryRemaining };
            for (int i = Living.Count - 1; i >= 0; i--) if (!game.IsLiving(Living[i])) Living.RemoveAt(i);
            foreach (MosquitoBody body in game.Bodies) if (body != null && game.IsLiving(body.Id) && !Living.Contains(body.Id)) Living.Add(body.Id);
        }
        public void RequestBed()
        {
            if (!IsSpawned) game.TryBed(game.LocalId); else BedServerRpc();
        }
        public void RequestSwat()
        {
            if (!IsSpawned) game.TrySwat(game.LocalId); else SwatServerRpc();
        }
        [ServerRpc(RequireOwnership = false)] private void BedServerRpc(ServerRpcParams rpc = default) => game.TryBed((int)rpc.Receive.SenderClientId);
        [ServerRpc(RequireOwnership = false)] private void SwatServerRpc(ServerRpcParams rpc = default) => game.TrySwat((int)rpc.Receive.SenderClientId);
        private void Disconnected(ulong id) => pendingDepartures.Add((int)id);
        public void Effect(byte kind, Vector3 position, int attacker = -1)
        {
            if (IsSpawned && IsServer) EffectClientRpc(kind, position, attacker); else if (!IsSpawned) game.PlayEffect(kind, position, attacker);
        }
        [ClientRpc] private void EffectClientRpc(byte kind, Vector3 position, int attacker) => game.PlayEffect(kind, position, attacker);
    }
}
