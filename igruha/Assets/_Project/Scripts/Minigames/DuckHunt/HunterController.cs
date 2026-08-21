using System;
using UnityEngine;
using Igruha.Core.Arena;
using Igruha.Core.CameraSystems;
using Igruha.Core.Combat;
using Igruha.Core.Player;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Роль Охотника: лифт под его управлением, hitscan-оружие и обзор от
    /// первого лица. Единственный в мини-игре, кто не бегает.
    ///
    /// Сам по себе он ничего не решает. Готовый выстрел уходит наружу событием
    /// <see cref="DuckShot"/>, а смерть засчитывает мини-игра — только она знает
    /// время раунда, прогресс жертвы и условие конца. В сетевой фазе
    /// <see cref="TryFire"/> становится телом ServerRpc: клиент присылает точку
    /// и направление, сервер проверяет обойму с задержкой, сам делает луч и
    /// накладывает разброс. Ничего из этого переписывать не придётся.
    ///
    /// Компонент вешается на аватар в рантайме — состав игроков известен только
    /// после раздачи ролей.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class HunterController : MonoBehaviour
    {
        /// <summary>Запасная высота глаза, м — только если у тела нет капсулы.</summary>
        private const float DefaultEyeHeight = 1.5f;
        /// <summary>На сколько глаз ниже макушки, м. То же значение, что у рига первого лица.</summary>
        private const float EyeDropFromTop = 0.15f;

        /// <summary>Выстрел попал в Утку: жертва, точка попадания, импульс отлёта.</summary>
        public event Action<PlayerElimination, Vector3, Vector3> DuckShot;

        /// <summary>Выстрел состоялся — для звука, трассера и вспышки в арт-фазе.</summary>
        public event Action<HitscanWeapon.HitResult> Fired;

        private DuckHuntConfig config;
        private RidePlatform elevator;
        private FirstPersonCameraRig cameraRig;
        private HitscanWeapon weapon;
        private PlayerController motor;
        private PlayerInputReader input;

        /// <summary>Оружие Охотника — HUD читает у него обойму и перезарядку.</summary>
        public HitscanWeapon Weapon => weapon;

        /// <summary>Роль активна: ввод читается, выстрел разрешён.</summary>
        public bool Active { get; private set; }

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            input = GetComponent<PlayerInputReader>();
        }

        /// <summary>
        /// Занять место на лифте. Тело блокируется целиком: ввод движения
        /// уходит на подъём платформы, а не на ноги. Иммунитет к импульсам —
        /// подстраховка: достать Охотника нельзя и так, между башней и лифтом
        /// шестнадцать ШП пустоты, но роль, обязанная простоять весь раунд на
        /// одной платформе, не должна зависеть от случайного толчка.
        /// </summary>
        public void Attach(DuckHuntConfig gameConfig, RidePlatform platform, FirstPersonCameraRig rig)
        {
            config = gameConfig;
            elevator = platform;
            cameraRig = rig;

            motor.MovementLocked = true;
            motor.ImpulseImmune = true;

            SetRivalInputConsumersEnabled(false);
            EnsureWeapon();
            ApplyCameraSettings();

            // Пассажир общей платформе назначается явно: она возит того, кого
            // ей назвали, а не всех, кто оказался сверху. Для Охотника это и
            // нужно — на лифте он один на весь раунд.
            elevator?.SetPassenger(motor);
            elevator?.SnapTo(config != null ? config.ElevatorMinHeightUnits : 0f);
            Active = true;
        }

        /// <summary>Снять роль: тело снова обычное. Нужно при пересдаче ролей в соло-тесте.</summary>
        public void Detach()
        {
            Active = false;
            elevator?.SetAxis(0f);

            // Пассажира снимаем вместе с ролью: платформа держит ссылку на
            // тело и продолжила бы возить его после пересдачи ролей.
            elevator?.SetPassenger(null);

            if (motor != null)
            {
                motor.MovementLocked = false;
                motor.ImpulseImmune = false;
            }

            SetRivalInputConsumersEnabled(true);
        }

        /// <summary>
        /// Отобрать нажатия у обычных способностей персонажа. Удар и
        /// взаимодействие читают те же кнопки, на которых у Охотника висят
        /// выстрел и перезарядка, и гасят их у себя безусловно — кто первым
        /// обновился, тот и забрал. Достать кулаком Охотнику всё равно некого,
        /// а нажать он ничего не может: он заперт на платформе.
        /// </summary>
        private void SetRivalInputConsumersEnabled(bool enabled)
        {
            if (TryGetComponent(out PlayerPushAbility push))
            {
                push.enabled = enabled;
            }

            if (TryGetComponent(out Igruha.Core.Interaction.PlayerInteractor interactor))
            {
                interactor.enabled = enabled;
            }
        }

        private void EnsureWeapon()
        {
            if (weapon == null && !TryGetComponent(out weapon))
            {
                weapon = gameObject.AddComponent<HitscanWeapon>();
            }

            if (config == null)
            {
                return;
            }

            weapon.Configure(
                config.MagazineSize,
                config.FireDelay,
                config.ReloadDuration,
                config.ShotRangeUnits,
                config.ShotMask);
        }

        private void ApplyCameraSettings()
        {
            if (cameraRig == null || config == null)
            {
                return;
            }

            cameraRig.SetPitchLimits(-config.CameraPitchLimit, config.CameraPitchLimit);
            cameraRig.SetMaxTurnSpeed(config.CameraTurnSpeed);
            cameraRig.SetHorizontalFieldOfView(config.HorizontalFieldOfView);
        }

        private void Update()
        {
            if (!Active)
            {
                return;
            }

            DriveElevator();
            HandleWeaponInput();
        }

        /// <summary>
        /// Ось лифта — это ось движения персонажа. Тело заблокировано, поэтому
        /// «вперёд» ему всё равно некуда идти, а игроку не нужно запоминать
        /// отдельные клавиши.
        /// </summary>
        private void DriveElevator()
        {
            if (elevator == null)
            {
                return;
            }

            float axis = input != null && input.enabled ? input.MoveInput.y : 0f;
            elevator.SetAxis(axis);

            // Разброс зависит от того, едет ли платформа: стрелять на ходу
            // можно, но заметно хуже.
            if (weapon != null && config != null)
            {
                weapon.SpreadAngle = elevator.Moving ? config.SpreadMoving : config.SpreadStanding;
            }
        }

        private void HandleWeaponInput()
        {
            if (input == null || !input.enabled || weapon == null)
            {
                return;
            }

            // Выстрел висит на той же кнопке, что удар у остальных ролей:
            // отдельного действия ради одной роли в схеме ввода не заводим.
            if (input.PushPressed)
            {
                input.ConsumePush();
                TryFire();
            }

            if (input.InteractPressed)
            {
                input.ConsumeInteract();
                weapon.Reload();
            }
        }

        /// <summary>Выстрелить туда, куда смотрит камера.</summary>
        public bool TryFire()
        {
            TryGetAim(out Vector3 origin, out Vector3 direction);
            return TryFire(origin, direction);
        }

        /// <summary>
        /// Выстрелить из точки в направлении. Настоящая точка выстрела: в
        /// сетевой фазе ровно это тело переезжает в FireServerRpc, куда клиент
        /// присылает origin и direction, а проверка обоймы, луч и разброс
        /// остаются на сервере.
        ///
        /// Отдельно от <see cref="TryFire()"/> она нужна ещё и болванке
        /// соло-теста: та целится расчётом, а не камерой живого игрока.
        /// </summary>
        public bool TryFire(Vector3 origin, Vector3 direction)
        {
            if (!Active || weapon == null)
            {
                return false;
            }

            if (!weapon.TryFire(origin, direction, out HitscanWeapon.HitResult result))
            {
                return false;
            }

            Fired?.Invoke(result);
            ResolveHit(result);
            return true;
        }

        /// <summary>Точка, из которой уходит луч — глаз Охотника. Болванке нужна та же, что и живому.</summary>
        public Vector3 GetMuzzlePosition()
        {
            TryGetAim(out Vector3 origin, out _);
            return origin;
        }

        /// <summary>Двигать лифт напрямую — для болванки соло-теста.</summary>
        public void SetElevatorAxis(float axis) => elevator?.SetAxis(axis);

        /// <summary>
        /// Откуда и куда уходит луч. Целимся ровно тем, что видит игрок, но
        /// только если его риг сейчас работает: у Охотника-болванки и у чужой
        /// копии в сетевой игре риг выключен и стоит в начале координат —
        /// выстрел уходил бы из угла карты. Тогда берём глаз самого тела.
        ///
        /// Своя точка вылета у ствола появится в арт-фазе: трассер поедет от
        /// дула, а решает попадание всё равно луч из глаза.
        /// </summary>
        private void TryGetAim(out Vector3 origin, out Vector3 direction)
        {
            if (cameraRig != null && cameraRig.isActiveAndEnabled)
            {
                origin = cameraRig.transform.position;
                direction = cameraRig.transform.forward;
                return;
            }

            origin = transform.position + Vector3.up * ResolveEyeHeight();
            direction = motor != null ? motor.Facing : transform.forward;
        }

        /// <summary>Высота глаза от капсулы: у персонажей разный рост, числом её не задать.</summary>
        private float ResolveEyeHeight()
        {
            if (!TryGetComponent(out CapsuleCollider capsule))
            {
                return DefaultEyeHeight;
            }

            return Mathf.Max(capsule.center.y + capsule.height * 0.5f - EyeDropFromTop, capsule.radius);
        }

        private void ResolveHit(HitscanWeapon.HitResult result)
        {
            if (!result.Hit || result.Collider == null)
            {
                return;
            }

            PlayerElimination victim = result.Collider.GetComponentInParent<PlayerElimination>();

            // В себя не стреляем: луч выходит из глаза, то есть изнутри
            // собственной капсулы, и на некоторых формах коллайдера способен
            // её задеть.
            if (victim == null || victim.gameObject == gameObject || victim.IsEliminated)
            {
                return;
            }

            float force = config != null ? config.DeathImpulse : 18f;
            float lift = config != null ? config.DeathImpulseLift : 0.4f;
            Vector3 impulse = (result.Direction + Vector3.up * lift).normalized * force;

            DuckShot?.Invoke(victim, result.Point, impulse);
        }
    }
}
