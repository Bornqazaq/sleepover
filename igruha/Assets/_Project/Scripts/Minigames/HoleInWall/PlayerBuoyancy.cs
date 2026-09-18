using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Сопротивление воды при провале: гасит удар и движение, но оставляет
    /// гравитацию, чтобы ноги опирались на дно, а не на невидимую поверхность.
    /// Компонент временный; префабы, капсулы и анимации игроков не меняются.
    /// Каждая машина применяет силы только к своему аватару.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerBuoyancy : MonoBehaviour
    {
        /// <summary>Торможение смягчает контакт с дном после быстрого входа в воду.</summary>
        private const float VerticalDamping = 8f;
        private const float HorizontalDamping = 9f;
        /// <summary>За такт 50 Гц импульс меньше порога нокдауна 5 м/с.</summary>
        private const float MaxAcceleration = 160f;
        /// <summary>Короткое ожидание возврата: вода ограничивает движение по дну.</summary>
        private const float SwimSpeed = 0.15f;

        /// <summary>Разгон в воде — доля обычного. Вода не даёт стартовать рывком.</summary>
        private const float SwimAcceleration = 0.35f;

        /// <summary>Торможение в воде — доля обычного. Ниже единицы: в воде не встают как вкопанные.</summary>
        private const float SwimDeceleration = 0.5f;

        /// <summary>Рост, которым считается погружение, если капсулы почему-то нет, м.</summary>
        private const float FallbackHeight = 1.8f;

        private static readonly List<PlayerBuoyancy> participants = new List<PlayerBuoyancy>(8);
        private PlayerController motor;
        private NetworkObject body;
        private Rigidbody physics;
        private CapsuleCollider capsule;
        private Transform head, leftFoot, rightFoot, leftHand, rightHand;
        private HoleInWallConfig config;
        private HoleInWallPoolContacts contacts;

        /// <summary>Человек сейчас в воде. По ней же ставится и снимается вязкость.</summary>
        private bool submerged;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            body = GetComponent<NetworkObject>();
            physics = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            var animator = GetComponentInChildren<Animator>();
            float radius = capsule != null ? capsule.radius * Mathf.Abs(transform.lossyScale.x) : .3f;
            contacts = new HoleInWallPoolContacts(animator, radius);
            if (animator != null && animator.isHuman)
            {
                head = animator.GetBoneTransform(HumanBodyBones.Head);
                leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            }
        }

        private void OnDisable() => Release();

        /// <summary>Указать воде её уровень. Зовётся сразу после навешивания компонента.</summary>
        public void Configure(HoleInWallConfig gameConfig, Collider[] supports)
        {
            config = gameConfig;
            if (!participants.Contains(this)) participants.Add(this);
            contacts.Configure(supports);
        }

        /// <summary>
        /// Отпустить персонажа: раунд кончился. Вязкость снимается обязательно —
        /// персонаж переезжает в хаб живым, и незакрытый потолок скорости уехал
        /// бы вместе с ним.
        /// </summary>
        public void Release()
        {
            participants.Remove(this);
            config = null;
            contacts?.Reset();
            SetSubmerged(false);
        }

        private void FixedUpdate()
        {
            if (config == null || physics == null || motor == null || !motor.enabled || physics.isKinematic)
            {
                contacts?.Reset();
                return;
            }

            // 🔴 Своего персонажа ведёт только его машина. Чужую копию двигает
            // сетевой транспорт, и вторая сила по ней — это дрожь на всех
            // экранах, кроме одного. Тот же приём, что у воронки и троса.
            if (!WorldAuthority.DrivenHere(body))
            {
                return;
            }

            if (physics.detectCollisions)
                foreach (var peer in participants)
                    if (peer != this && peer.config == config && peer.physics != null &&
                        peer.physics.detectCollisions && peer.motor != null && peer.motor.enabled &&
                        (motor.IsKnockedDown || peer.motor.IsKnockedDown))
                        contacts.ResolvePeer(physics, peer.contacts, peer.physics,
                            body != null && body.IsSpawned && peer.body != null && peer.body.IsSpawned
                                ? body.NetworkObjectId < peer.body.NetworkObjectId
                                : GetInstanceID() < peer.GetInstanceID());

            float height = BodyHeight;
            float feetY = motor.Position.y;

            // Доля тела под водой. Ноги выше уровня — воды нет вовсе.
            float share = Mathf.Clamp01((config.WaterSurfaceY - feetY) / height);
            SetSubmerged(share > 0f);

            if (share <= 0f)
            {
                contacts.Reset();
                return;
            }

            contacts.Resolve(physics);

            Vector3 velocity = physics.linearVelocity;

            // Только сопротивление: при нулевой скорости нет силы вверх.
            // Прежнее выталкивание удерживало стоячую модель над дном.
            float vertical = -velocity.y * VerticalDamping * share;
            // The frozen fall clip extends below the upright capsule. Cushion the
            // actual head before the pool floor; neither capsule nor clip is changed.
            if (head != null && motor.IsKnockedDown)
            {
                float headRadius = capsule != null ? capsule.radius * Mathf.Abs(transform.lossyScale.x) : .3f;
                float lowest = head.position.y - headRadius;
                if (leftFoot != null) lowest = Mathf.Min(lowest, leftFoot.position.y - .1f);
                if (rightFoot != null) lowest = Mathf.Min(lowest, rightFoot.position.y - .1f);
                if (leftHand != null) lowest = Mathf.Min(lowest, leftHand.position.y - .06f);
                if (rightHand != null) lowest = Mathf.Min(lowest, rightHand.position.y - .06f);
                float clearance = config.PoolBottomY + .08f - lowest;
                if (clearance > 0) vertical = Mathf.Max(vertical, clearance * 180f - velocity.y * 22f);
                if (lowest < config.PoolBottomY)
                    physics.position += Vector3.up * (config.PoolBottomY - lowest);
            }
            vertical = Mathf.Clamp(vertical, -MaxAcceleration, MaxAcceleration);

            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 drag = Vector3.ClampMagnitude(-horizontal * HorizontalDamping, MaxAcceleration);

            // Долей погружения умножается и торможение: по колено в воде она
            // мешать не должна.
            Vector3 acceleration = Vector3.up * vertical + drag * share;

            motor.ApplyImpulse(acceleration * (Time.fixedDeltaTime * physics.mass));
        }

        /// <summary>
        /// Вязкость воды: потолок скорости и ленивый разгон. Ставится и
        /// снимается по переходу, а не каждый такт — источник у обоих
        /// множителей общий, и лишние присвоения затирали бы чужие.
        /// </summary>
        private void SetSubmerged(bool value)
        {
            if (submerged == value)
            {
                return;
            }

            submerged = value;

            if (value)
            {
                motor.ApplySpeedCap(this, SwimSpeed);
                motor.ApplySurface(this, SwimAcceleration, SwimDeceleration);
                return;
            }

            motor.ClearSpeedCap(this);
            motor.ClearSurface(this);
        }

        /// <summary>
        /// Рост капсулы нужен для постепенного включения сопротивления воды.
        /// </summary>
        private float BodyHeight
        {
            get
            {
                if (capsule == null)
                {
                    return FallbackHeight;
                }

                float scale = Mathf.Abs(transform.lossyScale.y);
                return Mathf.Max(0.1f, capsule.height * scale);
            }
        }
    }
}
