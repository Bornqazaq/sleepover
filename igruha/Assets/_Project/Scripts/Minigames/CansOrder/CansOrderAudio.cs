using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Minigame;
using Igruha.Core.Player;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>
    /// Звук, который есть только у «Порядка банок» — подфаза 4.5. Четыре
    /// события, которых у арены нет: стук банки о полку, колокол
    /// подтверждения, загорающееся табло и фанфара собравшему.
    ///
    /// Всё, что описывает саму арену — лебёдка, створки, лапа медведя, вздох
    /// и смех зала, — лежит в <c>CircusAudio</c> и общее с «Секундомером».
    /// Генерировать его заново значило бы получить два разных шатра на слух.
    ///
    /// <b>Своего состояния и своих RPC нет.</b> Каждый слот висит на событии,
    /// которое игра уже подняла:
    /// <list type="bullet">
    /// <item>стук — <see cref="CanShelf.ArrangementChanged"/>;</item>
    /// <item>колокол — <see cref="CanConfirmButton.Confirmed"/>;</item>
    /// <item>табло — <see cref="MinigameStageState.StageStarted"/>, стадия
    /// приезжает с сервера и одинакова у всех;</item>
    /// <item>фанфара — <see cref="CansOrderMinigame.SolvedFanfarePlayed"/>,
    /// единственная воронка на все машины.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <b>Стук и колокол слышит только тот, кто их вызвал, и это не ошибка
    /// привязки.</b> Оба события по построению локальны: перестановка банок
    /// вообще не синхронизируется (спека 10.2), а «принято» до стадии показа
    /// не должно быть известно никому, кроме самого игрока — по тому же
    /// правилу, по которому лампа кнопки зажигается только у владельца
    /// (спека 4). Услышать чужой колокол значило бы узнать, что сосед уже
    /// подтвердил, а это преимущество, которого в игре быть не должно.
    ///
    /// Табло и фанфара, наоборот, звучат у всех: это общие события круга.
    /// </remarks>
    public sealed class CansOrderAudio : MonoBehaviour
    {
        [SerializeField] private MinigameAudioPlayer audioPlayer;

        [Tooltip("Полки клеток. Заполняет билдер реквизита")]
        [SerializeField] private CanShelf[] shelves;

        [Tooltip("Кнопки клеток. Заполняет билдер реквизита")]
        [SerializeField] private CanConfirmButton[] buttons;

        [Tooltip("Стадии круга. Отсюда приходит вспышка табло")]
        [SerializeField] private MinigameStageState stageState;

        [Tooltip("Контроллер игры. Отсюда приходит фанфара собравшему")]
        [SerializeField] private CansOrderMinigame game;

        private const string SwapId = "can_swap";
        private const string ConfirmId = "confirm_bell";
        private const string RevealId = "board_reveal";
        private const string FanfareId = "solved_fanfare";

        private void OnEnable()
        {
            for (int i = 0; i < ShelfCount; i++)
            {
                if (shelves[i] != null)
                {
                    shelves[i].ArrangementChanged += OnArrangementChanged;
                }
            }

            for (int i = 0; i < ButtonCount; i++)
            {
                if (buttons[i] != null)
                {
                    buttons[i].Confirmed += OnConfirmed;
                }
            }

            if (stageState != null)
            {
                stageState.StageStarted += OnStageStarted;
            }

            if (game != null)
            {
                game.SolvedFanfarePlayed += OnSolved;
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < ShelfCount; i++)
            {
                if (shelves[i] != null)
                {
                    shelves[i].ArrangementChanged -= OnArrangementChanged;
                }
            }

            for (int i = 0; i < ButtonCount; i++)
            {
                if (buttons[i] != null)
                {
                    buttons[i].Confirmed -= OnConfirmed;
                }
            }

            if (stageState != null)
            {
                stageState.StageStarted -= OnStageStarted;
            }

            if (game != null)
            {
                game.SolvedFanfarePlayed -= OnSolved;
            }
        }

        private int ShelfCount => shelves != null ? shelves.Length : 0;
        private int ButtonCount => buttons != null ? buttons.Length : 0;

        /// <summary>
        /// Стук банки о доску. Событие поднимается и на первой раскладке полки,
        /// когда банок ещё не было, — там стучать нечему, поэтому играем только
        /// пока окно выставления открыто.
        /// </summary>
        private void OnArrangementChanged(CanShelf shelf)
        {
            if (audioPlayer == null || shelf == null || !shelf.Active)
            {
                return;
            }

            audioPlayer.PlayAt(SwapId, shelf.Board != null ? shelf.Board.position : shelf.transform.position);
        }

        private void OnConfirmed(CanConfirmButton button, PlayerController player)
        {
            if (audioPlayer != null && button != null)
            {
                audioPlayer.PlayAt(ConfirmId, button.transform.position);
            }
        }

        private void OnStageStarted(byte stage)
        {
            if (audioPlayer != null && stage == CansOrderMinigame.StageReveal)
            {
                audioPlayer.Play(RevealId);
            }
        }

        private void OnSolved(Vector3 at)
        {
            if (audioPlayer != null)
            {
                audioPlayer.PlayAt(FanfareId, at);
            }
        }
    }
}
