using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.Exam
{
    /// <summary>
    /// Звук «Экзамена» — подфаза 4.5. Одиннадцать слотов на события, которые
    /// игра уже подняла у каждого.
    ///
    /// <b>Своего состояния и своих RPC здесь нет.</b> Фазы приезжают через
    /// <see cref="MinigameStageState"/> — он реплицирован, и его
    /// <c>StageStarted</c> поднимается на каждой машине. Створки раскрываются
    /// локально и у хоста, и у клиента. Бонус за одиночество и несостоявшийся
    /// вопрос — локальные события игры, поднятые в тех же местах, где меняется
    /// уже реплицированное состояние. Звук, слышный только инициатору, означал
    /// бы неверную привязку, а не проблему звука.
    ///
    /// <b>Створки звучат из своей точки.</b> Открытие и закрытие идут через
    /// <c>PlayAt</c> с позицией платформы: у А и Б они звучат с разных сторон
    /// зала, и это часть ответа на вопрос «какая раскрылась» — тот же канал
    /// читаемости, что цвет и буква, только на слух.
    /// </summary>
    public sealed class ExamAudio : MonoBehaviour
    {
        [Tooltip("Проигрыватель звука мини-игры со ссылкой на библиотеку слотов")]
        [SerializeField] private MinigameAudioPlayer player;

        [Tooltip("Машина фаз: по её стадиям играют печать, показ, выбор и нагнетание")]
        [SerializeField] private MinigameStageState stageState;

        [Tooltip("Контроллер: с него приходит бонус за одиночество")]
        [SerializeField] private ExamMinigame minigame;

        [Tooltip("Доска: с неё приходит несостоявшийся вопрос")]
        [SerializeField] private ExamBoard board;

        [SerializeField] private ExamAnswerPlatform platformA;
        [SerializeField] private ExamAnswerPlatform platformB;

        [Tooltip("Точка, из которой звучит провал: середина ямы под платформами")]
        [SerializeField] private Transform pitPoint;

        private const string SlotTyping = "typing_loop";
        private const string SlotPosted = "question_posted";
        private const string SlotChoice = "choice_start";
        private const string SlotDrum = "tension_drum";
        private const string SlotHatchOpen = "hatch_open";
        private const string SlotHatchClose = "hatch_close";
        private const string SlotFall = "fall_scream";
        private const string SlotRespawn = "respawn_pop";
        private const string SlotLonely = "lonely_bonus";
        private const string SlotSkipped = "question_skipped";
        private const string SlotFanfare = "match_fanfare";

        private bool openA;
        private bool openB;
        private bool typing;

        private void OnEnable()
        {
            if (stageState != null)
            {
                stageState.StageStarted += OnStageStarted;
            }

            if (minigame != null)
            {
                minigame.LonelyBonusAwarded += OnLonelyBonus;
            }

            if (board != null)
            {
                board.Skipped += OnSkipped;
            }
        }

        private void OnDisable()
        {
            if (stageState != null)
            {
                stageState.StageStarted -= OnStageStarted;
            }

            if (minigame != null)
            {
                minigame.LonelyBonusAwarded -= OnLonelyBonus;
            }

            if (board != null)
            {
                board.Skipped -= OnSkipped;
            }

            StopTyping();
        }

        /// <summary>
        /// Створки опрашиваются, а не подписываются, по той же причине, что
        /// и у эффектов: событие <c>Released</c> поднимается в середине хода,
        /// а звук раскрытия обязан начаться вместе с первым движением.
        /// </summary>
        private void Update()
        {
            Track(platformA, ref openA);
            Track(platformB, ref openB);
        }

        private void Track(ExamAnswerPlatform platform, ref bool wasOpen)
        {
            if (platform == null || player == null)
            {
                return;
            }

            bool open = platform.DoorsOpen;
            if (open == wasOpen)
            {
                return;
            }

            wasOpen = open;
            if (open)
            {
                player.PlayAt(SlotHatchOpen, platform.transform.position);
                player.PlayAt(SlotFall, pitPoint != null ? pitPoint.position : platform.transform.position);
            }
            else
            {
                player.PlayAt(SlotHatchClose, platform.transform.position);
            }
        }

        private void OnStageStarted(byte stage)
        {
            if (player == null)
            {
                return;
            }

            // Печать — единственный луп игры. Он держится всю фазу и снимается
            // на любой следующей: вопрос напечатан или Ведущий не успел —
            // стрёкот обязан прекратиться в обоих случаях.
            if (stage != ExamMinigame.StageTyping)
            {
                StopTyping();
            }

            switch (stage)
            {
                case ExamMinigame.StageTyping:
                    StartTyping();
                    break;

                case ExamMinigame.StageReveal:
                    player.Play(SlotPosted);
                    break;

                case ExamMinigame.StageChoice:
                    player.Play(SlotChoice);
                    break;

                case ExamMinigame.StageTension:
                    player.Play(SlotDrum);
                    break;

                case ExamMinigame.StageRespawn:
                    player.Play(SlotRespawn);
                    break;
            }
        }

        private void StartTyping()
        {
            if (typing || player == null)
            {
                return;
            }

            typing = true;
            player.StartLoop(SlotTyping);
        }

        private void StopTyping()
        {
            if (!typing || player == null)
            {
                return;
            }

            typing = false;
            player.StopLoop(SlotTyping);
        }

        private void OnLonelyBonus() => player?.Play(SlotLonely);

        private void OnSkipped() => player?.Play(SlotSkipped);

        /// <summary>
        /// Фанфара итогов. Вызывается игрой в момент показа результатов —
        /// у экрана итогов нет фазы в <see cref="MinigameStageState"/>,
        /// он приходит из общего <c>MinigameControllerBase</c>.
        /// </summary>
        public void PlayMatchFanfare() => player?.Play(SlotFanfare);
    }
}
