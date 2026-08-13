using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Igruha.Core.Combat;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.Spawning;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Правила Duck Hunt поверх шаблона: раздача ролей, финиш на крыше,
    /// определение мест. Бег, прыжок, толчки, кувырок, респаун, таймер,
    /// обучалка, ловушки и стрельба берутся готовыми из Igruha.Core.
    /// </summary>
    public sealed class DuckHuntMinigame : MinigameControllerBase
    {
        [Header("Арена")]
        [SerializeField] private SpawnPointSet spawnPoints;
        [SerializeField] private DuckHuntFinishZone finishZone;
        [Tooltip("Ружьё охотника: префаб стрелка, вешается на персонажа-охотника")]
        [SerializeField] private ProjectileShooter hunterShooterPrefab;

        [Header("Правила")]
        [Tooltip("Сколько попаданий улучшают место охотника на одну позицию")]
        [SerializeField] private int hitsPerPlacement = 2;

        [Header("Дебаг (тест в одиночку)")]
        [Tooltip("Локальный игрок играет за охотника, иначе за утку")]
        [SerializeField] private bool localPlayerIsHunter;
        [SerializeField] private Key roleSwitchKey = Key.F1;

        private readonly List<DuckProgress> ducks = new List<DuckProgress>(8);
        private HunterCombat hunter;
        private int hunterPlayerIndex;
        private float roundStartTime;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (finishZone != null)
            {
                finishZone.DuckArrived += OnDuckArrived;
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (finishZone != null)
            {
                finishZone.DuckArrived -= OnDuckArrived;
            }
        }

        protected override void OnPlayersReady()
        {
            AssignRoles();
        }

        protected override void OnRoundStarted()
        {
            roundStartTime = Time.time;
        }

        private void Update()
        {
            // В сетевой катке роли одинаковы у всех — переключать их нельзя.
            if (SessionScoreboard.IsNetworked)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[roleSwitchKey].wasPressedThisFrame)
            {
                return;
            }

            localPlayerIsHunter = !localPlayerIsHunter;
            AssignRoles();
            Debug.Log($"Duck Hunt: локальный игрок теперь {(localPlayerIsHunter ? "ОХОТНИК" : "УТКА")}");
        }

        private void AssignRoles()
        {
            if (Players.Count == 0)
            {
                return;
            }

            ducks.Clear();
            hunter = null;

            // Охотник ровно один. В сети роль выводим из ростера (он одинаков на
            // всех машинах), поэтому договариваться по сети не нужно. В соло-тесте
            // роль переключается дебаг-кнопкой: если игрок — утка, охотником
            // становится ближайший манекен.
            hunterPlayerIndex = SessionScoreboard.IsNetworked
                ? 0
                : localPlayerIsHunter ? 0 : Mathf.Min(1, Players.Count - 1);

            int duckIndex = 0;
            for (int i = 0; i < Players.Count; i++)
            {
                PlayerController avatar = Players[i].Avatar;
                if (avatar == null)
                {
                    continue;
                }

                bool isHunter = i == hunterPlayerIndex && Players.Count > 1;
                if (isHunter)
                {
                    SetupHunter(avatar, Players[i].Id);
                }
                else
                {
                    SetupDuck(avatar, Players[i].Id, duckIndex);
                    duckIndex++;
                }
            }
        }

        private void SetupDuck(PlayerController avatar, int playerId, int duckIndex)
        {
            HunterCombat leftoverHunter = avatar.GetComponent<HunterCombat>();
            if (leftoverHunter != null)
            {
                Destroy(leftoverHunter);
            }

            ProjectileShooter leftoverShooter = avatar.GetComponentInChildren<ProjectileShooter>();
            if (leftoverShooter != null)
            {
                Destroy(leftoverShooter.gameObject);
            }

            DuckProgress duck = avatar.GetComponent<DuckProgress>();
            if (duck == null)
            {
                duck = avatar.gameObject.AddComponent<DuckProgress>();
            }

            duck.PlayerId = playerId;
            ducks.Add(duck);
            MoveToSpawn(avatar, SpawnRole.Default, duckIndex);
        }

        private void SetupHunter(PlayerController avatar, int playerId)
        {
            DuckProgress leftoverDuck = avatar.GetComponent<DuckProgress>();
            if (leftoverDuck != null)
            {
                Destroy(leftoverDuck);
            }

            HunterCombat combat = avatar.GetComponent<HunterCombat>();
            if (combat == null)
            {
                combat = avatar.gameObject.AddComponent<HunterCombat>();
            }

            ProjectileShooter shooter = avatar.GetComponentInChildren<ProjectileShooter>();
            if (shooter == null && hunterShooterPrefab != null)
            {
                shooter = Instantiate(hunterShooterPrefab, avatar.transform);
                shooter.transform.localPosition = new Vector3(0f, 0.35f, 0.5f);
                shooter.transform.localRotation = Quaternion.identity;
            }

            combat.ResetHits();
            combat.Configure(avatar.GetComponent<PlayerInputReader>(), shooter);
            hunter = combat;
            MoveToSpawn(avatar, SpawnRole.Special, 0);
        }

        private void MoveToSpawn(PlayerController avatar, SpawnRole role, int index)
        {
            if (spawnPoints == null)
            {
                return;
            }

            SpawnPoint point = spawnPoints.GetPoint(role, index);
            if (point == null)
            {
                return;
            }

            avatar.RequestTeleport(point.transform.position, point.transform.rotation);

            if (avatar.TryGetComponent(out PlayerRespawner respawner))
            {
                respawner.SetRespawnPoint(point.transform);
            }
        }

        private void OnDuckArrived(DuckProgress duck)
        {
            // Зона финиша срабатывает на каждой машине, но зачёт и досрочное
            // завершение раунда — дело авторитета, иначе итоги разъедутся.
            if (!RoundActive || !HasAuthority)
            {
                return;
            }

            duck.MarkFinished(Time.time - roundStartTime);

            for (int i = 0; i < ducks.Count; i++)
            {
                if (!ducks[i].Finished)
                {
                    return;
                }
            }

            // Все утки на крыше — раунд закончен досрочно.
            EndMinigame();
        }

        protected override void CollectResults(MinigameResults results)
        {
            // Утки: сначала дошедшие до крыши по времени, затем остальные по высоте.
            var ranked = new List<DuckProgress>(ducks);
            ranked.Sort(CompareDucks);

            var order = new List<int>(Players.Count);
            for (int i = 0; i < ranked.Count; i++)
            {
                order.Add(ranked[i].PlayerId);
            }

            if (hunter != null)
            {
                int finished = 0;
                for (int i = 0; i < ducks.Count; i++)
                {
                    if (ducks[i].Finished)
                    {
                        finished++;
                    }
                }

                int bonus = hitsPerPlacement > 0 ? hunter.Hits / hitsPerPlacement : 0;
                int hunterIndex = Mathf.Clamp(finished - bonus, 0, order.Count);
                order.Insert(hunterIndex, Players[hunterPlayerIndex].Id);
            }

            for (int i = 0; i < order.Count; i++)
            {
                results.Add(order[i], i + 1);
            }
        }

        private static int CompareDucks(DuckProgress a, DuckProgress b)
        {
            if (a.Finished != b.Finished)
            {
                return a.Finished ? -1 : 1;
            }

            return a.Finished
                ? a.FinishTime.CompareTo(b.FinishTime)
                : b.BestHeight.CompareTo(a.BestHeight);
        }
    }
}
