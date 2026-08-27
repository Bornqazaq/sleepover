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

        /// <summary>
        /// Состав раунда получен — <see cref="StartMinigame"/> отработал.
        /// До этого применять сетевую фазу нельзя, см. <see cref="ApplyPhase"/>.
        /// </summary>
        private bool playersReady;

        /// <summary>Фаза, приехавшая из сети раньше состава. Ждёт <see cref="StartMinigame"/>.</summary>
        private MinigamePhase pendingPhase;
        private bool hasPendingPhase;

        public MinigameDefinition Definition => definition;
        public event Action<MinigameResults> ResultsReported;

        /// <summary>
        /// Мини-игра текущей сцены. Пусто — сцена без мини-игры, то есть хаб.
        /// По этому и различает свои две роли кнопка «Выход» на паузе: из
        /// раунда или из сессии.
        ///
        /// Через статическую ссылку, а не поле в инспекторе, намеренно: поле
        /// пришлось бы заполнять в каждой сцене, а забытая ссылка молчит —
        /// на этом уже терялось колесо эмоций в Duck Hunt.
        /// </summary>
        public static MinigameControllerBase Current { get; private set; }

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
            Current = this;

            if (roundTimer != null)
            {
                roundTimer.Finished += HandleTimerFinished;
            }

            if (hud != null)
            {
                hud.RestartRequested += HandleRestartRequested;
            }
        }

        protected virtual void OnDisable()
        {
            if (Current == this)
            {
                Current = null;
            }

            if (roundTimer != null)
            {
                roundTimer.Finished -= HandleTimerFinished;
            }

            if (hud != null)
            {
                hud.RestartRequested -= HandleRestartRequested;
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
            playersReady = true;
            OnPlayersReady();

            // Фазы объявляет авторитет. Клиент уже готов (ростер и роли есть),
            // но ждёт команды из сети, иначе его раунд пойдёт в своём времени.
            if (!HasAuthority)
            {
                ApplyPendingPhase();
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

        /// <summary>
        /// Идёт раунд (или обучалка перед ним) — из него есть куда выходить.
        /// </summary>
        public bool CanLeaveRound => phase == MinigamePhase.Round || phase == MinigamePhase.Tutorial;

        /// <summary>
        /// Локальный игрок выходит из раунда, оставаясь в катке. Дальше он
        /// смотрит за остальными и вместе со всеми уезжает в хаб.
        ///
        /// Само правило выхода — дело конкретной игры и решается на сервере:
        /// здесь только отправка намерения.
        /// </summary>
        public void LeaveRound()
        {
            if (!CanLeaveRound)
            {
                return;
            }

            if (bridge != null && bridge.IsNetworkSession)
            {
                bridge.RequestLeaveRound();
                return;
            }

            // Сцена открыта напрямую: сервера нет, решаем на месте.
            SessionPlayer local = SessionScoreboard.Current?.LocalPlayer;
            if (local != null)
            {
                ApplyLeaveRound(local.Id);
            }
        }

        /// <summary>Намерение доехало до сервера — разбираем правилами игры.</summary>
        public void ApplyLeaveRound(int playerId)
        {
            if (!HasAuthority || !CanLeaveRound)
            {
                return;
            }

            OnPlayerLeftRound(playerId);
        }

        /// <summary>
        /// Правило игры: что делать с тем, кто вышел из раунда сам. Зовётся
        /// только у авторитета. По умолчанию — ничего: играм без выбывания
        /// выход из раунда смысла не добавляет.
        /// </summary>
        protected virtual void OnPlayerLeftRound(int playerId) { }

        /// <summary>
        /// Эта машина досматривает матч со стороны: игрок подключился, когда
        /// раунд уже шёл, и тела у него нет. Правило игры решает, за кем
        /// смотреть; по умолчанию — ни за кем, камера остаётся как есть.
        ///
        /// Чисто клиентское представление: на состояние раунда зритель не
        /// влияет и в результатах не участвует.
        /// </summary>
        public virtual void BeginViewing() { }

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

        /// <summary>
        /// Применить фазу: у авторитета — из GoToPhase, у клиента — из сети.
        ///
        /// Фаза может приехать раньше состава, и это норма: клиент грузит сцену
        /// и ждёт, пока соберётся ростер, а сервер к этому времени уже объявил
        /// раунд. Применить её сейчас — значит войти в раунд с пустым списком
        /// участников, а пришедший следом <see cref="StartMinigame"/> выключит
        /// всем управление, и включать его будет уже некому: <c>ApplyPhase</c>
        /// на ту же фазу второй раз не сработает. Ровно так у клиентов и
        /// отнимался ввод целиком — замерено 25.08 на host + 3 client, в логе
        /// «раунд начат (клиент), участников 0».
        ///
        /// Поэтому придерживаем фазу до состава. Приехало несколько — держим
        /// последнюю: подключившемуся к середине важно текущее положение дел,
        /// а не путь, которым к нему пришли.
        /// </summary>
        public void ApplyPhase(MinigamePhase next)
        {
            if (!playersReady)
            {
                pendingPhase = next;
                hasPendingPhase = true;
                return;
            }

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

        /// <summary>Состав пришёл — применить фазу, которую держали до него.</summary>
        private void ApplyPendingPhase()
        {
            if (!hasPendingPhase)
            {
                return;
            }

            hasPendingPhase = false;
            Debug.Log($"🎬 {name}: состав собран ({playerList.Count}) — применяю отложенную фазу {pendingPhase}");
            ApplyPhase(pendingPhase);
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

            // Переигрывать вправе только тот, кто объявляет фазы: в сетевой
            // катке сцену перезагружает сервер, клиент за собой её утащить
            // не может.
            hud?.SetRestartAvailable(HasAuthority);
            hud?.ShowResults(finalResults, playerList);
        }

        /// <summary>
        /// Переиграть тот же раунд: перезагрузить свою же сцену. Заведомо грубо
        /// и намеренно — это отладочный путь на время разработки, а не рематч
        /// катки (тот живёт в EPIC 4 вместе с очками и тай-брейком).
        ///
        /// В сетевой катке сцену объявляет сервер через NGO, клиенты
        /// переезжают сами — тем же путём, что и возврат в хаб.
        /// </summary>
        private void HandleRestartRequested()
        {
            if (!HasAuthority)
            {
                return;
            }

            StopAllCoroutines();

            string sceneName = gameObject.scene.name;
            NetworkManager network = NetworkManager.Singleton;

            if (network == null || !network.IsListening)
            {
                SceneManager.LoadScene(sceneName);
                return;
            }

            if (!network.IsServer)
            {
                return;
            }

            if (!BuildSceneCatalog.TryResolvePath(sceneName, out string scenePath))
            {
                Debug.LogError($"{name}: сцены '{sceneName}' нет в Build Settings — переигрывать нечего", this);
                return;
            }

            SceneEventProgressStatus status = network.SceneManager.LoadScene(scenePath, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"{name}: NGO не смог перезапустить '{sceneName}': {status}", this);
            }
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
