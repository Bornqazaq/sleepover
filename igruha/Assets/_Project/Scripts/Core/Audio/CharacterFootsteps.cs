using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Audio
{
    /// <summary>
    /// Шаги следуют постановке стоп в Run/CrouchWalk настоящего Animator.
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
        private const float MovementGraceSeconds = 0.12f;
        private const float MinimumFootSwing = 0.03f;
        private const float PlantTravel = 0.005f;
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
        private Transform leftFoot;
        private Transform rightFoot;
        private Renderer[] renderers;
        private FootPlant leftPlant;
        private FootPlant rightPlant;
        private float lastMovedAt = float.NegativeInfinity;
        private int previousState;
        private int previousStep;
        private bool trackingCycle;
        private bool sampledFeet;

        /// <summary>After a forward swing, the foot starts moving backwards
        /// relative to the body when it plants. Hysteresis ignores pose noise.
        /// Unlike a fixed two-beat cycle this also follows clips with four steps.</summary>
        private struct FootPlant
        {
            private float rear;
            private float front;
            private bool swinging;

            public void Reset(float position)
            {
                rear = front = position;
                swinging = false;
            }

            public bool Sample(float position)
            {
                if (!swinging)
                {
                    rear = Mathf.Min(rear, position);
                    if (position - rear >= MinimumFootSwing)
                    {
                        swinging = true;
                        front = position;
                    }
                    return false;
                }
                front = Mathf.Max(front, position);
                if (front - position < PlantTravel) return false;
                swinging = false;
                rear = position;
                return true;
            }
        }

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
            renderers = animator != null ? animator.GetComponentsInChildren<Renderer>(true) : null;
            capsule = GetComponent<CapsuleCollider>();
            if (animator != null && animator.isHuman)
            {
                leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            }
            lastPosition = transform.position;
        }

        private void OnEnable()
        {
            lastPosition = transform.position;
            trackingCycle = false;
            lastMovedAt = float.NegativeInfinity;
        }

        private void LateUpdate()
        {
            if (controller == null || audioPlayer == null || animator == null) return;

            // Rigidbody.position changes only on physics ticks. Reading it in
            // LateUpdate made intervening render frames reset the entire gait.
            Vector3 position = transform.position;
            Vector3 delta = position - lastPosition;
            lastPosition = position;
            if (controller.IsKnockedDown || controller.MovementLocked ||
                delta.sqrMagnitude > TeleportDistance * TeleportDistance)
            {
                trackingCycle = false;
                lastMovedAt = float.NegativeInfinity;
                return;
            }
            delta.y = 0f;
            float minDistance = MinSpeed * Time.deltaTime;
            if (delta.sqrMagnitude > minDistance * minDistance) lastMovedAt = Time.time;
            if (Time.time - lastMovedAt > MovementGraceSeconds)
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

            bool play;
            Vector3 soundPosition = position;
            bool sampleFeet = FeetAreUpdating();
            if (sampleFeet != sampledFeet) trackingCycle = false;
            sampledFeet = sampleFeet;
            if (sampleFeet)
            {
                Transform visual = animator.transform;
                float left = Vector3.Dot(leftFoot.position - visual.position, visual.forward);
                float right = Vector3.Dot(rightFoot.position - visual.position, visual.forward);
                if (!trackingCycle || previousState != hash)
                {
                    leftPlant.Reset(left);
                    rightPlant.Reset(right);
                }
                bool leftContact = leftPlant.Sample(left);
                bool rightContact = rightPlant.Sample(right);
                play = leftContact || rightContact;
                soundPosition = leftContact ? leftFoot.position : rightFoot.position;
            }
            else
            {
                // CullUpdateTransforms still advances the state, but freezes bones
                // outside the camera. Keep the clip's cadence there without
                // changing the Animator's culling or any animation asset.
                int contacts = hash == CrouchWalkHash ? 4 : 2;
                int step = Mathf.FloorToInt(state.normalizedTime * contacts - .5f);
                play = trackingCycle && previousState == hash && step > previousStep;
                previousStep = step;
            }
            previousState = hash;
            trackingCycle = true;
            // One sound at most after a long frame; never replay missed steps in a burst.
            if (play)
                audioPlayer.PlayAt(ResolveSlot(ground), soundPosition,
                    controller.IsCrouched ? CrouchVolume : 1f, rangeOverride);
        }

        private bool FeetAreUpdating()
        {
            if (leftFoot == null || rightFoot == null) return false;
            if (animator.cullingMode == AnimatorCullingMode.AlwaysAnimate) return true;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i].enabled && renderers[i].isVisible) return true;
            return false;
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
