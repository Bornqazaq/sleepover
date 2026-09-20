using UnityEngine;

namespace Igruha.Core.Voice
{
    /// <summary>
    /// Голос одного собеседника: свой источник звука, свой буфер, свой счётчик
    /// кадров. Заводится на первый пришедший пакет и живёт, пока человек в игре.
    ///
    /// Звук здесь намеренно не пространственный. В мини-играх участники
    /// разнесены по всей арене, кого-то уже выбило, кто-то смотрит с трибуны —
    /// и затухание по расстоянию означало бы, что половина разговора пропала.
    /// Разговор в party-game слышат все и всегда, как в голосовом чате рядом с
    /// игрой, а не как крик через комнату.
    ///
    /// Звук отдаётся потоковым клипом: Unity сама дёргает
    /// <see cref="ReadPcm"/> из звукового потока и берёт ровно столько,
    /// сколько нужно карте. Складывать пришедшее в обычный клип и заводить его
    /// заново на каждый пакет нельзя — на стыках щёлкает.
    /// </summary>
    public sealed class VoiceSpeaker
    {
        /// <summary>Сколько секунд звука держит кольцо. С запасом на любую икоту сети.</summary>
        private const float BufferSeconds = 2f;

        /// <summary>Запас перед стартом воспроизведения — три кадра, то есть 120 мс.</summary>
        private const int PrimeFrames = 3;

        /// <summary>Докуда запас растёт на неровной сети — 240 мс. Дальше задержка слышнее обрывов.</summary>
        private const int MaxPrimeFrames = 6;

        /// <summary>Потолок задержки: больше — выбрасываем старое, иначе разговор отстаёт.</summary>
        private const int MaxFrames = 12;

        /// <summary>
        /// Сколько подряд потерянных кадров достраиваем сами. Два — это 80 мс;
        /// дальше повтор слышен как заедающая пластинка, и честная тишина лучше.
        /// </summary>
        private const int MaxConcealedFrames = 2;

        /// <summary>Во сколько раз тише каждый следующий достроенный кадр.</summary>
        private const float ConcealDecay = 0.5f;

        /// <summary>Через сколько чистых секунд разговора запас снова уменьшается на кадр.</summary>
        private const float RelaxSeconds = 12f;

        /// <summary>Сколько считать человека говорящим после последнего пакета.</summary>
        private const float SpeakingHoldSeconds = 0.25f;

        private readonly VoiceJitterBuffer buffer;
        private readonly float[] decoded = new float[VoiceFormat.FrameSamples];

        /// <summary>Последний услышанный кадр — из него достраиваются потерянные.</summary>
        private readonly float[] previous = new float[VoiceFormat.FrameSamples];

        private readonly GameObject host;
        private readonly AudioSource source;

        private ushort lastSequence;
        private bool hasSequence;
        private bool hasPrevious;
        private float lastPacketAt = -100f;
        private float level;

        private int primeFrames = PrimeFrames;
        private int knownUnderruns;
        private float relaxAt;

        public VoiceSpeaker(ulong clientId, Transform parent, float volume)
        {
            ClientId = clientId;

            buffer = new VoiceJitterBuffer(
                Mathf.CeilToInt(VoiceFormat.SampleRate * BufferSeconds),
                VoiceFormat.FrameSamples * PrimeFrames,
                VoiceFormat.FrameSamples * MaxFrames);

            host = new GameObject($"Voice_{clientId}");
            host.transform.SetParent(parent, false);

            AudioClip clip = AudioClip.Create(
                $"voice_{clientId}",
                VoiceFormat.SampleRate,
                1,
                VoiceFormat.SampleRate,
                true,
                ReadPcm);

            source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.spatialBlend = 0f;
            source.playOnAwake = false;
            source.bypassEffects = true;
            source.bypassListenerEffects = true;
            source.bypassReverbZones = true;
            source.ignoreListenerPause = true;
            source.ignoreListenerVolume = false;
            source.volume = Mathf.Clamp01(volume);
            source.Play();
        }

        public ulong ClientId { get; }

