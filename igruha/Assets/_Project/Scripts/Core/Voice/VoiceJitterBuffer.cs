using System;

namespace Igruha.Core.Voice
{
    /// <summary>
    /// Кольцевой буфер голоса одного говорящего: сеть пишет в него кадрами из
    /// главного потока, звуковая карта читает произвольными порциями из своего.
    /// Отсюда блокировка — критический участок тут только копирование.
    ///
    /// Буфер заодно гасит джиттер. Пакеты приходят не ровно раз в 40 мс: сеть
    /// то задержит, то выдаст два подряд. Поэтому воспроизведение не начинается,
    /// пока не накопится запас (<see cref="PrimeSamples"/>) — иначе звук
    /// захлёбывается на каждой неровности. Опустевший буфер снова копит запас.
    ///
    /// Обратная беда — накопление задержки: если пакетов пришло больше, чем
    /// успели съесть, разговор начинает отставать и отставание уже не уходит.
    /// Поэтому переполнение сбрасывает старое, а не растёт: лучше один щелчок,
    /// чем секунда опоздания до конца катки.
    ///
    /// <b>Запас не постоянный.</b> Одна и та же цифра не подходит и локальной
    /// сети, и разговору через Tailscale: маленький запас на неровном канале
    /// обрывает звук каждые несколько слов, большой — добавляет задержку там,
    /// где она не нужна. Поэтому буфер считает обрывы (<see cref="Underruns"/>),
    /// а решение, насколько поднять запас, принимает <see cref="VoiceSpeaker"/> —
    /// он один знает, что такое кадр и сколько их в секунде.
    /// </summary>
    public sealed class VoiceJitterBuffer
    {
        private readonly object gate = new object();
        private readonly float[] buffer;
        private readonly int minPrimeSamples;
        private readonly int maxSamples;

        private int primeSamples;
        private int readAt;
        private int available;
        private bool priming = true;
        private int underruns;

        public VoiceJitterBuffer(int capacitySamples, int primeSamples, int maxSamples)
        {
            if (capacitySamples <= 0) throw new ArgumentOutOfRangeException(nameof(capacitySamples));
            if (maxSamples > capacitySamples) throw new ArgumentOutOfRangeException(nameof(maxSamples));

            buffer = new float[capacitySamples];
            minPrimeSamples = Math.Max(0, primeSamples);
            this.primeSamples = minPrimeSamples;
            this.maxSamples = Math.Max(minPrimeSamples, maxSamples);
        }

        /// <summary>Сколько отсчётов ждёт воспроизведения.</summary>
        public int Available
        {
            get { lock (gate) return available; }
        }

        /// <summary>Набирает ли буфер запас — то есть молчит ли он прямо сейчас.</summary>
        public bool Priming
        {
            get { lock (gate) return priming; }
        }

        /// <summary>Порог, после которого буфер начинает отдавать звук.</summary>
        public int PrimeSamples
        {
            get { lock (gate) return primeSamples; }
        }

        /// <summary>
        /// Сколько раз звук кончился посреди воспроизведения. По этому счётчику
        /// снаружи решают, что запас мал для этой сети, и поднимают его: сам
        /// буфер про кадры и секунды ничего не знает.
        /// </summary>
        public int Underruns
        {
            get { lock (gate) return underruns; }
        }

        /// <summary>
        /// Задать запас. Ниже заводского не опускается: запас в один кадр
        /// означает, что любая неровность сети слышна щелчком.
        ///
        /// Уменьшение не выбрасывает уже накопленное — лишнее само разойдётся
        /// на чтениях, а вот подрезать буфер на ходу значит потерять звук,
        /// который уже приехал.
        /// </summary>
        public void SetPrime(int samples)
        {
            lock (gate)
            {
                int limit = Math.Max(minPrimeSamples, maxSamples / 2);
                primeSamples = Math.Min(Math.Max(samples, minPrimeSamples), limit);
            }
        }

        /// <summary>Положить раскодированный кадр. Вызывается из главного потока.</summary>
        public void Write(float[] source, int offset, int count)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (count <= 0) return;

            lock (gate)
            {
                if (count >= buffer.Length)
                {
                    offset += count - buffer.Length;
                    count = buffer.Length;
                }

                int writeAt = (readAt + available) % buffer.Length;
                for (int i = 0; i < count; i++)
                {
                    buffer[writeAt] = source[offset + i];
                    writeAt++;
                    if (writeAt == buffer.Length) writeAt = 0;
                }

                available += count;
                if (available > buffer.Length) available = buffer.Length;

                // Отстали — выбрасываем самое старое и оставляем ровно запас.
                if (available > maxSamples)
                {
                    int drop = available - primeSamples;
                    readAt = (readAt + drop) % buffer.Length;
                    available -= drop;
                }

                if (priming && available >= primeSamples) priming = false;
            }
        }

        /// <summary>
        /// Забрать отсчёты для звуковой карты. Чего нет — отдаётся тишиной,
        /// потому что звуковому потоку нечего вернуть кроме буфера. Возвращает
        /// число настоящих отсчётов.
        /// </summary>
        public int Read(float[] destination, int offset, int count)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (count <= 0) return 0;

            lock (gate)
            {
                if (priming)
                {
                    Array.Clear(destination, offset, count);
                    return 0;
                }

                int taken = Math.Min(count, available);
                for (int i = 0; i < taken; i++)
                {
                    destination[offset + i] = buffer[readAt];
                    readAt++;
                    if (readAt == buffer.Length) readAt = 0;
                }

                available -= taken;
                if (taken < count) Array.Clear(destination, offset + taken, count - taken);

                // Звук кончился на полуслове. Считаем это отдельно от обычной
                // паузы в разговоре: повторяющийся обрыв — единственный
                // объективный признак того, что запаса мало для этой сети.
                if (available == 0)
                {
                    priming = true;
                    if (taken > 0) underruns++;
                }

                return taken;
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                readAt = 0;
                available = 0;
                priming = true;
                underruns = 0;
            }
        }
    }
}
