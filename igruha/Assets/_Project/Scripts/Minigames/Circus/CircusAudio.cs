using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Minigame;
using Igruha.Core.Player;

namespace Igruha.Minigames.Circus
{
    /// <summary>
    /// Звук цирковой арены — подфаза 4.5, общий на «Секундомер» и «Порядок
    /// банок». Лебёдка на спуске клетки, лязг створок, удар лапой, реакция
    /// публики и финальные фанфары.
    ///
    /// <b>Своего состояния и своих RPC нет.</b> Как и <see cref="CircusEffects"/>,
    /// компонент висит на событиях, которые игра уже подняла на каждой машине:
    /// движении синхронизированной платформы, <c>DoorsOpened</c>, состоянии
    /// медведя и <c>ResultsReported</c>. Отсюда следует главное требование
    /// подфазы: <b>сетевые события звучат у всех, а не только у инициатора</b> —
    /// не потому, что звук куда-то рассылается, а потому, что каждая машина
    /// приходит к нему сама из одинакового состояния.
    ///
    /// <b>Почему общий, а не «секундомерный».</b> Клетка опускается, створки
    /// открываются и медведь ловит одинаково в обеих играх. Свои у
    /// «Секундомера» только кнопка, гонг подраунда и тик — они в
    /// <c>StopwatchAudio</c>.
    /// </summary>
    /// <remarks>
    /// <b>Вздох и смех — два слота, а не один файл.</b> Публика ахает сразу,
    /// а смеётся спустя мгновение, и пауза между ними подбирается на слух.
    /// Склеенным клипом её уже не подвинуть, поэтому это два источника
    /// и две строки в манифесте.
    /// </remarks>
    public sealed class CircusAudio : MonoBehaviour
    {
        [SerializeField] private MinigameAudioPlayer audioPlayer;

        [Tooltip("Клетки арены. Заполняет билдер")]
        [SerializeField] private CageStation[] cages;

        [SerializeField] private PitBear bear;

        [SerializeField] private MinigameControllerBase controller;

        /// <summary>Через сколько после вздоха публика начинает смеяться, с.</summary>
        private const float LaughDelay = 0.85f;

        /// <summary>Через сколько после фанфар вступают аплодисменты, с.</summary>
        private const float ApplauseDelay = 1.1f;

        /// <summary>Как часто медведь бьёт лапой по решётке, пока дразнит, с.</summary>
        private const float PawPeriod = 1.6f;

        private const string WinchId = "cage_winch";
        private const string HatchId = "hatch_open";
        private const string PawId = "bear_paw";
        private const string GaspId = "crowd_gasp";
        private const string LaughId = "crowd_laugh";
        private const string AmbienceId = "crowd_ambience";
        private const string FanfareId = "fanfare";
        private const string ApplauseId = "applause";

        private bool[] wasDescending;
        private float pawTimer;

        private void Awake()
        {
            wasDescending = new bool[CageCount];
        }

        private void OnEnable()
        {
            for (int i = 0; i < CageCount; i++)
            {
                if (cages[i] != null)
                {
                    cages[i].DoorsOpened += OnDoorsOpened;
                }
            }

            if (bear != null)
            {
                bear.Caught += OnCaught;
            }

            if (controller != null)
            {
                controller.ResultsReported += OnResults;
            }

            // Шум шатра идёт всегда: спека 8.5 включает его с третьего
            // подраунда, но громкостью — гул зала не появляется из тишины.
            if (audioPlayer != null)
            {
                audioPlayer.StartLoop(AmbienceId);
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < CageCount; i++)
            {
                if (cages[i] != null)
                {
                    cages[i].DoorsOpened -= OnDoorsOpened;
                }
            }

            if (bear != null)
            {
                bear.Caught -= OnCaught;
            }

            if (controller != null)
            {
                controller.ResultsReported -= OnResults;
            }

            if (audioPlayer != null)
            {
                audioPlayer.StopLoop(AmbienceId);
            }
        }

        private int CageCount => cages == null ? 0 : cages.Length;

        private void Update()
        {
            TickWinch();
            TickPaw(Time.deltaTime);
        }

        /// <summary>
        /// Лебёдка звучит, пока клетка едет. Читаем движение платформы,
        /// а не событие: события о начале спуска игра не поднимает, а
        /// платформа синхронизирована — значит скрип начнётся у всех разом.
        /// </summary>
        private void TickWinch()
        {
            if (audioPlayer == null || wasDescending == null)
            {
                return;
            }

            for (int i = 0; i < CageCount && i < wasDescending.Length; i++)
            {
                CageStation cage = cages[i];
                if (cage == null)
                {
                    continue;
                }

                bool moving = cage.Descending;
                if (moving == wasDescending[i])
                {
                    continue;
                }

                wasDescending[i] = moving;
                if (moving)
                {
                    audioPlayer.PlayAt(WinchId, cage.transform.position);
                }
            }
        }

        private void TickPaw(float deltaTime)
        {
            if (audioPlayer == null || bear == null || bear.State != PitBear.BearState.Taunt)
            {
                pawTimer = 0f;
                return;
            }

            pawTimer -= deltaTime;
            if (pawTimer > 0f)
            {
                return;
            }

            pawTimer = PawPeriod;
            audioPlayer.PlayAt(PawId, bear.transform.position);
        }

        private void OnDoorsOpened(CageStation cage)
        {
            if (audioPlayer != null && cage != null)
            {
                audioPlayer.PlayAt(HatchId, cage.transform.position);
            }
        }

        /// <summary>
        /// Поимка: сначала вздох, следом смех. Оба слышны всем и не привязаны
        /// к точке — реагирует зал, а не место в яме.
        /// </summary>
        private void OnCaught(PlayerController player, Vector3 hitPoint)
        {
            if (audioPlayer == null)
            {
                return;
            }

            audioPlayer.Play(GaspId);
            CancelInvoke(nameof(PlayLaugh));
            Invoke(nameof(PlayLaugh), LaughDelay);
        }

        private void PlayLaugh()
        {
            if (audioPlayer != null)
            {
                audioPlayer.Play(LaughId);
            }
        }

        private void OnResults(MinigameResults results)
        {
            if (audioPlayer == null)
            {
                return;
            }

            audioPlayer.Play(FanfareId);
            CancelInvoke(nameof(PlayApplause));
            Invoke(nameof(PlayApplause), ApplauseDelay);
        }

        private void PlayApplause()
        {
            if (audioPlayer != null)
            {
                audioPlayer.Play(ApplauseId);
            }
        }
    }
}
