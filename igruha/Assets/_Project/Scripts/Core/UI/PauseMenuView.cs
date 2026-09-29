using Igruha.Core.Audio;
using Igruha.Core.Voice;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Igruha.Core.UI
{
    /// <summary>Одинаковая пауза в хабе и мини-играх. Вводом и выходом владеет PauseScreen.</summary>
    public sealed class PauseMenuView : MonoBehaviour
    {
        [SerializeField] private TMP_Text title, message, status, primaryLabel, secondaryLabel, footer;
        [SerializeField] private Button primary, quit;
        [SerializeField] private GameObject settings;
        [SerializeField] private Slider music, voice;
        [SerializeField] private TMP_Text musicValue, voiceValue;
        private bool networked, leavingRound, hosting, volumeTouched;
        public bool IsConfirmingExit { get; private set; }
        public bool WantsQuit { get; private set; }

        private void Awake()
        {
            music.onValueChanged.AddListener(SetMusic);
            voice.onValueChanged.AddListener(SetVoice);
        }
        private void OnDestroy()
        {
            music.onValueChanged.RemoveListener(SetMusic);
            voice.onValueChanged.RemoveListener(SetVoice);
        }
        private void OnDisable()
        {
            if (!volumeTouched) return;
            VoiceSettings.Flush(); MusicSettings.Flush(); volumeTouched = false;
        }
        private void LateUpdate()
        {
            var events = EventSystem.current;
            if (events == null || primary == null) return;
            var selected = events.currentSelectedGameObject;
            if (selected == null || !selected.activeInHierarchy || !selected.transform.IsChildOf(transform)) primary.Select();
        }
        public void Show(bool online, bool canLeaveRound, bool isHost)
        {
            networked = online; leavingRound = canLeaveRound; hosting = isHost;
            music.SetValueWithoutNotify(MusicSettings.Volume); voice.SetValueWithoutNotify(VoiceSettings.Volume);
            SetPercent(musicValue, music.value); SetPercent(voiceValue, voice.value);
            CancelExit();
        }
        public void ConfirmExit(bool quitGame = false)
        {
            if (!IsConfirmingExit) UiAudio.Play(CoreSfx.UiDialogOpen);
            IsConfirmingExit = true;
            WantsQuit = quitGame || !leavingRound;
            title.text = WantsQuit ? "Закончить на сегодня?" : "Покинуть этот раунд?";
            message.text = !WantsQuit ? "Вы останетесь в компании и сможете наблюдать до следующего раунда." :
                hosting ? "Вы — ведущий. Игра закроется, а друзья потеряют соединение." :
                networked ? "Вы отключитесь от компании. Игра закроется." : "Игра закроется. Продолжим в следующий раз?";
            primaryLabel.text = "Остаться";
            secondaryLabel.text = WantsQuit ? "Да, выйти из игры" : "Да, покинуть раунд";
            footer.text = "ESC — отменить";
            settings.SetActive(false); quit.gameObject.SetActive(false);
            primary.Select();
        }
        public void CancelExit()
        {
            if (IsConfirmingExit) UiAudio.Play(CoreSfx.UiBack);
            IsConfirmingExit = WantsQuit = false;
            title.text = "Небольшой перерыв";
            message.text = networked ? "Меню открыто только у вас. Друзья продолжают играть." : "Устраивайтесь поудобнее. Продолжим, когда будете готовы.";
            status.text = networked ? "ОНЛАЙН  ·  ИГРА ПРОДОЛЖАЕТСЯ" : "ИГРА НА ПАУЗЕ";
            primaryLabel.text = "Продолжить";
            secondaryLabel.text = leavingRound ? "Покинуть раунд" : "Выйти из игры";
            footer.text = "ESC — вернуться в игру  ·  ↑ ↓ — выбор";
            settings.SetActive(true); quit.gameObject.SetActive(leavingRound);
            primary.Select();
        }
        private void SetMusic(float value)
        {
            if (MusicPlayer.Instance != null) MusicPlayer.Instance.SetVolume(value); else MusicSettings.Volume = value;
            SetPercent(musicValue, value); volumeTouched = true;
        }
        private void SetVoice(float value)
        {
            if (VoiceChatRuntime.Instance != null) VoiceChatRuntime.Instance.SetVolume(value); else VoiceSettings.Volume = value;
            SetPercent(voiceValue, value); volumeTouched = true;
        }
        private static void SetPercent(TMP_Text label, float value) => label.text = Mathf.RoundToInt(value * 100f) + "%";
    }
}
