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
        public int StormStage, StormTarget;
        public double StormCloses, StormIdle;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T:IReaderWriter
        {
            s.SerializeValue(ref Ready);s.SerializeValue(ref Finished);
            s.SerializeValue(ref Holder);s.SerializeValue(ref Pickup);s.SerializeValue(ref Previous);s.SerializeValue(ref Winner);
            s.SerializeValue(ref Begins);s.SerializeValue(ref Ends);s.SerializeValue(ref Spawn);
            s.SerializeValue(ref StormStage);s.SerializeValue(ref StormTarget);s.SerializeValue(ref StormCloses);s.SerializeValue(ref StormIdle);
        }
        public bool Equals(OneBulletNetHeader b)=>Ready==b.Ready&&Finished==b.Finished&&Holder==b.Holder&&Pickup==b.Pickup&&Previous==b.Previous&&Winner==b.Winner&&Begins==b.Begins&&Ends==b.Ends&&Spawn==b.Spawn&&StormStage==b.StormStage&&StormTarget==b.StormTarget&&StormCloses==b.StormCloses&&StormIdle==b.StormIdle;
    }
    public struct OneBulletNetPlayer : INetworkSerializable,IEquatable<OneBulletNetPlayer>
    {
        public int Id,Kills,Cans; public bool Alive; public double Life,DangerSince;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T:IReaderWriter
        {s.SerializeValue(ref Id);s.SerializeValue(ref Kills);s.SerializeValue(ref Alive);s.SerializeValue(ref Life);s.SerializeValue(ref Cans);s.SerializeValue(ref DangerSince);}
        public bool Equals(OneBulletNetPlayer b)=>Id==b.Id&&Kills==b.Kills&&Alive==b.Alive&&Life==b.Life&&Cans==b.Cans&&DangerSince==b.DangerSince;
    }
    /// <summary>State is replicated only on changes. Sender identity never comes from a client argument.</summary>
    public sealed class OneBulletNetwork : NetworkBehaviour
    {
        private readonly NetworkVariable<OneBulletNetHeader> header = new NetworkVariable<OneBulletNetHeader>();
        private NetworkList<OneBulletNetPlayer> players;
        private NetworkList<OneBulletNetCan> cans;
        private OneBulletDecoys decoys;
        private readonly List<int> leavers = new List<int>(8);
        private OneBulletMinigame game;
        private bool dirty;
        public bool Active => IsSpawned;
        private void Awake(){game=GetComponent<OneBulletMinigame>();players=new NetworkList<OneBulletNetPlayer>();cans=new NetworkList<OneBulletNetCan>();decoys=GetComponent<OneBulletDecoys>();}
        public override void OnNetworkSpawn()
        {
            header.OnValueChanged+=HeaderChanged;players.OnListChanged+=PlayersChanged;
            game.Changed+=Publish;
            cans.OnListChanged+=CansChanged;
            if(decoys!=null){decoys.StateChanged+=PublishCan;decoys.ThrowRequested+=RequestCan;}
            if(IsServer){NetworkManager.OnClientDisconnectCallback+=Disconnected;Publish();}
            else dirty=true;
        }
        public override void OnNetworkDespawn()
        {
            header.OnValueChanged-=HeaderChanged;players.OnListChanged-=PlayersChanged;game.Changed-=Publish;
            if(IsServer&&NetworkManager!=null)NetworkManager.OnClientDisconnectCallback-=Disconnected;
            cans.OnListChanged-=CansChanged;
            if(decoys!=null){decoys.StateChanged-=PublishCan;decoys.ThrowRequested-=RequestCan;}
            base.OnNetworkDespawn();
        }
        public void RefreshPresentation()=>dirty=true;
        private void HeaderChanged(OneBulletNetHeader old,OneBulletNetHeader value)=>dirty=true;
        private void PlayersChanged(NetworkListEvent<OneBulletNetPlayer> change)=>dirty=true;
        private void CansChanged(NetworkListEvent<OneBulletNetCan> change)=>dirty=true;
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
            {var p=players[i];game.Round.ApplyRecord(p.Id,p.Alive,p.Kills,p.Life,p.Cans,p.DangerSince);}
            if(game.Storm!=null)
            {game.Storm.State.Apply(h.StormStage,h.StormTarget,h.StormCloses,h.StormIdle);game.Storm.RefreshPresentation();}
            if(decoys!=null)for(int i=0;i<cans.Count;i++)decoys.ApplyState(i,cans[i].Value);
            game.ApplySnapshotPresentation(previousHolder);
        }
        public void Publish()
        {
            if(!IsSpawned||!IsServer||game.Round.Records.Count==0)return;
            var r=game.Round;
            if(decoys!=null)
            {
                while(cans.Count>decoys.Count)cans.RemoveAt(cans.Count-1);
                for(int i=0;i<decoys.Count;i++)PublishCan(i,decoys.GetState(i));
            }
            header.Value=new OneBulletNetHeader{Ready=true,Finished=r.Finished,Holder=r.Holder,Pickup=r.Pickup,Previous=r.PreviousPickup,Winner=r.Winner,Begins=r.BeginsAt,Ends=r.EndsAt,Spawn=r.SpawnAt,StormStage=game.Storm?.State.Stage??0,StormTarget=game.Storm?.State.Target??0,StormCloses=game.Storm?.State.ClosesAt??0,StormIdle=game.Storm?.State.IdleAt??0};
            for(int i=0;i<r.Records.Count;i++)
            {
                var p=r.Records[i];var next=new OneBulletNetPlayer{Id=p.Id,Alive=p.Alive,Kills=p.Kills,Life=p.Life,Cans=p.Cans,DangerSince=p.DangerSince};
                if(i>=players.Count)players.Add(next);else if(!players[i].Equals(next))players[i]=next;
            }
        }
        private void PublishCan(int index, OneBulletCanState state)
        {
            if (!IsSpawned || !IsServer) return;
            while (cans.Count < decoys.Count) cans.Add(default);
            var next = new OneBulletNetCan { Value = state };
            if (!cans[index].Equals(next)) cans[index] = next;
        }
        public void RequestCan(Vector3 direction)
        {
            if (!IsSpawned || !OneBulletMinigame.ValidDirection(direction)) return;
            ThrowCanServerRpc(direction);
        }
        [ServerRpc(RequireOwnership = false)]
        private void ThrowCanServerRpc(Vector3 direction, ServerRpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (sender > int.MaxValue || !NetworkManager.ConnectedClients.ContainsKey(sender)) return;
            decoys?.QueueThrow((int)sender, direction);
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
