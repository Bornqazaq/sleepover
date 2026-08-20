using System;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Состояние Бегущего в раунде. Пока два: СВОБОДЕН и ЗАМОРОЖЕН
    /// (ОКАМЕНЕЛ добавляет 14.6 вместе со счётчиком).
    ///
    /// Заморозка блокирует только движение: камера остаётся управляемой,
    /// иначе замороженный слепнет и перестаёт понимать, что происходит,
    /// а вся соль роли — смотреть, как тебя обходят.
    ///
    /// Заморозка и нокдаун независимы. Замороженного можно толкнуть — он
    /// комично отлетает и падает, но остаётся замороженным, пока на нём луч.
    /// Управление вернётся, когда закончатся оба: этим занимается сам
    /// PlayerController, у него блокировка и нокдаун живут рядом.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class RunnerState : MonoBehaviour
    {
        public enum Phase
        {
            Free,
            Frozen,
            Petrified
        }

        private PlayerController motor;
        private PlayerRespawner respawner;
        private CryingAngelsConfig config;
        private float petrifyTimer;
        private float petrifyAnimationTimer;

        /// <summary>Состояние сменилось — визуал и HUD цепляются сюда.</summary>
        public event Action<Phase> Changed;

        public Phase Current { get; private set; } = Phase.Free;

        public bool IsFree => Current == Phase.Free;

        /// <summary>
        /// Номер нелепой позы, в которой игрок замер, 0..FreezePoseCount-1.
        /// Выбирается там же, где принимается решение о заморозке, — то есть
        /// у авторитета, — и потому одинаков у всех: поза должна выглядеть
        /// одинаково на всех машинах, иначе замерший «дёргается» по сети.
        /// </summary>
        public int FreezePose { get; private set; }

        /// <summary>Точка внутри клипа, на которой встал стоп-кадр, 0..1.</summary>
        public float FreezePoseTime { get; private set; }

        /// <summary>
        /// Насколько игрок близок к окаменению, 0..1. По нему красится луч
        /// и виньетка (14.7): без обратной связи ни Водящий, ни Бегущий
        /// не понимают, что счётчик вообще существует.
        /// </summary>
        public float PetrifyProgress =>
            config != null && config.PetrifyThreshold > 0f
                ? Mathf.Clamp01(petrifyTimer / config.PetrifyThreshold)
                : 0f;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            respawner = GetComponent<PlayerRespawner>();
        }

        /// <summary>Числа раунда. Ставится на раздаче ролей, до первого тика.</summary>
        public void Configure(CryingAngelsConfig roundConfig) => config = roundConfig;

        /// <summary>
        /// Единственная точка смены заморозки. Решение принимает контроллер
        /// раунда под авторитетом; в сетевой фазе сюда придёт реплицированное
        /// состояние, а тело метода останется прежним.
        /// </summary>
        public void SetFrozen(bool frozen)
        {
            // Окаменение главнее луча: оно доигрывает свою анимацию и возвращает
            // игрока на старт, чем бы его в это время ни светили.
            if (Current == Phase.Petrified)
            {
                return;
            }

            Phase next = frozen ? Phase.Frozen : Phase.Free;
            if (Current == next)
            {
                return;
            }

            if (frozen)
            {
                PickFreezePose();
            }

            Current = next;
            motor.MovementLocked = frozen;
            Changed?.Invoke(Current);
        }

        /// <summary>
        /// Поставить состояние, решённое сервером. Клиент засветку не считает
        /// вовсе: он узнаёт результат и применяет его у себя — блокировку
        /// движения владельцу (двигается он всё равно сам, через
        /// ClientNetworkTransform) и стоп-кадр всем.
        ///
        /// Тело метода намеренно повторяет то, что делают <see cref="SetFrozen"/>
        /// и окаменение, но без правил: правила — работа сервера, здесь только
        /// применение результата.
        /// </summary>
        public void ApplyNetworkState(Phase phase, int freezePose, float freezePoseTime, float petrifyProgress)
        {
            // Поза ставится до смены состояния: стоп-кадр читает её в обработчике
            // Changed, и переставленная следом поза до него уже не доедет.
            FreezePose = freezePose;
            FreezePoseTime = freezePoseTime;

            // Счётчик кладём в то же поле, из которого его считает сервер, а не
            // в отдельное «сетевое»: тогда PetrifyProgress, виньетка и цвет луча
            // считаются у всех одной формулой, и вторая ветка не заводится.
            if (config != null)
            {
                petrifyTimer = Mathf.Clamp01(petrifyProgress) * config.PetrifyThreshold;
            }

            if (Current == phase)
            {
                return;
            }

            Current = phase;
            motor.MovementLocked = phase != Phase.Free;
            Changed?.Invoke(Current);
        }

        /// <summary>
        /// Случайная нелепая поза. На каркасе это стоп-кадр анимации в
        /// произвольной точке клипа — отдельных клипов поз ещё нет, их
        /// завозит арт-фаза, а механику надо щупать уже сейчас.
        /// </summary>
        private void PickFreezePose()
        {
            int poses = config != null ? config.FreezePoseCount : 1;
            FreezePose = UnityEngine.Random.Range(0, poses);
            FreezePoseTime = UnityEngine.Random.value;
        }

        /// <summary>
        /// Тик раунда: копим или откатываем счётчик окаменения и доигрываем
        /// окаменение, если оно началось. Зовётся контроллером под авторитетом
        /// в физическом такте — решение зависит от положения тел.
        ///
        /// Счётчик живёт отдельно от заморозки: он копится, пока луч на игроке,
        /// и откатывается вдвое медленнее, когда луча нет — при этом игрок уже
        /// бегает. Отсюда тактика Водящего «давить короткими подходами» вместо
        /// честного удержания четырёх секунд.
        /// </summary>
        public void Tick(bool lit, float deltaTime)
        {
            if (config == null)
            {
                return;
            }

            if (Current == Phase.Petrified)
            {
                TickPetrification(deltaTime);
                return;
            }

            float rate = lit ? config.PetrifyGainMultiplier : -config.PetrifyDecayMultiplier;
            petrifyTimer = Mathf.Clamp(petrifyTimer + rate * deltaTime, 0f, config.PetrifyThreshold);

            if (petrifyTimer >= config.PetrifyThreshold)
            {
                BeginPetrification();
            }
        }

        /// <summary>
        /// Окаменение — откат на старт, а не выбывание: штрафа по очкам нет,
        /// число окаменений не ограничено. Лучший достигнутый радиус при этом
        /// не сбрасывается, иначе рывок к центру наказывался бы дважды.
        /// </summary>
        private void BeginPetrification()
        {
            Current = Phase.Petrified;
            petrifyAnimationTimer = config.PetrifyAnimationDuration;
            motor.MovementLocked = true;
            Changed?.Invoke(Current);
        }

        private void TickPetrification(float deltaTime)
        {
            petrifyAnimationTimer -= deltaTime;
            if (petrifyAnimationTimer > 0f)
            {
                return;
            }

            respawner?.Respawn();
            petrifyTimer = 0f;
            Current = Phase.Free;
            motor.MovementLocked = false;
            Changed?.Invoke(Current);
        }

        /// <summary>Снять всё и вернуть в СВОБОДЕН: конец раунда, пересдача ролей.</summary>
        public void ResetState()
        {
            petrifyTimer = 0f;
            petrifyAnimationTimer = 0f;

            if (Current == Phase.Petrified)
            {
                Current = Phase.Free;
                motor.MovementLocked = false;
                Changed?.Invoke(Current);
                return;
            }

            SetFrozen(false);
        }
    }
}
