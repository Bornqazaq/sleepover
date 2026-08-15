using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Items;
using Igruha.Core.Player;

namespace Igruha.Networking
{
    /// <summary>
    /// Сетевая обёртка для PlayerController.
    /// Модель по CLAUDE.md 3:
    /// - Владелец обрабатывает ввод и двигает себя локально (ClientNetworkTransform)
    /// - Толчки идут через сервер: он валидирует запрос и назначает силу
    /// - Применяет толчок владелец цели, иначе результат будет перетёрт
    /// - Воздействия мира (ловушки, зоны смерти) решает сервер, применяет владелец
    /// - Взаимодействие с объектами: клиент шлёт намерение, сервер проверяет и исполняет
    /// </summary>
    public sealed class NetworkPlayerController : NetworkBehaviour, IPushRelay, IWorldEffectRelay, IInteractionRelay
    {
        private PlayerController playerController;
        private NetworkTransform networkTransform;
        private CharacterAnimatorDriver animatorDriver;
        private PlayerInputReader inputReader;
        private PlayerInteractor interactor;
        private PlayerCarryAbility carryAbility;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            networkTransform = GetComponent<NetworkTransform>();
            animatorDriver = GetComponent<CharacterAnimatorDriver>();
            inputReader = GetComponent<PlayerInputReader>();
            interactor = GetComponent<PlayerInteractor>();
            carryAbility = GetComponent<PlayerCarryAbility>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (playerController == null)
            {
                Debug.LogError($"{name}: NetworkPlayerController не нашел PlayerController!", this);
                return;
            }

            if (networkTransform == null)
            {
                Debug.LogWarning($"{name}: NetworkPlayerController не нашел NetworkTransform!", this);
            }

            if (!IsOwner)
            {
                DisableLocalControl();
                Debug.Log($"📡 [{name}] Это удалённый персонаж (владелец другого клиента) — синхронизация через NetworkTransform");
            }
            else
            {
                Debug.Log($"🎮 [{name}] Это МОЙ персонаж — ввод активен, позиция будет реплицирована");
            }
        }

        /// <summary>
        /// Заглушить всё, что управляет персонажем локально. Без этого локальный
        /// ввод машины дёргает и чужие копии: они бьют, толкают и подбирают предметы.
        /// </summary>
        private void DisableLocalControl()
        {
            playerController.enabled = false;

            // Ридер гасит все нажатия в OnDisable, поэтому одного выключения
            // достаточно для мотора, удара и взаимодействия. Забираем управление
            // навсегда: мини-игра включает ввод по фазе и иначе разбудила бы копию.
            if (inputReader != null)
            {
                inputReader.RevokeLocalControl();
            }

            // Иначе он перезапишет параметры Animator, пришедшие через NetworkAnimator
            if (animatorDriver != null)
            {
                animatorDriver.enabled = false;
            }
        }

        // ========== ТОЛЧКИ ==========

        /// <summary>
        /// Владелец бьющего просит сервер применить толчок к цели.
        /// </summary>
        public bool TryRelayPush(PlayerController target, Vector3 direction, float force)
        {
            if (!IsSpawned || !IsOwner || target == null)
            {
                return false;
            }

            var targetObject = target.GetComponent<NetworkObject>();
            if (targetObject == null || !targetObject.IsSpawned)
            {
                return false;
            }

            RequestPushRpc(new NetworkObjectReference(targetObject));
            return true;
        }

        /// <summary>
        /// Сервер: проверить запрос и назначить силу.
        /// Направление и силу считает сервер — клиент не может прислать
        /// произвольные значения и зашвырнуть цель за карту.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = true)]
        private void RequestPushRpc(NetworkObjectReference targetReference)
        {
            if (!targetReference.TryGet(out NetworkObject targetObject))
            {
                return;
            }

            var targetController = targetObject.GetComponent<NetworkPlayerController>();
            if (targetController == null || targetController == this)
            {
                return;
            }

            CharacterConfig config = playerController != null ? playerController.Config : null;
            if (config == null)
            {
                return;
            }

            Vector3 toTarget = targetObject.transform.position - transform.position;
            toTarget.y = 0f;

            // Запас на задержку: у сервера позиции чуть отстают от машины владельца
            float maxDistance = config.PushRadius * 1.5f;
            if (toTarget.sqrMagnitude > maxDistance * maxDistance || toTarget.sqrMagnitude < 0.0001f)
            {
                Debug.LogWarning($"⛔ [{name}] Толчок отклонён: цель вне радиуса ({toTarget.magnitude:F2} > {maxDistance:F2})");
                return;
            }

            targetController.ServerApplyPush(toTarget, config.PushForce);
        }

        /// <summary>
        /// Сервер поручает владельцу цели применить толчок к себе.
        /// </summary>
        internal void ServerApplyPush(Vector3 direction, float force)
        {
            if (!IsServer)
            {
                return;
            }

            ApplyPushRpc(direction, force);
        }

