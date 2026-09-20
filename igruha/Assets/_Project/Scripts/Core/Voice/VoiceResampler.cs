using System;

namespace Igruha.Core.Voice
{
    /// <summary>
    /// Приведение микрофонной записи к частоте голосового потока.
    ///
    /// Нужен потому, что <c>Microphone</c> отдаёт не ту частоту, которую
    /// попросили, а ту, которую умеет устройство: гарнитуры обычно пишут
    /// 44100 или 48000 и 16 кГц не поддерживают вовсе. Без приведения голос
    /// уезжает по высоте и скорости — «бурундук» на стороне слушателей.
    ///
    /// Интерполяция линейная: для речи в 16 кГц слышимой разницы с честным
    /// фильтром нет, а считается она в один проход без аллокаций.
    /// </summary>
    public sealed class VoiceResampler
    {
        private readonly double ratio;
        private float previous;
        private double position;

        public VoiceResampler(int sourceRate, int targetRate)
        {
            if (sourceRate <= 0) throw new ArgumentOutOfRangeException(nameof(sourceRate));
            if (targetRate <= 0) throw new ArgumentOutOfRangeException(nameof(targetRate));

            ratio = sourceRate / (double)targetRate;
            SourceRate = sourceRate;
            TargetRate = targetRate;
        }

        public int SourceRate { get; }

        public int TargetRate { get; }

        /// <summary>Нужна ли вообще пересчётка, или частоты совпали.</summary>
        public bool Passthrough => SourceRate == TargetRate;

        /// <summary>С запасом: сколько отсчётов выйдет из блока такой длины.</summary>
        public int EstimateOutput(int sourceCount) => (int)(sourceCount / ratio) + 2;

        /// <summary>
        /// Пересчитать блок. Дробная позиция и последний отсчёт сохраняются,
        /// поэтому на стыке блоков не щёлкает. Возвращает число записанных
        /// отсчётов.
        /// </summary>
        public int Process(float[] source, int sourceOffset, int sourceCount, float[] destination, int destinationOffset)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (sourceCount <= 0) return 0;

            int written = 0;
            int limit = destination.Length - destinationOffset;

            while (position <= sourceCount - 1 && written < limit)
            {
                int index = (int)Math.Floor(position);
                float fraction = (float)(position - index);

                float first = index < 0 ? previous : source[sourceOffset + index];

                // Следующего отсчёта может не быть: на position ровно в конце
                // блока доля нулевая и второй отсчёт всё равно не участвует,
                // но прочитать его за границей массива нельзя.
                int nextIndex = index + 1;
                float second = nextIndex >= sourceCount ? first : source[sourceOffset + nextIndex];

                destination[destinationOffset + written] = first + (second - first) * fraction;
                written++;
                position += ratio;
            }

            previous = source[sourceOffset + sourceCount - 1];
            position -= sourceCount;
            if (position < -1d) position = -1d;

            return written;
        }

        public void Reset()
        {
            previous = 0f;
            position = 0d;
        }
    }
}
