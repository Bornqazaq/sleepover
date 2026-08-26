using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.MemoryRun
{
    /// <summary>
    /// Погонщик болванок для одиночного прогона: ведёт того, чей сейчас ход,
    /// если этой машиной он не управляется.
    ///
    /// Без него прогон на восьмерых не состоится вовсе. В «Экзамене» болванку
    /// достаточно водить ногами, здесь этого мало: ход кончается <b>прыжком</b>,
    /// и семеро неподвижных манекенов просто досидели бы каждый свой ход до
    /// таймера. Ни одного приземления, ни одной раскрытой плиты, очередь
    /// провернулась бы вхолостую.
    /// </summary>
    /// <remarks>
    /// <b>Память общая на всех болванок — и это правильно.</b> В настоящей игре
    /// каждый шаг видят все восемь человек сразу; отдельная память на болванку
    /// эмулировала бы игроков, которые не смотрят на арену.
    ///
    /// Путь только человеческий: <c>DriveMove</c> и <c>DriveJump</c> — те же
    /// вызовы, которыми ходит живой игрок. Короткого пути в обход правил нет.
    /// </remarks>
    public sealed class MemoryRunDebugBot : MonoBehaviour
    {
        /// <summary>Насколько близко к дальнему краю плиты болванка отталкивается.</summary>
        ///
        /// Узкое намеренно. При широком окне отталкивание срабатывает через
        /// полметра после начала разбега — то есть разгон отменяется тем же
        /// условием, ради которого он затевался, и болванка снова прыгает
        /// с места. Так она недолетала до следующей плиты 42 сантиметра.
        private const float TakeoffWindow = 0.7f;

        /// <summary>С какого расстояния считаем, что болванка уже встала на старт разбега.</summary>
        private const float RunStartTolerance = 0.9f;

        /// <summary>
        /// Дальность прыжка на полной скорости, м. Считается по CharacterConfig
        /// построителем арены и совпадает с 4.70; здесь нужна как масштаб.
        ///
        /// Разгоняться всегда до предела нельзя: первая пропасть — от стартовой
        /// зоны до нулевого ряда — короткая, и болванка на полной скорости
        /// перелетала первую плиту целиком, ни разу на неё не встав.
        /// </summary>
        /// 4.70 — теоретический предел по CharacterConfig; на деле связка
        /// «разбег по плите + прыжок» даёт около 3.2, и считать надо по факту,
        /// а не по формуле.
        private const float MaxJumpReach = 3.2f;

        /// <summary>Ниже этой доли газа не опускаемся: иначе разбега не хватает даже на своей плите.</summary>
        private const float MinThrottle = 0.4f;

        /// <summary>Во сколько раз перепад высоты добавляет дальности прыжку вниз.</summary>
        private const float DropReachBonus = 1.4f;

        /// <summary>Порог по X, ниже которого болванка считает, что уже встала на нужную полосу.</summary>
        private const float LaneAlignTolerance = 0.35f;

        /// <summary>На сколько не доходить до края плиты — своей при отталкивании и чужой при прицеливании.</summary>
        private const float EdgeInset = 0.35f;

        [SerializeField] private MemoryRunMinigame game;
        [SerializeField] private MemoryRunConfig config;
        [Tooltip("Писать в лог каждое отталкивание: скорость разгона, полоса и цель. Нужно, когда болванки не долетают")]
        [SerializeField] private bool logJumps;

        private bool[,] provedSafe;
        private bool[,] provedMine;
        private int plannedStep = -1;

        /// <summary>Разгон на текущей плите уже начат с нужной точки.</summary>
        private bool runUpDone;
        private int plannedLane = -1;
        private bool networkNoticed;
        private PlayerController lastWalker;

        /// <summary>Курс, с которым оттолкнулись. В полёте держим только его.</summary>
        private Vector3 airCourse = Vector3.forward;

        /// <summary>
        /// Сколько метров болванка реально пролетает при полном газе.
        ///
        /// <b>Замеряется на ходу, а не берётся из формулы.</b> Формула по
        /// CharacterConfig даёт 4.70 м, на деле связка «разбег по плите +
        /// прыжок» выходит около трёх с небольшим — разница копится из разгона,
        /// доворота и высоты приземления. Подбирать эту константу руками —
        /// значит перекалибровывать её при каждой правке физики персонажа;
        /// болванка калибрует себя сама за несколько прыжков.
        /// </summary>
        private float measuredReach = 3.6f;

        private bool inFlight;
        private Vector3 takeoffPoint;
        private float takeoffThrottle = 1f;

        /// <summary>
        /// Кем болванке позволено рулить.
        ///
        /// <b>Не по флагу <c>enabled</c> у ридера.</b> Выключенный ридер — признак
        /// манекена в редакторе, но в автопрогоне на восьми процессах персонаж
        /// у каждой машины <b>свой</b>, то есть локально управляемый и с живым
        /// ридером: там болванку взводит <c>--bot</c> через <c>EngageAutopilot</c>.
        /// Ключись на <c>enabled</c> — и весь стенд молча простоит все ходы,
        /// а выглядеть это будет как сломанная игра, а не сломанный признак.
        /// </summary>
        private static bool IsDriveable(PlayerInputReader reader) =>
            reader.Autopilot || !reader.LocallyControlled;

        private void OnEnable()
        {
            if (game == null || config == null)
            {
                enabled = false;
                return;
            }

            provedSafe = new bool[config.Steps, MemoryRunConfig.LaneCount];
            provedMine = new bool[config.Steps, MemoryRunConfig.LaneCount];

            game.SafePlateProved += OnSafeProved;
            game.MinePlateProved += OnMineProved;
        }

        private void OnDisable()
        {
            if (game == null)
            {
                return;
            }

            game.SafePlateProved -= OnSafeProved;
            game.MinePlateProved -= OnMineProved;
        }

        private void Update()
        {
            // 🔴 В сетевой сессии болванок не бывает вовсе: у каждой копии
            // персонажа есть владелец-человек. А признак болванки здесь —
            // «этой машиной не управляется», и у СЕРВЕРА так выглядит любой
            // клиент. Без этой проверки погонщик прыгал бы за живого человека,
            // который стоит и вспоминает маршрут, — то есть решал бы за него
            // единственное действие, которое в этой игре вообще есть.
            // Тот же класс ошибок ловили в «Экзамене» (STATE 3.17)
            // и в «Верю / не верю» (STATE 3.18).
            if (WorldAuthority.IsNetworkSession)
            {
                if (!networkNoticed)
                {
                    networkNoticed = true;
                    Debug.Log($"{name}: погонщик болванок выключен — идёт сетевая катка, " +
                              "здесь за каждого играет живой человек", this);
                }

                enabled = false;
                return;
            }

            PlayerController walker = game.CurrentWalker;
            if (walker == null)
            {
                plannedStep = -1;
                runUpDone = false;
                lastWalker = null;
                return;
            }

            PlayerInputReader reader = walker.GetComponent<PlayerInputReader>();
            if (reader == null || !IsDriveable(reader))
            {
                // Этой машиной управляет человек — не мешаем.
                return;
            }

            // План живёт ровно один ход. Без сброса следующая болванка
            // унаследует чужой выбор полосы и пойдёт не туда, куда решала сама.
            if (!ReferenceEquals(walker, lastWalker))
            {
                lastWalker = walker;
                plannedStep = -1;
                runUpDone = false;
            }

            Drive(walker, reader);
        }

        private void Drive(PlayerController walker, PlayerInputReader reader)
        {
            if (walker.IsKnockedDown)
            {
                reader.DriveMove(Vector2.zero);
                return;
            }

            Vector3 position = walker.transform.position;
            bool onPlate = config.TryGetCell(position, out int step, out int lane);

            int targetStep = onPlate ? step + 1 : 0;
            if (!onPlate && position.z > config.ChainStartZ)
            {
                // Уже за цепочкой — значит на выходной площадке, идти больше некуда.
                reader.DriveMove(Vector2.zero);
                return;
            }

            if (targetStep >= config.Steps)
            {
                // Последний ряд пройден: остаётся дойти до двери.
                DriveTowards(walker, reader, new Vector3(0f, position.y, config.ExitPadZ + 1.5f), false);
                return;
            }

            if (targetStep != plannedStep)
            {
                plannedStep = targetStep;
                runUpDone = false;
                plannedLane = ChooseLane(targetStep, onPlate ? lane : -1);
            }

            float half = config.PlateSize * 0.5f;

            // Полоса разгона: та же, с которой отталкиваемся, прижатая к краю
            // СВОЕЙ плиты со стороны цели. Центр соседней полосы лежит за
            // пределами плиты, и выходить на него по земле нельзя.
            float targetX = config.LaneX(plannedLane);
            float runwayX = onPlate
                ? Mathf.Clamp(targetX, config.LaneX(lane) - half + EdgeInset, config.LaneX(lane) + half - EdgeInset)
                : targetX;

            // 🔴 Целимся в БЛИЖНИЙ УГОЛ нужной плиты, а не в её центр.
            //
            // Разница не косметическая. С края своей плиты до центра соседней
            // полосы 3.97 м, и всю боковую составляющую приходится добирать
            // доворотом в воздухе — а доворот срезает скорость, и болванка
            // пролетала 1.95 м вместо четырёх. До ближнего угла той же плиты
            // 2.44 м почти по прямой. Человек прыгает именно так: в угол,
            // а не в середину.
            // Целимся в ЦЕНТР плиты: у центра запас 1.44 м во все стороны,
            // у ближнего края — ноль в одну из них. Промах болванки на
            // полметра при прицеле в край означает пропасть, при прицеле
            // в центр — просто некрасивое приземление.
            Vector3 aim = new Vector3(targetX, position.y, config.StepZ(plannedStep));

            if (!walker.IsGrounded)
            {
                inFlight = true;

                // 🔴 В воздухе курс не меняем вовсе — держим тот, с которым
                // оттолкнулись.
                //
                // Доворот в полёте выглядит естественным, но обходится дорого:
                // управление в воздухе не добавляет скорость, а разворачивает
                // уже имеющуюся, и дальность прыжка падает с 3.3 м до 1.9 —
                // болванка садилась в пропасть в семидесяти сантиметрах от
                // края нужной плиты. Куда лететь, решают ноги на разгоне.
                reader.DriveMove(walker.WorldToMoveInput(airCourse));
                return;
            }

            float takeoffZ = onPlate
                ? config.StepZ(step) + half - EdgeInset
                : config.GateZ - EdgeInset;

            // 🔴 Разгон идёт ПО ДИАГОНАЛИ через всю свою плиту, от дальнего
            // от цели угла к ближнему. Так к моменту отталкивания скорость уже
            // направлена в сторону цели, и доворачивать в воздухе почти нечего.
            //
            // Прямой разгон вперёд с доворотом в воздухе даёт ровно половину
            // нужного бокового сноса: болванка садилась в щель между полосами,
            // в 36 см от края нужной плиты, раз за разом. Управление в воздухе
            // это 0.55 от наземного — добрать им весь боковой снос нельзя,
            // его надо набирать ногами. Человек так и делает.
            float runStartX = onPlate
                ? Mathf.Clamp(2f * config.LaneX(lane) - runwayX,
                    config.LaneX(lane) - half + EdgeInset, config.LaneX(lane) + half - EdgeInset)
                : runwayX;

            float runStartZ = onPlate ? config.StepZ(step) - half + EdgeInset : takeoffZ - 3f;

            // Разгон обязателен, даже если приземлился уже у дальнего края.
            //
            // Без этой отметки болванка вела себя так: садилась в глубине плиты,
            // сразу оказывалась «у края» и прыгала с места, без бокового сноса
            // и без скорости. В логе это выглядело как упрямое хождение по одной
            // и той же известной мине — она просто не могла с неё свернуть.
            if (!runUpDone)
            {
                Vector3 runStart = new Vector3(runStartX, position.y, runStartZ);
                bool atRunStart = Mathf.Abs(position.x - runStartX) < LaneAlignTolerance &&
                                  position.z < runStartZ + RunStartTolerance;

                if (!atRunStart)
                {
                    DriveTowards(walker, reader, runStart, false);
                    return;
                }

                runUpDone = true;
            }

            // Курс держим сквозь точку отталкивания, а не в неё: целься в саму
            // точку — и болванка начнёт тормозить, подходя к ней.
            Vector3 takeoff = new Vector3(runwayX, position.y, takeoffZ);
            Vector3 through = takeoff + (takeoff - new Vector3(runStartX, position.y, runStartZ)).normalized * 4f;

            bool atEdge = position.z > takeoffZ - TakeoffWindow;

            Vector3 course = through - position;
            course.y = 0f;
            airCourse = course.normalized;

            if (atEdge && logJumps)
            {
                Debug.Log($"[Бот] отталкивание {walker.name}: pos={position:F2} скорость={walker.NormalizedSpeed:F2} " +
                          $"разгон {runStartX:F2}->{runwayX:F2} цель={aim:F2} шаг {step}->{plannedStep} полоса {lane}->{plannedLane}");
            }

            if (inFlight)
            {
                // Приземлились: замеряем, сколько на самом деле пролетели,
                // и подтягиваем оценку. Скользящее среднее, а не последнее
                // значение: один кривой прыжок не должен уводить калибровку.
                inFlight = false;
                float flown = Vector3.Distance(
                    new Vector3(takeoffPoint.x, 0f, takeoffPoint.z),
                    new Vector3(position.x, 0f, position.z));

                if (takeoffThrottle > 0.01f && flown > 0.2f)
                {
                    measuredReach = Mathf.Lerp(measuredReach, flown / takeoffThrottle, 0.35f);
                    measuredReach = Mathf.Clamp(measuredReach, 1.5f, 8f);
                }
            }

            // Газ по длине прыжка, а не «всегда полный»: дальность растёт со
            // скоростью почти линейно, значит и скорость берём по нужде.
            // Перелёт здесь так же смертелен, как недолёт — первую плиту
            // болванка на полном газу перепрыгивала целиком.
            float needed = Vector3.Distance(new Vector3(position.x, 0f, position.z), new Vector3(aim.x, 0f, aim.z));

            // Со стартовой зоны прыжок идёт вниз, и высота добавляет дальности.
            if (!onPlate)
            {
                needed -= config.StartZoneLift * DropReachBonus;
            }

            float throttle = Mathf.Clamp(needed / measuredReach, MinThrottle, 1f);

            if (atEdge)
            {
                takeoffPoint = position;
                takeoffThrottle = throttle;
            }

            DriveTowards(walker, reader, through, atEdge, throttle);
        }

        private void DriveTowards(PlayerController walker, PlayerInputReader reader, Vector3 target, bool jump,
            float throttle = 1f)
        {
            Vector3 course = target - walker.transform.position;
            course.y = 0f;

            reader.DriveMove(walker.WorldToMoveInput(course.normalized) * throttle);

            if (jump)
            {
                reader.DriveJump();
            }
        }

        /// <summary>
        /// Куда прыгать. Болванка играет по тем же правилам, что человек:
        /// знает — идёт наверняка, не знает — выбирает из того, что ещё
        /// не подорвалось, и только среди достижимых полос.
        /// </summary>
        private int ChooseLane(int step, int fromLane)
        {
            for (int lane = 0; lane < MemoryRunConfig.LaneCount; lane++)
            {
                if (provedSafe[step, lane] && Reachable(fromLane, lane))
                {
                    return lane;
                }
            }

            int candidates = 0;
            for (int lane = 0; lane < MemoryRunConfig.LaneCount; lane++)
            {
                if (!provedMine[step, lane] && Reachable(fromLane, lane))
                {
                    candidates++;
                }
            }

            if (candidates == 0)
            {
                // Все достижимые полосы уже известны как мины — такого при
                // корректной генерации не бывает, но упереться в тупик молча
                // хуже, чем шагнуть наугад.
                return Mathf.Clamp(fromLane < 0 ? MemoryRunRoute.Center : fromLane,
                    0, MemoryRunConfig.LaneCount - 1);
            }

            int pick = Random.Range(0, candidates);
            for (int lane = 0; lane < MemoryRunConfig.LaneCount; lane++)
            {
                if (provedMine[step, lane] || !Reachable(fromLane, lane))
                {
                    continue;
                }

                if (pick-- == 0)
                {
                    return lane;
                }
            }

            return MemoryRunRoute.Center;
        }

        /// <summary>Со старта достижима любая полоса, с плиты — только соседняя.</summary>
        private static bool Reachable(int fromLane, int toLane) =>
            fromLane < 0 || Mathf.Abs(toLane - fromLane) <= 1;

        private void OnSafeProved(int step, int lane)
        {
            if (step >= 0 && step < config.Steps)
            {
                provedSafe[step, lane] = true;
            }
        }

        private void OnMineProved(int step, int lane)
        {
            if (step >= 0 && step < config.Steps)
            {
                provedMine[step, lane] = true;
                plannedStep = -1;
                runUpDone = false;
            }
        }
    }
}
