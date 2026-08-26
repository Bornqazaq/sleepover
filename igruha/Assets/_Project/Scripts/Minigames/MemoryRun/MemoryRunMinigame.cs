using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.UI;

namespace Igruha.Minigames.MemoryRun
{
    /// <summary>
    /// Правила «Рейса на память»: очередь ходов, проход по плитам, детонация,
    /// возврат в стартовую зону и расстановка мест.
    ///
    /// Общий цикл — обучалка, таймер, результаты, возврат в хаб — целиком
    /// в <see cref="MinigameControllerBase"/>, здесь только своё.
    ///
    /// <b>Маршрут не покидает этот объект.</b> Ни плиты, ни барьер, ни HUD не
    /// знают, какая плита безопасна: единственный, кто спрашивает у маршрута, —
    /// <see cref="ResolveLanding"/>. В сетевой фазе объект живёт только
    /// на сервере, и проверять это будут рефлексией по реплицируемым полям.
    /// </summary>
    /// <remarks>
    /// <b>Network-ready.</b> Важное состояние меняют ровно четыре метода —
    /// <see cref="ResolveLanding"/>, <see cref="FinishTurn"/>,
    /// <see cref="BeginTurn"/> и <see cref="CollectResults"/>, — и все они уже
    /// вызываются только под <see cref="MinigameControllerBase.HasAuthority"/>.
    /// В фазе 3 их останется обернуть, а не переписать.
    /// </remarks>
    [RequireComponent(typeof(RoundTimer))]
    public sealed class MemoryRunMinigame : MinigameControllerBase
    {
        /// <summary>Причина, по которой ход кончился. Влияет и на счётчик смертей, и на HUD.</summary>
        private enum TurnEnd
        {
            Mine,
            Fell,
            TimedOut,
            Reached,

            /// <summary>Персонажа вытащил <c>StuckDetector</c>. Смерть за это не засчитывается.</summary>
            Unstuck
        }

        /// <summary>
        /// Ниже этой отметки игрок считается упавшим. Стоит заметно выше дна
        /// пропасти: ждать настоящего дна значит ждать лишнюю секунду полёта
        /// и держать очередь всё это время.
        /// </summary>
        private const float FallThreshold = -2f;

        /// <summary>Страховка от зацикливания, если у всех подряд не оказалось аватара.</summary>
        private const int MaxTurnSkips = 16;

        [Header("Ссылки")]
        [SerializeField] private MemoryRunConfig config;
        [SerializeField] private TurnGate gate;
        [Tooltip("Метка над тем, чей ход. Без неё зрители не разбирают, на какую плиту он встал")]
        [SerializeField] private ActivePlayerMarker activeMarker;

        [Tooltip("Писать в лог, чем кончился каждый ход. Нужно на прогонах: без разбивки по причинам смерти нельзя отличить «маршрут сложный» от «прыжок не долетает»")]
        [SerializeField] private bool logTurns;

        private readonly MemoryRunRoute route = new MemoryRunRoute();
        private readonly MemoryRunState state = new MemoryRunState();
        private readonly TurnQueue queue = new TurnQueue();
        private readonly MemoryRunRanking ranking = new MemoryRunRanking();
        private readonly List<int> playerIds = new List<int>(8);

        /// <summary>
        /// Плита оказалась безопасной. <b>Утечкой не является:</b> это видели
        /// все восемь человек в зале, ровно на этом игра и построена. Событие
        /// нужно звуку, VFX и погонщику болванок.
        /// </summary>
        public event System.Action<int, int> SafePlateProved;

        /// <summary>Плита оказалась миной. Тоже общеизвестно: все слышали взрыв.</summary>
        public event System.Action<int, int> MinePlateProved;

        /// <summary>Кто сейчас идёт. Общеизвестно — над ним горит метка.</summary>
        public PlayerController CurrentWalker => walker;

        /// <summary>Чей ход. <see cref="TurnQueue.NoPlayer"/>, когда ходить некому.</summary>
        public int CurrentWalkerId => walkerId;

        /// <summary>Порядок ходов целиком — интерфейсу показать, кто следующий.</summary>
        public IReadOnlyList<int> TurnOrder => queue.Order;

        /// <summary>Сколько секунд осталось у идущего. Ноль, пока идёт объявление хода.</summary>
        public float TurnSecondsLeft =>
            turnArmed ? Mathf.Max(0f, (float)(turnDeadline - NetworkClock.Now)) : 0f;

