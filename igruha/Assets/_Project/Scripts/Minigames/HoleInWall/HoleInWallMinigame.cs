using System;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Правила «Дырки в стене»: пары на дорожках, восемь стен по расписанию,
    /// вердикт «влез / не влез», сметание в воду и расстановка мест.
    ///
    /// Общий цикл — обучалка, таймер, результаты, возврат в хаб — целиком
    /// в <see cref="MinigameControllerBase"/>, здесь только своё.
    /// </summary>
    /// <remarks>
    /// <b>Сеть.</b> Важное состояние меняют пять методов —
    /// <see cref="ResolveWall"/>, <see cref="SweepTrack"/>,
    /// <see cref="ReturnMember"/>, <see cref="ApplyPose"/> и
    /// <see cref="CollectResults"/>, — и все они зовутся только под
    /// <see cref="MinigameControllerBase.HasAuthority"/>. Состав пар, рисунок
    /// стен и момент начала раунда считает сервер один раз и объявляет
    /// через <see cref="HoleInWallNetwork"/>; движение стены не шлётся вовсе —
    /// оно чистая функция от объявленного момента и общих часов
    /// (<see cref="SweepingWall"/>).
    ///
    /// <b>Пуск стены считает каждая машина сама, вердикт — только сервер.</b>
    /// Это одна и та же формула от одного и того же объявленного момента,
    /// поэтому стена стоит у всех в одном месте без единого пакета. Исход
    /// парный и стоит места в таблице, поэтому выносит его один сервер
    /// и рассылает готовым (спека 10.1).
    ///
    /// <b>Каждого судят по его собственному экрану</b> (IGR-594). Момент
    /// проверки — грань стены на линии проверки, по часам той машины, которая
    /// ведёт тело. У клиента эти часы отстают от серверных, а копию клиента
    /// сервер видит ещё и с опозданием сетевого пути, — поэтому клиент
    /// в свой момент отчитывается, где стоит его тело, а сервер позу и вырез
    /// берёт свои и место из отчёта проверяет на правдоподобие
    /// (<see cref="HoleInWallCheck"/>). Раньше сервер судил клиента по своей
    /// стене и по устаревшей копии, и сметал его прежде, чем стена доезжала
    /// до него на его же экране.
    ///
    /// <b>Вырез закреплён за игроком.</b> Нулевой участник пары — нулевой
    /// вырез: дырка режется по силуэту конкретного персонажа, и чужой ему
    /// не по фигуре. Кто чей, видно по цвету контура. Зеркальный переворот
    /// меняет вырезы местами по X — и паре приходится перебегать.
    ///
    /// <b>«Стоит на платформе» проверяется по высоте, а не по</b> <c>IsGrounded</c>.
    /// Проверку земли считает мотор персонажа, а мотор на чужих копиях выключен —
    /// в том числе на сервере, у аватара любого клиента. У сервера
    /// <c>IsGrounded</c> клиента ложен всегда, и в фазе 3 в прыжке оказывались
    /// бы все. По высоте это решается без мотора и одинаково для всех: тот же
    /// приём, что в «Рейсе на память».
    /// </remarks>
    public sealed class HoleInWallMinigame : MinigameControllerBase
    {
        /// <summary>Первая стадия подраунда. Номер стадии = номер стены, с единицы.</summary>
        private const byte FirstStage = 1;

        /// <summary>Шаг сида между дорожками. Простое число, чтобы соседние дорожки не попадали в одну последовательность.</summary>
        private const int TrackSeedStride = 7919;

        /// <summary>Запас за габаритом арены, дальше которого человек считается вылетевшим, м.</summary>
        private const float ArenaMargin = 0.5f;

        /// <summary>
        /// Сколько секунд после засчитанной стены падение в воду считается
        /// подозрительным: прошедшего стена сталкивать не должна. Полторы
        /// секунды — стена за это время уходит с платформы целиком даже
        /// на первой, самой медленной скорости.
        /// </summary>
        private const float FallAfterPassWindow = 1.5f;

        [Header("Дырка в стене")]
        [SerializeField] private HoleInWallConfig config;
        [Tooltip("Все дорожки арены. Заполняется построителем арены")]
        [SerializeField] private HoleInWallTrack[] tracks = Array.Empty<HoleInWallTrack>();
        [SerializeField] private Collider[] poolSupports = Array.Empty<Collider>();
        [Tooltip("Стадии внутри раунда: стадия = стена")]
        [SerializeField] private MinigameStageState stageState;

        [Header("Оформление верёвки")]
        [SerializeField] private Material ropeMaterial;
        [SerializeField] private Material ropeTracerMaterial;
        [SerializeField] private Material ropeCollarMaterial;

        private readonly List<PairAssignment.Pair> pairs = new List<PairAssignment.Pair>(4);
        private readonly List<HoleInWallTrack> playingTracks = new List<HoleInWallTrack>(4);
        private readonly HashSet<int> earlyFinalFailures = new HashSet<int>();
        private readonly Dictionary<int, HoleInWallTrack> trackByPlayer = new Dictionary<int, HoleInWallTrack>(8);
        private readonly WallPatternGenerator generator = new WallPatternGenerator();

        /// <summary>Строка разбора вердикта. Одна на игру: печатается раз на стену и дорожку.</summary>
        private readonly System.Text.StringBuilder verdictLine = new System.Text.StringBuilder(256);
        private readonly ScoreRanking ranking = new ScoreRanking();

        /// <summary>
        /// Счёт ушедших, замороженный на моменте выхода. Дорожку после ухода
        /// может доигрывать напарник, и набранное им уже не общее: ушедший
        /// ранжируется по тому, что успел сам (спека 10.2).
        /// </summary>
        private readonly Dictionary<int, int> frozenScores = new Dictionary<int, int>(8);

        /// <summary>Буфер перерисовки дорожки, потерявшей напарника. Переиспользуется, чтобы не плодить мусор.</summary>
        private readonly List<WallPattern> soloPatterns = new List<WallPattern>(8);

        private HoleInWallNetwork network;
        [SerializeField] private HoleInWallRecovery[] recoveries = Array.Empty<HoleInWallRecovery>();
        private double roundStartTime;

        /// <summary>
        /// Момент начала раунда известен: у сервера он проставлен, у клиента
        /// приехал. До этого считать расписание не от чего — стены стояли бы
        /// в нуле общих часов, то есть уже уехавшими.
        /// </summary>
        private bool roundStartKnown;

        /// <summary>Сид раунда. Хранится: по нему перерисовывается дорожка, потерявшая напарника.</summary>
        private int roundSeed;
        private int currentWall = -1;
        private float stageHitTime;
        private bool wallWarned;

        /// <summary>Номер текущей стены, с 1. Ноль — стены ещё не едут. Интерфейсу.</summary>
        public int CurrentWallNumber => currentWall + 1;

        /// <summary>Сколько всего стен в раунде. Интерфейсу.</summary>
        public int WallCount => config != null ? config.WallCount : 0;

        /// <summary>Сколько секунд осталось до удара текущей стены. Ноль — стена уже прошла.</summary>
        public float SecondsToHit
        {
            get
            {
                if (config == null || currentWall < 0 || !RoundActive || !roundStartKnown)
                {
                    return 0f;
                }

                float elapsed = (float)(NetworkClock.Now - roundStartTime);
                return Mathf.Max(0f, stageHitTime - elapsed);
            }
        }

        /// <summary>Числа игры. Интерфейсу и погонщику болванок — чтобы не заводить вторую ссылку на тот же ассет.</summary>
        public HoleInWallConfig Config => config;

        /// <summary>
        /// Дорожки этого раунда. Погонщику болванок, интерфейсу и сетевой
        /// половине. Список набирается на старте и до конца раунда не меняется:
        /// дорожка, потерявшая всех участников, остаётся в нём пустой
        /// (<see cref="HoleInWallTrack.Active"/> — ложь), потому что её номер
        /// держит раскладку у клиента.
        /// </summary>
        public IReadOnlyList<HoleInWallTrack> PlayingTracks => playingTracks;

        /// <summary>Сколько участников в раунде. Сетевой половине — понять, собрался ли уже ростер.</summary>
        public int RosterCount => Players.Count;

        /// <summary>
        /// Вердикт стены объявлен: дорожка и её исход. Поднимается <b>на каждой
        /// машине</b> ровно один раз — на сервере в момент подсчёта, на клиенте
        /// в момент приёма оповещения, — поэтому подписчик получает его у всех
        /// участников, а не только у того, кто считал.
        ///
        /// Точка привязки арта: эффекты подфазы 4.4 и звук фазы 5. Своего
        /// состояния событие не несёт и ничего не решает — исход уже правда,
        /// здесь его только показывают.
        /// </summary>
        public event Action<HoleInWallTrack, bool> WallResolved;

        /// <summary>
        /// До удара текущей стены осталась <see cref="HoleInWallConfig.WarningLead"/>
        /// секунда — единственная подсказка в игре, и она нужна по механике:
        /// на последней стене подъезд короче, чем разворот камеры (спека 8.8).
        ///
        /// Поднимается <b>один раз на стену и на каждой машине</b>: момент
        /// удара считается от объявленного начала раунда по общим часам,
        /// поэтому сигнал звучит у всех одновременно и без единого пакета.
        /// Один на стену, а не на дорожку, — момент у всех дорожек общий,
        /// и четыре источника дали бы четырёхкратную громкость вместо
        /// подсказки.
        ///
        /// Точка привязки арта: слот <c>impact_warning</c> фазы 5.
        /// На каркасе здесь стоял синтезированный тон 880 Гц — заглушка,
        /// снятая вместе с приходом настоящего звука.
        /// </summary>
        public event Action WallWarning;

        /// <summary>Дорожка этого участника. Пусто — участника в раунде нет.</summary>
        public HoleInWallTrack TrackOf(int playerId) =>
            trackByPlayer.TryGetValue(playerId, out HoleInWallTrack track) ? track : null;

        protected override void Awake()
        {
            base.Awake();
            network = GetComponent<HoleInWallNetwork>();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            ResultsReported += LogResults;

            if (stageState != null)
            {
                stageState.StageStarted += HandleStageStarted;
                stageState.StageElapsed += HandleStageElapsed;
            }

            SubscribeWalls(true);
        }

        protected override void OnDisable()
        {
            ResultsReported -= LogResults;

            if (stageState != null)
            {
                stageState.StageStarted -= HandleStageStarted;
                stageState.StageElapsed -= HandleStageElapsed;
            }

            SubscribeWalls(false);

            base.OnDisable();
        }

        /// <summary>
        /// Подписка на удар стеной. По всем дорожкам арены, а не только по
        /// играющим: состав меняется дисконнектом посреди раунда, а стены
        /// в сцене одни и те же от начала до конца.
        /// </summary>
        private void SubscribeWalls(bool subscribe)
        {
            for (int i = 0; i < tracks.Length; i++)
            {
                SweepingWall wall = tracks[i] != null ? tracks[i].Wall : null;
                if (wall == null)
                {
                    continue;
                }

                wall.PlayerStruck -= HandleWallStrike;
                if (subscribe)
                {
                    wall.PlayerStruck += HandleWallStrike;
                }
            }
        }

        /// <summary>
        /// Стена задела человека физически. <b>Это удар, и он решает стену
        /// немедленно</b> — не дожидаясь линии проверки.
        ///
        /// До 04.09 удара не было вовсе: прыгнувший стене навстречу упирался
        /// в её коробку и ехал перед ней до вердикта, вместо того чтобы
        /// улететь. Исход считается тем же <see cref="ResolveWall"/>, что и
        /// на линии: задетый в вырез не влез, значит дорожка провалила стену,
        /// значит летят оба — правило парного провала не меняется оттого, что
        /// провал случился раньше.
        ///
        /// <b>Удар засчитывает машина, которая ведёт тело</b> (IGR-594). Копию
        /// клиента сервер видит с опозданием, а свою стену — раньше клиента:
        /// касание плиты копией у сервера — эхо, а не удар, и раньше оно
        /// сметало человека, до которого на его экране стена ещё не доехала.
        /// Поэтому у сервера удар — только по телу, которое ведёт он сам,
        /// а клиент о своём ударе отчитывается (<see cref="ApplyCheckReport"/>).
        ///
        /// <b>Влезшего не бьём.</b> Касание — только повод спросить разбор
        /// этого участника, а не сам вердикт.
        /// </summary>
        private void HandleWallStrike(SweepingWall wall, PlayerController victim)
        {
            if (!RoundActive || currentWall < 0 || victim == null)
            {
                return;
            }

            for (int i = 0; i < playingTracks.Count; i++)
            {
                HoleInWallTrack track = playingTracks[i];
                if (track.Wall != wall)
                {
                    continue;
                }

                int m = MemberIndexOf(track, victim);
                if (m < 0 || !track.Members[m].DrivenHere || !track.WallLaunched || track.WallResolved ||
                    track.CheckPending || earlyFinalFailures.Contains(track.Index))
                {
                    return;
                }

                HoleInWallFit fit = FitFor(track, m);
                if (fit.Fits)
                {
                    return;
                }

                if (HasAuthority)
                {
                    track.Members[m].Check.Decide(fit, HoleInWallCheck.Source.Server, NetworkClock.Now);
                    StrikeTrack(track, track.Members[m].PlayerId);
                }
                else if (!track.StrikeReported && !track.LocalChecked)
                {
                    // OnCollisionStay зовёт это каждый такт — отчитываемся один раз.
                    // После своей линии проверки поздно: отчёт уже ушёл.
                    track.StrikeReported = true;
                    network?.ReportCheck(currentWall, victim.Position, struck: true);
                }

                return;
            }
        }

        private static int MemberIndexOf(HoleInWallTrack track, PlayerController avatar)
        {
            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;
            for (int m = 0; m < members.Count; m++)
            {
                if (members[m].Avatar == avatar)
                {
                    return m;
                }
            }

            return -1;
        }

        /// <summary>Удар засчитан: дорожка провалила стену раньше линии.</summary>
        private void StrikeTrack(HoleInWallTrack track, int struckId)
        {
            if (currentWall == config.WallCount - 1 && NetworkClock.Now < roundStartTime + config.ScheduleLength)
            {
                // Keep the physical hit immediate; publish the last wall's verdict
                // at its scheduled hit, even if someone jumped into it early.
                earlyFinalFailures.Add(track.Index);
                LogVerdict(track, false, $"удар стеной по id={struckId}, итог объявится на ударе");
                SweepTrack(track);
            }
            else
            {
                ResolveWall(track, $"удар стеной по id={struckId}");
            }
        }

        /// <summary>
        /// Итоговая таблица в лог — <b>на каждой машине</b>, ровно та, которую
        /// эта машина показывает.
        ///
        /// Пишется по событию шаблона, а не из <see cref="CollectResults"/>:
        /// места считает сервер, и лог из подсчёта был бы только у него. Чтобы
        /// приёмка сверяла восемь таблиц построчно, а не на глаз, каждая машина
        /// обязана назвать свою. Тот же приём в «Рейсе на память».
        ///
        /// Дорожка и её счёт идут рядом с местом: исход здесь парный, и без
        /// номера дорожки нельзя увидеть, что двое разделили место не случайно.
        ///
        /// ⚠️ Побуквенно строки сходятся у всех, <b>пока никто не выходил</b>.
        /// У ушедшего дорожки на клиентах уже нет, а замороженный счёт знает
        /// только сервер — его строка будет полнее. Это не рассинхрон: место
        /// ушедшего считает сервер и рассылает готовым, и вот оно совпадает
        /// у всех.
        /// </summary>
        private void LogResults(MinigameResults results)
        {
            var report = new System.Text.StringBuilder("[Дырка] итог: ");
            IReadOnlyList<MinigameResults.PlayerResult> entries = results.Entries;

            for (int i = 0; i < entries.Count; i++)
            {
                MinigameResults.PlayerResult entry = entries[i];
                HoleInWallTrack track = TrackOf(entry.PlayerId);

                report.Append(entry.Place).Append(". id=").Append(entry.PlayerId)
                      .Append(" дорожка=").Append(track != null ? track.Index.ToString() : "нет")
                      .Append(" стен=").Append(ScoreOf(entry.PlayerId))
                      .Append(frozenScores.ContainsKey(entry.PlayerId) ? " (вышел)" : string.Empty)
                      .Append("; ");
            }

            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Проверка снятия ролей — <b>на каждой машине</b>, отдельной строкой
        /// и сразу после того, как роли сняты.
        ///
        /// Почему не вместе с таблицей мест: у клиента места приезжают
        /// отдельным <c>Rpc</c>, и он рассылается <b>раньше</b>, чем публикуется
        /// смена фазы. Значит, на клиенте таблица печатается ещё до
        /// <see cref="OnRoundEnded"/> — то есть до снятия ролей, — и проверка
        /// оттуда врала бы «не снято» на каждом прогоне.
        ///
        /// Проверять это глазами нельзя вовсе: незакрытая роль уезжает в хаб
        /// вместе с живым <c>NetworkObject</c> персонажа и даёт симптом
        /// «не работает у одного человека из всех», а он и означает, что
        /// состояние привязано к машине и на одной его не увидеть.
        /// </summary>
        private void LogRoleAudit()
        {
            int checkedMembers = 0;
            int stuckRoles = 0;

            for (int i = 0; i < playingTracks.Count; i++)
            {
                HoleInWallTrack track = playingTracks[i];
                IReadOnlyList<HoleInWallTrack.Member> members = track.Members;

                for (int m = 0; m < members.Count; m++)
                {
                    PlayerController avatar = members[m].Avatar;
                    if (avatar == null)
                    {
                        continue;
                    }

                    checkedMembers++;

                    // Сам компонент позы здесь ещё жив — Destroy у Unity
                    // отложенный, — поэтому спрашиваем не «есть ли он», а
                    // «работает ли»: выключенный уже снял с персонажа всё,
                    // что вешал.
                    PlayerPoseAbility pose = avatar.GetComponent<PlayerPoseAbility>();

                    if (avatar.FacingOverride.HasValue || avatar.CrouchInputSuppressed ||
                        (pose != null && pose.enabled) || track.Tether != null)
                    {
                        stuckRoles++;
                    }
                }
            }

            Debug.Log($"[Дырка] роли сняты у {checkedMembers - stuckRoles} из {checkedMembers}");

            if (stuckRoles > 0)
            {
                Debug.LogError($"{name}: {stuckRoles} участников уезжают в хаб с ролью раунда — " +
                               "фиксированный фронт, запрет Ctrl, поза или трос не сняты", this);
            }
        }

        // ========== ПОДГОТОВКА РАУНДА ==========

        protected override void OnPlayersReady()
        {
            if (config == null)
            {
                Debug.LogError($"{name}: не назначен HoleInWallConfig — играть нечем", this);
                return;
            }

            ClearRound();

            // Состав делит сервер один раз и объявляет готовым. Клиент его
            // не пересчитывает: тот же сид у него сошёлся бы в ту же раскладку
            // только при точно совпавшем ростере, а объявленный список
            // сходится всегда.
            if (!HasAuthority)
            {
                // ⚠️ Момент начала раунда здесь НЕ сбрасывается, и это важно.
                // Сервер объявляет его, как только начал раунд, а ростер
                // у клиента собирается своим темпом: объявление вполне
                // приезжает раньше, чем сюда дойдёт очередь. Сбросив его,
                // мы получили бы клиента, у которого стены не едут вовсе,
                // — а второй раз объявление не придёт, оно уже не меняется.
                return;
            }

            roundStartKnown = false;
            roundSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

            PairAssignment.Assign(Players, roundSeed, pairs);
            AssignTracks();
            GeneratePatterns();

            network?.PublishTracks(playingTracks);
            network?.PublishSchedule(playingTracks);
            PublishPoses();

            Debug.Log($"🧱 «Дырка в стене»: {Players.Count} игроков, {playingTracks.Count} дорожек, " +
                      $"{config.WallCount} стен, раунд {config.RoundLength:F1} с");
        }

        /// <summary>
        /// Разослать всё, что сервер уже решил. Зовётся сетевой половиной, если
        /// она ожила позже раунда: порядок спавна и старта мини-игры ничем
        /// не связан.
        /// </summary>
        public void PublishNetworkState()
        {
            if (!HasAuthority || network == null || playingTracks.Count == 0)
            {
                return;
            }

            network.PublishTracks(playingTracks);
            network.PublishSchedule(playingTracks);
            PublishPoses();

            if (roundStartKnown)
            {
                network.PublishRoundStart(roundStartTime);
            }
        }

        private void PublishPoses()
        {
            if (network == null)
            {
                return;
            }

            for (int i = 0; i < playingTracks.Count; i++)
            {
                IReadOnlyList<HoleInWallTrack.Member> members = playingTracks[i].Members;
                for (int m = 0; m < members.Count; m++)
                {
                    HoleInWallTrack.Member member = members[m];
                    network.PublishPose(member.PlayerId,
                        member.Pose != null ? member.Pose.CurrentPose : HoleInWallPose.None);
                }
            }
        }

        /// <summary>
        /// Разложить пары по дорожкам и навесить на аватары всё, что нужно
        /// раунду. Лишние дорожки выключаются целиком: при двоих их четыре
        /// не нужно, а пустая платформа читается как чужая брошенная.
        /// </summary>
        private void AssignTracks()
        {
            for (int i = 0; i < tracks.Length; i++)
            {
                HoleInWallTrack track = tracks[i];
                if (track == null || track.Wall == null)
                {
                    Debug.LogError($"{name}: дорожка {i} не собрана — построить арену пунктом меню " +
                                   "Igruha/Дырка в стене/Построить арену", this);
                    continue;
                }

                bool used = i < pairs.Count;
                track.gameObject.SetActive(used);

                if (!used)
                {
                    continue;
                }

                PairAssignment.Pair pair = pairs[i];

                for (int slot = 0; slot < pair.Size; slot++)
                {
                    int playerId = pair.MemberAt(slot);
                    if (playerId == PairAssignment.NoPlayer)
                    {
                        continue;
                    }

                    HoleInWallTrack.Member member = BuildMember(playerId);
                    if (member == null)
                    {
                        continue;
                    }

                    track.AddMember(member);
                    trackByPlayer[playerId] = track;
                }

                if (!track.Active)
                {
                    track.gameObject.SetActive(false);
                    continue;
                }

                track.ArrangeSlots(config);
                track.Wall.Configure(config, track.ShapesOf(0), track.ShapesOf(1));
                track.AimFunnels(config);
                playingTracks.Add(track);

                PlaceMembers(track);
                BindTether(track);
            }
        }

        /// <summary>Дорожка по её номеру на арене. Пусто — такой в раунде нет.</summary>
        private HoleInWallTrack TrackByIndex(int index)
        {
            for (int i = 0; i < playingTracks.Count; i++)
            {
                if (playingTracks[i].Index == index)
                {
                    return playingTracks[i];
                }
            }

            return null;
        }

        private HoleInWallTrack.Member BuildMember(int playerId)
        {
            PlayerController avatar = FindAvatar(playerId);
            if (avatar == null)
            {
                // Молчать здесь нельзя: пара без второго превращается
                // в одиночку, ей достаётся один вырез вместо двух, и стена
                // на вид расходится с той, по которой судит сервер.
                Debug.LogError($"{name}: у игрока {playerId} нет аватара — дорожка соберётся неполной", this);
                return null;
            }

            // Способность вешается на аватар, а не лежит в префабе: префабы
            // персонажей заморожены (igruha/CLAUDE.md, раздел 🔒 0).
            if (!avatar.TryGetComponent(out PlayerPoseAbility pose))
            {
                pose = avatar.gameObject.AddComponent<PlayerPoseAbility>();
            }

            pose.Configure(this, playerId);

            // Воронка вешается тем же порядком: она доводит игрока до его
            // выреза в последние полсекунды. Номер выреза совпадает с местом
            // на платформе и проставляется, когда состав дорожки собран.
            if (!avatar.TryGetComponent(out WallFunnel funnel))
            {
                funnel = avatar.gameObject.AddComponent<WallFunnel>();
            }

            // Вода гасит удар и движение, оставляя погружение до дна.
            if (!avatar.TryGetComponent(out PlayerBuoyancy buoyancy))
            {
                buoyancy = avatar.gameObject.AddComponent<PlayerBuoyancy>();
            }

            buoyancy.Configure(config, poolSupports);

            avatar.TryGetComponent(out StuckDetector stuck);
            avatar.TryGetComponent(out PlayerRespawner respawner);
            avatar.TryGetComponent(out PlayerInputReader input);

            // Вырез закреплён за игроком, значит и контур у него собственный.
            // Персонаж опознаётся по имени контроллера аниматора: префабы
            // персонажей заморожены, метки на них не повесить.
            var shapes = new CutoutShapes(config, CutoutShapes.KeyOf(avatar.gameObject));
            if (!shapes.Exact)
            {
                Debug.LogWarning($"{name}: персонаж «{shapes.Character}» не найден в ассете силуэтов — " +
                                 "вырез берётся на весь ростер и будет выглядеть кляксой. " +
                                 "Испечь: Igruha/Дырка в стене/Испечь силуэты вырезов", this);
            }

            return new HoleInWallTrack.Member(playerId, avatar, pose, stuck, respawner, input, shapes,
                funnel, buoyancy);
        }

        /// <summary>
        /// Поставить участников на их места и зафиксировать фронт: лицом к
        /// стене, движение боковое (решение геймдизайнера 31.08).
        /// </summary>
        private void PlaceMembers(HoleInWallTrack track)
        {
            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;
            for (int i = 0; i < members.Count; i++)
            {
                HoleInWallTrack.Member member = members[i];
                Transform slot = track.SlotOf(i);
                if (slot == null || member.Avatar == null)
                {
                    continue;
                }

                member.Avatar.RequestTeleport(slot.position, slot.rotation);
                member.Avatar.FacingOverride = null;
                member.Respawner?.SetRespawnPoint(slot);
            }
        }

        /// <summary>
        /// Связать пару тросом. У одиночки троса нет — ему и мешать некому.
        /// </summary>
        private void BindTether(HoleInWallTrack track)
        {
            if (track.Solo || track.Members.Count < 2)
            {
                return;
            }

            var holder = new GameObject($"Tether_{track.Index}");
            holder.transform.SetParent(transform, false);

            var tether = holder.AddComponent<PlayerTether>();
            tether.Configure(config.TetherLength, config.TetherRamp,
                config.TetherPullAcceleration, config.TetherHardLimit);
            tether.Bind(track.Members[0].Avatar, track.Members[1].Avatar);
            track.Tether = tether;
            if (ropeMaterial != null && ropeTracerMaterial != null && ropeCollarMaterial != null)
                holder.AddComponent<HoleInWallRopeVisual>().Initialize(tether,
                    ropeMaterial, ropeTracerMaterial, ropeCollarMaterial,
                    track.Members[0].Avatar, track.Members[1].Avatar, config);

            // ⚠️ Пока трос натянут, детектор застревания обязан молчать: игрок
            // на натянутом тросе выглядит для него точно как зажатый геометрией,
            // и страховка выкинула бы его в центр платформы посреди правильно
            // занятой позиции (спека 9.6).
            for (int i = 0; i < track.Members.Count; i++)
            {
                if (track.Members[i].Stuck != null)
                {
                    track.Members[i].Stuck.Tether = tether;
                }
            }
        }

        private void GeneratePatterns()
        {
            if (!HasAuthority)
            {
                return;
            }

            for (int i = 0; i < playingTracks.Count; i++)
            {
                HoleInWallTrack track = playingTracks[i];
                generator.Generate(config, track.ShapesOf(0), track.ShapesOf(1),
                    TrackSeed(track), track.Solo, track.Patterns);
            }
        }

        /// <summary>
        /// Свой сид на дорожку: соседи решают разные задачи, и половина
        /// удовольствия в том, что видно, как позорятся рядом.
        /// </summary>
        private int TrackSeed(HoleInWallTrack track) => roundSeed + track.Index * TrackSeedStride;

        // ========== ТЕЧЕНИЕ РАУНДА ==========

        /// <summary>HUD и восемь стадий используют одну длительность из расписания стен.</summary>
        protected override float RoundDuration => config != null ? config.RoundLength : base.RoundDuration;

        protected override void OnRoundStarted()
        {
            if (config == null || !HasAuthority)
            {
                // Момент начала объявит сервер: от него считается всё
                // расписание, и придумывать свой клиенту нельзя.
                return;
            }

            roundStartTime = NetworkClock.Now;
            earlyFinalFailures.Clear();
            roundStartKnown = true;
            // The wall clock owns both HUD and completion. RoundTimer.Update must not
            // finish the round before the final FixedUpdate has scored every lane.
            if (Timer != null) Timer.DrivenExternally = true;
            network?.PublishRoundStart(roundStartTime);
            stageState?.BeginSubround(1, FirstStage, config.StageDuration(0));
        }

        /// <summary>Момент начала раунда приехал от сервера. От него считается всё расписание.</summary>
        public void ApplyNetworkRoundStart(double time)
        {
            if (HasAuthority)
            {
                return;
            }

            roundStartTime = time;
            roundStartKnown = true;
        }

        private void HandleStageStarted(byte stage)
        {
            currentWall = stage - FirstStage;
            stageHitTime = config != null ? config.HitTime(currentWall) : 0f;
            wallWarned = false;

            for (int i = 0; i < playingTracks.Count; i++)
            {
                HoleInWallTrack track = playingTracks[i];
                track.BeginWall();

                // Дорожка, оставшаяся без участников, уходит со СЛЕДУЮЩЕЙ
                // стены, а не в момент ухода: пустая платформа посреди подъезда
                // читалась бы как чужая брошенная (спека 10.2).
                if (!track.Active && track.gameObject.activeSelf)
                {
                    track.Wall.Retire();
                    track.gameObject.SetActive(false);
                }
            }
        }

        private void HandleStageElapsed(byte stage)
        {
            if (config == null)
            {
                return;
            }

            int next = stage - FirstStage + 1;
            if (next >= config.WallCount)
            {
                // Досрочного конца в игре нет: раунд кончается после
                // разрешения последней стены, провалившие продолжают играть.
                // Final verdicts are resolved in FixedUpdate before entering Results.
                return;
            }

            stageState.EnterStage((byte)(next + FirstStage), config.StageDuration(next));
        }

        /// <summary>
        /// Такт раунда. Идёт на <b>каждой</b> машине, а не только у сервера:
        /// пустить стену и довезти её до конца обязан каждый, иначе арена
        /// у клиента была бы пустой. Что именно делает только сервер, отмечено
        /// внутри <see cref="TickTrack"/>.
        /// </summary>
        private void FixedUpdate()
        {
            if (!RoundActive || config == null || currentWall < 0 || !roundStartKnown)
            {
                return;
            }

            double now = NetworkClock.Now;
            float elapsed = (float)(now - roundStartTime);
            if (HasAuthority && Timer != null)
                Timer.SyncFromNetwork(Mathf.Max(0f, config.RoundLength - elapsed), config.RoundLength);

            // Сигнал звучит один раз на стену, а не на дорожку: момент удара
            // у всех дорожек общий, и четыре источника дали бы четырёхкратную
            // громкость вместо подсказки. Звук локальный — по сети не едет.
            if (!wallWarned && elapsed >= stageHitTime - config.WarningLead)
            {
                wallWarned = true;
                WallWarning?.Invoke();
            }

            for (int i = 0; i < playingTracks.Count; i++)
            {
                TickTrack(playingTracks[i], elapsed, now);
            }

            if (HasAuthority && currentWall == config.WallCount - 1 && elapsed >= config.RoundLength)
            {
                for (int i = 0; i < playingTracks.Count; i++)
                    if (playingTracks[i].Active && !playingTracks[i].WallResolved) return;
                Timer?.SyncFromNetwork(0f, config.RoundLength);
                EndMinigame();
            }
        }

        private void TickTrack(HoleInWallTrack track, float elapsed, double now)
        {
            if (track.Tether != null)
            {
                bool lifting = false;
                for (int slot = 0; slot < track.Members.Count; slot++)
                {
                    int key = track.Index * 2 + slot;
                    lifting |= key < recoveries.Length && recoveries[key] != null && recoveries[key].Active;
                }
                track.Tether.enabled = !lifting;
            }
            SweepingWall wall = track.Wall;

            // Пуск считает каждая машина сама — это чистая функция от
            // объявленного момента начала раунда и таблицы подъездов, поэтому
            // на движение стены не уходит ни одного пакета. Стена одиночки
            // стартует позже: подъезд у неё короче на те же 25 %, на которые
            // выше скорость, а момент удара общий.
            // Рисунок обязателен: у клиента он приезжает отдельным списком
            // и может опоздать на кадр-другой относительно стадии. Без этой
            // проверки стена той стены просто не поехала бы вовсе — пуск
            // бывает один раз, а промахнуться им можно навсегда.
            if (track.Active && !track.WallLaunched && currentWall < track.Patterns.Count &&
                elapsed >= stageHitTime - config.ApproachSeconds(currentWall, track.Solo))
            {
                LaunchWall(track);
            }

            // Своя половина проверки — на каждой машине и по её часам: у
            // клиента стена доходит до линии позже, чем у сервера. Раньше
            // вердикта: у хоста иначе в строку попадало тело, уже сметённое.
            if (track.Active && track.WallLaunched && !track.LocalChecked && wall.FrontZ <= config.CheckLineZ)
            {
                track.LocalChecked = true;
                CheckDrivenMembers(track);
            }

            // 🔴 Вердикт — только сервер и только один раз. Момент проверки —
            // передняя грань стены на линии проверки; формула та же, по которой
            // стена и едет, разойтись им негде. Клиент исход не считает
            // и узнаёт его оповещением (спека 10.1). На линии сервер только
            // начинает разбор: тела клиентов он дождётся в их отчётах
            // (HoleInWallCheck), но не дольше ReportTimeout.
            if (HasAuthority && track.Active && track.WallLaunched && !track.WallResolved)
            {
                if (!track.CheckPending && wall.FrontZ <= config.CheckLineZ)
                {
                    OpenCheck(track, now);
                }

                if (track.CheckPending && (track.AllChecked || now >= track.CheckDeadline))
                {
                    ResolveWall(track, "линия проверки");
                }
            }

            GateCollision(track);

            if (wall.Running && wall.Finished)
            {
                wall.Retire();
            }

            if (HasAuthority)
            {
                TickReturns(track, now);
            }
        }

        private void LaunchWall(HoleInWallTrack track)
        {
            if (currentWall >= track.Patterns.Count)
            {
                Debug.LogError($"{name}: у дорожки {track.Index} нет рисунка стены {currentWall + 1}", this);
                return;
            }

            // Отметка ставится только после того, как пуск состоялся: иначе
            // единственная попытка сгорела бы на недоехавшем расписании.
            track.WallLaunched = true;

            WallPattern pattern = track.Patterns[currentWall];
            double startTime = roundStartTime + stageHitTime - config.ApproachSeconds(currentWall, track.Solo);
            double trickTime = roundStartTime + stageHitTime - TrickLead(pattern.Trick);

            track.Wall.Launch(pattern, startTime, config.WallSpeed(currentWall, track.Solo), trickTime);
        }

        private float TrickLead(WallTrick trick)
        {
            switch (trick)
            {
                case WallTrick.Mirror:
                    return config.MirrorLead;
                case WallTrick.Morph:
                    return config.MorphLead;
                default:
                    return 0f;
            }
        }

        // ========== ВЕРДИКТ ==========

        /// <summary>
        /// Единственное место, где решается исход стены. Зовётся только под
        /// авторитетом и ровно один раз на дорожку и стену.
        /// </summary>
        private void ResolveWall(HoleInWallTrack track, string cause)
        {
            track.WallResolved = true;
            DecideRemaining(track);

            bool sweptEarly = earlyFinalFailures.Contains(track.Index);
            bool passed = !sweptEarly && TrackFits(track);
            LogVerdict(track, passed, sweptEarly ? "сметены ударом раньше срока" : cause);
            if (passed)
            {
                track.AwardWall(currentWall + 1, NetworkClock.Now);

                // Плиты снимаются только у прошедших. Пара стоит в вырезах,
                // но у самого узкого выреза допуск попадания шире физического
                // зазора: оставленный коллайдер толкнул бы игрока за успешный
                // проход. Разбор с числами — в SweepingWall.ColliderRecess.
                track.Wall.DisableCollision();
            }
            else if (!sweptEarly)
            {
                SweepTrack(track);
            }

            // Счёт — состояние, и уезжает реплицированным списком. Сам вердикт —
            // событие, и уезжает оповещением: правдой он уже стал здесь.
            network?.PublishTracks(playingTracks);
            network?.AnnounceWallResolved(track.Index, currentWall, passed, track.Score);

            // И только теперь — тем, кто на исход смотрит. Порядок не случаен:
            // сначала правда уходит по сети, потом её показывают. Иначе
            // сервер успел бы поднять брызги раньше, чем клиенты узнали,
            // из-за чего они.
            WallResolved?.Invoke(track, passed);
        }

        /// <summary>
        /// Исход стены, посчитанный сервером. Клиент его <b>узнаёт</b>, а не
        /// считает: отметка нужна, чтобы он не пытался решить сам, и чтобы
        /// в фазе 4 было к чему цеплять звук удара и брызги.
        /// </summary>
        public void ApplyNetworkWallResolved(int trackIndex, int wallIndex, bool passed, int score)
        {
            if (HasAuthority)
            {
                return;
            }

            HoleInWallTrack track = TrackByIndex(trackIndex);
            if (track == null)
            {
                return;
            }

            // Results can arrive before the NetworkList delta in the same tick.
            // Apply the server snapshot even if the stage announcement is late.
            track.ApplyScore(score);
            if (wallIndex != currentWall) return;

            track.WallResolved = true;

            // Прошедшим плиты не нужны и у клиента. Обычно их уже снял
            // GateCollision, но тело пары ведёт не только эта машина.
            if (passed)
            {
                track.Wall.DisableCollision();
            }

            // Вторая половина той же точки: у сервера вердикт объявляется
            // подсчётом, у клиента — приёмом. Дальше подписчик один и тот же,
            // и различать эти две половины ему не нужно.
            WallResolved?.Invoke(track, passed);
        }

        /// <summary>
        /// Пролезла ли дорожка в свою стену: каждый участник в своём вырезе.
        /// Зовётся, когда разбор участников уже собран (<see cref="DecideRemaining"/>).
        ///
        /// Крест-накрест не считается: вырез вырезан по силуэту конкретного
        /// игрока, и чужой ему просто не по фигуре. Кто чей, видно по цвету
        /// контура — он совпадает с цветом половины пола.
        /// </summary>
        private bool TrackFits(HoleInWallTrack track)
        {
            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;
            if (members.Count == 0 || (members.Count > 1 && track.Wall.CutoutCount < members.Count))
            {
                return false;
            }

            for (int m = 0; m < members.Count; m++)
            {
                if (!VerdictFit(track, m).Fits)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Разбор участника под этим номером против его выреза — три условия
        /// спеки 5.3 разом: поза, горизонталь, опора.
        ///
        /// У пары вырез закреплён за местом: нулевой участник — нулевой вырез.
        /// Одиночке годится любой: обычно вырез один, но пара, потерявшая
        /// напарника посреди подъезда, доигрывает эту стену прежним рисунком
        /// на двоих, и лезть оставшемуся есть куда — менять вырез в момент
        /// подъезда нечестно (спека 10.2). Ему отдаётся лучший из двух.
        ///
        /// <b>Проверка осталась дискретной сознательно.</b> Геометрическая
        /// («силуэт внутри контура») выглядит честнее, но поза дрожит, и
        /// попадание начало бы зависеть от фазы дрожи. Допуск берётся у форм
        /// участника (<c>CutoutShapes.Tolerance</c>) — это запас контура, а
        /// последние сантиметры доводит <see cref="WallFunnel"/>.
        /// </summary>
        private HoleInWallFit FitFor(HoleInWallTrack track, int memberIndex)
        {
            PlayerController avatar = track.Members[memberIndex].Avatar;
            return FitFor(track, memberIndex, avatar != null ? avatar.Position : Vector3.zero);
        }

        /// <summary>То же, но тело стоит там, где сказано: так разбирается отчёт владельца.</summary>
        private HoleInWallFit FitFor(HoleInWallTrack track, int memberIndex, Vector3 position)
        {
            HoleInWallTrack.Member member = track.Members[memberIndex];
            if (track.Members.Count > 1)
            {
                return HoleInWallFit.Measure(config, track, member, memberIndex, position);
            }

            HoleInWallFit best = HoleInWallFit.Measure(config, track, member, 0, position);
            for (int cutout = 1; cutout < track.Wall.CutoutCount && !best.Fits; cutout++)
            {
                HoleInWallFit next = HoleInWallFit.Measure(config, track, member, cutout, position);
                if (next.Fits || (next.PoseOk && Mathf.Abs(next.OffsetX) < Mathf.Abs(best.OffsetX)))
                {
                    best = next;
                }
            }

            return best;
        }

        /// <summary>Разбор, по которому судим: собранный, а если его нет — вид сервера прямо сейчас.</summary>
        private HoleInWallFit VerdictFit(HoleInWallTrack track, int memberIndex)
        {
            HoleInWallCheck check = track.Members[memberIndex].Check;
            return check.Decided ? check.Fit : FitFor(track, memberIndex);
        }

        // ========== РАЗБОР НА ЛИНИИ ==========

        /// <summary>
        /// Сервер дошёл до линии проверки. Тела, которые он ведёт сам,
        /// разбираются сразу: его вид и есть правда. За телами клиентов он
        /// видит копии с опозданием — их разбор приедет отчётом владельца,
        /// а свой вид сохраняется на случай, если отчёт не придёт.
        /// </summary>
        private void OpenCheck(HoleInWallTrack track, double now)
        {
            track.OpenCheck(now, HoleInWallCheck.ReportTimeout);

            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;
            for (int m = 0; m < members.Count; m++)
            {
                HoleInWallTrack.Member member = members[m];
                HoleInWallFit seen = FitFor(track, m);

                if (member.DrivenHere)
                {
                    member.Check.Decide(seen, HoleInWallCheck.Source.Server, now);
                    continue;
                }

                member.Check.Await(seen);
                if (member.Check.HasEarlyReport)
                {
                    AcceptReport(track, m, member.Check.EarlyReport, now);
                }
            }
        }

        /// <summary>
        /// Отчёт владельца приехал. Кто отчитался — определила сетевая
        /// половина по отправителю пакета.
        /// </summary>
        /// <param name="struck">Плита коснулась тела на его машине раньше линии.</param>
        public void ApplyCheckReport(int playerId, int wallIndex, Vector3 position, bool struck)
        {
            if (!HasAuthority || !RoundActive || wallIndex != currentWall)
            {
                return;
            }

            HoleInWallTrack track = TrackOf(playerId);
            int m = track != null ? track.IndexOfMember(playerId) : -1;
            if (m < 0 || track.WallResolved || !track.WallLaunched)
            {
                return;
            }

            double now = NetworkClock.Now;
            HoleInWallCheck check = track.Members[m].Check;

            if (struck)
            {
                AcceptStrike(track, m, position, now);
                return;
            }

            if (!track.CheckPending)
            {
                // Часы клиента сошлись с серверными не до миллисекунды: отчёт
                // бывает раньше линии у сервера. Разберём его на линии.
                check.StashEarlyReport(position);
                return;
            }

            if (check.AwaitingReport)
            {
                AcceptReport(track, m, position, now);
            }
        }

        /// <summary>
        /// Проверить отчёт и решить по нему. Место — из отчёта, если оно
        /// правдоподобно; поза и вырез — свои, серверные.
        /// </summary>
        private void AcceptReport(HoleInWallTrack track, int memberIndex, Vector3 position, double now)
        {
            HoleInWallTrack.Member member = track.Members[memberIndex];
            if (member.Avatar == null || !HoleInWallCheck.Plausible(position, member.Avatar.Position))
            {
                Debug.LogWarning($"[Дырка] отчёт id={member.PlayerId} отклонён: присланное место " +
                                 $"{position} дальше {HoleInWallCheck.ReportSlack} м от видимого сервером", this);
                member.Check.DecideByFallback(HoleInWallCheck.Source.Rejected, now);
                return;
            }

            member.Check.Decide(FitFor(track, memberIndex, position), HoleInWallCheck.Source.Owner, now);
        }

        /// <summary>
        /// Клиент сообщил об ударе стеной по своему телу. Сервер удар засчитывает,
        /// только если и по его разбору участник в вырез не влез.
        /// </summary>
        private void AcceptStrike(HoleInWallTrack track, int memberIndex, Vector3 position, double now)
        {
            HoleInWallTrack.Member member = track.Members[memberIndex];
            if (track.CheckPending || earlyFinalFailures.Contains(track.Index) || member.Avatar == null ||
                !HoleInWallCheck.Plausible(position, member.Avatar.Position))
            {
                return;
            }

            HoleInWallFit fit = FitFor(track, memberIndex, position);
            if (fit.Fits)
            {
                return;
            }

            member.Check.Decide(fit, HoleInWallCheck.Source.Owner, now);
            StrikeTrack(track, member.PlayerId);
        }

        /// <summary>
        /// Вердикт выносится: кто не разобран — тот по виду сервера. Отчёта
        /// ждали и не дождались — по виду на линии; на линию вовсе не вышли
        /// (удар раньше неё) — по виду прямо сейчас.
        /// </summary>
        private void DecideRemaining(HoleInWallTrack track)
        {
            double now = NetworkClock.Now;
            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;
            for (int m = 0; m < members.Count; m++)
            {
                HoleInWallCheck check = members[m].Check;
                if (check.Decided)
                {
                    continue;
                }

                if (check.AwaitingReport)
                {
                    check.DecideByFallback(HoleInWallCheck.Source.Timeout, now);
                }
                else
                {
                    check.Decide(FitFor(track, m), members[m].DrivenHere
                        ? HoleInWallCheck.Source.Server
                        : HoleInWallCheck.Source.Seen, now);
                }
            }
        }

        /// <summary>
        /// Своя половина проверки: машина, которая ведёт тело, сверяет его
        /// в тот момент, когда стена дошла до линии <b>на её экране</b>.
        /// Клиент отдаёт это серверу отчётом; в сетевой катке каждая машина
        /// печатает свою строку — расхождение со строкой вердикта у сервера
        /// и есть то, что игрок видел иначе, чем его судили.
        /// </summary>
        private void CheckDrivenMembers(HoleInWallTrack track)
        {
            if (!WorldAuthority.IsNetworkSession)
            {
                return;
            }

            for (int m = 0; m < track.Members.Count; m++)
            {
                HoleInWallTrack.Member member = track.Members[m];
                if (!member.DrivenHere || member.Avatar == null)
                {
                    continue;
                }

                verdictLine.Clear();
                verdictLine.Append("[Дырка] у себя: стена ").Append(currentWall + 1)
                    .Append(", дорожка ").Append(track.Index)
                    .Append(" · id=").Append(member.PlayerId).Append(' ');
                FitFor(track, m).Describe(verdictLine);
                if (member.Funnel != null)
                {
                    verdictLine.Append(" · воронка ").Append(member.Funnel.EngagedSteps)
                        .Append(" шагов с ").Append(member.Funnel.EngagedFrom.ToString("+0.00;-0.00"));
                }

                Debug.Log(verdictLine.ToString(), this);

                if (!HasAuthority && !track.StrikeReported)
                {
                    network?.ReportCheck(currentWall, member.Avatar.Position, struck: false);
                }
            }
        }

        /// <summary>
        /// Снять с плит столкновения для тела, которое ведёт эта машина, если
        /// оно стоит в своём вырезе. Решает каждая машина за своё тело и по
        /// своей стене — в тот момент, когда видимая грань дошла до капсулы:
        /// плиты утоплены на <c>ColliderRecess</c> и подойдут к телу позже.
        ///
        /// ⚠️ Раньше плиты снимал только вердикт у сервера. Клиенту его стена
        /// приезжает позже, и прошедшего, который ещё подстраивался, она могла
        /// столкнуть в воду уже после засчитанного очка — вероятная причина
        /// «напарник упал, а второму засчитали» (IGR-594). Исход это не решает:
        /// не влезший по вердикту всё равно получит сметание от сервера.
        /// </summary>
        private void GateCollision(HoleInWallTrack track)
        {
            SweepingWall wall = track.Wall;
            if (!track.Active || !track.WallLaunched || !wall.Running || !wall.CollisionEnabled)
            {
                return;
            }

            bool reached = false;
            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;
            for (int m = 0; m < members.Count; m++)
            {
                HoleInWallTrack.Member member = members[m];
                if (member.Avatar == null || !member.DrivenHere ||
                    wall.FrontZ > member.Avatar.Position.z + member.Radius)
                {
                    continue;
                }

                if (!FitFor(track, m).Fits)
                {
                    // Не влез — плиты остаются: стена обязана его ударить.
                    return;
                }

                reached = true;
            }

            if (reached)
            {
                wall.DisableCollision();
            }
        }

        /// <summary>
        /// «Почему упал» — строка на каждый вердикт, у сервера. По каждому
        /// участнику: поза против выреза, смещение против допуска, высота против
        /// опоры и чем это установлено — своим видом сервера или отчётом
        /// владельца (с задержкой отчёта после линии). Без неё жалобу «прошёл,
        /// а упал» нечем было проверить.
        /// </summary>
        private void LogVerdict(HoleInWallTrack track, bool passed, string cause)
        {
            verdictLine.Clear();
            verdictLine.Append("[Дырка] вердикт: стена ").Append(currentWall + 1)
                .Append(", дорожка ").Append(track.Index).Append(" — ")
                .Append(passed ? "ПРОШЛИ" : "ПРОВАЛ").Append(" (").Append(cause).Append(')');

            for (int m = 0; m < track.Members.Count; m++)
            {
                HoleInWallCheck check = track.Members[m].Check;
                verdictLine.Append(" · id=").Append(track.Members[m].PlayerId).Append(' ');
                VerdictFit(track, m).Describe(verdictLine);
                verdictLine.Append(" [").Append(HoleInWallCheck.Label(check.DecidedBy));
                if (check.DecidedBy == HoleInWallCheck.Source.Owner && track.CheckPending)
                {
                    verdictLine.Append(' ').Append((check.DecidedAt - track.CheckOpenedAt).ToString("+0.00")).Append(" с");
                }

                verdictLine.Append(']');
            }

            Debug.Log(verdictLine.ToString(), this);
        }

        /// <summary>
        /// Стена сметает дорожку. Провал парный: ошибся один — летят оба,
        /// они связаны тросом.
        /// </summary>
        private void SweepTrack(HoleInWallTrack track)
        {
            // Импульс считается от скорости ЭТОЙ стены, а не берётся постоянным:
            // подъезд к концу раунда короче, стена быстрее, и заданных 16
            // перестаёт хватать, чтобы её обогнать. Разбор с числами —
            // в HoleInWallConfig.SweepImpulseFor.
            float wallSpeed = config.WallSpeed(currentWall, track.Solo);
            Vector3 direction =
                (SweepingWall.TravelDirection + Vector3.up * config.SweepUpward).normalized;

            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;
            for (int i = 0; i < members.Count; i++)
            {
                HoleInWallTrack.Member member = members[i];
                if (member.Avatar == null)
                {
                    continue;
                }

                // Стена идёт навстречу взгляду, то есть прилетает в лицо:
                // отсюда отлёт назад, а не подсечка сзади.
                //
                // Через ApplyWorldImpulse, а не напрямую: решает сервер, но
                // телом распоряжается машина владельца, и прямой вызов на
                // чужой копии тут же перетёрло бы сетевым транспортом.
                // Масса берётся у самого тела, а не из общего конфига: если
                // персонажи когда-нибудь разойдутся по массе, обгон стены
                // обязан считаться каждому свой.
                float mass = member.Avatar.TryGetComponent(out Rigidbody avatarBody) ? avatarBody.mass : 1f;

                member.Avatar.ApplyWorldImpulse(
                    direction * config.SweepImpulseFor(wallSpeed, mass), KnockdownType.FlyBack);
                ScheduleReturn(member, config.SweptReturnSeconds, config.SweptReturnMinSeconds);
            }
        }

        // ========== ВОЗВРАТ ИЗ ВОДЫ ==========

        /// <summary>
        /// Кто в воде — тому назначен возврат. Проверка по высоте, а не по
        /// зоне: <c>KillZone</c> на бассейне стоит в режиме <c>EventOnly</c>
        /// и исхода не решает, а высота одинаково видна и серверу, и любому
        /// зрителю. Тот же приём, что в «Рейсе на память».
        /// </summary>
        private void TickReturns(HoleInWallTrack track, double now)
        {
            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;
            for (int i = 0; i < members.Count; i++)
            {
                HoleInWallTrack.Member member = members[i];
                if (member.Avatar == null)
                {
                    continue;
                }

                Vector3 position = member.Avatar.Position;

                // ⚠️ Улетел за арену — возвращаем немедленно, без барахтанья.
                // Бассейн ловит только тех, кто упал в него; вылетевшего за
                // борт ловить нечем, и обычная задержка означала бы три
                // секунды падения в чёрную пустоту. Такого падения не должно
                // видеть вообще, поэтому здесь не расписание, а сразу.
                if (OutsideArena(position))
                {
                    ClearPose(member);
                    ReturnMember(track, member);
                    continue;
                }

                if (!member.Returning && position.y < config.WaterSurfaceY)
                {
                    // Сам сошёл с платформы: полёта не было, только барахтанье.
                    WarnFallAfterPass(track, member, now);
                    ScheduleReturn(member, config.SplashSeconds, config.MinSplashSeconds);
                }

                if (member.Returning && !member.RecoveryStarted && now >= member.ReturnAt - config.RecoveryTransitSeconds)
                {
                    Transform slot = track.SlotOf(track.IndexOfMember(member.PlayerId));
                    if (slot != null)
                    {
                        Vector3 destination = slot.position + Vector3.up * .034f;
                        ApplyNetworkRecovery(member.PlayerId, position, destination, now, member.ReturnAt);
                        network?.AnnounceRecovery(member.PlayerId, position, destination, now, member.ReturnAt);
                    }
                }

                if (member.Returning && now >= member.ReturnAt)
                {
                    ReturnMember(track, member, true);
                }
            }
        }

        /// <summary>
        /// Прошедшего стена сталкивать не должна. Упал в воду сам вскоре после
        /// засчитанного прохода — значит, его столкнуло что-то, чего вердикт
        /// не видел: чаще всего плиты стены на машине, которая ведёт тело.
        /// </summary>
        private void WarnFallAfterPass(HoleInWallTrack track, HoleInWallTrack.Member member, double now)
        {
            double sincePass = now - track.PassedAt;
            if (sincePass > FallAfterPassWindow)
            {
                return;
            }

            Debug.LogWarning($"[Дырка] ⚠ id={member.PlayerId} упал в воду через {sincePass:F2} с " +
                             $"после засчитанной стены {track.PassedWall} (дорожка {track.Index})", this);
        }

        /// <summary>
        /// Назначить возврат. Задержку считает <see cref="HoleInWallConfig.ReturnDelay"/>
        /// по расписанию, а не по одному числу: <paramref name="ceiling"/> и
        /// <paramref name="floor"/> — границы этого пути падения, внутри них
        /// момент выбирает ближайший удар.
        ///
        /// Зовётся только под авторитетом — и сметание, и разбор воды серверные,
        /// — поэтому <c>roundStartTime</c> здесь заведомо свой, а не приехавший.
        /// </summary>
        private void ScheduleReturn(HoleInWallTrack.Member member, float ceiling, float floor)
        {
            if (member.Returning)
            {
                return;
            }

            double now = NetworkClock.Now;

            // Без объявленного начала раунда расписания нет вовсе: тогда
            // работает прежнее поведение — полная задержка пути.
            float delay = roundStartKnown
                ? config.ReturnDelay((float)(now - roundStartTime), ceiling, floor)
                : ceiling;

            member.Returning = true;
            member.ReturnAt = now + delay;

            // Полетел — позы больше нет. Поза это стойка на платформе, и в воде
            // ей взяться неоткуда: сметённый обязан лететь и плыть обычным
            // телом, а не ехать в позе, в которой не пролез.
            //
            // Здесь, а не в SweepTrack: сюда сходятся оба пути в воду — и снос
            // стеной, и сход с платформы своими ногами, — и оба серверные
            // (см. заметку о HasAuthority выше).
            ClearPose(member);
        }

        /// <summary>
        /// Снять позу под авторитетом и объявить снятие всем. Отдельный метод,
        /// а не вызов <see cref="ApplyPose"/>: тот отсеивает участников не этого
        /// раунда поиском по дорожкам, а здесь участник уже в руках.
        /// </summary>
        private void ClearPose(HoleInWallTrack.Member member)
        {
            if (member.Pose == null || member.Pose.CurrentPose == HoleInWallPose.None)
            {
                return;
            }

            member.Pose.SetPose(HoleInWallPose.None);
            network?.PublishPose(member.PlayerId, HoleInWallPose.None);
        }

        /// <summary>
        /// Человек вне арены: ниже дна бассейна или за его бортами.
        ///
        /// Границы берутся с запасом в полметра, чтобы обычное барахтанье
        /// у самого борта не считалось вылетом. Ниже дна оказаться нельзя
        /// вовсе — дно сплошное, — но проверка стоит: провалиться сквозь
        /// коллайдер на скорости 13 м/с физике по силам, и тогда падение
        /// уже ничем не кончится.
        /// </summary>
        private bool OutsideArena(Vector3 position)
        {
            if (position.y < config.PoolBottomY - ArenaMargin)
            {
                return true;
            }

            float halfWidth = config.ArenaWidth * 0.5f + ArenaMargin;
            return Mathf.Abs(position.x) > halfWidth ||
                   position.z < config.ArenaNearZ - ArenaMargin ||
                   position.z > config.ArenaFarZ + ArenaMargin;
        }

        private void ReturnMember(HoleInWallTrack track, HoleInWallTrack.Member member, bool finishRecovery = false)
        {
            bool scheduled = finishRecovery && member.RecoveryStarted;
            member.Returning = false;
            member.RecoveryStarted = false;

            int slot = track.IndexOfMember(member.PlayerId);
            int recoveryKey = track.Index * 2 + slot;
            // The server already scheduled the destination and deadline. Let the
            // owner's continuous path finish there; a second teleport would cut
            // through the final get-up blend. Emergency/end-of-round resets below
            // still use the authoritative teleport.
            if (scheduled && recoveryKey >= 0 && recoveryKey < recoveries.Length && recoveries[recoveryKey] != null)
            {
                recoveries[recoveryKey].Complete();
                return;
            }
            if (recoveryKey >= 0 && recoveryKey < recoveries.Length) recoveries[recoveryKey]?.Cancel();
            Transform point = track.SlotOf(slot);
            if (point == null)
            {
                return;
            }

            member.Avatar.RequestTeleport(point.position + Vector3.up * .034f, point.rotation);
        }

        public void ApplyNetworkRecovery(int playerId, Vector3 from, Vector3 landing, double start, double end)
        {
            foreach (var track in playingTracks)
                for (int slot = 0; slot < track.Members.Count; slot++)
                {
                    var member = track.Members[slot];
                    if (member.PlayerId != playerId || member.Avatar == null) continue;
                    int key = track.Index * 2 + slot;
                    if (key >= recoveries.Length || recoveries[key] == null) return;
                    member.RecoveryStarted = true;
                    recoveries[key].Begin(member.Avatar, from, landing, config.RecoveryClearZ, config.WaterSurfaceY, start, end);
                    return;
                }
        }

        // ========== КОНЕЦ РАУНДА ==========

        protected override void OnRoundEnded()
        {
            Timer?.SyncFromNetwork(0f, config.RoundLength);
            foreach (var recovery in recoveries) recovery?.Cancel();
            // ⚠️ Три роли обязаны сниматься здесь, иначе уедут в хаб вместе
            // с персонажем: фиксированный фронт, трос и поза. Плюс возвращается
            // штатный присед на Ctrl, выключенный на время раунда (спека 10.3).
            for (int i = 0; i < playingTracks.Count; i++)
            {
                HoleInWallTrack track = playingTracks[i];
                track.Wall.Retire();

                IReadOnlyList<HoleInWallTrack.Member> members = track.Members;
                for (int m = 0; m < members.Count; m++)
                {
                    if (HasAuthority && members[m].Avatar != null)
                        ReturnMember(track, members[m]);
                    ReleaseMember(members[m]);
                }
            }

            ReleaseTethers();
            stageState?.StopSequence();
            currentWall = -1;
            roundStartKnown = false;

            LogRoleAudit();
        }

        private static void ReleaseMember(HoleInWallTrack.Member member)
        {
            if (member.Stuck != null)
            {
                member.Stuck.Tether = null;
            }

            // Точка респавна указывает внутрь этой сцены: оставленная, она
            // уедет в хаб ссылкой на уже выгруженный объект.
            member.Respawner?.SetRespawnPoint(member.OriginalRespawnPoint);

            if (member.Avatar == null)
            {
                return;
            }

            member.Avatar.FacingOverride = null;

            if (member.Pose != null)
            {
                // ⚠️ Сначала выключаем, и только потом уничтожаем. Способность
                // снимает позу и запрет Ctrl-приседа в OnDisable, а Destroy
                // у Unity отложенный: до конца кадра компонент жив, и всё это
                // время персонаж стоит с отключённым приседом. Кадр — мелочь,
                // но роль обязана сниматься там, где написано, а не когда-то
                // потом: иначе проверить это нечем, а симптом «не работает
                // у одного человека из всех» ищут потом сутки.
                member.Pose.enabled = false;
                Destroy(member.Pose);
            }

            // Воронка тем же порядком: сначала перестаёт доводить, потом
            // уходит. Оставленная, она тянула бы игрока к вырезу уже в хабе —
            // стены там нет, но ссылка на неё пережила бы выгрузку сцены.
            if (member.Funnel != null)
            {
                member.Funnel.Release();
                Destroy(member.Funnel);
            }

            // Вода тем же порядком. Оставленная, она увезла бы в хаб потолок
            // скорости 1.6 м/с: там воды нет, а медленный персонаж есть.
            if (member.Buoyancy != null)
            {
                member.Buoyancy.Release();
                Destroy(member.Buoyancy);
            }
        }

        private void ReleaseTethers()
        {
            for (int i = 0; i < tracks.Length; i++)
            {
                if (tracks[i] != null)
                {
                    ReleaseTether(tracks[i]);
                }
            }
        }

        /// <summary>
        /// Снять трос с дорожки. Обязателен и в конце раунда, и в момент, когда
        /// пара теряет одного: иначе оставшегося таскает за мёртвым напарником,
        /// а в хабе — за живым.
        /// </summary>
        private void ReleaseTether(HoleInWallTrack track)
        {
            if (track.Tether == null)
            {
                return;
            }

            track.Tether.Release();
            Destroy(track.Tether.gameObject);
            track.Tether = null;

            // Детектор застревания держал на трос ссылку: оставленная, она
            // указывала бы на уничтоженный объект.
            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].Stuck != null)
                {
                    members[i].Stuck.Tether = null;
                }
            }
        }

        private void ClearRound()
        {
            ReleaseTethers();

            playingTracks.Clear();
            trackByPlayer.Clear();
            pairs.Clear();
            frozenScores.Clear();
            currentWall = -1;

            // ⚠️ Момент начала раунда здесь НЕ сбрасывается. Клиент собирает
            // дорожки этим же путём, когда приезжает состав, — а состав вполне
            // может приехать позже объявленного момента. Сбросив его тут,
            // мы получили бы клиента, у которого стены не едут вовсе.
            for (int i = 0; i < tracks.Length; i++)
            {
                if (tracks[i] != null)
                {
                    tracks[i].ResetTrack();
                }
            }
        }

        // ========== МЕСТА ==========

        /// <summary>
        /// Место каждому из 2–8 участников: по числу пройденных стен.
        ///
        /// Оба в паре всегда имеют одинаковый счёт, поэтому делят одно место
        /// автоматически — отдельного правила под пары не нужно.
        /// </summary>
        protected override void CollectResults(MinigameResults results)
        {
            ranking.Clear();

            for (int i = 0; i < Players.Count; i++)
            {
                int playerId = Players[i].Id;
                ranking.Add(playerId, ScoreOf(playerId));
            }

            ranking.Build(results);
        }

        /// <summary>
        /// Счёт участника. У ушедшего он заморожен на моменте выхода: дорожку
        /// после него мог доигрывать напарник, и те очки уже не его. Место
        /// ушедший всё равно получает наравне со всеми (спека 10.2) — из состава
        /// раунда его не вычёркиваем.
        /// </summary>
        private int ScoreOf(int playerId)
        {
            if (frozenScores.TryGetValue(playerId, out int frozen))
            {
                return frozen;
            }

            HoleInWallTrack track = TrackOf(playerId);
            return track != null ? track.Score : 0;
        }

        private PlayerController FindAvatar(int playerId)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].Id == playerId)
                {
                    return Players[i].Avatar;
                }
            }

            return null;
        }

        // ========== ПОЗА ==========

        /// <summary>
        /// Намерение игрока встать в позу. Зовёт <see cref="PlayerPoseAbility"/>
        /// на машине того, кто нажал.
        ///
        /// Поза — единственное действие в этой игре и единственное, что решает
        /// исход, поэтому применяет её сервер: клиент шлёт номер, сервер
        /// проверяет и кладёт в реплицируемое состояние. Отклик при этом
        /// остаётся мгновенным — своё изображение клиент меняет сразу, не дожидаясь
        /// круга до сервера, иначе игра играется с пингом в единственном
        /// своём действии. Сервер перепишет, если не согласен.
        /// </summary>
        public void SubmitPoseIntent(int playerId, HoleInWallPose pose)
        {
            if (!RoundActive)
            {
                return;
            }

            if (!CanHoldPose(playerId)) pose = HoleInWallPose.None;
            if (HasAuthority)
            {
                ApplyPose(playerId, pose);
                return;
            }

            PoseOf(playerId)?.SetPose(pose);
            network?.SubmitPose(pose);
        }

        /// <summary>
        /// Принять позу игрока. <b>Единственное место, где поза становится
        /// общей правдой</b>, и работает оно только под авторитетом.
        ///
        /// Диапазон проверяется здесь, а не у отправителя: из сети приезжает
        /// байт, и он может быть любым.
        ///
        /// <b>Сброс ходит этим же маршрутом.</b> <see cref="HoleInWallPose.None"/>
        /// здесь законное значение, и отдельного намерения под него не заведено
        /// намеренно: транспорт уже возит номер позы байтом и ноль в нём
        /// помещается, а второй маршрут пришлось бы дублировать целиком —
        /// RPC, проверку, публикацию. Проверять его строже, чем обычную позу,
        /// не за что: подделать отправителя нельзя (номер берётся из пакета,
        /// см. <c>HoleInWallNetwork.SetPoseRpc</c>), а снять позу можно только
        /// себе — и это чистый проигрыш, а не преимущество.
        /// </summary>
        public void ApplyPose(int playerId, HoleInWallPose pose)
        {
            if (!HasAuthority || !RoundActive)
            {
                return;
            }

            if ((int)pose > HoleInWallConfig.PoseCount)
            {
                return;
            }

            PlayerPoseAbility ability = PoseOf(playerId);
            if (ability == null)
            {
                // Не участник этого раунда — позы у него нет и быть не может.
                return;
            }

            // Отказ обязан доехать до клиента: у себя он позу уже показал.
            bool refused = pose != HoleInWallPose.None && !CanHoldPose(playerId);
            if (refused) pose = HoleInWallPose.None;
            ability.SetPose(pose);
            network?.PublishPose(playerId, pose, correction: refused);
        }

        private bool CanHoldPose(int playerId)
        {
            HoleInWallTrack track = TrackOf(playerId);
            if (track == null) return false;
            int index = track.IndexOfMember(playerId);
            if (index < 0) return false;
            var member = track.Members[index];
            return member.Avatar != null && !member.Returning && !member.Avatar.IsKnockedDown &&
                member.Avatar.Position.y >= config.WaterSurfaceY;
        }

        /// <summary>Поза, подтверждённая сервером. Клиент её только применяет.</summary>
        public void ApplyNetworkPose(int playerId, HoleInWallPose pose)
        {
            if (HasAuthority)
            {
                return;
            }

            PoseOf(playerId)?.SetPose(pose);
        }

        /// <summary>Способность позы этого участника. Пусто — участника в раунде нет.</summary>
        private PlayerPoseAbility PoseOf(int playerId)
        {
            HoleInWallTrack track = TrackOf(playerId);
            if (track == null)
            {
                return null;
            }

            int slot = track.IndexOfMember(playerId);
            return slot >= 0 ? track.Members[slot].Pose : null;
        }

        // ========== ДИСКОННЕКТЫ ==========

        /// <summary>
        /// Игрок сам вышел из раунда, оставшись в катке. Разбирается ровно так
        /// же, как уход по обрыву связи: для дорожки разницы нет.
        /// </summary>
        protected override void OnPlayerLeftRound(int playerId) => HandlePlayerLeft(playerId);

        /// <summary>
        /// Участник ушёл. Игра парная, поэтому уход одного ломает не только
        /// его, но и напарника (спека 10.2):
        ///
        /// <list type="bullet">
        /// <item>отвалился один из пары — со <b>следующей</b> стены дорожка идёт
        /// одиночкой: вырез один, скорость +25 %. Текущая доигрывается прежним
        /// рисунком, менять вырез в момент подъезда нечестно. Счёт сохраняется;</item>
        /// <item>отвалился одиночка или оба из пары — дорожка уходит со
        /// следующей стены;</item>
        /// <item>ушедший остаётся в таблице мест и ранжируется по счёту
        /// на момент выхода.</item>
        /// </list>
        ///
        /// Зовётся только у авторитета: он один знает про уход.
        /// </summary>
        public void HandlePlayerLeft(int playerId)
        {
            if (!HasAuthority)
            {
                return;
            }

            HoleInWallTrack track = TrackOf(playerId);
            if (track == null)
            {
                return;
            }

            // Счёт замирает до того, как дорожка поедет дальше без него.
            frozenScores[playerId] = track.Score;
            trackByPlayer.Remove(playerId);

            // Трос снимается сразу: за ушедшим оставшегося таскать некому,
            // но жёсткий предел честно тянул бы его к мёртвому телу.
            ReleaseTether(track);

            if (!track.RemoveMember(playerId))
            {
                return;
            }

            if (track.Active)
            {
                RedrawAsSolo(track);
                network?.PublishSchedule(playingTracks);
            }

            network?.PublishTracks(playingTracks);

            Debug.Log($"🚪 «Дырка в стене»: игрок {playerId} вышел, дорожка {track.Index} " +
                      (track.Active ? "переходит в режим одиночки со следующей стены" : "снимается со следующей стены"), this);
        }

        /// <summary>
        /// Перерисовать дорожке всё, что она ещё не сыграла, — под одиночку.
        /// Текущую стену не трогаем: она уже пущена, и подменить ей вырез
        /// на подъезде значит обмануть того, кто под неё встал.
        /// </summary>
        private void RedrawAsSolo(HoleInWallTrack track)
        {
            generator.Generate(config, track.ShapesOf(0), null, TrackSeed(track), true, soloPatterns);

            for (int wall = currentWall + 1; wall < track.Patterns.Count && wall < soloPatterns.Count; wall++)
            {
                track.Patterns[wall] = soloPatterns[wall];
            }

            // Одиночка стоит по центру платформы: и место возврата, и точка
            // респавна переезжают туда же.
            track.ArrangeSlots(config);
            RebindSlots(track);

            // ⚠️ Места пересчитались: ушедший мог быть нулевым, и оставшийся
            // с первого места переехал на нулевое. Воронка целится по номеру
            // места, и без этого она осталась бы наведённой на вырез, которого
            // у одиночки больше нет.
            track.AimFunnels(config);
        }

        /// <summary>
        /// Пере-навесить точки возврата после смены состава: место участника
        /// внутри дорожки могло сдвинуться, а точка респавна берётся по номеру
        /// места, а не по человеку.
        /// </summary>
        private static void RebindSlots(HoleInWallTrack track)
        {
            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;
            for (int i = 0; i < members.Count; i++)
            {
                Transform slot = track.SlotOf(i);
                if (slot != null)
                {
                    members[i].Respawner?.SetRespawnPoint(slot);
                }
            }
        }

        // ========== ПРИЁМ СЕТЕВОГО СОСТОЯНИЯ ==========

        /// <summary>
        /// Состав дорожек приехал от сервера.
        ///
        /// Состав меняется дважды за раунд — на старте и на дисконнекте, —
        /// а счёт после каждой стены. Пересобирать дорожки на каждое очко
        /// нельзя: это снесло бы и расписание, и позы, и способности с аватаров.
        /// Поэтому сначала смотрим, изменился ли состав.
        /// </summary>
        /// <returns>Истина — дорожки пересобраны, разложить по ним остальное заново.</returns>
        public bool ApplyNetworkTracks(IReadOnlyList<HoleInWallTrackNetState> states)
        {
            if (HasAuthority || config == null || states == null || states.Count == 0 || Players.Count == 0)
            {
                return false;
            }

            bool rebuilt = false;

            if (playingTracks.Count != states.Count)
            {
                BuildTracksFromNetwork(states);
                rebuilt = true;
            }
            else
            {
                rebuilt = SyncMembersFromNetwork(states);
            }

            for (int i = 0; i < states.Count; i++)
            {
                HoleInWallTrack track = TrackByIndex(states[i].TrackIndex);
                track?.ApplyScore(states[i].Score);
            }

            return rebuilt;
        }

        private void BuildTracksFromNetwork(IReadOnlyList<HoleInWallTrackNetState> states)
        {
            ClearRound();

            pairs.Clear();
            for (int i = 0; i < states.Count; i++)
            {
                // Раскладка держится на позиции в списке: пара под номером i
                // встаёт на дорожку i. Номер дорожки едет в строке, и если
                // в нумерации всё же окажется дырка, добиваем её пустой парой,
                // а не сдвигаем всех влево.
                while (pairs.Count < states[i].TrackIndex)
                {
                    pairs.Add(new PairAssignment.Pair(PairAssignment.NoPlayer, PairAssignment.NoPlayer));
                }

                pairs.Add(new PairAssignment.Pair(states[i].FirstMemberId, states[i].SecondMemberId));
            }

            AssignTracks();
        }

        /// <summary>
        /// Состав уже собран — сверить его с приехавшим и снять тех, кого
        /// в нём больше нет. Пересборкой это делать нельзя: у оставшегося
        /// снялись бы способность позы и фиксированный фронт.
        /// </summary>
        private bool SyncMembersFromNetwork(IReadOnlyList<HoleInWallTrackNetState> states)
        {
            bool changed = false;

            for (int i = 0; i < states.Count; i++)
            {
                HoleInWallTrackNetState state = states[i];
                HoleInWallTrack track = TrackByIndex(state.TrackIndex);
                if (track == null)
                {
                    continue;
                }

                bool trackChanged = false;

                for (int m = track.Members.Count - 1; m >= 0; m--)
                {
                    int playerId = track.Members[m].PlayerId;
                    if (playerId == state.FirstMemberId || playerId == state.SecondMemberId)
                    {
                        continue;
                    }

                    trackByPlayer.Remove(playerId);
                    ReleaseTether(track);
                    track.RemoveMember(playerId);
                    trackChanged = true;
                }

                if (!trackChanged)
                {
                    continue;
                }

                changed = true;

                if (track.Active)
                {
                    track.ArrangeSlots(config);
                    RebindSlots(track);
                }
            }

            return changed;
        }

        /// <summary>
        /// Расписание стен приехало от сервера. Рисунок объявлен структурой,
        /// а не сидом: см. <see cref="HoleInWallWallNetState"/>.
        /// </summary>
        public void ApplyNetworkSchedule(IReadOnlyList<HoleInWallWallNetState> states)
        {
            if (HasAuthority || states == null)
            {
                return;
            }

            for (int i = 0; i < playingTracks.Count; i++)
            {
                playingTracks[i].Patterns.Clear();
            }

            for (int i = 0; i < states.Count; i++)
            {
                HoleInWallTrack track = TrackByIndex(states[i].TrackIndex);
                if (track == null)
                {
                    continue;
                }

                // Строки приходят по стенам подряд на каждую дорожку, поэтому
                // добавления хватает — сортировать нечего.
                if (track.Patterns.Count == states[i].WallIndex)
                {
                    track.Patterns.Add(states[i].ToPattern());
                }
            }
        }
    }
}
