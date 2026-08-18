using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Interaction
{
    /// <summary>
    /// Поиск ближайшего IInteractable рядом с игроком и вызов Interact по кнопке.
    /// UI подписывается на CurrentInteractable для подсказки.
    ///
    /// Нажатие — это намерение: по сети оно уходит серверу, сервер проверяет
    /// дистанцию и право на действие и исполняет. Локально выполняется только
    /// когда сети нет (одиночный тест сцены).
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader inputReader;
        [Tooltip("Радиус поиска интерактивных объектов, м")]
        [SerializeField] private float interactRadius = 1.8f;

        /// <summary>
        /// Размер буфера OverlapSphereNonAlloc. Восьми не хватает: игрок
        /// в клетке «Секундомера» уже собирает семь коллайдеров (три стены,
        /// две створки пола, своя капсула, кнопка), и любой восьмой рядом
        /// вытеснял бы кнопку из выборки — взаимодействие молча переставало
        /// бы работать. Массив создаётся один раз, запас ничего не стоит.
        /// </summary>
        private const int MaxCandidates = 16;

        /// <summary>
        /// Запас на задержку: у сервера позиция игрока отстаёт от машины владельца,
        /// и честное нажатие у самой границы радиуса иначе отклонялось бы.
        /// Тот же приём, что в серверной проверке толчка.
        /// </summary>
        private const float ServerReachTolerance = 1.5f;

        private PlayerController self;
        private IInteractionRelay relay;
        private readonly Collider[] overlapResults = new Collider[MaxCandidates];

        public IInteractable CurrentInteractable { get; private set; }

        /// <summary>Кого сейчас держим. Null — ничего не держим.</summary>
        private IHoldInteractable heldTarget;

        private void Awake()
        {
            self = GetComponent<PlayerController>();
            relay = GetComponent<IInteractionRelay>();
        }

        private void Update()
        {
            // Подсветка — чисто локальная: это UI, синхронизировать её не надо.
            CurrentInteractable = FindNearest();

            if (inputReader == null)
            {
                return;
            }

            UpdateHold();

            if (!inputReader.InteractPressed)
            {
                return;
            }

            inputReader.ConsumeInteract();

            if (CurrentInteractable == null || !CurrentInteractable.CanInteract(self))
            {
                return;
            }

            // Удерживаемые интерактивы разовым нажатием не дёргаем: одно нажатие
            // означало бы сразу и «сработай», и «начни держать».
            if (CurrentInteractable is IHoldInteractable)
            {
                return;
            }

            SendInteraction(CurrentInteractable);
        }

        /// <summary>
        /// Начало и конец удержания. Отпускание отрабатывается всегда, даже
        /// если игрок успел отойти или цель перестала быть доступной: иначе
        /// объект остался бы «зажатым» навсегда.
        /// </summary>
        private void UpdateHold()
        {
            bool held = inputReader.InteractHeld;

            if (heldTarget != null && !held)
            {
                heldTarget.HoldChanged(self, false);
                heldTarget = null;
                return;
            }

            if (heldTarget != null || !held)
            {
                return;
            }

            if (CurrentInteractable is IHoldInteractable candidate && candidate.CanInteract(self))
            {
                heldTarget = candidate;
                heldTarget.HoldChanged(self, true);
            }
        }

        private void OnDisable()
        {
            if (heldTarget != null)
            {
                heldTarget.HoldChanged(self, false);
                heldTarget = null;
            }
        }

        /// <summary>
        /// Намерение уходит серверу; если сети нет — исполняем на месте.
        /// </summary>
        private void SendInteraction(IInteractable target)
        {
            if (target is not Component targetComponent)
            {
                return;
            }

            // Локальное взаимодействие серверу не адресуют: у таких объектов нет
            // NetworkObject, и мост отклонил бы намерение целиком (см. ILocalInteraction).
            if (target is ILocalInteraction)
            {
                ExecuteInteraction(targetComponent.gameObject);
                return;
            }

            if (relay != null && relay.TryRelayInteract(targetComponent.gameObject))
            {
                return;
            }

            ExecuteInteraction(targetComponent.gameObject);
        }

        /// <summary>
        /// Единственная точка исполнения взаимодействия. По сети её зовёт сервер,
        /// получив намерение от владельца, поэтому все проверки — здесь, а не у клиента:
        /// клиенту верить нельзя ни в дистанции, ни в доступности цели.
        /// </summary>
        public void ExecuteInteraction(GameObject targetObject)
        {
            if (targetObject == null)
            {
                return;
            }

            IInteractable target = ResolveInteractable(targetObject);
            if (target == null)
            {
                return;
            }

            if (!IsWithinReach(targetObject))
            {
                Debug.LogWarning($"⛔ [{name}] Взаимодействие отклонено: цель '{targetObject.name}' вне радиуса", this);
                return;
            }

            if (!target.CanInteract(self))
            {
                return;
            }

            target.Interact(self);
        }

        /// <summary>
        /// Локальный путь приходит с объекта самого интерактива, серверный —
        /// с объекта его <c>NetworkObject</c>, поэтому ищем в обе стороны.
        /// </summary>
        private static IInteractable ResolveInteractable(GameObject targetObject)
        {
            IInteractable target = targetObject.GetComponentInParent<IInteractable>();
            return target ?? targetObject.GetComponentInChildren<IInteractable>();
        }

        private bool IsWithinReach(GameObject targetObject)
        {
            float maxDistance = interactRadius * ServerReachTolerance;
            return (targetObject.transform.position - transform.position).sqrMagnitude
                   <= maxDistance * maxDistance;
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
