using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Minigames.OneBullet
{
    public struct OneBulletNetHeader : INetworkSerializable, IEquatable<OneBulletNetHeader>
    {
        public bool Ready, Finished;
        public int Holder, Pickup, Previous, Winner;
        public double Begins, Ends, Spawn;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T:IReaderWriter
        {
            s.SerializeValue(ref Ready);s.SerializeValue(ref Finished);
            s.SerializeValue(ref Holder);s.SerializeValue(ref Pickup);s.SerializeValue(ref Previous);s.SerializeValue(ref Winner);
            s.SerializeValue(ref Begins);s.SerializeValue(ref Ends);s.SerializeValue(ref Spawn);
        }
        public bool Equals(OneBulletNetHeader b)=>Ready==b.Ready&&Finished==b.Finished&&Holder==b.Holder&&Pickup==b.Pickup&&Previous==b.Previous&&Winner==b.Winner&&Begins==b.Begins&&Ends==b.Ends&&Spawn==b.Spawn;
    }
    public struct OneBulletNetPlayer : INetworkSerializable,IEquatable<OneBulletNetPlayer>
    {
        public int Id,Kills; public bool Alive; public double Life;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T:IReaderWriter
        {s.SerializeValue(ref Id);s.SerializeValue(ref Kills);s.SerializeValue(ref Alive);s.SerializeValue(ref Life);}
        public bool Equals(OneBulletNetPlayer b)=>Id==b.Id&&Kills==b.Kills&&Alive==b.Alive&&Life==b.Life;
    }
    /// <summary>State is replicated only on changes. Sender identity never comes from a client argument.</summary>
    public sealed class OneBulletNetwork : NetworkBehaviour
    {
        private readonly NetworkVariable<OneBulletNetHeader> header = new NetworkVariable<OneBulletNetHeader>();
        private NetworkList<OneBulletNetPlayer> players;
        private readonly List<int> leavers = new List<int>(8);
        private OneBulletMinigame game;
        private bool dirty;
        public bool Active => IsSpawned;
        private void Awake(){game=GetComponent<OneBulletMinigame>();players=new NetworkList<OneBulletNetPlayer>();}
        public override void OnNetworkSpawn()
        {
            header.OnValueChanged+=HeaderChanged;players.OnListChanged+=PlayersChanged;
            game.Changed+=Publish;
            if(IsServer){NetworkManager.OnClientDisconnectCallback+=Disconnected;Publish();}
            else dirty=true;
        }
        public override void OnNetworkDespawn()
        {
            header.OnValueChanged-=HeaderChanged;players.OnListChanged-=PlayersChanged;game.Changed-=Publish;
            if(IsServer&&NetworkManager!=null)NetworkManager.OnClientDisconnectCallback-=Disconnected;
            base.OnNetworkDespawn();
        }
        public void RefreshPresentation()=>dirty=true;
        private void HeaderChanged(OneBulletNetHeader old,OneBulletNetHeader value)=>dirty=true;
        private void PlayersChanged(NetworkListEvent<OneBulletNetPlayer> change)=>dirty=true;
        private void Disconnected(ulong id){if(IsServer&&id<=int.MaxValue)leavers.Add((int)id);}
        private void FixedUpdate()
        {
            if(!IsSpawned)return;
            if(IsServer)
            {
                for(int i=0;i<leavers.Count;i++)game.Leave(leavers[i]);
                leavers.Clear();return;
            }
            // All list deltas are delivered before physics. Never apply half a roster.
            if(!dirty||!header.Value.Ready||!game.LocalRosterReady)return;
            dirty=false;var h=header.Value;
            int previousHolder=game.Round.Holder;
            game.Round.ApplyHeader(h.Holder,h.Pickup,h.Previous,h.Begins,h.Ends,h.Spawn,h.Finished,h.Winner);
            for(int i=0;i<players.Count;i++)
            {var p=players[i];game.Round.ApplyRecord(p.Id,p.Alive,p.Kills,p.Life);}
            game.ApplySnapshotPresentation(previousHolder);
        }
        public void Publish()
        {
            if(!IsSpawned||!IsServer||game.Round.Records.Count==0)return;
            var r=game.Round;
            header.Value=new OneBulletNetHeader{Ready=true,Finished=r.Finished,Holder=r.Holder,Pickup=r.Pickup,Previous=r.PreviousPickup,Winner=r.Winner,Begins=r.BeginsAt,Ends=r.EndsAt,Spawn=r.SpawnAt};
            for(int i=0;i<r.Records.Count;i++)
            {
                var p=r.Records[i];var next=new OneBulletNetPlayer{Id=p.Id,Alive=p.Alive,Kills=p.Kills,Life=p.Life};
                if(i>=players.Count)players.Add(next);else if(!players[i].Equals(next))players[i]=next;
            }
        }
        public void RequestShot(Vector3 direction)
        {
            if(!IsSpawned||!OneBulletMinigame.ValidDirection(direction))return;
            FireServerRpc(direction);
        }
        [ServerRpc(RequireOwnership=false)]
        private void FireServerRpc(Vector3 direction,ServerRpcParams rpc=default)
        {
            ulong sender=rpc.Receive.SenderClientId;
            if(sender>int.MaxValue||!NetworkManager.ConnectedClients.ContainsKey(sender))return;
            game.QueueShot((int)sender,direction);
        }
        public void PublishShot(Vector3 origin,Vector3 end,bool hit)
        {if(IsSpawned&&IsServer)ShotClientRpc(origin,end,hit);}
        [ClientRpc] private void ShotClientRpc(Vector3 origin,Vector3 end,bool hit)
        {if(!IsServer)game.ApplyShotPresentation(origin,end,hit);}
    }
}
