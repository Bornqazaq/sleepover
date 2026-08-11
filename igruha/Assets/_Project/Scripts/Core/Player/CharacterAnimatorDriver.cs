using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Гонит параметры Animator по состоянию мотора. Цепочки «падение → подъём»
    /// собраны внутри Animator Controller (переход по exit time), поэтому здесь
    /// дёргается только триггер падения — нужного направления.
    /// </summary>
    public sealed class CharacterAnimatorDriver : MonoBehaviour
    {
        [SerializeField] private PlayerController motor;
        [SerializeField] private Animator animator;
        [Tooltip("Рут визуальной модели (ребёнок с Animator)")]
        [SerializeField] private Transform visualRoot;

        private static readonly int SpeedParameterHash = Animator.StringToHash("Speed");
        private static readonly int JumpParameterHash = Animator.StringToHash("Jump");
        private static readonly int PunchParameterHash = Animator.StringToHash("Punch");
        private static readonly int KnockdownFrontHash = Animator.StringToHash("KnockdownFront");
        private static readonly int KnockdownBackHash = Animator.StringToHash("KnockdownBack");

        private PlayerPushAbility punchAbility;

        private void Awake()
        {
            punchAbility = GetComponent<PlayerPushAbility>();
        }

        private void OnEnable()
        {
            if (motor != null)
            {
                motor.Jumped += OnJumped;
                motor.KnockdownStarted += OnKnockdownStarted;
            }

            if (punchAbility != null)
            {
                punchAbility.PunchStarted += OnPunchStarted;
            }
        }

        private void OnDisable()
        {
            if (motor != null)
            {
                motor.Jumped -= OnJumped;
                motor.KnockdownStarted -= OnKnockdownStarted;
            }

            if (punchAbility != null)
            {
                punchAbility.PunchStarted -= OnPunchStarted;
            }
        }

        private void Update()
        {
            if (animator != null && motor != null)
            {
                animator.SetFloat(SpeedParameterHash, motor.NormalizedSpeed);
            }
        }

        private void OnJumped()
        {
            if (animator != null)
            {
                animator.SetTrigger(JumpParameterHash);
            }
        }

        private void OnPunchStarted()
        {
            if (animator != null)
            {
                animator.SetTrigger(PunchParameterHash);
            }
        }

        private void OnKnockdownStarted(KnockdownType type)
        {
            if (animator == null)
            {
                return;
            }

            animator.SetTrigger(type == KnockdownType.FlyBack ? KnockdownFrontHash : KnockdownBackHash);
        }
    }
}
