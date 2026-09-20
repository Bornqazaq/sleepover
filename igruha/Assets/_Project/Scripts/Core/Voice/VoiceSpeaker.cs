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

        /// <summary>Запас перед стартом воспроизведения — два кадра, то есть 80 мс.</summary>
        private const int PrimeFrames = 2;

        /// <summary>Потолок задержки: больше — выбрасываем старое, иначе разговор отстаёт.</summary>
        private const int MaxFrames = 8;

        /// <summary>Сколько считать человека говорящим после последнего пакета.</summary>
        private const float SpeakingHoldSeconds = 0.25f;

        private readonly VoiceJitterBuffer buffer;
        private readonly float[] decoded = new float[VoiceFormat.FrameSamples];
        private readonly GameObject host;
        private readonly AudioSource source;

        private ushort lastSequence;
        private bool hasSequence;
        private float lastPacketAt = -100f;
        private float level;

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
        /// </summary>
        public void Push(ushort sequence, byte[] data, int offset, int count)
        {
            if (hasSequence)
            {
                // Разность со знаком переживает переполнение счётчика.
                short age = (short)(sequence - lastSequence);
                if (age <= 0) return;
            }

            int samples = VoiceCodec.Decode(data, offset, count, decoded, 0);
            if (samples <= 0) return;

            lastSequence = sequence;
            hasSequence = true;
            lastPacketAt = Time.unscaledTime;
            level = VoiceActivityDetector.RootMeanSquare(decoded, 0, samples);

            buffer.Write(decoded, 0, samples);
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
