using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Session;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Состояние одного Бегущего в сетевом виде. Отдельная структура нужна
    /// потому, что NetworkList умеет только unmanaged-типы: RunnerRecord — класс
    /// со ссылками на компоненты сцены, реплицировать его нечем.
    ///
    /// Живёт списком на самой мини-игре, а не компонентом на игроке. Причина
    /// в том, что RunnerState и FreezePoseDriver вешаются в рантайме, а
    /// NetworkBehaviour в рантайме добавлять нельзя — NGO индексирует их при
    /// спавне. Второй вариант (положить сетевое состояние на общий Player.prefab)
    /// поехал бы во все мини-игры проекта ради правил одной.
    /// </summary>
    public struct RunnerNetState : INetworkSerializable, IEquatable<RunnerNetState>
    {
        public int PlayerId;

        /// <summary><see cref="RunnerState.Phase"/> числом: enum в NetworkList не кладётся.</summary>
        public byte Phase;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Phase);
        }

        public bool Equals(RunnerNetState other) => PlayerId == other.PlayerId && Phase == other.Phase;
    }

    /// <summary>
    /// Сетевая половина «Плачущих ангелов»: вешается на тот же объект, что и
    /// <see cref="CryingAngelsMinigame"/>. Правила остаются обычным
    /// MonoBehaviour и работают без этого компонента, когда сцену открывают
    /// напрямую — образец взят у <see cref="Igruha.Networking.NetworkMinigameBridge"/>,
    /// который рядом реплицирует фазу, таймер и итоговые места. Своего канала
    /// под них здесь нет намеренно.
    ///
    /// Что реплицируется здесь: кому выпала роль Водящего, куда смотрит его луч,
    /// горит ли фонарь и в каком состоянии каждый Бегущий.
    ///
    /// Ключевое место — направление луча. Тело Водящего едет под авторитетом
    /// владельца (ClientNetworkTransform реплицирует и поворот), поэтому конус,
    /// висящий на теле дочерним объектом, смотрел бы туда, куда развернулся
    /// клиент: сервер честно считал бы засветку по подделанному мгновенному
    /// развороту, и потолок скорости остался бы честным словом клиента. Поэтому
    /// луч снят с тела и ведётся отдельным серверным значением: владелец шлёт
    /// только желаемое направление, а доводит до него — с потолком — сервер.
    /// </summary>
    public sealed class CryingAngelsNetwork : NetworkBehaviour
    {
        /// <summary>Сколько раз в секунду владелец шлёт серверу желаемое направление.</summary>
        private const float DesiredYawSendRate = 20f;

        /// <summary>
        /// С какого расхождения обзор владельца подтягивается к серверному, °.
        /// Меньше — дёргается на обычной задержке, больше — луч заметно
        /// расходится с картинкой.
        /// </summary>
        private const float OwnerDriftThreshold = 6f;

        /// <summary>
        /// Насколько владелец должен «довести» поворот, чтобы коррекция вообще
        /// включилась, °. Во время разворота расхождение с сервером — это
        /// обычная задержка, и подтягивать камеру здесь значит ломать управление.
        /// </summary>
        private const float OwnerSettledThreshold = 2f;

        /// <summary>
        /// Сколько секунд серверный луч должен стоять, чтобы коррекция включилась.
        ///
        /// Одного «владелец довёл поворот» мало, и это стоило отдельного замера
        /// 16.08. В конце быстрого разворота владелец уже на месте, а сервер ещё
        /// доводит луч со своим потолком — расхождение в этот момент законное.
        /// Коррекция срабатывала на нём, а <see cref="FirstPersonCameraRig.SetYaw"/>
        /// переписывает и желаемое направление: клиент начинал слать серверу
        /// уменьшенную цель, сервер шёл к ней, коррекция срабатывала снова — и
        /// оба сходились где-то посередине. Замерено: разворот на 179° вставал
        /// на 169°, то есть намерение игрока просто терялось.
        /// </summary>
        private const float ServerSettleTime = 0.3f;

        /// <summary>Во сколько раз быстрее потолка остальные машины догоняют серверный луч.</summary>
        private const float RemoteCatchUpFactor = 1.35f;

        /// <summary>С какого расхождения луч на чужой машине ставится сразу, а не доводится, °.</summary>
        private const float RemoteSnapThreshold = 90f;

        private readonly NetworkVariable<int> keeperPlayerId =
            new NetworkVariable<int>(SpecialRoleHistory.NoPlayer);

        private readonly NetworkVariable<float> keeperYaw = new NetworkVariable<float>();

        private readonly NetworkVariable<bool> beamOn = new NetworkVariable<bool>();

        private readonly NetworkList<RunnerNetState> runnerStates = new NetworkList<RunnerNetState>();

        private CryingAngelsMinigame game;
        private AngelKeeper keeperRole;
        private FirstPersonCameraRig ownerRig;
        private bool localIsKeeper;

        /// <summary>Желаемое направление, присланное владельцем. Живёт только на сервере.</summary>
        private float serverDesiredYaw;

        /// <summary>Направление луча на этой машине: у сервера точное, у остальных — доведённое.</summary>
        private float displayYaw;

        private float nextYawSendTime;

        /// <summary>Последнее увиденное серверное направление и момент, когда оно перестало меняться.</summary>
        private float lastSeenServerYaw;
        private float serverStillSince;

        /// <summary>Идёт сетевая катка и эта половина живая.</summary>
        public bool IsActive => IsSpawned;

        /// <summary>Кому выпала роль. <see cref="SpecialRoleHistory.NoPlayer"/> — ещё не выбрана.</summary>
        public int KeeperPlayerId => IsSpawned ? keeperPlayerId.Value : SpecialRoleHistory.NoPlayer;

        /// <summary>Куда сервер держит луч сейчас.</summary>
        public float KeeperYaw => keeperYaw.Value;

        private void Awake()
        {
            game = GetComponent<CryingAngelsMinigame>();
            if (game == null)
            {
                Debug.LogError($"{name}: CryingAngelsNetwork не нашёл CryingAngelsMinigame на своём объекте", this);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            keeperPlayerId.OnValueChanged += OnKeeperChanged;
            beamOn.OnValueChanged += OnBeamChanged;
            runnerStates.OnListChanged += OnRunnerStatesChanged;

            displayYaw = keeperYaw.Value;
            lastSeenServerYaw = displayYaw;
            serverStillSince = Time.time;

            // Подключились в середине раунда — догоняем то, что уже решено.
            if (!IsServer)
            {
                ApplyAllRunnerStates();
            }
        }

        public override void OnNetworkDespawn()
        {
            keeperPlayerId.OnValueChanged -= OnKeeperChanged;
            beamOn.OnValueChanged -= OnBeamChanged;
            runnerStates.OnListChanged -= OnRunnerStatesChanged;

            base.OnNetworkDespawn();
        }

        // ========== РОЛЬ ==========

        /// <summary>
        /// Сервер объявил Водящего. Стартовое направление луча берётся с тела:
        /// иначе луч на первом кадре смотрит в нулевой азимут и медленно, с
        /// потолком скорости, ползёт оттуда к тому, куда игрок уже смотрит.
        /// </summary>
        public void PublishKeeper(int playerId, float startYaw)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            serverDesiredYaw = startYaw;
            displayYaw = startYaw;
            keeperYaw.Value = startYaw;
            keeperPlayerId.Value = playerId;
        }

        /// <summary>
        /// Кого мини-игра назначила Водящим на этой машине и её ли это игрок.
        /// Зовётся на каждой раздаче ролей у всех, включая сервер.
        ///
        /// Пока сети нет, метод обязан ничего не делать: иначе он снял бы луч
        /// с тела и запер его в нулевом азимуте, сломав соло-тест и болванку-Водящего.
        /// </summary>
        public void ConfigureKeeper(AngelKeeper role, bool isLocal, FirstPersonCameraRig rig)
        {
            if (!IsSpawned)
            {
                return;
            }

            keeperRole = role;
            localIsKeeper = isLocal;
            ownerRig = rig;

            if (role != null)
            {
                role.SetBeamYaw(IsServer ? keeperYaw.Value : displayYaw);
            }

            // Раздача ролей на клиенте — единственный момент, когда у него
            // появляются и Бегущие, и фонарь. Догоняем то, что сервер решил
            // раньше: при входе в середине раунда события изменения уже прошли,
            // и без этого клиент до первой смены состояния показывал бы
            // погашенный фонарь и всех свободными.
            if (!IsServer && game != null)
            {
                game.ApplyNetworkBeam(beamOn.Value);
                ApplyAllRunnerStates();
            }
        }

        private void OnKeeperChanged(int previous, int current)
        {
            // У сервера роли уже розданы — пересдавать нечего.
            if (!IsServer)
            {
                game?.ApplyNetworkKeeper();
            }
        }

        // ========== ФОНАРЬ ==========

        public void PublishBeam(bool enabled)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            beamOn.Value = enabled;
        }

        private void OnBeamChanged(bool previous, bool current)
        {
            if (!IsServer)
            {
                game?.ApplyNetworkBeam(current);
            }
        }

        // ========== НАПРАВЛЕНИЕ ЛУЧА ==========

        /// <summary>
        /// Сервер доводит луч к желаемому со своим потолком. Зовётся мини-игрой
        /// в физическом такте прямо перед расчётом засветки: считать по конусу,
        /// переставленному кадром раньше, значит терять на разворотах до двух
        /// градусов — на границе конуса это уже разница между «поймал» и «нет».
        /// </summary>
        public void ServerTickBeamYaw(float deltaTime, float maxTurnSpeed)
        {
            if (!IsSpawned || !IsServer || maxTurnSpeed <= 0f)
            {
                return;
            }

            // Хост в роли Водящего читает своё намерение напрямую: круг через
            // сеть к самому себе добавил бы шаг отправки и ничего не проверил.
            if (localIsKeeper && ownerRig != null)
            {
                serverDesiredYaw = ownerRig.DesiredYaw;
            }

            float next = Mathf.MoveTowardsAngle(keeperYaw.Value, serverDesiredYaw, maxTurnSpeed * deltaTime);
            if (!Mathf.Approximately(next, keeperYaw.Value))
            {
                keeperYaw.Value = next;
            }

            displayYaw = next;
            keeperRole?.SetBeamYaw(next);
        }

        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            SubmitOwnerYaw();
            DriveRemoteBeam();
            CorrectOwnerRig();
        }

        /// <summary>
        /// Владелец шлёт намерение, а не результат: подрезанный потолком угол
        /// заставил бы считать клэмп дважды и вдвое замедлил бы разворот.
        /// </summary>
        private void SubmitOwnerYaw()
        {
            if (IsServer || !localIsKeeper || ownerRig == null)
            {
                return;
            }

            if (Time.time < nextYawSendTime)
            {
                return;
            }

            nextYawSendTime = Time.time + 1f / DesiredYawSendRate;
            SubmitDesiredYawRpc(ownerRig.DesiredYaw);
        }

        /// <summary>
        /// Сервер принимает намерение только от того, кто действительно Водящий:
        /// иначе любой клиент крутил бы чужой луч. Само значение не ограничивается —
        /// потолок применяется при доводке, поэтому подделанный угол даёт ровно
        /// тот же максимум градусов в секунду.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void SubmitDesiredYawRpc(float yaw, RpcParams rpcParams = default)
        {
            if ((int)rpcParams.Receive.SenderClientId != keeperPlayerId.Value)
            {
                return;
            }

            if (float.IsNaN(yaw) || float.IsInfinity(yaw))
            {
                return;
            }

            serverDesiredYaw = yaw;
        }

        /// <summary>
        /// Остальные машины догоняют серверное значение чуть быстрее потолка:
        /// голое присваивание давало бы ступеньки по сетевому тику, а луч —
        /// это то, за чем в этой игре следят весь раунд.
        /// </summary>
        private void DriveRemoteBeam()
        {
            if (IsServer || keeperRole == null || game == null)
            {
                return;
            }

            float target = keeperYaw.Value;
            float delta = Mathf.DeltaAngle(displayYaw, target);

            if (Mathf.Abs(delta) > RemoteSnapThreshold)
            {
                // Смена Водящего или вход в середине раунда: доводить полкруга
                // с потолком скорости значит полторы секунды светить не туда.
                displayYaw = target;
            }
            else
            {
                float catchUp = game.KeeperTurnSpeed * RemoteCatchUpFactor;
                displayYaw = Mathf.MoveTowardsAngle(displayYaw, target, catchUp * Time.deltaTime);
            }

            keeperRole.SetBeamYaw(displayYaw);
        }

        /// <summary>
        /// Подтянуть обзор владельца к серверному — но только когда доводить
        /// уже нечего: и владелец довёл поворот, и сервер довёл луч. Пока идёт
        /// разворот, расхождение законно (сервер получает намерение с задержкой
        /// и клэмпит его сам), и коррекция на нём не просто дёргает камеру, а
        /// съедает намерение игрока — см. <see cref="ServerSettleTime"/>.
        ///
        /// Скорость коррекции равна потолку поворота: подтянуться быстрее
        /// честного разворота нельзя.
        /// </summary>
        private void CorrectOwnerRig()
        {
            if (IsServer || !localIsKeeper || ownerRig == null || game == null)
            {
                return;
            }

            if (!ServerBeamSettled() ||
                Mathf.Abs(Mathf.DeltaAngle(ownerRig.Yaw, ownerRig.DesiredYaw)) > OwnerSettledThreshold)
            {
                return;
            }

            float drift = Mathf.DeltaAngle(ownerRig.Yaw, keeperYaw.Value);
            if (Mathf.Abs(drift) <= OwnerDriftThreshold)
            {
                return;
            }

            ownerRig.SetYaw(Mathf.MoveTowardsAngle(ownerRig.Yaw, keeperYaw.Value, game.KeeperTurnSpeed * Time.deltaTime));
        }

        /// <summary>Серверный луч стоит на месте дольше <see cref="ServerSettleTime"/>.</summary>
        private bool ServerBeamSettled()
        {
            float current = keeperYaw.Value;
            if (Mathf.Abs(Mathf.DeltaAngle(current, lastSeenServerYaw)) > 0.01f)
            {
                lastSeenServerYaw = current;
                serverStillSince = Time.time;
                return false;
            }

            return Time.time - serverStillSince >= ServerSettleTime;
        }

        // ========== СОСТОЯНИЯ БЕГУЩИХ ==========

        /// <summary>
        /// Пересобрать список под новый состав Бегущих. Только сервер: у
        /// остальных состав приедет репликацией.
        /// </summary>
        public void ServerResetRunners(IReadOnlyList<int> playerIds)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            runnerStates.Clear();
            for (int i = 0; i < playerIds.Count; i++)
            {
                runnerStates.Add(new RunnerNetState
                {
                    PlayerId = playerIds[i],
                    Phase = (byte)RunnerState.Phase.Free
                });
            }
        }

        /// <summary>
        /// Записать состояние Бегущего, если оно изменилось. Сравнение обязательно:
        /// присваивание элемента NetworkList шлёт дельту без проверки, и запись
        /// тем же значением каждый такт превратилась бы в постоянный трафик.
        /// </summary>
        public void ServerSyncRunner(int playerId, RunnerState.Phase phase)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            int index = IndexOf(playerId);
            if (index < 0)
            {
                return;
            }

            RunnerNetState state = runnerStates[index];
            if (state.Phase == (byte)phase)
            {
                return;
            }

            state.Phase = (byte)phase;
            runnerStates[index] = state;
        }

        private void OnRunnerStatesChanged(NetworkListEvent<RunnerNetState> changeEvent)
        {
            if (IsServer)
            {
                return;
            }

            ApplyAllRunnerStates();
        }

        /// <summary>
        /// Применяем весь список, а не одну изменившуюся запись: Бегущих не
        /// больше семи, а разбор индексов события NetworkList — источник ошибок
        /// на удалении и вставке. Применение состояния, которое уже стоит,
        /// ничего не делает.
        /// </summary>
        private void ApplyAllRunnerStates()
        {
            if (game == null)
            {
                return;
            }

            for (int i = 0; i < runnerStates.Count; i++)
            {
                RunnerNetState state = runnerStates[i];
                game.ApplyNetworkRunnerPhase(state.PlayerId, (RunnerState.Phase)state.Phase);
            }
        }

        private int IndexOf(int playerId)
        {
            for (int i = 0; i < runnerStates.Count; i++)
            {
                if (runnerStates[i].PlayerId == playerId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
