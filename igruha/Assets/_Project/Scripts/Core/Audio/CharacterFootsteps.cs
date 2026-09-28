using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Audio
{
    /// <summary>
    /// Шаги следуют двум опорам цикла Run/CrouchWalk настоящего Animator.
    /// Каждый персонаж сохраняет частоту своего клипа. Позиция нужна только
    /// для отсечения покоя/телепортов; поверхности и локальные копии общие.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public sealed class CharacterFootsteps : MonoBehaviour
    {
        private const float CrouchVolume = 0.4f;
        private const float MinSpeed = 0.6f;
        private const float TeleportDistance = 2.5f;
        private const int StepsPerCycle = 2;
        private static readonly int RunHash = Animator.StringToHash("Run");
        private static readonly int CrouchWalkHash = Animator.StringToHash("CrouchWalk");

        [Tooltip("Проигрыватель звука этого персонажа")]
        [SerializeField] private MinigameAudioPlayer audioPlayer;

        [Tooltip("Контроллер персонажа. Не задан — берётся с этого же объекта")]
        [SerializeField] private PlayerController controller;

        [Tooltip("Чем звучит пол, на котором нет метки SurfaceAudio")]
        [SerializeField] private SurfaceKind defaultSurface = SurfaceKind.Concrete;

        /// <summary>Слот шага в обход поверхности — ставит мини-игра: шаг заражённого, шаг по воде.</summary>
        private string slotOverride;
        private float rangeOverride;
        public string SlotOverride => slotOverride;
        public float RangeOverride => rangeOverride;
        public void SetRangeOverride(float metres) => rangeOverride = Mathf.Max(0f, metres);

        private Vector3 lastPosition;
        private Animator animator;
        private CapsuleCollider capsule;
        private int previousState;
        private int previousStep;
        private bool trackingCycle;

        /// <summary>Разобранная поверхность прошлого шага. Пол под ногами меняется редко, а искать метку каждый шаг незачем.</summary>
        private Collider cachedGround;
        private string cachedSlot;

        /// <summary>
        /// Подменить слот шага на свой — или вернуть обычный, передав пустую строку.
        /// Нужно мини-играм, где шаг несёт смысл: заражённый слышен со спины,
        /// и это предупреждение, а не украшение.
        /// </summary>
        public void SetSlotOverride(string slotId) => slotOverride = slotId;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<PlayerController>();
            animator = GetComponentInChildren<Animator>(true);
            capsule = GetComponent<CapsuleCollider>();
            lastPosition = transform.position;
        }

        private void OnEnable()
        {
            lastPosition = transform.position;
            trackingCycle = false;
        }

        private void LateUpdate()
        {
            if (controller == null || audioPlayer == null || animator == null) return;

            Vector3 position = controller.Position;
            Vector3 delta = position - lastPosition;
            delta.y = 0f;
            lastPosition = position;
            float distance = delta.magnitude;
            if (controller.IsKnockedDown ||
                distance > TeleportDistance || distance < MinSpeed * Time.deltaTime)
            {
                trackingCycle = false;
                return;
            }

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (animator.IsInTransition(0)) state = animator.GetNextAnimatorStateInfo(0);
            int hash = state.shortNameHash;
            if (hash != RunHash && hash != CrouchWalkHash)
            {
                trackingCycle = false;
                return;
            }

            if (!TryGetGround(out Collider ground))
            {
                trackingCycle = false;
                return;
            }

            int step = Mathf.FloorToInt(state.normalizedTime * StepsPerCycle);
            bool play = trackingCycle && previousState == hash && step > previousStep;
            previousState = hash;
            previousStep = step;
            trackingCycle = true;
            // One sound at most after a long frame; never replay missed steps in a burst.
            if (play)
                audioPlayer.PlayAt(ResolveSlot(ground), position,
                    controller.IsCrouched ? CrouchVolume : 1f, rangeOverride);
        }

        private bool TryGetGround(out Collider ground)
        {
            if (controller.enabled)
            {
                ground = controller.GroundCollider;
                return controller.IsGrounded;
            }

            // Remote motors do not update IsGrounded. Sample their visible position
            // with the same capsule and mask instead of trusting a stale local flag.
            ground = null;
            if (capsule == null || !capsule.enabled || controller.Config == null) return false;
            float distance = capsule.bounds.extents.y - capsule.radius + controller.Config.GroundCheckDistance;
            if (!Physics.SphereCast(capsule.bounds.center, capsule.radius * 0.95f,
                    Vector3.down, out RaycastHit hit, distance, controller.GroundLayers, QueryTriggerInteraction.Ignore))
                return false;
            ground = hit.collider;
            return true;
        }

        private string ResolveSlot(Collider ground)
        {
            if (!string.IsNullOrEmpty(slotOverride)) return slotOverride;

            if (ground != cachedGround || cachedSlot == null)
            {
                cachedGround = ground;
                cachedSlot = SurfaceAudio.ResolveStepSlot(ground, defaultSurface);
            }

            return cachedSlot;
        }
    }
}