        /// <summary>Говорит ли человек прямо сейчас — для отметки в списке.</summary>
        public bool IsSpeaking => Time.unscaledTime - lastPacketAt < SpeakingHoldSeconds;

        /// <summary>Громкость последнего кадра — для полоски рядом с именем.</summary>
        public float Level => IsSpeaking ? level : 0f;

        /// <summary>Сколько секунд назад приходил звук — по этому чистим ушедших.</summary>
        public float SilentFor => Time.unscaledTime - lastPacketAt;

        public void SetVolume(float volume) => source.volume = Mathf.Clamp01(volume);

        /// <summary>
        /// Принять кадр. Опоздавшие пакеты отбрасываются: доставка ненадёжная,
        /// и порядок она не держит, а звук, вставленный задним числом, слышен
        /// как щелчок.
        ///
        /// Пропуск в номерах означает потерю по дороге, а не паузу в речи:
        /// молчащая машина кадров не шлёт вовсе и счётчик ей не двигает.
        /// Поэтому короткий пропуск достраивается затухающим повтором
        /// прошлого кадра — не ради красоты, а ради времени: без вставки
        /// буфер пустеет на 40 мс раньше срока, глохнет и заново набирает
        /// запас, и одна потеря превращается в четверть секунды тишины.
        /// </summary>
        public void Push(ushort sequence, byte[] data, int offset, int count)
        {
            int lost = 0;

            if (hasSequence)
            {
                // Разность со знаком переживает переполнение счётчика.
                short age = (short)(sequence - lastSequence);
                if (age <= 0) return;
                lost = age - 1;
            }

            int samples = VoiceCodec.Decode(data, offset, count, decoded, 0);
            if (samples <= 0) return;

            Conceal(lost);

            lastSequence = sequence;
            hasSequence = true;
            lastPacketAt = Time.unscaledTime;
            level = VoiceActivityDetector.RootMeanSquare(decoded, 0, samples);

            buffer.Write(decoded, 0, samples);

            System.Array.Copy(decoded, previous, samples);
            hasPrevious = samples == VoiceFormat.FrameSamples;

            AdjustPrime();
        }

        /// <summary>Достроить потерянные кадры повтором прошлого, всё тише с каждым.</summary>
        private void Conceal(int lost)
        {
            if (lost <= 0 || lost > MaxConcealedFrames || !hasPrevious) return;

            // Копия затухает прямо на месте: следующей строкой Push кладёт
            // в неё свежий кадр, и портить тут нечего.
            for (int frame = 0; frame < lost; frame++)
            {
                for (int i = 0; i < previous.Length; i++) previous[i] *= ConcealDecay;
                buffer.Write(previous, 0, previous.Length);
            }
        }

        /// <summary>
        /// Подогнать запас буфера под эту сеть. Каждый обрыв поднимает его на
        /// кадр, чистая минута разговора — опускает обратно.
        ///
        /// Подгонка нужна потому, что играем и по локальной сети, и через
        /// Tailscale с чужого города: постоянный запас, годный для второго,
        /// добавляет первому четверть секунды задержки ни за что.
        /// </summary>
        private void AdjustPrime()
        {
            int underruns = buffer.Underruns;
            float now = Time.unscaledTime;

            if (underruns != knownUnderruns)
            {
                knownUnderruns = underruns;
                relaxAt = now + RelaxSeconds;

                if (primeFrames >= MaxPrimeFrames) return;

                primeFrames++;
                buffer.SetPrime(VoiceFormat.FrameSamples * primeFrames);
                return;
            }

            if (relaxAt <= 0f)
            {
                relaxAt = now + RelaxSeconds;
                return;
            }

            if (now < relaxAt || primeFrames <= PrimeFrames) return;

            primeFrames--;
            relaxAt = now + RelaxSeconds;
            buffer.SetPrime(VoiceFormat.FrameSamples * primeFrames);
        }

        /// <summary>Звуковой поток забирает отсчёты отсюда, не из главного.</summary>
        private void ReadPcm(float[] data)
        {
            buffer.Read(data, 0, data.Length);
        }

        public void Dispose()
        {
            if (source != null) source.Stop();
            buffer.Clear();
            if (host != null) Object.Destroy(host);
        }
    }
}
