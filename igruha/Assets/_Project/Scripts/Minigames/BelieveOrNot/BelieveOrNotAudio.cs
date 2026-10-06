using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>
    /// Звук «Верю / не верю» — фаза 5. Гонг на рассадке, щелчок защёлки
    /// на показе карточки, блипы реплик, тик последних секунд, скрип обмена,
    /// две крышки разом, аккорд исхода, пшик облачка и реакция зала поверх
    /// тихого гула комнаты.
    ///
    /// <b>Своего состояния и своих RPC здесь нет</b> — ровно как у
    /// <c>HoleInWallAudio</c> фазы 5. Каждый звук висит на том, что игра
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
        /// Клятва и окончательное решение звучат одинаково при любом скрытом исходе.
        /// </summary>
        private const string SlotOath = "oath_seal";
        private const string SlotDecision = "decision_lock";
        private const string SlotApplause = "crowd_applause";

        [Header("Сцена")]
        [SerializeField] private BelieveOrNotMinigame game;
        [SerializeField] private BelieveTable table;
        [SerializeField] private MinigameStageState stageState;
        [SerializeField] private BelieveOrNotConfig config;
        [SerializeField] private MinigameAudioPlayer audioPlayer;

        private bool ambient;
        private int lastTick = -1;
        private bool deciderWon;
        public event System.Action<string> CuePlayed;

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
                game.OathCommitted += OnOathCommitted;
                game.LidsOpening += OnLidsOpening;
                game.CardsRevealed += OnCardsRevealed;
                game.DuelEnded += StopAudio;
                game.FinalAnnounced += OnFinal;
                game.ChampionAnnounced += OnChampion;
                game.PredictionStreakAwarded += OnStreak;
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
                game.OathCommitted -= OnOathCommitted;
                game.LidsOpening -= OnLidsOpening;
                game.CardsRevealed -= OnCardsRevealed;
                game.DuelEnded -= StopAudio;
                game.FinalAnnounced -= OnFinal;
                game.ChampionAnnounced -= OnChampion;
                game.PredictionStreakAwarded -= OnStreak;
            }
            StopAudio();
        }

        private void StopAudio()
        {
            lastTick = -1;
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

            int second = Mathf.CeilToInt(stageState.StageRemaining);
            if (stageState.Stage != BelieveStage.Persuasion || second <= 0 || second > TickLeadSeconds || second == lastTick) return;
            lastTick = second;
            Play(SlotTick);
        }

        private void OnStageStarted(byte stage)
        {
            if (audioPlayer == null)
            {
                return;
            }

            // Гул комнаты заводится с первой же стадией и живёт до конца
            // мини-игры: это воздух зала, а не событие.
            if (stage == BelieveStage.Champion) return;
            if (!ambient)
            {
                ambient = true;
                audioPlayer.StartLoop(SlotAmbience);
            }

            lastTick = -1;
            if (stage == BelieveStage.Cancelled) { audioPlayer.StopAll(); ambient = false; return; }
            // Тихий воздух под разговором; на раскрытии оставляем место замкам и исходу.
            audioPlayer.SetLoopLevel(SlotAmbience, stage == BelieveStage.Reveal ? .18f : 1f, 1f);

            switch (stage)
            {
                case BelieveStage.Seating:
                    Play(SlotGong);
                    break;

                case BelieveStage.Peek:
                    Play(SlotLatch);
                    break;

                case BelieveStage.Reaction:
                    PlayGagSound();
                    Play(deciderWon ? SlotApplause : SlotCrowd);
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
                    PlayAt(SlotGag, box.transform.position);
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
            PlayAt(byKnower ? SlotPhraseKnower : SlotPhraseDecider, point);
        }

        private void OnRevealStarted(Decision decision, bool deciderGuessedRight)
        {
            deciderWon = deciderGuessedRight;
            Play(SlotDecision);
            if (decision == Decision.Swap) PlayAt(SlotSwap, TablePoint);
        }

        private Vector3 TablePoint => table != null ? table.transform.position : transform.position;
        private void OnFinal() => Play("final_intro");
        private void OnChampion() { audioPlayer?.StopAll(); ambient = false; Play("tournament_win"); }
        private void OnStreak() => Play("prediction_streak");
        private void OnOathCommitted() => Play(SlotOath);
        private void OnLidsOpening() => PlayAt(SlotLids, TablePoint);
        private void OnCardsRevealed(bool guessedRight) => Play(guessedRight ? SlotWin : SlotFail);

        private void Play(string slot)
        {
            if (audioPlayer == null) return;
            audioPlayer.Play(slot);
            CuePlayed?.Invoke(slot);
        }

        private void PlayAt(string slot, Vector3 position)
        {
            if (audioPlayer == null) return;
            audioPlayer.PlayAt(slot, position);
            CuePlayed?.Invoke(slot);
        }
    }
}
