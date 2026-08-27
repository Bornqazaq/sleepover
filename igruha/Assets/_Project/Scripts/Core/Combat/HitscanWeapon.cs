using System;
using UnityEngine;
using Igruha.Core.Minigame;

namespace Igruha.Core.Combat
{
    /// <summary>
    /// Оружие мгновенного попадания: луч без баллистики, обойма, задержка между
    /// выстрелами, перезарядка и конус разброса. Пара к <see cref="ProjectileShooter"/>,
    /// а не замена ему — тот остаётся для игр, где нужен летящий снаряд.
    ///
    /// Компонент не знает правил игры: он сообщает, во что попал луч, а решение
    /// (смерть, урон, засчитанное очко) принимает вызывающий. Поэтому одно и то же
    /// оружие годится и охотнику Duck Hunt, и любому тиру.
    ///
    /// <see cref="TryFire"/> — единственная точка, меняющая состояние оружия.
    /// В сетевой фазе её зовёт сервер, получив от клиента чистое направление:
    /// конус разброса накладывается здесь, то есть на авторитетной стороне,
    /// и клиент не может выстрелить точнее, чем ему положено.
    ///
    /// Обойма и перезарядка — состояние раунда, а не картинка, поэтому машина,
    /// которая их не ведёт, ставит себе <see cref="DrivenExternally"/> и живёт
    /// присланным: стрелять и перезаряжаться сама она уже не вправе.
    ///
    /// Оба таймера хранятся <b>моментами на общих часах</b>, а не остатками.
    /// Остаток пришлось бы досылать каждый кадр, момент достаточно объявить
    /// один раз — и перезарядка кончается у всех одновременно, без поправки
    /// на пинг. Вне сети общие часы — это обычное время сцены, так что
    /// одиночный тест работает как раньше.
    /// </summary>
    public sealed class HitscanWeapon : MonoBehaviour
    {
        /// <summary>Что дал выстрел. Пустой результат — луч ушёл в никуда.</summary>
        public readonly struct HitResult
        {
            /// <summary>Луч во что-то попал.</summary>
            public bool Hit { get; }

            /// <summary>
            /// Откуда ушёл луч. Без неё подписчик не нарисует трассер, не спросив
            /// стрелка отдельно, — а в сетевой катке спрашивать некого: выстрел
            /// посчитал сервер, и точка вылета приезжает вместе с результатом.
            /// </summary>
            public Vector3 Origin { get; }

            /// <summary>Точка попадания, либо конец луча на предельной дальности.</summary>
            public Vector3 Point { get; }
            /// <summary>Направление уже с наложенным разбросом — по нему рисуется трассер.</summary>
            public Vector3 Direction { get; }

            /// <summary>Во что попали. Пусто на машинах, которые выстрел не считали.</summary>
            public Collider Collider { get; }

            public HitResult(bool hit, Vector3 origin, Vector3 point, Vector3 direction, Collider collider)
            {
                Hit = hit;
                Origin = origin;
                Point = point;
                Direction = direction;
                Collider = collider;
            }
        }

        [Header("Обойма")]
        [Tooltip("Патронов в обойме")]
        [SerializeField] private int magazineSize = 5;
        [Tooltip("Задержка между выстрелами, с")]
        [SerializeField] private float fireDelay = 2f;
        [Tooltip("Перезарядка обоймы, с. Запускается сама, когда обойма опустела")]
        [SerializeField] private float reloadDuration = 4f;

        [Header("Луч")]
        [Tooltip("Дальность выстрела, м. Дальше неё луч ничего не задевает")]
        [SerializeField] private float range = 43.2f;
        [Tooltip("Во что попадает луч. Прозрачные барьеры, ограничивающие игроков, из маски исключать — иначе они ловят выстрел")]
        [SerializeField] private LayerMask hitMask = ~0;
        [Tooltip("Половина угла конуса разброса, °. Меняется на ходу через SpreadAngle: стрельба в движении обычно вдвое хуже")]
        [SerializeField] private float spreadAngle = 1.5f;

        /// <summary>Выстрел состоялся: направление с разбросом и результат. Для звука, трассера и вспышки.</summary>
        public event Action<HitResult> Fired;

        /// <summary>Патронов стало другое количество — чтобы HUD не опрашивал поле каждый кадр.</summary>
        public event Action<int> AmmoChanged;

        /// <summary>Началась (true) или кончилась (false) перезарядка.</summary>
        public event Action<bool> ReloadingChanged;

        /// <summary>Момент, раньше которого следующий выстрел не пройдёт. Общие часы.</summary>
        private double nextFireTime;

        /// <summary>Момент конца перезарядки на общих часах. Ноль — оружие не перезаряжается.</summary>
        private double reloadEndsAt;

        /// <summary>Патронов в обойме прямо сейчас.</summary>
        public int Ammo { get; private set; }

        public int MagazineSize => magazineSize;

        /// <summary>
        /// Обойму и перезарядку ведёт не эта машина: она их только рисует.
        /// Ставится на всех, кроме авторитета, в сетевой катке.
        /// </summary>
        public bool DrivenExternally { get; set; }

        public bool IsReloading => reloadEndsAt > 0d && NetworkClock.Now < reloadEndsAt;

        /// <summary>Момент конца перезарядки на общих часах — его и реплицируем.</summary>
        public double ReloadEndsAt => reloadEndsAt;

        /// <summary>Сколько осталось до конца перезарядки, с. Ноль — оружие готово.</summary>
        public float ReloadRemaining => IsReloading ? (float)(reloadEndsAt - NetworkClock.Now) : 0f;

