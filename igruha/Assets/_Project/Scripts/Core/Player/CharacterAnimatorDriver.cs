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
        private static readonly int EmoteHash = Animator.StringToHash("Emote");
        private static readonly int EmotePlayHash = Animator.StringToHash("EmotePlay");
        private static readonly int EmoteStopHash = Animator.StringToHash("EmoteStop");
        private static readonly int FireParameterHash = Animator.StringToHash("Fire");

        /// <summary>
        /// Имя слоя ружья в контроллере. Слой отдельный и лежит поверх основного:
        /// пока его вес ноль, персонаж анимируется ровно как раньше, а роль
        /// со стойкой и выстрелом не задевает ни одного замороженного состояния.
        /// </summary>
        private const string RifleLayerName = "Rifle";

        /// <summary>
        /// Имя слоя сидячей позы. Слой тоже отдельный и тоже поверх основного:
        /// пока его вес ноль, персонаж анимируется ровно как раньше. Слоёв два,
        /// но одновременно они не включаются никогда — ружьё живёт в Duck Hunt,
        /// посадка в «Верю / не верю», сцены разные.
        /// </summary>
        private const string SitLayerName = "Sit";

        /// <summary>Слой ружья не найден — у этого персонажа его просто нет.</summary>
        private const int NoLayer = -1;

        private PlayerPushAbility punchAbility;
        private PlayerEmoteAbility emoteAbility;
        private CapsuleCollider capsule;

        /// <summary>Индекс слоя ружья. Считается один раз: поиск по имени идёт строкой.</summary>
        private int rifleLayer = NoLayer;
        private bool rifleLayerResolved;

        /// <summary>Индекс слоя посадки. Считается так же один раз.</summary>
        private int sitLayer = NoLayer;
        private bool sitLayerResolved;
        private Vector3 visualBaseScale = Vector3.one;
        private float standingHeight = 1f;

        private void Awake()
        {
            punchAbility = GetComponent<PlayerPushAbility>();
            emoteAbility = GetComponent<PlayerEmoteAbility>();
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

            if (emoteAbility != null)
            {
                emoteAbility.EmotePlayed += OnEmotePlayed;
                emoteAbility.EmoteStopped += OnEmoteStopped;
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

            if (emoteAbility != null)
            {
                emoteAbility.EmotePlayed -= OnEmotePlayed;
                emoteAbility.EmoteStopped -= OnEmoteStopped;
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
        /// <summary>
        /// Пересчитать сжатие модели под текущую капсулу. Публичный вход нужен
        /// чужим копиям: у них этот компонент выключен (иначе он затирал бы
        /// параметры, пришедшие через NetworkAnimator), но присед показать надо —
        /// зовёт NetworkPlayerController, получив состояние по сети.
        /// </summary>
        public void ApplyCrouchVisual() => UpdateCrouchSquash();

        private void UpdateCrouchSquash()
        {
            if (!squashVisualOnCrouch || visualRoot == null || capsule == null || standingHeight <= 0f)
            {
                return;
            }

            float ratio = Mathf.Clamp(capsule.height / standingHeight, 0.1f, 1f);
            visualRoot.localScale = new Vector3(visualBaseScale.x, visualBaseScale.y * ratio, visualBaseScale.z);
        }

        /// <summary>
        /// Взять или убрать ружьё. Это вес слоя, а не параметр: слой либо
        /// перекрывает позу целиком, либо не существует для персонажа вовсе.
        ///
        /// Публичный вход нужен так же, как <see cref="ApplyCrouchVisual"/>:
        /// у чужих копий этот компонент выключен, чтобы не затирать параметры
        /// из NetworkAnimator, — но стойку Охотника показать надо, и роль
        /// известна на каждой машине.
        /// </summary>
        public void SetRifleAiming(bool aiming)
        {
            int layer = ResolveRifleLayer();
            if (layer == NoLayer)
            {
                return;
            }

            animator.SetLayerWeight(layer, aiming ? 1f : 0f);

            // Накопленный триггер снимаем: иначе, взяв ружьё второй раз за матч,
            // Охотник первым же кадром отыгрывает выстрел, которого не делал.
            if (!aiming)
            {
                animator.ResetTrigger(FireParameterHash);
            }
        }

        /// <summary>
        /// Отыграть выстрел. Зовётся на каждой машине по состоявшемуся выстрелу,
        /// а не по нажатию: на клике в кулдаун или по пустой обойме выстрела нет,
        /// и дёргать ствол не на что.
        /// </summary>
        public void PlayFire()
        {
            if (ResolveRifleLayer() == NoLayer)
            {
                return;
            }

            animator.SetTrigger(FireParameterHash);
        }

        /// <summary>
        /// Сесть или встать. Это вес слоя, а не параметр: слой либо перекрывает
        /// позу целиком, либо не существует для персонажа вовсе.
        ///
        /// Публичный вход нужен так же, как <see cref="SetRifleAiming"/>:
        /// у чужих копий этот компонент выключен, чтобы не затирать параметры
        /// из NetworkAnimator, — но посадку показать надо, и кто сидит,
        /// известно на каждой машине.
        /// </summary>
        public void SetSitting(bool sitting)
        {
            int layer = ResolveSitLayer();
            if (layer == NoLayer)
            {
                return;
            }

            animator.SetLayerWeight(layer, sitting ? 1f : 0f);
        }

        private int ResolveSitLayer()
        {
            if (sitLayerResolved)
            {
                return sitLayer;
            }

            sitLayerResolved = true;
            sitLayer = animator != null ? animator.GetLayerIndex(SitLayerName) : NoLayer;
            return sitLayer;
        }

        private int ResolveRifleLayer()
        {
            if (rifleLayerResolved)
            {
                return rifleLayer;
            }

            rifleLayerResolved = true;
            rifleLayer = animator != null ? animator.GetLayerIndex(RifleLayerName) : NoLayer;
            return rifleLayer;
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

        /// <summary>
        /// Танец крутится в лупе сам: номер эмоции — состояние (Int), а вход в него —
        /// разовое событие (Trigger). Одним только Int обойтись нельзя: переход
        /// из AnyState срывался бы обратно в танец после каждого удара и прыжка,
        /// пока игрок не сменит эмоцию.
        /// </summary>
        private void OnEmotePlayed(int emoteNumber)
        {
            if (animator == null)
            {
                return;
            }

            animator.SetInteger(EmoteHash, emoteNumber);
            animator.SetTrigger(EmotePlayHash);
        }

        private void OnEmoteStopped()
        {
            if (animator == null)
            {
                return;
            }

            animator.SetInteger(EmoteHash, PlayerEmoteAbility.NoEmote);
            animator.ResetTrigger(EmotePlayHash);
            animator.SetTrigger(EmoteStopHash);
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
