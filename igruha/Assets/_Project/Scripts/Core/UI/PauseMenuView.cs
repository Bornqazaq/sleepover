using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Igruha.Core.Audio;

namespace Igruha.Core.UI
{
    /// <summary>Presentation of the pause menu; PauseScreen owns input, time and exit behaviour.</summary>
    public sealed class PauseMenuView : MonoBehaviour
    {
        [SerializeField] private TMP_Text title, message, status, primaryLabel, secondaryLabel, footer;
        [SerializeField] private Button primary;
        private bool networked, leavingRound, hosting;
        public bool IsConfirmingExit { get; private set; }

        private void LateUpdate()
        {
            // Clicking the backdrop must not lose keyboard navigation to another screen.
            var events = EventSystem.current;
            if (events == null || primary == null) return;
            var selected = events.currentSelectedGameObject;
            if (selected == null || !selected.transform.IsChildOf(transform)) primary.Select();
        }

        public void Show(bool online, bool canLeaveRound, bool isHost)
        {
            networked = online; leavingRound = canLeaveRound; hosting = isHost;
            CancelExit();
        }

        public void ConfirmExit()
        {
            // Вопрос «точно выйти?» звучит своим звуком: кнопка его открывает
            // обычным щелчком, а вопрос — это уже другой экран, и слышно это
            // должно быть до того, как игрок прочтёт текст.
            if (!IsConfirmingExit) UiAudio.Play(CoreSfx.UiDialogOpen);

            IsConfirmingExit = true;
            title.text = leavingRound ? "Покинуть\nэтот раунд?" : "Закончить\nна сегодня?";
            message.text = leavingRound ? "Ты останешься в компании и сможешь\nнаблюдать до следующего раунда." :
                hosting ? "Ты — хост. При выходе игра закроется,\nа друзья потеряют соединение." :
                networked ? "Ты отключишься от компании.\nИгра закроется." : "Игра закроется.\nМожем сыграть ещё один раунд?";
            primaryLabel.text = "ОСТАТЬСЯ";
            secondaryLabel.text = leavingRound ? "ПОКИНУТЬ РАУНД" : "ДА, ВЫЙТИ";
            footer.text = "↑ ↓  ВЫБОР     ENTER  ПОДТВЕРДИТЬ     ESC  НАЗАД";
            primary.Select();
        }

        public void CancelExit()
        {
            // Отмена звучит только когда есть что отменять: этот же метод зовёт
            // открытие паузы, и там звук был бы эхом уже сыгранного щелчка.
            if (IsConfirmingExit) UiAudio.Play(CoreSfx.UiBack);

            IsConfirmingExit = false;
            title.text = "Небольшой\nперерыв";
            message.text = "Устраивайся поудобнее.\nПродолжим, когда будешь готов.";
            status.text = networked ? "ОНЛАЙН · ИГРА ПРОДОЛЖАЕТСЯ" : "ИГРА НА ПАУЗЕ";
            if (networked) message.text = "Меню открыто только у тебя.\nДрузья продолжают играть.";
            primaryLabel.text = "ПРОДОЛЖИТЬ";
            secondaryLabel.text = leavingRound ? "ПОКИНУТЬ РАУНД" : "ВЫЙТИ ИЗ ИГРЫ";
            footer.text = "↑ ↓  ВЫБОР     ENTER  ВЫБРАТЬ     ESC  В ИГРУ";
            primary.Select();
        }
    }
}
