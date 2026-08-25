using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.UI;

namespace Igruha.Core.CameraSystems
{
    /// <summary>
    /// Наблюдение за живыми после выбывания. Раунд идёт ещё до минуты после того,
    /// как игрок закончил свой — без спектатора он смотрит в пустоту.
    ///
    /// Компонент не знает правил игры: кто выбыл и кто ещё в деле, решает
    /// мини-игра и отдаёт сюда список целей. Список берётся по ссылке —
    /// мини-игра его обновляет, а камера сама уходит с пропавшей цели.
    ///
    /// Первая цель выбирается случайно, а не по порядку списка: иначе все
    /// выбывшие смотрят за одним и тем же человеком — первым в ростере.
    /// Случайность тут чисто зрительская и считается локально: на исход
    /// раунда она не влияет, поэтому серверу её решать незачем.
    ///
    /// Листается мышью: ЛКМ — следующий, ПКМ — предыдущий. Читаем устройство
    /// напрямую, тем же приёмом, что и Esc в <see cref="PauseScreen"/>, —
    /// это не игровое действие, и заводить под него привязку в общем ассете
    /// управления не нужно.
    /// </summary>
    public sealed class SpectatorCamera : MonoBehaviour
    {
        [SerializeField] private MinigameCameraController cameraController;
        [SerializeField] private RoundHud hud;
        [SerializeField] private InputActionReference nextAction;
        [SerializeField] private InputActionReference previousAction;
        [Tooltip("Каким ригом смотрим за живыми")]
        [SerializeField] private CameraMode spectatorMode = CameraMode.ThirdPerson;
        [Tooltip("Риг 3rd-person: наблюдателю он глушится, а поворот ведётся от тела того, за кем смотрим")]
        [SerializeField] private ThirdPersonCameraRig cameraRig;
        [Tooltip("Наклон камеры наблюдателя. Свой угол наблюдаемого по сети не ходит, поэтому берём постоянный")]
        [SerializeField] private float spectatorPitch = 12f;

        public bool IsActive { get; private set; }

        /// <summary>За кем смотрим сейчас. Пусто — живых не осталось.</summary>
        public SessionPlayer Target { get; private set; }

        private IReadOnlyList<SessionPlayer> alive;
        private CameraMode restoreMode;
        private Transform restoreTarget;
        private CinemachineOrbitalFollow orbit;

        /// <summary>
        /// Уйти в наблюдатели. Список живых берётся по ссылке и перечитывается
        /// каждый кадр — отдельно сообщать о выбывших не нужно.
        /// </summary>
        public void Activate(IReadOnlyList<SessionPlayer> alivePlayers)
        {
            if (cameraController == null)
            {
                Debug.LogWarning($"{name}: SpectatorCamera без MinigameCameraController — смотреть нечем.", this);
                return;
            }

            bool wasActive = IsActive;
            if (!wasActive)
            {
                restoreMode = cameraController.CurrentMode;
                restoreTarget = cameraController.CurrentTarget;
            }

            IsActive = true;
            alive = alivePlayers;

            nextAction?.action.Enable();
            previousAction?.action.Enable();

            // Наблюдатель камеру не крутит: он смотрит чужими глазами, и своя
            // мышь тут только увела бы кадр от того, что делает игрок.
            if (cameraRig != null)
            {
                cameraRig.SetLookSuspended(true);
                if (orbit == null)
                {
                    orbit = cameraRig.GetComponent<CinemachineOrbitalFollow>();
                }
            }

            SetLocalControlEnabled(false);

            // Повторный вызов только обновляет список целей. Цель не
            // перевыбираем: мини-игра зовёт Activate и на смерть, и следом на
            // исчезновение тела, и камера прыгала бы на случайного дважды.
            if (!wasActive)
            {
                PickRandomTarget();
            }
        }

        /// <summary>Вернуть камеру своему персонажу.</summary>
        public void Deactivate()
        {
            if (!IsActive)
            {
                return;
            }

            IsActive = false;
            alive = null;
            Target = null;

            nextAction?.action.Disable();
            previousAction?.action.Disable();

            // Своя камера возвращается игроку вместе с управлением.
            cameraRig?.SetLookSuspended(false);

            hud?.HideSpectatorTarget();
            SetLocalControlEnabled(true);

            if (cameraController != null && restoreTarget != null)
            {
                cameraController.Apply(restoreMode, restoreTarget);
            }
        }

        private void Update()
        {
            if (!IsActive)
            {
                return;
            }

            // Цель выбыла или отвалилась по сети — уходим на следующую живую сами.
            if (!IsWatchable(Target))
            {
                Advance(1);
                return;
            }

            if (WasPressed(nextAction) || WasMousePressed(true))
            {
                Advance(1);
            }
            else if (WasPressed(previousAction) || WasMousePressed(false))
            {
                Advance(-1);
            }
        }

