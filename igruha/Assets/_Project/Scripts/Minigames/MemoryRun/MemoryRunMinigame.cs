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
    /// <see cref="ResolveLanding"/>. Маршрут и оба сида живут приватными полями
    /// сервера и не уезжают никуда; перечень того, что действительно едет,
    /// снимается рефлексией в <c>MemoryRunTrafficAudit</c>.
    /// </summary>
    /// <remarks>
    /// <b>Сеть.</b> Важное состояние меняют ровно четыре метода —
    /// <see cref="ResolveLanding"/>, <see cref="FinishTurn"/>,
    /// <see cref="BeginTurn"/> и <see cref="CollectResults"/>, — и все они
    /// вызываются только под <see cref="MinigameControllerBase.HasAuthority"/>.
    /// Машина без авторитета не считает ничего: очередь, ход и прогресс
    /// приезжают готовыми через <see cref="MemoryRunNetwork"/> и попадают сюда
    /// тремя методами <c>ApplyNetwork*</c>.
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

        /// <summary>
        /// На сколько метров от настила игрок ещё считается стоящим на нём.
        ///
        /// Заменяет проверку земли у мотора, которой у сервера нет для чужих
        /// аватаров. Полметра прыжка выше этой полосы, а спуск на плиту при
        /// пятидесяти шагах физики проходит её за три-четыре кадра — так что
        /// приземление ловится, а пролёт над плитой мимо цели — нет.
        /// </summary>
        private const float SurfaceTolerance = 0.3f;

        /// <summary>
        /// Сколько ждать перед объявлением результатов, чтобы последняя
        /// публикация прогресса успела уйти тиком. При частоте тика 30 это
        /// семь рассылок подряд — с запасом на любую загрузку.
        /// </summary>
        private const float EndPublishGraceSeconds = 0.25f;

        /// <summary>Страховка от зацикливания, если у всех подряд не оказалось аватара.</summary>
        private const int MaxTurnSkips = 16;

        /// <summary>
        /// Как часто сервер проверяет, не вышел ли кто на плиты вне очереди.
        /// Каждый кадр незачем: барьер держит физикой, а это проверка на того,
        /// кто барьер обошёл.
        /// </summary>
        private const float GateCheckPeriod = 0.5f;

        /// <summary>
        /// Сколько секунд не трогать нарушителя после возврата. Перенос — это
        /// поручение владельцу, и до сервера новая позиция доедет не мгновенно;
        /// без выдержки сервер слал бы поручение каждую проверку.
        /// </summary>
        private const float GatePushBackCooldown = 1.5f;

        [Header("Ссылки")]
        [SerializeField] private MemoryRunConfig config;
        [SerializeField] private TurnGate gate;
        [Tooltip("Метка над тем, чей ход. Без неё зрители не разбирают, на какую плиту он встал")]
        [SerializeField] private ActivePlayerMarker activeMarker;

        [Tooltip("Писать в лог, чем кончился каждый ход. Нужно на прогонах: без разбивки по причинам смерти нельзя отличить «маршрут сложный» от «прыжок не долетает»")]
        [SerializeField] private bool logTurns;

        /// <summary>
        /// 🔴 Секрет игры. Приватное поле сервера, которое не сериализуется
        /// в инспекторе, не реплицируется и не отдаётся ни одним публичным
        /// членом. Сид генерации не хранится вовсе — по нему маршрут
        /// восстанавливается целиком.
        /// </summary>
        private readonly MemoryRunRoute route = new MemoryRunRoute();

        private readonly MemoryRunState state = new MemoryRunState();
        private readonly TurnQueue queue = new TurnQueue();
        private readonly MemoryRunRanking ranking = new MemoryRunRanking();
        private readonly List<int> playerIds = new List<int>(8);

        /// <summary>
        /// Кто сейчас летит обратно в стартовую зону. Барьер их не трогает:
        /// погибший полторы секунды отыгрывает рагдолл далеко за барьером, и
        /// без этого списка сервер вернул бы его на старт первой же проверкой,
        /// оборвав отлёт на полпути.
        /// </summary>
        private readonly HashSet<int> returning = new HashSet<int>();

        /// <summary>Когда нарушителя в последний раз возвращали за барьер.</summary>
        private readonly Dictionary<int, double> gatePushedAt = new Dictionary<int, double>(8);

        /// <summary>
        /// Имена участников на момент старта раунда.
        ///
        /// Ушедшего из состава вычёркивают, а место он получает — спека 10.2
        /// требует, чтобы он сохранял достигнутое. Без запомненного имени
        /// итоговая строка называла бы его «?», и разобрать таблицу прогона
        /// было бы нельзя.
        /// </summary>
        private readonly Dictionary<int, string> displayNames = new Dictionary<int, string>(8);

        /// <summary>
        /// Плита оказалась безопасной. <b>Утечкой не является:</b> это видели
        /// все восемь человек в зале, ровно на этом игра и построена. Событие
        /// нужно звуку, VFX и погонщику болванок.
        /// </summary>
        public event System.Action<int, int> SafePlateProved;

        /// <summary>
        /// Плита оказалась миной. Тоже общеизвестно: все слышали взрыв.
        ///
        /// Событие <b>серверное</b> — им пользуется погонщик болванок соло-прогона,
        /// и координаты плиты в нём есть. Всему, что должно сработать на каждой
        /// машине (VFX, звук), нужен <see cref="MineDetonated"/>.
        /// </summary>
        public event System.Action<int, int> MinePlateProved;

        /// <summary>
        /// Взрыв под ногами — на каждой машине матча, с местом взрыва.
        /// Точка подключения для VFX и звука фазы 4.
        ///
        /// Отдельно от <see cref="MinePlateProved"/> намеренно: там номер шага
        /// и полоса, то есть язык маршрута, и в сеть это не уезжает. Здесь
        /// точка в мире — ровно то, что и так видели все восемь человек.
        /// </summary>
        public event System.Action<Vector3> MineDetonated;

        /// <summary>Кто сейчас идёт. Общеизвестно — над ним горит метка.</summary>
        public PlayerController CurrentWalker => walker;

        /// <summary>Чей ход. <see cref="TurnQueue.NoPlayer"/>, когда ходить некому.</summary>
        public int CurrentWalkerId => walkerId;

        /// <summary>Порядок ходов целиком — интерфейсу показать, кто следующий.</summary>
        public IReadOnlyList<int> TurnOrder => queue.Order;

        /// <summary>Сколько секунд осталось у идущего. Ноль, пока идёт объявление хода.</summary>
        public float TurnSecondsLeft =>
            TurnArmed ? Mathf.Max(0f, (float)(turnDeadline - NetworkClock.Now)) : 0f;

        /// <summary>
        /// Идёт ли отсчёт хода. False — играет двухсекундное объявление.
        ///
        /// Считается, а не хранится: оба момента общие для всех машин, поэтому
        /// сервер и клиент отвечают одинаково без единого лишнего пакета.
        /// </summary>
        public bool TurnArmed =>
            walkerId != TurnQueue.NoPlayer && NetworkClock.Now >= announceDeadline;

        /// <summary>Размер состава. Сетевой половине — понять, что ростер доехал.</summary>
        public int RosterCount => Players.Count;

        /// <summary>Лимит попыток из конфига — интерфейсу для строки «3 / 10».</summary>
        public int DeathLimit => config != null ? config.DeathLimit : 0;

        /// <summary>Сколько раз погиб игрок. Своё показывает HUD, чужое не показывает никто.</summary>
        public int DeathsOf(int playerId) => state.DeathsOf(playerId);

        /// <summary>Имя участника по идентификатору.</summary>
        public string DisplayNameOf(int playerId) => NameOf(playerId);

        /// <summary>Персонаж участника. Нужен интерфейсу, чтобы понять, кто из них — эта машина.</summary>
        public PlayerController AvatarOf(int playerId) => FindAvatar(playerId);

        /// <summary>
        /// Ходит ли участник ещё — то есть стоит ли его называть в очереди.
        ///
        /// Считается из реплицированного прогресса, а не из очереди, нарочно:
        /// у клиента очереди с выбывшими нет, и ответ обязан совпасть с
        /// серверным до буквы, иначе host и client показывают разные строки
        /// «Дальше:». Число смертей наружу при этом не отдаётся — чужой
        /// счётчик попыток не показывает никто (спека 10).
        /// </summary>
        public bool IsStillWalking(int playerId)
        {
            if (!state.TryGet(playerId, out MemoryRunProgress progress) || progress.Finished)
            {
                return false;
            }

            if (config != null && progress.Deaths >= config.DeathLimit)
            {
                return false;
            }

            return FindAvatar(playerId) != null;
        }

        private MemoryRunNetwork network;
        private PlayerController walker;
        private StuckDetector stuckDetector;
        private int walkerId = TurnQueue.NoPlayer;
        private int turnNumber;
        private double announceDeadline;
        private double turnDeadline;
        private bool turnClosing;
        private double nextGateCheck;
        private int lastStep = -1;
        private int lastLane = -1;

        protected override void Awake()
        {
            base.Awake();
            network = GetComponent<MemoryRunNetwork>();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            ResultsReported += LogResults;
        }

        protected override void OnDisable()
        {
            ResultsReported -= LogResults;
            base.OnDisable();
        }

        protected override void OnPlayersReady()
        {
            if (config == null)
            {
                Debug.LogError("MemoryRunMinigame: не назначен MemoryRunConfig — играть нечем", this);
                return;
            }

            playerIds.Clear();
            gatePushedAt.Clear();
            displayNames.Clear();
            returning.Clear();
            turnNumber = 0;

            for (int i = 0; i < Players.Count; i++)
            {
                playerIds.Add(Players[i].Id);
                displayNames[Players[i].Id] = Players[i].DisplayName;

                // Заводим все ключи заранее: словарь потом только читается
                // и переписывается, то есть в раунде не аллоцирует.
                gatePushedAt[Players[i].Id] = 0d;
            }

            state.Reset(playerIds);

            if (!HasAuthority)
            {
                return;
            }

            // Два независимых сида, и оба остаются здесь. Порядок ходов
            // объявляется готовым списком, а не сидом тасования: список
            // сходится у всех всегда, а сид — только при точно совпадающем
            // составе. Сид маршрута не объявляется тем более: по нему маршрут
            // восстанавливается целиком, то есть это тот же секрет, только
            // в профиль.
            queue.Build(Players, Random.Range(int.MinValue, int.MaxValue));
            route.Generate(config, Random.Range(int.MinValue, int.MaxValue));

            network?.PublishTurnOrder(queue.Order);
            network?.PublishProgress(state.Records);
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
            returning.Clear();
            ClearTurn();

            // Ход снимается у всех, а не только у сервера: без этого метка
            // над ушедшим в результаты игроком осталась бы гореть на клиенте.
            network?.PublishTurn(TurnQueue.NoPlayer, turnNumber, 0d, 0d);
        }

        private void Update()
        {
            if (!RoundActive || !HasAuthority || walker == null)
            {
                return;
            }

            if (NetworkClock.Now >= turnDeadline)
            {
                // Отсидеться нельзя: без этого один игрок морозит всю очередь
                // до конца общего таймера, а остальные семеро просто ждут.
                FinishTurn(TurnEnd.TimedOut);
            }
        }

        /// <summary>
        /// Клиент нашёл персонажа идущего не сразу: ход мог приехать раньше,
        /// чем аватар заспавнился. Без этой попытки метка и барьер остались бы
        /// висеть в прошлом ходу до конца текущего.
        /// </summary>
        private void LateUpdate()
        {
            if (HasAuthority || walkerId == TurnQueue.NoPlayer || walker != null)
            {
                return;
            }

            BindWalkerVisuals();
        }

        private void FixedUpdate()
        {
            if (!RoundActive || !HasAuthority)
            {
                return;
            }

            EnforceGate();

            if (walker == null || turnClosing)
            {
                return;
            }

            Vector3 position = walker.transform.position;

            if (position.y < FallThreshold)
            {
                FinishTurn(TurnEnd.Fell);
                return;
            }

            // 🔴 Не IsGrounded, и это стоило первого же сетевого прогона.
            //
            // Проверку земли считает мотор персонажа, а мотор на чужих копиях
            // выключен — в том числе на сервере, у аватара любого клиента
            // (NetworkPlayerController.DisableLocalControl). Значит у сервера
            // IsGrounded клиента ЛОЖЕН всегда, и сервер не заметил бы ни одного
            // его приземления и ни одного прихода к двери. На стенде это
            // выглядело так: клиент своим ходом дошёл до выходной площадки
            // и стоял на ней, пока не истёк таймер, — «дальний шаг 0».
            //
            // По позиции это решается без мотора и одинаково для всех: верхняя
            // грань плиты и настил площадки лежат на одной отметке, и стоящий
            // держится у неё, а летящий — нет.
            if (Mathf.Abs(position.y - config.PlateSurfaceY) > SurfaceTolerance)
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
        /// безопасна ли плита. Зовётся только из <see cref="FixedUpdate"/>
        /// под авторитетом, то есть на сервере и нигде больше.
        ///
        /// <b>Безопасный шаг наружу не объявляется.</b> Это и был бы маршрут,
        /// выданный по одному шагу. Что шаг пройден, зрители видят сами —
        /// по тому, что взрыва не было.
        /// </summary>
        private void ResolveLanding(int step, int lane)
        {
            if (route.IsSafe(step, lane))
            {
                state.RegisterReach(walkerId, step, NetworkClock.Now);
                SafePlateProved?.Invoke(step, lane);
                network?.PublishProgress(state.Records);
                return;
            }

            MinePlateProved?.Invoke(step, lane);
            Detonate(config.CellCenter(step, lane));
            FinishTurn(TurnEnd.Mine);
        }

        /// <summary>
        /// Взрыв под ногами. <b>Плита при этом не меняется вообще</b> — ни
        /// вмятины, ни копоти, ни смены материала. Именно на этом держится
        /// вся механика: останься след, и следующий пойдёт по следам, а не
        /// по памяти. Копоть — только на лице персонажа, это фаза арта.
        /// </summary>
        /// <remarks>
        /// Импульс уходит через <c>ApplyWorldImpulse</c>, а не напрямую:
        /// позицией персонажа распоряжается машина его владельца, и сила,
        /// приложенная к серверной копии, была бы тут же перетёрта сетевым
        /// состоянием. Тот же путь у пружины, снаряда и зоны смерти.
        /// </remarks>
        private void Detonate(Vector3 center)
        {
            Vector3 away = walker.transform.position - center;
            away.y = 0f;

            Vector3 direction = away.sqrMagnitude > 0.01f
                ? (away.normalized + Vector3.up * 1.6f).normalized
                : Vector3.up;

            walker.ApplyWorldImpulse(direction * config.MineImpulse);

            // Взрыв — событие, а не состояние: его отыгрывает каждая машина
            // у себя. Уходит наружу ровно то, что и так видел весь зал.
            network?.AnnounceDetonation(center);
            MineDetonated?.Invoke(center);
        }

        /// <summary>
        /// Барьер очереди глазами сервера: кто оказался на плитах не в свой
        /// ход, тот возвращается в стартовую зону.
        ///
        /// Барьер держит физикой, но физику ходящего считает машина владельца,
        /// и снять у себя одно исключение коллизии — правка на одну строчку
        /// в подменённом клиенте. Значит право пройти обязан проверять сервер,
        /// а не стена. Информация в этой игре — общий ресурс: вышедший вне
        /// очереди либо столкнёт идущего до прыжка, либо разведает шаг,
        /// которого зрители не увидели.
        /// </summary>
        private void EnforceGate()
        {
            double now = NetworkClock.Now;
            if (now < nextGateCheck)
            {
                return;
            }

            nextGateCheck = now + GateCheckPeriod;

            for (int i = 0; i < Players.Count; i++)
            {
                SessionPlayer player = Players[i];
                if (player.Id == walkerId || returning.Contains(player.Id))
                {
                    continue;
                }

                // Дошедший стоит на выходной площадке по праву — она за плитами.
                if (state.TryGet(player.Id, out MemoryRunProgress progress) && progress.Finished)
                {
                    continue;
                }

                PlayerController avatar = player.Avatar;
                if (avatar == null || avatar.transform.position.z <= config.GateZ)
                {
                    continue;
                }

                if (gatePushedAt.TryGetValue(player.Id, out double pushedAt) &&
                    now - pushedAt < GatePushBackCooldown)
                {
                    continue;
                }

                gatePushedAt[player.Id] = now;
                avatar.RequestTeleport(StartZonePoint(avatar), Quaternion.identity);

                Debug.LogWarning($"[Рейс] {NameOf(player.Id)} оказался за барьером не в свой ход — " +
                                 "возвращён в стартовую зону сервером", this);
            }
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

            ClearTurn();

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
                    returning.Add(finishedId);
                    StartCoroutine(ReturnToStart(finishedId, finished, reason == TurnEnd.TimedOut));
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

            // Прогресс объявляется один раз на закрытый ход, а не по каждому
            // изменению внутри него: смерть, выбывание и приход к двери
            // случаются вместе.
            network?.PublishProgress(state.Records);

            if (IsRoundOver())
            {
                StartCoroutine(EndAfterProgressPublished());
                return;
            }

            BeginTurn();
        }

        /// <summary>
        /// Закрыть раунд не раньше, чем последняя публикация прогресса успеет
        /// уйти в сеть.
        ///
        /// Места едут отдельным <c>Rpc</c>, прогресс — списком состояния.
        /// <c>Rpc</c> уходит кадром, а состояние — <b>тиком</b>: при частоте
        /// тика 30 и трёхстах кадрах в секунду между двумя рассылками
        /// состояния помещается десяток кадров, и объявленные места обгоняют
        /// последнюю строку прогресса. На стенде это дало таблицу, где места
        /// у всех совпали, а последняя смерть и последний приход к двери
        /// у клиентов не показались: «дошёл #3» был только у хоста.
        ///
        /// Отсюда пауза, а не кадр: кадра при быстрой отрисовке не хватает
        /// даже на один тик. Стоит она ничего — раунд и так заканчивается
        /// экраном результатов на двенадцать секунд.
        /// </summary>
        private IEnumerator EndAfterProgressPublished()
        {
            yield return new WaitForSeconds(EndPublishGraceSeconds);
            EndMinigame();
        }

        private void HandleWalkerUnstuck() => FinishTurn(TurnEnd.Unstuck);

        /// <summary>
        /// Снять ход: барьер закрыт, метка погашена, ходящего нет.
        /// Одна точка на все четыре повода — конец хода, конец раунда,
        /// уход того, чей ход, и досрочное завершение.
        /// </summary>
        private void ClearTurn()
        {
            gate?.CloseForAll();
            activeMarker?.Clear();

            walker = null;
            walkerId = TurnQueue.NoPlayer;
            announceDeadline = 0d;
            turnDeadline = 0d;
        }

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
            turnNumber++;

            // Оба момента считаются здесь и объявляются разом. Раньше конец
            // хода вычислялся в момент, когда истекало объявление, — при
            // моменте вместо остатка так нельзя: клиенту пришлось бы досылать
            // второй пакет ровно тогда, когда пойдёт отсчёт.
            announceDeadline = NetworkClock.Now + config.TurnAnnounceSeconds;
            turnDeadline = announceDeadline + config.TurnSeconds;

            gate?.OpenFor(walker);
            activeMarker?.SetTarget(walker.transform);

            // Страховка от застревания: без неё зажатый геометрией игрок
            // просто досиживает свой ход до таймера и получает смерть ни за что.
            stuckDetector = walker.GetComponent<StuckDetector>();
            if (stuckDetector != null)
            {
                stuckDetector.PlayerUnstuck += HandleWalkerUnstuck;
            }

            network?.PublishTurn(walkerId, turnNumber, announceDeadline, turnDeadline);
        }

        /// <summary>
        /// Рагдолл отыгрывается, потом персонаж возвращается в стартовую зону.
        /// Следующая попытка начнётся снова с первого шага — накопленное
        /// в <see cref="MemoryRunState"/> при этом не сбрасывается: смерть
        /// стоит очереди и попытки, а не прогресса.
        /// </summary>
        private IEnumerator ReturnToStart(int playerId, PlayerController player, bool silent)
        {
            if (!silent)
            {
                yield return new WaitForSeconds(config.RagdollSeconds);
            }

            if (player != null)
            {
                player.RequestTeleport(StartZonePoint(player), Quaternion.identity);
            }

            // Перенос — поручение владельцу, и новая позиция доедет до сервера
            // не этим кадром. Без отметки барьер увидел бы возвращаемого ещё
            // за собой и отчитался бы о нарушителе на каждой смерти.
            gatePushedAt[playerId] = NetworkClock.Now;

            // Пометку снимаем в любом случае, в том числе когда персонажа уже
            // нет: иначе ушедший навсегда останется в списке возвращающихся,
            // и барьер перестанет его касаться на весь раунд.
            returning.Remove(playerId);
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
        }

        /// <summary>
        /// Итоговая таблица в лог — <b>на каждой машине</b>, ту самую, которую
        /// эта машина показывает.
        ///
        /// Пишется по событию шаблона, а не из <see cref="CollectResults"/>:
        /// места считает сервер, и лог из подсчёта был бы только у него. Чтобы
        /// приёмка могла сверить восемь таблиц построчно, а не «на глаз»,
        /// каждая машина обязана назвать свою.
        ///
        /// Прогон при этом читается из консоли целиком, без единого обращения
        /// в play-режим — а любое обращение ставит редактор на паузу и
        /// останавливает саму игру.
        /// </summary>
        private void LogResults(MinigameResults results)
        {
            if (!logTurns)
            {
                return;
            }

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

            // Ушедшего в составе уже нет, а место ему причитается по лучшему
            // результату (спека 10.2). Имя берём из того, что запомнили на старте.
            return displayNames.TryGetValue(playerId, out string remembered) ? remembered : "?";
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

            // Запись в состоянии остаётся: спека требует, чтобы ушедший
            // сохранял достигнутое и получал место по лучшему результату.
            // Из списков барьера убираем — за ним больше некому ходить.
            returning.Remove(playerId);
            gatePushedAt.Remove(playerId);

            if (wasWalking)
            {
                if (stuckDetector != null)
                {
                    stuckDetector.PlayerUnstuck -= HandleWalkerUnstuck;
                    stuckDetector = null;
                }

                ClearTurn();
            }

            // Проверять надо и когда ушёл ожидающий: если он был предпоследним,
            // гонять оставшегося по уже раскрытому маршруту незачем — спека
            // требует завершить раунд и отдать ему следующее свободное место.
            if (IsRoundOver())
            {
                EndMinigame();
                return;
            }

            if (!wasWalking)
            {
                return;
            }

            // Ход уходит следующему немедленно и в этом же кадре. Всё, что
            // тут промедлит, встанет всей очередью до конца общего таймера.
            BeginTurn();
        }

        // ========== ПРИЁМ СЕТЕВОГО СОСТОЯНИЯ ==========

        /// <summary>
        /// Порядок ходов, объявленный сервером. Очередь общеизвестна — её
        /// показывает строка состояния, и она же нужна, чтобы понять, кто
        /// следующий.
        /// </summary>
        public void ApplyNetworkTurnOrder(IReadOnlyList<int> order)
        {
            if (HasAuthority)
            {
                return;
            }

            queue.ApplyOrder(order);
        }

        /// <summary>
        /// Чей ход и до какого момента. Оба момента — по общим часам, поэтому
        /// остаток на экране клиента сходится с серверным без поправки на пинг.
        /// </summary>
        public void ApplyNetworkTurn(int netWalkerId, double armTime, double deadline)
        {
            if (HasAuthority)
            {
                return;
            }

            walkerId = netWalkerId;
            announceDeadline = armTime;
            turnDeadline = deadline;

            BindWalkerVisuals();
        }

        /// <summary>Строка прогресса, посчитанная сервером.</summary>
        public void ApplyNetworkProgress(in MemoryRunProgress record)
        {
            if (HasAuthority)
            {
                return;
            }

            state.ApplyReplicated(record);
        }

        /// <summary>
        /// Взрыв, объявленный сервером. Отыгрывается на каждой машине: сам
        /// отлёт везёт владелец персонажа, а VFX и звук фазы 4 приедут сюда.
        /// </summary>
        public void ApplyDetonation(Vector3 center)
        {
            if (HasAuthority)
            {
                return;
            }

            MineDetonated?.Invoke(center);
        }

        /// <summary>
        /// Показать ход: метка над идущим и открытый ему барьер.
        ///
        /// Барьер открывается на каждой машине, а не только на серверной:
        /// движение персонажа считает его владелец, и стена, не снятая
        /// у него, остановила бы идущего у себя — а сервер продолжал бы
        /// возвращать его туда, где он по своей картинке уже прошёл.
        /// </summary>
        private void BindWalkerVisuals()
        {
            walker = walkerId != TurnQueue.NoPlayer ? FindAvatar(walkerId) : null;

            if (walker == null)
            {
                gate?.CloseForAll();
                activeMarker?.Clear();
                return;
            }

            gate?.OpenFor(walker);
            activeMarker?.SetTarget(walker.transform);
        }
    }
}
