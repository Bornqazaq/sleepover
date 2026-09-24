using UnityEngine;
using Unity.Cinemachine;
using Igruha.Core.Minigame;
using Igruha.Core.Player;

namespace Igruha.Core.CameraSystems
{
    /// <summary>
    /// Переключение режима камеры под тип мини-игры (GDD 9.2).
    /// Реализованы ThirdPerson (PartyCameraRig) и FirstPerson (риг ведущего);
    /// TopDown/Fixed — заготовки: добавить риг и включить в switch.
    /// </summary>
    public sealed class MinigameCameraController : MonoBehaviour
    {
        [Tooltip("3rd-person риг (PartyCameraRig) — режим по умолчанию")]
        [SerializeField] private CinemachineCamera thirdPersonRig;
        [Tooltip("1st-person риг ведущего (FirstPersonCameraRig)")]
        [SerializeField] private CinemachineCamera firstPersonRig;
        [Tooltip("Заготовка под top-down риг")]
        [SerializeField] private CinemachineCamera topDownRig;
        [Tooltip("Заготовка под fixed-риг")]
        [SerializeField] private CinemachineCamera fixedRig;

        /// <summary>Режим, включённый сейчас — чтобы было куда вернуться после спектатора.</summary>
        public CameraMode CurrentMode { get; private set; } = CameraMode.ThirdPerson;

        /// <summary>За кем камера следит сейчас.</summary>
        public Transform CurrentTarget { get; private set; }

        public void SetTutorialLookSuspended(bool suspended)
        {
            if (thirdPersonRig != null && thirdPersonRig.TryGetComponent(out ThirdPersonCameraRig look))
                look.SetLookSuspended(suspended);
        }

        public void Apply(CameraMode mode, Transform followTarget)
        {
            followTarget = ChestLevel(mode, followTarget);
            CinemachineCamera rig = SelectRig(mode);
            if (rig == null)
            {
                Debug.LogWarning($"{name}: риг для режима {mode} не назначен — остаёмся на ThirdPerson", this);
                rig = thirdPersonRig;
            }

            // Цель ставится до включения рига: 1st-person при старте подхватывает
            // разворот персонажа, и без цели он смотрел бы в произвольную сторону.
            if (rig != null && followTarget != null)
            {
                rig.Target.TrackingTarget = followTarget;
                SnapBehind(rig, followTarget);

                // Камера получает героя сразу после спавна, а спавн — это несколько
                // длинных кадров. Мусорная дельта от захвата курсора долетает уже
                // после них, поэтому защиту взводим здесь, а не только при включении рига.
                if (rig.TryGetComponent(out ThirdPersonCameraRig lookRig))
                {
                    lookRig.ArmLookGuard();
                }

                CurrentTarget = followTarget;
            }

            CurrentMode = mode;

            SetRigActive(thirdPersonRig, rig == thirdPersonRig);
            SetRigActive(firstPersonRig, rig == firstPersonRig);
            SetRigActive(topDownRig, rig == topDownRig);
            SetRigActive(fixedRig, rig == fixedRig);
        }

        /// <summary>
        /// Поднять точку привязки от ступней к груди персонажа.
        ///
        /// Орбита третьего лица центрируется на цели: с корнем персонажа её
        /// центр оказывается на полу, камера висит на высоте пояса и смотрит
        /// снизу вверх в ноги. Хаб передавал <c>CameraTarget</c> и кадрировал
        /// правильно, а мини-игры — <c>transform</c>, и кадр в них был другим.
        /// Приведение живёт здесь, в единственной точке входа, чтобы десять
        /// игр не повторяли одно и то же и одиннадцатая не забыла.
        ///
        /// Вид от первого лица и фикс-риги не трогаем: первый сам ищет голову,
        /// вторым передают не персонажа, а точку в сцене. Вид сверху поднимаем
        /// наравне с третьим лицом — он и включается-то подменой того же рига.
        /// </summary>
        private static Transform ChestLevel(CameraMode mode, Transform followTarget)
        {
            bool followsPlayer = mode == CameraMode.ThirdPerson || mode == CameraMode.TopDown;
            if (!followsPlayer || followTarget == null)
            {
                return followTarget;
            }

            PlayerController player = followTarget.GetComponentInParent<PlayerController>();
            return player != null ? player.CameraTarget : followTarget;
        }

        /// <summary>
        /// Поставить камеру за спину цели тем же кадром, без доводки из прежней позиции.
        /// Простой смены TrackingTarget мало: Cinemachine считает предыдущее состояние
        /// валидным и демпфирует переход, поэтому после выбора персонажа камера секунду
        /// ползёт к нему через всю комнату — и по дороге ныряет в лестницу и стены
        /// (deoccluder помогает только когда цель уже перекрыта, а не в полёте).
        /// </summary>
        private static void SnapBehind(CinemachineCamera rig, Transform followTarget)
        {
            if (rig.TryGetComponent(out CinemachineOrbitalFollow orbit))
            {
                // Орбита живёт в мировых координатах (BindingMode.WorldSpace), её угол
                // не знает про разворот героя. Один раз доворачиваем ось ему за спину:
                // это стартовая установка, а не привязка — дальше риг снова крутит камеру
                // сам, независимо от того, куда повернётся персонаж.
                InputAxis horizontal = orbit.HorizontalAxis;
                horizontal.Value = Mathf.Repeat(followTarget.eulerAngles.y + 180f, 360f) - 180f;
                orbit.HorizontalAxis = horizontal;

                InputAxis vertical = orbit.VerticalAxis;
                vertical.Value = Mathf.Clamp(vertical.Center, vertical.Range.x, vertical.Range.y);
                orbit.VerticalAxis = vertical;
            }

            // Сброс демпфирования: ближайший пересчёт риг сделает «с нуля» и встанет
            // в конечную точку сразу, а не поедет в неё.
            rig.PreviousStateIsValid = false;
        }

        private CinemachineCamera SelectRig(CameraMode mode)
        {
            switch (mode)
            {
                case CameraMode.FirstPerson: return firstPersonRig;
                case CameraMode.TopDown: return topDownRig;
                case CameraMode.Fixed: return fixedRig;
                default: return thirdPersonRig;
            }
        }

        private static void SetRigActive(CinemachineCamera rig, bool active)
        {
            if (rig != null)
            {
                rig.gameObject.SetActive(active);
            }
        }
    }
}
