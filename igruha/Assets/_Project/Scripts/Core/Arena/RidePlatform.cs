using System;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Player;

namespace Igruha.Core.Arena
{
    /// <summary>
    /// Вертикальная платформа с пассажиром. Два режима, одновременно работает
    /// ровно один:
    ///
    /// — <see cref="DriveMode.InputAxis"/>: едет туда, куда просит пассажир
    ///   (лифт Охотника в Duck Hunt);
    /// — <see cref="DriveMode.Scripted"/>: едет туда, куда сказали правила игры,
    ///   и пассажир на это не влияет (клетка «Секундомера»).
    ///
    /// <b>Пассажира несёт та машина, которой он принадлежит.</b> Авторитет над
    /// позицией персонажа у владельца (ClientNetworkTransform), и сервер чужого
    /// игрока сдвинуть не может — это ограничение IGR-297. Обойти его удаётся
    /// потому, что сама платформа движется детерминированно: одна и та же
    /// начальная точка, цель и длительность дают одинаковый путь на всех
    /// машинах, поэтому расхождения не возникает и синхронизировать пассажира
    /// отдельно не нужно.
    ///
    /// Не отвечает за то, чтобы с платформы нельзя было сойти: борта и крышу
    /// строит то, что платформу использует (у клетки это прутья и крыша).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RidePlatform : MonoBehaviour
    {
        public enum DriveMode
        {
            /// <summary>Едет по оси ввода пассажира.</summary>
            InputAxis,
            /// <summary>Едет по команде правил игры, ввод игнорируется.</summary>
            Scripted
        }

        [SerializeField] private DriveMode mode = DriveMode.InputAxis;
        [Tooltip("Скорость хода в режиме оси ввода, м/с")]
        [SerializeField] private float speed = 3f;
        [Tooltip("Нижняя граница хода по мировому Y")]
        [SerializeField] private float minY;
        [Tooltip("Верхняя граница хода по мировому Y")]
        [SerializeField] private float maxY = 10f;

        /// <summary>Платформа доехала до заданной отметки в скриптовом режиме.</summary>
        public event Action Arrived;

        private Rigidbody body;
        private PlayerController passenger;
        private Rigidbody passengerBody;
        private NetworkObject passengerNetwork;

        private float axis;
        private bool scripted;
        private float scriptedFrom;
        private float scriptedTo;
        private float scriptedDuration;
        private float scriptedElapsed;

        /// <summary>Момент начала хода на общих часах. Ноль — ход считается кадрами.</summary>
        private double scriptedStartTime;
        private bool scriptedFromClock;

        public DriveMode Mode
        {
            get => mode;
            set
            {
                mode = value;
                axis = 0f;
                scripted = false;
            }
        }

        /// <summary>Едет ли платформа прямо сейчас.</summary>
        public bool Moving => mode == DriveMode.Scripted ? scripted : !Mathf.Approximately(axis, 0f);

        public float CurrentY => body != null ? body.position.y : transform.position.y;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            // Без интерполяции платформа дёргается: она движется шагами
            // FixedUpdate, а кадры рисуются чаще.
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        /// <summary>Границы хода. Клетка задаёт их по своим уровням.</summary>
        public void SetLimits(float lower, float upper)
        {
            minY = Mathf.Min(lower, upper);
            maxY = Mathf.Max(lower, upper);
        }

        /// <summary>
        /// Кого везём. Пассажир один: и лифт Охотника, и клетка рассчитаны
        /// ровно на одного.
        /// </summary>
        public void SetPassenger(PlayerController player)
        {
            passenger = player;
            passengerBody = player != null ? player.GetComponent<Rigidbody>() : null;
            passengerNetwork = player != null ? player.GetComponent<NetworkObject>() : null;
        }

        /// <summary>Ось ввода −1…1. В скриптовом режиме игнорируется.</summary>
        public void SetAxis(float value)
        {
            if (mode != DriveMode.InputAxis)
            {
                return;
            }

            axis = Mathf.Clamp(value, -1f, 1f);
        }

        /// <summary>
        /// Доехать до отметки за заданное время. Повторный вызов посреди хода
        /// перебивает предыдущий: клетка может получить вторую ошибку, не успев
        /// доехать по первой.
        /// </summary>
        public void MoveTo(float targetY, float duration)
        {
            if (mode != DriveMode.Scripted)
            {
                Debug.LogWarning($"{name}: MoveTo в режиме оси ввода — переключи Mode на Scripted", this);
                return;
            }

            scriptedFrom = CurrentY;
            scriptedTo = Mathf.Clamp(targetY, minY, maxY);
            scriptedDuration = Mathf.Max(0.01f, duration);
            scriptedElapsed = 0f;
            scriptedFromClock = false;
            scripted = true;
        }

        /// <summary>
        /// Тот же ход, но прогресс берётся из общих часов, а не из суммы кадров.
        /// Нужен там, где платформа обязана быть на одной высоте у всех: каждая
        /// машина считает по одной формуле от одного момента, поэтому расхождение
        /// не копится и не зависит от того, кто когда получил команду. Машина,
        /// получившая команду позже, встаёт сразу на верную высоту и едет дальше.
        /// </summary>
        public void MoveTo(float targetY, float duration, double startTime)
        {
            MoveTo(targetY, duration);
            if (!scripted)
            {
                return;
            }

            scriptedStartTime = startTime;
            scriptedFromClock = true;
        }

        /// <summary>Поставить платформу на отметку мгновенно — расстановка уровней на старте матча.</summary>
        public void SnapTo(float y)
        {
            scripted = false;
            Vector3 position = transform.position;
            position.y = Mathf.Clamp(y, minY, maxY);
            transform.position = position;

            if (body != null)
            {
                body.position = position;
            }
        }

        private void FixedUpdate()
        {
            float currentY = CurrentY;
            float nextY = mode == DriveMode.Scripted ? StepScripted() : StepAxis(currentY);
            float delta = nextY - currentY;
            if (Mathf.Approximately(delta, 0f))
            {
                return;
            }

            Vector3 position = body.position;
            position.y = nextY;
            body.MovePosition(position);
            CarryPassenger(delta);
        }

        private float StepAxis(float currentY)
        {
            if (Mathf.Approximately(axis, 0f))
            {
                return currentY;
            }

            return Mathf.Clamp(currentY + axis * speed * Time.fixedDeltaTime, minY, maxY);
        }

        private float StepScripted()
        {
            if (!scripted)
            {
                return CurrentY;
            }

            scriptedElapsed += Time.fixedDeltaTime;
            float t = scriptedFromClock
                ? Mathf.Clamp01((float)(NetworkClock.Now - scriptedStartTime) / scriptedDuration)
                : Mathf.Clamp01(scriptedElapsed / scriptedDuration);
            float y = Mathf.Lerp(scriptedFrom, scriptedTo, t);

            if (t >= 1f)
            {
                // Доводим ровно до отметки: накопленная ошибка Lerp по шагам
                // FixedUpdate иначе оставляет клетку в паре миллиметров от уровня,
                // и за несколько ступеней это становится видно.
                y = scriptedTo;
                scripted = false;
                Arrived?.Invoke();
            }

            return y;
        }

        /// <summary>
        /// Сдвинуть пассажира вместе с платформой — но только на его машине.
        /// На чужой машине его позицией распоряжается её владелец, и попытка
        /// подвинуть его отсюда даст рывок и откат.
        /// </summary>
        private void CarryPassenger(float deltaY)
        {
            if (passengerBody == null)
            {
                return;
            }

            if (passengerNetwork != null && passengerNetwork.IsSpawned && !passengerNetwork.IsOwner)
            {
                return;
            }

            passengerBody.MovePosition(passengerBody.position + Vector3.up * deltaY);
        }
    }
}
