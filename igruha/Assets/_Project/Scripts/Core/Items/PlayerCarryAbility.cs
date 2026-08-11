using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Items
{
    /// <summary>
    /// Ношение предмета: якорь в руках, бросок вперёд-вверх.
    /// Бросок инициирует PlayerPushAbility (одна кнопка: нет предмета — толчок,
    /// есть предмет — бросок).
    /// </summary>
    public sealed class PlayerCarryAbility : MonoBehaviour
    {
        [Tooltip("Куда крепится предмет (пустышка перед грудью)")]
        [SerializeField] private Transform holdAnchor;
        [Tooltip("Импульс броска")]
        [SerializeField] private float throwForce = 9f;
        [Tooltip("Доля броска вверх")]
        [Range(0f, 1f)]
        [SerializeField] private float throwUpward = 0.35f;

        private PickupItem carried;
        private PlayerController motor;

        public bool IsCarrying => carried != null;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
        }

        private void OnEnable()
        {
            if (motor != null)
            {
                motor.KnockdownStarted += OnKnockdown;
            }
        }

        private void OnDisable()
        {
            if (motor != null)
            {
                motor.KnockdownStarted -= OnKnockdown;
            }
        }

        private void OnKnockdown(KnockdownType type) => Drop();

        public bool TryPickup(PickupItem item)
        {
            if (IsCarrying || item == null || item.IsHeld || holdAnchor == null)
            {
                return false;
            }

            carried = item;
            item.OnPickedUp(this, holdAnchor);
            return true;
        }

        public void Throw()
        {
            if (!IsCarrying)
            {
                return;
            }

            Vector3 impulse = (transform.forward + Vector3.up * throwUpward).normalized * throwForce;
            PickupItem item = carried;
            carried = null;
            item.OnThrown(impulse);
        }

        /// <summary>Выронить без импульса (нокдаун, респаун).</summary>
        public void Drop()
        {
            if (!IsCarrying)
            {
                return;
            }

            PickupItem item = carried;
            carried = null;
            item.OnThrown(Vector3.zero);
        }
    }
}
