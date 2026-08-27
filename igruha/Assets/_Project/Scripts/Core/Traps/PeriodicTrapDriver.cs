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
    /// Дрейфа нет и внутри одной машины: номер срабатывания считается делением
    /// прошедшего времени на период, а не прибавлением периода к «сейчас».
    /// Пропущенный кадр не сдвигает всю дальнейшую сетку.
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

        /// <summary>Момент, от которого отсчитывается сетка срабатываний, на общих часах.</summary>
        private double originTime;

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

        /// <summary>Начать отсчёт заново от текущего момента общих часов.</summary>
        public void Restart()
        {
            originTime = NetworkClock.Now + phaseOffset;
            lastFiredIndex = -1;
            running = true;
        }

        /// <summary>Остановить. Ловушка остаётся в том состоянии, в каком была.</summary>
        public void StopDriving() => running = false;

        private void Update()
        {
            if (!running || period <= 0f)
            {
                return;
            }

            // Срабатывание — исход раунда, и решает его авторитет. Вне сети
            // авторитет здесь же, поэтому одиночный тест работает как есть.
            if (!WorldAuthority.HasAuthority)
            {
                return;
            }

            double elapsed = NetworkClock.Now - originTime;
            if (elapsed < 0d)
            {
                return;
            }

            long index = (long)(elapsed / period);
            if (index <= lastFiredIndex)
            {
                return;
            }

            lastFiredIndex = index;

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
