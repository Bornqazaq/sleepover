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
    /// поэтому стена стоит у всех в одном месте без единого пакета, — но
    /// сравнивать её с позами вправе только тот, кто видит обоих партнёров
    /// в один и тот же миг. Исход парный и стоит места в таблице: при пинге
    /// двое иначе увидели бы разный итог одной стены (спека 10.1).
    ///
    /// <b>Вердикт считается один раз и на всю дорожку сразу.</b> Спека говорит
    /// «когда передняя грань доходит до центра капсулы игрока», и для одиночки
    /// это буквально так. У пары нельзя: исход парный, а сопоставить двоих
    /// с двумя вырезами можно только зная состояние обоих в один и тот же миг.
    /// Поэтому момент один — когда грань доходит до линии проверки, то есть
    /// до центра платформы, где пара и должна стоять. Платформа глубиной
    /// 5 ШП разброса почти не даёт.
    ///
    /// <b>Кто в какой вырез — не назначено заранее.</b> Пара проходит, если
    /// двоих можно разложить по двум вырезам взаимно однозначно. Из этого само
    /// собой следует и правило зеркального переворота («позы те же, места
    /// меняются»), и запрет обоим втиснуться в один вырез.
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

        [Header("Дырка в стене")]
        [SerializeField] private HoleInWallConfig config;
        [Tooltip("Все дорожки арены. Заполняется построителем арены")]
        [SerializeField] private HoleInWallTrack[] tracks = Array.Empty<HoleInWallTrack>();
        [Tooltip("Стадии внутри раунда: стадия = стена")]
        [SerializeField] private MinigameStageState stageState;

        private readonly List<PairAssignment.Pair> pairs = new List<PairAssignment.Pair>(4);
        private readonly List<HoleInWallTrack> playingTracks = new List<HoleInWallTrack>(4);
        private readonly Dictionary<int, HoleInWallTrack> trackByPlayer = new Dictionary<int, HoleInWallTrack>(8);
        private readonly WallPatternGenerator generator = new WallPatternGenerator();
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
        /// Точка привязки арта: эффекты подфазы 4.4 и звук 4.5. Своего
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
        /// Точка привязки арта: слот <c>impact_warning</c> подфазы 4.5.
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
        }

        protected override void OnDisable()
        {
            ResultsReported -= LogResults;

            if (stageState != null)
            {
                stageState.StageStarted -= HandleStageStarted;
                stageState.StageElapsed -= HandleStageElapsed;
            }

            base.OnDisable();
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
                track.Wall.Configure(config);
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

            pose.Configure(config, this, playerId);

            avatar.TryGetComponent(out StuckDetector stuck);
            avatar.TryGetComponent(out PlayerRespawner respawner);
            avatar.TryGetComponent(out PlayerInputReader input);

            return new HoleInWallTrack.Member(playerId, avatar, pose, stuck, respawner, input);
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
                member.Avatar.FacingOverride = -SweepingWall.TravelDirection;
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
                generator.Generate(config, TrackSeed(track), track.Solo, track.Patterns);
            }
        }

        /// <summary>
        /// Свой сид на дорожку: соседи решают разные задачи, и половина
        /// удовольствия в том, что видно, как позорятся рядом.
        /// </summary>
        private int TrackSeed(HoleInWallTrack track) => roundSeed + track.Index * TrackSeedStride;

        // ========== ТЕЧЕНИЕ РАУНДА ==========

        protected override void OnRoundStarted()
        {
            if (config == null || !HasAuthority)
            {
                // Момент начала объявит сервер: от него считается всё
                // расписание, и придумывать свой клиенту нельзя.
                return;
            }

            roundStartTime = NetworkClock.Now;
            roundStartKnown = true;
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
                EndMinigame();
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
        }

        private void TickTrack(HoleInWallTrack track, float elapsed, double now)
        {
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

            // 🔴 Вердикт — только сервер и только один раз. Момент проверки —
            // передняя грань стены на линии проверки; формула та же, по которой
            // стена и едет, разойтись им негде. Клиент исход не считает
            // и узнаёт его оповещением (спека 10.1).
            if (HasAuthority && track.Active && track.WallLaunched && !track.WallResolved &&
                wall.FrontZ <= config.CheckLineZ)
            {
                ResolveWall(track);
            }

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
        private void ResolveWall(HoleInWallTrack track)
        {
            track.WallResolved = true;

            bool passed = TrackFits(track);
            if (passed)
            {
                track.AwardWall();
            }
            else
            {
                SweepTrack(track);
            }

            // Счёт — состояние, и уезжает реплицированным списком. Сам вердикт —
            // событие, и уезжает оповещением: правдой он уже стал здесь.
            network?.PublishTracks(playingTracks);
            network?.AnnounceWallResolved(track.Index, currentWall, passed);

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
        public void ApplyNetworkWallResolved(int trackIndex, int wallIndex, bool passed)
        {
            if (HasAuthority || wallIndex != currentWall)
            {
                return;
            }

            HoleInWallTrack track = TrackByIndex(trackIndex);
            if (track == null)
            {
                return;
            }

            track.WallResolved = true;

            // Вторая половина той же точки: у сервера вердикт объявляется
            // подсчётом, у клиента — приёмом. Дальше подписчик один и тот же,
            // и различать эти две половины ему не нужно.
            WallResolved?.Invoke(track, passed);
        }

        /// <summary>
        /// Пролезла ли дорожка в свою стену.
        ///
        /// У пары — взаимно однозначное сопоставление двоих с двумя вырезами:
        /// хватает любого из двух вариантов. Отсюда же и запрет обоим встать
        /// в один вырез: биекции не получится.
        /// </summary>
        private bool TrackFits(HoleInWallTrack track)
        {
            IReadOnlyList<HoleInWallTrack.Member> members = track.Members;

            if (members.Count == 1)
            {
                // Одиночке годится любой вырез. Обычно он один — но пара,
                // потерявшая напарника посреди подъезда, доигрывает эту стену
                // прежним рисунком на двоих, и лезть оставшемуся есть куда:
                // менять вырез в момент подъезда нечестно (спека 10.2).
                for (int cutout = 0; cutout < track.Wall.CutoutCount; cutout++)
                {
                    if (MemberFits(track, members[0], cutout))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (members.Count < 2 || track.Wall.CutoutCount < 2)
            {
                return false;
            }

            bool straight = MemberFits(track, members[0], 0) && MemberFits(track, members[1], 1);
            bool crossed = MemberFits(track, members[0], 1) && MemberFits(track, members[1], 0);
            return straight || crossed;
        }

        /// <summary>
        /// Три условия спеки 5.3 разом: поза, горизонталь, опора.
        ///
        /// Габариты капсулы не участвуют вовсе — сравниваются номер позы и одно
        /// число по горизонтали. Поэтому толстый персонаж и тонкий проходят
        /// абсолютно одинаково, и подгонять их замороженные капсулы не нужно.
        /// </summary>
        private bool MemberFits(HoleInWallTrack track, HoleInWallTrack.Member member, int cutoutIndex)
        {
            if (member.Avatar == null || member.Pose == null)
            {
                return false;
            }

            if (!track.Wall.TryGetCutout(cutoutIndex, out HoleInWallPose pose, out float offset))
            {
                return false;
            }

            if (member.Pose.CurrentPose != pose)
            {
                return false;
            }

            // Позиция берётся у мотора, а не у трансформа: тот отстаёт на кадр
            // после телепорта, и вернувшийся из воды на этом кадре считался бы
            // всё ещё в воде.
            Vector3 position = member.Avatar.Position;

            float cutoutX = track.transform.position.x + offset;
            if (Mathf.Abs(position.x - cutoutX) > config.HitTolerance)
            {
                return false;
            }

            // В прыжке — провал. И в воде тоже: не успел вылезти к следующей
            // стене — теряешь и её, ровно тот каскад, который задуман.
            return Mathf.Abs(position.y - config.PlatformSurfaceY) <= config.GroundedTolerance;
        }

        /// <summary>
        /// Стена сметает дорожку. Провал парный: ошибся один — летят оба,
        /// они связаны тросом.
        /// </summary>
        private void SweepTrack(HoleInWallTrack track)
        {
            Vector3 impulse = (SweepingWall.TravelDirection + Vector3.up * config.SweepUpward).normalized
                              * config.SweepImpulse;

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
                member.Avatar.ApplyWorldImpulse(impulse, KnockdownType.FlyBack);
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

                if (!member.Returning && member.Avatar.Position.y < config.WaterSurfaceY)
                {
                    // Сам сошёл с платформы: полёта не было, только барахтанье.
                    ScheduleReturn(member, config.SplashSeconds, config.MinSplashSeconds);
                }

                if (member.Returning && now >= member.ReturnAt)
                {
                    ReturnMember(track, member);
                }
            }
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
        }

        private void ReturnMember(HoleInWallTrack track, HoleInWallTrack.Member member)
        {
            member.Returning = false;

            int slot = track.IndexOfMember(member.PlayerId);
            Transform point = track.SlotOf(slot);
            if (point == null)
            {
                return;
            }

            member.Avatar.RequestTeleport(point.position, point.rotation);
        }

        // ========== КОНЕЦ РАУНДА ==========

        protected override void OnRoundEnded()
        {
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
        /// </summary>
        public void ApplyPose(int playerId, HoleInWallPose pose)
        {
            if (!HasAuthority || !RoundActive)
            {
                return;
            }

            if (pose == HoleInWallPose.None || (int)pose > HoleInWallConfig.PoseCount)
            {
                return;
            }

            PlayerPoseAbility ability = PoseOf(playerId);
            if (ability == null)
            {
                // Не участник этого раунда — позы у него нет и быть не может.
                return;
            }

            ability.SetPose(pose);
            network?.PublishPose(playerId, pose);
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
            generator.Generate(config, TrackSeed(track), true, soloPatterns);

            for (int wall = currentWall + 1; wall < track.Patterns.Count && wall < soloPatterns.Count; wall++)
            {
                track.Patterns[wall] = soloPatterns[wall];
            }

            // Одиночка стоит по центру платформы: и место возврата, и точка
            // респавна переезжают туда же.
            track.ArrangeSlots(config);
            RebindSlots(track);
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
