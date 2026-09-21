using System;
using UnityEngine;

namespace Igruha.Core.Voice
{
    /// <summary>
    /// Микрофон этой машины: запись, приведение к формату голоса и нарезка на
    /// сетевые кадры. Про сеть ничего не знает — отдаёт готовые кадры наружу.
    ///
    /// Два места, на которых это обычно ломается, разобраны прямо здесь.
    ///
    /// Первое: <c>Microphone.Start</c> пишет не с той частотой, которую
    /// попросили, а с той, которую умеет устройство. Гарнитуры сплошь пишут
    /// 44100 или 48000, поэтому частота берётся из <c>GetDeviceCaps</c>, а
    /// к 16 кГц поток приводит <see cref="VoiceResampler"/>.
    ///
    /// Второе: микрофонный клип кольцевой, и чтение обязано не пересекать его
    /// границу — <c>GetData</c> заполняет массив целиком и через конец клипа
    /// не заворачивает. Поэтому читается он блоками фиксированной длины, а
    /// остаток круга дочитывается отдельным коротким блоком.
    /// </summary>
    public sealed class VoiceCapture
    {
        /// <summary>
        /// Длина кольцевого буфера микрофона. Две секунды, а не одна: при
        /// просадке кадров (загрузка следующей мини-игры, свёрнутое окно)
        /// непрочитанный звук затирается новым, и запас решает, услышат
        /// человека или проглотят ему полслова.
        /// </summary>
        private const int RecordSeconds = 2;

        /// <summary>Читаем блоками по 20 мс — мельче нет смысла, крупнее добавляет задержку.</summary>
        private const int BlockMilliseconds = 20;

        /// <summary>
        /// За сколько секунд показанный уровень оседает втрое.
        ///
        /// Спад именно пропорциональный, а не «столько-то шкалы в секунду»:
        /// речь живёт в сотых долях громкости, и линейный спад по всей шкале
        /// гасил бы её за пару миллисекунд — полоска уровня стояла бы на нуле
        /// даже когда человек говорит, и выставить порог по ней было бы нечем.
        /// </summary>
        private const float LevelDecaySeconds = 0.25f;

        private readonly VoiceActivityDetector detector = new VoiceActivityDetector();

        private AudioClip clip;
        private string device;
        private int deviceRate;
        private int clipSamples;
        private int blockSamples;
        private int tailSamples;
        private int readAt;

        private float[] block;
        private float[] tail;
        private float[] resampled;
        private float[] frame;
        private int frameFill;

        private VoiceResampler resampler;

        private bool muted;
        private bool pushToTalkHeld;
        private VoiceInputMode mode;

        /// <summary>Готовый кадр в формате голоса. Длина массива — <see cref="VoiceFormat.FrameSamples"/>.</summary>
        public event Action<float[]> FrameReady;

        public bool IsRecording => clip != null;

        /// <summary>Устройство, с которого идёт запись. Пусто — системное по умолчанию.</summary>
        public string Device => device;

        /// <summary>Частота, на которой реально пишет устройство.</summary>
        public int DeviceRate => deviceRate;

        /// <summary>Почему запись не идёт — текстом для человека, а не для лога.</summary>
        public string Error { get; private set; } = string.Empty;

        /// <summary>Сглаженный уровень микрофона для индикатора.</summary>
        public float Level { get; private set; }

        /// <summary>Уходит ли звук в сеть прямо сейчас.</summary>
        public bool Transmitting { get; private set; }

        /// <summary>
        /// Сколько кадров микрофон выдал с начала записи — независимо от того,
        /// ушли они в сеть или их съел порог. По нему видно, читается ли
        /// устройство ровно: штатно это 25 кадров в секунду, и отставание
        /// означает, что рвётся запись, а не связь.
        /// </summary>
        public int FramesProduced { get; private set; }

        /// <summary>Порог открытого микрофона.</summary>
        public float Threshold
        {
            get => detector.Threshold;
            set => detector.Threshold = value;
        }

        /// <summary>Усиление входа — для тихих гарнитур.</summary>
        public float Gain { get; set; } = 1f;

        /// <summary>Есть ли на машине хоть одно устройство записи.</summary>
        public static bool AnyDeviceAvailable => Microphone.devices != null && Microphone.devices.Length > 0;

        /// <summary>
        /// Начать запись. Возвращает false и заполняет <see cref="Error"/>,
        /// если устройства нет или система не дала доступ.
        /// </summary>
        public bool Start(string preferredDevice)
        {
            Stop();

            string[] devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                Error = "Микрофон не найден. Проверьте, что он подключён и что игре разрешён доступ к нему в настройках системы.";
                return false;
            }

            device = ResolveDevice(devices, preferredDevice);

            Microphone.GetDeviceCaps(device, out int minRate, out int maxRate);
            deviceRate = minRate == 0 && maxRate == 0
                ? VoiceFormat.SampleRate
                : Mathf.Clamp(VoiceFormat.SampleRate, minRate, maxRate);

            clip = Microphone.Start(device, true, RecordSeconds, deviceRate);
            if (clip == null)
            {
                Error = $"Не удалось открыть микрофон «{device}».";
                return false;
            }

