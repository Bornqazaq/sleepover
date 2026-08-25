using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.UI;

namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>
    /// Правила «Верю / не верю»: ротация мест и ролей, последовательность
    /// стадий кона, разрешение исхода и начисление.
    ///
    /// <b>Скрытая информация — главное свойство этого класса.</b> Что лежит
    /// в какой коробке, знает только поле <see cref="winningSeat"/>, и оно
    /// не покидает авторитета. Клиенты в фазе 3 узнают исход из того, какая
    /// крышка открылась, а Знающий получает свою карточку адресно, ровно
    /// в <see cref="ApplyPeek"/>. Отдельного канала «галочка в левой коробке»
    /// не существует вовсе (спека 10.3).
    ///
    /// Всё, что меняет важное состояние, собрано в трёх точках входа:
    /// <see cref="HandleDecision"/>, <see cref="HandlePhrase"/> и
    /// <see cref="HandlePlayerLeft"/>. В фазе 3 они уйдут за <c>IsServer</c>
    /// и <c>ServerRpc</c> без переписывания логики.
    /// </summary>
    public sealed class BelieveOrNotMinigame : MinigameControllerBase
    {
        /// <summary>Ключи истории ролей. У команд истории раздельные: составы не пересекаются.</summary>
        private const string SeatRoleKeyTeamA = "believe-seat-a";
        private const string SeatRoleKeyTeamB = "believe-seat-b";
        private const string SeatRoleKeySolo = "believe-seat";
        private const string KnowerRoleKey = "believe-knower";

        /// <summary>Ниже двух играть не в кого: за стол садятся двое.</summary>
        private const int MinPlayers = 2;

        [Header("Данные")]
        [SerializeField] private BelieveOrNotConfig config;

        [Header("Сцена")]
        [SerializeField] private BelieveTable table;
        [SerializeField] private MinigameStageState stageState;
        [SerializeField] private MinigameCameraController cameraController;

        [Header("Интерфейс")]
        [SerializeField] private BelievePeekView peekView;
        [SerializeField] private BelieveDecisionPanel decisionPanel;
        [SerializeField] private QuickPhrasePanel phrasePanel;

        [Header("Наборы реплик")]
        [SerializeField] private QuickPhraseSet knowerPhrases;
        [SerializeField] private QuickPhraseSet deciderPhrases;

        private readonly List<BelieveEntry> entries = new List<BelieveEntry>(8);
        private readonly SpecialRoleHistory roles = new SpecialRoleHistory();
        private readonly List<SessionPlayer> teamA = new List<SessionPlayer>(4);
        private readonly List<SessionPlayer> teamB = new List<SessionPlayer>(4);
        private readonly List<SessionPlayer> pair = new List<SessionPlayer>(2);
        private readonly double[] nextPhraseAt = new double[BelieveTable.SeatCount];

        private BelieveMatchState match;
        private Coroutine revealRoutine;

        /// <summary>Сцена собрана и состав разобран: без этого кон начинать нечем.</summary>
        private bool ready;

        /// <summary>
        /// Место, чья коробка держит галочку прямо сейчас. <b>Единственное
        /// хранилище скрытой информации, и оно не покидает авторитета.</b>
        /// Обмен коробок его не меняет: меняются места, а не содержимое —
        /// поэтому исход считается как «поменяли ли» поверх этого значения.
        /// </summary>
        private int winningSeat;

        /// <summary>Номер кона, который уже разрешён. Защита от второго разрешения того же кона.</summary>
        private int resolvedRound;

        public BelieveOrNotConfig Config => config;

        /// <summary>Кто сейчас за столом Знающим. Читают отладочные болванки и сетевой слой.</summary>
        public int KnowerPlayerId => match.KnowerPlayerId;

        /// <summary>Кто сейчас Решающий.</summary>
        public int DeciderPlayerId => match.DeciderPlayerId;

        /// <summary>Текущая стадия кона. <c>MinigameStageState.NoStage</c> — кон не идёт.</summary>
        public byte Stage => stageState != null ? stageState.Stage : MinigameStageState.NoStage;

        /// <summary>Состав, который клиент уже разобрал. Состояние вполне может приехать раньше ростера.</summary>
        public int EntryCount => entries.Count;

        protected override void Awake()
        {
            base.Awake();

            if (decisionPanel != null)
            {
                decisionPanel.DecisionPicked += SubmitDecision;
            }

            if (phrasePanel != null)
            {
                phrasePanel.PhrasePicked += SubmitPhrase;
            }

            if (stageState != null)
            {
                stageState.StageStarted += ApplyStageVisuals;
                stageState.StageElapsed += AdvanceStage;
            }
        }

        private void OnDestroy()
        {
            if (decisionPanel != null)
            {
                decisionPanel.DecisionPicked -= SubmitDecision;
            }

            if (phrasePanel != null)
            {
                phrasePanel.PhrasePicked -= SubmitPhrase;
            }

            if (stageState != null)
            {
                stageState.StageStarted -= ApplyStageVisuals;
                stageState.StageElapsed -= AdvanceStage;
            }
        }

        // ========== СТАРТ И КОНЕЦ МАТЧА ==========

        protected override void OnPlayersReady()
        {
            entries.Clear();
            teamA.Clear();
            teamB.Clear();
            ready = false;

            if (config == null || table == null || stageState == null)
            {
                Debug.LogError($"{name}: «Верю / не верю» не настроена (config/table/stageState)", this);
                return;
            }

            SplitTeams();

            for (int i = 0; i < Players.Count; i++)
            {
                SessionPlayer player = Players[i];
                entries.Add(new BelieveEntry
                {
                    PlayerId = player.Id,
                    Team = TeamOf(player.Id),
                    Present = true
                });
            }

            match = default;
            match.TotalRounds = config.GetRoundCount(Players.Count);
            resolvedRound = 0;
            ready = true;
        }

        protected override void OnRoundStarted()
        {
            if (!HasAuthority || !ready)
            {
                return;
            }

            if (Players.Count < MinPlayers)
            {
                Debug.LogWarning($"{name}: 🎴 играть не с кем — меньше {MinPlayers} участников, матч закрыт", this);
                EndMinigame();
                return;
            }

            Debug.Log($"🎴 «Верю / не верю»: {Players.Count} игроков, {match.TotalRounds} конов " +
                      $"по {config.RoundSeconds:F0} с");
            BeginRound(1);
        }

        protected override void OnRoundEnded()
        {
            // Всё, что игра навесила, она обязана снять сама — иначе оно уедет
            // в хаб вместе с персонажем (MinigameTemplate_HOWTO).
            if (revealRoutine != null)
            {
                StopCoroutine(revealRoutine);
                revealRoutine = null;
            }

            peekView?.Close();
            decisionPanel?.Close();
            phrasePanel?.Close();
            table?.HideBubbles();

            for (int i = 0; i < Players.Count; i++)
            {
                PlayerController avatar = Players[i].Avatar;
                if (avatar == null)
                {
                    continue;
                }

                avatar.MovementLocked = false;
                avatar.ImpulseImmune = false;
            }

            // Строго последним: риг стола умрёт вместе со сценой, и камера
            // обязана уехать в хаб на собственном персонаже.
            RestoreLocalCamera();
            stageState?.StopSequence();
        }

        protected override void CollectResults(MinigameResults results)
        {
            BelieveRanking.Fill(entries, match.TeamAWins, match.TeamBWins, results);

            // Места в лог целиком: именно по этой строке сверяются дележи мест
            // и порядок тайбрейков — глазами на экране результатов их не поймать.
            var line = new System.Text.StringBuilder("🎴 места:");
            IReadOnlyList<MinigameResults.PlayerResult> table = results.Entries;
            for (int i = 0; i < table.Count; i++)
            {
                int index = IndexOf(table[i].PlayerId);
                BelieveEntry entry = index >= 0 ? entries[index] : default;
                line.Append($" [{table[i].Place}] {NameOf(table[i].PlayerId)} " +
                            $"({entry.Team}, побед {entry.RoundsWon}, из них вслепую {entry.DeciderWins}, " +
                            $"сидел {entry.RoundsSeated})");
            }

            Debug.Log(line.ToString());
        }

        // ========== КОН ==========

        /// <summary>
        /// Начать кон: раздать места и роли, разложить карточки. Всё решается
        /// у авторитета — и рассадка, и рандом содержимого коробок.
        /// </summary>
        private void BeginRound(int roundNumber)
        {
            match.RoundNumber = roundNumber;
            match.Decision = Decision.None;
            match.Resolved = false;
            match.Cancelled = false;

            if (!TrySeatPlayers())
            {
                Debug.LogWarning($"{name}: 🎴 кон {roundNumber} не составить — матч закрыт досрочно", this);
                EndMinigame();
                return;
            }

            winningSeat = Random.Range(0, BelieveTable.SeatCount);
            nextPhraseAt[0] = 0d;
            nextPhraseAt[1] = 0d;

            // Состав кона в лог: по нему сверяется ротация на приёмке фазы.
            // Что в коробках — не пишем: лог читают и в сетевом прогоне.
            Debug.Log($"🎴 кон {roundNumber}: за столом {NameOf(SeatedId(0))} и {NameOf(SeatedId(1))}, " +
                      $"знает {NameOf(match.KnowerPlayerId)}");

            stageState.BeginSubround(roundNumber, BelieveStage.Seating, config.SeatingSeconds);
        }

        /// <summary>
        /// Кто садится и кто из двоих Знающий.
        ///
        /// Ротация, а не чистый рандом: садится случайный из тех, кто ещё
        /// не сидел. Без неё на двоих один человек с вероятностью 1/8 получает
        /// роль Знающего все четыре кона, и матч ломается.
        /// </summary>
        private bool TrySeatPlayers()
        {
            SessionPlayer first;
            SessionPlayer second;

            if (teamA.Count > 0 && teamB.Count > 0)
            {
                // Истории команд раздельные: составы не пересекаются, и сброс
                // одной не должен обнулять память о другой.
                first = FindPlayer(roles.Pick(SeatRoleKeyTeamA, teamA));
                second = FindPlayer(roles.Pick(SeatRoleKeyTeamB, teamB));
            }
            else if (Players.Count == 3)
            {
                // Каждый с каждым: три пары по кругу, третий в коне без роли.
                int index = (match.RoundNumber - 1) % 3;
                first = Players[index == 2 ? 1 : 0];
                second = Players[index == 0 ? 1 : 2];
            }
            else if (Players.Count >= MinPlayers)
            {
                first = FindPlayer(roles.Pick(SeatRoleKeySolo, Players));
                second = FirstOther(first);
            }
            else
            {
                return false;
            }

            if (first == null || second == null || first == second)
            {
                return false;
            }

            match.Seat0PlayerId = first.Id;
            match.Seat1PlayerId = second.Id;

            pair.Clear();
            pair.Add(first);
            pair.Add(second);

            int knowerId = roles.Pick(KnowerRoleKey, pair);
            match.KnowerPlayerId = knowerId;
            match.DeciderPlayerId = knowerId == first.Id ? second.Id : first.Id;
            return true;
        }

        /// <summary>Перевести последовательность на следующую стадию. Решает только авторитет.</summary>
        private void AdvanceStage(byte finished)
        {
            switch (finished)
            {
                case BelieveStage.Seating:
                    stageState.EnterStage(BelieveStage.Peek, config.PeekSeconds);
                    break;

                case BelieveStage.Peek:
                    stageState.EnterStage(BelieveStage.Persuasion, config.PersuasionSeconds);
                    break;

                case BelieveStage.Persuasion:
                    // Таймер истёк без решения — засчитывается «Оставить».
                    if (match.Decision == Decision.None)
                    {
                        match.Decision = Decision.Keep;
                    }

                    ResolveRound();
                    stageState.EnterStage(BelieveStage.Reveal, config.RevealSeconds);
                    break;

                case BelieveStage.Reveal:
                    stageState.EnterStage(BelieveStage.Reaction, config.ReactionSeconds);
                    break;

                case BelieveStage.Reaction:
                    if (match.RoundNumber >= match.TotalRounds)
                    {
                        Debug.Log($"🎴 матч окончен: {match.TeamAWins}:{match.TeamBWins}");
                        EndMinigame();
                        return;
                    }

                    BeginRound(match.RoundNumber + 1);
                    break;
            }
        }

        /// <summary>
        /// Посчитать исход и начислить. Кон выигрывает тот, у кого в итоге
        /// оказалась карточка с галочкой — правило симметрично, и выиграть
        /// может как Решающий, так и Знающий.
        /// </summary>
        private void ResolveRound()
        {
            if (match.Cancelled || match.Resolved || resolvedRound == match.RoundNumber)
            {
                return;
            }

            resolvedRound = match.RoundNumber;
            match.Resolved = true;

            // Обмен меняет места коробок, а не их содержимое: поэтому исход —
            // это «поменяли ли» поверх того, у какого места лежала галочка.
            int winnerSeat = match.Decision == Decision.Swap
                ? BelieveTable.SeatCount - 1 - winningSeat
                : winningSeat;

            int winnerId = SeatedId(winnerSeat);
            bool deciderWon = winnerId == match.DeciderPlayerId;

            MarkSeated(SeatedId(0));
            MarkSeated(SeatedId(1));
            AwardRound(winnerId, deciderWon);

            Debug.Log($"🎴 кон {match.RoundNumber}/{match.TotalRounds}: " +
                      $"{(match.Decision == Decision.Swap ? "поменял" : "оставил")} — " +
                      $"выиграл {NameOf(winnerId)} ({(deciderWon ? "Решающий" : "Знающий")})");
        }

        private void AwardRound(int winnerId, bool deciderWon)
        {
            int index = IndexOf(winnerId);
            if (index < 0)
            {
                return;
            }

            BelieveEntry entry = entries[index];
            entry.RoundsWon++;
            if (deciderWon)
            {
                entry.DeciderWins++;
            }

            entry.LastWonAt = NetworkClock.Now;
            entries[index] = entry;

            if (entry.Team == TeamId.A)
            {
                match.TeamAWins++;
            }
            else if (entry.Team == TeamId.B)
            {
                match.TeamBWins++;
            }
        }

        private void MarkSeated(int playerId)
        {
            int index = IndexOf(playerId);
            if (index < 0)
            {
                return;
            }

            BelieveEntry entry = entries[index];
            entry.RoundsSeated++;
            entries[index] = entry;
        }

        // ========== ТОЧКИ ВХОДА ДЛЯ ДЕЙСТВИЙ ИГРОКА ==========

        /// <summary>
        /// Решение Решающего. <b>Единственная точка, где решение принимается</b> —
        /// в фазе 3 сюда придёт <c>ServerRpc</c>, и проверки останутся теми же.
        ///
        /// Отказ логируется с причиной, но отправителю не сообщает ничего,
        /// чего он знать не должен.
        /// </summary>
        public void HandleDecision(int playerId, Decision decision)
        {
            if (!HasAuthority)
            {
                return;
            }

            if (decision != Decision.Keep && decision != Decision.Swap)
            {
                Debug.LogWarning($"{name}: 🎴 решение отклонено — неизвестное значение {decision}", this);
                return;
            }

            if (playerId != match.DeciderPlayerId)
            {
                Debug.LogWarning($"{name}: 🎴 решение отклонено — {NameOf(playerId)} не Решающий", this);
                return;
            }

            if (Stage != BelieveStage.Persuasion)
            {
                Debug.LogWarning($"{name}: 🎴 решение отклонено — сейчас не фаза уговоров", this);
                return;
            }

            if (match.Decision != Decision.None)
            {
                Debug.LogWarning($"{name}: 🎴 решение отклонено — в этом коне уже решено", this);
                return;
            }

            match.Decision = decision;

            // Решение немедленно обрывает уговоры: дальше сразу раскрытие.
            stageState.EndStageNow();
        }

        /// <summary>
        /// Реплика сидящего. Вторая и последняя точка входа для действий игрока.
        /// Проверяется всё: кто говорит, из своего ли набора и не спамит ли.
        /// </summary>
        public void HandlePhrase(int playerId, int phraseIndex)
        {
            if (!HasAuthority)
            {
                return;
            }

            int seat = SeatOf(playerId);
            if (seat < 0)
            {
                Debug.LogWarning($"{name}: 🎴 реплика отклонена — {NameOf(playerId)} не за столом", this);
                return;
            }

            if (Stage != BelieveStage.Persuasion)
            {
                Debug.LogWarning($"{name}: 🎴 реплика отклонена — сейчас не фаза уговоров", this);
                return;
            }

            QuickPhraseSet set = SetFor(playerId);
            if (set == null || !set.IsValidIndex(phraseIndex))
            {
                Debug.LogWarning($"{name}: 🎴 реплика отклонена — индекс {phraseIndex} вне набора {NameOf(playerId)}", this);
                return;
            }

            if (NetworkClock.Now < nextPhraseAt[seat])
            {
                Debug.LogWarning($"{name}: 🎴 реплика отклонена — кулдаун у {NameOf(playerId)}", this);
                return;
            }

            nextPhraseAt[seat] = NetworkClock.Now + config.PhraseCooldown;
            ApplyPhrase(playerId, phraseIndex);
        }

        /// <summary>
        /// Участник вышел из матча.
        ///
        /// Три разных случая, и различает их стадия. Отдельная строка про
        /// «Знающий ушёл после решения» нужна не для полноты: без неё
        /// выдёргивание кабеля становится способом отменить собственный
        /// проигрыш (спека 10.4).
        /// </summary>
        public void HandlePlayerLeft(int playerId)
        {
            if (!HasAuthority)
            {
                return;
            }

            int index = IndexOf(playerId);
            if (index >= 0)
            {
                BelieveEntry entry = entries[index];
                entry.Present = false;
                entries[index] = entry;
            }

            roles.Forget(playerId);
            RemovePlayer(playerId);
            teamA.RemoveAll(p => p.Id == playerId);
            teamB.RemoveAll(p => p.Id == playerId);

            if (!stageState.Running)
            {
                return;
            }

            bool decisionPending = Stage == BelieveStage.Peek || Stage == BelieveStage.Persuasion;

            if (playerId == match.DeciderPlayerId && decisionPending)
            {
                Debug.Log($"🎴 Решающий ушёл — засчитано «Оставить», кон {match.RoundNumber} разрешается сам");
                match.Decision = Decision.Keep;
                stageState.EndStageNow();
            }
            else if (playerId == match.KnowerPlayerId && decisionPending)
            {
                Debug.Log($"🎴 Знающий ушёл до решения — кон {match.RoundNumber} отменён, очко никому");
                match.Cancelled = true;
                stageState.EndStageNow();
            }

            // Знающий, ушедший ПОСЛЕ решения, ничего не отменяет: исход уже
            // посчитан на сервере и от него больше не зависит.

            if (CountPresent() < MinPlayers || !CanFormRound())
            {
                Debug.Log("🎴 матч окончен: составить кон больше не из кого");
                EndMinigame();
            }
        }

        /// <summary>Локальное намерение Решающего. В фазе 3 отсюда уйдёт <c>ServerRpc</c>.</summary>
        private void SubmitDecision(Decision decision) => HandleDecision(LocalPlayerId, decision);

        /// <summary>Локальное намерение сидящего сказать реплику.</summary>
        private void SubmitPhrase(int phraseIndex) => HandlePhrase(LocalPlayerId, phraseIndex);

        // ========== ПРИМЕНЕНИЕ СОСТОЯНИЯ (общее для авторитета и клиента) ==========

        /// <summary>
        /// Что происходит на экране в начале стадии. Зовётся на всех машинах:
        /// у авторитета — из своей последовательности, у клиента — из сети.
        /// </summary>
        private void ApplyStageVisuals(byte stage)
        {
            switch (stage)
            {
                case BelieveStage.Seating:
                    ApplySeating();
                    break;

                case BelieveStage.Peek:
                    ApplyPeekStage();
                    break;

                case BelieveStage.Persuasion:
                    ApplyPersuasionStage();
                    break;

                case BelieveStage.Reveal:
                    ApplyRevealStage();
                    break;

                case BelieveStage.Reaction:
                    ApplyReactionStage();
                    break;
            }

            UpdateHud();
        }

        private void ApplySeating()
        {
            peekView?.Close();
            decisionPanel?.Close();
            phrasePanel?.Close();
            table.HideBubbles();

            if (HasAuthority)
            {
                SeatAvatars();
                PrepareBoxes();
            }

            ApplySeatCamera();
        }

        /// <summary>
        /// Перенести двоих за стол, обездвижить и защитить от толчков.
        ///
        /// Иммунитет — это и есть требование «сидящих нельзя толкать»: он уже
        /// написан в Core, и <c>PlayerPushAbility</c> отсекает иммунного ещё
        /// на стороне бьющего, чтобы по сети не летел заведомо пустой толчок.
        /// </summary>
        private void SeatAvatars()
        {
            for (int i = 0; i < Players.Count; i++)
            {
                PlayerController avatar = Players[i].Avatar;
                if (avatar == null)
                {
                    continue;
                }

                bool seated = Players[i].Id == SeatedId(0) || Players[i].Id == SeatedId(1);
                avatar.MovementLocked = seated;
                avatar.ImpulseImmune = seated;
            }

            for (int seat = 0; seat < BelieveTable.SeatCount; seat++)
            {
                PlayerController avatar = FindPlayer(SeatedId(seat))?.Avatar;
                Transform anchor = table.GetSeatAnchor(seat);
                if (avatar == null || anchor == null)
                {
                    continue;
                }

                avatar.RequestTeleport(anchor.position, anchor.rotation);
                avatar.SetFacing(anchor.eulerAngles.y);
            }
        }

        /// <summary>
        /// Разложить карточки по коробкам. Рандом серверный, и результат
        /// наружу не уходит: коробки на клиентах получают
        /// <see cref="BelieveCard.Unknown"/> и остаются закрытыми.
        /// </summary>
        private void PrepareBoxes()
        {
            for (int seat = 0; seat < BelieveTable.SeatCount; seat++)
            {
                BelieveBox box = table.GetBox(seat);
                box?.Prepare(seat == winningSeat ? BelieveCard.Win : BelieveCard.Lose,
                    seat == 0 ? BoxSlot.Seat0 : BoxSlot.Seat1,
                    table.GetBoxPosition(seat));
            }
        }

        private void ApplyPeekStage()
        {
            int knowerSeat = SeatOf(match.KnowerPlayerId);
            if (knowerSeat >= 0)
            {
                table.GetBox(knowerSeat)?.OpenPeekCrack(config.LidPeekAngle, config.LidOpenSeconds);
            }

            if (!HasAuthority)
            {
                return;
            }

            // Карточка уходит ровно одному. В фазе 3 здесь будет адресный Rpc,
            // а точка приёма (ApplyPeek) останется той же.
            if (match.KnowerPlayerId == LocalPlayerId)
            {
                ApplyPeek(knowerSeat == winningSeat ? BelieveCard.Win : BelieveCard.Lose);
            }
        }

        /// <summary>
        /// Показать карточку Знающему. Единственный путь, которым содержимое
        /// коробки вообще становится известно клиенту, и он адресный.
        /// </summary>
        public void ApplyPeek(BelieveCard card) => peekView?.Show(card);

        private void ApplyPersuasionStage()
        {
            int knowerSeat = SeatOf(match.KnowerPlayerId);
            if (knowerSeat >= 0)
            {
                table.GetBox(knowerSeat)?.ClosePeekCrack(config.LidOpenSeconds);
            }

            peekView?.Close();

            int localSeat = SeatOf(LocalPlayerId);
            if (localSeat < 0)
            {
                return;
            }

            // Колесо эмоций у сидящих не гасим: рожи за столом — половина
            // комедии, а замороженный бинд Tab при этом не тронут.
            phrasePanel?.Open(SetFor(LocalPlayerId), config.PhraseCooldown, FindPlayer(LocalPlayerId)?.Avatar,
                suppressMovement: false,
                hint: LocalPlayerId == match.KnowerPlayerId ? "Ты знаешь. Он — нет." : "Читай его.");

            if (LocalPlayerId == match.DeciderPlayerId)
            {
                decisionPanel?.Open();
            }
        }

        /// <summary>Сказанная реплика: пузырь над головой, видно всем в зале.</summary>
        public void ApplyPhrase(int playerId, int phraseIndex)
        {
            int seat = SeatOf(playerId);
            QuickPhraseSet set = SetFor(playerId);
            if (seat < 0 || set == null)
            {
                return;
            }

            SpeechBubble bubble = table.GetBubble(seat);
            if (bubble == null)
            {
                return;
            }

            bubble.AttachTo(FindPlayer(playerId)?.Avatar?.CameraTarget);
            bubble.Show(set.Get(phraseIndex), config.PhraseBubbleSeconds);
        }

        private void ApplyRevealStage()
        {
            decisionPanel?.Close();
            phrasePanel?.Close();

            if (!HasAuthority)
            {
                return;
            }

            // Карточки объявляются в тот момент, когда их и так все увидят.
            ApplyReveal(
                winningSeat == 0 ? BelieveCard.Win : BelieveCard.Lose,
                winningSeat == 1 ? BelieveCard.Win : BelieveCard.Lose);
        }

        /// <summary>
        /// Обмен коробок (если меняли) и одновременное раскрытие обеих крышек.
        /// Карточки приходят параметрами: в фазе 3 это будет <c>Rpc</c>,
        /// и клиент узнает содержимое ровно тогда же, когда его увидит зал.
        /// </summary>
        public void ApplyReveal(BelieveCard seat0Card, BelieveCard seat1Card)
        {
            if (revealRoutine != null)
            {
                StopCoroutine(revealRoutine);
            }

            revealRoutine = StartCoroutine(RevealRoutine(seat0Card, seat1Card));
        }

        private IEnumerator RevealRoutine(BelieveCard seat0Card, BelieveCard seat1Card)
        {
            BelieveBox atSeat0 = table.GetBox(0);
            BelieveBox atSeat1 = table.GetBox(1);

            if (match.Decision == Decision.Swap && atSeat0 != null && atSeat1 != null)
            {
                atSeat0.MoveToSlot(BoxSlot.Seat1, table.GetBoxPosition(1), config.BoxSwapSeconds, config.BoxSwapArcHeight);
                atSeat1.MoveToSlot(BoxSlot.Seat0, table.GetBoxPosition(0), config.BoxSwapSeconds, config.BoxSwapArcHeight);
                yield return new WaitForSeconds(config.BoxSwapSeconds);
            }

            // Обе крышки — одним кадром. Иначе зал успевает прочитать исход
            // по первой открывшейся.
            atSeat0?.Reveal(seat0Card, config.LidOpenSeconds);
            atSeat1?.Reveal(seat1Card, config.LidOpenSeconds);

            revealRoutine = null;
        }

        private void ApplyReactionStage()
        {
            // Гэг бьёт по проигравшей коробке — той, что стоит у проигравшего.
            for (int seat = 0; seat < BelieveTable.SeatCount; seat++)
            {
                BelieveBox box = table.GetBox(seat);
                if (box != null && box.Card == BelieveCard.Lose)
                {
                    box.PlayGag();
                }
            }
        }

        // ========== КАМЕРА ==========

        /// <summary>
        /// Поставить камеру сидящего. Фиксированный риг в сцене один, поэтому
        /// игра переставляет его к нужному месту — режим `Fixed` из Core сам
        /// позицию не задаёт, он лишь выбирает риг.
        /// </summary>
        private void ApplySeatCamera()
        {
            int seat = SeatOf(LocalPlayerId);
            if (seat < 0)
            {
                RestoreLocalCamera();
                return;
            }

            if (cameraController == null || table.FixedCameraRig == null)
            {
                return;
            }

            Transform anchor = table.GetCameraAnchor(seat);
            if (anchor != null)
            {
                table.FixedCameraRig.SetPositionAndRotation(anchor.position, anchor.rotation);
            }

            int opponentSeat = BelieveTable.SeatCount - 1 - seat;
            cameraController.Apply(CameraMode.Fixed, table.GetLookTarget(opponentSeat));
        }

        private void RestoreLocalCamera()
        {
            if (cameraController == null || cameraController.CurrentMode == CameraMode.ThirdPerson)
            {
                return;
            }

            PlayerController avatar = SessionScoreboard.Current?.LocalPlayer?.Avatar;
            if (avatar != null)
            {
                cameraController.Apply(CameraMode.ThirdPerson, avatar.transform);
            }
        }

        // ========== HUD ==========

        private void UpdateHud()
        {
            if (Hud == null)
            {
                return;
            }

            string score = teamA.Count > 0 && teamB.Count > 0
                ? $"счёт {match.TeamAWins}:{match.TeamBWins}"
                : "личный зачёт";

            Hud.ShowStatus($"Кон {match.RoundNumber}/{match.TotalRounds}   •   {score}   •   " +
                           $"знает {NameOf(match.KnowerPlayerId)}   •   решает {NameOf(match.DeciderPlayerId)}");
        }

        /// <summary>
        /// Насколько сидящему позволено отъехать от своего места, прежде чем
        /// его вернут. Пять сантиметров — это шум физики, а не смещение.
        /// </summary>
        private const float SeatDriftTolerance = 0.05f;

        /// <summary>
        /// Держать сидящих на их местах.
        ///
        /// <c>ImpulseImmune</c> гасит толчки и импульсы, но не обычное
        /// столкновение: зритель, вбежавший в сидящего, просто расталкивает
        /// его телом. Замерено 25.08 — сидящих сносило на 0.2–0.4 м, и кадр
        /// уезжал вместе с ними, потому что камера стоит на месте, а
        /// персонаж нет.
        ///
        /// Возврат, а не заморозка Rigidbody, выбран намеренно: забытая
        /// заморозка уехала бы в хаб вместе с персонажем и он остался бы
        /// неподвижным навсегда. Забытый возврат не делает ничего.
        /// </summary>
        private void PinSeatedPlayers()
        {
            if (!HasAuthority || table == null)
            {
                return;
            }

            for (int seat = 0; seat < BelieveTable.SeatCount; seat++)
            {
                PlayerController avatar = FindPlayer(SeatedId(seat))?.Avatar;
                Transform anchor = table.GetSeatAnchor(seat);
                if (avatar == null || anchor == null || !avatar.MovementLocked)
                {
                    continue;
                }

                if (Vector3.Distance(avatar.transform.position, anchor.position) > SeatDriftTolerance)
                {
                    avatar.RequestTeleport(anchor.position, anchor.rotation);
                }
            }
        }

        private void Update()
        {
            if (stageState == null || !stageState.Running)
            {
                return;
            }

            PinSeatedPlayers();

            float remaining = stageState.StageRemaining;

            if (peekView != null && peekView.IsOpen)
            {
                peekView.SetCountdown(remaining);
            }

            if (decisionPanel != null && decisionPanel.IsOpen)
            {
                decisionPanel.SetCountdown(remaining);
            }
        }

        // ========== СОСТАВ И ПОИСК ==========

        /// <summary>
        /// Разделить на команды: поровну, при нечётном большая половина —
        /// команде A. При 2–3 игроках команд нет, и счёт только личный.
        /// </summary>
        private void SplitTeams()
        {
            if (Players.Count < 4)
            {
                return;
            }

            int half = Mathf.CeilToInt(Players.Count * 0.5f);
            for (int i = 0; i < Players.Count; i++)
            {
                if (i < half)
                {
                    teamA.Add(Players[i]);
                }
                else
                {
                    teamB.Add(Players[i]);
                }
            }
        }

        private TeamId TeamOf(int playerId)
        {
            for (int i = 0; i < teamA.Count; i++)
            {
                if (teamA[i].Id == playerId)
                {
                    return TeamId.A;
                }
            }

            for (int i = 0; i < teamB.Count; i++)
            {
                if (teamB[i].Id == playerId)
                {
                    return TeamId.B;
                }
            }

            return TeamId.None;
        }

        private QuickPhraseSet SetFor(int playerId) =>
            playerId == match.KnowerPlayerId ? knowerPhrases : deciderPhrases;

        /// <summary>
        /// Кто сидит на этом месте. Хранится в состоянии матча, а не отдельным
        /// полем: клиенту оно нужно ровно так же, как серверу — по нему он
        /// понимает, чья коробка где стоит и куда ставить камеру.
        /// </summary>
        private int SeatedId(int seat) => seat == 0 ? match.Seat0PlayerId : match.Seat1PlayerId;

        private int SeatOf(int playerId)
        {
            if (playerId == SpecialRoleHistory.NoPlayer)
            {
                return -1;
            }

            for (int seat = 0; seat < BelieveTable.SeatCount; seat++)
            {
                if (SeatedId(seat) == playerId)
                {
                    return seat;
                }
            }

            return -1;
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

        private SessionPlayer FirstOther(SessionPlayer except)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i] != except)
                {
                    return Players[i];
                }
            }

            return null;
        }

        private int CountPresent()
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Present)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Кон ещё можно составить: в командном формате обе команды должны быть живы.</summary>
        private bool CanFormRound()
        {
            if (entries.Count >= 4 && HasTeams())
            {
                return teamA.Count > 0 && teamB.Count > 0;
            }

            return Players.Count >= MinPlayers;
        }

        private bool HasTeams()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Team != TeamId.None)
                {
                    return true;
                }
            }

            return false;
        }

        private int LocalPlayerId => SessionScoreboard.Current?.LocalPlayer?.Id ?? SpecialRoleHistory.NoPlayer;

        private string NameOf(int playerId) => FindPlayer(playerId)?.DisplayName ?? "—";
    }
}
