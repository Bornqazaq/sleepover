using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Interaction
{
    /// <summary>
    /// Поиск ближайшего IInteractable рядом с игроком и вызов Interact по кнопке.
    /// UI подписывается на CurrentInteractable для подсказки.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader inputReader;
        [Tooltip("Радиус поиска интерактивных объектов, м")]
        [SerializeField] private float interactRadius = 1.8f;

        private const int MaxCandidates = 8;

        private PlayerController self;
        private readonly Collider[] overlapResults = new Collider[MaxCandidates];

        public IInteractable CurrentInteractable { get; private set; }

        private void Awake()
        {
            self = GetComponent<PlayerController>();
        }

        private void Update()
        {
            CurrentInteractable = FindNearest();

            if (inputReader == null || !inputReader.InteractPressed)
            {
                return;
            }

            inputReader.ConsumeInteract();

            if (CurrentInteractable != null && CurrentInteractable.CanInteract(self))
            {
                CurrentInteractable.Interact(self);
            }
        }

        private IInteractable FindNearest()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, interactRadius, overlapResults);
            IInteractable nearest = null;
            float nearestSqr = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                IInteractable candidate = overlapResults[i].GetComponentInParent<IInteractable>();
                if (candidate == null || !candidate.CanInteract(self))
                {
                    continue;
                }

                float sqr = (overlapResults[i].transform.position - transform.position).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = candidate;
                }
            }

            return nearest;
        }
    }
}
