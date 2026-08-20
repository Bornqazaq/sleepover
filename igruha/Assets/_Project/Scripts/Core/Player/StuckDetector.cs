using System;
using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Страховка от застревания: игрок рвётся вперёд, а не едет — значит зажат
    /// геометрией. На аренах с плотной застройкой это происходит регулярно, и
    /// без страховки застрявший выпадает из раунда и портит матч всем.
    ///
    /// Ввод обязателен в условии: стоять на месте — легальная тактика (прятки,
    /// «Плачущие ангелы»), и детектор не должен выкидывать тех, кто стоит нарочно.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class StuckDetector : MonoBehaviour
    {
        [Tooltip("Куда возвращать застрявшего. Пусто — берётся с этого же объекта")]
        [SerializeField] private PlayerRespawner respawner;
        [Tooltip("Ниже этой горизонтальной скорости (м/с) персонаж считается стоящим")]
        [SerializeField] private float stuckSpeedThreshold = 0.2f;
        [Tooltip("Сколько секунд стоять при ненулевом вводе, чтобы сработал авто-респаун")]
        [SerializeField] private float stuckDuration = 3f;

        /// <summary>
        /// Застрявшего сейчас вернут на точку. Мини-игра может подписаться,
        /// чтобы не считать это провалом и не сбрасывать накопленный прогресс.
        /// </summary>
        public event Action PlayerUnstuck;

        private PlayerController motor;
        private PlayerInputReader inputReader;
        private float stuckTimer;

        /// <summary>
        /// Где тело было в конце прошлого физического такта. По нему считается
        /// пройденный путь — единственная величина, которой можно верить, см.
        /// <see cref="IsPushingIntoGeometry"/>.
        /// </summary>
        private Vector3 lastPosition;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            inputReader = GetComponent<PlayerInputReader>();

            if (respawner == null)
            {
                respawner = GetComponent<PlayerRespawner>();
            }
        }

        private void OnEnable()
        {
            stuckTimer = 0f;
            lastPosition = transform.position;
            motor.Teleported += ResetTimer;
        }

        private void OnDisable()
        {
            motor.Teleported -= ResetTimer;
        }

        private void ResetTimer()
        {
            stuckTimer = 0f;
            lastPosition = transform.position;
        }

        private void FixedUpdate()
        {
            bool pushing = IsPushingIntoGeometry();
            lastPosition = transform.position;

            if (!pushing)
            {
                stuckTimer = 0f;
                return;
            }

            stuckTimer += Time.fixedDeltaTime;
            if (stuckTimer < stuckDuration)
            {
                return;
            }

            stuckTimer = 0f;
            Unstick();
        }

        private bool IsPushingIntoGeometry()
        {
            // Нокдаун и «замри» тоже держат персонажа на месте, но это не застревание:
            // иначе замороженный с зажатым W улетал бы на старт сам собой.
            if (motor.IsKnockedDown || motor.MovementLocked || inputReader == null || motor.Config == null)
            {
                return false;
            }

            float deadzone = motor.Config.InputDeadzone;
            if (inputReader.MoveInput.sqrMagnitude < deadzone * deadzone)
            {
                return false;
            }

            // Меряем пройденный путь, а не скорость из Rigidbody.
            //
            // PlayerController в своём FixedUpdate ЗАПИСЫВАЕТ в linearVelocity
            // желаемую скорость, а порядок выполнения между ним и детектором
            // ничем не задан. Когда контроллер отрабатывает первым, детектор
            // читает скорость, которую тот только что заказал (полный ход
            // в стену), хотя решатель столкновений её тут же гасит и тело
            // стоит намертво. Условие «стою» не выполнялось никогда, таймер
            // обнулялся каждый такт, и авто-респаун не срабатывал вообще.
            // Замерено 17.08: скорость тела 0.00 м/с, упор 24.6 с, stuckTimer 0.
            //
            // Пройденный путь от порядка выполнения не зависит: он про то,
            // что реально произошло, а не про то, что кто-то попросил.
            Vector3 travelled = transform.position - lastPosition;
            travelled.y = 0f;
            float horizontalSpeed = travelled.magnitude / Mathf.Max(Time.fixedDeltaTime, Mathf.Epsilon);
            return horizontalSpeed <= stuckSpeedThreshold;
        }

        private void Unstick()
        {
            // Решение принимает сервер: без этой проверки респаун сработает
            // на каждой машине матча, у каждой копии персонажа свой.
            if (!motor.HasWorldAuthority)
            {
                return;
            }

            if (respawner == null)
            {
                Debug.LogWarning($"{name}: StuckDetector без PlayerRespawner — вытащить застрявшего некуда.", this);
                return;
            }

            PlayerUnstuck?.Invoke();
            respawner.Respawn();
        }
    }
}
