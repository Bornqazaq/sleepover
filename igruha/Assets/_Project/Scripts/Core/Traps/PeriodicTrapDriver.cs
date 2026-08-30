using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Session;

namespace Igruha.Core.Traps
{
    /// <summary>
    /// Дёргает ловушку с фиксированным периодом. Нажимать её никто не должен:
    /// такие ловушки тикают сами и бьют обе команды одинаково.
    ///
    /// <b>Фазу считает от общих часов, а не от локального таймера.</b> Разница
    /// принципиальная: сумма кадров у каждой машины своя и за 200 секунд раунда
    /// расходится на видимые доли секунды, а <see cref="NetworkClock"/> у всех
    /// один. Поэтому в сетевой фазе ловушки сойдутся у всех без единого пакета,
    /// и синхронизировать их не придётся вовсе.
    ///
    /// Сетка отсчитывается от <b>нуля самих часов</b>, а не от момента запуска
    /// этой машины: запускаются машины вразнобой, и сетка от собственного
    /// старта у каждой была бы своя.
    ///
    /// Дрейфа нет и внутри одной машины: номер срабатывания считается делением
    /// прошедшего времени на период, а не прибавлением периода к «сейчас».
    /// Пропущенный кадр не сдвигает всю дальнейшую сетку.
    ///
    /// <b>Решает срабатывание сервер, отыгрывают все.</b> Урон и импульсы
    /// назначает только авторитет (<c>TrapBase.Activate</c>), остальные машины
    /// в тот же момент общей сетки играют эффект через <c>PlayFired</c> — иначе
    /// клиент не увидел бы и не услышал ни одного срабатывания за раунд.
    /// </summary>
    public sealed class PeriodicTrapDriver : MonoBehaviour
    {
        [Tooltip("Ловушка, которую дёргаем. Пусто — берётся с этого же объекта")]
        [SerializeField] private TrapBase trap;
        [Tooltip("Период срабатывания, с")]
        [SerializeField] private float period = 6f;
        [Tooltip("Сдвиг фазы, с. Им разводят две ловушки с одинаковым периодом, чтобы они не били в такт")]
        [SerializeField] private float phaseOffset;
        [Tooltip("Тикать сразу со старта сцены. Снять, если ловушку включают правила раунда")]
        [SerializeField] private bool runOnStart = true;

        /// <summary>Номер последнего сработавшего интервала. −1 — ещё ни одного.</summary>
        private long lastFiredIndex = -1;

        private bool running;
        private bool warnedNotReady;

        public bool Running => running;

        /// <summary>Период срабатывания, с. Задаётся мини-игрой из её конфига.</summary>
        public float Period
        {
            get => period;
            set => period = Mathf.Max(0.01f, value);
        }

        private void Awake()
        {
            if (trap == null)
            {
                trap = GetComponent<TrapBase>();
            }

            if (trap == null)
            {
                Debug.LogError($"{name}: PeriodicTrapDriver без TrapBase — дёргать нечего.", this);
                enabled = false;
            }
        }

        private void Start()
        {
            if (runOnStart)
            {
                Restart();
            }
        }

        /// <summary>
        /// Включить ловушку. Сетка при этом не сдвигается — она общая и
        /// абсолютная; сдвигается только отметка «отсюда считаем сработавшим»,
        /// чтобы включённая посреди раунда ловушка не отыграла разом весь
        /// пропущенный ряд.
        /// </summary>
        public void Restart()
        {
            lastFiredIndex = CurrentIndex();
            running = true;
        }

        /// <summary>Остановить. Ловушка остаётся в том состоянии, в каком была.</summary>
        public void StopDriving() => running = false;

        /// <summary>Номер интервала общей сетки прямо сейчас.</summary>
        private long CurrentIndex() =>
            period > 0f ? (long)System.Math.Floor((NetworkClock.Now - phaseOffset) / period) : 0L;

        private void Update()
        {
            if (!running || period <= 0f)
            {
                return;
            }

            long index = CurrentIndex();
            if (index <= lastFiredIndex)
            {
                return;
            }

            lastFiredIndex = index;

            // Срабатывание — исход раунда, и решает его авторитет. Остальные
            // машины дошли до того же интервала той же сеткой и просто играют
            // эффект: без этого клиент за раунд не увидел бы ни одного удара.
            if (!WorldAuthority.HasAuthority)
            {
                trap.PlayFired();
                return;
            }

            // Кулдаун самой ловушки длиннее периода — она будет молча
            // пропускать срабатывания, и это почти всегда ошибка настройки.
            if (!trap.IsReady && !warnedNotReady)
            {
                warnedNotReady = true;
                Debug.LogWarning($"{name}: кулдаун ловушки длиннее периода {period:F1} с — " +
                                 "часть срабатываний пропадёт. Уменьши кулдаун или увеличь период.", this);
            }

            trap.Activate();
        }
    }
}