        /// <summary>Идёт ли отсчёт хода. False — играет двухсекундное объявление.</summary>
        public bool TurnArmed => turnArmed;

        /// <summary>Лимит попыток из конфига — интерфейсу для строки «3 / 10».</summary>
        public int DeathLimit => config != null ? config.DeathLimit : 0;

        /// <summary>Сколько раз погиб игрок. Своё показывает HUD, чужое не показывает никто.</summary>
        public int DeathsOf(int playerId) => state.DeathsOf(playerId);

        /// <summary>Имя участника по идентификатору.</summary>
        public string DisplayNameOf(int playerId) => NameOf(playerId);

        /// <summary>Персонаж участника. Нужен интерфейсу, чтобы понять, кто из них — эта машина.</summary>
        public PlayerController AvatarOf(int playerId) => FindAvatar(playerId);

        private PlayerController walker;
        private StuckDetector stuckDetector;
        private int walkerId = TurnQueue.NoPlayer;
        private double announceDeadline;
        private double turnDeadline;
        private bool turnArmed;
        private bool turnClosing;
        private int lastStep = -1;
        private int lastLane = -1;

        protected override void OnPlayersReady()
        {
            if (config == null)
            {
                Debug.LogError("MemoryRunMinigame: не назначен MemoryRunConfig — играть нечем", this);
                return;
            }

            playerIds.Clear();
            for (int i = 0; i < Players.Count; i++)
            {
                playerIds.Add(Players[i].Id);
            }

            state.Reset(playerIds);

            if (!HasAuthority)
            {
                return;
            }

            // Два независимых сида, и это не педантизм. Сид очереди в фазе 3
            // объявляется всем — иначе порядок ходов разъедется. Сид маршрута
            // не объявляется никому и никогда: по нему маршрут восстанавливается
            // целиком, то есть это тот же секрет, только в профиль.
            queue.Build(Players, Random.Range(int.MinValue, int.MaxValue));
            route.Generate(config, Random.Range(int.MinValue, int.MaxValue));
        }

        protected override void OnRoundStarted()
        {
            if (!HasAuthority)
            {
                return;
            }

            BeginTurn();
        }

        protected override void OnRoundEnded()
        {
            // Барьер и метка обязаны сниматься здесь. Персонаж переезжает между
            // сценами живым NetworkObject, и незакрытое исключение коллизии
            // уехало бы в хаб вместе с ним — у «Ангелов» так уехала
            // обездвиженность Водящего.
            StopAllCoroutines();
            gate?.CloseForAll();
            activeMarker?.Clear();

            walker = null;
            walkerId = TurnQueue.NoPlayer;
            turnArmed = false;
        }

        private void Update()
        {
            if (!RoundActive || !HasAuthority || walker == null)
            {
                return;
            }

            double now = NetworkClock.Now;

            if (!turnArmed)
            {
                if (now >= announceDeadline)
                {
                    turnArmed = true;
                    turnDeadline = now + config.TurnSeconds;
                }

                return;
            }

            if (now >= turnDeadline)
            {
                // Отсидеться нельзя: без этого один игрок морозит всю очередь
                // до конца общего таймера, а остальные семеро просто ждут.
                FinishTurn(TurnEnd.TimedOut);
                return;
            }

        }

        private void FixedUpdate()
        {
            if (!RoundActive || !HasAuthority || walker == null || turnClosing)
            {
                return;
            }

            Vector3 position = walker.transform.position;

            if (position.y < FallThreshold)
            {
                FinishTurn(TurnEnd.Fell);
                return;
            }

            if (!walker.IsGrounded)
            {
                return;
            }

            if (position.z >= config.ExitPadZ)
            {
                FinishTurn(TurnEnd.Reached);
                return;
            }

            // Сервер сам определяет, на какой плите игрок, — по позиции.
            // Клиент об этом не сообщает: приземление здесь единственное
            // действие, влияющее на исход, и верить в нём клиенту нельзя.
            if (!config.TryGetCell(position, out int step, out int lane))
            {
                return;
            }

            if (step == lastStep && lane == lastLane)
            {
                return;
            }

            lastStep = step;
            lastLane = lane;
            ResolveLanding(step, lane);
        }

