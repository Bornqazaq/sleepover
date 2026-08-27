using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Болванка соло-прогона: берёт тару со штабеля своей команды, тащит её к
    /// своему баку и идёт за следующей.
    ///
    /// Без неё фазу каркаса не принять вовсе. Вся механика игры — про
    /// <b>рассогласование между несущими</b>, а несущий в одиночном прогоне
    /// ровно один: ни крена от рывка партнёра, ни срыва ручки, ни тарана между
    /// командами проверить нечем. Одному человеку двумя персонажами не
    /// поуправлять, а сети на фазе каркаса ещё нет.
    ///
    /// Путь только человеческий: <c>DriveMove</c> у ходьбы и
    /// <c>DriveInteract</c> у кнопки E. Короткого пути в обход правил нет —
    /// иначе проверка перестала бы проверять то, что делает живой игрок.
    ///
    /// Это отладочный стенд, а не ИИ: болванка не саботирует, не выбирает
    /// момент и не бережёт воду. В сетевой катке не ставится вовсе.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class CarryItemDebugBot : MonoBehaviour
    {
        /// <summary>Что болванка делает прямо сейчас.</summary>
        private enum Step
        {
            /// <summary>Идёт к штабелю и держит там E.</summary>
            ToStack,
            /// <summary>Идёт к выданной таре и берётся за ручку.</summary>
            ToBottle,
            /// <summary>Тащит тару к своему баку.</summary>
            ToTank
        }

        /// <summary>
        /// С какого зазора <b>до коллайдера</b> цели болванка жмёт E, м.
        ///
        /// Меряется до поверхности, а не до корня объекта: у ящика штабеля
        /// сторона 1.8 м, и мерка от центра означала бы, что болванка,
        /// упёршаяся в него лбом, до цели «не дотягивается».
        ///
        /// Меньше радиуса поиска <c>PlayerInteractor</c> (1.8) с запасом:
        /// нажатие на самой границе отбивается проверкой дистанции, и болванка
        /// залипала бы у цели, жмя кнопку впустую.
        /// </summary>
        private const float InteractRange = 1.2f;

        /// <summary>Сколько ждать между попытками взяться, с. Без паузы E жмётся каждый кадр и берёт-отпускает по кругу.</summary>
        private const float GrabRetryDelay = 0.6f;

        /// <summary>
        /// На сколько метров болванка забегает вперёд своей стоянки в сторону
        /// бака. Ровно на стоянке она стояла бы на месте: связь не натянута —
        /// тара не едет, — а это и есть «несу».
        ///
        /// Не меньше щупа обхода (1.8 м) с запасом. Короткая цель отключает
        /// обход препятствий целиком: <c>DebugPlayerBot</c> щупает не дальше
        /// цели, и с целью в полуметре болванка упирается в завал и стоит там
        /// до конца раунда — замерено на первом же прогоне.
        ///
        /// Уводить болванку от стоянки это не даёт: связь всё равно ограничивает
        /// её уход наружу, и натяжение садится на своё равновесие.
        /// </summary>
        private const float TankLead = 3f;

        /// <summary>
        /// Какую долю предела связи болванка считает комфортным натяжением.
        /// Дальше она перестаёт тянуть и ждёт тару: связь у нас упругая, и
        /// натянутая до предела означает крен, а не скорость.
        /// </summary>
        private const float ComfortFraction = 0.2f;

        private PlayerController motor;
        private PlayerInputReader reader;
        private DebugPlayerBot walker;
        private CarryItemMinigame game;
        private TeamSide team = TeamSide.None;

        private Step step = Step.ToStack;
        private float grabCooldown;

        /// <summary>Коллайдеры целей. Кэшируются при смене цели: искать их каждый кадр незачем.</summary>
        private Collider stackCollider;
        private Collider bottleCollider;
        private WaterBottle knownBottle;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            reader = GetComponent<PlayerInputReader>();
        }

        private void OnDisable()
        {
            // Снятая болванка обязана бросить и ход, и кнопку: иначе она уходит
            // из-под управления с зажатым E и держит штабель до конца раунда.
            reader?.DriveMove(Vector2.zero);
            reader?.DriveInteractHold(false);

            if (walker != null)
            {
                walker.Stop();
                walker.enabled = false;
            }
        }

        /// <summary>Подключить к раунду. Слои препятствий задаёт мини-игра: у каждой арены они свои.</summary>
        public void Configure(CarryItemMinigame minigame, TeamSide side, LayerMask obstacles)
        {
            game = minigame;
            team = side;
            step = Step.ToStack;
            grabCooldown = 0f;

            if (walker == null && !TryGetComponent(out walker))
            {
                walker = gameObject.AddComponent<DebugPlayerBot>();
            }

            walker.enabled = true;
            walker.Configure(obstacles);
        }

        private void Update()
        {
            if (game == null || reader == null || walker == null)
            {
                return;
            }

            // Управление на обучалке отнимают только у живых игроков: у
            // болванки ридер и так не «локально управляемый», и без этой
            // проверки она уходит в ходку, пока остальные читают правила.
            if (game.Phase != MinigamePhase.Round)
            {
                reader.DriveInteractHold(false);
                walker.Stop();
                return;
            }

            grabCooldown = Mathf.Max(0f, grabCooldown - Time.deltaTime);

            BottleStack stack = game.StackOf(team);
            WaterTank tank = game.TankOf(team);
            if (stack == null || tank == null)
            {
                return;
            }

            if (stackCollider == null)
            {
                stackCollider = stack.GetComponentInChildren<Collider>();
            }

            WaterBottle bottle = stack.LiveBottle;
            if (bottle != knownBottle)
            {
                knownBottle = bottle;
                bottleCollider = bottle != null ? bottle.GetComponentInChildren<Collider>() : null;
            }

            // Нокдаун и заморозка болванку не касаются: она просто перестаёт
            // жать. Держать E под ними нельзя — штабель отсчитает выдачу
            // лежачему.
            if (motor.IsKnockedDown || motor.MovementLocked || reader.Suspended)
            {
                reader.DriveInteractHold(false);
                walker.Stop();
                return;
            }

            step = ChooseStep(bottle);

            switch (step)
            {
                case Step.ToStack:
                    RunToStack(stack);
                    break;
                case Step.ToBottle:
                    RunToBottle(bottle);
                    break;
                case Step.ToTank:
                    RunToTank(tank);
                    break;
            }
        }

        private Step ChooseStep(WaterBottle bottle)
        {
            if (bottle == null || bottle.IsGone)
            {
                return Step.ToStack;
            }

            return bottle.Carry.IsCarriedBy(motor) ? Step.ToTank : Step.ToBottle;
        }

        /// <summary>У штабеля держим E: взятие — удержание, а не нажатие.</summary>
        private void RunToStack(BottleStack stack)
        {
            bool close = IsNear(stackCollider, stack.transform.position, InteractRange);
            reader.DriveInteractHold(close);

            if (close)
            {
                walker.Stop();
                return;
            }

            Steer(stack.transform.position);
        }

        /// <summary>
        /// Идти к цели по маршруту команды: доска, горлышко, доска. Обход
        /// завалов и пропастей болванке не по зубам, поэтому путь ей задаётся,
        /// а не ищется. Путевую точку проходим не останавливаясь.
        /// </summary>
        private void Steer(Vector3 destination)
        {
            Vector3 waypoint = game.NextWaypoint(team, transform.position, destination, out bool isFinal);
            walker.SetTarget(waypoint, isFinal);
        }

        /// <summary>
        /// К таре подходим не вплотную, а на расстояние своей стоянки: встав
        /// внутрь бутыли, болванка упёрлась бы в её коллайдер и сработал бы
        /// детектор застревания.
        /// </summary>
        private void RunToBottle(WaterBottle bottle)
        {
            reader.DriveInteractHold(false);

            Vector3 bottlePosition = bottle.transform.position;
            Vector3 station = NearestFreeStation(bottle);

            if (!IsNear(bottleCollider, bottlePosition, InteractRange))
            {
                Steer(station);
                return;
            }

            walker.Stop();

            if (grabCooldown > 0f)
            {
                return;
            }

            grabCooldown = GrabRetryDelay;
            reader.DriveInteract();
        }

        /// <summary>
        /// С тарой в руках болванка идёт не в бак напрямую, а держит свою
        /// стоянку, сдвинутую в сторону бака.
        ///
        /// Разница принципиальная. Идя прямо в бак, оба несущих обгоняют свои
        /// стоянки — тара едет медленнее их, — натяжения складываются в одну
        /// сторону, и бутыль клюёт вперёд весь путь. Держась стоянки, болванка
        /// тянет ровно столько, сколько нужно, чтобы тара ехала.
        /// </summary>
        private void RunToTank(WaterTank tank)
        {
            reader.DriveInteractHold(false);

            int slot = SlotOf(knownBottle);
            if (slot < 0)
            {
                Steer(tank.transform.position);
                return;
            }

            // Ведём тару к следующей точке маршрута, а не прямо в бак: сначала
            // доска, потом горлышко, потом вторая доска.
            Vector3 goal = game.NextWaypoint(team, knownBottle.transform.position, tank.transform.position, out _);

            Vector3 station = knownBottle.Carry.StationOf(slot);
            Vector3 toGoal = goal - knownBottle.transform.position;
            toGoal.y = 0f;

            // Забегаем вперёд, только пока связь не натянута. Натянулась —
            // возвращаемся на стоянку и даём таре себя догнать.
            //
            // Без этого болванка тянет всё время, связь садится на свой предел,
            // и на четырёх ручках бутыль едет с креном 75° весь путь: четыре
            // натяжения по полтора метра в одну сторону. Живой игрок ведёт
            // себя так же, если не отпускает «вперёд», — и наказывается тем же
            // креном. Разница в том, что он видит бутыль, а болванка нет.
            float comfort = knownBottle.Carry.Settings.breakDistance * ComfortFraction;
            bool slack = knownBottle.Carry.StretchOf(slot) < comfort;

            if (slack && toGoal.sqrMagnitude > 0.0001f)
            {
                station += toGoal.normalized * TankLead;
            }

            walker.SetTarget(station, false);
        }

        /// <summary>
        /// Стоянка ближайшей свободной ручки. Идти к самой таре нельзя: болванка
        /// встанет в её коллайдер и упрётся, а взявшись, окажется на стоянке
        /// с другой стороны — связь сразу за пределом.
        /// </summary>
        private Vector3 NearestFreeStation(WaterBottle bottle)
        {
            Vector3 best = bottle.transform.position;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < bottle.Carry.HandleCount; i++)
            {
                if (bottle.Carry.CarrierAt(i) != null)
                {
                    continue;
                }

                Vector3 station = bottle.Carry.StationOf(i);
                float sqr = (station - transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = station;
                }
            }

            return best;
        }

        private int SlotOf(WaterBottle bottle)
        {
            if (bottle == null)
            {
                return -1;
            }

            for (int i = 0; i < bottle.Carry.HandleCount; i++)
            {
                if (bottle.Carry.CarrierAt(i) == motor)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Зазор до поверхности цели не больше <paramref name="range"/>.
        /// Без коллайдера меряем до корня — запасной путь, а не рабочий.
        /// </summary>
        private bool IsNear(Collider target, Vector3 fallback, float range)
        {
            Vector3 origin = transform.position;
            Vector3 point = target != null ? target.ClosestPoint(origin) : fallback;

            Vector3 offset = point - origin;
            offset.y = 0f;
            return offset.sqrMagnitude <= range * range;
        }
    }
}
