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
        [Tooltip("Приседание без клипа: модель сжимается по высоте вслед за капсулой. Снять, когда появится анимация приседания")]
        [SerializeField] private bool squashVisualOnCrouch = true;

        private static readonly int SpeedParameterHash = Animator.StringToHash("Speed");
        private static readonly int CrouchParameterHash = Animator.StringToHash("Crouch");
        private static readonly int JumpParameterHash = Animator.StringToHash("Jump");
        private static readonly int PunchParameterHash = Animator.StringToHash("Punch");
        private static readonly int KnockdownFrontHash = Animator.StringToHash("KnockdownFront");
        private static readonly int KnockdownBackHash = Animator.StringToHash("KnockdownBack");

        private PlayerPushAbility punchAbility;
        private CapsuleCollider capsule;
        private Vector3 visualBaseScale = Vector3.one;
        private float standingHeight = 1f;

        private void Awake()
        {
            punchAbility = GetComponent<PlayerPushAbility>();
            capsule = GetComponent<CapsuleCollider>();

            if (visualRoot != null)
            {
                visualBaseScale = visualRoot.localScale;
            }

            if (capsule != null)
            {
                standingHeight = Mathf.Max(capsule.height, capsule.radius * 2f);
            }
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
                animator.SetBool(CrouchParameterHash, motor.IsCrouched);
            }

            UpdateCrouchSquash();
        }

        /// <summary>
        /// Клипа приседания среди импортированных анимаций нет, поэтому на каркасе
        /// модель просто сжимается вслед за капсулой. Без этого присед виден только
        /// по гизмо коллайдера, и на плейтесте механику невозможно оценить глазами.
        /// В арт-фазе флаг снимается, и приседание отыгрывает настоящий клип.
        /// </summary>
        private void UpdateCrouchSquash()
        {
            if (!squashVisualOnCrouch || visualRoot == null || capsule == null || standingHeight <= 0f)
            {
                return;
            }

            float ratio = Mathf.Clamp(capsule.height / standingHeight, 0.1f, 1f);
            visualRoot.localScale = new Vector3(visualBaseScale.x, visualBaseScale.y * ratio, visualBaseScale.z);
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
