using UnityEngine;
using Unity.Netcode;
using Igruha.Core.Player;

namespace Igruha.Networking
{
    /// <summary>
    /// Сетевая версия CharacterAnimatorDriver для синхронизации анимаций через NGO.
    ///
    /// Как это работает:
    /// - Владелец персонажа (IsOwner) вызывает animator.SetFloat/SetTrigger локально
    /// - NetworkAnimator компонент на Player.prefab автоматически реплицирует все вызовы через сеть
    /// - Удалённые клиенты видят синхронизированные анимации
    ///
    /// Требования:
    /// 1. На Player.prefab должен быть компонент NetworkAnimator
    /// 2. CharacterAnimatorDriver должен быть на Player.prefab
    /// 3. Animator должен быть правильно настроен (параметры: Speed, Jump, KnockdownFront, etc.)
    /// </summary>
    public sealed class NetworkCharacterAnimatorDriver : NetworkBehaviour
    {
        [SerializeField] private CharacterAnimatorDriver animatorDriver;
        private PlayerController playerController;
        private Animator animator;

        private static readonly int SpeedParameterHash = Animator.StringToHash("Speed");
        private static readonly int JumpParameterHash = Animator.StringToHash("Jump");
        private static readonly int KnockdownFrontHash = Animator.StringToHash("KnockdownFront");
        private static readonly int KnockdownBackHash = Animator.StringToHash("KnockdownBack");

        private void Awake()
        {
            if (animatorDriver == null)
            {
                animatorDriver = GetComponent<CharacterAnimatorDriver>();
            }

            playerController = GetComponent<PlayerController>();
            animator = GetComponentInChildren<Animator>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Только владелец (IsOwner) может управлять анимациями
            // Удалённые персонажи видят результат синхронизации автоматически через NetworkAnimator
            if (!IsOwner)
            {
                Debug.Log($"📡 [{name}] Удалённый персонаж — анимации синхронизируются через NetworkAnimator");
                return;
            }

            // Подписываемся на события движения для синхронизации анимаций
            if (playerController != null)
            {
                playerController.Jumped += OnJumped;
                playerController.KnockdownStarted += OnKnockdownStarted;
            }

            Debug.Log($"🎬 [{name}] Владелец персонажа — анимации контролируются локально и реплицируются через NetworkAnimator");
        }

        public override void OnNetworkDespawn()
        {
            if (playerController != null)
            {
                playerController.Jumped -= OnJumped;
                playerController.KnockdownStarted -= OnKnockdownStarted;
            }

            base.OnNetworkDespawn();
        }

        private void Update()
        {
            // Синхронизировать Speed параметр
            // NetworkAnimator автоматически реплицирует это значение на других клиентах
            if (IsOwner && animator != null && playerController != null)
            {
                animator.SetFloat(SpeedParameterHash, playerController.NormalizedSpeed);
            }
        }

        private void OnJumped()
        {
            if (animator != null)
            {
                animator.SetTrigger(JumpParameterHash);
                // Trigger автоматически реплицируется через NetworkAnimator
            }
        }

        private void OnKnockdownStarted(KnockdownType type)
        {
            if (animator == null)
                return;

            int triggerHash = type == KnockdownType.FlyBack ? KnockdownFrontHash : KnockdownBackHash;
            animator.SetTrigger(triggerHash);
            // Trigger автоматически реплицируется через NetworkAnimator
        }
    }
}
