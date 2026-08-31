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
    /// <b>Сеть.</b> Важное состояние меняют четыре метода —
    /// <see cref="ResolveWall"/>, <see cref="SweepTrack"/>,
    /// <see cref="ReturnMember"/> и <see cref="CollectResults"/>, — и все они
    /// зовутся только под <see cref="MinigameControllerBase.HasAuthority"/>.
    /// Расписание и рисунок стен считает сервер один раз на раунд; движение
    /// стены не шлётся вовсе — оно чистая функция от общих часов
    /// (<see cref="SweepingWall"/>).
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

        /// <summary>Частота сигнала перед ударом, Гц. Заглушка каркаса — настоящий звук в фазе 4.</summary>
        private const int WarningToneHz = 880;

        /// <summary>Длительность сигнала перед ударом, с.</summary>
        private const float WarningToneSeconds = 0.18f;

        [Header("Дырка в стене")]
        [SerializeField] private HoleInWallConfig config;
        [Tooltip("Все дорожки арены. Заполняется построителем арены")]
        [SerializeField] private HoleInWallTrack[] tracks = Array.Empty<HoleInWallTrack>();
        [Tooltip("Стадии внутри раунда: стадия = стена")]
        [SerializeField] private MinigameStageState stageState;

        private readonly List<PairAssignment.Pair> pairs = new List<PairAssignment.Pair>(4);
        private readonly List<HoleInWallTrack> playingTracks = new List<HoleInWallTrack>(4);
        private readonly Dictionary<int, HoleInWallTrack> trackByPlayer = new Dictionary<int, HoleInWallTrack>(8);
        private readonly List<PlayerTether> tethers = new List<PlayerTether>(4);
        private readonly WallPatternGenerator generator = new WallPatternGenerator();
        private readonly ScoreRanking ranking = new ScoreRanking();

        private AudioSource warningSource;
        private AudioClip warningClip;
        private double roundStartTime;
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
                if (config == null || currentWall < 0 || !RoundActive)
                {
                    return 0f;
                }

                float elapsed = (float)(NetworkClock.Now - roundStartTime);
                return Mathf.Max(0f, stageHitTime - elapsed);
            }
        }

        /// <summary>Числа игры. Интерфейсу и погонщику болванок — чтобы не заводить вторую ссылку на тот же ассет.</summary>
        public HoleInWallConfig Config => config;

        /// <summary>Дорожки, на которых идёт игра. Погонщику болванок и интерфейсу.</summary>
        public IReadOnlyList<HoleInWallTrack> PlayingTracks => playingTracks;

        /// <summary>Дорожка этого участника. Пусто — участника в раунде нет.</summary>
        public HoleInWallTrack TrackOf(int playerId) =>
            trackByPlayer.TryGetValue(playerId, out HoleInWallTrack track) ? track : null;

        protected override void Awake()
        {
            base.Awake();
            BuildWarningSound();
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            if (stageState != null)
            {
                stageState.StageStarted += HandleStageStarted;
                stageState.StageElapsed += HandleStageElapsed;
            }
        }

        protected override void OnDisable()
        {
            if (stageState != null)
            {
                stageState.StageStarted -= HandleStageStarted;
                stageState.StageElapsed -= HandleStageElapsed;
            }

            base.OnDisable();
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

            // Состав делит сервер один раз. У клиента в фазе 3 он приедет
            // готовым — пересчитывать его там будет нечем и незачем.
            int seed = HasAuthority
                ? UnityEngine.Random.Range(int.MinValue, int.MaxValue)
                : 0;

            PairAssignment.Assign(Players, seed, pairs);
            AssignTracks();
            GeneratePatterns(seed);

            Debug.Log($"🧱 «Дырка в стене»: {Players.Count} игроков, {playingTracks.Count} дорожек, " +
                      $"{config.WallCount} стен, раунд {config.RoundLength:F1} с");
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

        private HoleInWallTrack.Member BuildMember(int playerId)
        {
            PlayerController avatar = FindAvatar(playerId);
            if (avatar == null)
            {
                return null;
            }

            // Способность вешается на аватар, а не лежит в префабе: префабы
            // персонажей заморожены (igruha/CLAUDE.md, раздел 🔒 0).
            if (!avatar.TryGetComponent(out PlayerPoseAbility pose))
            {
                pose = avatar.gameObject.AddComponent<PlayerPoseAbility>();
            }

            pose.Configure(config);

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
            tethers.Add(tether);

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

        private void GeneratePatterns(int seed)
        {
            if (!HasAuthority)
            {
                return;
            }

            for (int i = 0; i < playingTracks.Count; i++)
            {
                HoleInWallTrack track = playingTracks[i];

                // Свой сид на дорожку: соседи решают разные задачи, и половина
                // удовольствия в том, что видно, как позорятся рядом.
                generator.Generate(config, seed + track.Index * 7919, track.Solo, track.Patterns);
            }
        }

        // ========== ТЕЧЕНИЕ РАУНДА ==========

        protected override void OnRoundStarted()
        {
            if (config == null || !HasAuthority)
            {
                return;
            }

            roundStartTime = NetworkClock.Now;
            stageState?.BeginSubround(1, FirstStage, config.StageDuration(0));
        }

        private void HandleStageStarted(byte stage)
        {
            currentWall = stage - FirstStage;
            stageHitTime = config != null ? config.HitTime(currentWall) : 0f;
            wallWarned = false;

            for (int i = 0; i < playingTracks.Count; i++)
            {
                playingTracks[i].BeginWall();
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

        private void FixedUpdate()
        {
            if (!RoundActive || !HasAuthority || config == null || currentWall < 0)
            {
                return;
            }

            double now = NetworkClock.Now;
            float elapsed = (float)(now - roundStartTime);

            // Сигнал звучит один раз на стену, а не на дорожку: момент удара
            // у всех дорожек общий, и четыре источника дали бы четырёхкратную
            // громкость вместо подсказки.
            if (!wallWarned && elapsed >= stageHitTime - config.WarningLead)
            {
                wallWarned = true;
                PlayWarning();
            }

            for (int i = 0; i < playingTracks.Count; i++)
            {
                TickTrack(playingTracks[i], elapsed, now);
            }
        }

        private void TickTrack(HoleInWallTrack track, float elapsed, double now)
        {
            SweepingWall wall = track.Wall;

            // Стена одиночки стартует позже: подъезд у неё короче на те же
            // 25 %, на которые выше скорость, а момент удара общий.
            if (!track.WallLaunched &&
                elapsed >= stageHitTime - config.ApproachSeconds(currentWall, track.Solo))
            {
                LaunchWall(track);
            }

            // Момент проверки — передняя грань стены на линии проверки.
            // Формула та же, по которой стена и едет: разойтись им негде.
            if (track.WallLaunched && !track.WallResolved && wall.FrontZ <= config.CheckLineZ)
            {
                ResolveWall(track);
            }

            if (wall.Running && wall.Finished)
            {
                wall.Retire();
            }

            TickReturns(track, now);
        }

        private void LaunchWall(HoleInWallTrack track)
        {
            track.WallLaunched = true;

            if (currentWall >= track.Patterns.Count)
            {
                Debug.LogError($"{name}: у дорожки {track.Index} нет рисунка стены {currentWall + 1}", this);
                return;
            }

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

            if (TrackFits(track))
            {
                track.AwardWall();
                return;
            }

            SweepTrack(track);
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
                return MemberFits(track, members[0], 0);
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
                member.Avatar.ApplyImpulse(impulse, KnockdownType.FlyBack);
                ScheduleReturn(member, config.SweptReturnSeconds);
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
                    ScheduleReturn(member, config.SplashSeconds);
                }

                if (member.Returning && now >= member.ReturnAt)
                {
                    ReturnMember(track, member);
                }
            }
        }

        private static void ScheduleReturn(HoleInWallTrack.Member member, float delay)
        {
            if (member.Returning)
            {
                return;
            }

            member.Returning = true;
            member.ReturnAt = NetworkClock.Now + delay;
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

            // Способность сама снимает и позу, и запрет Ctrl-приседа в OnDisable.
            if (member.Pose != null)
            {
                Destroy(member.Pose);
            }
        }

        private void ReleaseTethers()
        {
            for (int i = 0; i < tethers.Count; i++)
            {
                if (tethers[i] == null)
                {
                    continue;
                }

                tethers[i].Release();
                Destroy(tethers[i].gameObject);
            }

            tethers.Clear();
        }

        private void ClearRound()
        {
            playingTracks.Clear();
            trackByPlayer.Clear();
            pairs.Clear();
            currentWall = -1;

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
                HoleInWallTrack track = TrackOf(playerId);
                ranking.Add(playerId, track != null ? track.Score : 0);
            }

            ranking.Build(results);
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

        // ========== ЗВУК ==========

        /// <summary>
        /// Сигнал за секунду до удара — единственная подсказка в игре, и она
        /// нужна по механике: на последней стене подъезд короче, чем разворот
        /// камеры (спека 8.8).
        ///
        /// На каркасе это синтезированный тон: настоящий звук приезжает
        /// в фазе 4, и менять для этого код не придётся — достаточно положить
        /// клип в <see cref="warningSource"/>.
        /// </summary>
        private void BuildWarningSound()
        {
            warningSource = gameObject.AddComponent<AudioSource>();
            warningSource.playOnAwake = false;
            warningSource.spatialBlend = 0f;
            warningSource.volume = 0.5f;

            int samples = Mathf.RoundToInt(AudioSettings.outputSampleRate * WarningToneSeconds);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)AudioSettings.outputSampleRate;
                // Затухание к концу: щелчок обрыва слышен сильнее самого тона.
                float envelope = 1f - i / (float)samples;
                data[i] = Mathf.Sin(2f * Mathf.PI * WarningToneHz * t) * envelope;
            }

            warningClip = AudioClip.Create("HoleInWallWarning", samples, 1, AudioSettings.outputSampleRate, false);
            warningClip.SetData(data, 0);
        }

        private void PlayWarning()
        {
            if (warningSource != null && warningClip != null)
            {
                warningSource.PlayOneShot(warningClip);
            }
        }
    }
}
