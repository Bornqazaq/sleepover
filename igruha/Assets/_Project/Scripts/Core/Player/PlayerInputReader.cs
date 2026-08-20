using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Читает ввод локального игрока (Update) и отдаёт его мотору и способностям.
    /// Отдельный компонент, чтобы при переходе на NGO ввод обрабатывался только
    /// у владельца, а мотор оставался без изменений.
    /// </summary>
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference jumpAction;
        [SerializeField] private InputActionReference pushAction;
        [SerializeField] private InputActionReference interactAction;
        [Tooltip("Удержание Tab: пока зажато — открыто колесо эмоций")]
        [SerializeField] private InputActionReference emoteAction;
        [Tooltip("Тот же Look, что крутит камеру: пока открыто колесо, его дельта водит курсор по секторам")]
        [SerializeField] private InputActionReference lookAction;
        [SerializeField] private InputActionReference crouchAction;

        /// <summary>
        /// InputActionReference указывает на общий ассет: один InputAction на всю сцену.
        /// Поэтому Disable() при выключении одного ридера погасил бы ввод и остальным
        /// (в тесте с манекенами это отбирало управление у живого игрока).
        /// Считаем ссылки и гасим действие только когда его не использует никто.
        /// </summary>
        private static readonly Dictionary<InputAction, int> ActionUsers = new Dictionary<InputAction, int>();

        public Vector2 MoveInput { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool PushPressed { get; private set; }
        public bool InteractPressed { get; private set; }
        /// <summary>Кнопка эмоций зажата — состояние, а не нажатие: колесо живёт всё удержание.</summary>
        public bool EmoteHeld { get; private set; }
        /// <summary>Смещение мыши/стика за кадр — им же водится курсор колеса эмоций.</summary>
        public Vector2 LookDelta { get; private set; }

        /// <summary>Приседание — удержание, а не нажатие: отпустил кнопку, встал.</summary>
        public bool CrouchHeld { get; private set; }

        /// <summary>
        /// Кнопка взаимодействия зажата. Отдельно от <see cref="InteractPressed"/>:
        /// есть интерактивы, которые живут всё удержание, а не срабатывают
        /// разово — кнопка отсчёта в «Секундомере» тикает, пока её держат.
        /// </summary>
        public bool InteractHeld { get; private set; }

        /// <summary>
        /// Ложь у персонажей, которыми эта машина не управляет: чужие сетевые
        /// копии и манекены локального теста. Такой ридер нельзя включать снова —
        /// иначе локальные нажатия дёргают сразу всех.
        /// </summary>
        public bool LocallyControlled { get; private set; } = true;

        /// <summary>Навсегда отобрать управление у этой копии персонажа.</summary>
        public void RevokeLocalControl()
        {
            LocallyControlled = false;
            enabled = false;
        }

        /// <summary>
        /// Подать ввод движения извне — болванке соло-теста или скриптовой
        /// сцене. Разрешено только копиям без локального управления: своему
        /// персонажу значение всё равно затрёт <c>Update</c>, а два источника
        /// ввода на одном персонаже дают гонку, которую потом не найти.
        /// </summary>
        public void DriveMove(Vector2 move)
        {
            if (LocallyControlled)
            {
                return;
            }

            MoveInput = move;
        }

        /// <summary>Прыжок извне. Гасится потребителем через <see cref="ConsumeJump"/>, как обычное нажатие.</summary>
        public void DriveJump()
        {
            if (LocallyControlled)
            {
                return;
            }

            JumpPressed = true;
        }

        private void OnEnable()
        {
            Acquire(moveAction);
            Acquire(jumpAction);
            Acquire(pushAction);
            Acquire(interactAction);
            Acquire(emoteAction);
            Acquire(lookAction);
            Acquire(crouchAction);
        }

        private void OnDisable()
        {
            Release(moveAction);
            Release(jumpAction);
            Release(pushAction);
            Release(interactAction);
            Release(emoteAction);
            Release(lookAction);
            Release(crouchAction);
            MoveInput = Vector2.zero;
            LookDelta = Vector2.zero;
            JumpPressed = PushPressed = InteractPressed = EmoteHeld = false;
            CrouchHeld = false;
            InteractHeld = false;
        }

        private void Update()
        {
            MoveInput = moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;
            LookDelta = lookAction != null ? lookAction.action.ReadValue<Vector2>() : Vector2.zero;
            EmoteHeld = emoteAction != null && emoteAction.action.IsPressed();
            JumpPressed |= WasPressed(jumpAction);
            PushPressed |= WasPressed(pushAction);
            InteractPressed |= WasPressed(interactAction);
            CrouchHeld = crouchAction != null && crouchAction.action.IsPressed();
            InteractHeld = interactAction != null && interactAction.action.IsPressed();
        }

        /// <summary>Сбросить одноразовые нажатия — вызывается потребителем после обработки.</summary>
        public void ConsumeJump() => JumpPressed = false;
        public void ConsumePush() => PushPressed = false;
        public void ConsumeInteract() => InteractPressed = false;

        private static bool WasPressed(InputActionReference reference) =>
            reference != null && reference.action.WasPerformedThisFrame();

        private static void Acquire(InputActionReference reference)
        {
            if (reference == null || reference.action == null)
            {
                return;
            }

            InputAction action = reference.action;
            ActionUsers.TryGetValue(action, out int users);
            ActionUsers[action] = users + 1;
            action.Enable();
        }

        private static void Release(InputActionReference reference)
        {
            if (reference == null || reference.action == null)
            {
                return;
            }

            InputAction action = reference.action;
            if (!ActionUsers.TryGetValue(action, out int users))
            {
                return;
            }

            users--;
            if (users > 0)
            {
                ActionUsers[action] = users;
                return;
            }

            ActionUsers.Remove(action);
            action.Disable();
        }
    }
}
