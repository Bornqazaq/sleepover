using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace Igruha.Core.Minigame
{
    public struct TutorialParticipant : INetworkSerializable, IEquatable<TutorialParticipant>
    {
        public int PlayerId;
        public bool Ready;

        public TutorialParticipant(int playerId, bool ready = false)
        {
            PlayerId = playerId;
            Ready = ready;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Ready);
        }

        public bool Equals(TutorialParticipant other) => PlayerId == other.PlayerId && Ready == other.Ready;
    }

    /// <summary>Состав фиксируется на входе. Зрители и неизвестные отправители не голосуют.</summary>
    public sealed class TutorialReadiness
    {
        private readonly List<TutorialParticipant> participants = new List<TutorialParticipant>();
        public IReadOnlyList<TutorialParticipant> Participants => participants;
        public bool AllReady
        {
            get
            {
                if (participants.Count == 0) return false;
                for (int i = 0; i < participants.Count; i++)
                    if (!participants[i].Ready) return false;
                return true;
            }
        }

        public void Reset() => participants.Clear();
        public void Add(int playerId, bool ready = false)
        {
            if (IndexOf(playerId) < 0) participants.Add(new TutorialParticipant(playerId, ready));
        }

        public bool SetReady(int playerId, bool ready)
        {
            int index = IndexOf(playerId);
            if (index < 0 || participants[index].Ready == ready) return false;
            participants[index] = new TutorialParticipant(playerId, ready);
            return true;
        }

        public bool Remove(int playerId)
        {
            int index = IndexOf(playerId);
            if (index < 0) return false;
            participants.RemoveAt(index);
            return true;
        }

        public bool IsReady(int playerId)
        {
            int index = IndexOf(playerId);
            return index >= 0 && participants[index].Ready;
        }

        public void Apply(IReadOnlyList<TutorialParticipant> snapshot)
        {
            participants.Clear();
            for (int i = 0; i < snapshot.Count; i++) Add(snapshot[i].PlayerId, snapshot[i].Ready);
        }

        private int IndexOf(int playerId)
        {
            for (int i = 0; i < participants.Count; i++)
                if (participants[i].PlayerId == playerId) return i;
            return -1;
        }
    }

    public interface ITutorialNetworkBridge
    {
        void PublishTutorialReadiness(IReadOnlyList<TutorialParticipant> participants);
        void RequestTutorialReady(bool ready);
        void RequestPracticeRestart();
    }

    public interface ITutorialNetworkTarget
    {
        IReadOnlyList<TutorialParticipant> TutorialParticipants { get; }
        void ApplyTutorialReadiness(IReadOnlyList<TutorialParticipant> participants);
        void SetTutorialReady(int playerId, bool ready);
        void RemoveTutorialParticipant(int playerId);
        void RestartPractice(int playerId);
    }
}
