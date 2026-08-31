using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Session;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Сетевая половина «Дырки в стене»: вешается на тот же объект, что и
    /// <see cref="HoleInWallMinigame"/>. Правила остаются обычным
    /// MonoBehaviour и работают без этого компонента, когда сцену открывают
    /// напрямую, — образец взят у <c>StopwatchNetwork</c> и <c>MemoryRunNetwork</c>.
    ///
    /// Фазу, время общего таймера и итоговые места везёт
    /// <c>NetworkMinigameBridge</c> рядом на том же объекте.
    /// </summary>
    /// <remarks>
    /// <b>Каналов пять, и ни один из них не про положение стены.</b>
    ///
    /// | Что | Чем | Как часто |
    /// | -- | -- | -- |
    /// | Состав дорожек и счёт | <c>NetworkList</c> | на старте и после каждой стены |
    /// | Расписание восьми стен | <c>NetworkList</c> | один раз на раунд |
    /// | Поза каждого участника | <c>NetworkList</c> | на смену позы |
    /// | Момент начала раунда | <c>NetworkVariable</c> | один раз |
    /// | Стадия = номер стены | <c>NetworkVariable</c> | восемь раз |
    ///
    /// <b>Положение стены не шлётся вовсе.</b> Оно чистая функция от момента
    /// начала раунда, таблицы подъездов в конфиге и общих часов — считает его
    /// каждая машина сама (<see cref="SweepingWall"/>, образец —
    /// <c>Core/Traps/SwingingBeamTrap</c>). Опоздавший к середине подъезда
    /// видит стену там же, где все, и без единого пакета.
    ///
    /// <b>Наверх уходит два сообщения на человека за стену, а не поток.</b>
    /// Смена позы — намерение, вердикт — оповещение. Ввод, движение и
    /// натяжение троса сюда не попадают вовсе: движение везёт штатный
    /// транспорт персонажа, натяжение каждая машина считает своему сама
    /// (<c>Core/Player/PlayerTether</c>).
    /// </remarks>
    public sealed class HoleInWallNetwork : NetworkBehaviour
    {
        /// <summary>Состав дорожек и счёт. Порядок списка = порядок дорожек, см. <see cref="HoleInWallTrackNetState"/>.</summary>
        private readonly NetworkList<HoleInWallTrackNetState> trackStates =
            new NetworkList<HoleInWallTrackNetState>();

        /// <summary>Рисунок всех стен всех дорожек: восемь строк на дорожку.</summary>
        private readonly NetworkList<HoleInWallWallNetState> schedule =
            new NetworkList<HoleInWallWallNetState>();

        /// <summary>Поза каждого участника — та, которую подтвердил сервер.</summary>
        private readonly NetworkList<HoleInWallPoseNetState> poses =
            new NetworkList<HoleInWallPoseNetState>();

        /// <summary>
        /// Момент начала раунда в общих часах. Одно число, из которого
        /// считается всё расписание: подъезды, удары, подвохи.
        /// </summary>
        private readonly NetworkVariable<double> roundStart = new NetworkVariable<double>(0d);

        /// <summary>Текущая стадия: стадия = стена.</summary>
        private readonly NetworkVariable<HoleInWallStageNetState> stage =
            new NetworkVariable<HoleInWallStageNetState>();

        /// <summary>
        /// Буферы под разбор списков на клиенте. <c>NetworkList</c> не
        /// <c>IReadOnlyList</c>, поэтому содержимое перекладывается сюда —
        /// один раз на приезд, а не каждый кадр.
        /// </summary>
        private readonly List<HoleInWallTrackNetState> trackBuffer = new List<HoleInWallTrackNetState>(4);
        private readonly List<HoleInWallWallNetState> scheduleBuffer = new List<HoleInWallWallNetState>(32);

        /// <summary>Ушедшие, которых осталось разобрать. Почему не сразу — см. <see cref="OnClientDisconnected"/>.</summary>
        private readonly List<ulong> pendingLeavers = new List<ulong>(4);

        private HoleInWallMinigame game;
        private MinigameStageState stageState;

        private bool tracksDirty;
        private bool scheduleDirty;
        private bool posesDirty;
        private bool stageDirty;
        private bool roundStartDirty;

        /// <summary>
        /// Состав, под который состояние уже разобрано. Порядок спавна этого
        /// объекта и старта мини-игры ничем не связан: расписание вполне может
        /// приехать раньше, чем контроллер получит ростер, — и тогда его
        /// пришлось бы разбирать заново, иначе у клиента не было бы ни дорожек,
        /// ни стен.
        /// </summary>
        private int appliedRoster = -1;

        /// <summary>Идёт сетевая катка и эта половина живая.</summary>
        public bool IsActive => IsSpawned;

        private void Awake()
        {
            game = GetComponent<HoleInWallMinigame>();
            if (game == null)
            {
                Debug.LogError($"{name}: HoleInWallNetwork не нашёл HoleInWallMinigame на своём объекте", this);
            }

            stageState = GetComponent<MinigameStageState>();
            if (stageState == null)
            {
                Debug.LogError($"{name}: HoleInWallNetwork не нашёл MinigameStageState на своём объекте", this);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            trackStates.OnListChanged += OnTracksChanged;
            schedule.OnListChanged += OnScheduleChanged;
            poses.OnListChanged += OnPosesChanged;
            roundStart.OnValueChanged += OnRoundStartChanged;
            stage.OnValueChanged += OnStageChanged;

            if (IsServer)
            {
                // Стадию объявляет тот же, кто её начал: событие приходит уже
                // после того, как момент конца проставлен.
                if (stageState != null)
                {
                    stageState.StageStarted += OnServerStageStarted;
                }

                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

                // Эта половина могла ожить позже раунда — разослать то, что
                // сервер уже решил.
                game?.PublishNetworkState();
                return;
            }

            // Подключились в середине раунда — догоняем то, что уже решено.
            tracksDirty = true;
            scheduleDirty = true;
            posesDirty = true;
            stageDirty = true;
            roundStartDirty = true;
        }

        public override void OnNetworkDespawn()
        {
            trackStates.OnListChanged -= OnTracksChanged;
            schedule.OnListChanged -= OnScheduleChanged;
            poses.OnListChanged -= OnPosesChanged;
            roundStart.OnValueChanged -= OnRoundStartChanged;
            stage.OnValueChanged -= OnStageChanged;

            if (IsServer)
            {
                if (stageState != null)
                {
                    stageState.StageStarted -= OnServerStageStarted;
                }

                if (NetworkManager != null)
                {
                    NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
                }
            }

            base.OnNetworkDespawn();
        }

        // ========== СОСТАВ ДОРОЖЕК И СЧЁТ ==========

        /// <summary>
        /// Объявить состав дорожек и их счёт.
        ///
        /// Список правится поэлементно, а не пересобирается: <c>Clear</c> плюс
        /// четыре <c>Add</c> — это восемь событий списка на каждое очко, тогда
        /// как меняется в нём одна строка.
        /// </summary>
        public void PublishTracks(IReadOnlyList<HoleInWallTrack> tracks)
        {
            if (!IsSpawned || !IsServer || tracks == null)
            {
                return;
            }

            if (trackStates.Count != tracks.Count)
            {
                trackStates.Clear();
                for (int i = 0; i < tracks.Count; i++)
                {
                    trackStates.Add(ToNetState(tracks[i]));
                }

                return;
            }

            for (int i = 0; i < tracks.Count; i++)
            {
                HoleInWallTrackNetState next = ToNetState(tracks[i]);
                if (!trackStates[i].Equals(next))
                {
                    trackStates[i] = next;
                }
            }
        }

        private static HoleInWallTrackNetState ToNetState(HoleInWallTrack track)
        {
            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;
            return new HoleInWallTrackNetState
            {
                TrackIndex = (byte)track.Index,
                FirstMemberId = members.Count > 0 ? members[0].PlayerId : PairAssignment.NoPlayer,
                SecondMemberId = members.Count > 1 ? members[1].PlayerId : PairAssignment.NoPlayer,
                Score = (byte)Mathf.Clamp(track.Score, 0, byte.MaxValue)
            };
        }

        /// <summary>
        /// Состав и счёт разбираются <b>сразу</b>, а не отложенно в
        /// <see cref="LateUpdate"/>.
        ///
        /// Итоговые места приезжают отдельным каналом и применяются в тот же
        /// кадр, но раньше отложенного разбора: порядок доставки разных каналов
        /// не гарантирован. Отложить разбор значит показать состояние на кадр
        /// назад ровно в тот момент, когда его читают, — и итоговая таблица
        /// напечаталась бы по вчерашнему счёту. На восьми процессах это уже
        /// ловили в «Рейсе на память»: места сошлись у всех, а счётчик смертей
        /// у одного отставал на единицу.
        /// </summary>
        private void OnTracksChanged(NetworkListEvent<HoleInWallTrackNetState> change)
        {
            if (IsServer)
            {
                return;
            }

            if (ApplyTracks())
            {
                scheduleDirty = posesDirty = stageDirty = true;
            }
        }

        /// <summary>Разложить приехавший состав по дорожкам. Истина — дорожки пересобраны.</summary>
        private bool ApplyTracks()
        {
            tracksDirty = false;

            trackBuffer.Clear();
            for (int i = 0; i < trackStates.Count; i++)
            {
                trackBuffer.Add(trackStates[i]);
            }

            return game != null && game.ApplyNetworkTracks(trackBuffer);
        }

        // ========== РАСПИСАНИЕ СТЕН ==========

        /// <summary>
        /// Объявить рисунок всех стен. Зовётся один раз на раунд и ещё раз,
        /// если дисконнект перевёл дорожку в режим одиночки: у неё меняются
        /// все ещё не сыгранные стены.
        /// </summary>
        public void PublishSchedule(IReadOnlyList<HoleInWallTrack> tracks)
        {
            if (!IsSpawned || !IsServer || tracks == null)
            {
                return;
            }

            scheduleBuffer.Clear();
            for (int i = 0; i < tracks.Count; i++)
            {
                HoleInWallTrack track = tracks[i];
                List<WallPattern> patterns = track.Patterns;
                for (int wall = 0; wall < patterns.Count; wall++)
                {
                    scheduleBuffer.Add(HoleInWallWallNetState.From(track.Index, wall, patterns[wall]));
                }
            }

            if (schedule.Count != scheduleBuffer.Count)
            {
                schedule.Clear();
                for (int i = 0; i < scheduleBuffer.Count; i++)
                {
                    schedule.Add(scheduleBuffer[i]);
                }

                return;
            }

            for (int i = 0; i < scheduleBuffer.Count; i++)
            {
                if (!schedule[i].Equals(scheduleBuffer[i]))
                {
                    schedule[i] = scheduleBuffer[i];
                }
            }
        }

        private void OnScheduleChanged(NetworkListEvent<HoleInWallWallNetState> change) => scheduleDirty = true;

        // ========== МОМЕНТ НАЧАЛА РАУНДА ==========

        /// <summary>
        /// Объявить момент начала раунда. Одно число на весь раунд: подъезды,
        /// удары и подвохи считаются от него по таблице конфига, а она у всех
        /// одна и та же.
        /// </summary>
        public void PublishRoundStart(double time)
        {
            if (IsSpawned && IsServer)
            {
                roundStart.Value = time;
            }
        }

        private void OnRoundStartChanged(double previous, double current)
        {
            if (!IsServer)
            {
                roundStartDirty = true;
            }
        }

        private void ApplyRoundStart()
        {
            if (roundStart.Value > 0d)
            {
                game?.ApplyNetworkRoundStart(roundStart.Value);
            }
        }

        // ========== СТАДИЯ ==========

        private void OnServerStageStarted(byte started)
        {
            if (!IsSpawned || !IsServer || stageState == null)
            {
                return;
            }

            stage.Value = new HoleInWallStageNetState
            {
                Subround = stageState.Subround,
                Stage = started,
                EndTime = stageState.StageEndTime,
                Duration = stageState.StageDuration
            };
        }

        private void OnStageChanged(HoleInWallStageNetState previous, HoleInWallStageNetState current)
        {
            if (!IsServer)
            {
                stageDirty = true;
            }
        }

        private void ApplyStage()
        {
            HoleInWallStageNetState value = stage.Value;
            if (value.Stage == MinigameStageState.NoStage || stageState == null)
            {
                return;
            }

            stageState.ApplyState(value.Subround, value.Stage, value.EndTime, value.Duration);
        }

        // ========== ПОЗА ==========

        /// <summary>
        /// Отправить серверу намерение встать в позу. Зовётся только у клиента:
        /// у сервера поза применяется на месте.
        /// </summary>
        public void SubmitPose(HoleInWallPose pose)
        {
            if (IsSpawned && !IsServer)
            {
                SetPoseRpc((byte)pose);
            }
        }

        /// <summary>
        /// Сервер принимает намерение. <b>Кто именно просит — берётся
        /// у отправителя, а не из сообщения.</b> Номер в сообщении клиент
        /// подделал бы и поставил позу соседу, то есть провалил бы стену
        /// за него: поза здесь — единственное, что решает исход.
        ///
        /// Диапазон проверяет <see cref="HoleInWallMinigame.ApplyPose"/>:
        /// байт из сети может быть любым.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void SetPoseRpc(byte pose, RpcParams rpcParams = default)
        {
            game?.ApplyPose((int)rpcParams.Receive.SenderClientId, (HoleInWallPose)pose);
        }

        /// <summary>Объявить подтверждённую позу участника. Видят все: чужие позы — половина зрелища.</summary>
        public void PublishPose(int playerId, HoleInWallPose pose)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            var next = new HoleInWallPoseNetState { PlayerId = playerId, Pose = (byte)pose };

            for (int i = 0; i < poses.Count; i++)
            {
                if (poses[i].PlayerId != playerId)
                {
                    continue;
                }

                if (!poses[i].Equals(next))
                {
                    poses[i] = next;
                }

                return;
            }

            poses.Add(next);
        }

        private void OnPosesChanged(NetworkListEvent<HoleInWallPoseNetState> change) => posesDirty = true;

        // ========== ВЕРДИКТ ==========

        /// <summary>
        /// Стена разрешена. <b>Это оповещение, а не источник правды</b>: исход
        /// уже посчитан на сервере и уже лежит в счёте дорожки. Клиенту оно
        /// нужно затем, чтобы не пытаться считать исход самому, и затем, чтобы
        /// в фазе 4 было к чему цеплять звук удара и брызги.
        /// </summary>
        public void AnnounceWallResolved(int trackIndex, int wallIndex, bool passed)
        {
            if (IsSpawned && IsServer)
            {
                WallResolvedRpc((byte)trackIndex, (byte)wallIndex, passed);
            }
        }

        /// <summary>Сервер уже применил исход у себя, поэтому себе не шлём.</summary>
        [Rpc(SendTo.NotServer)]
        private void WallResolvedRpc(byte trackIndex, byte wallIndex, bool passed)
        {
            game?.ApplyNetworkWallResolved(trackIndex, wallIndex, passed);
        }

        // ========== ПРИЁМ НА КЛИЕНТЕ ==========

        private void LateUpdate()
        {
            if (!IsSpawned || game == null)
            {
                return;
            }

            if (IsServer)
            {
                ApplyPendingLeavers();
                return;
            }

            // Ростер мог собраться уже после того, как состояние разобрали:
            // тогда разбираем заново, иначе клиент досидит раунд без дорожек
            // и без стен.
            if (game.RosterCount != appliedRoster)
            {
                appliedRoster = game.RosterCount;
                tracksDirty = scheduleDirty = posesDirty = stageDirty = roundStartDirty = true;
            }

            // Момент начала раунда — первым: без него не считается ни одно
            // расписание, а объявлен он бывает раньше, чем у клиента собрался
            // ростер. Второй раз это объявление не приедет — оно больше
            // не меняется, — поэтому применяется по флагу, а не по событию.
            if (roundStartDirty)
            {
                roundStartDirty = false;
                ApplyRoundStart();
            }

            // Дорожки остаются здесь ради одного случая: состав приехал раньше
            // ростера, и раскладывать его было некуда. Обычный приезд
            // разбирается сразу, в OnTracksChanged.
            //
            // Дальше порядок обязателен: расписание кладётся в дорожки, позы
            // навешиваются на их участников, а стадия перебирает их же.
            if (tracksDirty && ApplyTracks())
            {
                // Дорожки пересобраны — расписание, позы и стадию надо
                // разложить по ним заново.
                scheduleDirty = posesDirty = stageDirty = true;
            }

            if (scheduleDirty)
            {
                scheduleDirty = false;

                scheduleBuffer.Clear();
                for (int i = 0; i < schedule.Count; i++)
                {
                    scheduleBuffer.Add(schedule[i]);
                }

                game.ApplyNetworkSchedule(scheduleBuffer);
            }

            if (stageDirty)
            {
                stageDirty = false;
                ApplyStage();
            }

            if (posesDirty)
            {
                posesDirty = false;

                for (int i = 0; i < poses.Count; i++)
                {
                    game.ApplyNetworkPose(poses[i].PlayerId, (HoleInWallPose)poses[i].Pose);
                }
            }
        }

        // ========== ДИСКОННЕКТ ==========

        /// <summary>
        /// Разбираем уход не здесь, а на ближайшем тике — тем же приёмом, что
        /// у «Секундомера» и «Ангелов». Этот колбэк приходит и когда выключается
        /// сам сервер, а отличить два случая по состоянию <c>NetworkManager</c>
        /// нельзя. При обычном выходе игрока тик будет, при выключении сервера
        /// тиков больше нет — и переводить дорожку в режим одиночки некому
        /// и незачем.
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
                game?.HandlePlayerLeft((int)pendingLeavers[i]);
            }

            pendingLeavers.Clear();
        }
    }
}
