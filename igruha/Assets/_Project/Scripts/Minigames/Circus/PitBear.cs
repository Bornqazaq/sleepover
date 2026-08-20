using System;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Minigames.Circus
{
    /// <summary>
    /// Медведь в яме. Первый серверный NPC в проекте, поэтому написан так,
    /// чтобы в фазе 3 уйти под сервер без переписывания: всё, что меняет
    /// состояние мира, идёт через <see cref="Tick"/>, который зовёт владелец
    /// арены, и через одно событие <see cref="Caught"/>. Клиенту останется
    /// только не звать Tick и получать позицию через NetworkTransform.
    ///
    /// Победить медведя нельзя: здоровья у него нет, задача — не убить,
    /// а честно побегать пять секунд. Догоняет он срезанием по хорде,
    /// а не скоростью: 5.5 м/с против 6.5 у игрока.
    ///
    /// <b>Как поменять серую заготовку на настоящего медведя:</b>
    /// 1. Положить модель ребёнком в <c>Visual</c> и удалить оттуда примитивы.
    /// 2. Назначить <c>animator</c> — аниматор модели.
    /// 3. Если у ассета другие имена параметров, вписать их в поля
    ///    <c>speedParameter</c> / <c>strikeParameter</c> / <c>roarParameter</c>.
    /// Кода это не трогает: логика не знает, как медведь выглядит.
    /// </summary>
    public sealed class PitBear : MonoBehaviour
    {
        public enum BearState
        {
            /// <summary>В яме никого — медведь наматывает круги.</summary>
            Patrol,
            /// <summary>Разворачивается и разгоняется, ещё не бьёт.</summary>
            WindUp,
            /// <summary>Гонится за ближайшим.</summary>
            Chase,
            /// <summary>Встал на задние лапы под нижней клеткой: рёв, удар по решётке, урона нет.</summary>
            Taunt
        }

        [Header("Тушка — меняется на ассет без правок кода")]
        [Tooltip("Корень визуала. Сюда кладётся модель медведя вместо серых примитивов")]
        [SerializeField] private Transform visualRoot;
        [Tooltip("Аниматор модели. Пусто — анимаций нет, логика работает как есть")]
        [SerializeField] private Animator animator;
        [Tooltip("Float-параметр скорости в аниматоре")]
        [SerializeField] private string speedParameter = "Speed";
        [Tooltip("Trigger удара лапой")]
        [SerializeField] private string strikeParameter = "Strike";
        [Tooltip("Trigger рёва")]
        [SerializeField] private string roarParameter = "Roar";

        [Header("Движение")]
        [Tooltip("Скорость поворота корпуса, °/с")]
        [SerializeField] private float turnSpeed = 220f;
        [Tooltip("Насколько близко к борту медведь подходит, м")]
        [SerializeField] private float wallMargin = 0.8f;

        /// <summary>Медведь достал игрока. Второй аргумент — импульс отлёта.</summary>
        public event Action<PlayerController, Vector3> Caught;

        private float chaseSpeed = 5.5f;
        private float patrolSpeed = 2.5f;
        private float strikeRadius = 1.5f;
        private float windUpDuration = 3f;
        private float knockbackSpeed = 8f;
        private float pitRadius = 8.64f;

        private float windUpLeft;
        private float patrolAngle;
        private BearState state = BearState.Patrol;

        public BearState State => state;

        /// <summary>Кого гонит прямо сейчас. Null — никого.</summary>
        public PlayerController Target { get; private set; }

        /// <summary>Корень визуала — сюда кладут модель вместо серых примитивов.</summary>
        public Transform VisualRoot => visualRoot;

        /// <summary>
        /// Числа приходят снаружи: они лежат в конфиге «Секундомера», а медведь
        /// живёт в общей папке арены и про конкретную игру знать не должен.
        /// </summary>
        public void Configure(float chase, float patrol, float strike, float windUp, float knockback, float pit)
        {
            chaseSpeed = chase;
            patrolSpeed = patrol;
            strikeRadius = strike;
            windUpDuration = windUp;
            knockbackSpeed = knockback;
            pitRadius = pit;
        }

        /// <summary>
        /// Шаг ИИ. Зовёт владелец арены — в фазе 3 только на сервере.
        /// Единственная точка, которая двигает медведя и решает, кого он достал.
        /// </summary>
        public void Tick(float deltaTime, PlayerController nearest, bool someoneOnLowestCage)
        {
            if (nearest == null)
            {
                Target = null;
                // Медведь дразнит того, кто на последней ступени: рёв и удар
                // по решётке. Урона нет — игрок на грани и так в худшем
                // положении из всех, добивать его помехами незачем.
                SetState(someoneOnLowestCage ? BearState.Taunt : BearState.Patrol);
                Patrol(deltaTime);
                return;
            }

            if (Target != nearest)
            {
                // Новая жертва — новый разгон: у выпавшего должен быть
                // честный забег, а не мгновенная смерть.
                Target = nearest;
                windUpLeft = windUpDuration;
                SetState(BearState.WindUp);
            }

            Vector3 toTarget = nearest.transform.position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            FaceTowards(toTarget, deltaTime);

            if (windUpLeft > 0f)
            {
                windUpLeft -= deltaTime;
                SetAnimatorSpeed(0f);
                if (windUpLeft <= 0f)
                {
                    SetState(BearState.Chase);
                }

                return;
            }

            SetState(BearState.Chase);
            MoveBy(toTarget.normalized * (chaseSpeed * deltaTime));
            SetAnimatorSpeed(chaseSpeed);

            if (distance > strikeRadius)
            {
                return;
            }

            Vector3 impulse = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : transform.forward;
            impulse.y = 0.35f;
            Trigger(strikeParameter);
            Target = null;
            Caught?.Invoke(nearest, impulse.normalized * knockbackSpeed);
        }

        /// <summary>
        /// Показать состояние, решённое сервером. Клиент медведя не двигает —
        /// позицию везёт серверный NetworkTransform, — но рёв и скорость
        /// в аниматоре обязаны совпасть у всех, иначе на одной машине медведь
        /// встаёт на лапы, а на другой молча идёт мимо.
        /// </summary>
        public void ApplyNetworkState(BearState next, float animatorSpeed)
        {
            SetState(next);
            SetAnimatorSpeed(animatorSpeed);
        }

        /// <summary>Скорость для аниматора по текущему состоянию — её же реплицируем.</summary>
        public float AnimatorSpeed =>
            state == BearState.Chase ? chaseSpeed : (state == BearState.Patrol ? patrolSpeed : 0f);

        private void Patrol(float deltaTime)
        {
            float radius = Mathf.Max(1f, pitRadius - wallMargin * 2f);
            patrolAngle += patrolSpeed / radius * deltaTime * Mathf.Rad2Deg;
            Vector3 target = Quaternion.Euler(0f, patrolAngle, 0f) * Vector3.forward * radius;
            Vector3 delta = target - transform.position;
            delta.y = 0f;

            FaceTowards(delta, deltaTime);
            MoveBy(Vector3.ClampMagnitude(delta, patrolSpeed * deltaTime));
            SetAnimatorSpeed(patrolSpeed);
        }

        /// <summary>Держим медведя внутри ямы: за борт ему нельзя ни при какой погоне.</summary>
        private void MoveBy(Vector3 delta)
        {
            Vector3 next = transform.position + delta;
            Vector2 flat = new Vector2(next.x, next.z);
            float limit = Mathf.Max(0.5f, pitRadius - wallMargin);
            if (flat.magnitude > limit)
            {
                flat = flat.normalized * limit;
                next.x = flat.x;
                next.z = flat.y;
            }

            transform.position = next;
        }

        private void FaceTowards(Vector3 direction, float deltaTime)
        {
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion wanted = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, turnSpeed * deltaTime);
        }

        private void SetState(BearState next)
        {
            if (state == next)
            {
                return;
            }

            state = next;
            if (next == BearState.Taunt)
            {
                Trigger(roarParameter);
            }
        }

        private void SetAnimatorSpeed(float value)
        {
            if (animator != null && !string.IsNullOrEmpty(speedParameter))
            {
                animator.SetFloat(speedParameter, value);
            }
        }

        private void Trigger(string parameter)
        {
            if (animator != null && !string.IsNullOrEmpty(parameter))
            {
                animator.SetTrigger(parameter);
            }
        }
    }
}
