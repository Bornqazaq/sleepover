using System;
using UnityEngine;
using Igruha.Core.Arena;
using Igruha.Core.CameraSystems;
using Igruha.Core.Combat;
using Igruha.Core.Minigame;
using Igruha.Core.Player;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Роль Охотника: лифт под его управлением, hitscan-оружие и обзор от
    /// первого лица. Единственный в мини-игре, кто не бегает.
    ///
    /// Сам по себе он ничего не решает. Готовый выстрел уходит наружу событием
    /// <see cref="DuckShot"/>, а смерть засчитывает мини-игра — только она знает
    /// время раунда, прогресс жертвы и условие конца.
    ///
    /// Про сеть роль не знает ничего: и лифт, и оружие ведут себя как обычно,
    /// а машина, которая ими не распоряжается, подставляет ей два шва —
    /// <see cref="Relay"/> для намерений и <c>DrivenExternally</c> у оружия.
    /// Ни один из них не переписывает логику ниже.
    ///
    /// Компонент вешается на аватар в рантайме — состав игроков известен только
    /// после раздачи ролей.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class HunterController : MonoBehaviour, ISpectatorView
    {
        /// <summary>Запасная высота глаза, м — только если у тела нет капсулы.</summary>
        private const float DefaultEyeHeight = 1.5f;
        /// <summary>На сколько глаз ниже макушки, м. То же значение, что у рига первого лица.</summary>
        private const float EyeDropFromTop = 0.15f;

        /// <summary>Запасная дальность прицельного луча, м — только если конфиг не подставлен.</summary>
        private const float DefaultAimRange = 43.2f;

        /// <summary>
        /// На сколько луч прицела начинается дальше собственного тела, м.
        /// Полметра с запасом перекрывают капсулу: иначе центр экрана упирается
        /// в затылок Охотника, и прицел показывает его собственную макушку.
        /// </summary>
        private const float AimSkipPastBody = 0.5f;



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
        private CharacterAnimatorDriver animatorDriver;

        /// <summary>Выданная на роль модель ружья. Живёт ровно столько, сколько роль.</summary>
        private GameObject rifleProp;

        /// <summary>Оружие Охотника — HUD читает у него обойму и перезарядку.</summary>
        public HitscanWeapon Weapon => weapon;

        /// <summary>
        /// Куда уходят намерения роли — ход лифта, выстрел, перезарядка, —
        /// когда решает не эта машина. Пусто в одиночной сцене: там всё
        /// применяется на месте. В сетевой катке решает сам релей: что-то
        /// он отправляет серверу, что-то молча глотает (см. IHunterRelay).
        /// </summary>
        public IHunterRelay Relay { get; set; }

        /// <summary>Роль активна: ввод читается, выстрел разрешён.</summary>
        public bool Active { get; private set; }

        /// <summary>Риг первого лица этой роли — сетевая половина шлёт с него прицел.</summary>
        public FirstPersonCameraRig Rig => cameraRig;

        /// <summary>
        /// Прицелом правит эта машина: она хозяин роли. У остальных углы
        /// приезжают с сервера и лежат в <see cref="networkYaw"/>.
        /// </summary>
        public bool AimIsLocal { get; private set; }

        /// <summary>Присланный сервером прицел: азимут и наклон.</summary>
        private float networkYaw;
        private float networkPitch;

        /// <summary>Кто правит прицелом. Ставит сетевая половина после каждой выдачи роли.</summary>
        public void SetAimLocal(bool local) => AimIsLocal = local;

        /// <summary>Прицел, посчитанный сервером. Нужен только для показа: выстрел несёт своё направление.</summary>
        public void ApplyNetworkAim(float yaw, float pitch)
        {
            networkYaw = yaw;
            networkPitch = pitch;
        }

        /// <summary>
        /// Что видит Охотник. Наблюдателю отдаём первое лицо: смотреть за
        /// стрелком из-за спины — не то же самое, что видеть его прицел.
        ///
        /// У хозяина роли углы берём с рига живьём, у остальных — присланные:
        /// на чужой машине риг этой роли не крутится вовсе.
        ///
        /// Роль снята — точки обзора нет. Компонент при этом остаётся на
        /// аватаре (<see cref="Detach"/> его не удаляет), и без этой проверки
        /// бывший Охотник, ставший Уткой, показывал бы наблюдателю первое лицо
        /// по углам прошлого раунда.
        /// </summary>
        public bool TryGetView(out CameraMode mode, out float yaw, out float pitch)
        {
            mode = CameraMode.FirstPerson;
            yaw = 0f;
            pitch = 0f;

            if (!Active)
            {
                return false;
            }

            if (AimIsLocal && cameraRig != null)
            {
                yaw = cameraRig.Yaw;
                pitch = cameraRig.Pitch;
                return true;
            }

            yaw = networkYaw;
            pitch = networkPitch;
            return true;
        }

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            input = GetComponent<PlayerInputReader>();
            TryGetComponent(out animatorDriver);
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

            // Ставить платформу на нижнюю отметку вправе только та машина,
            // которая ею и правит: у остальных высота приезжает с сервера,
            // и местный сдвиг они всё равно тут же отыграют назад.
            if (elevator != null && !elevator.FollowsNetwork)
            {
                elevator.SnapTo(config != null ? config.ElevatorMinHeightUnits : 0f);
            }

            // Ружьё в руках всю роль: пока не стреляют, стойка держится
            // неподвижной — это нулевая скорость состояния, а не отдельный клип.
            animatorDriver?.SetRifleAiming(true);
            ShowRifleProp(true);

            // Своё тело Охотнику видно: иначе руки и ствол не в кадре, и вся
            // анимация выстрела играет для кого угодно, кроме него самого.
            cameraRig?.SetOwnModelVisible(true);

            Active = true;
        }

        /// <summary>
        /// Выдать или забрать модель ружья.
        ///
        /// Ружьё крепится к кости правой кисти аватара и уничтожается вместе с
        /// ролью. Ни префаб персонажа, ни аниматор при этом не меняются — они
        /// заморожены (igruha/CLAUDE.md, раздел 0), и трогать их ради предмета
        /// в руке нельзя. Слой стойки с ружьём уже существует отдельно; здесь
        /// добавляется только то, что этой стойке до сих пор не хватало —
        /// собственно ружьё.
        ///
        /// Модель чисто визуальная: коллайдеры срезаются, попадание считает
        /// hitscan-луч из <see cref="HitscanWeapon"/>, а не геометрия ствола.
        /// </summary>
        private void ShowRifleProp(bool show)
        {
            if (!show)
            {
                if (rifleProp != null)
                {
                    Destroy(rifleProp);
                    rifleProp = null;
                }

                return;
            }

            if (rifleProp != null || config == null || config.RifleProp == null)
            {
                return;
            }

            Transform hand = ResolveRightHand();
            if (hand == null)
            {
                return;
            }

            rifleProp = Instantiate(config.RifleProp, hand);
            rifleProp.name = "RifleProp";
            rifleProp.transform.localPosition = config.RifleLocalPosition;
            rifleProp.transform.localRotation = Quaternion.Euler(config.RifleLocalRotation);
            rifleProp.transform.localScale = Vector3.one * config.RifleScale;

            var colliders = rifleProp.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Destroy(colliders[i]);
            }
        }

        /// <summary>Кость правой кисти аватара. Скелет у всех восьмерых Humanoid, поэтому кость ищется по роли, а не по имени.</summary>
        private Transform ResolveRightHand()
        {
            var animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
            {
                return null;
            }

            return animator.GetBoneTransform(HumanBodyBones.RightHand);
        }

        /// <summary>Снять роль: тело снова обычное. Нужно при пересдаче ролей в соло-тесте.</summary>
        public void Detach()
        {
            Active = false;

            // Ноль уходит тем же путём, что и ход: на клиенте это последнее
            // намерение, иначе сервер так и будет держать последнюю ось и
            // повезёт лифт дальше без хозяина.
            SetElevatorAxis(0f);

            // Пассажира снимаем вместе с ролью: платформа держит ссылку на
            // тело и продолжила бы возить его после пересдачи ролей.
            elevator?.SetPassenger(null);
            animatorDriver?.SetRifleAiming(false);
            ShowRifleProp(false);
            cameraRig?.SetOwnModelVisible(false);

            // Риг общий на все игры: отвод камеры снимаем вместе с ролью,
            // иначе следующая роль первого лица получит вид из-за плеча.
            cameraRig?.SetShoulderOffset(Vector3.zero);

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
            cameraRig.SetShoulderOffset(config.CameraOffset);
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
            SetElevatorAxis(axis);

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
                RequestReload();
            }
        }

        /// <summary>Выстрелить туда, куда смотрит камера.</summary>
        public bool TryFire()
        {
            TryGetAim(out Vector3 origin, out Vector3 direction);
            return TryFire(origin, direction);
        }

        /// <summary>
        /// Выстрелить из точки в направлении. Единственная точка выстрела:
        /// сюда приходит и живой игрок, и болванка соло-теста (та целится
        /// расчётом, а не камерой), и сервер, получивший намерение клиента.
        ///
        /// Когда решает не эта машина, направление уходит серверу чистым —
        /// обойму, задержку, конус разброса и сам луч считает он. Отсюда и
        /// <c>false</c> в ответ: выстрел не состоялся <b>здесь</b>, а результат
        /// приедет обратно готовым.
        /// </summary>
        public bool TryFire(Vector3 origin, Vector3 direction)
        {
            if (!Active || weapon == null)
            {
                return false;
            }

            if (Relay != null && Relay.TryRelayFire(origin, direction))
            {
                return false;
            }

            if (!weapon.TryFire(origin, direction, out HitscanWeapon.HitResult result))
            {
                return false;
            }

            RaiseShot(result);
            ResolveHit(result);
            return true;
        }

        /// <summary>
        /// Перезарядить вручную. Тем же путём, что и выстрел: обойма — состояние
        /// раунда, и менять её вправе только тот, кто её ведёт.
        /// </summary>
        public void RequestReload()
        {
            if (!Active || weapon == null)
            {
                return;
            }

            if (Relay != null && Relay.TryRelayReload())
            {
                return;
            }

            weapon.Reload();
        }

        /// <summary>
        /// Отыграть выстрел, посчитанный сервером: звук, вспышка и трассер
        /// от дула до точки попадания. Коллайдера здесь нет и быть не может —
        /// во что попал луч, решено уже на сервере, а этой машине нужна только
        /// картинка.
        /// </summary>
        public void PlayRemoteShot(Vector3 origin, Vector3 point, bool hit)
        {
            Vector3 direction = point - origin;
            direction = direction.sqrMagnitude > Mathf.Epsilon ? direction.normalized : transform.forward;

            RaiseShot(new HitscanWeapon.HitResult(hit, origin, point, direction, null));
        }

        /// <summary>
        /// Выстрел состоялся — отыграть его на этой машине. Одна точка на оба
        /// случая: и когда стреляли здесь, и когда результат приехал с сервера.
        /// Анимация висит на состоявшемся выстреле, а не на нажатии: щелчок
        /// в кулдаун или по пустой обойме выстрелом не является, и дёргать
        /// ствол на нём нечего.
        /// </summary>
        private void RaiseShot(HitscanWeapon.HitResult result)
        {
            animatorDriver?.PlayFire();
            Fired?.Invoke(result);
        }

        /// <summary>Точка, из которой уходит луч — глаз Охотника. Болванке нужна та же, что и живому.</summary>
        public Vector3 GetMuzzlePosition()
        {
            TryGetAim(out Vector3 origin, out _);
            return origin;
        }

        /// <summary>
        /// Двигать лифт. Единственная точка: и живой игрок, и болванка
        /// соло-теста идут через неё. Когда высотой распоряжается сервер, ось
        /// уходит ему намерением, а платформу здесь не трогаем — иначе она
        /// поедет от двух рук сразу и задёргается.
        /// </summary>
        public void SetElevatorAxis(float axis)
        {
            if (Relay != null && Relay.TryRelayElevatorAxis(axis))
            {
                return;
            }

            elevator?.SetAxis(axis);
        }

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
                // Стреляем из глаза, а не из камеры: камера отведена за плечо,
                // и луч из неё пошёл бы сквозь собственное тело — первым
                // попаданием стала бы своя капсула.
                origin = cameraRig.EyePosition;

                // А целимся туда, куда показывает прицел. Это отдельное
                // направление: центр экрана — ось камеры, а она за плечом,
                // и прямая «глаз → взгляд» с ней не совпадает. Считать надо
                // именно так, иначе прицел врёт ровно на величину отвода.
                direction = (ResolveAimPoint() - origin).normalized;
                return;
            }

            origin = transform.position + Vector3.up * ResolveEyeHeight();
            direction = motor != null ? motor.Facing : transform.forward;
        }

        /// <summary>
        /// Точка под прицелом — то, во что упирается центр экрана. Луч пускается
        /// от камеры, но начинается за спиной персонажа: иначе первым же, во что
        /// он упрётся, окажется собственный затылок.
        ///
        /// Не попал ни во что — берём точку на предельной дальности: целиться
        /// в небо игроку никто не запрещает.
        /// </summary>
        private Vector3 ResolveAimPoint()
        {
            Transform view = cameraRig.transform;
            float range = config != null ? config.ShotRangeUnits : DefaultAimRange;
            float skip = Vector3.Distance(view.position, cameraRig.EyePosition) + AimSkipPastBody;
            Vector3 start = view.position + view.forward * skip;

            LayerMask mask = config != null ? config.ShotMask : ~0;
            if (Physics.Raycast(start, view.forward, out RaycastHit hit, range, mask, QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }

            return start + view.forward * range;
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
