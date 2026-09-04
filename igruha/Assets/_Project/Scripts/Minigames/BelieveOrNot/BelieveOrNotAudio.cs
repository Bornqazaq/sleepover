using System.Collections;
using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>
    /// Звук «Верю / не верю» — подфаза 4.5. Гонг на рассадке, щелчок защёлки
    /// на показе карточки, блипы реплик, тик последних секунд, скрип обмена,
    /// две крышки разом, аккорд исхода, пшик облачка и реакция зала поверх
    /// тихого гула комнаты.
    ///
    /// <b>Своего состояния и своих RPC здесь нет</b> — ровно как у
    /// <c>HoleInWallAudio</c> подфазы 4.5. Каждый звук висит на том, что игра
    /// уже посчитала и уже показала всем: стадии приходят из
    /// <see cref="MinigameStageState"/>, реплика и раскрытие — из локальных
    /// событий контроллера, которые он поднимает на каждой машине после того,
    /// как показал то же самое глазами.
    ///
    /// Отсюда и проверка привязки: <b>звук, слышный только инициатору,
    /// означает неверное событие, а не проблему звука.</b>
    /// </summary>
    /// <remarks>
    /// <b>Что здесь пространственное, а что нет.</b> Зал 24 метра в
    /// поперечнике, и зритель у дальней стены обязан слышать, что за столом
    /// что-то произошло. Поэтому гонг, щелчок защёлки, тик, аккорд исхода
    /// и реакция зала звучат ровно у всех: это события кона, а не предметы.
    /// Трёхмерны только те звуки, у которых есть место: реплики (над своим
    /// местом), обмен, крышки и облачко (у стола).
    ///
    /// <b>Аккорд исхода один на всех, а не «победа мне, провал сопернику».</b>
    /// Исход кона — общий факт: Решающий либо угадал, либо нет. Личный звук
    /// пришлось бы считать от того, кто слушает, а зрителю в зале он не
    /// достался бы вовсе.
    /// </remarks>
    public sealed class BelieveOrNotAudio : MonoBehaviour
    {
        // ========== СЛОТЫ БИБЛИОТЕКИ ==========
        // Те же идентификаторы, что в docs/art/believe-or-not-sfx.json.

        private const string SlotGong = "round_gong";
        private const string SlotLatch = "peek_latch";
        private const string SlotPhraseKnower = "phrase_knower";
        private const string SlotPhraseDecider = "phrase_decider";
        private const string SlotTick = "tick_last";
        private const string SlotSwap = "box_swap";
        private const string SlotLids = "lids_open";
        private const string SlotWin = "outcome_win";
        private const string SlotFail = "outcome_fail";
        private const string SlotGag = "gag_puff";
        private const string SlotCrowd = "crowd_react";
        private const string SlotAmbience = "hall_ambience";

        /// <summary>
        /// За сколько секунд до конца уговоров пускается тик. Пять — то же
        /// число, что в брифе: раньше он превращается в фон и перестаёт
        /// подгонять, позже — не успевает прозвучать.
        /// </summary>
        private const float TickLeadSeconds = 5f;

        /// <summary>
        /// Пауза между крышками и аккордом исхода. Полсекунды тишины —
        /// требование брифа (14.6): аккорд, наложенный на стук крышек,
        /// перестаёт быть ответом на вопрос «ну что там».
        /// </summary>
        private const float ChordDelaySeconds = 0.45f;

        [Header("Сцена")]
        [SerializeField] private BelieveOrNotMinigame game;
        [SerializeField] private BelieveTable table;
        [SerializeField] private MinigameStageState stageState;
        [SerializeField] private BelieveOrNotConfig config;
        [SerializeField] private MinigameAudioPlayer audioPlayer;

        private bool ticking;
        private bool ambient;
        private Coroutine revealRoutine;

        private void OnEnable()
        {
            if (stageState != null)
            {
                stageState.StageStarted += OnStageStarted;
            }

            if (game != null)
            {
                game.PhraseShown += OnPhraseShown;
                game.RevealStarted += OnRevealStarted;
            }
        }

        private void OnDisable()
        {
            if (stageState != null)
            {
                stageState.StageStarted -= OnStageStarted;
            }

            if (game != null)
            {
                game.PhraseShown -= OnPhraseShown;
                game.RevealStarted -= OnRevealStarted;
            }

            if (revealRoutine != null)
            {
                StopCoroutine(revealRoutine);
                revealRoutine = null;
            }

            ticking = false;
            ambient = false;
            audioPlayer?.StopAll();
        }

        /// <summary>
        /// Тик последних секунд. Ведётся временем стадии, а не таймером
        /// компонента: у стадии одно время на всех машинах, а свой таймер
        /// разъехался бы у каждого по-своему.
        /// </summary>
        private void Update()
        {
            if (audioPlayer == null || stageState == null)
            {
                return;
            }

            bool shouldTick = stageState.Stage == BelieveStage.Persuasion
                              && stageState.StageRemaining <= TickLeadSeconds;

            if (shouldTick == ticking)
            {
                return;
            }

            ticking = shouldTick;
            if (ticking)
            {
                audioPlayer.StartLoop(SlotTick);
            }
            else
            {
                audioPlayer.StopLoop(SlotTick);
            }
        }

        private void OnStageStarted(byte stage)
        {
            if (audioPlayer == null)
            {
                return;
            }

            // Гул комнаты заводится с первой же стадией и живёт до конца
            // мини-игры: это воздух зала, а не событие.
            if (!ambient)
            {
                ambient = true;
                audioPlayer.StartLoop(SlotAmbience);
            }

            switch (stage)
            {
                case BelieveStage.Seating:
                    audioPlayer.Play(SlotGong);
                    break;

                case BelieveStage.Peek:
                    audioPlayer.Play(SlotLatch);
                    break;

                case BelieveStage.Reaction:
                    PlayGagSound();
                    audioPlayer.Play(SlotCrowd);
                    break;
            }
        }

        /// <summary>
        /// Пшик облачка — у проигравшей коробки, а не у обеих. Коробка
        /// с крестом известна на каждой машине: карточки приезжают
        /// в раскрытие всем сразу.
        /// </summary>
        private void PlayGagSound()
        {
            if (table == null)
            {
                return;
            }

            for (int seat = 0; seat < BelieveTable.SeatCount; seat++)
            {
                BelieveBox box = table.GetBox(seat);
                if (box != null && box.Card == BelieveCard.Lose)
                {
                    audioPlayer.PlayAt(SlotGag, box.transform.position);
                    return;
                }
            }
        }

        private void OnPhraseShown(int seat, bool byKnower)
        {
            if (audioPlayer == null || table == null)
            {
                return;
            }

            Transform anchor = table.GetSeatAnchor(seat);
            Vector3 point = anchor != null ? anchor.position : table.transform.position;
            audioPlayer.PlayAt(byKnower ? SlotPhraseKnower : SlotPhraseDecider, point);
        }

        private void OnRevealStarted(Decision decision, bool deciderGuessedRight)
        {
            if (audioPlayer == null)
            {
                return;
            }

            if (revealRoutine != null)
            {
                StopCoroutine(revealRoutine);
            }

            revealRoutine = StartCoroutine(RevealRoutine(decision, deciderGuessedRight));
        }

        /// <summary>
        /// Звуковая дорожка раскрытия. Идёт теми же паузами, что и картинка:
        /// сначала обмен коробок, потом крышки, потом аккорд. Числа берутся
        /// из конфига, а не пишутся здесь: разъехавшись с анимацией, звук
        /// объявил бы исход раньше, чем крышки поднялись.
        /// </summary>
        private IEnumerator RevealRoutine(Decision decision, bool deciderGuessedRight)
        {
            Vector3 point = table != null ? table.transform.position : transform.position;

            if (decision == Decision.Swap)
            {
                audioPlayer.PlayAt(SlotSwap, point);
                yield return new WaitForSeconds(config != null ? config.BoxSwapSeconds : 1.2f);
            }

            audioPlayer.PlayAt(SlotLids, point);
            yield return new WaitForSeconds(ChordDelaySeconds);

            audioPlayer.Play(deciderGuessedRight ? SlotWin : SlotFail);
            revealRoutine = null;
        }
    }
}
