using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Items;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.Spawning;
using Igruha.Core.UI;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Правила «Переноски предмета»: кто в какой команде, когда конец, кто
    /// выиграл и какое у каждого место.
    ///
    /// Ролей в игре нет — обе команды делают одно и то же, — поэтому нет и
    /// дыры «отвалился единственный носитель роли». Худший случай, команда из
    /// одного человека, легален и играется.
    ///
    /// Досрочного конца нет намеренно: даже полный бак раунд не останавливает.
    /// Излишек некуда девать, а соперник ещё может догнать.
    /// </summary>
    public sealed class CarryItemMinigame : MinigameControllerBase
    {
        /// <summary>Всё, что нужно одной команде на арене. Парой полей это разъехалось бы при первой же правке.</summary>
        [System.Serializable]
        private sealed class TeamRig
        {
            [Tooltip("Штабель тары в стартовой зоне")]
            public BottleStack Stack;
            [Tooltip("Бак команды — он же её счёт")]
            public WaterTank Tank;
            [Tooltip("Роль точек спавна этой команды")]
            public SpawnRole SpawnRole = SpawnRole.TeamA;
            [Tooltip("Маршрут болванок соло-теста: доска, горлышко, доска. Живому игроку не нужен")]
            public CarryItemBotRoute Route;
        }

        /// <summary>Участник раунда: место в составе и команда. В фазе 3 уедет в NetworkList.</summary>
        private struct Entry
        {
            public int PlayerId;
            public TeamSide Team;
            public PlayerController Avatar;
            public Transform OriginalRespawn;
        }

        [Header("Переноска предмета")]
        [SerializeField] private CarryItemConfig config;
        [SerializeField] private SpawnPointSet spawnPoints;
        [SerializeField] private BottleRamDetector ramDetector;
        [SerializeField] private TeamProgressBar progressBar;

        [Header("Команды")]
        [SerializeField] private TeamRig teamA = new TeamRig { SpawnRole = SpawnRole.TeamA };
        [SerializeField] private TeamRig teamB = new TeamRig { SpawnRole = SpawnRole.TeamB };

        [Header("Арена")]
        [Tooltip("Ниже этой отметки бутыль считается улетевшей в пропасть и теряется целиком")]
        [SerializeField] private float voidLevel = -5f;
        [Tooltip("Слои, которые болванки соло-теста считают препятствием")]
        [SerializeField] private LayerMask botObstacles;

        private readonly List<Entry> entries = new List<Entry>(8);
        private readonly List<TeamRanking.Entry> rankingBuffer = new List<TeamRanking.Entry>(8);
        private readonly List<CarryItemDebugBot> bots = new List<CarryItemDebugBot>(8);

        /// <summary>
        /// Куда ушла вода за раунд: команда × причина, единиц.
        ///
        /// Нужен приёмке и плейтесту. Без разреза «расплескали» — это одно
        /// число, из которого не видно, что чинить: перекос от рассинхрона,
        /// балка над горлышком или пропорция тарана. Считается по тому же
        /// событию, что и сама потеря, поэтому разойтись со счётом не может.
        /// </summary>
        private readonly int[,] spentByReason =
            new int[3, System.Enum.GetValues(typeof(WaterLossReason)).Length];

        private CarryItemState state;
        private Coroutine countdownRoutine;

        /// <summary>Числа игры. Нужны болванкам и предметам арены.</summary>
        public CarryItemConfig Config => config;

        /// <summary>Счёт раунда одной структурой. В фазе 3 уедет в NetworkVariable целиком.</summary>
        public CarryItemState State => state;

        // ========== СТАРТ ==========

        protected override void OnPlayersReady()
        {
            if (config == null)
            {
                Debug.LogError($"{name}: CarryItemMinigame без CarryItemConfig — числа брать неоткуда", this);
                return;
            }

            state = default;
            System.Array.Clear(spentByReason, 0, spentByReason.Length);

            AssignTeams();
            PlaceTeams();
            ConfigureRigs();
            AttachBots();

            progressBar?.ResetBars(config.TankCapacity);
            progressBar?.SetLocalTeam(TeamOfPlayer(SessionScoreboard.Current?.LocalPlayer?.Id ?? -1));
        }

        /// <summary>
        /// Делит сервер, детерминированно, по порядковому номеру в составе.
        /// Компенсации за неравенство нет: меньшая команда несёт устойчивее,
        /// зато у неё меньше рук на саботаж (спека 2.2).
        /// </summary>
        private void AssignTeams()
        {
            entries.Clear();

            for (int i = 0; i < Players.Count; i++)
            {
                SessionPlayer player = Players[i];
                entries.Add(new Entry
                {
                    PlayerId = player.Id,
                    Team = TeamAssignment.SideFor(i),
                    Avatar = player.Avatar,
                    OriginalRespawn = player.Avatar != null &&
                                      player.Avatar.TryGetComponent(out PlayerRespawner respawner)
                        ? respawner.RespawnPoint
                        : null
                });
            }

            Debug.Log($"🫙 [Переноска] составы: A — {TeamAssignment.TeamASize(Players.Count)}, " +
                      $"B — {TeamAssignment.TeamBSize(Players.Count)} из {Players.Count}");
        }

        /// <summary>
        /// Развести по стартовым зонам. Спавнер раздаёт точки одной роли, а
        /// команд здесь две, поэтому расстановку делают правила игры — и они же
        /// переставляют точку респавна: свалившийся в пропасть обязан вернуться
        /// к своему штабелю, а не к чужому.
        /// </summary>
        private void PlaceTeams()
        {
            if (spawnPoints == null)
            {
                Debug.LogError($"{name}: не назначен SpawnPointSet — разводить команды не по чему", this);
                return;
            }

            int indexA = 0;
            int indexB = 0;
            int sizeA = TeamAssignment.TeamASize(entries.Count);
            int sizeB = TeamAssignment.TeamBSize(entries.Count);

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry.Avatar == null)
                {
                    continue;
                }

                bool isA = entry.Team == TeamSide.A;
                SpawnRole role = isA ? teamA.SpawnRole : teamB.SpawnRole;
                int slot = isA ? indexA++ : indexB++;
                int count = isA ? sizeA : sizeB;

                SpawnPoint point = spawnPoints.GetSpreadPoint(role, slot, count);
                if (point == null)
                {
                    continue;
                }

                entry.Avatar.RequestTeleport(point.transform.position, point.transform.rotation);

                if (entry.Avatar.TryGetComponent(out PlayerRespawner respawner))
                {
                    respawner.SetRespawnPoint(point.transform);
                }
            }
        }

        private void ConfigureRigs()
        {
            int sizeA = TeamAssignment.TeamASize(entries.Count);
            int sizeB = TeamAssignment.TeamBSize(entries.Count);

            SetUpRig(teamA, TeamSide.A, sizeA);
            SetUpRig(teamB, TeamSide.B, sizeB);

            ramDetector?.Configure(config);
        }

        private void SetUpRig(TeamRig rig, TeamSide side, int teamSize)
        {
            if (rig.Tank != null)
            {
                rig.Tank.Configure(config, side);
                rig.Tank.Delivered += (amount, time) => OnDelivered(side, amount, time);
                rig.Tank.BottleFinished += () => OnBottleFinished(side);
            }

            if (rig.Stack == null)
            {
                Debug.LogError($"{name}: у команды {side} нет штабеля — брать тару неоткуда", this);
                return;
            }

            rig.Stack.Configure(config, side, teamSize, voidLevel);
            rig.Stack.TeamFilter = player => TeamOfAvatar(player) == side;
            rig.Stack.BottleTaken += bottle => OnBottleTaken(side, bottle);
        }

        /// <summary>
        /// Новая тара у команды: настроить фильтр своих и пересобрать пару для
        /// детектора тарана. Тару выдают шесть раз за раунд, поэтому связи
        /// пересобираются здесь, а не один раз на старте.
        /// </summary>
        private void OnBottleTaken(TeamSide side, WaterBottle bottle)
        {
            bottle.Carry.SetOwnerFilter(player => TeamOfAvatar(player) == side);
            bottle.WaterSpent += (amount, reason) => spentByReason[(int)side, (int)reason] += amount;

            ramDetector?.SetBottles(teamA.Stack != null ? teamA.Stack.LiveBottle : null,
                teamB.Stack != null ? teamB.Stack.LiveBottle : null);
        }

        /// <summary>Сколько воды команда потеряла по этой причине за раунд, единиц.</summary>
        public int SpentBy(TeamSide side, WaterLossReason reason) => spentByReason[(int)side, (int)reason];

        // ========== РАУНД ==========

        protected override void OnRoundStarted()
        {
            if (countdownRoutine != null)
            {
                StopCoroutine(countdownRoutine);
            }

            countdownRoutine = StartCoroutine(CountdownThenGo());
        }

        /// <summary>
        /// Обратный отсчёт: ввод заморожен целиком, чтобы «управления нет»
        /// значило именно это, а не «двигаться нельзя, а толкать можно».
        /// </summary>
        private IEnumerator CountdownThenGo()
        {
            SetInputSuspended(true);

            float remaining = config.CountdownSeconds;
            while (remaining > 0f)
            {
                Hud?.ShowCountdown(remaining);
                yield return null;
                remaining -= Time.deltaTime;
            }

            Hud?.HideCountdown();
            SetInputSuspended(false);
            countdownRoutine = null;
        }

        private void OnDelivered(TeamSide side, int amount, double time)
        {
            state.Deliver(side, amount, config.TankCapacity, time, false);
            progressBar?.SetValue(side, state.Of(side).Water, config.TankCapacity);
        }

        private void OnBottleFinished(TeamSide side)
        {
            state.Deliver(side, 0, config.TankCapacity, 0d, true);
            Debug.Log($"🫙 [Переноска] команда {side} закрыла ходку №{state.Of(side).Deliveries}, " +
                      $"в баке {state.Of(side).Water} из {config.TankCapacity}");
        }

        // ========== КОНЕЦ ==========

        /// <summary>
        /// Снять с игроков всё, что игра на них вешала: привязку к ручке,
        /// потолок скорости, запрет удара и подбора.
        ///
        /// Правило, стоившее отдельного дня на «Ангелах»: персонаж уезжает в хаб
        /// живым, и незакрытая роль уезжает вместе с ним. Всё это снимает
        /// <c>MultiCarryObject.ReleaseAll</c> — по одному несущему за раз, той же
        /// точкой входа, что и обычное отцепление.
        /// </summary>
        protected override void OnRoundEnded()
        {
            if (countdownRoutine != null)
            {
                StopCoroutine(countdownRoutine);
                countdownRoutine = null;
            }

            Hud?.HideCountdown();

            ReleaseTeam(teamA);
            ReleaseTeam(teamB);

            for (int i = 0; i < bots.Count; i++)
            {
                if (bots[i] != null)
                {
                    bots[i].enabled = false;
                }
            }

            bots.Clear();

            // Точка респавна вела внутрь этой сцены. Не вернув прежнюю, персонаж
            // уедет в хаб с точкой внутри уже выгруженной арены.
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry.Avatar != null && entry.Avatar.TryGetComponent(out PlayerRespawner respawner))
                {
                    respawner.SetRespawnPoint(entry.OriginalRespawn);
                }
            }

            SetInputSuspended(false);

            TeamSide winner = state.Winner();
            Debug.Log($"🫙 [Переноска] итог: A — {state.TeamA.Water} за {state.TeamA.Deliveries} ходок, " +
                      $"B — {state.TeamB.Water} за {state.TeamB.Deliveries}, " +
                      $"победитель — {(winner == TeamSide.None ? "нет" : winner.ToString())}");

            Debug.Log($"🫙 [Переноска] куда ушла вода — A: {LossReport(TeamSide.A)}");
            Debug.Log($"🫙 [Переноска] куда ушла вода — B: {LossReport(TeamSide.B)}");
        }

        /// <summary>Разрез потерь одной строкой. Числа приёмки и плейтеста.</summary>
        private string LossReport(TeamSide side) =>
            $"перекос {SpentBy(side, WaterLossReason.Tilt)}, " +
            $"удары {SpentBy(side, WaterLossReason.Hit)}, " +
            $"падения {SpentBy(side, WaterLossReason.Drop)}, " +
            $"броски {SpentBy(side, WaterLossReason.Throw)}, " +
            $"таран {SpentBy(side, WaterLossReason.RamVictim) + SpentBy(side, WaterLossReason.RamAttacker)}, " +
            $"пропасть {SpentBy(side, WaterLossReason.Void)}, " +
            $"донесено {SpentBy(side, WaterLossReason.Poured)}";

        private void ReleaseTeam(TeamRig rig)
        {
            WaterBottle bottle = rig.Stack != null ? rig.Stack.LiveBottle : null;
            if (bottle != null)
            {
                bottle.Carry.ReleaseAll(CarryReleaseReason.RoundEnded);
            }

            rig.Stack?.ClearLiveBottle();
        }

        /// <summary>
        /// Места половинами: победившая команда делит верхние, проигравшая —
        /// нижние. Счёт 0 : 0 — победителя нет, и все получают последнее место
        /// и ноль очков: отдать всем первое означало бы выдать максимум за
        /// общий провал.
        /// </summary>
        protected override void CollectResults(MinigameResults results)
        {
            rankingBuffer.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                rankingBuffer.Add(new TeamRanking.Entry(entries[i].PlayerId, entries[i].Team));
            }

            TeamRanking.Fill(rankingBuffer, state.Winner(), results);
        }

        // ========== СЛУЖЕБНОЕ ==========

        /// <summary>Команда участника по его номеру в сессии.</summary>
        public TeamSide TeamOfPlayer(int playerId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].PlayerId == playerId)
                {
                    return entries[i].Team;
                }
            }

            return TeamSide.None;
        }

        /// <summary>Команда участника по его персонажу. Восемь записей — перебор дешевле словаря.</summary>
        public TeamSide TeamOfAvatar(PlayerController avatar)
        {
            if (avatar == null)
            {
                return TeamSide.None;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Avatar == avatar)
                {
                    return entries[i].Team;
                }
            }

            return TeamSide.None;
        }

        /// <summary>
        /// Следующая точка пути команды: доска, горлышко, доска — и только
        /// потом сама цель. Разбор, зачем это нужно, — в
        /// <see cref="CarryItemBotRoute"/>.
        /// </summary>
        public Vector3 NextWaypoint(TeamSide side, Vector3 from, Vector3 to, out bool isFinal)
        {
            CarryItemBotRoute route = side == TeamSide.A ? teamA.Route : teamB.Route;
            if (route == null)
            {
                isFinal = true;
                return to;
            }

            return route.NextPoint(from, to, out isFinal);
        }

        /// <summary>Штабель этой команды. Нужен болванкам соло-теста.</summary>
        public BottleStack StackOf(TeamSide side) =>
            side == TeamSide.A ? teamA.Stack : side == TeamSide.B ? teamB.Stack : null;

        /// <summary>Бак этой команды. Нужен болванкам соло-теста.</summary>
        public WaterTank TankOf(TeamSide side) =>
            side == TeamSide.A ? teamA.Tank : side == TeamSide.B ? teamB.Tank : null;

        private void SetInputSuspended(bool suspended)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                PlayerController avatar = entries[i].Avatar;
                if (avatar != null && avatar.TryGetComponent(out PlayerInputReader reader))
                {
                    reader.SetSuspended(suspended);
                }
            }
        }

        /// <summary>
        /// Повесить болванки на манекенов. Только вне сети: в сетевой сессии
        /// манекенов не бывает, а болванка, доживи она туда, играла бы за
        /// живого человека.
        /// </summary>
        private void AttachBots()
        {
            bots.Clear();

            if (WorldAuthority.IsNetworkSession)
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                PlayerController avatar = entries[i].Avatar;
                if (avatar == null || !avatar.TryGetComponent(out PlayerInputReader reader) ||
                    reader.LocallyControlled)
                {
                    continue;
                }

                if (!avatar.TryGetComponent(out CarryItemDebugBot bot))
                {
                    bot = avatar.gameObject.AddComponent<CarryItemDebugBot>();
                }

                bot.enabled = true;
                bot.Configure(this, entries[i].Team, botObstacles);
                bots.Add(bot);
            }
        }
    }
}
