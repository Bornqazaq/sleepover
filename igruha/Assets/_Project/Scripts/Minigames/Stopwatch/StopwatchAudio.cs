using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.Stopwatch
{
    /// <summary>
    /// Звук, который есть только у «Секундомера» — подфаза 4.5: гонг подраунда,
    /// щелчки кнопки и барабанная дробь с тарелкой на показе результатов.
    /// Всё, что описывает саму арену — лебёдка, створки, медведь, публика, —
    /// лежит в <c>CircusAudio</c> и общее с «Порядком банок».
    ///
    /// <b>Своего состояния и своих RPC нет.</b> Щелчки висят на событиях
    /// <see cref="CageButton.Started"/> и <see cref="CageButton.Stopped"/>,
    /// а гонг и дробь — на <see cref="MinigameStageState"/>. Он и есть тот
    /// сетевой источник, которого не хватало: сервер объявляет стадию, клиент
    /// принимает её через <c>ApplyState</c>, и оба поднимают одни и те же
    /// события. Значит звук идёт у всех и в один момент, без единого пакета
    /// ради звука.
    /// </summary>
    /// <remarks>
    /// <b>Почему щелчок кнопки трёхмерный, а гонг нет.</b> Кнопка — предмет
    /// в конкретной клетке, и по звуку должно быть слышно, своя она или
    /// соседская: спека 5.2 разрешает знать, что сосед начал. Гонг и дробь
    /// объявляют стадию всем разом и точки в пространстве не имеют.
    ///
    /// <b>Дробь и тарелка — два слота, а не один файл.</b> Дробь тянется всю
    /// стадию результатов, тарелка бьёт в конце; склеенным клипом их уже не
    /// развести по времени, а стадия настраивается числом в конфиге.
    /// Поэтому тарелка ставится по длительности стадии, а не по фиксированной
    /// задержке.
    /// </remarks>
    public sealed class StopwatchAudio : MonoBehaviour
    {
        [SerializeField] private MinigameAudioPlayer audioPlayer;

        [Tooltip("Кнопки клеток. Заполняет билдер реквизита")]
        [SerializeField] private CageButton[] buttons;

        [Tooltip("Стадии подраунда. Отсюда приходят гонг и дробь")]
        [SerializeField] private MinigameStageState stageState;

        private const string GongId = "round_gong";
        private const string StartId = "button_start";
        private const string StopId = "button_stop";
        private const string DrumrollId = "drumroll";
        private const string CymbalId = "cymbal";

        /// <summary>
        /// За сколько до конца стадии результатов бьёт тарелка, с.
        /// Она обязана прийтись на момент, когда цифры уже прочитаны,
        /// а не на их появление.
        /// </summary>
        private const float CymbalLead = 0.9f;

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

            if (stageState != null)
            {
                stageState.SubroundStarted += OnSubroundStarted;
                stageState.StageStarted += OnStageStarted;
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

            if (stageState != null)
            {
                stageState.SubroundStarted -= OnSubroundStarted;
                stageState.StageStarted -= OnStageStarted;
            }

            CancelInvoke();
        }

        private int ButtonCount => buttons == null ? 0 : buttons.Length;

        private void OnSubroundStarted(int subround)
        {
            if (audioPlayer != null)
            {
                audioPlayer.Play(GongId);
            }
        }

        private void OnStageStarted(byte stage)
        {
            if (audioPlayer == null || stage != StopwatchMinigame.StageResults)
            {
                return;
            }

            audioPlayer.Play(DrumrollId);

            // Тарелку ставим от длительности стадии, а не от длительности
            // дроби: стадия крутится числом в конфиге, и приколоченная
            // задержка разъехалась бы с ней при первой же правке.
            float lead = Mathf.Max(0.2f, stageState.StageDuration - CymbalLead);
            CancelInvoke(nameof(PlayCymbal));
            Invoke(nameof(PlayCymbal), lead);
        }

        private void PlayCymbal()
        {
            if (audioPlayer != null)
            {
                audioPlayer.Play(CymbalId);
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
