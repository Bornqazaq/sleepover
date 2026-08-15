using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Player;

namespace Igruha.Core.Items
{
    /// <summary>
    /// Подбираемый и бросаемый предмет (Переноска, Duck Hunt, подушка тай-брейка).
    /// Подбор — через Interact, бросок — кнопкой толчка у носителя.
    ///
    /// Кто несёт предмет — состояние, а не событие, поэтому живёт в
    /// <see cref="holderObjectId"/>: оно же защищает от двойного взятия и оно же
    /// догоняет позднее подключившегося клиента.
    ///
    /// Без сети (одиночный тест сцены) объект работает по-старому: сетевые ветки
    /// выключаются проверкой <c>IsSpawned</c>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PickupItem : NetworkBehaviour, IInteractable
    {
        /// <summary>Предмет свободен. Идентификаторы сетевых объектов начинаются с единицы.</summary>
        private const ulong NoHolder = 0UL;

        [SerializeField] private string itemName = "Предмет";

        private readonly NetworkVariable<ulong> holderObjectId = new NetworkVariable<ulong>(NoHolder);

        private Rigidbody body;
        private Collider[] colliders;
        private NetworkTransform networkTransform;
        private PlayerCarryAbility holder;

        public bool IsHeld => IsSpawned ? holderObjectId.Value != NoHolder : holder != null;
        public string InteractionPrompt => $"Подобрать: {itemName}";

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            colliders = GetComponentsInChildren<Collider>();
            networkTransform = GetComponent<NetworkTransform>();

        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            holderObjectId.OnValueChanged += OnHolderChanged;

            // Подключились в середине раунда — предмет уже может быть у кого-то в руках.
            ApplyHolder(holderObjectId.Value);
        }

        public override void OnNetworkDespawn()
        {
            holderObjectId.OnValueChanged -= OnHolderChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || holderObjectId.Value == NoHolder)
            {
                return;
            }

            // Носитель вышел — предмет должен упасть там, где был, а не пропасть.
            if (holder == null || ResolveHolder(holderObjectId.Value) == null)
            {
                ServerRelease(Vector3.zero);
            }
        }

        public bool CanInteract(PlayerController player) => !IsHeld;

        /// <summary>
        /// По сети вызывается только на сервере — намерение приводит сюда
        /// <c>PlayerInteractor.ExecuteInteraction</c> (2.20).
        /// </summary>
        public void Interact(PlayerController player)
        {
            if (player.TryGetComponent(out PlayerCarryAbility carry))
            {
                carry.TryPickup(this);
            }
        }

        /// <summary>Вызывается <see cref="PlayerCarryAbility"/> у авторитета.</summary>
        public void OnPickedUp(PlayerCarryAbility newHolder, Transform anchor)
        {
            if (!IsSpawned)
            {
                // Сети нет — состояние держим прямо здесь.
                holder = newHolder;
                SnapToAnchor();
                SetPhysicsHeld(true);
                return;
            }

            // Занять предмет вправе только сервер: иначе двое возьмут его одновременно.
            if (!IsServer || holderObjectId.Value != NoHolder)
            {
                return;
            }

            var holderObject = newHolder.GetComponent<NetworkObject>();
            if (holderObject == null || !holderObject.IsSpawned)
            {
                Debug.LogWarning($"{name}: у носителя нет заспавненного NetworkObject — предмет не выдан", this);
                return;
            }

            // Само состояние разъедется всем, а с ним и привязка к руке.
            holderObjectId.Value = holderObject.NetworkObjectId;
        }

        public void OnThrown(Vector3 impulse)
        {
            if (!IsSpawned)
            {
                holder?.OnItemLost(this);
                holder = null;
                SetPhysicsHeld(false);
                body.AddForce(impulse, ForceMode.Impulse);
                return;
            }

            if (!IsServer)
            {
                return;
            }

            ServerRelease(impulse);
        }

        /// <summary>Сервер: отпустить предмет и придать ему импульс.</summary>
        private void ServerRelease(Vector3 impulse)
        {
            holderObjectId.Value = NoHolder;

            // Полёт считает сервер, остальные видят его через NetworkTransform.
            body.AddForce(impulse, ForceMode.Impulse);
        }

        private void OnHolderChanged(ulong previous, ulong current) => ApplyHolder(current);

        /// <summary>
        /// Применить состояние на этой машине. Привязку к руке каждый делает сам
        /// по идентификатору носителя: сетевое перепривязывание родителя в NGO
        /// работает по NetworkObject, а предмет должен висеть на якоре руки,
        /// то есть на дочернем трансформе.
        /// </summary>
        private void ApplyHolder(ulong holderId)
        {
            if (holderId == NoHolder)
            {
                holder?.OnItemLost(this);
                holder = null;
                SetPhysicsHeld(false);
                Debug.Log($"🎒 [{name}] свободен");
                return;
            }

            PlayerCarryAbility newHolder = ResolveHolder(holderId);
            if (newHolder == null)
            {
                return;
            }

            holder = newHolder;
            holder.OnItemTaken(this);
            SnapToAnchor();
            SetPhysicsHeld(true);
            Debug.Log($"🎒 [{name}] в руках у {holder.name} (объект {holderId})");
        }

        private static PlayerCarryAbility ResolveHolder(ulong holderId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.SpawnManager == null)
            {
                return null;
            }

            return manager.SpawnManager.SpawnedObjects.TryGetValue(holderId, out NetworkObject holderObject)
                ? holderObject.GetComponent<PlayerCarryAbility>()
                : null;
        }

        /// <summary>
        /// Предмет не становится дочерним объектом носителя, а следует за якорем.
        /// Родителем нельзя по двум причинам: NGO запрещает вешать сетевой объект
        /// под обычный трансформ (а якорь руки — именно такой), и при выходе
        /// носителя предмет уничтожился бы вместе с ним, вместо того чтобы упасть.
        /// </summary>
        private void LateUpdate()
        {
            if (holder == null || !IsHeld)
            {
                return;
            }

            SnapToAnchor();
        }

        private void SnapToAnchor()
        {
            Transform anchor = holder != null ? holder.HoldAnchor : null;
            if (anchor == null)
            {
                return;
            }

            transform.SetPositionAndRotation(anchor.position, anchor.rotation);
        }

        /// <summary>
        /// В руках предмет держит привязка к якорю, а не физика и не сеть:
        /// пока он несётся, NetworkTransform выключен, иначе он дёргал бы
        /// предмет между позицией сервера и рукой владельца.
        /// </summary>
        private void SetPhysicsHeld(bool held)
        {
            // Свободный предмет летает под физикой сервера; у остальных его
            // ведёт NetworkTransform, поэтому своя физика им только мешает.
            body.isKinematic = held || (IsSpawned && !IsServer);

            if (!held)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            SetCollidersEnabled(!held);

            if (networkTransform != null)
            {
                networkTransform.enabled = !held;
            }
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
