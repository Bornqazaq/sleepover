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
    public abstract class MinigameControllerBase : MonoBehaviour, IMinigame, IMinigameNetworkTarget, ITutorialNetworkTarget
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
        [Tooltip("Сколько секунд показывать таблицу катки после итогов последней игры серии")]
        [SerializeField] private float finalStandingsSeconds = 10f;

        private readonly MinigameResults results = new MinigameResults();
        private readonly List<SessionPlayer> playerList = new List<SessionPlayer>(8);
        private readonly SessionStandings standings = new SessionStandings();
        private IMinigameNetworkBridge bridge;
        private ITutorialNetworkBridge tutorialBridge;
        private readonly TutorialReadiness tutorialReadiness = new TutorialReadiness();
        public IReadOnlyList<TutorialParticipant> TutorialParticipants => tutorialReadiness.Participants;
        private MinigamePhase phase = MinigamePhase.Idle;

        /// <summary>
        /// Сколько игроков начинало раунд. По этому числу считаются очки:
        /// ушедшие посреди матча не обесценивают победу оставшихся.
        /// </summary>
        private int startingPlayerCount;

        /// <summary>Этот раунд — последний в серии: после итогов будет таблица катки.</summary>
        private bool seriesFinal;

        /// <summary>
        /// Состав раунда получен — <see cref="StartMinigame"/> отработал.
        /// До этого применять сетевую фазу нельзя, см. <see cref="ApplyPhase"/>.
        /// </summary>
        private bool playersReady;
        private bool restartPending;
        private bool practiceSession = true;
        private Igruha.Core.CameraSystems.MinigameCameraController tutorialCamera;

        public void BindTutorialCamera(Igruha.Core.CameraSystems.MinigameCameraController camera) => tutorialCamera = camera;
        private static string preparedRoundScene;
        private static string preparedPracticeScene;
        private static readonly TutorialReadiness preparedPracticeReadiness = new TutorialReadiness();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPreparedRound()
        {
            preparedRoundScene = preparedPracticeScene = null;
            preparedPracticeReadiness.Reset();
        }

        public bool IsPractice => practiceSession;
        public bool GameplayActive => phase.IsGameplay();
        public bool AwaitingTutorialReady => phase == MinigamePhase.Tutorial ||
            phase == MinigamePhase.Practice || phase == MinigamePhase.PracticeComplete;

        /// <summary>Фаза, приехавшая из сети раньше состава. Ждёт <see cref="StartMinigame"/>.</summary>
        private MinigamePhase pendingPhase;
        private bool hasPendingPhase;

        public MinigameDefinition Definition => definition;
        public event Action<MinigameResults> ResultsReported;

        /// <summary>
        /// Показана таблица катки — серия доиграна. Для игр со своей панелью
        /// итогов (Дырка в стене), чтобы и они показали финал.
        /// </summary>
        public event Action<SessionStandings> FinalStandingsReported;

        /// <summary>Сколько секунд висит таблица катки до отъезда в хаб.</summary>
        public float FinalStandingsSeconds => finalStandingsSeconds;

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
        protected bool RoundActive => GameplayActive;
        protected RoundTimer Timer => roundTimer;
        /// <summary>Длительность таймера: игра с расписанием может вычислять её из своего конфига.</summary>
        protected virtual float RoundDuration => definition != null ? definition.RoundDuration : 0f;
        /// <summary>HUD раунда — играм со стартовым отсчётом и своими строками статуса.</summary>
        protected RoundHud Hud => hud;

        /// <summary>Сервер сетевой катки либо единственная машина локального теста.</summary>
        protected bool HasAuthority => bridge == null || bridge.HasAuthority;

        protected virtual void Awake()
        {
            bridge = GetComponent<IMinigameNetworkBridge>();
            tutorialBridge = GetComponent<ITutorialNetworkBridge>();
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

            practiceSession = !HasAuthority || preparedRoundScene != gameObject.scene.path;
            if (HasAuthority) preparedRoundScene = null;
            startingPlayerCount = playerList.Count;
            seriesFinal = false;

            hud?.Bind(roundTimer);
            ClearInheritedLocks();
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

            if (practiceSession) InitializeTutorialReadiness();
            GoToPhase(practiceSession ? MinigamePhase.Practice : MinigamePhase.Round);
            // При повторе неготовый участник мог отключиться во время загрузки.
            // Если все оставшиеся уже готовы, нового клика для старта не требуется.
            if (practiceSession && tutorialReadiness.AllReady && !restartPending)
                StartCoroutine(ReloadTutorialArena(true));
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
                RemoveTutorialParticipant(playerId);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Идёт раунд (или обучалка перед ним) — из него есть куда выходить.
        /// </summary>
        public bool CanLeaveRound => GameplayActive || AwaitingTutorialReady;

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

            if (AwaitingTutorialReady) RemoveTutorialParticipant(playerId);
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
            if (!HasAuthority || !GameplayActive)
            {
                return;
            }

            GoToPhase(IsPractice ? MinigamePhase.PracticeComplete : MinigamePhase.Results);
        }

        // ========== ФАЗЫ ==========

        private void GoToPhase(MinigamePhase next)
        {
            if (!HasAuthority || phase == next)
            {
                return;
            }

            ApplyPhase(next);
            bridge?.PublishPhase(phase);
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

            MinigamePhase previous = phase;
            phase = next;
            practiceSession = next != MinigamePhase.Round && next != MinigamePhase.Results;

            if (roundTimer != null)
            {
                roundTimer.DrivenExternally = !HasAuthority;
            }

            switch (next)
            {
                case MinigamePhase.Practice:
                    EnterRound();
                    if (phase == MinigamePhase.Practice) EnterTutorial();
                    break;
                case MinigamePhase.PracticeComplete:
                    roundTimer?.StopTimer();
                    if (previous == MinigamePhase.Practice) OnRoundEnded();
                    EnterTutorial();
                    tutorialScreen?.SetPracticeComplete();
                    break;
                case MinigamePhase.PreparingRound:
                    roundTimer?.StopTimer();
                    if (previous == MinigamePhase.Practice) OnRoundEnded();
                    SetPlayersControlEnabled(false);
                    tutorialScreen?.Hide();
                    break;
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
            if (tutorialScreen == null) tutorialScreen = gameObject.AddComponent<TutorialScreen>();
            tutorialScreen.Show(definition, ToggleTutorialReady, SetTutorialReading, RequestPracticeRestart);
            RefreshTutorialReadiness();
            if (LaunchArguments.BotEnabled && !LaunchArguments.TryGetValue("--tutorial-check", out _))
                StartCoroutine(ConfirmTutorialForBot());
        }

        private IEnumerator ConfirmTutorialForBot()
        {
            // Явный --bot — участник автоматического стенда, не таймаут для человека.
            yield return new WaitForSecondsRealtime(1f);
            if (AwaitingTutorialReady && !tutorialReadiness.IsReady(LocalTutorialPlayerId))
                ToggleTutorialReady();
        }

        private int LocalTutorialPlayerId => SessionScoreboard.Current?.LocalPlayer?.Id ??
            (bridge != null && bridge.IsNetworkSession ? -1 : playerList.Count > 0 ? playerList[0].Id : -1);

        private void InitializeTutorialReadiness()
        {
            tutorialReadiness.Reset();
            bool networked = bridge != null && bridge.IsNetworkSession;
            ICharacterSelection selection = CharacterSelection.Current;
            for (int i = 0; i < playerList.Count; i++)
            {
                SessionPlayer player = playerList[i];
                if (networked && selection != null && !selection.HasCharacter(player.Id)) continue;
                // В прямом запуске сцены остальные персонажи — манекены без собственного ввода.
                bool retainedReady = preparedPracticeScene == gameObject.scene.path && preparedPracticeReadiness.IsReady(player.Id);
                tutorialReadiness.Add(player.Id, retainedReady || (!networked && player.Id != LocalTutorialPlayerId));
            }
            preparedPracticeScene = null;
            preparedPracticeReadiness.Reset();
            tutorialBridge?.PublishTutorialReadiness(TutorialParticipants);
        }

        public void ToggleTutorialReady()
        {
            if (!AwaitingTutorialReady) return;
            bool ready = !tutorialReadiness.IsReady(LocalTutorialPlayerId);
            if (bridge != null && bridge.IsNetworkSession)
                tutorialBridge?.RequestTutorialReady(ready);
            else
                SetTutorialReady(LocalTutorialPlayerId, ready);
        }

        public void SetTutorialReady(int playerId, bool ready)
        {
            if (!HasAuthority || !AwaitingTutorialReady || !tutorialReadiness.SetReady(playerId, ready)) return;
            PublishTutorialReadiness();
        }

        public void RemoveTutorialParticipant(int playerId)
        {
            if (!HasAuthority || !AwaitingTutorialReady || !tutorialReadiness.Remove(playerId)) return;
            PublishTutorialReadiness();
        }

        public void ApplyTutorialReadiness(IReadOnlyList<TutorialParticipant> participants)
        {
            if (HasAuthority) return;
            tutorialReadiness.Apply(participants);
            RefreshTutorialReadiness();
        }

        private void PublishTutorialReadiness()
        {
            tutorialBridge?.PublishTutorialReadiness(TutorialParticipants);
            RefreshTutorialReadiness();
            if (tutorialReadiness.AllReady && !restartPending) StartCoroutine(ReloadTutorialArena(true));
        }

        private void RefreshTutorialReadiness()
        {
            if (AwaitingTutorialReady)
                tutorialScreen?.SetReadiness(playerList, TutorialParticipants, LocalTutorialPlayerId);
        }

        private void SetTutorialReading(bool reading)
        {
            // Правила — подсказка поверх практики, а не скрытая блокировка WASD.
            SetPlayersControlEnabled(phase == MinigamePhase.Practice);
            tutorialCamera?.SetTutorialLookSuspended(reading);
        }

        public void RequestPracticeRestart()
        {
            if (phase != MinigamePhase.PracticeComplete) return;
            if (bridge != null && bridge.IsNetworkSession) tutorialBridge?.RequestPracticeRestart();
            else RestartPractice(LocalTutorialPlayerId);
        }

        public void RestartPractice(int playerId)
        {
            if (!HasAuthority || phase != MinigamePhase.PracticeComplete || restartPending) return;
            for (int i = 0; i < TutorialParticipants.Count; i++)
            {
                if (TutorialParticipants[i].PlayerId != playerId) continue;
                StartCoroutine(ReloadTutorialArena(false));
                return;
            }
        }

        private IEnumerator ReloadTutorialArena(bool scoredRound)
        {
            restartPending = true;
            string path = gameObject.scene.path;
            GoToPhase(MinigamePhase.PreparingRound);
            // Завершить текущий сетевой кадр перед выгрузкой контроллера.
            yield return null;
            if (scoredRound) preparedRoundScene = path;
            else
            {
                preparedPracticeScene = path;
                preparedPracticeReadiness.Apply(TutorialParticipants);
            }
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsListening)
            {
                SceneManager.LoadScene(path, LoadSceneMode.Single);
                yield break;
            }
            SceneEventProgressStatus status = network.SceneManager.LoadScene(path, LoadSceneMode.Single);
            if (status == SceneEventProgressStatus.Started) yield break;
            ResetPreparedRound();
            restartPending = false;
            Debug.LogError($"{name}: не удалось подготовить раунд: {status}", this);
            // Сбой загрузки не выдаёт очки и не запускает повреждённую арену.
            GoToPhase(MinigamePhase.PracticeComplete);
        }

        private void EnterRound()
        {
            tutorialScreen?.Hide();
            tutorialCamera?.SetTutorialLookSuspended(false);
            SetPlayersControlEnabled(true);

            if (HasAuthority && roundTimer != null && definition != null)
            {
                roundTimer.StartTimer(RoundDuration);
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

            results.Reset();
            results.PlayerCount = startingPlayerCount;
            results.GameKey = definition != null && !string.IsNullOrEmpty(definition.SceneName)
                ? definition.SceneName
                : gameObject.scene.name;
            CollectResults(results);

            ISessionScoreboard session = SessionScoreboard.Current;
            int rosterCount = session != null ? session.Players.Count : playerList.Count;

            // В общий счёт катки идут только раунды серии («Полная игра»).
            // Одиночная игра с телевизора считает очки внутри себя и
            // показывает победителя, а сумму и журнал катки не трогает.
            results.CountsTowardSession = PartySeries.Active && session != null;
            if (results.CountsTowardSession)
            {
                session.ReportResults(results);
            }
            else
            {
                SessionScoring.AwardStandalone(results, rosterCount);
            }

            // Последняя игра серии: чемпионов фиксируем здесь, до показа
            // таблицы катки, и сообщаем клиентам вместе с местами — очереди
            // серии у них нет, сами они этого не узнают.
            seriesFinal = PartySeries.Active && !PartySeries.HasNext(rosterCount);
            if (seriesFinal)
            {
                PartySeries.Complete();
            }

            bridge?.PublishResults(results, seriesFinal);
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

            if (seriesFinal)
            {
                ShowFinalStandings();
                yield return new WaitForSeconds(finalStandingsSeconds);
            }

            if (PartySeries.Active)
            {
                var seriesLoader = gameObject.AddComponent<MinigameLoader>();
                if (PartySeries.Advance(seriesLoader)) yield break;
                Destroy(seriesLoader);
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

        public void ApplyResults(MinigameResults networkResults, bool isSeriesFinal)
        {
            if (HasAuthority)
            {
                return;
            }

            results.CopyFrom(networkResults);
            if (string.IsNullOrEmpty(results.GameKey))
            {
                // Ключ игры по сети не едет — клиент и так знает свою сцену.
                results.GameKey = definition != null && !string.IsNullOrEmpty(definition.SceneName)
                    ? definition.SceneName
                    : gameObject.scene.name;
            }

            seriesFinal = isSeriesFinal;
            ShowResults(results);

            if (seriesFinal)
            {
                StartCoroutine(ShowFinalStandingsAfterResults());
            }
        }

        /// <summary>
        /// Клиент показывает таблицу катки по тому же расписанию, что и
        /// сервер, — после итогов раунда. В хаб всех увезёт сервер.
        /// </summary>
        private IEnumerator ShowFinalStandingsAfterResults()
        {
            yield return new WaitForSeconds(resultsDisplaySeconds);
            ShowFinalStandings();
        }

        /// <summary>
        /// Таблица катки по суммам табло: места, очки, чемпион. К этому
        /// моменту ростер с очками за последний раунд у клиента уже есть —
        /// он ехал те же секунды, что висели итоги раунда.
        /// </summary>
        private void ShowFinalStandings()
        {
            ISessionScoreboard session = SessionScoreboard.Current;
            if (session == null)
            {
                return;
            }

            standings.Rebuild(session);
            FinalStandingsReported?.Invoke(standings);
            hud?.ShowFinalStandings(standings, session.Players, session.Champions);
            LogStandingsForComparison(session);
        }

        /// <summary>
        /// Одна строка итогов на машину — чтобы стенд мог сличить хост и
        /// клиентов дословно: «id:место:+очки/всего». Расхождение тут — и
        /// есть рассинхрон счёта, который иначе не поймать.
        /// </summary>
        private void LogResultsForComparison(MinigameResults finalResults)
        {
            var sb = new System.Text.StringBuilder(128);
            sb.Append("📊 итоги раунда ").Append(finalResults.GameKey).Append(" [").Append(finalResults.PlayerCount)
              .Append(finalResults.CountsTowardSession ? " на старте, серия]:" : " на старте, одиночная]:");
            IReadOnlyList<MinigameResults.PlayerResult> entries = finalResults.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                sb.Append(' ').Append(entries[i].PlayerId).Append(':').Append(entries[i].Place)
                  .Append(":+").Append(entries[i].Points).Append('/').Append(entries[i].Total);
            }

            Debug.Log(sb.ToString());
        }

        private void LogStandingsForComparison(ISessionScoreboard session)
        {
            var sb = new System.Text.StringBuilder(128);
            sb.Append("🏁 таблица катки [").Append(standings.RoundsPlayed).Append(" игр, лидеров ")
              .Append(standings.LeaderCount).Append("]:");
            IReadOnlyList<SessionStandings.Entry> entries = standings.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                sb.Append(' ').Append(entries[i].PlayerId).Append(':').Append(entries[i].Place)
                  .Append(':').Append(entries[i].Score).Append(session.IsChampion(entries[i].PlayerId) ? "👑" : string.Empty);
            }

            Debug.Log(sb.ToString());
        }

        private void ShowResults(MinigameResults finalResults)
        {
            LogResultsForComparison(finalResults);
            ResultsReported?.Invoke(finalResults);

            // Переигрывать вправе только тот, кто объявляет фазы: в сетевой
            // катке сцену перезагружает сервер, клиент за собой её утащить
            // не может.
            hud?.SetRestartAvailable(HasAuthority && !PartySeries.Active);
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
            if (!HasAuthority || PartySeries.Active || phase != MinigamePhase.Results || restartPending)
            {
                return;
            }

            string sceneName = gameObject.scene.name;
            NetworkManager network = NetworkManager.Singleton;

            if (network == null || !network.IsListening)
            {
                restartPending = true;
                StopAllCoroutines();
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
                return;
            }

            restartPending = true;
            StopAllCoroutines();
            Debug.Log($"{name}: повторный раунд '{sceneName}' запущен для всей сессии");
        }

        private void SetPlayersControlEnabled(bool enabled)
        {
            for (int i = 0; i < playerList.Count; i++)
            {
                ApplyControl(playerList[i].Avatar, enabled);
            }

            // Свой персонаж — отдельной строкой, даже если его нет в снимке
            // состава. Список снимается один раз на старте раунда, и человек,
            // чей аватар доехал позже, в него не попадал вовсе: раунд ему
            // управление не возвращал, а конец прошлого раунда его уже отнял
            // (EnterResults). В «Полной игре» это не лечилось ничем — серия
            // едет из мини-игры сразу в следующую, минуя хаб с его
            // страховкой, и человек доигрывал катку обездвиженным.
            ApplyControl(SessionScoreboard.Current?.LocalPlayer?.Avatar, enabled);
        }

        private static void ApplyControl(PlayerController avatar, bool enabled)
        {
            if (avatar == null || !avatar.TryGetComponent(out PlayerInputReader reader))
            {
                return;
            }

            // Манекены локального теста и чужие сетевые копии лишены
            // управления навсегда — их будить нельзя.
            if (!reader.LocallyControlled)
            {
                return;
            }

            reader.enabled = enabled;
        }

        /// <summary>
        /// Снять блокировки движения, доставшиеся от прошлой мини-игры.
        ///
        /// <c>MovementLocked</c> ставят роли — заморозка в «Ангелах», кресло в
        /// «Верю / не верю», клетка в «Секундомере», всплеск краски в
        /// «Заражении», — и снимает её тот, кто ставил. Персонаж при этом
        /// переезжает между сценами живым, вместе со всем своим состоянием.
        /// Одного несработавшего снятия хватало, чтобы человек приехал в
        /// следующую игру каменным: в хабе это чинит <c>HubBootstrap</c>, но
        /// «Полная игра» в хаб не заезжает вовсе.
        ///
        /// Зовётся до <see cref="OnPlayersReady"/>, то есть до того, как игра
        /// раздаст свои роли и поставит свои блокировки, — чужое снимаем, своё
        /// не трогаем.
        /// </summary>
        private void ClearInheritedLocks()
        {
            for (int i = 0; i < playerList.Count; i++)
            {
                PlayerController avatar = playerList[i].Avatar;
                if (avatar != null)
                {
                    avatar.MovementLocked = false;
                }
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
