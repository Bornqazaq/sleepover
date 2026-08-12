using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Networking
{
    /// <summary>
    /// Сетевая обёртка для PlayerController.
    /// Модель по CLAUDE.md 3:
    /// - Владелец обрабатывает ввод и двигает себя локально (ClientNetworkTransform)
    /// - Толчки идут через сервер: он валидирует запрос и назначает силу
    /// - Применяет толчок владелец цели, иначе результат будет перетёрт
    /// </summary>
    public sealed class NetworkPlayerController : NetworkBehaviour, IPushRelay
    {
        private PlayerController playerController;
        private NetworkTransform networkTransform;
        private CharacterAnimatorDriver animatorDriver;
        private PlayerInputReader inputReader;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            networkTransform = GetComponent<NetworkTransform>();
            animatorDriver = GetComponent<CharacterAnimatorDriver>();
            inputReader = GetComponent<PlayerInputReader>();
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
            // достаточно для мотора, удара и взаимодействия
            if (inputReader != null)
            {
                inputReader.enabled = false;
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

        // ========== ИМПУЛЬСЫ ОТ МИРА (пружины, взрывы, ловушки) ==========

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
    }
}
