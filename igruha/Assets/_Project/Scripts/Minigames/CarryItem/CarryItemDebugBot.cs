using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Болванка соло-прогона: берёт тару со штабеля своей команды, тащит её к
    /// своему баку и идёт за следующей.
    ///
    /// Без неё фазу каркаса не принять вовсе. Вся механика игры — про
    /// <b>рассогласование между несущими</b>, а несущий в одиночном прогоне
    /// ровно один: ни крена от рывка партнёра, ни срыва ручки, ни тарана между
    /// командами проверить нечем. Одному человеку двумя персонажами не
    /// поуправлять, а сети на фазе каркаса ещё нет.
    ///
    /// Путь только человеческий: <c>DriveMove</c> у ходьбы и
    /// <c>DriveInteract</c> у кнопки E. Короткого пути в обход правил нет —
    /// иначе проверка перестала бы проверять то, что делает живой игрок.
    ///
    /// Это отладочный стенд, а не ИИ: болванка не саботирует, не выбирает
    /// момент и не бережёт воду. В сетевой катке не ставится вовсе.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class CarryItemDebugBot : MonoBehaviour
    {
        /// <summary>Что болванка делает прямо сейчас.</summary>
        private enum Step
        {
            /// <summary>Идёт к штабелю и держит там E.</summary>
            ToStack,
            /// <summary>Идёт к выданной таре и берётся за ручку.</summary>
            ToBottle,
            /// <summary>Тащит тару к своему баку.</summary>
            ToTank
        }

        /// <summary>
        /// С какого расстояния до цели болванка жмёт E, м.
        ///
        /// Заметно меньше радиуса поиска <c>PlayerInteractor</c> (1.8): нажатие
        /// на самой границе отбивается серверной проверкой дистанции, и
        /// болванка залипала бы у цели, жмя кнопку впустую.
        /// </summary>
        private const float InteractRange = 1.3f;

        /// <summary>Сколько ждать между попытками взяться, с. Без паузы E жмётся каждый кадр и берёт-отпускает по кругу.</summary>
        private const float GrabRetryDelay = 0.6f;

        private PlayerController motor;
        private PlayerInputReader reader;
        private DebugPlayerBot walker;
        private CarryItemMinigame game;
        private TeamSide team = TeamSide.None;

        private Step step = Step.ToStack;
        private float grabCooldown;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            reader = GetComponent<PlayerInputReader>();
        }

        private void OnDisable()
        {
            // Снятая болванка обязана бросить и ход, и кнопку: иначе она уходит
            // из-под управления с зажатым E и держит штабель до конца раунда.
            reader?.DriveMove(Vector2.zero);
            reader?.DriveInteractHold(false);

            if (walker != null)
            {
                walker.Stop();
                walker.enabled = false;
            }
        }

        /// <summary>Подключить к раунду. Слои препятствий задаёт мини-игра: у каждой арены они свои.</summary>
        public void Configure(CarryItemMinigame minigame, TeamSide side, LayerMask obstacles)
        {
            game = minigame;
            team = side;
            step = Step.ToStack;
            grabCooldown = 0f;

            if (walker == null && !TryGetComponent(out walker))
            {
                walker = gameObject.AddComponent<DebugPlayerBot>();
            }

            walker.enabled = true;
            walker.Configure(obstacles);
        }

        private void Update()
        {
            if (game == null || reader == null || walker == null)
            {
                return;
            }

            grabCooldown = Mathf.Max(0f, grabCooldown - Time.deltaTime);

            BottleStack stack = game.StackOf(team);
            WaterTank tank = game.TankOf(team);
            if (stack == null || tank == null)
            {
                return;
            }

            WaterBottle bottle = stack.LiveBottle;

            // Нокдаун и заморозка болванку не касаются: она просто перестаёт
            // жать. Держать E под ними нельзя — штабель отсчитает выдачу
            // лежачему.
            if (motor.IsKnockedDown || motor.MovementLocked || reader.Suspended)
            {
                reader.DriveInteractHold(false);
                walker.Stop();
                return;
            }

            step = ChooseStep(bottle);

            switch (step)
            {
                case Step.ToStack:
                    RunToStack(stack);
                    break;
                case Step.ToBottle:
                    RunToBottle(bottle);
                    break;
                case Step.ToTank:
                    RunToTank(tank);
                    break;
            }
        }

        private Step ChooseStep(WaterBottle bottle)
        {
            if (bottle == null || bottle.IsGone)
            {
                return Step.ToStack;
            }

            return bottle.Carry.IsCarriedBy(motor) ? Step.ToTank : Step.ToBottle;
        }

        /// <summary>У штабеля держим E: взятие — удержание, а не нажатие.</summary>
        private void RunToStack(BottleStack stack)
        {
            bool close = IsWithin(stack.transform.position, InteractRange);
            reader.DriveInteractHold(close);

            if (close)
            {
                walker.Stop();
                return;
            }

            walker.SetTarget(stack.transform.position);
        }

        /// <summary>
        /// К таре подходим не вплотную, а на расстояние своей стоянки: встав
        /// внутрь бутыли, болванка упёрлась бы в её коллайдер и сработал бы
        /// детектор застревания.
        /// </summary>
        private void RunToBottle(WaterBottle bottle)
        {
            reader.DriveInteractHold(false);

            Vector3 bottlePosition = bottle.transform.position;
            Vector3 away = transform.position - bottlePosition;
            away.y = 0f;

            float standoff = bottle.Carry.Settings.handleRadius + bottle.Carry.Settings.carrierStandoff;
            Vector3 station = away.sqrMagnitude > 0.0001f
                ? bottlePosition + away.normalized * standoff
                : bottlePosition + transform.forward * standoff;

            if (!IsWithin(bottlePosition, InteractRange + standoff))
            {
                walker.SetTarget(station);
                return;
            }

            walker.Stop();

            if (grabCooldown > 0f)
            {
                return;
            }

            grabCooldown = GrabRetryDelay;
            reader.DriveInteract();
        }

        private void RunToTank(WaterTank tank)
        {
            reader.DriveInteractHold(false);
            walker.SetTarget(tank.transform.position);
        }

        private bool IsWithin(Vector3 point, float range)
        {
            Vector3 offset = point - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= range * range;
        }
    }
}
