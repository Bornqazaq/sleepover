using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Player;

namespace Igruha.Core.Items
{
    /// <summary>
    /// Подбираемый и бросаемый предмет (Переноска, Duck Hunt, подушка тай-брейка).
    /// Подбор — через Interact, бросок — кнопкой толчка у носителя.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PickupItem : MonoBehaviour, IInteractable
    {
        [SerializeField] private string itemName = "Предмет";

        private Rigidbody rb;
        private Collider[] colliders;
        private PlayerCarryAbility holder;

        public bool IsHeld => holder != null;
        public string InteractionPrompt => $"Подобрать: {itemName}";

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            colliders = GetComponentsInChildren<Collider>();
        }

        public bool CanInteract(PlayerController player) => !IsHeld;

        public void Interact(PlayerController player)
        {
            if (player.TryGetComponent(out PlayerCarryAbility carry))
            {
                carry.TryPickup(this);
            }
        }

        /// <summary>Вызывается PlayerCarryAbility. Позже станет server-authoritative.</summary>
        public void OnPickedUp(PlayerCarryAbility newHolder, Transform anchor)
        {
            holder = newHolder;
            rb.isKinematic = true;
            SetCollidersEnabled(false);
            transform.SetParent(anchor, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        public void OnThrown(Vector3 impulse)
        {
            holder = null;
            transform.SetParent(null, true);
            SetCollidersEnabled(true);
            rb.isKinematic = false;
            rb.AddForce(impulse, ForceMode.Impulse);
        }

        private void SetCollidersEnabled(bool value)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = value;
            }
        }
    }
}
