using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Session;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Состояние одной Утки в сетевом виде. Отдельная структура нужна потому,
    /// что NetworkList умеет только unmanaged-типы, а DuckRecord — класс со
    /// ссылками на компоненты сцены.
    ///
    /// Живёт списком на самой мини-игре, а не компонентом на игроке:
    /// <see cref="DuckProgress"/> вешается на аватар в рантайме, а NetworkBehaviour
    /// в рантайме добавлять нельзя — NGO индексирует их при спавне. Второй
    /// вариант (положить сетевое состояние на общий Player.prefab) поехал бы
    /// во все мини-игры проекта ради правил одной.
    /// </summary>
    public struct DuckNetState : INetworkSerializable, IEquatable<DuckNetState>
    {
        public int PlayerId;

        /// <summary>Этаж, индекс с нуля. Байта хватает: этажей в башне пять.</summary>
        public byte Floor;

        /// <summary>Прогресс вдоль трассы этажа, ШП.</summary>
        public float Progress;

        /// <summary>Номер по порядку прибытия на финиш, с 1. Ноль — не добежал.</summary>
        public byte FinishOrder;

        public bool Dead;

        /// <summary>Время гибели от начала раунда, с. Разводит погибших на одной точке.</summary>
        public float DeathTime;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Floor);
            serializer.SerializeValue(ref Progress);
            serializer.SerializeValue(ref FinishOrder);
            serializer.SerializeValue(ref Dead);
            serializer.SerializeValue(ref DeathTime);
        }

        public bool Equals(DuckNetState other) =>
            PlayerId == other.PlayerId &&
            Floor == other.Floor &&
            FinishOrder == other.FinishOrder &&
            Dead == other.Dead &&
            Mathf.Approximately(Progress, other.Progress) &&
            Mathf.Approximately(DeathTime, other.DeathTime);
    }

    /// <summary>
    /// Сетевая половина Duck Hunt: вешается на тот же объект, что и
    /// <see cref="DuckHuntMinigame"/>. Правила остаются обычным MonoBehaviour
    /// и работают без этого компонента, когда сцену открывают напрямую —
    /// образец взят у <c>CryingAngelsNetwork</c> и <c>StopwatchNetwork</c>.
    ///
    /// Фазу, время раунда и итоговые места везёт
    /// <see cref="Igruha.Networking.NetworkMinigameBridge"/> рядом на том же
    /// объекте — своего канала под них здесь нет намеренно.
    ///
    /// Что реплицируется здесь: кому выпала роль Охотника, момент свистка,
    /// высота лифта, обойма с перезарядкой, состояние каждой Утки и то, какие
    /// ловушки сейчас сработаны.
    ///
    /// Отдельно от состояния ходят <b>события</b>: намерения Охотника наверх
    /// (ось лифта, выстрел, перезарядка) и то, что сервер решил, вниз (выстрел
    /// состоялся, Утка погибла). Они не состояние, а мгновение — держать их
    /// в реплицируемом поле незачем, поэтому это RPC.
    ///
    /// <b>Сервер не толкает состояние из точек изменения, а опрашивает правила
    /// раз в кадр и досылает разницу.</b> Точек изменения много (выстрел, финиш,
    /// провал пола, кнопка, ход лифта), и забытый вызов дал бы молчаливый
    /// рассинхрон вместо ошибки. Запись идёт только при реальном расхождении,
    /// поэтому в покое трафика нет.
    /// </summary>
    public sealed class DuckHuntNetwork : NetworkBehaviour, IHunterRelay
    {
        /// <summary>
        /// Сколько раз в секунду сервер объявляет высоту лифта. Каждый кадр —
        /// это трафик впустую: платформа идёт 3.25 м/с, и за такт при двадцати
        /// в секунду она проходит шестнадцать сантиметров, которые остальные
        /// машины и так доводят ходом.
        /// </summary>
        private const float ElevatorSyncRate = 20f;

        /// <summary>С какого сдвига высота лифта считается новой, м.</summary>
        private const float ElevatorEpsilon = 0.01f;

        /// <summary>
        /// Сколько раз в секунду уходит прогресс Утки вдоль трассы. Как время
        /// раунда в NetworkMinigameBridge: по этому числу не рисуется ничего,
        /// оно решает только места в конце. Смена этажа, финиш и гибель этой
        /// выдержке не подчиняются — они уходят сразу.
        /// </summary>
        private const float ProgressSyncRate = 10f;

        /// <summary>С какого расхождения прогресс считается новым, ШП.</summary>
        private const float ProgressEpsilon = 0.05f;

        /// <summary>С какого расхождения ось лифта считается новой.</summary>
        private const float AxisEpsilon = 0.05f;

        private readonly NetworkVariable<int> hunterPlayerId =
            new NetworkVariable<int>(SpecialRoleHistory.NoPlayer);

        /// <summary>Момент свистка на общих часах. Объявляется один раз за раунд.</summary>
        private readonly NetworkVariable<double> liveTime =
            new NetworkVariable<double>(DuckHuntMinigame.NotAnnounced);

        private readonly NetworkVariable<float> elevatorHeight = new NetworkVariable<float>();

        private readonly NetworkVariable<int> hunterAmmo = new NetworkVariable<int>();

        /// <summary>Момент конца перезарядки на общих часах. Ноль — оружие готово.</summary>
        private readonly NetworkVariable<double> hunterReloadEndsAt = new NetworkVariable<double>();

        /// <summary>Какие ловушки сработаны — по биту на ловушку в порядке массива мини-игры.</summary>
        private readonly NetworkVariable<uint> trapMask = new NetworkVariable<uint>();

        /// <summary>
        /// Куда смотрит Охотник: x — азимут, y — наклон.
        ///
        /// Спека, 10.1, взгляд Охотника постоянно не реплицировала намеренно —
        /// на исход он не влияет, направление уходит вместе с выстрелом. Но
        /// наблюдателю нужен именно взгляд: смотреть за стрелком из-за спины
        /// не то же самое, что видеть его прицел. Поэтому угол всё же ходит,
        /// но дёшево: два числа с выдержкой и только пока роль занята.
        /// </summary>
        private readonly NetworkVariable<Vector2> hunterAim = new NetworkVariable<Vector2>();

        /// <summary>Сколько раз в секунду хозяин роли шлёт прицел.</summary>
        private const float AimSyncRate = 20f;

        /// <summary>Насколько должен сдвинуться угол, чтобы его вообще слать, °.</summary>
        private const float AimEpsilon = 0.35f;

        private float nextAimSend;
        private Vector2 lastSentAim;
        private bool aimSent;

        /// <summary>Роль Охотника на этой машине — с неё читается прицел у хозяина.</summary>
        private HunterController hunterRole;

        private readonly NetworkList<DuckNetState> duckStates = new NetworkList<DuckNetState>();

        /// <summary>Когда каждой Утке можно снова слать прогресс, по playerId.</summary>
        private readonly Dictionary<int, float> nextProgressSync = new Dictionary<int, float>(8);

        /// <summary>Ушедшие, которых осталось разобрать. Почему не сразу — см. OnClientDisconnected.</summary>
        private readonly List<ulong> pendingLeavers = new List<ulong>(4);

        private DuckHuntMinigame game;

        private float nextElevatorSync;

        /// <summary>Список Уток приехал и ещё не применён. Только вне сервера.</summary>
        private bool ducksDirty;

        /// <summary>
        /// Аватар Охотника. По его владению и решается, чей это ввод: спрашиваем
        /// NGO прямо в момент намерения, а не запоминаем ответ.
        ///
        /// Снимок здесь стоил сломанной роли. Раньше в этом поле лежало «я ли
        /// Охотник», посчитанное один раз в момент выдачи роли через ростер
        /// сессии. Стоило ростеру в тот момент ещё не собраться — снимок
        /// оставался ложным навсегда, пересчитывать его было некому, и машина
        /// хозяина роли молча глотала и ось лифта, и выстрел: Охотник не стрелял
        /// и лифт не ехал, без единой ошибки в консоли. Владение аватаром знает
        /// сама сеть, и знает всегда.
        /// </summary>
        private NetworkObject hunterBody;

        private float lastSentAxis;
        private bool axisSent;

        /// <summary>Идёт сетевая катка и эта половина живая.</summary>
        public bool IsActive => IsSpawned;

        /// <summary>Кому выпала роль. <see cref="SpecialRoleHistory.NoPlayer"/> — ещё не выбрана.</summary>
        public int HunterPlayerId => IsSpawned ? hunterPlayerId.Value : SpecialRoleHistory.NoPlayer;

        /// <summary>
        /// Момент свистка, объявленный сервером. Нужен на старте раунда: у
        /// клиента раунд начинается позже сетевого — он ждёт, пока соберётся
        /// состав, — и свисток вполне может быть объявлен раньше. Не перечитай
        /// его начало раунда, отсчёт ждал бы события, которое уже прошло.
        /// </summary>
        public double LiveTime => IsSpawned ? liveTime.Value : DuckHuntMinigame.NotAnnounced;

        /// <summary>Аватаром Охотника управляет эта машина — значит её ввод и есть ввод роли.</summary>
        private bool LocalOwnsHunter => hunterBody != null && hunterBody.IsSpawned && hunterBody.IsOwner;

        private void Awake()
        {
            game = GetComponent<DuckHuntMinigame>();
            if (game == null)
            {
                Debug.LogError($"{name}: DuckHuntNetwork не нашёл DuckHuntMinigame на своём объекте", this);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            hunterPlayerId.OnValueChanged += OnHunterChanged;
            liveTime.OnValueChanged += OnLiveTimeChanged;
            elevatorHeight.OnValueChanged += OnElevatorChanged;
            hunterAmmo.OnValueChanged += OnAmmoChanged;
            hunterReloadEndsAt.OnValueChanged += OnReloadChanged;
            trapMask.OnValueChanged += OnTrapMaskChanged;
            hunterAim.OnValueChanged += OnHunterAimChanged;
            duckStates.OnListChanged += OnDuckStatesChanged;

            if (IsServer)
            {
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
                return;
            }

            // Подключились в середине раунда — догоняем то, что уже решено.
            // Лифт первым: до этого вызова платформа считает, что ею правит
            // эта машина, и стоит на месте, пока Охотник поднимается у всех.
            ApplyElevator();
            ApplyLiveTime();
            ApplyWeapon();
            ApplyTraps();
            ApplyAim();
            ApplyHunter();
            ducksDirty = true;
        }

        public override void OnNetworkDespawn()
        {
            hunterPlayerId.OnValueChanged -= OnHunterChanged;
            liveTime.OnValueChanged -= OnLiveTimeChanged;
            elevatorHeight.OnValueChanged -= OnElevatorChanged;
            hunterAmmo.OnValueChanged -= OnAmmoChanged;
            hunterReloadEndsAt.OnValueChanged -= OnReloadChanged;
            trapMask.OnValueChanged -= OnTrapMaskChanged;
            hunterAim.OnValueChanged -= OnHunterAimChanged;
            duckStates.OnListChanged -= OnDuckStatesChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            if (!IsServer)
            {
                // Сети под платформой больше нет: возвращаем ей право ходить
                // самой, иначе она навсегда замрёт на последней присланной
                // отметке.
                game?.ReleaseNetworkElevator();
            }

            base.OnNetworkDespawn();
        }

        /// <summary>
        /// Кто на этой машине распоряжается лифтом и оружием Охотника.
        /// Зовётся после каждой выдачи роли: Attach создаёт оружие заново,
        /// и режим ему надо проставить снова.
        ///
        /// Лифт и оружие расходятся, и это главное место всей связки.
        /// <b>Оружие ведёт сервер всегда</b> — он один считает попадания.
        /// <b>Ось лифта применяет только та машина, где живёт ввод Охотника.</b>
        /// Копия роли есть у всех, но ввод у неё пустой, и сервер, у которого
        /// Охотник играет с клиента, каждым кадром обнулял бы ось, только что
        /// приехавшую от хозяина роли: лифт не тронулся бы с места вообще.
        /// </summary>
        public void ConfigureHunter(HunterController role, bool isLocal)
        {
            axisSent = false;
            aimSent = false;
            hunterRole = role;
            hunterBody = role != null ? role.GetComponent<NetworkObject>() : null;

            if (role == null)
            {
                return;
            }

            // Кто правит прицелом: у хозяина роли углы живые, у остальных
            // приезжают с сервера. Без этого чужая копия показывала бы
            // наблюдателю свой собственный, никем не тронутый риг.
            role.SetAimLocal(isLocal);

            // Релей ставится всем машинам сетевой катки — что с намерением
            // делать, он решает сам по каждому виду отдельно (см. методы ниже).
            role.Relay = IsSpawned ? this : null;

            if (role.Weapon != null)
            {
                role.Weapon.DrivenExternally = IsSpawned && !IsServer;
            }
        }

        // ========== ЛИФТ ==========

        /// <summary>
        /// Ось хода лифта от машины Охотника. Ответственность берём всегда,
        /// когда решает не эта машина: платформу тут не двигают вовсе, даже
        /// если само намерение никуда не пошло.
        /// </summary>
        public bool TryRelayElevatorAxis(float axis)
        {
            if (!IsSpawned)
            {
                return false;
            }

            // Хост-Охотник водит лифт сам: он и авторитет, и хозяин ввода.
            // Сервер с Охотником-клиентом, наоборот, обязан свою пустую ось
            // проглотить, иначе она каждым кадром затирала бы ту, что приехала
            // от хозяина роли. Остальные машины держат чужую копию роли —
            // им платформу трогать нельзя тем более.
            if (IsServer)
            {
                return !LocalOwnsHunter;
            }

            if (!LocalOwnsHunter)
            {
                return true;
            }

            if (axisSent && Mathf.Abs(axis - lastSentAxis) < AxisEpsilon)
            {
                return true;
            }

            lastSentAxis = axis;
            axisSent = true;
            MoveElevatorRpc(axis);
            return true;
        }

        /// <summary>
        /// Тот самый MoveElevatorServerRpc спеки (§10.1). Имя оканчивается на
        /// Rpc, потому что суффикс ServerRpc в NGO закреплён за устаревшим
        /// атрибутом <c>[ServerRpc]</c>.
        ///
        /// Отправителя берём из RpcParams, а не из аргумента: иначе клиент
        /// возил бы лифт за Охотника. Остальные проверки — на стороне правил,
        /// они одни знают, кому выпала роль и жив ли раунд.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void MoveElevatorRpc(float axis, RpcParams rpcParams = default)
        {
            game?.ServerMoveElevator((int)rpcParams.Receive.SenderClientId, Mathf.Clamp(axis, -1f, 1f));
        }

        // ========== ВЫСТРЕЛ ==========

        /// <summary>
        /// Намерение выстрелить от машины Охотника. Сервер сюда не попадает:
        /// он считает выстрел на месте, ему передавать некому.
        /// </summary>
        public bool TryRelayFire(Vector3 origin, Vector3 direction)
        {
            if (!IsSpawned || IsServer)
            {
                return false;
            }

            if (!LocalOwnsHunter)
            {
                return true;
            }

            FireRpc(origin, direction);
            return true;
        }

        /// <summary>Намерение перезарядиться. Тем же путём, что выстрел: обойму ведёт сервер.</summary>
        public bool TryRelayReload()
        {
            if (!IsSpawned || IsServer)
            {
                return false;
            }

            if (!LocalOwnsHunter)
            {
                return true;
            }

            ReloadRpc();
            return true;
        }

        /// <summary>
        /// Тот самый FireServerRpc спеки (§10.1). Клиент присылает <b>только</b>
        /// точку и чистое направление; обойму, задержку, конус разброса и сам
        /// луч считает сервер, и результат клиентский он не спрашивает вовсе.
        ///
        /// Отправителя берём из RpcParams, а не из аргумента: иначе клиент
        /// стрелял бы за Охотника. Кому выпала роль и жив ли раунд, знают
        /// правила — проверки там.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void FireRpc(Vector3 origin, Vector3 direction, RpcParams rpcParams = default)
        {
            game?.ServerFire((int)rpcParams.Receive.SenderClientId, origin, direction);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void ReloadRpc(RpcParams rpcParams = default)
        {
            game?.ServerReload((int)rpcParams.Receive.SenderClientId);
        }

        /// <summary>
        /// Выстрел состоялся. Это мгновение, а не состояние: звук, вспышка
        /// и трассер нужны один раз и сразу, держать их в реплицируемом поле
        /// незачем.
        /// </summary>
        public void AnnounceShot(Vector3 origin, Vector3 point, bool hit)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            ShotFiredRpc(origin, point, hit);
        }

        /// <summary>Сервер уже отыграл выстрел у себя, поэтому себе не шлём.</summary>
        [Rpc(SendTo.NotServer)]
        private void ShotFiredRpc(Vector3 origin, Vector3 point, bool hit)
        {
            game?.ApplyNetworkShot(origin, point, hit);
        }

        /// <summary>
        /// Утка убита. Точка попадания и импульс отлёта нужны в момент удара
        /// и больше никогда — поэтому RPC, а не поле. Сама поза не
        /// синхронизируется: отлёт каждая машина играет у себя.
        /// </summary>
        public void AnnounceDuckDeath(int playerId, Vector3 hitPoint, Vector3 impulse)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            DuckDiedRpc(playerId, hitPoint, impulse);
        }

        [Rpc(SendTo.NotServer)]
        private void DuckDiedRpc(int playerId, Vector3 hitPoint, Vector3 impulse)
        {
            game?.ApplyNetworkDuckDeath(playerId, hitPoint, impulse);
        }

        private void OnElevatorChanged(float previous, float current)
        {
            if (!IsServer)
            {
                ApplyElevator();
            }
        }

        private void ApplyElevator() => game?.ApplyNetworkElevatorHeight(elevatorHeight.Value);

        // ========== РОЛЬ И СВИСТОК ==========

        private void OnHunterChanged(int previous, int current)
        {
            if (!IsServer)
            {
                ApplyHunter();
            }
        }

        /// <summary>
        /// Пересдача ролей на клиенте заново создаёт записи Уток и обнуляет
        /// им прогресс, поэтому список надо применить ещё раз. Иначе Утка,
        /// чьё состояние на сервере с этого момента не менялось, осталась бы
        /// у клиента на старте трассы до конца раунда.
        /// </summary>
        private void ApplyHunter()
        {
            game?.ApplyNetworkHunter();
            ducksDirty = true;
        }

        private void OnLiveTimeChanged(double previous, double current)
        {
            if (!IsServer)
            {
                ApplyLiveTime();
            }
        }

        private void ApplyLiveTime() => game?.ApplyNetworkLiveTime(liveTime.Value);

        // ========== ОРУЖИЕ ==========

        private void OnAmmoChanged(int previous, int current)
        {
            if (!IsServer)
            {
                ApplyWeapon();
            }
        }

        private void OnReloadChanged(double previous, double current)
        {
            if (!IsServer)
            {
                ApplyWeapon();
            }
        }

        private void ApplyWeapon() => game?.ApplyNetworkHunterWeapon(hunterAmmo.Value, hunterReloadEndsAt.Value);

        // ========== ЛОВУШКИ ==========

        /// <summary>
        /// Ловушка сработала. Состояние ловушек едет отдельно, маской, но по
        /// нему не поймать ни момент, ни мгновенные ловушки: гейзер срабатывает
        /// и остаётся свободным, маска при этом не меняется вовсе.
        /// </summary>
        public void AnnounceTrapFired(int index)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            TrapFiredRpc(index);
        }

        /// <summary>Сервер уже отыграл срабатывание у себя, поэтому себе не шлём.</summary>
        [Rpc(SendTo.NotServer)]
        private void TrapFiredRpc(int index)
        {
            game?.ApplyNetworkTrapFired(index);
        }

        private void OnTrapMaskChanged(uint previous, uint current)
        {
            if (!IsServer)
            {
                ApplyTraps();
            }
        }

        private void ApplyTraps() => game?.ApplyNetworkTrapMask(trapMask.Value);

        // ========== ПРИЦЕЛ ОХОТНИКА ==========

        private void OnHunterAimChanged(Vector2 previous, Vector2 current)
        {
            if (!IsServer)
            {
                ApplyAim();
            }
        }

        private void ApplyAim() => game?.ApplyNetworkHunterAim(hunterAim.Value.x, hunterAim.Value.y);

        /// <summary>
        /// Отдать прицел остальным. Зовётся на машине хозяина роли: только у
        /// неё риг первого лица действительно крутится.
        ///
        /// Шлём с выдержкой и только на заметный сдвиг — угол меняется каждым
        /// движением мыши, и слать его кадр в кадр значит гнать поток ради
        /// картинки у наблюдателей. Двадцати раз в секунду хватает: между
        /// отметками камера доводится сама.
        /// </summary>
        private void PublishAim()
        {
            if (hunterRole == null || !hunterRole.AimIsLocal || hunterRole.Rig == null)
            {
                return;
            }

            var aim = new Vector2(hunterRole.Rig.Yaw, hunterRole.Rig.Pitch);

            if (aimSent &&
                Mathf.Abs(Mathf.DeltaAngle(aim.x, lastSentAim.x)) < AimEpsilon &&
                Mathf.Abs(aim.y - lastSentAim.y) < AimEpsilon)
            {
                return;
            }

            if (Time.time < nextAimSend)
            {
                return;
            }

            nextAimSend = Time.time + 1f / AimSyncRate;
            lastSentAim = aim;
            aimSent = true;

            if (IsServer)
            {
                hunterAim.Value = aim;
                return;
            }

            SubmitAimRpc(aim);
        }

        /// <summary>
        /// Прицел от хозяина роли. Не проверяем: на исход он не влияет —
        /// попадание сервер считает сам, по направлению из самого выстрела.
        /// Это картинка для наблюдателей, и подделать ей нечего.
        /// </summary>
        [Rpc(SendTo.Server)]
        private void SubmitAimRpc(Vector2 aim, RpcParams rpcParams = default)
        {
            if (hunterPlayerId.Value != (int)rpcParams.Receive.SenderClientId)
            {
                return;
            }

            hunterAim.Value = aim;
        }

        // ========== УТКИ ==========

        private void OnDuckStatesChanged(NetworkListEvent<DuckNetState> changeEvent)
        {
            if (!IsServer)
            {
                ducksDirty = true;
            }
        }

        /// <summary>
        /// Применяем весь список, а не одну изменившуюся запись: Уток не больше
        /// семи, а разбор индексов события NetworkList — источник ошибок на
        /// удалении и вставке. Применение состояния, которое уже стоит, ничего
        /// не делает.
        /// </summary>
        private void ApplyDuckStates()
        {
            if (game == null)
            {
                return;
            }

            for (int i = 0; i < duckStates.Count; i++)
            {
                DuckNetState state = duckStates[i];
                game.ApplyNetworkDuck(state.PlayerId, state.Floor, state.Progress,
                    state.FinishOrder, state.Dead, state.DeathTime);
            }
        }

        // ========== ОПРОС ПРАВИЛ ==========

        private void Update()
        {
            if (!IsSpawned || game == null)
            {
                return;
            }

            // Прицел шлёт хозяин роли — им может быть и клиент, и хост,
            // поэтому до серверной развилки.
            PublishAim();

            if (!IsServer)
            {
                // Список применяем один раз за кадр, а не на каждой дельте:
                // восемь изменившихся Уток дали бы восемь полных проходов.
                if (ducksDirty)
                {
                    ducksDirty = false;
                    ApplyDuckStates();
                }

                return;
            }

            // Ушедших разбираем первыми: дальше по кадру публикуется состояние,
            // и уход должен успеть в него попасть тем же тиком.
            ApplyPendingLeavers();

            PublishRole();
            PublishElevator();
            PublishWeapon();
            PublishTraps();
            PublishDucks();
        }

        // ========== УХОД ИГРОКА ==========

        /// <summary>
        /// Разбираем уход не здесь, а на ближайшем тике — тем же приёмом, что
        /// и <see cref="Igruha.Networking.NetworkSessionManager"/>. Этот колбэк
        /// приходит и когда выключается сам сервер, а отличить два случая по
        /// состоянию NetworkManager нельзя (замерено 15.08: на обоих
        /// IsListening=True, ShutdownInProgress=False). При обычном выходе
        /// игрока тик будет, при выключении сервера тиков больше нет — и
        /// заканчивать раунд некому и незачем.
        /// </summary>
        private void OnClientDisconnected(ulong clientId)
        {
            if (IsServer)
            {
                pendingLeavers.Add(clientId);
            }
        }

        private void ApplyPendingLeavers()
        {
            if (pendingLeavers.Count == 0)
            {
                return;
            }

            if (NetworkManager == null || NetworkManager.ShutdownInProgress || !NetworkManager.IsListening)
            {
                return;
            }

            for (int i = 0; i < pendingLeavers.Count; i++)
            {
                game.HandlePlayerLeft((int)pendingLeavers[i]);
            }

            pendingLeavers.Clear();
        }

        private void PublishRole()
        {
            if (hunterPlayerId.Value != game.HunterPlayerId)
            {
                hunterPlayerId.Value = game.HunterPlayerId;
            }

            if (!liveTime.Value.Equals(game.LiveTime))
            {
                liveTime.Value = game.LiveTime;
            }
        }

        private void PublishElevator()
        {
            if (Time.time < nextElevatorSync)
            {
                return;
            }

            float height = game.ElevatorHeight;
            if (Mathf.Abs(height - elevatorHeight.Value) < ElevatorEpsilon)
            {
                return;
            }

            nextElevatorSync = Time.time + 1f / ElevatorSyncRate;
            elevatorHeight.Value = height;
        }

        private void PublishWeapon()
        {
            if (!game.TryGetHunterWeapon(out int ammo, out double reloadEndsAt))
            {
                return;
            }

            if (hunterAmmo.Value != ammo)
            {
                hunterAmmo.Value = ammo;
            }

            if (!hunterReloadEndsAt.Value.Equals(reloadEndsAt))
            {
                hunterReloadEndsAt.Value = reloadEndsAt;
            }
        }

        private void PublishTraps()
        {
            uint mask = game.TrapMask;
            if (trapMask.Value != mask)
            {
                trapMask.Value = mask;
            }
        }

        private void PublishDucks()
        {
            int count = game.DuckCount;

            while (duckStates.Count > count)
            {
                duckStates.RemoveAt(duckStates.Count - 1);
            }

            for (int i = 0; i < count; i++)
            {
                if (!game.TryGetDuckState(i, out DuckNetState state))
                {
                    continue;
                }

                if (i >= duckStates.Count)
                {
                    duckStates.Add(state);
                    nextProgressSync[state.PlayerId] = Time.time + 1f / ProgressSyncRate;
                    continue;
                }

                DuckNetState known = duckStates[i];

                // Исход уходит сразу, ползущий прогресс — с выдержкой, и
                // выдержка своя у каждой Утки: общая растянула бы обновление
                // на всю толпу.
                bool decided = known.PlayerId != state.PlayerId ||
                               known.Floor != state.Floor ||
                               known.FinishOrder != state.FinishOrder ||
                               known.Dead != state.Dead;

                if (!decided)
                {
                    if (Mathf.Abs(known.Progress - state.Progress) < ProgressEpsilon)
                    {
                        continue;
                    }

                    nextProgressSync.TryGetValue(state.PlayerId, out float allowedAt);
                    if (Time.time < allowedAt)
                    {
                        continue;
                    }
                }

                nextProgressSync[state.PlayerId] = Time.time + 1f / ProgressSyncRate;
                duckStates[i] = state;
            }
        }
    }
}
