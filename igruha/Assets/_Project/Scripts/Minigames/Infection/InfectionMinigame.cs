using System;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Arena;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.Traps;
using Igruha.Core.UI;

namespace Igruha.Minigames.Infection
{
    /// <summary>
    /// Правила «Заражения» поверх шаблона: разбегание, назначение Нулевого
    /// пациента, зачёт касаний, снежный ком и расстановка мест.
    ///
    /// Бег, толчки, падения, респавн, таймер, обучалка и HUD берутся готовыми
    /// из <c>Igruha.Core</c>; карусель, скат горки, качели, песок и прозрачные
    /// трубы — тоже Core, потому что нужны не только этой игре.
    ///
    /// <b>Всё, что решает исход, идёт под <see cref="MinigameControllerBase.HasAuthority"/>:</b>
    /// выбор Нулевого, зачёт касания, конец раунда, очки. Состояние игроков
    /// живёт в <see cref="InfectionState"/> на аватарах, а не в полях контроллера:
    /// в фазе 3 эти записи переедут в <c>NetworkList</c>, а вызовы — за
    /// <c>IsServer</c>, и больше не изменится ничего.
    /// </summary>
    public sealed class InfectionMinigame : MinigameControllerBase
    {
        /// <summary>Своя очередь Нулевого, независимая от ролей других игр.</summary>
        private const string PatientZeroRoleKey = "Infection.PatientZero";

        private const string GroundLayerName = "Ground";
        private const string CoverLayerName = "Cover";

        [Header("Правила")]
        [SerializeField] private InfectionConfig config;

        [Header("Арена")]
        [Tooltip("Центр арены: от него считается разброс болванок и сюда смотрит камера результатов")]
        [SerializeField] private Transform arenaCenter;
        [Tooltip("Радиус, за который болванкам бегать незачем — внутри забора")]
        [SerializeField] private float arenaRadius = 22f;
        [Tooltip("Карусель: скорость вращения задаётся из конфига, а не рукой в сцене")]
        [SerializeField] private RotatingPlatform carousel;
        [Tooltip("Сиденья качелей: период из конфига, фазы разводятся автоматически")]
        [SerializeField] private PendulumSwing[] swings = Array.Empty<PendulumSwing>();
        [Tooltip("Песочница: множитель скорости из конфига")]
        [SerializeField] private SpeedZone sandbox;

        [Header("UI")]
        [Tooltip("Плашка диктора. Пусто — реплик не будет, на правила это не влияет")]
        [SerializeField] private AnnouncerBanner announcer;

        [Header("Дебаг (тест в одиночку)")]
        [Tooltip("Гонять болванок: заражённые ловят, чистые убегают. Выключить — встанут столбами")]
        [SerializeField] private bool driveDummies = true;
        [Tooltip("Как часто болванки пересчитывают цель, с")]
        [SerializeField] private float dummyRetargetSeconds = 0.4f;

        private readonly List<InfectionState> states = new List<InfectionState>(8);
        private readonly List<DebugPlayerBot> bots = new List<DebugPlayerBot>(8);
        private readonly InfectionContactResolver resolver = new InfectionContactResolver();
        private readonly ScoreRanking ranking = new ScoreRanking();
        private readonly SpecialRoleHistory localRoles = new SpecialRoleHistory();

        private float scatterRemaining;
        private bool patientZeroAssigned;
        private int patientZeroId = SpecialRoleHistory.NoPlayer;
        private int shownCleanCount = -1;
        private bool firstInfectionAnnounced;
        private bool halfAnnounced;
        private bool lastCleanAnnounced;
        private float dummyTimer;
        private bool swingsSubscribed;

        /// <summary>Центр арены в мире. Болванки и реплики берут его отсюда, а не ищут заново.</summary>
        private Vector3 Center => arenaCenter != null ? arenaCenter.position : Vector3.zero;

        protected override void OnPlayersReady()
        {
            ApplyConfigToArena();
            BuildStates();
        }

        protected override void OnRoundStarted()
        {
            if (config == null)
            {
                Debug.LogError($"{name}: не назначен InfectionConfig — раунд не по чему считать", this);
                return;
            }

            announcer?.Clear();
            firstInfectionAnnounced = false;
            halfAnnounced = false;
            lastCleanAnnounced = false;
            patientZeroAssigned = false;
            patientZeroId = SpecialRoleHistory.NoPlayer;
            shownCleanCount = -1;
            dummyTimer = 0f;
            scatterRemaining = config.ScatterSeconds;

            for (int i = 0; i < states.Count; i++)
            {
                states[i].SetClean();
            }

            SubscribeSwings(true);

            // Разбегание идёт до раунда по очкам: таймер держим выключенным,
            // а на экране — стартовый отсчёт. Иначе шкала успела бы убежать
            // на три секунды раньше, чем появился тот, от кого бегут.
            if (HasAuthority)
            {
                Timer?.StopTimer();
            }

            announcer?.Announce("Разбегайтесь. Через пару секунд кому-то станет нехорошо.", 2.6f);
        }