        /// <summary>Оружие готово выстрелить: есть патрон, вышла задержка, не идёт перезарядка.</summary>
        public bool CanFire => Ammo > 0 && NetworkClock.Now >= nextFireTime && !IsReloading;

        /// <summary>
        /// Половина угла конуса разброса, °. Роль меняет его по обстоятельствам —
        /// например, повышает, пока стрелок движется.
        /// </summary>
        public float SpreadAngle
        {
            get => spreadAngle;
            set => spreadAngle = Mathf.Max(0f, value);
        }

        private void Awake()
        {
            Ammo = magazineSize;
        }

        /// <summary>
        /// Задать параметры извне. Нужно там, где оружие вешается на персонажа
        /// в рантайме и его числа живут в конфиге мини-игры, а не в инспекторе
        /// префаба: два места для одного числа неминуемо разъезжаются.
        /// </summary>
        public void Configure(int magazine, float delayBetweenShots, float reload, float shotRange, LayerMask mask)
        {
            magazineSize = Mathf.Max(1, magazine);
            fireDelay = Mathf.Max(0f, delayBetweenShots);
            reloadDuration = Mathf.Max(0f, reload);
            range = Mathf.Max(0f, shotRange);
            hitMask = mask;
            ResetWeapon();
        }

        /// <summary>
        /// Досыпать обойму по моменту конца перезарядки. Считают все машины, а не
        /// один авторитет: момент общий, и результат у него ровно один — полная
        /// обойма. Ждать ради него пакета значило бы показать стрелку пустое
        /// оружие на лишние полпинга.
        /// </summary>
        private void Update()
        {
            if (reloadEndsAt <= 0d || NetworkClock.Now < reloadEndsAt)
            {
                return;
            }

            reloadEndsAt = 0d;
            Ammo = magazineSize;
            AmmoChanged?.Invoke(Ammo);
            ReloadingChanged?.Invoke(false);
        }

        /// <summary>
        /// Выстрелить. Возвращает false, если оружие не готово — вызывающему
        /// не нужно самому следить за обоймой и задержкой.
        ///
        /// Направление приходит чистым, конус накладывается здесь: в сетевой
        /// фазе это гарантирует, что разброс посчитан на сервере.
        /// </summary>
        public bool TryFire(Vector3 origin, Vector3 direction, out HitResult result)
        {
            // Машина, которая обойму не ведёт, не стреляет вовсе: локальный
            // выстрел списал бы патрон, который ей не принадлежит, и её счётчик
            // разошёлся бы с серверным до конца раунда.
            if (DrivenExternally || !CanFire)
            {
                result = default;
                return false;
            }

            nextFireTime = NetworkClock.Now + fireDelay;
            Ammo--;
            AmmoChanged?.Invoke(Ammo);

            Vector3 spread = ApplySpread(direction.normalized);
            result = Cast(origin, spread);
            Fired?.Invoke(result);

            if (Ammo <= 0)
            {
                BeginReload();
            }

            return true;
        }

        /// <summary>
        /// Перезарядить вручную. Полную обойму не перезаряжаем: иначе нажатие
        /// в спокойный момент отнимает у стрелка те же секунды, ничего не давая.
        /// </summary>
        public void Reload()
        {
            if (DrivenExternally || IsReloading || Ammo >= magazineSize)
            {
                return;
            }

            BeginReload();
        }

        /// <summary>Вернуть оружие в исходное состояние: полная обойма, таймеры сброшены. Для старта раунда.</summary>
        public void ResetWeapon()
        {
            bool wasReloading = IsReloading;
            nextFireTime = 0d;
            reloadEndsAt = 0d;

            if (wasReloading)
            {
                ReloadingChanged?.Invoke(false);
            }

            Ammo = magazineSize;
            AmmoChanged?.Invoke(Ammo);
        }

        /// <summary>
        /// Применить состояние, решённое сервером. Момент конца перезарядки
        /// приходит в тех же общих часах, поэтому остаток совпадает с серверным
        /// без поправки на пинг.
        /// </summary>
        public void ApplyNetworkState(int ammo, double reloadEnd)
        {
            bool wasReloading = IsReloading;
            reloadEndsAt = reloadEnd;

            if (Ammo != ammo)
            {
                Ammo = Mathf.Clamp(ammo, 0, magazineSize);
                AmmoChanged?.Invoke(Ammo);
            }

            if (wasReloading != IsReloading)
            {
                ReloadingChanged?.Invoke(IsReloading);
            }
        }

        private void BeginReload()
        {
            reloadEndsAt = NetworkClock.Now + Mathf.Max(0f, reloadDuration);
            ReloadingChanged?.Invoke(true);
        }

        /// <summary>
        /// Отклонение внутри конуса. Угол берётся по корню из случайного числа,
        /// иначе попадания сгущаются к центру круга и заявленный разброс на
        /// деле оказывается заметно меньше.
        /// </summary>
        private Vector3 ApplySpread(Vector3 direction)
        {
            if (spreadAngle <= 0f)
            {
                return direction;
            }

            float angle = spreadAngle * Mathf.Sqrt(UnityEngine.Random.value);
            float roll = UnityEngine.Random.value * 360f;

            Vector3 axis = Vector3.Cross(direction, Mathf.Abs(direction.y) > 0.99f ? Vector3.forward : Vector3.up).normalized;
            Vector3 tilted = Quaternion.AngleAxis(angle, axis) * direction;
            return Quaternion.AngleAxis(roll, direction) * tilted;
        }

        private HitResult Cast(Vector3 origin, Vector3 direction)
        {
            if (Physics.Raycast(origin, direction, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
            {
                return new HitResult(true, origin, hit.point, direction, hit.collider);
            }

            return new HitResult(false, origin, origin + direction * range, direction, null);
        }
    }
}
