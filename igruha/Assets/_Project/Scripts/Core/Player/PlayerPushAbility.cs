using System;
using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Удар (Cross Punch): по кнопке проигрывается замах, а импульс прилетает
    /// целям в переднем секторе с задержкой под контакт анимации.
    /// Урон/отбрасывание идёт через публичный ApplyPush цели — при переходе
    /// на NGO этот вызов станет ServerRpc (клиент просит, сервер применяет).
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerPushAbility : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader inputReader;

        private const int MaxTargets = 8;

        /// <summary>Замах начался — визуал проигрывает клип удара.</summary>
        public event Action PunchStarted;

        /// <summary>
        /// Роль, забравшая кнопку себе: пока она стоит, персонаж не бьёт и не
        /// бросает предмет. Ставит и снимает тот, кто занял руки, — см.
        /// <see cref="IPushButtonOverride"/>.
        /// </summary>
        public IPushButtonOverride ButtonOverride { get; set; }

        private PlayerController self;
        private CapsuleCollider body;
        private Igruha.Core.Items.PlayerCarryAbility carryAbility;
        private IPushRelay pushRelay;
        private readonly Collider[] overlapResults = new Collider[MaxTargets];
        private float cooldownTimer;
        private float impactTimer;
        private bool impactPending;

        private void Awake()
        {
            self = GetComponent<PlayerController>();
            body = GetComponent<CapsuleCollider>();
            carryAbility = GetComponent<Igruha.Core.Items.PlayerCarryAbility>();
            pushRelay = GetComponent<IPushRelay>();
        }

        private void Update()
        {
            cooldownTimer = Mathf.Max(0f, cooldownTimer - Time.deltaTime);
            UpdatePendingImpact();

            if (inputReader == null || !inputReader.PushPressed)
            {
                return;
            }

            inputReader.ConsumePush();

            if (self.IsKnockedDown || self.Config == null)
            {
                return;
            }

            // Роль, занявшая руки, разбирает нажатие раньше кулдауна удара:
            // у неё своя цена действия, и кулдаун кулака к ней отношения не имеет.
            if (ButtonOverride != null && ButtonOverride.HandlePushButton(self))
            {
                return;
            }

            if (cooldownTimer > 0f)
            {
                return;
            }

            // Одна кнопка: несёшь предмет — бросок, иначе — удар.
            if (carryAbility != null && carryAbility.IsCarrying)
            {
                carryAbility.Throw();
                return;
            }

            cooldownTimer = self.Config.PushCooldown;
            impactTimer = self.Config.PunchImpactDelay;
            impactPending = true;
            PunchStarted?.Invoke();
        }

        private void UpdatePendingImpact()
        {
            if (!impactPending)
            {
                return;
            }

            impactTimer -= Time.deltaTime;
            if (impactTimer > 0f)
            {
                return;
            }

            impactPending = false;

            // Сбили в момент замаха — удар не доходит.
            if (!self.IsKnockedDown)
            {
                PushTargetsInArc();
            }
        }

        /// <summary>
        /// Зазор между телами, м. Обе капсулы вертикальные — вращение по X и Z
        /// у персонажа заперто, — поэтому расстояние между ними считается
        /// точно: по горизонтали между осями, по вертикали между отрезками.
        /// Отсюда же берётся и честная проверка по высоте: стоящий этажом выше
        /// перестаёт быть целью сам собой, без отдельного условия.
        ///
        /// Не капсула — считаем по ближайшей точке коллайдера: это запасной
        /// путь для целей, собранных не из капсулы.
        /// </summary>
        private float BodyGap(Collider targetCollider)
        {
            if (body == null || targetCollider is not CapsuleCollider targetCapsule)
            {
                return Vector3.Distance(targetCollider.ClosestPoint(transform.position), transform.position);
            }

            Bounds mine = body.bounds;
            Bounds theirs = targetCapsule.bounds;

            float myRadius = mine.extents.x;
            float theirRadius = theirs.extents.x;

            float horizontal = new Vector2(theirs.center.x - mine.center.x, theirs.center.z - mine.center.z).magnitude;

            // Отрезок оси капсулы: от центра вверх и вниз на половину высоты
            // минус радиус. У приземистой капсулы отрезок вырождается в точку.
            float myHalf = Mathf.Max(0f, mine.extents.y - myRadius);
            float theirHalf = Mathf.Max(0f, theirs.extents.y - theirRadius);
            float vertical = Mathf.Max(0f,
                Mathf.Abs(theirs.center.y - mine.center.y) - myHalf - theirHalf);

            float axisDistance = Mathf.Sqrt(horizontal * horizontal + vertical * vertical);
            return axisDistance - myRadius - theirRadius;
        }

        /// <summary>
        /// Кого достаёт удар.
        ///
        /// Сфера здесь — только широкая выборка кандидатов, а не досягаемость.
        /// Досягаемость меряется зазором между телами: раньше её задавал радиус
        /// сферы от корня персонажа, а корень стоит в ступнях — до соседа
        /// «доставало» через полтора метра пустоты и вдобавок через этаж вверх,
        /// потому что сфере всё равно, на какой высоте цель.
        /// </summary>
        private void PushTargetsInArc()
        {
            CharacterConfig config = self.Config;
            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, config.PushRadius, overlapResults);
            float halfArc = config.PushArcAngle * 0.5f;

            for (int i = 0; i < hitCount; i++)
            {
                if (!overlapResults[i].TryGetComponent(out PlayerController target) || target == self)
                {
                    continue;
                }

                // Иммунного отсекаем здесь, а не на его стороне: иначе в сетевой
                // игре на каждый замах уходит заведомо пустой толчок по сети.
                if (target.ImpulseImmune)
                {
                    continue;
                }

                Vector3 toTarget = target.transform.position - transform.position;
                toTarget.y = 0f;

                if (toTarget.sqrMagnitude >= 0.0001f &&
                    Vector3.Angle(transform.forward, toTarget) > halfArc)
                {
                    continue;
                }

                if (BodyGap(overlapResults[i]) > config.PunchReach)
                {
                    continue;
                }

                // В сетевой игре толчок доставляет владельцу цели сетевой слой,
                // иначе локальное изменение будет перетёрто владельцем
                if (pushRelay != null && pushRelay.TryRelayPush(target, toTarget, config.PushForce))
                {
                    continue;
                }

                target.ApplyPush(toTarget, config.PushForce);
            }
        }
    }
}
