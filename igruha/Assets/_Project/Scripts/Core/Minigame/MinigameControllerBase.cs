using System;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.UI;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// База контроллера мини-игры: обучалка → раунд → таймер → авто-завершение →
    /// сбор результатов → отчёт (IMinigame). Конкретная игра переопределяет
    /// только хуки и CollectResults — весь общий цикл живёт здесь.
    /// </summary>
    public abstract class MinigameControllerBase : MonoBehaviour, IMinigame
    {
        [SerializeField] private MinigameDefinition definition;
        [SerializeField] private RoundTimer roundTimer;
        [SerializeField] private TutorialScreen tutorialScreen;
        [SerializeField] private RoundHud hud;

        private readonly MinigameResults results = new MinigameResults();
        private readonly List<SessionPlayer> playerList = new List<SessionPlayer>(8);
        private bool roundActive;
        private bool finished;

        public MinigameDefinition Definition => definition;
        public event Action<MinigameResults> ResultsReported;

        protected IReadOnlyList<SessionPlayer> Players => playerList;
        protected bool RoundActive => roundActive;
        protected RoundTimer Timer => roundTimer;

        protected virtual void OnEnable()
        {
            if (roundTimer != null)
            {
                roundTimer.Finished += HandleTimerFinished;
            }
        }

        protected virtual void OnDisable()
        {
            if (roundTimer != null)
            {
                roundTimer.Finished -= HandleTimerFinished;
            }
        }

        public void StartMinigame(IReadOnlyList<SessionPlayer> players)
        {
            playerList.Clear();
            for (int i = 0; i < players.Count; i++)
            {
                playerList.Add(players[i]);
            }

            finished = false;
            roundActive = false;

            hud?.Bind(roundTimer);
            SetPlayersControlEnabled(false);
            OnPlayersReady();

            if (tutorialScreen != null)
            {
                tutorialScreen.Show(definition, BeginRound);
            }
            else
            {
                BeginRound();
            }
        }

        private void BeginRound()
        {
            roundActive = true;
            SetPlayersControlEnabled(true);
            if (roundTimer != null && definition != null)
            {
                roundTimer.StartTimer(definition.RoundDuration);
            }

            OnRoundStarted();
        }

        private void HandleTimerFinished() => EndMinigame();

        /// <summary>Завершение раунда: по таймеру или досрочно правилами игры.</summary>
        public void EndMinigame()
        {
            if (finished)
            {
                return;
            }

            finished = true;
            roundActive = false;
            roundTimer?.StopTimer();
            SetPlayersControlEnabled(false);
            OnRoundEnded();

            results.Clear();
            CollectResults(results);
            ResultsReported?.Invoke(results);
            hud?.ShowResults(results, playerList);
        }

        private void SetPlayersControlEnabled(bool enabled)
        {
            for (int i = 0; i < playerList.Count; i++)
            {
                PlayerController avatar = playerList[i].Avatar;
                if (avatar != null && avatar.TryGetComponent(out PlayerInputReader reader))
                {
                    // Манекенов не включаем: их ридер выключен спавнером навсегда.
                    if (enabled && i != 0)
                    {
                        continue;
                    }

                    reader.enabled = enabled;
                }
            }
        }

        /// <summary>Игроки заспавнены, обучалка ещё висит.</summary>
        protected virtual void OnPlayersReady() { }

        /// <summary>Раунд пошёл (таймер запущен).</summary>
        protected virtual void OnRoundStarted() { }

        /// <summary>Раунд кончился (до сбора результатов).</summary>
        protected virtual void OnRoundEnded() { }

        /// <summary>Заполнить места игроков по правилам конкретной игры.</summary>
        protected abstract void CollectResults(MinigameResults results);
    }
}
