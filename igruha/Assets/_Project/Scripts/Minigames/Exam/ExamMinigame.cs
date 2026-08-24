using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.Exam
{
    /// <summary>
    /// «Экзамен»: Ведущий печатает вопрос и тайно отмечает верный вариант,
    /// Ученики разбегаются по платформам А и Б, пол под неверной раскрывается.
    ///
    /// Асимметрия <b>ротационная</b>: за матч в кафедре побывает каждый,
    /// поэтому формально роль есть, а по итогу игра равная.
    ///
    /// <b>Написано network-ready.</b> Всё, что меняет исход, проходит через
    /// точки под <see cref="MinigameControllerBase.HasAuthority"/> — в фазе 3
    /// их останется только обернуть. Состояние собрано в
    /// <see cref="ExamMatchState"/> и списке <see cref="ExamEntry"/>, а не
    /// разбросано по полям: именно это уедет в NetworkVariable.
    /// </summary>
    public sealed class ExamMinigame : MinigameControllerBase
    {
        private const byte StageTyping = 1;
        private const byte StageReveal = 2;
        private const byte StageChoice = 3;
        private const byte StageTension = 4;
        private const byte StageHatch = 5;
        private const byte StageRespawn = 6;

        [Header("Конфиг и контент")]
        [SerializeField] private ExamConfig config;
        [SerializeField] private ExamQuestionPresets presets;

        [Header("Сцена")]
        [SerializeField] private MinigameStageState stageState;
        [SerializeField] private ExamAnswerPlatform platformA;
        [SerializeField] private ExamAnswerPlatform platformB;
        [SerializeField] private ExamBoard board;
        [SerializeField] private ExamQuestionInput questionInput;
        [SerializeField] private Transform podiumStand;
        [SerializeField] private Transform returnZone;
        [SerializeField] private MinigameCameraController cameraController;
        [SerializeField] private Transform podiumCameraRig;

        /// <summary>Состояние матча — то, что в фазе 3 станет NetworkVariable.</summary>
        private ExamMatchState match;

        private readonly List<ExamEntry> entries = new List<ExamEntry>(8);

        /// <summary>Порядок Ведущих. Считается один раз на старте и не меняется.</summary>
        private readonly List<int> rotation = new List<int>(8);
        private int rotationCursor;

        /// <summary>
        /// Верный вариант этого вопроса. <b>Живёт только здесь.</b> В сетевом
        /// состоянии его нет и не будет: клиенты узнают ответ из того, какая
        /// платформа раскрылась (спека 10.3).
        /// </summary>
        private ExamSide correctSide;

        private string questionText;
        private string optionAText;
        private string optionBText;

        private readonly List<ExamDebugBot> bots = new List<ExamDebugBot>(8);

        private double matchStartedAt;
        private bool matchOver;

        private void OnEnableSubscribe()
        {
            if (stageState == null)
            {
                return;
            }

            stageState.StageStarted += HandleStageStarted;
            stageState.StageElapsed += HandleStageElapsed;
        }

        private void OnDisableUnsubscribe()
        {
            if (stageState == null)
            {
                return;
            }

            stageState.StageStarted -= HandleStageStarted;
            stageState.StageElapsed -= HandleStageElapsed;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            OnEnableSubscribe();

            if (questionInput != null)
            {
                questionInput.DoneRequested += HandleDoneRequested;
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            OnDisableUnsubscribe();

            if (questionInput != null)
            {
                questionInput.DoneRequested -= HandleDoneRequested;
            }
        }

        // ========== СТАРТ МАТЧА ==========

        protected override void OnPlayersReady()
        {
            if (config == null)
            {
                Debug.LogError($"{name}: не назначен ExamConfig — играть нечем", this);
                return;
            }

            entries.Clear();
            for (int i = 0; i < Players.Count; i++)
            {
                entries.Add(new ExamEntry
                {
                    PlayerId = Players[i].Id,
                    Present = true,
                    Side = ExamSide.None
                });
            }

            if (!HasAuthority)
            {
                return;
            }

            BuildRotation();
            AttachBots();

            match = new ExamMatchState
            {
                QuestionNumber = 0,
                // Считается ОДИН раз: цена вопроса привязана к номеру, и если
                // число поплывёт при выходе игрока, треть матча переоценится.
                TotalQuestions = config.GetQuestionCount(Players.Count),
                HostPlayerId = -1
            };

            matchStartedAt = NetworkClock.Now;
            matchOver = false;

            Debug.Log($"📚 [Экзамен] матч на {Players.Count} игроков: {match.TotalQuestions} вопросов", this);
            BeginQuestion();
        }

        /// <summary>
        /// Порядок Ведущих: случайная перестановка состава. Рандом серверный —
        /// в фазе 3 он уже не должен никуда переезжать.
        /// </summary>
        private void BuildRotation()
        {
            rotation.Clear();
            for (int i = 0; i < Players.Count; i++)
            {
                rotation.Add(Players[i].Id);
            }

            for (int i = rotation.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (rotation[i], rotation[j]) = (rotation[j], rotation[i]);
            }

            rotationCursor = 0;
        }

        /// <summary>
        /// Повесить болванки на манекенов. Только вне сети: в сетевой сессии
        /// манекенов не бывает, а болванка, доживи она туда, стала бы играть
        /// за живого человека.
        /// </summary>
        private void AttachBots()
        {
            bots.Clear();

            if (WorldAuthority.IsNetworkSession)
            {
                return;
            }

            for (int i = 0; i < Players.Count; i++)
            {
                PlayerController avatar = Players[i].Avatar;
                if (avatar == null || !avatar.TryGetComponent(out PlayerInputReader reader) || reader.LocallyControlled)
                {
                    continue;
                }

                if (!avatar.TryGetComponent(out ExamDebugBot bot))
                {
                    bot = avatar.gameObject.AddComponent<ExamDebugBot>();
                }

                bots.Add(bot);
            }
        }

        // ========== ВОПРОС ==========

        private void BeginQuestion()
        {
            if (!HasAuthority || matchOver)
            {
                return;
            }

            if (match.QuestionNumber >= match.TotalQuestions)
            {
                FinishMatch("вопросы кончились");
                return;
            }

            if (NetworkClock.Now - matchStartedAt > config.MatchTimeoutSeconds)
            {
                FinishMatch("жёсткий таймаут матча");
                return;
            }

            int hostId = TakeNextHost();
            if (hostId < 0)
            {
                FinishMatch("некому вести");
                return;
            }

            match.QuestionNumber++;
            match.QuestionValue = config.GetQuestionValue(match.QuestionNumber, match.TotalQuestions);
            match.HostPlayerId = hostId;
            match.QuestionPosted = false;

            correctSide = ExamSide.None;
            questionText = string.Empty;
            optionAText = string.Empty;
            optionBText = string.Empty;

            for (int i = 0; i < entries.Count; i++)
            {
                ExamEntry e = entries[i];
                e.Side = ExamSide.None;
                entries[i] = e;
            }

            stageState.BeginSubround(match.QuestionNumber, StageTyping, config.TypingSeconds);
        }

        /// <summary>
        /// Следующий Ведущий по кругу. Если очередь дошла до игрока, которого
        /// уже нет, ход переходит следующему живому, а общее число вопросов
        /// НЕ меняется: кто-то поведёт дважды, и это честнее, чем укорачивать
        /// матч на лету.
        /// </summary>
        private int TakeNextHost()
        {
            for (int step = 0; step < rotation.Count; step++)
            {
                int candidate = rotation[(rotationCursor + step) % rotation.Count];
                if (IsPresent(candidate))
                {
                    rotationCursor = (rotationCursor + step + 1) % rotation.Count;
                    return candidate;
                }
            }

            return -1;
        }

        // ========== СТАДИИ ==========

        private void HandleStageStarted(byte stage)
        {
            switch (stage)
            {
                case StageTyping:
                    OpenTypingForHost();
                    board?.ShowWaiting(match.QuestionNumber, match.TotalQuestions, config.TypingSeconds);
                    break;

                case StageReveal:
                    questionInput?.Close();
                    RestoreHostCamera();
                    if (match.QuestionPosted)
                    {
                        board?.ShowQuestion(match.QuestionNumber, match.TotalQuestions,
                            match.QuestionValue, questionText, optionAText, optionBText);
                    }
                    else
                    {
                        board?.ShowSkipped(match.QuestionNumber, match.TotalQuestions);
                    }
                    break;

                case StageHatch:
                    // Позиции снимаются ровно здесь и больше не меняются:
                    // всё, что игрок делает после, на исход уже не влияет.
                    if (HasAuthority && match.QuestionPosted)
                    {
                        CaptureSides();
                        ScoreQuestion();
                        OpenWrongPlatform();
                    }

                    if (match.QuestionPosted)
                    {
                        board?.HighlightCorrect(correctSide);
                    }
                    break;

                case StageChoice:
                    for (int i = 0; i < bots.Count; i++)
                    {
                        bots[i].ChooseSide(platformA != null ? platformA.transform : null,
                            platformB != null ? platformB.transform : null);
                    }
                    break;

                case StageTension:
                    for (int i = 0; i < bots.Count; i++)
                    {
                        bots[i].MaybeChangeMind(platformA != null ? platformA.transform : null,
                            platformB != null ? platformB.transform : null);
                    }
                    break;

                case StageRespawn:
                    platformA?.CloseDoors();
                    platformB?.CloseDoors();
                    for (int i = 0; i < bots.Count; i++)
                    {
                        bots[i].Halt();
                        bots[i].ResetForQuestion();
                    }
                    break;
            }

            UpdateHud();
        }

        private void HandleStageElapsed(byte stage)
        {
            if (!HasAuthority || matchOver)
            {
                return;
            }

            switch (stage)
            {
                case StageTyping:
                    CollectQuestionFromHost();
                    stageState.EnterStage(StageReveal, config.RevealQuestionSeconds);
                    break;

                case StageReveal:
                    // Вопрос не состоялся — беготне взяться неоткуда,
                    // сразу к следующему.
                    stageState.EnterStage(match.QuestionPosted ? StageChoice : StageRespawn,
                        match.QuestionPosted ? config.ChoiceSeconds : config.RespawnSeconds);
                    break;

                case StageChoice:
                    stageState.EnterStage(StageTension, config.TensionSeconds);
                    break;

                case StageTension:
                    stageState.EnterStage(StageHatch, config.HatchStageSeconds);
                    break;

                case StageHatch:
                    stageState.EnterStage(StageRespawn, config.RespawnSeconds);
                    break;

                case StageRespawn:
                    if (CountPresent() < 2)
                    {
                        FinishMatch("осталось меньше двух игроков");
                        return;
                    }

                    BeginQuestion();
                    break;
            }
        }

        // ========== ВЕДУЩИЙ ==========

        private void OpenTypingForHost()
        {
            Contestant host = FindEntryPlayer(match.HostPlayerId);
            if (host.Player?.Avatar == null)
            {
                return;
            }

            // Ведущий переезжает за кафедру мгновенно: проход через зал
            // отнял бы половину фазы печати.
            if (podiumStand != null && HasAuthority)
            {
                host.Player.Avatar.TeleportTo(podiumStand.position, podiumStand.rotation);
            }

            host.Player.Avatar.MovementLocked = true;

            if (!host.IsLocal)
            {
                return;
            }

            questionInput?.Open(config, presets, host.Player.Avatar);
            ApplyPodiumCamera();
        }

        /// <summary>
        /// Снять то, что напечатал Ведущий. Единственная точка приёма —
        /// в фазе 3 сюда придёт <c>ServerRpc</c>, и проверки останутся теми же.
        /// </summary>
        private void CollectQuestionFromHost()
        {
            if (!HasAuthority)
            {
                return;
            }

            Contestant host = FindEntryPlayer(match.HostPlayerId);

            if (host.IsLocal && questionInput != null && questionInput.HasQuestion)
            {
                ApplyQuestion(questionInput.Question, questionInput.OptionA, questionInput.OptionB,
                    questionInput.CorrectSide);
                return;
            }

            // Болванка или человек, не успевший напечатать: у первой берём
            // заготовку, второму засчитываем несостоявшийся вопрос.
            if (host.IsBot && presets != null)
            {
                ExamQuestionPresets.Preset preset = presets.GetRandom();
                ApplyQuestion(preset.Question, preset.OptionA, preset.OptionB, ExamSide.None);
                return;
            }

            match.QuestionPosted = false;
            Debug.Log($"📚 [Экзамен] вопрос {match.QuestionNumber} не состоялся: Ведущий не успел", this);
        }

        private void ApplyQuestion(string question, string optionA, string optionB, ExamSide chosen)
        {
            questionText = Truncate(question, config.QuestionMaxLength);
            optionAText = Truncate(optionA, config.OptionMaxLength);
            optionBText = Truncate(optionB, config.OptionMaxLength);

            // Не отметил верный вариант — сервер выбирает случайно. Рандом
            // серверный, как и любой другой в проекте.
            correctSide = chosen != ExamSide.None
                ? chosen
                : (Random.value < 0.5f ? ExamSide.A : ExamSide.B);

            match.QuestionPosted = true;
        }

        private void HandleDoneRequested()
        {
            // Досрочное закрытие фазы печати. Решает сервер: EndStageNow сам
            // уходит по !HasAuthority, но лишний вызов ни к чему.
            if (HasAuthority && stageState != null && stageState.Stage == StageTyping)
            {
                CollectQuestionFromHost();
                stageState.EndStageNow();
            }
        }

        // ========== РЕЗУЛЬТАТ ВОПРОСА ==========

        private void CaptureSides()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                ExamEntry e = entries[i];
                e.Side = ExamSide.None;

                if (e.PlayerId == match.HostPlayerId || !e.Present)
                {
                    entries[i] = e;
                    continue;
                }

                SessionPlayer player = FindPlayer(e.PlayerId);
                if (player?.Avatar != null)
                {
                    Vector3 point = player.Avatar.transform.position;
                    if (platformA != null && platformA.Contains(point))
                    {
                        e.Side = ExamSide.A;
                    }
                    else if (platformB != null && platformB.Contains(point))
                    {
                        e.Side = ExamSide.B;
                    }
                }

                entries[i] = e;
            }
        }

        /// <summary>
        /// Начислить очки Экзамена. Единственная точка — в фазе 3 уйдёт
        /// за <c>IsServer</c> целиком.
        /// </summary>
        private void ScoreQuestion()
        {
            int onA = 0, onB = 0, correctCount = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].PlayerId == match.HostPlayerId)
                {
                    continue;
                }

                if (entries[i].Side == ExamSide.A) onA++;
                else if (entries[i].Side == ExamSide.B) onB++;

                if (entries[i].Side == correctSide) correctCount++;
            }

            double now = NetworkClock.Now;

            for (int i = 0; i < entries.Count; i++)
            {
                ExamEntry e = entries[i];
                if (e.PlayerId == match.HostPlayerId || e.Side != correctSide)
                {
                    entries[i] = e;
                    continue;
                }

                e.Score += match.QuestionValue;
                e.CorrectAnswers++;

                // Бонус за одиночество: награждает того, кто пошёл против толпы.
                if (correctCount == 1)
                {
                    e.Score += config.LonelyBonus;
                    e.LonelyHits++;
                }

                e.LastScoredAt = now;
                entries[i] = e;
            }

            ScoreHost(onA, onB, now);
        }

        /// <summary>
        /// Награда Ведущего за раскол. Решает проблему ротации: тот, кто ведёт
        /// последний вопрос за три очка, сам на него не отвечает — без
        /// компенсации он терял бы самый дорогой вопрос просто по очерёдности.
        /// </summary>
        private void ScoreHost(int onA, int onB, double now)
        {
            int index = IndexOf(match.HostPlayerId);
            if (index < 0)
            {
                return;
            }

            int students = CountPresent() - 1;
            int reward;

            if (onA + onB == 0)
            {
                // Никто не встал на платформы: раскол не состоялся не по вине
                // вопроса, и Ведущий получает полную цену (спека 6.3).
                reward = match.QuestionValue;
            }
            else if (students == 1)
            {
                // Лобби из двух: раскол невозможен, отвечает один человек.
                // Ведущий берёт цену только если Ученик ошибся (спека 6.4).
                bool studentCorrect = onA + onB > 0 &&
                    ((correctSide == ExamSide.A && onA == 1) || (correctSide == ExamSide.B && onB == 1));
                reward = studentCorrect ? 0 : match.QuestionValue;
            }
            else
            {
                reward = config.GetHostReward(Mathf.Abs(onA - onB), match.QuestionValue);
            }

            if (reward <= 0)
            {
                return;
            }

            ExamEntry host = entries[index];
            host.Score += reward;
            host.LastScoredAt = now;
            entries[index] = host;
        }

        private void OpenWrongPlatform()
        {
            ExamAnswerPlatform wrong = correctSide == ExamSide.A ? platformB : platformA;
            wrong?.OpenDoors(config.HatchOpenSeconds);
        }

        // ========== КОНЕЦ ==========

        private void FinishMatch(string reason)
        {
            if (matchOver)
            {
                return;
            }

            matchOver = true;
            Debug.Log($"📚 [Экзамен] матч окончен: {reason}", this);
            stageState?.StopSequence();
            EndMinigame();
        }

        protected override void CollectResults(MinigameResults results) => ExamRanking.Fill(entries, results);

        /// <summary>
        /// Снять с игроков всё, что навесила мини-игра. Персонаж переезжает
        /// между сценами живым, и незакрытая роль уедет в хаб вместе с ним:
        /// у «Ангелов» так уехала блокировка движения Водящего.
        ///
        /// Здесь к обычному списку добавляется <b>фокус ввода</b> — иначе
        /// бывший Ведущий будет в хабе печатать вместо ходьбы.
        /// </summary>
        protected override void OnRoundEnded()
        {
            questionInput?.Close();

            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].Avatar != null)
                {
                    Players[i].Avatar.MovementLocked = false;
                }
            }

            platformA?.CloseDoors();
            platformB?.CloseDoors();

            // Строго последним: камера должна уехать в хаб на своём аватаре,
            // а не на риге кафедры — тот умрёт вместе со сценой.
            RestoreHostCamera();
            stageState?.StopSequence();
        }

        // ========== КАМЕРА, HUD, МЕЛОЧИ ==========

        private void ApplyPodiumCamera()
        {
            if (cameraController == null || podiumCameraRig == null)
            {
                return;
            }

            cameraController.Apply(CameraMode.Fixed, podiumCameraRig);
        }

        private void RestoreHostCamera()
        {
            if (cameraController == null)
            {
                return;
            }

            SessionPlayer local = SessionScoreboard.Current?.LocalPlayer;
            if (local?.Avatar != null)
            {
                cameraController.Apply(CameraMode.ThirdPerson, local.Avatar.transform);
            }
        }

        private void UpdateHud()
        {
            if (Hud == null)
            {
                return;
            }

            SessionPlayer host = FindPlayer(match.HostPlayerId);
            int index = IndexOf(SessionScoreboard.Current?.LocalPlayer?.Id ?? -1);
            int myScore = index >= 0 ? entries[index].Score : 0;

            Hud.ShowStatus($"Вопрос {match.QuestionNumber}/{match.TotalQuestions}" +
                          $"   •   {match.QuestionValue} очк.   •   ведёт {host?.DisplayName ?? "—"}" +
                          $"   •   у тебя {myScore}");
        }

        private void Update()
        {
            if (questionInput != null && questionInput.IsOpen && stageState != null)
            {
                questionInput.SetCountdown(stageState.StageRemaining);
            }
        }

        private struct Contestant
        {
            public SessionPlayer Player;
            public bool IsLocal;
            public bool IsBot;
        }

        private Contestant FindEntryPlayer(int playerId)
        {
            SessionPlayer player = FindPlayer(playerId);
            bool isLocal = false;
            bool isBot = false;

            if (player?.Avatar != null && player.Avatar.TryGetComponent(out PlayerInputReader reader))
            {
                isLocal = reader.LocallyControlled && reader.enabled;
                isBot = !reader.LocallyControlled;
            }

            return new Contestant { Player = player, IsLocal = isLocal, IsBot = isBot };
        }

        private SessionPlayer FindPlayer(int playerId)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].Id == playerId)
                {
                    return Players[i];
                }
            }

            return null;
        }

        private int IndexOf(int playerId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].PlayerId == playerId)
                {
                    return i;
                }
            }

            return -1;
        }

        private bool IsPresent(int playerId)
        {
            int index = IndexOf(playerId);
            return index >= 0 && entries[index].Present && FindPlayer(playerId) != null;
        }

        private int CountPresent()
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Present && FindPlayer(entries[i].PlayerId) != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static string Truncate(string value, int limit)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            return trimmed.Length <= limit ? trimmed : trimmed.Substring(0, limit);
        }
    }
}