        /// <summary>
        /// Вести камеру за взглядом того, за кем смотрим: это трансляция, а не
        /// свободный обзор. Азимут берём с тела — оно поворачивается туда же,
        /// куда смотрит игрок, и по сети ходит через NetworkTransform.
        ///
        /// Наклон берём постоянный: свой угол наблюдаемого не реплицируется
        /// (спека Duck Hunt, 10.1 — взгляд уходит только вместе с выстрелом),
        /// и взять его неоткуда.
        ///
        /// В LateUpdate, после того как тело за этот кадр уже переместилось:
        /// иначе камера отставала бы от цели ровно на кадр и картинка дрожала.
        /// </summary>
        private void LateUpdate()
        {
            if (!IsActive || orbit == null || !IsWatchable(Target))
            {
                return;
            }

            float yaw = Target.Avatar.transform.eulerAngles.y;

            InputAxis horizontal = orbit.HorizontalAxis;
            horizontal.Value = Mathf.Repeat(yaw + 180f, 360f) - 180f;
            orbit.HorizontalAxis = horizontal;

            InputAxis vertical = orbit.VerticalAxis;
            vertical.Value = spectatorPitch;
            orbit.VerticalAxis = vertical;
        }

        /// <summary>
        /// Первая цель — случайный живой, а не первый по списку. Обход от
        /// случайной точки, а не один бросок: выпасть может уже выбывший, и
        /// тогда камера осталась бы вовсе без цели.
        /// </summary>
        private void PickRandomTarget()
        {
            int count = alive != null ? alive.Count : 0;
            if (count == 0)
            {
                SetTarget(null);
                return;
            }

            int offset = Random.Range(0, count);
            for (int i = 0; i < count; i++)
            {
                SessionPlayer candidate = alive[(offset + i) % count];
                if (IsWatchable(candidate))
                {
                    SetTarget(candidate);
                    return;
                }
            }

            SetTarget(null);
        }

        /// <summary>
        /// ЛКМ — следующий, ПКМ — предыдущий.
        ///
        /// Клик по интерфейсу не листает: на экране паузы кнопка «Продолжить»
        /// жмётся той же левой, и без этой проверки один клик и нажимал бы
        /// кнопку, и уводил камеру на другого игрока.
        /// </summary>
        private static bool WasMousePressed(bool next)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return false;
            }

            EventSystem events = EventSystem.current;
            if (events != null && events.IsPointerOverGameObject())
            {
                return false;
            }

            return next
                ? mouse.leftButton.wasPressedThisFrame
                : mouse.rightButton.wasPressedThisFrame;
        }

        /// <summary>Перейти к следующей живой цели по кругу в заданную сторону.</summary>
        private void Advance(int step)
        {
            int count = alive != null ? alive.Count : 0;
            if (count == 0)
            {
                SetTarget(null);
                return;
            }

            int start = IndexOf(Target);

            for (int i = 1; i <= count; i++)
            {
                // Цели в списке уже нет — начинаем обход с края, а не от неё.
                int index = start < 0
                    ? (step > 0 ? i - 1 : count - i)
                    : (((start + step * i) % count) + count) % count;

                SessionPlayer candidate = alive[index];
                if (IsWatchable(candidate))
                {
                    SetTarget(candidate);
                    return;
                }
            }

            SetTarget(null);
        }

        private void SetTarget(SessionPlayer player)
        {
            if (Target == player)
            {
                return;
            }

            Target = player;

            if (player == null)
            {
                // Живых не осталось: камера замирает на последней позиции.
                hud?.HideSpectatorTarget();
                return;
            }

            cameraController.Apply(spectatorMode, player.Avatar.transform);
            hud?.ShowSpectatorTarget(player.DisplayName);
        }

        private int IndexOf(SessionPlayer player)
        {
            if (player == null || alive == null)
            {
                return -1;
            }

            for (int i = 0; i < alive.Count; i++)
            {
                if (alive[i] == player)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsWatchable(SessionPlayer player) =>
            player != null && player.Avatar != null && player.Avatar.gameObject.activeInHierarchy;

        /// <summary>
        /// Наблюдатель не должен влиять на раунд. Ридер, у которого управление
        /// отобрано навсегда (манекен, чужая сетевая копия), не будим.
        /// </summary>
        private static void SetLocalControlEnabled(bool enabled)
        {
            SessionPlayer local = SessionScoreboard.Current?.LocalPlayer;
            if (local?.Avatar == null || !local.Avatar.TryGetComponent(out PlayerInputReader reader))
            {
                return;
            }

            if (reader.LocallyControlled)
            {
                reader.enabled = enabled;
            }
        }

        private static bool WasPressed(InputActionReference reference) =>
            reference != null && reference.action.WasPerformedThisFrame();
    }
}
