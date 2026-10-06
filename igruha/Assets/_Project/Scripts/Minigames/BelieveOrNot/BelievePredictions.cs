using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace Igruha.Minigames.BelieveOrNot
{
    public struct BelievePrediction : IEquatable<BelievePrediction>
    {
        public int PlayerId;
        public int WinnerId;
        public bool Equals(BelievePrediction other) => PlayerId == other.PlayerId && WinnerId == other.WinnerId;
    }

    /// <summary>Только раскрытые прогнозы. До открытия коробок эта структура в сеть не попадает.</summary>
    public struct BelievePredictionResults : INetworkSerializable, IEquatable<BelievePredictionResults>
    {
        public int Round;
        public int WinnerId;
        public FixedList128Bytes<BelievePrediction> Picks;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Round);
            serializer.SerializeValue(ref WinnerId);
            int count = Picks.Length;
            serializer.SerializeValue(ref count);
            if (serializer.IsReader) Picks.Clear();
            for (int i = 0; i < count; i++)
            {
                BelievePrediction pick = serializer.IsReader ? default : Picks[i];
                serializer.SerializeValue(ref pick.PlayerId);
                serializer.SerializeValue(ref pick.WinnerId);
                if (serializer.IsReader) Picks.Add(pick);
            }
        }

        public bool Equals(BelievePredictionResults other)
        {
            if (Round != other.Round || WinnerId != other.WinnerId || Picks.Length != other.Picks.Length) return false;
            for (int i = 0; i < Picks.Length; i++) if (!Picks[i].Equals(other.Picks[i])) return false;
            return true;
        }
    }

    /// <summary>Закрытый журнал сервера. Один окончательный прогноз на зрителя и кон.</summary>
    public sealed class BelievePredictions
    {
        private FixedList128Bytes<BelievePrediction> picks;
        private int round;
        private int seat0;
        private int seat1;

        public void Reset(int roundNumber, int firstPlayer, int secondPlayer)
        {
            round = roundNumber;
            seat0 = firstPlayer;
            seat1 = secondPlayer;
            picks.Clear();
        }

        public int ChoiceOf(int playerId)
        {
            for (int i = 0; i < picks.Length; i++) if (picks[i].PlayerId == playerId) return picks[i].WinnerId;
            return -1;
        }

        public bool TryPick(int requestedRound, int playerId, int winnerId, bool accepting,
            IReadOnlyList<BelieveEntry> entries)
        {
            if (!accepting || round <= 0 || requestedRound != round || playerId < 0 ||
                playerId == seat0 || playerId == seat1 || (winnerId != seat0 && winnerId != seat1) ||
                ChoiceOf(playerId) >= 0 || picks.Length == picks.Capacity) return false;

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].PlayerId != playerId || !entries[i].Present) continue;
                picks.Add(new BelievePrediction { PlayerId = playerId, WinnerId = winnerId });
                return true;
            }
            return false;
        }

        public void Remove(int playerId)
        {
            for (int i = picks.Length - 1; i >= 0; i--) if (picks[i].PlayerId == playerId) picks.RemoveAt(i);
        }

        public BelievePredictionResults Reveal(int winnerId) =>
            new BelievePredictionResults { Round = round, WinnerId = winnerId, Picks = picks };
    }
}