        /// <summary>
        /// Единственное место во всей игре, которое спрашивает у маршрута,
        /// безопасна ли плита. В фазе 3 уходит за <c>IsServer</c> целиком.
        /// </summary>
        private void ResolveLanding(int step, int lane)
        {
            if (route.IsSafe(step, lane))
            {
                state.RegisterReach(walkerId, step, NetworkClock.Now);
                SafePlateProved?.Invoke(step, lane);
                return;
            }

            MinePlateProved?.Invoke(step, lane);
            Detonate(step, lane);
            FinishTurn(TurnEnd.Mine);
        }

        /// <summary>
        /// Взрыв под ногами. <b>Плита при этом не меняется вообще</b> — ни
        /// вмятины, ни копоти, ни смены материала. Именно на этом держится
        /// вся механика: останься след, и следующий пойдёт по следам, а не
        /// по памяти. Копоть — только на лице персонажа, это фаза арта.
        /// </summary>
        private void Detonate(int step, int lane)
        {
            Vector3 center = config.CellCenter(step, lane);
            Vector3 away = walker.transform.position - center;
            away.y = 0f;

            Vector3 direction = away.sqrMagnitude > 0.01f
                ? (away.normalized + Vector3.up * 1.6f).normalized
                : Vector3.up;

            walker.ApplyImpulse(direction * config.MineImpulse, KnockdownType.FlyBack);
        }

        /// <summary>
        /// Ход кончился — по любой из четырёх причин. Вторая точка смены
        /// важного состояния: считает смерть, выводит по лимиту, передаёт ход.
        /// </summary>
        private void FinishTurn(TurnEnd reason)
        {
            if (turnClosing)
            {
                return;
            }

            turnClosing = true;

            PlayerController finished = walker;
            int finishedId = walkerId;

            if (stuckDetector != null)
            {
                stuckDetector.PlayerUnstuck -= HandleWalkerUnstuck;
                stuckDetector = null;
            }

            if (logTurns)
            {
                state.TryGet(finishedId, out MemoryRunProgress before);
                Vector3 where = finished != null ? finished.transform.position : Vector3.zero;
                Debug.Log($"[Рейс] ход {NameOf(finishedId)} кончился: {reason}, " +
                          $"дальний шаг {before.BestStep}, смертей до этого {before.Deaths}, " +
                          $"место {where:F2}, последняя плита ш{lastStep}/п{lastLane}");
            }

            gate?.CloseForAll();
            activeMarker?.Clear();
            walker = null;
            turnArmed = false;

            if (reason == TurnEnd.Reached)
            {
                state.RegisterFinish(finishedId, NetworkClock.Now);
                queue.Retire(finishedId);
            }
            else if (reason == TurnEnd.Unstuck)
            {
                // Застревание — не провал игрока. Прогресс не теряется (он и так
                // рекорд), смерть не засчитывается, ход просто уходит дальше.
                queue.Advance();
            }
            else
            {
                bool exhausted = state.RegisterDeath(finishedId, config.DeathLimit);

                // Возврат идёт своим ходом и очередь не задерживает: следующий
                // начинает сразу, пока погибший ещё летит. Спека прямо требует
                // передавать ход в момент гибели.
                if (finished != null)
                {
                    StartCoroutine(ReturnToStart(finished, reason == TurnEnd.TimedOut));
                }

                if (exhausted)
                {
                    queue.Retire(finishedId);
                }
                else
                {
                    queue.Advance();
                }
            }

            turnClosing = false;

            if (IsRoundOver())
            {
                EndMinigame();
                return;
            }

            BeginTurn();
        }

        private void HandleWalkerUnstuck() => FinishTurn(TurnEnd.Unstuck);

        /// <summary>
        /// Начало хода: барьер открыт, метка зажглась, две секунды на то, чтобы
        /// игрок понял, что ход его, и только потом пошёл таймер.
        /// </summary>
        private void BeginTurn()
        {
            for (int skips = 0; skips < MaxTurnSkips; skips++)
            {
                walkerId = queue.CurrentPlayerId;
                if (walkerId == TurnQueue.NoPlayer)
                {
                    EndMinigame();
                    return;
                }

                walker = FindAvatar(walkerId);
                if (walker != null)
                {
                    break;
                }

                // Аватара нет — игрок уже не в матче. Вычёркиваем и берём
                // следующего, иначе очередь встанет на пустом месте.
                queue.Remove(walkerId);
            }

            if (walker == null)
            {
                EndMinigame();
                return;
            }

            lastStep = -1;
            lastLane = -1;
            turnArmed = false;
            announceDeadline = NetworkClock.Now + config.TurnAnnounceSeconds;

            gate?.OpenFor(walker);
            activeMarker?.SetTarget(walker.transform);

            // Страховка от застревания: без неё зажатый геометрией игрок
            // просто досиживает свой ход до таймера и получает смерть ни за что.
            stuckDetector = walker.GetComponent<StuckDetector>();
            if (stuckDetector != null)
            {
                stuckDetector.PlayerUnstuck += HandleWalkerUnstuck;
            }

        }