            deviceRate = clip.frequency;
            clipSamples = clip.samples;
            blockSamples = Mathf.Max(1, deviceRate * BlockMilliseconds / 1000);
            if (blockSamples > clipSamples) blockSamples = clipSamples;
            tailSamples = clipSamples % blockSamples;

            block = new float[blockSamples];
            tail = tailSamples > 0 ? new float[tailSamples] : null;

            resampler = new VoiceResampler(deviceRate, VoiceFormat.SampleRate);
            resampled = new float[resampler.EstimateOutput(blockSamples) + VoiceFormat.FrameSamples];
            frame = new float[VoiceFormat.FrameSamples];

            readAt = 0;
            frameFill = 0;
            FramesProduced = 0;
            Error = string.Empty;
            detector.Reset();
            return true;
        }

        public void Stop()
        {
            if (clip != null)
            {
                if (Microphone.IsRecording(device)) Microphone.End(device);
                clip = null;
            }

            Transmitting = false;
            Level = 0f;
            frameFill = 0;
            detector.Reset();
            resampler?.Reset();
        }

        /// <summary>
        /// Вычитать всё, что микрофон записал с прошлого кадра игры.
        /// </summary>
        /// <param name="deltaSeconds">Время кадра игры — для затухания индикатора.</param>
        /// <param name="muted">Микрофон выключен: звук читается для индикатора, но наружу не уходит.</param>
        /// <param name="pushToTalkHeld">Зажата клавиша рации.</param>
        /// <param name="mode">Открытый микрофон или рация.</param>
        public void Tick(float deltaSeconds, bool muted, bool pushToTalkHeld, VoiceInputMode mode)
        {
            // Кадры рождаются внутри вычитки, поэтому состояние ввода кладётся
            // в поля до неё: иначе пришлось бы тащить его через четыре вызова.
            this.muted = muted;
            this.pushToTalkHeld = pushToTalkHeld;
            this.mode = mode;

            Level *= Mathf.Exp(-deltaSeconds / LevelDecaySeconds);

            if (clip == null)
            {
                Transmitting = false;
                return;
            }

            int position = Microphone.GetPosition(device);
            if (position < 0 || position >= clipSamples) return;

            int pending = position - readAt;
            if (pending < 0) pending += clipSamples;

            // Отстали почти на круг — старое всё равно уже не звук, а каша.
            if (pending > clipSamples - blockSamples)
            {
                readAt = position - position % blockSamples;
                if (readAt < 0) readAt += clipSamples;
                pending = 0;
            }

            while (pending >= Remaining())
            {
                int taken = ReadBlock();
                if (taken <= 0) break;
                pending -= taken;
            }
        }

        /// <summary>Сколько отсчётов заберёт следующее чтение.</summary>
        private int Remaining()
        {
            int toEdge = clipSamples - readAt;
            return toEdge < blockSamples ? toEdge : blockSamples;
        }

        private int ReadBlock()
        {
            int toEdge = clipSamples - readAt;
            float[] target = toEdge < blockSamples ? tail : block;
            if (target == null || target.Length == 0) return 0;

            clip.GetData(target, readAt);
            int taken = target.Length;

            readAt += taken;
            if (readAt >= clipSamples) readAt = 0;

            Process(target, taken);
            return taken;
        }

        private void Process(float[] source, int count)
        {
            float gain = Gain;
            if (!Mathf.Approximately(gain, 1f))
            {
                for (int i = 0; i < count; i++) source[i] = Mathf.Clamp(source[i] * gain, -1f, 1f);
            }

            int produced = resampler.Process(source, 0, count, resampled, 0);
            int consumed = 0;

            while (consumed < produced)
            {
                int room = VoiceFormat.FrameSamples - frameFill;
                int take = Mathf.Min(room, produced - consumed);
                Array.Copy(resampled, consumed, frame, frameFill, take);
                frameFill += take;
                consumed += take;

                if (frameFill < VoiceFormat.FrameSamples) break;

                frameFill = 0;
                EmitFrame();
            }
        }

        private void EmitFrame()
        {
            FramesProduced++;

            float rms = VoiceActivityDetector.RootMeanSquare(frame, 0, VoiceFormat.FrameSamples);
            if (rms > Level) Level = rms;

            bool open = detector.Evaluate(rms, VoiceFormat.FrameSeconds);

            // Клавиша рации работает в обоих режимах: в режиме рации она
            // единственный способ говорить, в открытом — выручает, когда
            // микрофон тихий и порог до него не дотягивается.
            bool wants = !muted && (mode == VoiceInputMode.PushToTalk ? pushToTalkHeld : open || pushToTalkHeld);

            Transmitting = wants;
            if (!wants) return;

            FrameReady?.Invoke(frame);
        }

        private static string ResolveDevice(string[] devices, string preferred)
        {
            if (!string.IsNullOrEmpty(preferred))
            {
                for (int i = 0; i < devices.Length; i++)
                {
                    if (devices[i] == preferred) return preferred;
                }
            }

            return devices[0];
        }
    }
}