        /// <summary>
        /// Владелец цели применяет толчок локально — результат разъезжается
        /// по остальным через ClientNetworkTransform.
        /// </summary>
        [Rpc(SendTo.Owner)]
        private void ApplyPushRpc(Vector3 direction, float force)
        {
            if (playerController == null || playerController.IsKnockedDown)
            {
                return;
            }

            playerController.ApplyPush(direction, force);
            Debug.Log($"💥 [{name}] Толчок применён: direction={direction.normalized}, force={force}");
        }

        // ========== ВОЗДЕЙСТВИЯ МИРА (пружины, взрывы, зоны смерти) ==========

        /// <summary>
        /// Пока персонаж не заспавнен, сетевого авторитета не существует и решать
        /// вправе локальная машина — так сцены продолжают работать без сети.
        /// </summary>
        public bool HasAuthority => !IsSpawned || IsServer;

        public bool TryRelayImpulse(Vector3 impulse)
        {
            if (!IsSpawned)
            {
                return false;
            }

            if (IsServer)
            {
                ApplyImpulseRpc(impulse);
            }

            return true;
        }

        public bool TryRelayTeleport(Vector3 position, Quaternion rotation)
        {
            if (!IsSpawned)
            {
                return false;
            }

            if (IsServer)
            {
                TeleportRpc(position, rotation);
            }

            return true;
        }

        /// <summary>
        /// Сервер: отправить импульс персонажу. Вызывать только на сервере —
        /// применит его владелец, у которого авторитет над трансформом.
        /// </summary>
        public void ServerApplyImpulse(Vector3 impulse)
        {
            if (!IsServer)
            {
                Debug.LogWarning($"{name}: ServerApplyImpulse вызван не на сервере — проигнорирован", this);
                return;
            }

            ApplyImpulseRpc(impulse);
        }

        [Rpc(SendTo.Owner)]
        private void ApplyImpulseRpc(Vector3 impulse)
        {
            if (playerController == null)
            {
                return;
            }

            playerController.ApplyImpulse(impulse);
            Debug.Log($"💫 [{name}] Импульс применён: {impulse.magnitude:F2}");
        }

        [Rpc(SendTo.Owner)]
        private void TeleportRpc(Vector3 position, Quaternion rotation)
        {
            if (playerController == null)
            {
                return;
            }

            playerController.TeleportTo(position, rotation);

            // NetworkTransform интерполирует перенос между старой и новой точкой,
            // поэтому без явного Teleport остальные увидят, как персонаж
            // «проезжает» через всю карту вместо мгновенного respawn.
            if (networkTransform != null)
            {
                networkTransform.Teleport(position, rotation, transform.localScale);
            }

            Debug.Log($"♻️ [{name}] Респавн: перенесён в {position}");
        }

        // ========== ВЗАИМОДЕЙСТВИЕ С ОБЪЕКТАМИ (кнопки, двери, предметы) ==========

        /// <summary>
        /// Владелец отправляет серверу намерение «взаимодействую с этим».
        /// Ничего не исполняет сам: исход решает сервер.
        /// </summary>
        public bool TryRelayInteract(GameObject target)
        {
            if (!IsSpawned)
            {
                // Сети нет — пусть Core выполнит взаимодействие локально.
                return false;
            }

            if (!IsOwner || target == null)
            {
                // Чужая копия персонажа: намерение уже отправит её владелец.
                return true;
            }

            var targetObject = target.GetComponentInParent<NetworkObject>();
            if (targetObject == null || !targetObject.IsSpawned)
            {
                // Адресовать объект по сети нечем. Локально выполнить тоже нельзя:
                // у остальных состояние тогда разъедется — поэтому просто отказ.
                Debug.LogWarning($"⛔ [{name}] Взаимодействие с '{target.name}' невозможно: " +
                                 "у объекта нет заспавненного NetworkObject", this);
                return true;
            }

            RequestInteractRpc(new NetworkObjectReference(targetObject));
            return true;
        }

        /// <summary>
        /// Сервер: получить намерение и передать его в единственную точку исполнения.
        /// Дистанцию и доступность цели проверяет <see cref="PlayerInteractor.ExecuteInteraction"/> —
        /// проверки живут рядом с правилами, а не размазаны по сетевому слою.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = true)]
        private void RequestInteractRpc(NetworkObjectReference targetReference)
        {
            if (!targetReference.TryGet(out NetworkObject targetObject))
            {
                return;
            }

            if (interactor == null)
            {
                Debug.LogWarning($"{name}: пришло намерение взаимодействия, но PlayerInteractor не найден", this);
                return;
            }

            interactor.ExecuteInteraction(targetObject.gameObject);
        }

        /// <summary>
        /// Владелец отправляет серверу намерение расстаться с предметом.
        /// Сам ничего не бросает: предмет — общий объект, его судьбу решает сервер.
        /// </summary>
        public bool TryRelayThrow(bool withImpulse)
        {
            if (!IsSpawned)
            {
                return false;
            }

            if (!IsOwner)
            {
                return true;
            }

            RequestThrowRpc(withImpulse);
            return true;
        }

        [Rpc(SendTo.Server, RequireOwnership = true)]
        private void RequestThrowRpc(bool withImpulse)
        {
            if (carryAbility == null)
            {
                return;
            }

            carryAbility.ServerThrow(withImpulse);
        }
    }
}