        protected override void OnRoundEnded()
        {
            SubscribeSwings(false);
            StopDummies();

            for (int i = 0; i < states.Count; i++)
            {
                states[i].ResetRole();
            }

            Hud?.HideStatus();
            Hud?.HideCountdown();

            // «С кого всё началось» — дешёвая драма из спеки. Говорится в конце,
            // когда это уже ничего не решает, и потому безопасно.
            if (patientZeroId != SpecialRoleHistory.NoPlayer)
            {
                announcer?.Announce($"С {NameOf(patientZeroId)} всё началось.", 4f, 1);
            }
        }

        private void Update()
        {
            if (!RoundActive || config == null)
            {
                return;
            }

            if (HasAuthority)
            {
                if (patientZeroAssigned)
                {
                    TickStates(Time.deltaTime);
                }
                else
                {
                    TickScatter(Time.deltaTime);
                }

                DriveDummies();
            }

            UpdateCleanCounter();
        }

        private void FixedUpdate()
        {
            if (!RoundActive || !HasAuthority || !patientZeroAssigned || config == null)
            {
                return;
            }

            IReadOnlyList<InfectionContactResolver.Contact> contacts =
                resolver.Resolve(states, config.TouchRadius);

            for (int i = 0; i < contacts.Count; i++)
            {
                ApplyInfection(contacts[i].Carrier, contacts[i].Victim);
            }

            if (contacts.Count > 0)
            {
                CheckRoundOver();
            }
        }

        // ========== ФАЗЫ РАУНДА ==========

        private void TickScatter(float deltaTime)
        {
            scatterRemaining -= deltaTime;
            Hud?.ShowCountdown(Mathf.Max(0f, scatterRemaining));

            if (scatterRemaining > 0f)
            {
                return;
            }

            Hud?.HideCountdown();
            AssignPatientZero();
        }

        private void TickStates(float deltaTime)
        {
            for (int i = 0; i < states.Count; i++)
            {
                states[i].Tick(deltaTime);
            }
        }

        /// <summary>
        /// Назначить Нулевого. Рандом серверный: очередь роли живёт в сессии,
        /// чтобы один и тот же человек не становился Нулевым две катки подряд.
        /// Сцена, открытая напрямую из редактора, сессии не имеет — тогда
        /// очередь ведёт свой экземпляр истории.
        /// </summary>
        private void AssignPatientZero()
        {
            InfectionState chosen = PickPatientZero();
            if (chosen == null)
            {
                Debug.LogWarning($"{name}: некого назначить Нулевым — в раунде нет игроков", this);
                EndMinigame();
                return;
            }

            patientZeroId = chosen.PlayerId;
            patientZeroAssigned = true;
            chosen.MakePatientZero();

            // Отсчёт очков идёт с этого мига, значит и шкала раунда тоже.
            if (Definition != null)
            {
                Timer?.StartTimer(Definition.RoundDuration);
            }

            announcer?.Announce($"Кажется, у {NameOf(patientZeroId)} что-то зелёное на руках. Я бы отошёл.", 3.5f, 2);
        }

