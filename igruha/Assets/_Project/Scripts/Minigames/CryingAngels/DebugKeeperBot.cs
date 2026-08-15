using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Vision;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Водящий-болванка для соло-теста: водит лучом сам — ведёт, замирает,
    /// иногда разворачивается обратно и залипает на пойманном.
    ///
    /// Без него за Бегущего играть не во что. У болванки нет ввода, фонарь
    /// стоит неподвижно, и из игры исчезает ровно то, на чём она держится:
    /// чтение чужого взгляда и решение «успею или замру».
    ///
    /// Это отладочный стенд, а не ИИ: он не ищет, не помнит и не подозревает.
    /// Случайность здесь тоже отладочная — в сетевой катке Водящий всегда
    /// живой игрок, и бот не ставится вовсе.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class DebugKeeperBot : MonoBehaviour
    {
        private PlayerController motor;
        private CryingAngelsMinigame game;
        private CryingAngelsConfig config;
        private VisionCone vision;

        private float yaw;
        private float direction = 1f;
        private float sweepTimer;
        private float pauseTimer;
        private float holdTimer;
        private int litTargets;

        private void Awake() => motor = GetComponent<PlayerController>();

        /// <summary>Подключить к раунду. Конус нужен, чтобы бот залипал на пойманном.</summary>
        public void Configure(CryingAngelsMinigame minigame, VisionCone keeperVision)
        {
            Unsubscribe();

            game = minigame;
            config = minigame != null ? minigame.Config : null;
            vision = keeperVision;

            if (vision != null)
            {
                vision.TargetEntered += OnTargetLit;
                vision.TargetExited += OnTargetLost;
            }

            yaw = transform.eulerAngles.y;
            litTargets = 0;
            holdTimer = 0f;
            pauseTimer = 0f;
            sweepTimer = NextSweepDuration();
        }

        private void OnDisable() => Unsubscribe();

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            if (vision == null)
            {
                return;
            }

            vision.TargetEntered -= OnTargetLit;
            vision.TargetExited -= OnTargetLost;
            vision = null;
        }

        private void OnTargetLit(Collider target) => litTargets++;

        private void OnTargetLost(Collider target) => litTargets = Mathf.Max(0, litTargets - 1);

        private void FixedUpdate()
        {
            // На стартовом отсчёте фонарь выключен — крутить его незачем.
            if (game == null || config == null || !game.BeamEnabled)
            {
                return;
            }

            float delta = Time.fixedDeltaTime;

            // Поймал — держит. Живой Водящий не уводит луч с того, кого дожимает,
            // и без этого счётчик окаменения не успевает набраться ни разу.
            if (litTargets > 0)
            {
                if (holdTimer < config.BotHoldOnTarget)
                {
                    holdTimer += delta;
                    return;
                }
            }
            else
            {
                holdTimer = 0f;
            }

            if (pauseTimer > 0f)
            {
                pauseTimer -= delta;
                return;
            }

            // Пауза и разворот — то, что отличает живого человека от вентилятора:
            // равномерное вращение читается за один оборот и перестаёт пугать.
            sweepTimer -= delta;
            if (sweepTimer <= 0f)
            {
                pauseTimer = Random.Range(config.BotPauseDuration.x, config.BotPauseDuration.y);
                sweepTimer = NextSweepDuration();

                if (Random.value < config.BotReverseChance)
                {
                    direction = -direction;
                }

                return;
            }

            yaw += direction * game.KeeperTurnSpeed * delta;
            motor.SetFacing(yaw);
        }

        private float NextSweepDuration() =>
            config != null ? Random.Range(config.BotSweepInterval.x, config.BotSweepInterval.y) : 3f;
    }
}
