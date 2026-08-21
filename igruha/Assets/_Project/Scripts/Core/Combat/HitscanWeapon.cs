using System;
using UnityEngine;

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
    /// </summary>
    public sealed class HitscanWeapon : MonoBehaviour
    {
        /// <summary>Что дал выстрел. Пустой результат — луч ушёл в никуда.</summary>
        public readonly struct HitResult
        {
            /// <summary>Луч во что-то попал.</summary>
            public bool Hit { get; }
            /// <summary>Точка попадания, либо конец луча на предельной дальности.</summary>
            public Vector3 Point { get; }
            /// <summary>Направление уже с наложенным разбросом — по нему рисуется трассер.</summary>
            public Vector3 Direction { get; }
            public Collider Collider { get; }

            public HitResult(bool hit, Vector3 point, Vector3 direction, Collider collider)
            {
                Hit = hit;
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

        private float fireTimer;
        private float reloadTimer;

        /// <summary>Патронов в обойме прямо сейчас.</summary>
        public int Ammo { get; private set; }

        public int MagazineSize => magazineSize;

        public bool IsReloading => reloadTimer > 0f;

        /// <summary>Сколько осталось до конца перезарядки, с. Ноль — оружие готово.</summary>
        public float ReloadRemaining => reloadTimer;

        /// <summary>Оружие готово выстрелить: есть патрон, вышла задержка, не идёт перезарядка.</summary>
        public bool CanFire => Ammo > 0 && fireTimer <= 0f && !IsReloading;

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

        private void Update()
        {
            fireTimer = Mathf.Max(0f, fireTimer - Time.deltaTime);

            if (reloadTimer <= 0f)
            {
                return;
            }

            reloadTimer = Mathf.Max(0f, reloadTimer - Time.deltaTime);
            if (reloadTimer <= 0f)
            {
                Ammo = magazineSize;
                AmmoChanged?.Invoke(Ammo);
                ReloadingChanged?.Invoke(false);
            }
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
            if (!CanFire)
            {
                result = default;
                return false;
            }

            fireTimer = fireDelay;
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
            if (IsReloading || Ammo >= magazineSize)
            {
                return;
            }

            BeginReload();
        }

        /// <summary>Вернуть оружие в исходное состояние: полная обойма, таймеры сброшены. Для старта раунда.</summary>
        public void ResetWeapon()
        {
            fireTimer = 0f;

            if (IsReloading)
            {
                reloadTimer = 0f;
                ReloadingChanged?.Invoke(false);
            }

            Ammo = magazineSize;
            AmmoChanged?.Invoke(Ammo);
        }

        private void BeginReload()
        {
            reloadTimer = reloadDuration;
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
                return new HitResult(true, hit.point, direction, hit.collider);
            }

            return new HitResult(false, origin + direction * range, direction, null);
        }
    }
}
