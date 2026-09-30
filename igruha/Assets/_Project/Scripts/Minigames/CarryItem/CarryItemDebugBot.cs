using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Болванка соло-прогона: берётся за поручень тележки своей команды,
    /// ждёт под краном, пока та наберётся, везёт к своему баку, ждёт, пока
    /// сольётся, и катит пустую обратно к крану.
    ///
    /// Без неё фазу каркаса не принять вовсе. Вся механика игры — про
    /// <b>рассогласование между несущими</b>, а несущий в одиночном прогоне
    /// ровно один: ни крена от рывка партнёра, ни срыва поручня, ни тарана
    /// между командами проверить нечем. Одному человеку двумя персонажами не
    /// поуправлять, а сети на фазе каркаса ещё нет.
    ///
    /// Путь только человеческий: <c>DriveMove</c> у ходьбы и
    /// <c>DriveInteract</c> у кнопки E. Короткого пути в обход правил нет —
    /// иначе проверка перестала бы проверять то, что делает живой игрок.
    ///
    /// Это отладочный стенд, а не ИИ: болванка не саботирует, не выбирает
    /// момент и всегда набирает полную. В сетевой катке не ставится вовсе.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class CarryItemDebugBot : MonoBehaviour
    {
        /// <summary>Что болванка делает прямо сейчас.</summary>
        private enum Step
        {
            /// <summary>Идёт к тележке и берётся за свободный поручень.</summary>
            ToCart,
            /// <summary>Стоит у поручня под краном и ждёт, пока тележка наберётся.</summary>
            Filling,
            /// <summary>Везёт тележку к своему баку.</summary>
            ToTank,
            /// <summary>Стоит у бака и ждёт, пока тележка сольётся.</summary>
            Pouring,
            /// <summary>Катит пустую тележку обратно к крану.</summary>
            ToTap
        }

        /// <summary>
        /// С какого зазора <b>до коллайдера</b> тележки болванка жмёт E, м.
        /// Меньше радиуса поиска <c>PlayerInteractor</c> (1.8) с запасом:
        /// нажатие на самой границе отбивается проверкой дистанции.
        /// </summary>
        private const float InteractRange = 1.2f;

        /// <summary>Сколько ждать между попытками взяться, с. Без паузы E жмётся каждый кадр и берёт-отпускает по кругу.</summary>
        private const float GrabRetryDelay = 0.6f;

        /// <summary>
        /// На сколько метров болванка забегает вперёд своей стоянки в сторону
        /// цели. Ровно на стоянке она стояла бы на месте: связь не натянута —
        /// тележка не едет.
        ///
        /// Не меньше щупа обхода (1.8 м) с запасом: с целью в полуметре
        /// <c>DebugPlayerBot</c> упирается в завал и стоит там до конца раунда.
        /// </summary>
        private const float Lead = 3f;

        /// <summary>
        /// Какую долю предела связи болванка считает комфортным натяжением.
        /// Дальше она перестаёт тянуть и ждёт тележку: связь упругая, и
        /// натянутая до предела означает крен, а не скорость.
        /// </summary>
        private const float ComfortFraction = 0.2f;

        /// <summary>В каком радиусе от стоянки крана тележка считается «под краном», м.</summary>
        private const float DockRange = 1.6f;

        /// <summary>В каком радиусе от бака тележка считается «у бака», м. Зона слива шире, запас на подъезд.</summary>
        private const float TankRange = 3.2f;

        private PlayerController motor;
        private PlayerInputReader reader;
        private DebugPlayerBot walker;
        private CarryItemMinigame game;
        private TeamSide team = TeamSide.None;

        private Step step = Step.ToCart;
        private float grabCooldown;

        private Collider cartCollider;
        private WaterCart knownCart;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            reader = GetComponent<PlayerInputReader>();
        }

        private void OnDisable()
        {
            // Снятая болванка обязана бросить и ход, и кнопку.
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
            step = Step.ToCart;
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

            // A one-frame key pulse; the cart keeps its grip after release.
            reader.DriveInteractHold(false);

            // Управление на обучалке отнимают только у живых игроков: у
            // болванки ридер и так не «локально управляемый».
            if (!game.Phase.IsGameplay())
            {
                walker.Stop();
                return;
            }

            grabCooldown = Mathf.Max(0f, grabCooldown - Time.deltaTime);

            WaterCart cart = game.CartOf(team);
            WaterTank tank = game.TankOf(team);
            WaterTap tap = game.TapOf(team);
            if (cart == null || tank == null || tap == null)
            {
                walker.Stop();
                return;
            }

            if (cart != knownCart)
            {
                knownCart = cart;
                cartCollider = cart.GetComponentInChildren<Collider>();
            }

            // Нокдаун и заморозка болванку не касаются: она просто перестаёт
            // жать и ждёт.
            if (motor.IsKnockedDown || motor.MovementLocked || reader.Suspended || cart.IsLost)
            {
                walker.Stop();
                return;
            }

            step = ChooseStep(cart, tank, tap);

            switch (step)
            {
                case Step.ToCart:
                    RunToCart(cart);
                    break;
                case Step.Filling:
                case Step.Pouring:
                    HoldStation(cart);
                    break;
                case Step.ToTank:
                    Haul(cart, tank.transform.position);
                    break;
                case Step.ToTap:
                    Haul(cart, tap.DockPosition);
                    break;
            }
        }

        /// <summary>
        /// Полный цикл: набрать полную, отвезти, слить, вернуть. Болванка не
        /// выбирает между полной и полупустой — этот выбор проверяется на людях.
        /// </summary>
        private Step ChooseStep(WaterCart cart, WaterTank tank, WaterTap tap)
        {
            if (!cart.Carry.IsCarriedBy(motor))
            {
                return Step.ToCart;
            }

            bool atTap = Flat(cart.transform.position, tap.DockPosition) <= DockRange;
            bool atTank = Flat(cart.transform.position, tank.transform.position) <= TankRange;
            bool full = game.Config != null && cart.Water >= game.Config.CartCapacity;

            if (cart.Water <= 0)
            {
                return atTap ? Step.Filling : Step.ToTap;
            }

            if (atTank)
            {
                return Step.Pouring;
            }

            if (atTap && !full)
            {
                return Step.Filling;
            }

            return Step.ToTank;
        }

        /// <summary>
        /// К тележке подходим не вплотную, а на расстояние своей стоянки: встав
        /// внутрь кузова, болванка упёрлась бы в его коллайдер.
        /// </summary>
        private void RunToCart(WaterCart cart)
        {
            Vector3 station = NearestFreeStation(cart);

            if (!IsNear(cartCollider, cart.transform.position, InteractRange))
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
            reader.DriveInteractHold(true);
        }

        /// <summary>Под краном и у бака болванка просто стоит на своей стоянке: тележку двигать не нужно.</summary>
        private void HoldStation(WaterCart cart)
        {
            int slot = SlotOf(cart);
            if (slot < 0)
            {
                walker.Stop();
                return;
            }

            Vector3 station = cart.Carry.StationOf(slot);
            if (Flat(transform.position, station) < 0.35f)
            {
                walker.Stop();
                return;
            }

            walker.SetTarget(station, true);
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
        /// С тележкой в руках болванка идёт не в цель напрямую, а держит свою
        /// стоянку, сдвинутую в сторону цели.
        ///
        /// Разница принципиальная. Идя прямо в цель, несущие обгоняют свои
        /// стоянки — тележка едет медленнее их, — натяжения складываются в одну
        /// сторону, и тележка кренится весь путь. Держась стоянки, болванка
        /// тянет ровно столько, сколько нужно, чтобы тележка ехала.
        /// </summary>
        private void Haul(WaterCart cart, Vector3 destination)
        {
            int slot = SlotOf(cart);
            if (slot < 0)
            {
                Steer(destination);
                return;
            }

            // Ведём тележку к следующей точке маршрута, а не прямо в цель:
            // сначала доска, потом горлышко, потом вторая доска — в обе стороны.
            Vector3 goal = game.NextWaypoint(team, cart.transform.position, destination, out _);

            Vector3 station = cart.Carry.StationOf(slot);
            Vector3 toGoal = goal - cart.transform.position;
            toGoal.y = 0f;

            // Забегаем вперёд, только пока связь не натянута. Натянулась —
            // возвращаемся на стоянку и даём тележке себя догнать: у полной
            // разгон вдвое меньше, и она отстаёт заметно.
            float comfort = cart.Carry.Settings.breakDistance * ComfortFraction;
            bool slack = cart.Carry.StretchOf(slot) < comfort;

            if (slack && toGoal.sqrMagnitude > 0.0001f)
            {
                station += toGoal.normalized * Lead;
            }

            walker.SetTarget(station, false);
        }

        /// <summary>Стоянка ближайшего свободного поручня.</summary>
        private Vector3 NearestFreeStation(WaterCart cart)
        {
            Vector3 best = cart.transform.position;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < cart.Carry.HandleCount; i++)
            {
                if (cart.Carry.CarrierAt(i) != null)
                {
                    continue;
                }

                Vector3 station = cart.Carry.StationOf(i);
                float sqr = (station - transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = station;
                }
            }

            return best;
        }

        private int SlotOf(WaterCart cart)
        {
            if (cart == null)
            {
                return -1;
            }

            for (int i = 0; i < cart.Carry.HandleCount; i++)
            {
                if (cart.Carry.CarrierAt(i) == motor)
                {
                    return i;
                }
            }

            return -1;
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>Зазор до поверхности цели не больше <paramref name="range"/>. Без коллайдера меряем до корня.</summary>
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
