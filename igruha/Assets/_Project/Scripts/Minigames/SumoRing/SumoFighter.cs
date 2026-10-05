using Igruha.Core.Audio;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Minigames.SumoRing
{
    /// <summary>Owner applies server-approved horizontal shove after the shared motor; remote copies only animate.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class SumoFighter : MonoBehaviour
    {
        private const float ActionSpeed = .01f;
        private const float BalanceProbeInterval = .06f, BalanceBlendSeconds = .16f;
        private SumoCombat combat;
        private SumoConfig config;
        private Rigidbody body;
        private PlayerEmoteAbility emote;
        private MinigameAudioPlayer audioPlayer;
        private Vector3? savedFacing;
        private Vector3 slideDirection;
        private float slideSpeed, slideTime, slideLength;
        private int index;
        private bool released;
        private Vector3 previousPosition;
        public float PlanarSpeed { get; private set; }
        private float nextBalanceProbe, balanceTarget;
        public float BalanceWeight { get; private set; }
        public Vector3 EdgeDirection { get; private set; }
        private SumoCombatVisualState visual;
        private SumoCombatPose pose;
        private SumoCombatEffects effects;
        public SumoConfig Config => config;
        public bool IsGrounded => combat != null && combat.Grounded(index);
        public SumoParticipant Participant { get; private set; }
        public PlayerController Motor => Participant.Motor;
        public PlayerInputReader Input => Participant.Input;
        public NetworkObject Identity { get; private set; }
        public CapsuleCollider Capsule { get; private set; }
        public int Id => Participant.Player.Id;
        public float BodyHeight => Capsule != null ? Capsule.height : 1.8f;
        public bool LocallySimulated => body != null && !body.isKinematic && Motor.enabled;
        public SumoCombatState State => combat != null && index < combat.Count ? combat.StateAt(index) : SumoCombatState.Create(Id);
        public bool PredictsLocally => Identity != null && Identity.IsSpawned && Identity.IsOwner && !Identity.NetworkManager.IsServer;
        // Owner time estimates when this input reaches the server; remote copies use
        // buffered server time, like their NetworkTransform.
        public double VisualNow => PredictsLocally ? Identity.NetworkManager.LocalTime.Time : NetworkClock.Now;
        public SumoCombatState VisualState => visual != null ? visual.Evaluate(VisualNow) : State;
        public void Predict(int sequence, SumoCommand command, float yaw) => visual.Predict(sequence, command, yaw, VisualNow);
        public void Observe(SumoCombatState state) => visual.Receive(state, VisualNow, PredictsLocally);
        public SumoContact LastContact { get; private set; }
        public SumoAttack ContactAttack { get; private set; }
        public Vector3 ContactDirection { get; private set; }
        public float ContactSlide { get; private set; }
        public double ContactAt { get; private set; } = double.NegativeInfinity;
        public bool ContactAsTarget { get; private set; }

        public void Bind(SumoCombat owner, SumoParticipant participant, SumoConfig settings, int slot)
        {
            combat = owner; Participant = participant; config = settings; index = slot;
            body = GetComponent<Rigidbody>(); Capsule = GetComponent<CapsuleCollider>(); Identity = GetComponent<NetworkObject>();
            emote = GetComponent<PlayerEmoteAbility>(); audioPlayer = GetComponentInChildren<MinigameAudioPlayer>(); savedFacing = Motor.FacingOverride;
            visual = new SumoCombatVisualState(config, Id);
            pose = gameObject.AddComponent<SumoCombatPose>(); pose.Bind(this, config.Motions);
            effects = gameObject.AddComponent<SumoCombatEffects>(); effects.Bind(this, config.CombatEffectMaterial);
        }
        public void ResetCombat() { slideTime = 0; ContactAt = double.NegativeInfinity; }
        public void ResetVisual() => visual.Reset(State);
        public void Release()
        {
            if (released) return;
            released = true; slideTime = 0;
            if (pose != null) { pose.enabled = false; Destroy(pose); }
            if (effects != null) { effects.enabled = false; Destroy(effects); }
            if (Motor != null) { Motor.ClearSpeedCap(this); Motor.FacingOverride = savedFacing; }
        }
        private void OnDestroy() { if (Participant != null) Release(); }
        private void Update()
        {
            if (released || Participant == null) return;
            Vector3 step = Motor.Position - previousPosition; step.y = 0;
            PlanarSpeed = Mathf.Min(10, step.magnitude / Mathf.Max(.001f, Time.deltaTime)); previousPosition = Motor.Position;
            UpdateBalance();
            if (!LocallySimulated) return;
            var s = State;
            bool active = combat.Active && !Participant.Dead && !Motor.IsKnockedDown;
            if (!active) { visual.Reset(State); Motor.ClearSpeedCap(this); Motor.FacingOverride = savedFacing; return; }
            var shown = VisualState;
            bool aiming = shown.Phase == SumoCombatPhase.Guard || shown.Phase == SumoCombatPhase.Charge || shown.Phase == SumoCombatPhase.Windup || shown.Phase == SumoCombatPhase.Recovery;
            Motor.FacingOverride = aiming ? shown.Forward : savedFacing;
            float cap = s.Phase == SumoCombatPhase.Guard ? config.GuardSpeed : s.Phase == SumoCombatPhase.Charge ? config.ChargeSpeed : s.Phase == SumoCombatPhase.Idle ? 0 : ActionSpeed;
            if (cap > 0) Motor.ApplySpeedCap(this, cap); else Motor.ClearSpeedCap(this);
            if (s.Phase != SumoCombatPhase.Idle) emote?.StopEmote();
        }
        private void UpdateBalance()
        {
            bool standing = combat.Active && !Participant.Dead && !Motor.IsKnockedDown && !Motor.IsCrouched && IsGrounded;
            if (!standing) { balanceTarget = BalanceWeight = 0; return; }
            if (Time.unscaledTime >= nextBalanceProbe)
            {
                nextBalanceProbe = Time.unscaledTime + BalanceProbeInterval;
                balanceTarget = SumoEdgeBalance.Evaluate(config, Motor.Position, combat.Elapsed, out var direction);
                EdgeDirection = direction;
            }
            BalanceWeight = Mathf.MoveTowards(BalanceWeight, balanceTarget, Time.deltaTime / BalanceBlendSeconds);
        }
        private void FixedUpdate()
        {
            if (released || Participant == null || !combat.Active || Participant.Dead || !LocallySimulated || slideTime <= 0) return;
            float speed = slideSpeed * Mathf.Clamp01(slideTime / slideLength);
            var velocity = body.linearVelocity;
            body.linearVelocity = new Vector3(slideDirection.x * speed, velocity.y, slideDirection.z * speed);
            slideTime = Mathf.Max(0, slideTime - Time.fixedDeltaTime);
        }
        public void Feedback(SumoCombatHit hit, bool target)
        {
            LastContact = hit.Contact; ContactAt = NetworkClock.Now; ContactAsTarget = target;
            ContactAttack = hit.Attack; ContactDirection = hit.Direction; ContactSlide = hit.Slide;
            if (!target || hit.Contact == SumoContact.Miss) return;
            effects?.Show(hit);
            if (audioPlayer != null) audioPlayer.PlayAt(CoreSfx.PushHit, hit.Point, hit.Contact == SumoContact.Block ? .55f : 1f);
            if (!LocallySimulated || Participant.Dead || hit.Contact == SumoContact.Parry) return;
            // Combine converging shoves, with a cap: no order-dependent overwrite and no eight-player launch exploit.
            Vector3 velocity = slideDirection * slideSpeed * (slideLength > 0 ? slideTime / slideLength : 0) + hit.Direction * hit.Speed;
            slideSpeed = Mathf.Min(velocity.magnitude, config.CounterPushSpeed * 1.35f);
            slideDirection = velocity.normalized; slideTime = slideLength = hit.Slide;
        }
    }
}
