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
        [Tooltip("Позы на клавишах 1–4 («Дырка в стене»). Одно действие на все четыре: значение действия и есть номер позы")]
        [SerializeField] private InputActionReference poseAction;

        /// <summary>
        /// Сколько поз различает действие <c>Pose</c>: столько же, сколько
        /// у него привязок в <c>InputSystem_Actions</c> — четыре, клавиши 1–4.
        /// </summary>
        private const int MaxPose = 4;

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
        /// Какую позу просит игрок прямо сейчас: 1…4, ноль — ни одной клавиши
        /// не зажато. Именно <b>просит</b>, а не «стоит в ней»: позу защёлкивает
        /// способность игрока, и отпущенная клавиша её не снимает.
        ///
        /// Одно действие на четыре клавиши, а не четыре действия: номер позы —
        /// это значение действия, и он приезжает вместе с ним. Привязки несут
        /// процессор <c>scale</c>, у аналоговых триггеров — ещё и
        /// <c>axisDeadzone</c>, чтобы наполовину нажатый триггер не давал
        /// дробное значение и с ним чужую позу.
        /// </summary>
        public int PoseRequest { get; private set; }

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
        /// Ввод подаёт болванка автопрогона, а не клавиатура и не геймпад.
        ///
        /// Нужен затем, что в стенде из восьми процессов персонаж у каждой
        /// машины <b>свой</b>, то есть локально управляемый, — а
        /// <see cref="DriveMove"/> локально управляемым запрещён, и не зря:
        /// два источника ввода на одном персонаже дают гонку, которую потом
        /// не найти. Автопилот не добавляет второй источник, а <b>заменяет</b>
        /// первый: пока он взведён, <c>Update</c> действия не читает вовсе.
        ///
        /// Взводится только по аргументу запуска <c>--bot</c> и обратно
        /// не снимается: болванка живёт до конца процесса.
        /// </summary>
        public bool Autopilot { get; private set; }

        /// <summary>Отдать ввод болванке автопрогона. Обратного хода нет.</summary>
        public void EngageAutopilot()
        {
            if (Autopilot)
            {
                return;
            }

            Autopilot = true;
            ClearInput();
        }

        /// <summary>
        /// Подать ввод движения извне — болванке соло-теста или скриптовой
        /// сцене. Разрешено только копиям без локального управления: своему
        /// персонажу значение всё равно затрёт <c>Update</c>, а два источника
        /// ввода на одном персонаже дают гонку, которую потом не найти.
        /// </summary>
        public void DriveMove(Vector2 move)
        {
            if (LocallyControlled && !Autopilot)
            {
                return;
            }

            MoveInput = move;
        }

        /// <summary>Прыжок извне. Гасится потребителем через <see cref="ConsumeJump"/>, как обычное нажатие.</summary>
        public void DriveJump()
        {
            if (LocallyControlled && !Autopilot)
            {
                return;
            }

            JumpPressed = true;
        }

        /// <summary>
        /// Взаимодействие извне — разовое нажатие E. Гасится потребителем через
        /// <see cref="ConsumeInteract"/>, как обычное нажатие.
        ///
        /// Нужно болванкам соло-теста там, где половина правил игры живёт за
        /// кнопкой E: взяться за ручку, отпустить, взять предмет с кучки.
        /// Без этого болванку пришлось бы водить в обход правил, напрямую
        /// вызывая логику мини-игры, — и проверка перестала бы проверять то,
        /// что делает живой игрок.
        /// </summary>
        public void DriveInteract()
        {
            if (LocallyControlled && !Autopilot)
            {
                return;
            }

            InteractPressed = true;
        }

        /// <summary>
        /// Удержание E извне: им берут со штабеля и держат кнопку отсчёта.
        /// Состояние, а не нажатие, поэтому снимать его обязан тот же, кто
        /// поставил, — потребитель его не гасит.
        /// </summary>
        public void DriveInteractHold(bool held)
        {
            if (LocallyControlled && !Autopilot)
            {
                return;
            }

            InteractHeld = held;
        }

        /// <summary>
        /// Поза извне: болванке соло-теста. Состояние, а не нажатие, — как
        /// и у живого игрока, который держит клавишу; снимать его обязан тот,
        /// кто поставил.
        ///
        /// Путь тот же, что у человека: болванка не зовёт способность напрямую,
        /// а подаёт ввод. Короткого пути в обход правил нет — иначе проверка
        /// перестанет проверять то, что делает живой игрок.
        /// </summary>
        public void DrivePose(int pose)
        {
            if (LocallyControlled && !Autopilot)
            {
                return;
            }

            PoseRequest = Mathf.Clamp(pose, 0, MaxPose);
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
            Acquire(poseAction);
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
            Release(poseAction);
            ClearInput();
        }

        /// <summary>
        /// Ввод заморожен: ридер отдаёт пустоту, что бы игрок ни жал.
        ///
        /// Нужен на стартовом отсчёте: «управления нет» должно значить
        /// именно это, а не «двигаться нельзя, а толкать можно».
        /// Перекрывать ввод в каждой способности отдельно нельзя: одна уже
        /// забыла — <c>PlayerPushAbility</c> толкал прямо на отсчёте, — и следующая
        /// забудет точно так же. Одна точка отказа надёжнее восьми проверок.
        /// </summary>
        public bool Suspended { get; private set; }

        /// <summary>Заморозить или вернуть ввод. На заморозке накопленные нажатия гасятся, чтобы не выстрелить после разморозки.</summary>
        public void SetSuspended(bool suspended)
        {
            Suspended = suspended;

            if (suspended)
            {
                ClearInput();
            }
        }

        private void ClearInput()
        {
            MoveInput = Vector2.zero;
            LookDelta = Vector2.zero;
            JumpPressed = PushPressed = InteractPressed = EmoteHeld = false;
            CrouchHeld = false;
            InteractHeld = false;
            PoseRequest = 0;
        }

        private void Update()
        {
            // Замороженный ридер не читает действия вообще: иначе зажатое
            // на отсчёте накопится и выстрелит в первый же кадр после него.
            if (Suspended)
            {
                ClearInput();
                return;
            }

            // Под автопилотом действия не читаются вообще: иначе пустое
            // значение клавиатуры затирало бы то, что подала болванка,
            // в тот же кадр.
            if (Autopilot)
            {
                return;
            }

            MoveInput = moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;
            LookDelta = lookAction != null ? lookAction.action.ReadValue<Vector2>() : Vector2.zero;
            EmoteHeld = emoteAction != null && emoteAction.action.IsPressed();
            JumpPressed |= WasPressed(jumpAction);
            PushPressed |= WasPressed(pushAction);
            InteractPressed |= WasPressed(interactAction);
            CrouchHeld = crouchAction != null && crouchAction.action.IsPressed();
            InteractHeld = interactAction != null && interactAction.action.IsPressed();
            PoseRequest = ReadPose();
        }

        /// <summary>
        /// Номер позы под зажатой клавишей: 1…4, ноль — ничего не зажато.
        ///
        /// Значение действия уже несёт номер: привязки масштабированы
        /// процессорами в самом ассете, а не разбираются здесь по имени
        /// клавиши. Так раскладка остаётся данными и правится в редакторе
        /// ввода, а не кодом.
        /// </summary>
        private int ReadPose()
        {
            if (poseAction == null || poseAction.action == null)
            {
                return 0;
            }

            float raw = poseAction.action.ReadValue<float>();
            return raw < 0.5f ? 0 : Mathf.Clamp(Mathf.RoundToInt(raw), 1, MaxPose);
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
