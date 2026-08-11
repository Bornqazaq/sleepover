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

        private void OnEnable()
        {
            Acquire(moveAction);
            Acquire(jumpAction);
            Acquire(pushAction);
            Acquire(interactAction);
        }

        private void OnDisable()
        {
            Release(moveAction);
            Release(jumpAction);
            Release(pushAction);
            Release(interactAction);
            MoveInput = Vector2.zero;
            JumpPressed = PushPressed = InteractPressed = false;
        }

        private void Update()
        {
            MoveInput = moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;
            JumpPressed |= WasPressed(jumpAction);
            PushPressed |= WasPressed(pushAction);
            InteractPressed |= WasPressed(interactAction);
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
