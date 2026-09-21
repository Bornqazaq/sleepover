using UnityEngine;

namespace Igruha.Core.Audio
{
    /// <summary>
    /// Громкость музыки этой машины. Живёт в PlayerPrefs рядом с настройками
    /// голоса и по той же причине: это дело каждого, серверу до неё нет дела.
    ///
    /// Значение по умолчанию заметно ниже единицы. Музыка в party-game —
    /// подложка под крики восьмерых, и на полной громкости она съедает и
    /// реплики диктора, и звук событий, по которым игра читается.
    /// </summary>
    public static class MusicSettings
    {
        private const string VolumeKey = "music.volume";
        private const string EnabledKey = "music.enabled";

        /// <summary>Заводская громкость: слышно, но не мешает разговору.</summary>
        public const float DefaultVolume = 0.35f;

        /// <summary>Играет ли музыка вообще.</summary>
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(EnabledKey, 1) != 0;
            set => PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
        }

        /// <summary>Громкость музыки.</summary>
        public static float Volume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, DefaultVolume));
            set => PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value));
        }

        /// <summary>Записать на диск. Как и у голоса — не на каждое движение ползунка.</summary>
        public static void Flush() => PlayerPrefs.Save();
    }
}
