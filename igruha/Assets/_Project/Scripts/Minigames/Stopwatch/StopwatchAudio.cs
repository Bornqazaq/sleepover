using UnityEngine;
using Igruha.Core.Audio;

namespace Igruha.Minigames.Stopwatch
{
    /// <summary>
    /// Звук, который есть только у «Секундомера» — подфаза 4.5: гонг подраунда
    /// и щелчки кнопки. Всё, что описывает саму арену — лебёдка, створки,
    /// медведь, публика, — лежит в <c>CircusAudio</c> и общее с «Порядком
    /// банок».
    ///
    /// <b>Своего состояния и своих RPC нет.</b> Щелчки висят на событиях
    /// <see cref="CageButton.Started"/> и <see cref="CageButton.Stopped"/>,
    /// гонг — на смене номера подраунда, который приезжает с сервера через
    /// <c>ApplyNetworkTask</c>. Каждая машина приходит к звуку сама из
    /// одинакового состояния, поэтому он звучит у всех и в один момент.
    /// </summary>
    /// <remarks>
    /// <b>Почему щелчок кнопки трёхмерный, а гонг нет.</b> Кнопка — предмет
    /// в конкретной клетке, и по звуку должно быть слышно, своя она или
    /// соседская: спека 5.2 разрешает знать, что сосед начал. Гонг объявляет
    /// подраунд всем разом и точки в пространстве не имеет.
    ///
    /// <b>Чего здесь нет.</b> Барабанной дроби и тарелки на показе
    /// результатов: стадия подраунда наружу из <see cref="StopwatchMinigame"/>
    /// не выставлена, а заводить ради звука новое свойство — это правка
    /// правил игры, то есть фаза 2. Клипы `drumroll` и `cymbal` сгенерированы
    /// и лежат в библиотеке; привязать их — отдельный тикет.
    /// </remarks>
    public sealed class StopwatchAudio : MonoBehaviour
    {
        [SerializeField] private MinigameAudioPlayer audioPlayer;

        [Tooltip("Кнопки клеток. Заполняет билдер реквизита")]
        [SerializeField] private CageButton[] buttons;

        [SerializeField] private StopwatchMinigame game;

        private const string GongId = "round_gong";
        private const string StartId = "button_start";
        private const string StopId = "button_stop";

        private int lastSubround = -1;

        private void OnEnable()
        {
            for (int i = 0; i < ButtonCount; i++)
            {
                if (buttons[i] == null)
                {
                    continue;
                }

                buttons[i].Started += OnStarted;
                buttons[i].Stopped += OnStopped;
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < ButtonCount; i++)
            {
                if (buttons[i] == null)
                {
                    continue;
                }

                buttons[i].Started -= OnStarted;
                buttons[i].Stopped -= OnStopped;
            }
        }

        private int ButtonCount => buttons == null ? 0 : buttons.Length;

        /// <summary>
        /// Гонг на смене подраунда. Читаем номер, а не событие: события начала
        /// подраунда игра наружу не поднимает, а номер приезжает с сервера
        /// и одинаков у всех.
        /// </summary>
        private void Update()
        {
            if (game == null || audioPlayer == null)
            {
                return;
            }

            int subround = game.Subround;
            if (subround == lastSubround)
            {
                return;
            }

            // Первый заход не звенит: при загрузке сцены номер меняется
            // с −1 на 0, и гонг ударил бы до начала игры.
            bool first = lastSubround < 0;
            lastSubround = subround;
            if (!first && subround > 0)
            {
                audioPlayer.Play(GongId);
            }
        }

        private void OnStarted(CageButton button)
        {
            if (audioPlayer != null && button != null)
            {
                audioPlayer.PlayAt(StartId, button.transform.position);
            }
        }

        private void OnStopped(CageButton button, float measured)
        {
            if (audioPlayer != null && button != null)
            {
                audioPlayer.PlayAt(StopId, button.transform.position);
            }
        }
    }
}
