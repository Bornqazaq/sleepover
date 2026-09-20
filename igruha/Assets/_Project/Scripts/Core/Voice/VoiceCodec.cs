using System;

namespace Igruha.Core.Voice
{
    /// <summary>
    /// Состояние кодера между кадрами: предсказанный отсчёт и шаг квантования.
    /// Кодер ведёт его непрерывно, чтобы не щёлкало на стыке кадров, и кладёт
    /// копию в заголовок каждого кадра — из-за этого кадр самодостаточен.
    /// </summary>
    public struct VoiceCodecState
    {
        public short Predictor;
        public byte StepIndex;
    }

    /// <summary>
    /// IMA ADPCM: 4 бита на отсчёт, сжатие ровно вчетверо.
    ///
    /// Выбран не от бедности: Opus потребовал бы нативного плагина под Windows
    /// и macOS, а Vivox — облачного проекта Unity Gaming Services, то есть
    /// интернета и учётных записей там, где мы играем по Tailscale. ADPCM
    /// звучит хуже Opus, но речь передаёт разборчиво и стоит 64 кбит/с на
    /// говорящего.
    ///
    /// Каждый кадр декодируется сам по себе. Заголовок несёт состояние, с
    /// которого кодер начал кадр, поэтому потерянный по дороге пакет портит
    /// ровно свои 40 мс и не ломает следующие. Для ненадёжной доставки, на
    /// которой ходит голос, это обязательное свойство: потоковый ADPCM после
    /// первой же потери шипел бы до конца разговора.
    /// </summary>
    public static class VoiceCodec
    {
        private static readonly int[] StepTable =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
            50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307,
            337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066,
            2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487,
            12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
        };

        private static readonly int[] IndexTable =
        {
            -1, -1, -1, -1, 2, 4, 6, 8,
            -1, -1, -1, -1, 2, 4, 6, 8
        };

        private const int MaxStepIndex = 88;

        /// <summary>
        /// Закодировать кадр. Возвращает число записанных байт. Состояние
        /// обновляется и переезжает в следующий кадр, а его копия уходит
        /// в заголовок этого.
        /// </summary>
        public static int Encode(float[] pcm, int offset, int count, ref VoiceCodecState state, byte[] destination)
        {
            if (pcm == null) throw new ArgumentNullException(nameof(pcm));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (count <= 0 || (count & 1) != 0) throw new ArgumentException("Кадр обязан содержать чётное число отсчётов", nameof(count));
            if (offset < 0 || offset + count > pcm.Length) throw new ArgumentOutOfRangeException(nameof(offset));

            int written = VoiceFormat.HeaderBytes + count / 2;
            if (destination.Length < written) throw new ArgumentException("Буфер меньше закодированного кадра", nameof(destination));

            destination[0] = (byte)(state.Predictor & 0xFF);
            destination[1] = (byte)((state.Predictor >> 8) & 0xFF);
            destination[2] = state.StepIndex > MaxStepIndex ? (byte)MaxStepIndex : state.StepIndex;
            destination[3] = 0;

            int predictor = state.Predictor;
            int index = destination[2];
            int writeAt = VoiceFormat.HeaderBytes;

            for (int i = 0; i < count; i += 2)
            {
                byte low = EncodeSample(pcm[offset + i], ref predictor, ref index);
                byte high = EncodeSample(pcm[offset + i + 1], ref predictor, ref index);
                destination[writeAt++] = (byte)(low | (high << 4));
            }

            state.Predictor = (short)predictor;
            state.StepIndex = (byte)index;
            return written;
        }

        /// <summary>
        /// Раскодировать кадр в отсчёты от -1 до 1. Возвращает число отсчётов.
        /// Про предыдущие кадры ничего не знает — всё нужное лежит в заголовке.
        /// </summary>
        public static int Decode(byte[] source, int offset, int count, float[] destination, int destinationOffset)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (count <= VoiceFormat.HeaderBytes) return 0;
            if (offset < 0 || offset + count > source.Length) throw new ArgumentOutOfRangeException(nameof(offset));

            int samples = (count - VoiceFormat.HeaderBytes) * 2;
            if (destinationOffset < 0 || destinationOffset + samples > destination.Length)
                throw new ArgumentException("Буфер меньше раскодированного кадра", nameof(destination));

            int predictor = (short)(source[offset] | (source[offset + 1] << 8));
            int index = source[offset + 2];
            if (index > MaxStepIndex) index = MaxStepIndex;

            int readAt = offset + VoiceFormat.HeaderBytes;
            int writeAt = destinationOffset;

            for (int i = 0; i < samples; i += 2)
            {
                byte packed = source[readAt++];
                destination[writeAt++] = DecodeSample((byte)(packed & 0x0F), ref predictor, ref index);
                destination[writeAt++] = DecodeSample((byte)((packed >> 4) & 0x0F), ref predictor, ref index);
            }

            return samples;
        }

        /// <summary>Сколько отсчётов выйдет из кадра такой длины.</summary>
        public static int DecodedSampleCount(int encodedBytes) =>
            encodedBytes <= VoiceFormat.HeaderBytes ? 0 : (encodedBytes - VoiceFormat.HeaderBytes) * 2;

        private static byte EncodeSample(float value, ref int predictor, ref int index)
        {
            int sample = (int)(Clamp(value, -1f, 1f) * short.MaxValue);
            int step = StepTable[index];
            int diff = sample - predictor;

            int code = 0;
            if (diff < 0)
            {
                code = 8;
                diff = -diff;
            }

            int delta = step >> 3;
            if (diff >= step)
            {
                code |= 4;
                diff -= step;
                delta += step;
            }

            if (diff >= step >> 1)
            {
                code |= 2;
                diff -= step >> 1;
                delta += step >> 1;
            }

            if (diff >= step >> 2)
            {
                code |= 1;
                delta += step >> 2;
            }

            predictor = ClampToShort((code & 8) != 0 ? predictor - delta : predictor + delta);
            index = ClampIndex(index + IndexTable[code]);
            return (byte)code;
        }

        private static float DecodeSample(byte code, ref int predictor, ref int index)
        {
            int step = StepTable[index];
            int delta = step >> 3;
            if ((code & 4) != 0) delta += step;
            if ((code & 2) != 0) delta += step >> 1;
            if ((code & 1) != 0) delta += step >> 2;

            predictor = ClampToShort((code & 8) != 0 ? predictor - delta : predictor + delta);
            index = ClampIndex(index + IndexTable[code]);
            return predictor / (float)short.MaxValue;
        }

        private static int ClampIndex(int index) => index < 0 ? 0 : index > MaxStepIndex ? MaxStepIndex : index;

        private static int ClampToShort(int value) =>
            value < short.MinValue ? short.MinValue : value > short.MaxValue ? short.MaxValue : value;

        private static float Clamp(float value, float min, float max) =>
            value < min ? min : value > max ? max : value;
    }
}
