using System;

namespace Igruha.Core.Voice
{
    /// <summary>
    /// Решает, говорит человек или молчит, по громкости кадра.
    ///
    /// Нужен затем, что открытый микрофон восьмерых — это восемь потоков
    /// дыхания, кликов мыши и чужих телевизоров, сложенных в одну кашу, и
    /// хосту их все ретранслировать. С порогом в эфире только те, кто
    /// действительно говорит.
    ///
    /// Порог не один, а два: открывается канал по громкому, закрывается по
    /// заметно более тихому и не сразу (<see cref="ReleaseSeconds"/>). Один
    /// порог рубил бы речь на паузах между словами — слушатель получает
    /// рваную фразу с проглоченными концами.
    /// </summary>
    public sealed class VoiceActivityDetector
    {
        /// <summary>Во сколько раз тише порога должно стать, чтобы канал закрылся.</summary>
        private const float CloseFactor = 0.6f;

        private float silentFor;

        /// <summary>Громкость (RMS), с которой начинается передача.</summary>
        public float Threshold { get; set; } = 0.02f;

        /// <summary>Сколько держать канал открытым после того, как стало тихо.</summary>
        public float ReleaseSeconds { get; set; } = 0.4f;

        public bool IsOpen { get; private set; }

        /// <summary>Обновить решение по очередному кадру. Возвращает, идёт ли передача.</summary>
        public bool Evaluate(float rms, float deltaSeconds)
        {
            if (deltaSeconds < 0f) deltaSeconds = 0f;

            if (rms >= Threshold)
            {
                silentFor = 0f;
                IsOpen = true;
                return IsOpen;
            }

            if (!IsOpen) return false;

            if (rms >= Threshold * CloseFactor)
            {
                silentFor = 0f;
                return IsOpen;
            }

            silentFor += deltaSeconds;

            if (silentFor >= ReleaseSeconds)
            {
                IsOpen = false;
                silentFor = 0f;
            }

            return IsOpen;
        }

        public void Reset()
        {
            IsOpen = false;
            silentFor = 0f;
        }

        /// <summary>Громкость кадра — корень из средней мощности.</summary>
        public static float RootMeanSquare(float[] samples, int offset, int count)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (count <= 0) return 0f;

            double sum = 0d;
            for (int i = 0; i < count; i++)
            {
                float value = samples[offset + i];
                sum += value * value;
            }

            return (float)Math.Sqrt(sum / count);
        }
    }
}