        private InfectionState PickPatientZero()
        {
            ISessionScoreboard board = SessionScoreboard.Current;
            int picked = board != null
                ? board.PickSpecialRole(PatientZeroRoleKey)
                : localRoles.Pick(PatientZeroRoleKey, Players);

            InfectionState state = FindState(picked);
            if (state != null && state.CanBeInfected)
            {
                return state;
            }

            // Выбранного уже нет в раунде (вышел на обучалке) — берём любого чистого.
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].CanBeInfected)
                {
                    return states[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Зачесть заражение. Единственная точка входа: в фазе 3 она уйдёт за
        /// <c>IsServer</c>, а клиентам поедет уже готовое состояние.
        /// </summary>
        private void ApplyInfection(InfectionState carrier, InfectionState victim)
        {
            if (carrier == null || victim == null || !victim.CanBeInfected)
            {
                return;
            }

            victim.BeginInfecting();
            carrier.CountInfection();
            AnnounceProgress();
        }

        private void AnnounceProgress()
        {
            if (announcer == null)
            {
                return;
            }

            int clean = CountClean();
            int total = states.Count;

            if (!firstInfectionAnnounced)
            {
                firstInfectionAnnounced = true;
                announcer.Announce("Один готов. Дальше — математика.", 3f);
            }

            if (!halfAnnounced && total >= 4 && clean * 2 <= total)
            {
                halfAnnounced = true;
                announcer.Announce("Напоминаю: это была дружеская игра.", 3f, 1);
            }

            if (!lastCleanAnnounced && clean == 1 && total >= 3)
            {
                lastCleanAnnounced = true;
                announcer.Announce($"{total - 1} против одного. Честно как никогда.", 3.5f, 2);
            }
        }

        private void CheckRoundOver()
        {
            if (states.Count == 0)
            {
                EndMinigame();
                return;
            }

            if (CountClean() > 0)
            {
                return;
            }

            announcer?.Announce("Все зелёные. Расходимся, домой зовут.", 3.5f, 3);
            EndMinigame();
        }

        // ========== СОСТАВ ==========

        private void BuildStates()
        {
            states.Clear();
            bots.Clear();

            for (int i = 0; i < Players.Count; i++)
            {
                SessionPlayer player = Players[i];
                PlayerController avatar = player.Avatar;
                if (avatar == null)
                {
                    continue;
                }

                if (!avatar.TryGetComponent(out InfectionState state))
                {
                    state = avatar.gameObject.AddComponent<InfectionState>();
                }

                if (!avatar.TryGetComponent(out InfectionPaintView paint))
                {
                    paint = avatar.gameObject.AddComponent<InfectionPaintView>();
                }

                paint.Bind(avatar.gameObject, config != null ? config.BlinkPeriod : 0.25f);
                state.Bind(player.Id, avatar, config, paint);

                states.Add(state);
                bots.Add(EnsureDummyBot(avatar));
            }
        }

        /// <summary>
        /// Болванка для теста в одиночку. Живому игроку и сетевой копии бот
        /// не ставится: там ввод идёт от человека, и второй источник — гонка.
        /// </summary>
        private DebugPlayerBot EnsureDummyBot(PlayerController avatar)
        {
            if (!driveDummies || SessionScoreboard.IsNetworked)
            {
                return null;
            }

            if (!avatar.TryGetComponent(out PlayerInputReader reader) || reader.LocallyControlled)
            {
                return null;
            }

            if (!avatar.TryGetComponent(out DebugPlayerBot bot))
            {
                bot = avatar.gameObject.AddComponent<DebugPlayerBot>();
            }

            bot.Configure(LayerMask.GetMask(GroundLayerName, CoverLayerName));
            return bot;
        }

        /// <summary>
        /// Болванки играют в ту же игру: зелёные бегут к ближайшему чистому,
        /// чистые — прочь от ближайшего зелёного. Без этого снежный ком в
        /// одиночку не проверить: восемь столбов заражаются за четыре секунды.
        /// </summary>
        private void DriveDummies()
        {
            if (!driveDummies || bots.Count == 0)
            {
                return;
            }

            dummyTimer -= Time.deltaTime;
            if (dummyTimer > 0f)
            {
                return;
            }

            dummyTimer = Mathf.Max(0.1f, dummyRetargetSeconds);

            for (int i = 0; i < bots.Count; i++)
            {
                DebugPlayerBot bot = bots[i];
                InfectionState state = states[i];
                if (bot == null || state == null || state.Avatar == null)
                {
                    continue;
                }

                if (state.Phase == InfectionPhase.Infecting)
                {
                    bot.Stop();
                    continue;
                }

                bool hunting = state.Phase == InfectionPhase.Infected;
                InfectionState target = FindNearest(state, hunting);
                if (target == null)
                {
                    bot.SetTarget(Center);
                    continue;
                }

                Vector3 point = hunting
                    ? target.Position
                    : FleePoint(state.Position, target.Position);

                bot.SetTarget(point, false);
            }
        }

        /// <summary>Куда бежать от преследователя, не упираясь в забор.</summary>
        private Vector3 FleePoint(Vector3 from, Vector3 threat)
        {
            Vector3 away = from - threat;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
            {
                away = Vector3.forward;
            }

            Vector3 point = from + away.normalized * 8f;
            Vector3 offset = point - Center;
            offset.y = 0f;

            if (offset.magnitude > arenaRadius)
            {
                // У забора бежать «от» некуда — уходим вдоль него по кругу.
                offset = Quaternion.Euler(0f, 60f, 0f) * offset.normalized * arenaRadius;
            }

            Vector3 center = Center;
            return new Vector3(center.x + offset.x, from.y, center.z + offset.z);
        }

        private InfectionState FindNearest(InfectionState from, bool wantClean)
        {
            InfectionState best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < states.Count; i++)
            {
                InfectionState other = states[i];
                if (other == null || other == from || other.Avatar == null)
                {
                    continue;
                }

                bool isClean = other.Phase == InfectionPhase.Clean;
                if (isClean != wantClean)
                {
                    continue;
                }

                float distance = (other.Position - from.Position).sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                best = other;
                bestDistance = distance;
            }

            return best;
        }

        private void StopDummies()
        {
            for (int i = 0; i < bots.Count; i++)
            {
                bots[i]?.Stop();
            }
        }

        // ========== ВЫХОД ИГРОКА ==========

        protected override void OnPlayerLeftRound(int playerId)
        {
            InfectionState state = FindState(playerId);
            if (state == null)
            {
                return;
            }

            bool wasClean = state.Phase == InfectionPhase.Clean;
            state.MarkLeftRound();
            RemovePlayer(playerId);

            // Нулевой сбежал, никого не успев тронуть — раунд без заражающего
            // не игра. Назначаем нового, таймер при этом не трогаем.
            if (playerId == patientZeroId && CountInfected() <= 1 && CountClean() > 0)
            {
                patientZeroId = SpecialRoleHistory.NoPlayer;
                InfectionState replacement = PickPatientZero();
                if (replacement != null)
                {
                    patientZeroId = replacement.PlayerId;
                    replacement.MakePatientZero();
                    announcer?.Announce($"Зараза сменила хозяина: теперь она у {NameOf(patientZeroId)}.", 3.5f, 2);
                }
            }

            if (wasClean)
            {
                CheckRoundOver();
            }
        }

        // ========== ОЧКИ ==========

        /// <summary>
        /// Очки = секунды чистой жизни + бонус за каждое личное заражение
        /// (+ компенсация Нулевому). Считаем в десятых долях секунды: при
        /// целых секундах двое, разошедшиеся на полсекунды, делили бы место.
        /// </summary>
        protected override void CollectResults(MinigameResults results)
        {
            ranking.Clear();

            float bonus = config != null ? config.InfectionBonusSeconds : 6f;
            float compensation = config != null ? config.PatientZeroCompensationSeconds : 10f;

            for (int i = 0; i < states.Count; i++)
            {
                InfectionState state = states[i];
                float score = state.CleanSeconds + state.PersonalInfections * bonus;
                if (state.IsPatientZero)
                {
                    score += compensation;
                }

                ranking.Add(state.PlayerId, Mathf.RoundToInt(score * 10f));
            }

            ranking.Build(results);
        }

        // ========== СЛУЖЕБНОЕ ==========

        private void ApplyConfigToArena()
        {
            if (config == null)
            {
                return;
            }

            if (carousel != null)
            {
                carousel.DegreesPerSecond = config.CarouselDegreesPerSecond;
            }

            if (sandbox != null)
            {
                sandbox.SpeedMultiplier = config.SandSpeedMultiplier;
            }

            for (int i = 0; i < swings.Length; i++)
            {
                if (swings[i] == null)
                {
                    continue;
                }

                swings[i].Period = config.SwingPeriod;

                // Сиденья одной рамы разводятся по фазе: качающиеся в такт
                // читаются как одна балка и перекрывают проход разом.
                swings[i].PhaseOffset = config.SwingPeriod * (i * 0.37f);
            }
        }

        private void SubscribeSwings(bool subscribe)
        {
            if (subscribe == swingsSubscribed)
            {
                return;
            }

            swingsSubscribed = subscribe;

            for (int i = 0; i < swings.Length; i++)
            {
                if (swings[i] == null)
                {
                    continue;
                }

                if (subscribe)
                {
                    swings[i].Hit += HandleSwingHit;
                }
                else
                {
                    swings[i].Hit -= HandleSwingHit;
                }
            }
        }

        private void HandleSwingHit(PlayerController player)
        {
            announcer?.Announce("Качели сегодня без команды, но с результатом.", 2.5f);
        }

        private void UpdateCleanCounter()
        {
            int clean = CountClean();
            if (clean == shownCleanCount)
            {
                return;
            }

            shownCleanCount = clean;
            Hud?.ShowStatus($"Чистых: {clean} из {states.Count}");
        }

        private int CountClean()
        {
            int count = 0;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].Phase == InfectionPhase.Clean)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountInfected()
        {
            int count = 0;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].Phase != InfectionPhase.Clean)
                {
                    count++;
                }
            }

            return count;
        }

        private InfectionState FindState(int playerId)
        {
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].PlayerId == playerId)
                {
                    return states[i];
                }
            }

            return null;
        }

        private string NameOf(int playerId)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].Id == playerId)
                {
                    return Players[i].DisplayName;
                }
            }

            return "кто-то";
        }
    }
}
