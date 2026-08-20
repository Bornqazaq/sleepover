using Igruha.Core.Scenes;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.UI;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// База контроллера мини-игры: обучалка → раунд → таймер → авто-завершение →
    /// сбор результатов → отчёт (IMinigame). Конкретная игра переопределяет
    /// только хуки и CollectResults — весь общий цикл живёт здесь.
    ///
    /// Сеть: фазу, время раунда и итоговые места решает авторитет (сервер), а
    /// остальные машины их применяют. Если моста нет (сцена открыта напрямую),
    /// авторитет у локальной машины и цикл работает как раньше.
    /// </summary>
    public abstract class MinigameControllerBase : MonoBehaviour, IMinigame, IMinigameNetworkTarget
    {
        [SerializeField] private MinigameDefinition definition;
        [SerializeField] private RoundTimer roundTimer;
        [SerializeField] private TutorialScreen tutorialScreen;
        [SerializeField] private RoundHud hud;

        [Header("Возврат в хаб")]
        [Tooltip("Сколько секунд показывать места, прежде чем всех вернёт в хаб")]
        [SerializeField] private float resultsDisplaySeconds = 12f;
        [Tooltip("Имя сцены хаба в Build Settings")]
        [SerializeField] private string hubSceneName = "Hub";

        private readonly MinigameResults results = new MinigameResults();
        private readonly List<SessionPlayer> playerList = new List<SessionPlayer>(8);
        private IMinigameNetworkBridge bridge;
        private MinigamePhase phase = MinigamePhase.Idle;

        public MinigameDefinition Definition => definition;
        public event Action<MinigameResults> ResultsReported;

        public MinigamePhase Phase => phase;

        protected IReadOnlyList<SessionPlayer> Players => playerList;
        protected bool RoundActive => phase == MinigamePhase.Round;
        protected RoundTimer Timer => roundTimer;
        /// <summary>HUD раунда — играм со стартовым отсчётом и своими строками статуса.</summary>
        protected RoundHud Hud => hud;

        /// <summary>Сервер сетевой катки либо единственная машина локального теста.</summary>
        protected bool HasAuthority => bridge == null || bridge.HasAuthority;

        protected virtual void Awake()
        {
            bridge = GetComponent<IMinigameNetworkBridge>();
        }

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

            hud?.Bind(roundTimer);
            SetPlayersControlEnabled(false);
            OnPlayersReady();

            // Фазы объявляет авторитет. Клиент уже готов (ростер и роли есть),
            // но ждёт команды из сети, иначе его раунд пойдёт в своём времени.
            if (!HasAuthority)
            {
                return;
            }

            GoToPhase(tutorialScreen != null ? MinigamePhase.Tutorial : MinigamePhase.Round);
        }

        /// <summary>
        /// Участник вышел из матча: убрать его из состава раунда.
        ///
        /// Состав копируется на старте мини-игры и дальше живёт своей жизнью,
        /// поэтому чистки ростера сессии мало: оставшийся в списке беглец
        /// получит место в результатах, хотя его в матче уже нет. Зовёт правило
        /// конкретной игры — только у авторитета, он один знает про уход.
        /// </summary>
        protected bool RemovePlayer(int playerId)
        {
            for (int i = 0; i < playerList.Count; i++)
            {
                if (playerList[i].Id != playerId)
                {
                    continue;
                }

                playerList.RemoveAt(i);
                return true;
            }

            return false;
        }

        /// <summary>Завершение раунда: по таймеру или досрочно правилами игры.</summary>
        public void EndMinigame()
        {
            if (phase == MinigamePhase.Results || phase == MinigamePhase.Idle)
            {
                return;
            }

            GoToPhase(MinigamePhase.Results);
        }

        // ========== ФАЗЫ ==========

        private void GoToPhase(MinigamePhase next)
        {
            if (!HasAuthority || phase == next)
            {
                return;
            }

            ApplyPhase(next);
            bridge?.PublishPhase(next);
        }

        /// <summary>Применить фазу: у авторитета — из GoToPhase, у клиента — из сети.</summary>
        public void ApplyPhase(MinigamePhase next)
        {
            if (phase == next)
            {
                return;
            }

            phase = next;

            if (roundTimer != null)
            {
                roundTimer.DrivenExternally = !HasAuthority;
            }

            switch (next)
            {
                case MinigamePhase.Tutorial:
                    EnterTutorial();
                    break;
                case MinigamePhase.Round:
                    EnterRound();
                    break;
                case MinigamePhase.Results:
                    EnterResults();
                    break;
            }
        }

        private void EnterTutorial()
        {
            SetPlayersControlEnabled(false);
            tutorialScreen?.Show(definition, HandleTutorialClosed);
        }

        private void HandleTutorialClosed()
        {
            // У клиента заставка гаснет только визуально: раунд начнёт сервер.
            GoToPhase(MinigamePhase.Round);
        }

        private void EnterRound()
        {
            tutorialScreen?.Hide();
            SetPlayersControlEnabled(true);

            if (HasAuthority && roundTimer != null && definition != null)
            {
                roundTimer.StartTimer(definition.RoundDuration);
            }

            OnRoundStarted();
        }

        private void EnterResults()
        {
            roundTimer?.StopTimer();
            SetPlayersControlEnabled(false);
            OnRoundEnded();

            if (!HasAuthority)
            {
                // Места посчитает сервер и пришлёт в ApplyResults.
                return;
            }

            results.Clear();
            CollectResults(results);

            SessionScoreboard.Current?.ReportResults(results);
            bridge?.PublishResults(results);
            ShowResults(results);

            StartCoroutine(ReturnToHubAfterResults());
        }

        /// <summary>
        /// Показали места — и обратно в хаб, откуда можно взять следующую игру.
        /// Возврат объявляет только сервер: смену сцены он рассылает через NGO,
        /// клиенты переезжают сами. Иначе катка упиралась бы в экран результатов
        /// и продолжить можно было бы только перезапуском всей игры.
        /// </summary>
        private IEnumerator ReturnToHubAfterResults()
        {
            yield return new WaitForSeconds(resultsDisplaySeconds);

            if (!HasAuthority)
            {
                yield break;
            }

            if (string.IsNullOrEmpty(hubSceneName))
            {
                Debug.LogError($"{name}: не задано имя сцены хаба — возвращаться некуда", this);
                yield break;
            }

            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsListening)
            {
                // Сцена открыта напрямую из редактора — грузим сами.
                SceneManager.LoadScene(hubSceneName);
                yield break;
            }

            if (!network.IsServer)
            {
                yield break;
            }

            if (!BuildSceneCatalog.TryResolvePath(hubSceneName, out string hubScenePath))
            {
                Debug.LogError($"{name}: сцены хаба '{hubSceneName}' нет в Build Settings — возвращать некуда", this);
                yield break;
            }

            SceneEventProgressStatus status = network.SceneManager.LoadScene(hubScenePath, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"{name}: NGO не смог вернуть всех в '{hubSceneName}': {status}", this);
                yield break;
            }

            Debug.Log($"🏠 HOST: раунд закрыт, возвращаю всех в '{hubSceneName}'");
        }

        private void HandleTimerFinished() => EndMinigame();

        // ========== ПРИЁМ СЕТЕВОГО СОСТОЯНИЯ ==========

        public bool TryGetRoundTime(out float remaining, out float duration)
        {
            if (roundTimer == null)
            {
                remaining = 0f;
                duration = 0f;
                return false;
            }

            remaining = roundTimer.Remaining;
            duration = roundTimer.Duration;
            return true;
        }

        public void ApplyRoundTime(float remaining, float duration)
        {
            if (roundTimer == null || HasAuthority)
            {
                return;
            }

            roundTimer.SyncFromNetwork(remaining, duration);
        }

        public void ApplyResults(MinigameResults networkResults)
        {
            if (HasAuthority)
            {
                return;
            }

            results.Clear();
            IReadOnlyList<MinigameResults.PlayerResult> entries = networkResults.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                results.Add(entries[i].PlayerId, entries[i].Place);
            }

            ShowResults(results);
        }

        private void ShowResults(MinigameResults finalResults)
        {
            ResultsReported?.Invoke(finalResults);
            hud?.ShowResults(finalResults, playerList);
        }

        private void SetPlayersControlEnabled(bool enabled)
        {
            for (int i = 0; i < playerList.Count; i++)
            {
                PlayerController avatar = playerList[i].Avatar;
                if (avatar == null || !avatar.TryGetComponent(out PlayerInputReader reader))
                {
                    continue;
                }

                // Манекены локального теста и чужие сетевые копии лишены
                // управления навсегда — их будить нельзя.
                if (!reader.LocallyControlled)
                {
                    continue;
                }

                reader.enabled = enabled;
            }
        }

        /// <summary>Игроки заспавнены, обучалка ещё висит.</summary>
        protected virtual void OnPlayersReady() { }

        /// <summary>Раунд пошёл (таймер запущен).</summary>
        protected virtual void OnRoundStarted() { }

        /// <summary>Раунд кончился (до сбора результатов).</summary>
        protected virtual void OnRoundEnded() { }

        /// <summary>
        /// Заполнить места игроков по правилам конкретной игры.
        /// Вызывается только у авторитета — клиенты получат готовые места.
        /// </summary>
        protected abstract void CollectResults(MinigameResults results);
    }
}
