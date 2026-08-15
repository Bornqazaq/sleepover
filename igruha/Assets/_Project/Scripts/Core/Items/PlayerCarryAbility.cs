using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Player;

namespace Igruha.Core.Items
{
    /// <summary>
    /// Ношение предмета: якорь в руках, бросок вперёд-вверх.
    /// Бросок инициирует PlayerPushAbility (одна кнопка: нет предмета — толчок,
    /// есть предмет — бросок).
    ///
    /// Что именно несёт игрок — состояние предмета, а не персонажа: его хранит
    /// и рассылает <see cref="PickupItem"/>, а сюда оно приходит через
    /// <see cref="OnItemTaken"/> / <see cref="OnItemLost"/>. Так носитель одинаково
    /// известен на всех машинах, включая позднее подключившихся.
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
        private IInteractionRelay relay;

        public bool IsCarrying => carried != null;

        /// <summary>Якорь в руке. Предмет цепляется к нему сам, на каждой машине.</summary>
        public Transform HoldAnchor => holdAnchor;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            relay = GetComponent<IInteractionRelay>();
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

        /// <summary>
        /// Занять предмет. По сети зовётся только на сервере: сюда приводит
        /// <c>PickupItem.Interact</c>, а его — серверное взаимодействие (2.20).
        /// </summary>
        public bool TryPickup(PickupItem item)
        {
            if (IsCarrying || item == null || item.IsHeld || holdAnchor == null)
            {
                return false;
            }

            item.OnPickedUp(this, holdAnchor);
            return true;
        }

        /// <summary>
        /// Бросок — действие игрока, поэтому по сети оно уходит намерением серверу.
        /// Локально исполняется только когда сети нет.
        /// </summary>
        public void Throw()
        {
            if (!IsCarrying)
            {
                return;
            }

            if (relay != null && relay.TryRelayThrow(true))
            {
                return;
            }

            ServerThrow(true);
        }

        /// <summary>
        /// Единственная точка исполнения броска. По сети её зовёт сервер,
        /// получив намерение владельца. <paramref name="withImpulse"/> различает
        /// бросок и «выронил»: нокдаун роняет предмет под ноги, а не швыряет.
        /// </summary>
        public void ServerThrow(bool withImpulse)
        {
            if (!IsCarrying)
            {
                return;
            }

            Vector3 impulse = withImpulse
                ? (transform.forward + Vector3.up * throwUpward).normalized * throwForce
                : Vector3.zero;

            carried.OnThrown(impulse);
        }

        /// <summary>Выронить без импульса (нокдаун, респаун).</summary>
        public void Drop()
        {
            if (!IsCarrying)
            {
                return;
            }

            if (relay != null && relay.TryRelayThrow(false))
            {
                return;
            }

            ServerThrow(false);
        }

        /// <summary>Предмет сообщает, что теперь у этого носителя. Зовётся на всех машинах.</summary>
        public void OnItemTaken(PickupItem item) => carried = item;

        /// <summary>Предмет сообщает, что больше не здесь. Зовётся на всех машинах.</summary>
        public void OnItemLost(PickupItem item)
        {
            if (carried == item)
            {
                carried = null;
            }
        }
    }
}
