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
        /// <summary>
        /// Бонус за одиночество начислен. Локальное событие: поднимается
        /// у сервера в момент начисления и у клиента в момент, когда
        /// выросшее число приехало в составе. Своего пакета не имеет.
        /// </summary>
        public event System.Action LonelyBonusAwarded;

        internal const byte StageTyping = 1;
        internal const byte StageReveal = 2;
        internal const byte StageChoice = 3;
        internal const byte StageTension = 4;
        internal const byte StageHatch = 5;

        /// <summary>Ниже этой высоты игрок считается провалившимся в яму.</summary>
        private const float FallenBelowHeight = -1f;
        internal const byte StageRespawn = 6;

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
        [Tooltip("Точка, с которой Ведущий смотрит на зал, пока Ученики выбирают платформу")]
        [SerializeField] private Transform hallCameraRig;

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

        /// <summary>За эту машину играет болванка автопрогона (аргумент <c>--bot</c>).</summary>
        private bool autoplay;

        /// <summary>
        /// Когда болванка-Ведущий нажмёт «Готово». Ноль — сейчас ведёт не она.
        /// Момент по общим часам, а не таймер: печать закрывается по серверным
        /// часам, и локальный отсчёт разъехался бы с ней на пинг.
        /// </summary>
        private double autoplayDoneAt;

        /// <summary>Сетевая половина. Пусто — сцену открыли напрямую, играем локально.</summary>
        private ExamNetwork network;

        /// <summary>Вопрос, присланный Ведущим-клиентом и ждущий конца фазы печати.</summary>
        private string pendingQuestion;
        private string pendingOptionA;
        private string pendingOptionB;
        private ExamSide pendingCorrect;
        private bool pendingReady;

        /// <summary>Свой вопрос уже отправлен серверу в этом круге — второй раз не шлём.</summary>
        private bool questionSent;

        /// <summary>
        /// Вопрос этого круга уже снят с Ведущего. Второй сбор не просто лишний:
        /// «Готово» закрывает стадию досрочно, а закрытие стадии тут же зовёт
        /// сбор ещё раз — и у Ведущего-клиента отложенный вопрос к этому моменту
        /// уже применён и погашен, так что второй проход объявлял бы вопрос
        /// несостоявшимся сразу после того, как он состоялся.
        /// </summary>
        private bool questionCollected;

        /// <summary>Сколько строк состава разобрано из приехавшего списка.</summary>
        private int networkEntryCursor;

        private double matchStartedAt;
        private bool matchOver;

        /// <summary>Сколько участников знает контроллер. Нужно сетевой половине.</summary>
        public int ContestantCount => entries.Count;

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

        protected override void Awake()
        {
            base.Awake();
            network = GetComponent<ExamNetwork>();
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

            // Болванка автопрогона живёт на КАЖДОЙ машине: персонаж у клиента
            // свой, и водить его серверу нечем. Поэтому вешается до проверки
            // авторитета, а манекены одиночного прогона — после, они бывают
            // только там, где сети нет вовсе.
            bots.Clear();
            AttachAutoplay();

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

            matchOver = false;

            Debug.Log($"📚 [Экзамен] матч на {Players.Count} игроков: {match.TotalQuestions} вопросов", this);
        }

        /// <summary>
        /// Первый вопрос начинается вместе с раундом, а не на готовности состава.
        ///
        /// Раньше он стартовал прямо из <see cref="OnPlayersReady"/> — то есть
        /// поверх обучалки и при ещё выключенном управлении. Первому Ведущему
        /// доставалось не тридцать секунд на печать, а сколько останется после
        /// заставки, и панель открывалась ему под ней. Хуже того, панель
        /// запоминала «ввод был выключен» и после закрытия отбирала управление
        /// до конца матча.
        /// </summary>
        protected override void OnRoundStarted()
        {
            if (!HasAuthority || matchOver)
            {
                return;
            }

            matchStartedAt = NetworkClock.Now;
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

            // Порядок Ведущих в лог: на восьмерых проверяется, что каждый
            // отведёт ровно один раз, а по логу это единственный способ
            // сверить назначенное с состоявшимся.
            var order = new System.Text.StringBuilder(64);
            for (int i = 0; i < rotation.Count; i++)
            {
                if (i > 0) order.Append(" → ");
                order.Append("id=").Append(rotation[i]);
            }

            Debug.Log($"📚 [Экзамен] порядок Ведущих: {order}", this);
        }

        /// <summary>
        /// Повесить болванки на манекенов. Только вне сети: в сетевой сессии
        /// манекенов не бывает, а болванка, доживи она туда, стала бы играть
        /// за живого человека.
        /// </summary>
        private void AttachBots()
        {
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

        /// <summary>
        /// Повесить болванку автопрогона на СВОЙ аватар и отдать ей ввод.
        ///
        /// Нужна затем, что в «Экзамене» ответ выбирается ногами: неподвижные
        /// клиенты соберутся в зоне возврата, все восемь окажутся вне платформ,
        /// и ни раскол, ни цена по третям, ни бонус за одиночество не
        /// проверятся вовсе — стенд отчитается зелёным, ничего не проверив.
        ///
        /// Только по аргументу <c>--bot</c> и только в сетевой сессии:
        /// в одиночном прогоне ходят манекены, у них своя ветка.
        /// </summary>
        private void AttachAutoplay()
        {
            autoplay = false;
            autoplayDoneAt = 0d;

            if (!LaunchArguments.BotEnabled || !WorldAuthority.IsNetworkSession)
            {
                return;
            }

            PlayerController avatar = SessionScoreboard.Current?.LocalPlayer?.Avatar;
            if (avatar == null)
            {
                Debug.LogWarning($"{name}: 🤖 автопрогон: своего персонажа нет — водить некого", this);
                return;
            }

            if (!avatar.TryGetComponent(out PlayerInputReader reader))
            {
                Debug.LogWarning($"{name}: 🤖 автопрогон: у персонажа нет ридера ввода", this);
                return;
            }

            reader.EngageAutopilot();

            if (!avatar.TryGetComponent(out ExamDebugBot bot))
            {
                bot = avatar.gameObject.AddComponent<ExamDebugBot>();
            }

            bots.Add(bot);
            autoplay = true;
            Debug.Log($"{name}: 🤖 автопрогон: за '{avatar.name}' играет болванка", this);
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

            // Прежний Ведущий обязан вернуться в класс ДО того, как за кафедру
            // встанет следующий. Иначе он остаётся заблокированным на возвышении
            // и выпадает из игры навсегда: роль, которую игра навесила, она же
            // и снимает (MinigameTemplate_HOWTO).
            ReleasePreviousHost();

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

            pendingReady = false;
            questionSent = false;
            questionCollected = false;

            for (int i = 0; i < entries.Count; i++)
            {
                ExamEntry e = entries[i];
                e.Side = ExamSide.None;
                entries[i] = e;
            }

            PublishMatch();
            PublishEntries();

            // Строка на входе в вопрос, а не только на подсчёте очков:
            // несостоявшийся вопрос до подсчёта не доходит, и без неё
            // ротацию по логу не сверить — часть ходов просто не видна.
            Debug.Log($"📚 [Экзамен] начат вопрос {match.QuestionNumber}/{match.TotalQuestions} " +
                      $"цена {match.QuestionValue} ведёт id={match.HostPlayerId} — печать", this);

            stageState.BeginSubround(match.QuestionNumber, StageTyping, config.TypingSeconds);
        }

        /// <summary>Разослать положение матча. Пусто вне сети — играем локально.</summary>
        private void PublishMatch() => network?.PublishMatch(match.QuestionNumber, match.TotalQuestions,
            match.QuestionValue, match.HostPlayerId, match.QuestionPosted);

        /// <summary>
        /// Разослать состав. Сторона в строках публикуется только после
        /// фиксации: до неё клиенту знать, кто куда встал, неоткуда — кроме
        /// собственных глаз, и это правильно.
        /// </summary>
        private void PublishEntries()
        {
            if (network == null)
            {
                return;
            }

            network.BeginPublishEntries();
            for (int i = 0; i < entries.Count; i++)
            {
                ExamEntry e = entries[i];
                network.PublishEntry(e.PlayerId, e.Score, e.LonelyHits, e.CorrectAnswers,
                    e.LastScoredAt, e.Side, e.Present);
            }
        }

        /// <summary>
        /// Вернуть отведшего в класс: снять блокировку движения и поставить
        /// в зону возврата. Спека 4.6: смена Ведущего мгновенная, предыдущий
        /// уходит вниз без прохода через зал.
        /// </summary>
        private void ReleasePreviousHost()
        {
            if (match.HostPlayerId < 0)
            {
                return;
            }

            SessionPlayer previous = FindPlayer(match.HostPlayerId);
            if (previous?.Avatar == null)
            {
                return;
            }

            previous.Avatar.MovementLocked = false;
            ReleaseEmotesWhileLocked(previous.Avatar);
            autoplayDoneAt = 0d;

            if (HasAuthority && returnZone != null)
            {
                // Разводим по ширине зоны, чтобы бывшие Ведущие не слипались
                // в одной точке и не расталкивали друг друга физикой.
                float offset = (match.QuestionNumber % 5 - 2) * 1.6f;
                Vector3 spot = returnZone.position + returnZone.right * offset;
                previous.Avatar.RequestTeleport(spot, Quaternion.identity);
            }

            if (previous == SessionScoreboard.Current?.LocalPlayer)
            {
                questionInput?.Close();
                RestoreHostCamera();
            }
        }

        /// <summary>
        /// Поднять провалившихся из ямы в зону возврата (спека 5.8).
        ///
        /// Другого выхода из ямы нет: <c>KillZone</c> лежит ниже её дна и
        /// упавшего не ловит, а лестниц и лифтов в игре нет по дизайну.
        /// До 26.08 это не всплывало вовсе — пол зала шёл сплошной плитой
        /// под платформами, и провалиться было физически некуда.
        /// </summary>
        private void ReturnFallenPlayers()
        {
            if (!HasAuthority || returnZone == null)
            {
                return;
            }

            for (int i = 0; i < Players.Count; i++)
            {
                PlayerController avatar = Players[i].Avatar;
                if (avatar == null || avatar.transform.position.y > FallenBelowHeight)
                {
                    continue;
                }

                // Разводим по ширине зоны той же арифметикой, что и бывших
                // Ведущих: иначе поднятые встают в одну точку и расталкивают
                // друг друга физикой.
                //
                // RequestTeleport по той же причине, что и у кафедры: упавший
                // клиент прямым TeleportTo не поднимался вовсе и досиживал
                // матч в яме, ведя оттуда свои вопросы (живой прогон 28.08).
                float offset = (i % 5 - 2) * 1.6f;
                avatar.RequestTeleport(returnZone.position + returnZone.right * offset, Quaternion.identity);
            }
        }

        /// <summary>
        /// Поставить персонажа за кафедру: полный рост, никакого танца.
        /// Вызывается ДО блокировки — под ней поза уже не меняется.
        /// </summary>
        private static void ResetPodiumPose(PlayerController avatar, bool allowEmotes)
        {
            avatar.ForceStand();

            if (avatar.TryGetComponent(out PlayerEmoteAbility emotes))
            {
                emotes.StopEmote();
                emotes.AllowedWhileLocked = allowEmotes;
            }
        }

        /// <summary>
        /// Вернуть насмешкам обычное правило. Обязательно: персонаж переезжает
        /// между сценами живым, и разрешение, уехавшее в хаб или в следующую
        /// мини-игру, дало бы пляшущую статую там, где её быть не должно.
        /// </summary>
        private static void ReleaseEmotesWhileLocked(PlayerController avatar)
        {
            if (avatar.TryGetComponent(out PlayerEmoteAbility emotes))
            {
                emotes.AllowedWhileLocked = false;
            }
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

                    // Ведущему камеру НЕ возвращаем: он остаётся за кафедрой
                    // до конца вопроса, а места для 3rd person там нет. Между
                    // кафедрой (z=11.16) и дальней стеной (z=12.96) 1.8 м при
                    // требуемых правилом камеры ~4.5 (igruha/CLAUDE.md, 2a):
                    // риг уезжал за стену, деокклюдер подтягивал камеру
                    // вплотную к спине, и кадр вставал на уровне пояса — ни
                    // Ведущего в рост, ни доски. Повернуть было некуда: сзади
                    // стена с доской, спереди невидимая стенка возвышения.
                    //
                    // Фиксированная камера кафедры держится весь его ход и
                    // возвращается там, где Ведущий с кафедры сходит, —
                    // в ReleasePreviousHost и OnRoundEnded.
                    if (!FindEntryPlayer(match.HostPlayerId).IsLocal)
                    {
                        RestoreHostCamera();
                    }

                    // Текст уходит клиентам ровно в этот момент и ни секундой
                    // раньше: в фазе печати его нет ни у кого, кроме Ведущего
                    // и сервера (спека 10.3).
                    if (HasAuthority)
                    {
                        PublishMatch();
                        if (match.QuestionPosted)
                        {
                            network?.AnnounceQuestion(questionText, optionAText, optionBText);
                        }
                        else
                        {
                            network?.AnnounceSkipped();
                        }
                    }

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

                        // Публикуем результат и объявляем, какая платформа
                        // раскрылась. Это И ЕСТЬ публикация ответа: отдельного
                        // пакета с верным вариантом не существует.
                        PublishEntries();
                        network?.AnnounceHatch(correctSide == ExamSide.A ? ExamSide.B : ExamSide.A);
                    }

                    if (match.QuestionPosted)
                    {
                        board?.HighlightCorrect(correctSide);
                    }
                    break;

                case StageChoice:
                    // Ведущий разворачивается на зал. Свой вопрос он уже
                    // прочитал на доске в фазе показа, а дальше начинается то,
                    // ради чего он его писал: кто куда побежит и кто провалится.
                    // Камера кафедры смотрит НА кафедру — с неё этого не видно
                    // вовсе, и вся развязка проходила бы мимо него.
                    ApplyHallCamera();

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
                    ReturnFallenPlayers();
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
            //
            // RequestTeleport, а НЕ TeleportTo. Персонаж висит на
            // ClientNetworkTransform (авторитет владельца), и прямой перенос
            // с сервера чужую копию не двигает: владелец перезапишет позицию
            // в тот же кадр. Живой прогон 28.08: Ведущим стал клиент, за
            // кафедрой не появился никто, панель печати не открылась, и оба
            // игрока отсидели все 30 секунд фазы, ожидая «фантомного» третьего.
            if (podiumStand != null && HasAuthority)
            {
                host.Player.Avatar.RequestTeleport(podiumStand.position, podiumStand.rotation);
            }

            // Ставим за кафедру «с нуля»: в полный рост и без танца. Под
            // блокировкой поза застывает как есть, а играющая эмоция кончается
            // только когда игрок пошёл — то есть под блокировкой никогда.
            // Присевший или танцевавший в момент телепорта так и вёл бы вопрос
            // сидя или приплясывая, и снять это ему было бы нечем.
            //
            // Насмешки при этом остаются: уйти Ведущий не может, но класс
            // подразнить — ровно то, ради чего он там стоит.
            ResetPodiumPose(host.Player.Avatar, allowEmotes: true);

            host.Player.Avatar.MovementLocked = true;

            if (!host.IsLocal)
            {
                return;
            }

            questionInput?.Open(config, presets, host.Player.Avatar);
            ApplyPodiumCamera();

            if (!autoplay || questionInput == null)
            {
                return;
            }

            // Болванке печатать нечем, но дальше она идёт человеческим путём:
            // заготовка в поля, отметка варианта, «Готово» → ServerRpc →
            // серверные проверки. Короткого пути в обход них нет намеренно —
            // именно этот путь живой человек ещё ни разу не проходил.
            questionInput.FillWithPreset(Random.value < 0.5f ? ExamSide.A : ExamSide.B);
            autoplayDoneAt = NetworkClock.Now + Random.Range(AutoplayTypeMinSeconds, AutoplayTypeMaxSeconds);
        }

        /// <summary>
        /// Снять то, что напечатал Ведущий. Единственная точка приёма —
        /// в фазе 3 сюда придёт <c>ServerRpc</c>, и проверки останутся теми же.
        /// </summary>
        private void CollectQuestionFromHost()
        {
            if (!HasAuthority || questionCollected)
            {
                return;
            }

            questionCollected = true;

            // Ведущий-клиент прислал вопрос заранее — он уже проверен
            // в ServerApplyQuestion, здесь его только применяем.
            if (pendingReady)
            {
                ApplyQuestion(pendingQuestion, pendingOptionA, pendingOptionB, pendingCorrect);
                pendingReady = false;
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
            if (stageState == null || stageState.Stage != StageTyping)
            {
                return;
            }

            if (HasAuthority)
            {
                CollectQuestionFromHost();
                stageState.EndStageNow();
                return;
            }

            // У клиента «Готово» — это просьба к серверу, а не решение.
            // Вопрос уходит вместе с ней: иначе сервер закрыл бы фазу раньше,
            // чем получил текст.
            if (questionInput != null && questionInput.HasQuestion)
            {
                questionSent = true;
                network?.SubmitQuestion(questionInput.Question, questionInput.OptionA,
                    questionInput.OptionB, questionInput.CorrectSide);
            }

            network?.RequestDone();
        }

        // ========== ПРИЁМ НА СЕРВЕРЕ ==========

        /// <summary>
        /// Вопрос пришёл от Ведущего-клиента. Единственная точка приёма:
        /// сервер не верит ничему и проверяет всё сам.
        /// </summary>
        public void ServerApplyQuestion(int senderId, string question, string optionA, string optionB,
            ExamSide correct)
        {
            if (!HasAuthority)
            {
                return;
            }

            // 1. Роль. Прислать вопрос может только тот, кто сейчас за кафедрой.
            if (senderId != match.HostPlayerId)
            {
                Debug.LogWarning($"{name}: вопрос от игрока {senderId}, а ведёт {match.HostPlayerId} — отказ", this);
                return;
            }

            // 2. Стадия. Принимаем только в фазе печати. Это не формальность:
            //    правило 5.5 требует, чтобы верный вариант был зафиксирован
            //    ДО того, как класс увидит вопрос. Приняв отметку в фазе выбора,
            //    мы дали бы Ведущему топить лично неприятных людей, и игра
            //    сломалась бы тихо — снаружи это выглядит как невезение.
            if (stageState == null || stageState.Stage != StageTyping)
            {
                Debug.LogWarning($"{name}: вопрос от игрока {senderId} пришёл вне фазы печати " +
                                 $"(стадия {stageState?.Stage}) — отказ", this);
                return;
            }

            // 3. Содержимое. Пустой вопрос — то же, что не напечатанный.
            if (string.IsNullOrWhiteSpace(question) ||
                string.IsNullOrWhiteSpace(optionA) ||
                string.IsNullOrWhiteSpace(optionB))
            {
                return;
            }

            // 4. Вариант. Ровно А или Б, либо «не отмечен».
            if (correct != ExamSide.A && correct != ExamSide.B && correct != ExamSide.None)
            {
                Debug.LogWarning($"{name}: от игрока {senderId} пришёл вариант {correct} — отказ", this);
                return;
            }

            // Длины режет сервер, а не доверяет клиенту: пакет мог прийти
            // и не от нашего интерфейса.
            pendingQuestion = Truncate(question, config.QuestionMaxLength);
            pendingOptionA = Truncate(optionA, config.OptionMaxLength);
            pendingOptionB = Truncate(optionB, config.OptionMaxLength);
            pendingCorrect = correct;
            pendingReady = true;
        }

        /// <summary>Ведущий просит закрыть фазу печати досрочно.</summary>
        public void ServerRequestDone(int senderId)
        {
            if (!HasAuthority || senderId != match.HostPlayerId)
            {
                return;
            }

            if (stageState != null && stageState.Stage == StageTyping)
            {
                CollectQuestionFromHost();
                stageState.EndStageNow();
            }
        }

        /// <summary>
        /// Игрок ушёл. Судьба вопроса зависит от того, успел ли Ведущий его
        /// опубликовать: до показа вопрос пропадает вместе с ним, после —
        /// доигрывается штатно, потому что ответ уже лежит на сервере
        /// и участие Ведущего больше не требуется (спека 10.4).
        /// </summary>
        public void ServerHandleDisconnect(int playerId)
        {
            if (!HasAuthority)
            {
                return;
            }

            int index = IndexOf(playerId);
            if (index >= 0)
            {
                ExamEntry e = entries[index];
                e.Present = false;
                e.Side = ExamSide.None;
                entries[index] = e;
            }

            RemovePlayer(playerId);
            PublishEntries();

            if (playerId != match.HostPlayerId)
            {
                return;
            }

            if (!match.QuestionPosted && stageState != null && stageState.Stage == StageTyping)
            {
                Debug.Log($"📚 [Экзамен] Ведущий {playerId} ушёл в фазе печати — вопрос пропущен", this);
                stageState.EndStageNow();
            }
            else if (match.QuestionPosted)
            {
                Debug.Log($"📚 [Экзамен] Ведущий {playerId} ушёл после показа — вопрос доигрывается", this);
            }
            else
            {
                // Третий случай, и он не редкий: NGO замечает пропажу клиента
                // не сразу, и к моменту колбэка фаза печати уже кончилась сама,
                // объявив вопрос несостоявшимся. Доигрывать тут нечего, и
                // говорить «доигрывается» — врать в единственном месте,
                // по которому потом разбирают прогон.
                Debug.Log($"📚 [Экзамен] Ведущий {playerId} ушёл; вопрос к этому моменту " +
                          "уже был объявлен несостоявшимся", this);
            }
        }

        // ========== ПРИЁМ НА КЛИЕНТЕ ==========

        /// <summary>Положение матча приехало от сервера.</summary>
        public void ApplyNetworkMatch(int questionNumber, int totalQuestions, int questionValue,
            int hostPlayerId, bool questionPosted)
        {
            if (HasAuthority)
            {
                return;
            }

            bool hostChanged = match.HostPlayerId != hostPlayerId;

            // Прежнего Ведущего надо отпустить и на клиенте, до перезаписи
            // состояния — ReleasePreviousHost читает СТАРЫЙ HostPlayerId.
            //
            // Блокировку движения вешает OpenTypingForHost на каждой машине,
            // а снимал её только сервер, у себя. У клиента, побывавшего за
            // кафедрой, свой персонаж оставался обездвиженным до конца матча:
            // человек досматривал игру стоя. На приёмке 24.08 это не всплыло —
            // проверяли состояние после матча, а в хабе блокировку снимает
            // HubBootstrap.RestoreLocalControl.
            if (hostChanged)
            {
                ReleasePreviousHost();
            }

            match.QuestionNumber = questionNumber;
            match.TotalQuestions = totalQuestions;
            match.QuestionValue = questionValue;
            match.HostPlayerId = hostPlayerId;
            match.QuestionPosted = questionPosted;

            // Панель ввода открывается у того, кто стал Ведущим, — на его
            // машине и только на ней.
            if (hostChanged)
            {
                OpenTypingForHost();
            }

            UpdateHud();
        }

        /// <summary>Текст вопроса приехал — значит наступила стадия показа.</summary>
        public void ApplyNetworkQuestion(string question, string optionA, string optionB)
        {
            if (HasAuthority)
            {
                return;
            }

            questionText = question;
            optionAText = optionA;
            optionBText = optionB;
            match.QuestionPosted = true;

            board?.ShowQuestion(match.QuestionNumber, match.TotalQuestions, match.QuestionValue,
                questionText, optionAText, optionBText);
        }

        /// <summary>Вопрос не состоялся.</summary>
        public void ApplyNetworkSkipped()
        {
            if (HasAuthority)
            {
                return;
            }

            match.QuestionPosted = false;
            board?.ShowSkipped(match.QuestionNumber, match.TotalQuestions);
        }

        /// <summary>
        /// Сервер объявил, какая платформа раскрывается. Верный вариант
        /// клиент выводит отсюда — отдельного пакета с ответом нет.
        /// </summary>
        public void ApplyNetworkHatch(ExamSide wrongSide)
        {
            if (HasAuthority)
            {
                return;
            }

            correctSide = wrongSide == ExamSide.A ? ExamSide.B : ExamSide.A;
            ExamAnswerPlatform wrong = wrongSide == ExamSide.A ? platformA : platformB;
            wrong?.OpenDoors(config.HatchOpenSeconds);
            board?.HighlightCorrect(correctSide);
        }

        /// <summary>Начало разбора приехавшего состава.</summary>
        public void ApplyNetworkEntriesBegin() => networkEntryCursor = 0;

        /// <summary>Строка участника приехала от сервера.</summary>
        public void ApplyNetworkEntry(int playerId, int score, int lonelyHits, int correctAnswers,
            double lastScoredAt, ExamSide side, bool present)
        {
            if (HasAuthority)
            {
                return;
            }

            int index = IndexOf(playerId);
            if (index < 0)
            {
                entries.Add(new ExamEntry { PlayerId = playerId });
                index = entries.Count - 1;
            }

            ExamEntry e = entries[index];

            // Бонус за одиночество приезжает клиенту тем же составом, что и
            // счёт: отдельного пакета под него нет и заводить его незачем.
            // Рост числа одиночных попаданий и есть событие «кто-то пошёл
            // против толпы и угадал» — на нём висит звук (подфаза 4.5).
            if (lonelyHits > e.LonelyHits)
            {
                LonelyBonusAwarded?.Invoke();
            }

            e.Score = score;
            e.LonelyHits = lonelyHits;
            e.CorrectAnswers = correctAnswers;
            e.LastScoredAt = lastScoredAt;
            e.Side = side;
            e.Present = present;
            entries[index] = e;

            networkEntryCursor++;
        }

        /// <summary>Состав разобран.</summary>
        public void ApplyNetworkEntriesEnd()
        {
            if (!HasAuthority)
            {
                UpdateHud();
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

            // Строка на вопрос: по ней очки пересчитываются вручную из одного
            // лога, без догадок о том, кто где стоял.
            Debug.Log($"📚 [Экзамен] вопрос {match.QuestionNumber}/{match.TotalQuestions} " +
                      $"цена {match.QuestionValue} ведёт id={match.HostPlayerId}: " +
                      $"А={onA} Б={onB} верно={correctSide} угадали={correctCount}", this);

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
                    LonelyBonusAwarded?.Invoke();
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

            Debug.Log($"📚 [Экзамен] Ведущему id={match.HostPlayerId} за раскол {reward} ОЭ " +
                      $"(разница {Mathf.Abs(onA - onB)}, учеников {students})", this);

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
                    ReleaseEmotesWhileLocked(Players[i].Avatar);
                }
            }

            platformA?.CloseDoors();
            platformB?.CloseDoors();

            LogFinalTable();

            // Строго последним: камера должна уехать в хаб на своём аватаре,
            // а не на риге кафедры — тот умрёт вместе со сценой.
            RestoreHostCamera();
            stageState?.StopSequence();
        }

        /// <summary>
        /// Выписать итоговую таблицу в лог — на <b>каждой</b> машине, а не
        /// только у сервера.
        ///
        /// Стенд из восьми процессов проверяется сличением восьми Player.log
        /// между собой: разъехавшийся счёт у хоста и клиента иначе не поймать
        /// вовсе — у каждого своя картинка, и обе выглядят правдоподобно.
        /// </summary>
        private void LogFinalTable()
        {
            var table = new System.Text.StringBuilder(256);
            table.Append("📊 [Экзамен] итог у ");
            table.Append(HasAuthority
                ? "хоста"
                : $"клиента id={SessionScoreboard.Current?.LocalPlayer?.Id}");
            table.Append(':');

            for (int i = 0; i < entries.Count; i++)
            {
                ExamEntry e = entries[i];
                table.Append(" | id=").Append(e.PlayerId)
                     .Append(" ОЭ=").Append(e.Score)
                     .Append(" один=").Append(e.LonelyHits)
                     .Append(" верных=").Append(e.CorrectAnswers);

                if (!e.Present)
                {
                    table.Append(" ВЫШЕЛ");
                }
            }

            Debug.Log(table.ToString(), this);
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

        /// <summary>
        /// Общий план зала для Ведущего: платформы с толпой, а за ними он сам
        /// на кафедре и доска с вопросом. Ракурс фиксированный, поэтому
        /// деокклюдеру нечего подтягивать — у кафедры на 3rd person места нет
        /// (igruha/CLAUDE.md, 2a).
        /// </summary>
        private void ApplyHallCamera()
        {
            if (cameraController == null || hallCameraRig == null ||
                !FindEntryPlayer(match.HostPlayerId).IsLocal)
            {
                return;
            }

            cameraController.Apply(CameraMode.Fixed, hallCameraRig);
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
            if (questionInput == null || !questionInput.IsOpen || stageState == null)
            {
                return;
            }

            questionInput.SetCountdown(stageState.StageRemaining);

            // Болванка «допечатала» — жмём «Готово» её руками.
            if (autoplayDoneAt > 0d && NetworkClock.Now >= autoplayDoneAt)
            {
                autoplayDoneAt = 0d;
                HandleDoneRequested();
                return;
            }

            // Ведущий-клиент отправляет вопрос сам, не дожидаясь кнопки:
            // сервер закроет фазу по своим часам, и не успевший пакет означал
            // бы несостоявшийся вопрос у человека, который всё напечатал.
            // Шлём заранее, один раз за круг.
            if (!HasAuthority && !questionSent &&
                stageState.Stage == StageTyping &&
                stageState.StageRemaining <= QuestionSendLeadSeconds &&
                questionInput.HasQuestion)
            {
                questionSent = true;
                network?.SubmitQuestion(questionInput.Question, questionInput.OptionA,
                    questionInput.OptionB, questionInput.CorrectSide);
            }
        }

        /// <summary>
        /// За сколько до конца фазы печати клиент отсылает свой вопрос.
        /// Полторы секунды — с запасом на пинг: пакет должен успеть дойти
        /// до того, как сервер закроет стадию.
        /// </summary>
        private const float QuestionSendLeadSeconds = 1.5f;

        /// <summary>
        /// Сколько болванка «печатает», прежде чем нажать «Готово». Не ноль
        /// потому, что мгновенное «Готово» закрывало бы фазу печати раньше,
        /// чем клиенты успевают применить смену Ведущего, — стенд проверял бы
        /// не игру, а гонку. Разброс — чтобы восемь процессов не жали кнопку
        /// в один и тот же кадр.
        /// </summary>
        private const float AutoplayTypeMinSeconds = 4f;
        private const float AutoplayTypeMaxSeconds = 9f;

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
                // «Мой» — значит принадлежит этой машине, и только. Раньше
                // сюда входило ещё и reader.enabled, и это ломало игру целиком:
                // панель Ведущего на время печати сама гасит ридер, чтобы WASD
                // не водил персонажа вместо набора текста, — а сервер после
                // этого переставал считать Ведущего своим и не забирал у него
                // напечатанное. Ни один вопрос, введённый руками, не мог
                // состояться в принципе. Не всплыло потому, что соло-прогон
                // ходил манекенами (у них заготовка), а сетевая приёмка
                // крафтила пакеты прямо в точку приёма, минуя панель.
                isLocal = reader.LocallyControlled;

                // Болванка — это манекен ОДИНОЧНОГО прогона. В сетевой сессии
                // манекенов не бывает: там у каждой копии есть владелец, а
                // «управляется не мной» — это про чужую машину, а не про бота.
                //
                // Без второй половины условия сервер считал болванкой любого
                // клиента и, если тот не прислал вопрос, подставлял за него
                // заготовку со случайным верным вариантом. Человек, который
                // ничего не напечатал, получал вопрос от своего имени и
                // награду за раскол по нему — вместо «вопрос не состоялся»,
                // как требует спека 5.4.
                isBot = !reader.LocallyControlled && !WorldAuthority.IsNetworkSession;
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
