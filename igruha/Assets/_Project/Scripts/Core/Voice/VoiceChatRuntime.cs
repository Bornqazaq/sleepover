using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Igruha.Core.Voice
{
    /// <summary>
    /// Голосовой чат целиком, кроме собственно сети: микрофон, кодирование,
    /// чужие голоса, настройки и клавиши. Живёт на объекте, переживающем смену
    /// сцен, поэтому работает одинаково в хабе и во всех мини-играх — игре не
    /// нужно ничего про него знать и ни одна сцена его не содержит.
    ///
    /// Сетевой слой (<c>Igruha.Networking.VoiceChatService</c>) подписывается
    /// на <see cref="FrameEncoded"/> и зовёт <see cref="ReceiveFrame"/>. Такое
    /// разделение нужно не ради красоты: кодек, буфер и порог проверяются
    /// тестами без единого сетевого объекта.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceChatRuntime : MonoBehaviour
    {
        /// <summary>Через сколько молчания собеседник считается ушедшим и его источник сносится.</summary>
        private const float SpeakerLifetimeSeconds = 90f;

        /// <summary>Как часто проверять, не отвалился ли микрофон.</summary>
        private const float DeviceCheckSeconds = 2f;

        private readonly Dictionary<ulong, VoiceSpeaker> speakers = new Dictionary<ulong, VoiceSpeaker>(8);
        private readonly List<ulong> expired = new List<ulong>(8);
        private readonly List<VoiceSpeaker> speakerList = new List<VoiceSpeaker>(8);

        private readonly VoiceCapture capture = new VoiceCapture();
        private readonly byte[] encoded = new byte[VoiceFormat.EncodedFrameBytes];

        private VoiceCodecState codecState;
        private ushort sequence;
        private float deviceCheckAt;
        private bool networkActive;

        public static VoiceChatRuntime Instance { get; private set; }

        /// <summary>Готовый кадр: номер, байты и их длина. Слушает сетевой слой.</summary>
        public event Action<ushort, byte[], int> FrameEncoded;

        /// <summary>Сеть поднята и голосу есть куда идти.</summary>
        public bool NetworkActive
        {
            get => networkActive;
            set
            {
                if (networkActive == value) return;
                networkActive = value;

                if (networkActive) StartCapture();
                else
                {
                    capture.Stop();
                    ClearSpeakers();
                }
            }
        }

        /// <summary>Включён ли микрофон этой машины.</summary>
        public bool MicrophoneEnabled
        {
            get => VoiceSettings.MicrophoneEnabled;
            set => VoiceSettings.MicrophoneEnabled = value;
        }

        /// <summary>Уходит ли голос в сеть прямо сейчас.</summary>
        public bool Transmitting => capture.Transmitting;

        /// <summary>Уровень своего микрофона для индикатора.</summary>
        public float InputLevel => capture.Level;

        /// <summary>Идёт ли запись с устройства.</summary>
        public bool CaptureReady => capture.IsRecording;

        /// <summary>Что помешало записи — текстом для человека.</summary>
        public string CaptureError => capture.Error;

        /// <summary>Устройство, с которого идёт запись.</summary>
        public string CaptureDevice => capture.Device;

        /// <summary>Частота устройства — видно, если гарнитура пишет не в 16 кГц.</summary>
        public int CaptureRate => capture.DeviceRate;

        /// <summary>Сколько кадров выдал микрофон — для проверки ровности записи.</summary>
        public int FramesProduced => capture.FramesProduced;

        /// <summary>Открыта ли панель настроек голоса.</summary>
        public bool SettingsOpen { get; set; }

        /// <summary>
        /// Можно ли этой машине писать микрофон. Виртуальные игроки Play Mode
        /// делят с редактором одно устройство, и запись из трёх процессов
        /// сразу даёт на стенде вой вместо проверки.
        /// </summary>
        public bool CaptureAllowed { get; set; } = true;

        /// <summary>Чужие голоса, которые сейчас в эфире.</summary>
        public IReadOnlyList<VoiceSpeaker> Speakers
        {
            get
            {
                speakerList.Clear();
                foreach (KeyValuePair<ulong, VoiceSpeaker> pair in speakers) speakerList.Add(pair.Value);
                return speakerList;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            capture.Threshold = VoiceSettings.Threshold;
            capture.Gain = VoiceSettings.Gain;
            capture.FrameReady += OnFrameReady;
        }

        private void OnApplicationQuit() => VoiceSettings.Flush();

        private void OnDestroy()
        {
            VoiceSettings.Flush();
            capture.FrameReady -= OnFrameReady;
            capture.Stop();
            ClearSpeakers();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            ReadKeys();

            if (!networkActive) return;

            capture.Tick(Time.unscaledDeltaTime, !MicrophoneEnabled, VoiceKeys.PushToTalkHeld, VoiceSettings.Mode);
            KeepDeviceAlive();
            DropSilentSpeakers();
        }

        /// <summary>
        /// Принять чужой кадр. Зовётся сетевым слоем из главного потока.
        /// </summary>
        public void ReceiveFrame(ulong speakerId, ushort packetSequence, byte[] data, int offset, int count)
        {
            if (!networkActive) return;

            if (!speakers.TryGetValue(speakerId, out VoiceSpeaker speaker))
            {
                speaker = new VoiceSpeaker(speakerId, transform, VoiceSettings.Volume);
                speakers.Add(speakerId, speaker);

                // Единственный объективный след того, что голос дошёл: на
                // прогоне двух билдов услышать самому нечего, а строка в логе
                // отделяет «не передаётся» от «передаётся, но не слышно».
                Debug.Log($"🔊 голосовой чат: пошёл звук от клиента {speakerId}");
            }

            speaker.Push(packetSequence, data, offset, count);
        }

        /// <summary>Человек ушёл из игры — его источник больше не нужен.</summary>
        public void RemoveSpeaker(ulong speakerId)
        {
            if (!speakers.TryGetValue(speakerId, out VoiceSpeaker speaker)) return;
            speaker.Dispose();
            speakers.Remove(speakerId);
        }

        public void ClearSpeakers()
        {
            foreach (KeyValuePair<ulong, VoiceSpeaker> pair in speakers) pair.Value.Dispose();
            speakers.Clear();
        }

        public void ToggleMicrophone() => MicrophoneEnabled = !MicrophoneEnabled;

        public void SetVolume(float volume)
        {
            VoiceSettings.Volume = volume;
            foreach (KeyValuePair<ulong, VoiceSpeaker> pair in speakers) pair.Value.SetVolume(VoiceSettings.Volume);
        }

        public void SetThreshold(float threshold)
        {
            VoiceSettings.Threshold = threshold;
            capture.Threshold = VoiceSettings.Threshold;
        }

        public void SetGain(float gain)
        {
            VoiceSettings.Gain = gain;
            capture.Gain = VoiceSettings.Gain;
        }

        public void SetMode(VoiceInputMode mode) => VoiceSettings.Mode = mode;

        /// <summary>Сменить устройство записи и перезапустить микрофон.</summary>
        public void SetDevice(string device)
        {
            VoiceSettings.Device = device;
            if (networkActive) StartCapture();
        }

        /// <summary>Перечитать список устройств и поднять запись заново.</summary>
        public void RestartCapture()
        {
            if (networkActive) StartCapture();
        }

        private void StartCapture()
        {
            if (!CaptureAllowed) return;

            capture.Threshold = VoiceSettings.Threshold;
            capture.Gain = VoiceSettings.Gain;

            if (capture.Start(VoiceSettings.Device))
            {
                Debug.Log($"🎙️ голосовой чат: пишу с «{capture.Device}» на {capture.DeviceRate} Гц");
            }
            else
            {
                Debug.LogWarning($"🎙️ голосовой чат без микрофона: {capture.Error}");
            }

            deviceCheckAt = Time.unscaledTime + DeviceCheckSeconds;
        }

        /// <summary>
        /// Гарнитуру выдёргивают и переподключают посреди катки. Молча остаться
        /// немым на весь вечер — худший исход, поэтому запись поднимается заново.
        /// </summary>
        private void KeepDeviceAlive()
        {
            if (!CaptureAllowed) return;
            if (Time.unscaledTime < deviceCheckAt) return;
            deviceCheckAt = Time.unscaledTime + DeviceCheckSeconds;

            if (capture.IsRecording && Microphone.IsRecording(capture.Device)) return;
            if (!VoiceCapture.AnyDeviceAvailable) return;

            StartCapture();
        }

        private void DropSilentSpeakers()
        {
            expired.Clear();
            foreach (KeyValuePair<ulong, VoiceSpeaker> pair in speakers)
            {
                if (pair.Value.SilentFor > SpeakerLifetimeSeconds) expired.Add(pair.Key);
            }

            for (int i = 0; i < expired.Count; i++) RemoveSpeaker(expired[i]);
        }

        private void OnFrameReady(float[] frame)
        {
            int length = VoiceCodec.Encode(frame, 0, VoiceFormat.FrameSamples, ref codecState, encoded);
            sequence++;

            if (sequence == 1) Debug.Log("🎙️ голосовой чат: первый кадр ушёл в сеть");

            FrameEncoded?.Invoke(sequence, encoded, length);
        }

        private void ReadKeys()
        {
            // До того как сеть поднялась, человек стоит на экране входа и
            // печатает адрес хоста: перехватывать там клавиши нельзя.
            if (!networkActive || IsTypingSomewhere()) return;

            if (VoiceKeys.MuteToggled)
            {
                ToggleMicrophone();
                VoiceSettings.Flush();
            }

            if (VoiceKeys.SettingsToggled)
            {
                SettingsOpen = !SettingsOpen;
                if (!SettingsOpen) VoiceSettings.Flush();
            }
        }

        /// <summary>
        /// Человек вводит текст (имя в хабе, адрес хоста) — клавиши голоса
        /// обязаны ему не мешать, иначе буква «м» в имени выключает микрофон.
        /// </summary>
        private static bool IsTypingSomewhere()
        {
            EventSystem events = EventSystem.current;
            GameObject selected = events != null ? events.currentSelectedGameObject : null;
            if (selected == null) return false;

            TMPro.TMP_InputField modern = selected.GetComponent<TMPro.TMP_InputField>();
            if (modern != null && modern.isFocused) return true;

            UnityEngine.UI.InputField legacy = selected.GetComponent<UnityEngine.UI.InputField>();
            return legacy != null && legacy.isFocused;
        }
    }
}
