using System;
using Unity.Netcode;
using Igruha.Core.Session;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Состав дорожки и её счёт — то, что обязано быть одинаковым у всех.
    ///
    /// Состав и счёт лежат в одной строке, потому что и меняются они одним
    /// решением сервера: очко засчитывается дорожке целиком, а не человеку,
    /// и дисконнект правит ту же строку.
    /// </summary>
    /// <remarks>
    /// <b>Порядок списка — это раскладка по дорожкам.</b> Сервер публикует
    /// строку на каждую дорожку, которой достался состав, и порядок не меняет
    /// до конца раунда: дорожка, потерявшая всех участников, остаётся в списке
    /// пустой строкой, а не выпадает из него. Иначе у клиента поехали бы номера
    /// дорожек — у всех, кто стоит правее выбывшей.
    /// </remarks>
    public struct HoleInWallTrackNetState : INetworkSerializable, IEquatable<HoleInWallTrackNetState>
    {
        /// <summary>Номер дорожки на арене, с нуля.</summary>
        public byte TrackIndex;

        /// <summary>Первый участник. <see cref="PairAssignment.NoPlayer"/> — места нет.</summary>
        public int FirstMemberId;

        /// <summary>Напарник либо <see cref="PairAssignment.NoPlayer"/>: дорожка одиночки.</summary>
        public int SecondMemberId;

        /// <summary>Сколько стен дорожка прошла. Это же очко каждому её участнику.</summary>
        public byte Score;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref TrackIndex);
            serializer.SerializeValue(ref FirstMemberId);
            serializer.SerializeValue(ref SecondMemberId);
            serializer.SerializeValue(ref Score);
        }

        public bool Equals(HoleInWallTrackNetState other) =>
            TrackIndex == other.TrackIndex &&
            FirstMemberId == other.FirstMemberId &&
            SecondMemberId == other.SecondMemberId &&
            Score == other.Score;
    }

    /// <summary>
    /// Рисунок одной стены одной дорожки: обе позы, оба места, подвох и то,
    /// во что он превратит вырезы.
    /// </summary>
    /// <remarks>
    /// <b>Едет структура, а не сид.</b> Генератор детерминирован, и соблазн
    /// прислать четыре байта вместо шестисот велик — но тогда рисунок стены
    /// держался бы на побитовом совпадении арифметики с плавающей точкой
    /// на разных платформах. Ошибись оно на единицу в младшем разряде, и
    /// сервер судил бы по одному вырезу, а игрок целился в другой — ровно
    /// та поломка, которой боится вся спека. Расписание объявляется один раз
    /// на раунд, и шестьсот байт за снятый вопрос — цена никакая.
    ///
    /// Момента старта здесь нет намеренно: он считается из объявленного
    /// момента начала раунда и таблицы подъездов в конфиге, одинаково на всех
    /// машинах. На движение стены не уходит ни одного пакета.
    /// </remarks>
    public struct HoleInWallWallNetState : INetworkSerializable, IEquatable<HoleInWallWallNetState>
    {
        public byte TrackIndex;

        /// <summary>Номер стены с нуля.</summary>
        public byte WallIndex;

        public byte FirstPose;
        public byte SecondPose;

        /// <summary>Во что превращаются вырезы при <see cref="WallTrick.Morph"/>.</summary>
        public byte MorphFirst;
        public byte MorphSecond;

        public byte Trick;

        /// <summary>Вырезов два: дорожка пары. Ложь — дорожка одиночки.</summary>
        public bool HasSecond;

        public float FirstOffset;
        public float SecondOffset;

        public static HoleInWallWallNetState From(int trackIndex, int wallIndex, in WallPattern pattern) =>
            new HoleInWallWallNetState
            {
                TrackIndex = (byte)trackIndex,
                WallIndex = (byte)wallIndex,
                FirstPose = (byte)pattern.First.Pose,
                SecondPose = (byte)pattern.Second.Pose,
                MorphFirst = (byte)pattern.MorphFirst,
                MorphSecond = (byte)pattern.MorphSecond,
                Trick = (byte)pattern.Trick,
                HasSecond = pattern.HasSecond,
                FirstOffset = pattern.First.Offset,
                SecondOffset = pattern.Second.Offset
            };

        public WallPattern ToPattern() =>
            new WallPattern(
                new WallCutoutSpec((HoleInWallPose)FirstPose, FirstOffset),
                new WallCutoutSpec((HoleInWallPose)SecondPose, SecondOffset),
                HasSecond,
                (WallTrick)Trick,
                (HoleInWallPose)MorphFirst,
                (HoleInWallPose)MorphSecond);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref TrackIndex);
            serializer.SerializeValue(ref WallIndex);
            serializer.SerializeValue(ref FirstPose);
            serializer.SerializeValue(ref SecondPose);
            serializer.SerializeValue(ref MorphFirst);
            serializer.SerializeValue(ref MorphSecond);
            serializer.SerializeValue(ref Trick);
            serializer.SerializeValue(ref HasSecond);
            serializer.SerializeValue(ref FirstOffset);
            serializer.SerializeValue(ref SecondOffset);
        }

        public bool Equals(HoleInWallWallNetState other) =>
            TrackIndex == other.TrackIndex &&
            WallIndex == other.WallIndex &&
            FirstPose == other.FirstPose &&
            SecondPose == other.SecondPose &&
            MorphFirst == other.MorphFirst &&
            MorphSecond == other.MorphSecond &&
            Trick == other.Trick &&
            HasSecond == other.HasSecond &&
            FirstOffset.Equals(other.FirstOffset) &&
            SecondOffset.Equals(other.SecondOffset);
    }

    /// <summary>
    /// Поза одного участника. Единственное действие игрока в этой игре
    /// и единственное, что решает исход, — поэтому едет как состояние
    /// под сервером, а не как локальное значение у каждого своё.
    ///
    /// Видят её все: половина удовольствия в том, чтобы смотреть, как сосед
    /// не угадал силуэт.
    /// </summary>
    public struct HoleInWallPoseNetState : INetworkSerializable, IEquatable<HoleInWallPoseNetState>
    {
        public int PlayerId;

        /// <summary>Номер позы 1…4, ноль — <see cref="HoleInWallPose.None"/>.</summary>
        public byte Pose;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Pose);
        }

        public bool Equals(HoleInWallPoseNetState other) =>
            PlayerId == other.PlayerId && Pose == other.Pose;
    }

    /// <summary>
    /// Стадия раунда: стадия = стена. Момент конца, а не остаток, — дальше
    /// каждая машина считает сама от общих часов.
    /// </summary>
    public struct HoleInWallStageNetState : INetworkSerializable, IEquatable<HoleInWallStageNetState>
    {
        public int Subround;
        public byte Stage;
        public double EndTime;
        public float Duration;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Subround);
            serializer.SerializeValue(ref Stage);
            serializer.SerializeValue(ref EndTime);
            serializer.SerializeValue(ref Duration);
        }

        public bool Equals(HoleInWallStageNetState other) =>
            Subround == other.Subround &&
            Stage == other.Stage &&
            EndTime.Equals(other.EndTime) &&
            Duration.Equals(other.Duration);
    }
}
