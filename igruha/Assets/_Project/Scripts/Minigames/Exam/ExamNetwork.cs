using System;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.Exam
{
    /// <summary>
    /// Положение матча одной структурой: номер вопроса, его цена, кто ведёт.
    ///
    /// Вместе, а не по отдельности, потому что назначаются они одним решением
    /// сервера. Приехавшая раньше новая цена под старым номером вопроса
    /// означала бы очки, начисленные по чужой ставке.
    ///
    /// <b>Верного варианта здесь нет и быть не может.</b> Это и есть ответ:
    /// он живёт только в серверном поле контроллера и не реплицируется ни
    /// в каком виде до раскрытия створок (спека 10.3).
    /// </summary>
    public struct ExamMatchNetState : INetworkSerializable, IEquatable<ExamMatchNetState>
    {
        public int QuestionNumber;
        public int TotalQuestions;
        public byte QuestionValue;
        public int HostPlayerId;
        public bool QuestionPosted;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref QuestionNumber);
            serializer.SerializeValue(ref TotalQuestions);
            serializer.SerializeValue(ref QuestionValue);
            serializer.SerializeValue(ref HostPlayerId);
            serializer.SerializeValue(ref QuestionPosted);
        }

        public bool Equals(ExamMatchNetState other) =>
            QuestionNumber == other.QuestionNumber &&
            TotalQuestions == other.TotalQuestions &&
            QuestionValue == other.QuestionValue &&
            HostPlayerId == other.HostPlayerId &&
            QuestionPosted == other.QuestionPosted;
    }

    /// <summary>
    /// Стадия и момент её конца. Конец — момент на общих часах, а не остаток:
    /// остаток пришлось бы досылать каждый кадр, момент достаточно объявить
    /// один раз. Тот же приём, что у «Секундомера» и «Порядка банок».
    /// </summary>
    public struct ExamStageNetState : INetworkSerializable, IEquatable<ExamStageNetState>
    {
        public int Question;
        public byte Stage;
        public double EndTime;
        public float Duration;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Question);
            serializer.SerializeValue(ref Stage);
            serializer.SerializeValue(ref EndTime);
            serializer.SerializeValue(ref Duration);
        }

        public bool Equals(ExamStageNetState other) =>
            Question == other.Question &&
            Stage == other.Stage &&
            EndTime.Equals(other.EndTime) &&
            Mathf.Approximately(Duration, other.Duration);
    }

    /// <summary>
    /// Строка участника в сети. Сторона публикуется только после фиксации
    /// позиций — до неё знать, кто куда встал, клиенту неоткуда, кроме
    /// собственных глаз, и это правильно: подглядывать надо в зал, а не
    /// в трафик.
    /// </summary>
    public struct ExamEntryNetState : INetworkSerializable, IEquatable<ExamEntryNetState>
    {
        public int PlayerId;
        public int Score;
        public byte LonelyHits;
        public byte CorrectAnswers;
        public double LastScoredAt;
        public byte Side;
        public bool Present;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Score);
            serializer.SerializeValue(ref LonelyHits);
            serializer.SerializeValue(ref CorrectAnswers);
            serializer.SerializeValue(ref LastScoredAt);
            serializer.SerializeValue(ref Side);
            serializer.SerializeValue(ref Present);
        }

        public bool Equals(ExamEntryNetState other) =>
            PlayerId == other.PlayerId &&
            Score == other.Score &&
            LonelyHits == other.LonelyHits &&
            CorrectAnswers == other.CorrectAnswers &&
            LastScoredAt.Equals(other.LastScoredAt) &&
            Side == other.Side &&
            Present == other.Present;
    }

    /// <summary>
    /// Сетевая половина «Экзамена»: вешается на тот же объект, что и
    /// <see cref="ExamMinigame"/>. Правила остаются обычным MonoBehaviour
    /// и работают без этого компонента, когда сцену открывают напрямую, —
    /// образец взят у <c>CansOrderNetwork</c>.
    ///
    /// Фазу, время раунда и итоговые места везёт
    /// <c>NetworkMinigameBridge</c> рядом на том же объекте.
    ///
    /// <b>Чего здесь нет и не будет:</b>
    /// <list type="bullet">
    /// <item><b>верного варианта</b> — это ответ. Клиенты узнают его из того,
    /// какая платформа раскрылась, отдельного пакета «правильный — А»
    /// не существует;</item>
    /// <item><b>текста вопроса в NetworkVariable</b> — переменная
    /// синхронизируется при подключении, и её содержимое утекло бы позднему
    /// подключившемуся ещё в фазе печати. Текст едет <c>Rpc</c> ровно
    /// в момент показа;</item>
    /// <item>перемещений Учеников — их везёт обычный <c>NetworkTransform</c>.</item>
    /// </list>
    /// </summary>
    public sealed class ExamNetwork : NetworkBehaviour
    {
        private readonly NetworkVariable<ExamMatchNetState> match =
            new NetworkVariable<ExamMatchNetState>();

        private readonly NetworkVariable<ExamStageNetState> stage =
            new NetworkVariable<ExamStageNetState>();

        private readonly NetworkList<ExamEntryNetState> entries = new NetworkList<ExamEntryNetState>();

        private ExamMinigame game;
        private MinigameStageState stageState;

        private bool matchDirty;
        private bool stageDirty;
        private bool entriesDirty;

        /// <summary>
        /// Состав, под который состояние уже разобрано. Порядок спавна этого
        /// объекта и старта мини-игры ничем не связан: состояние вполне может
        /// приехать раньше, чем контроллер соберёт участников.
        /// </summary>
        private int appliedContestants = -1;

        /// <summary>Идёт сетевая катка и эта половина живая.</summary>
        public bool IsActive => IsSpawned;

        private void Awake()
        {
            game = GetComponent<ExamMinigame>();
            if (game == null)
            {
                Debug.LogError($"{name}: ExamNetwork не нашёл ExamMinigame на своём объекте", this);
            }

            stageState = GetComponent<MinigameStageState>();
            if (stageState == null)
            {
                Debug.LogError($"{name}: ExamNetwork не нашёл MinigameStageState на своём объекте", this);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            match.OnValueChanged += OnMatchChanged;
            stage.OnValueChanged += OnStageChanged;
            entries.OnListChanged += OnEntriesChanged;

            if (IsServer)
            {
                if (NetworkManager != null)
                {
                    NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
                }

                // Стадию публикуем сами, по событию машины стадий: контроллеру
                // не нужно помнить про сеть в каждой точке перехода.
                if (stageState != null)
                {
                    stageState.StageStarted += OnServerStageStarted;
                }
            }

            // Состояние могло приехать до подписки — разбираем то, что уже есть.
            matchDirty = true;
            stageDirty = true;
            entriesDirty = true;
        }

        public override void OnNetworkDespawn()
        {
            match.OnValueChanged -= OnMatchChanged;
            stage.OnValueChanged -= OnStageChanged;
            entries.OnListChanged -= OnEntriesChanged;

            if (IsServer)
            {
                if (NetworkManager != null)
                {
                    NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
                }

                if (stageState != null)
                {
                    stageState.StageStarted -= OnServerStageStarted;
                }
            }

            base.OnNetworkDespawn();
        }

        // ========== СЕРВЕР ПУБЛИКУЕТ ==========

        /// <summary>Опубликовать положение матча. Только сервер.</summary>
        public void PublishMatch(int questionNumber, int totalQuestions, int questionValue,
            int hostPlayerId, bool questionPosted)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            match.Value = new ExamMatchNetState
            {
                QuestionNumber = questionNumber,
                TotalQuestions = totalQuestions,
                QuestionValue = (byte)Mathf.Clamp(questionValue, 0, 255),
                HostPlayerId = hostPlayerId,
                QuestionPosted = questionPosted
            };
        }

        /// <summary>
        /// Стадия сменилась у сервера — объявляем её всем вместе с моментом
        /// конца. Момент берётся из общих часов, поэтому остаток у клиента
        /// сходится с серверным без поправки на пинг.
        /// </summary>
        private void OnServerStageStarted(byte stageId)
        {
            if (!IsSpawned || !IsServer || stageState == null)
            {
                return;
            }

            stage.Value = new ExamStageNetState
            {
                Question = stageState.Subround,
                Stage = stageId,
                EndTime = stageState.StageEndTime,
                Duration = stageState.StageDuration
            };
        }

        /// <summary>Начать публикацию состава заново: сервер перечитывает участников.</summary>
        public void BeginPublishEntries()
        {
            if (IsSpawned && IsServer)
            {
                entries.Clear();
            }
        }

        /// <summary>Добавить строку участника. Только сервер, только между Begin и End.</summary>
        public void PublishEntry(int playerId, int score, int lonelyHits, int correctAnswers,
            double lastScoredAt, ExamSide side, bool present)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            entries.Add(new ExamEntryNetState
            {
                PlayerId = playerId,
                Score = score,
                LonelyHits = (byte)Mathf.Clamp(lonelyHits, 0, 255),
                CorrectAnswers = (byte)Mathf.Clamp(correctAnswers, 0, 255),
                LastScoredAt = lastScoredAt,
                Side = (byte)side,
                Present = present
            });
        }

        // ========== ТЕКСТ ВОПРОСА: ТОЛЬКО В МОМЕНТ ПОКАЗА ==========

        /// <summary>
        /// Разослать текст вопроса. Зовётся ровно на входе в стадию показа
        /// и ни секундой раньше.
        ///
        /// Именно Rpc, а не <c>NetworkVariable</c>: переменная синхронизируется
        /// при подключении, поэтому её содержимое получил бы и тот, кто
        /// подключился в середине фазы печати. Rpc уходит только тем, кто уже
        /// в игре, и только тогда, когда решено.
        /// </summary>
        public void AnnounceQuestion(string question, string optionA, string optionB)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            AnnounceQuestionRpc(Encoding.UTF8.GetBytes(question ?? string.Empty),
                Encoding.UTF8.GetBytes(optionA ?? string.Empty),
                Encoding.UTF8.GetBytes(optionB ?? string.Empty));
        }

        [Rpc(SendTo.NotServer)]
        private void AnnounceQuestionRpc(byte[] question, byte[] optionA, byte[] optionB)
        {
            game?.ApplyNetworkQuestion(
                Encoding.UTF8.GetString(question),
                Encoding.UTF8.GetString(optionA),
                Encoding.UTF8.GetString(optionB));
        }

        /// <summary>
        /// Объявить, какая платформа раскрывается. <b>Это и есть публикация
        /// ответа</b> — отдельного пакета с верным вариантом не существует:
        /// клиент узнаёт правду ровно тогда, когда её видно глазами.
        /// </summary>
        public void AnnounceHatch(ExamSide wrongSide)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            AnnounceHatchRpc((byte)wrongSide);
        }

        [Rpc(SendTo.NotServer)]
        private void AnnounceHatchRpc(byte wrongSide) => game?.ApplyNetworkHatch((ExamSide)wrongSide);

        /// <summary>Вопрос не состоялся — Ведущий не успел напечатать.</summary>
        public void AnnounceSkipped()
        {
            if (IsSpawned && IsServer)
            {
                AnnounceSkippedRpc();
            }
        }

        [Rpc(SendTo.NotServer)]
        private void AnnounceSkippedRpc() => game?.ApplyNetworkSkipped();

        // ========== ДЕЙСТВИЯ ВЕДУЩЕГО ==========

        /// <summary>
        /// Отправить свой вопрос серверу. Зовёт только тот, кто сейчас
        /// за кафедрой; право на это сервер всё равно перепроверяет.
        /// </summary>
        public void SubmitQuestion(string question, string optionA, string optionB, ExamSide correct)
        {
            if (!IsSpawned)
            {
                return;
            }

            SubmitQuestionRpc(Encoding.UTF8.GetBytes(question ?? string.Empty),
                Encoding.UTF8.GetBytes(optionA ?? string.Empty),
                Encoding.UTF8.GetBytes(optionB ?? string.Empty),
                (byte)correct);
        }

        /// <summary>
        /// Приём вопроса на сервере. <c>RequireOwnership = false</c> потому,
        /// что объект принадлежит серверу, а шлёт клиент, — право проверяется
        /// не владением, а ролью Ведущего.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void SubmitQuestionRpc(byte[] question, byte[] optionA, byte[] optionB, byte correct,
            RpcParams rpcParams = default)
        {
            if (game == null)
            {
                return;
            }

            int senderId = (int)rpcParams.Receive.SenderClientId;
            game.ServerApplyQuestion(senderId,
                Encoding.UTF8.GetString(question),
                Encoding.UTF8.GetString(optionA),
                Encoding.UTF8.GetString(optionB),
                (ExamSide)correct);
        }

        /// <summary>Ведущий нажал «Готово»: просит закрыть фазу печати досрочно.</summary>
        public void RequestDone()
        {
            if (IsSpawned)
            {
                RequestDoneRpc();
            }
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void RequestDoneRpc(RpcParams rpcParams = default) =>
            game?.ServerRequestDone((int)rpcParams.Receive.SenderClientId);

        // ========== ПРИЁМ НА КЛИЕНТЕ ==========

        private void OnMatchChanged(ExamMatchNetState previous, ExamMatchNetState current) => matchDirty = true;

        private void OnStageChanged(ExamStageNetState previous, ExamStageNetState current) => stageDirty = true;

        private void OnEntriesChanged(NetworkListEvent<ExamEntryNetState> change) => entriesDirty = true;

        private void LateUpdate()
        {
            if (!IsSpawned || IsServer || game == null)
            {
                return;
            }

            // Состав мог смениться уже после того, как состояние разобрали:
            // тогда разбираем заново, иначе клиент, у которого так совпало,
            // остался бы без строк до конца матча.
            if (game.ContestantCount != appliedContestants)
            {
                appliedContestants = game.ContestantCount;
                matchDirty = true;
                stageDirty = true;
                entriesDirty = true;
            }

            if (matchDirty)
            {
                matchDirty = false;
                ExamMatchNetState m = match.Value;
                game.ApplyNetworkMatch(m.QuestionNumber, m.TotalQuestions, m.QuestionValue,
                    m.HostPlayerId, m.QuestionPosted);
            }

            if (stageDirty)
            {
                stageDirty = false;
                ExamStageNetState s = stage.Value;
                if (s.Stage != MinigameStageState.NoStage && stageState != null)
                {
                    stageState.ApplyState(s.Question, s.Stage, s.EndTime, s.Duration);
                }
            }

            if (entriesDirty)
            {
                entriesDirty = false;
                game.ApplyNetworkEntriesBegin();
                for (int i = 0; i < entries.Count; i++)
                {
                    ExamEntryNetState e = entries[i];
                    game.ApplyNetworkEntry(e.PlayerId, e.Score, e.LonelyHits, e.CorrectAnswers,
                        e.LastScoredAt, (ExamSide)e.Side, e.Present);
                }
                game.ApplyNetworkEntriesEnd();
            }
        }

        // ========== ДИСКОННЕКТ ==========

        /// <summary>
        /// Игрок ушёл. Решение принимает контроллер: если ушедший вёл вопрос,
        /// поведение зависит от того, успел ли он его опубликовать.
        /// </summary>
        private void OnClientDisconnected(ulong clientId)
        {
            if (IsServer)
            {
                game?.ServerHandleDisconnect((int)clientId);
            }
        }
    }
}