        /// <summary>
        /// Рагдолл отыгрывается, потом персонаж возвращается в стартовую зону.
        /// Следующая попытка начнётся снова с первого шага — накопленное
        /// в <see cref="MemoryRunState"/> при этом не сбрасывается: смерть
        /// стоит очереди и попытки, а не прогресса.
        /// </summary>
        private IEnumerator ReturnToStart(PlayerController player, bool silent)
        {
            if (!silent)
            {
                yield return new WaitForSeconds(config.RagdollSeconds);
            }

            if (player == null)
            {
                yield break;
            }

            player.RequestTeleport(StartZonePoint(player), Quaternion.identity);
        }

        /// <summary>
        /// Куда возвращать погибшего. Точка разведена по X от идентификатора,
        /// чтобы двое подряд не оказались друг в друге и не расталкивались
        /// физикой на глазах у всех.
        /// </summary>
        private Vector3 StartZonePoint(PlayerController player)
        {
            float spread = config.StartZoneSize * 0.3f;
            float x = Mathf.Repeat(player.GetInstanceID() * 0.618f, 2f) * spread - spread;
            float z = config.GateZ - config.StartZoneSize * 0.5f;
            return new Vector3(x, config.StartZoneLift + 0.5f, z);
        }

        /// <summary>
        /// Раунд кончился, когда ходить больше некому — или когда остался один
        /// ходящий, а все прочие уже дошли или выбыли. Гонять одного человека
        /// по уже раскрытому маршруту незачем.
        /// </summary>
        private bool IsRoundOver()
        {
            if (queue.ActiveCount == 0)
            {
                return true;
            }

            return queue.ActiveCount == 1 && playerIds.Count > 1;
        }

        protected override void CollectResults(MinigameResults results)
        {
            ranking.Fill(results, state.Records, queue);

            if (!logTurns)
            {
                return;
            }

            // Итоговая таблица в лог. Нужна приёмке: прогон читается из консоли
            // целиком, без единого обращения в play-режим — а любое обращение
            // ставит редактор на паузу и останавливает саму игру.
            var report = new System.Text.StringBuilder("[Рейс] итог: ");
            for (int i = 0; i < results.Entries.Count; i++)
            {
                MinigameResults.PlayerResult entry = results.Entries[i];
                state.TryGet(entry.PlayerId, out MemoryRunProgress p);
                report.Append(entry.Place).Append(". ").Append(NameOf(entry.PlayerId))
                      .Append(" — шаг ").Append(p.BestStep)
                      .Append(", смертей ").Append(p.Deaths)
                      .Append(p.Finished ? ", дошёл #" + p.ArrivalOrder : "")
                      .Append("; ");
            }

            Debug.Log(report.ToString());
        }

        private PlayerController FindAvatar(int playerId)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].Id == playerId)
                {
                    return Players[i].Avatar;
                }
            }

            return null;
        }

        private string NameOf(int playerId)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].Id == playerId)
                {
                    return Players[i].DisplayName;
                }
            }

            return "?";
        }

        /// <summary>
        /// Участник вышел из матча. Ход уходит следующему немедленно: в
        /// пошаговой игре уход того, чей сейчас ход, иначе подвешивает всех
        /// до конца общего таймера.
        /// </summary>
        public void HandlePlayerLeft(int playerId)
        {
            if (!HasAuthority)
            {
                return;
            }

            bool wasWalking = playerId == walkerId;
            queue.Remove(playerId);
            RemovePlayer(playerId);

            if (!wasWalking)
            {
                return;
            }

            gate?.CloseForAll();
            activeMarker?.Clear();
            walker = null;
            turnArmed = false;

            if (IsRoundOver())
            {
                EndMinigame();
                return;
            }

            BeginTurn();
        }
    }
}
