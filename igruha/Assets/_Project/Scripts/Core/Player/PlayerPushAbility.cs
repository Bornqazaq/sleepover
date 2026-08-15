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

        private PlayerController self;
        private Igruha.Core.Items.PlayerCarryAbility carryAbility;
        private IPushRelay pushRelay;
        private readonly Collider[] overlapResults = new Collider[MaxTargets];
        private float cooldownTimer;
        private float impactTimer;
        private bool impactPending;

        private void Awake()
        {
            self = GetComponent<PlayerController>();
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

            if (cooldownTimer > 0f || self.IsKnockedDown || self.Config == null)
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
