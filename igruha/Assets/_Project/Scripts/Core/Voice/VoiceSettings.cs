using UnityEngine;

namespace Igruha.Core.Voice
{
    /// <summary>Как включается микрофон.</summary>
    public enum VoiceInputMode
    {
        /// <summary>Открытый микрофон: передача начинается сама, как только человек заговорил.</summary>
        Open = 0,

        /// <summary>Рация: передаёт, пока зажата клавиша.</summary>
        PushToTalk = 1
    }

    /// <summary>
    /// Настройки голоса конкретной машины. Живут в PlayerPrefs, потому что это
    /// не игровое состояние: чувствительность микрофона и громкость чужих
    /// голосов — дело каждого, серверу до них нет дела и синхронизировать их
    /// нечего.
    ///
    /// Записываются на диск не сразу, а по <see cref="Flush"/> — когда человек
    /// закрыл настройки или вышел из игры.
    /// </summary>
    public static class VoiceSettings
    {
        private const string EnabledKey = "voice.enabled";
        private const string VolumeKey = "voice.volume";
        private const string ThresholdKey = "voice.threshold";
        private const string GainKey = "voice.gain";
        private const string ModeKey = "voice.mode";
        private const string DeviceKey = "voice.device";

        public const float MinThreshold = 0.002f;
        public const float MaxThreshold = 0.12f;
        public const float MinGain = 0.5f;
        public const float MaxGain = 6f;

        /// <summary>
        /// Записать настройки на диск.
        ///
        /// Отдельным вызовом, а не внутри каждого свойства: ползунок
        /// чувствительности за одно перетаскивание меняет значение десятки
        /// раз, а <c>PlayerPrefs.Save</c> — это запись на диск, и вызывать её
        /// на каждый кадр перетаскивания значит подвешивать игру.
        /// Достаточно сохранить, когда человек закрыл настройки.
        /// </summary>
        public static void Flush() => PlayerPrefs.Save();

        /// <summary>Включён ли микрофон. Выключенный — это полная тишина в эфир.</summary>
        public static bool MicrophoneEnabled
        {
            get => PlayerPrefs.GetInt(EnabledKey, 1) != 0;
            set => PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
        }

        /// <summary>Громкость чужих голосов.</summary>
        public static float Volume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
            set => PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value));
        }

        /// <summary>Порог срабатывания открытого микрофона.</summary>
        public static float Threshold
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(ThresholdKey, 0.02f), MinThreshold, MaxThreshold);
            set => PlayerPrefs.SetFloat(ThresholdKey, Mathf.Clamp(value, MinThreshold, MaxThreshold));
        }

        /// <summary>Усиление своего микрофона — спасает тихие гарнитуры.</summary>
        public static float Gain
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(GainKey, 1f), MinGain, MaxGain);
            set => PlayerPrefs.SetFloat(GainKey, Mathf.Clamp(value, MinGain, MaxGain));
        }

        public static VoiceInputMode Mode
        {
            get => (VoiceInputMode)PlayerPrefs.GetInt(ModeKey, (int)VoiceInputMode.Open);
            set => PlayerPrefs.SetInt(ModeKey, (int)value);
        }

        /// <summary>
        /// Выбранное устройство записи. Пустая строка — системное по умолчанию;
        /// так и оставляем, пока человек не выбрал другое, потому что имена
        /// устройств у всех разные и переносить их между машинами бессмысленно.
        /// </summary>
        public static string Device
        {
            get => PlayerPrefs.GetString(DeviceKey, string.Empty);
            set => PlayerPrefs.SetString(DeviceKey, value ?? string.Empty);
        }
    }
}
