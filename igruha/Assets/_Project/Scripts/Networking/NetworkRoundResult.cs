using Igruha.Core.Minigame;
using Igruha.Core.UI;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Networking
{
    /// <summary>Один снимок места и показателей: итоговый RPC не обгонит их отдельную репликацию.</summary>
    public struct NetworkRoundResult : INetworkSerializable
    {
        public int PlayerId, Place, Points, Total;
        private FixedString64Bytes value;
        private FixedString128Bytes note;
        private uint accent;

        public NetworkRoundResult(MinigameResults.PlayerResult entry)
        {
            PlayerId = entry.PlayerId; Place = entry.Place; Points = entry.Points; Total = entry.Total;
            value = new FixedString64Bytes(entry.Detail.Value ?? "—");
            note = new FixedString128Bytes(entry.Detail.Note ?? "");
            Color32 color = entry.Detail.Accent;
            accent = (uint)(color.r | color.g << 8 | color.b << 16 | color.a << 24);
        }
        public RoundResultDetail Detail => new RoundResultDetail(value.ToString(), note.ToString(),
            new Color32((byte)accent, (byte)(accent >> 8), (byte)(accent >> 16), (byte)(accent >> 24)));

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId); serializer.SerializeValue(ref Place);
            serializer.SerializeValue(ref Points); serializer.SerializeValue(ref Total);
            serializer.SerializeValue(ref value); serializer.SerializeValue(ref note); serializer.SerializeValue(ref accent);
        }
    }
}
