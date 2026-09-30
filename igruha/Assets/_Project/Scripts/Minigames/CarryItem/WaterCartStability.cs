using System;
using Igruha.Core.Items;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    public enum CartTiltCause : byte { None, Disagreement, Turn, Release, Impact }

    public struct CartStabilityState : INetworkSerializable, IEquatable<CartStabilityState>
    {
        public CartTiltCause Cause;
        public int Responsible;
        public ushort SpillSequence;
        public CartTiltCause SpillCause;
        public int SpillResponsible;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Cause);
            serializer.SerializeValue(ref Responsible);
            serializer.SerializeValue(ref SpillSequence);
            serializer.SerializeValue(ref SpillCause);
            serializer.SerializeValue(ref SpillResponsible);
        }
        public bool Equals(CartStabilityState other) => Cause == other.Cause && Responsible == other.Responsible &&
            SpillSequence == other.SpillSequence && SpillCause == other.SpillCause && SpillResponsible == other.SpillResponsible;
    }

    /// <summary>Server computes tilt and blame; WaterCart replicates the resulting state.</summary>
    [DefaultExecutionOrder(20), RequireComponent(typeof(MultiCarryObject))]
    public sealed class WaterCartStability : MonoBehaviour
    {
        private MultiCarryObject carry;
        private WaterCart cart;
        private CarryItemConfig config;
        private float lastYaw;
        private float releaseHold;
        private int releasedPlayer = -1;
        private bool spilledThisTrip;
        private float controlledSpeed;
        private Rigidbody body;
        private readonly Vector3[] tensions = new Vector3[MultiCarryObject.MaxHandles];
        private readonly PlayerController[] sampledCarriers = new PlayerController[MultiCarryObject.MaxHandles];
        public CartStabilityState State { get; private set; }
        public event Action<CartTiltCause, int> FirstSpill;
        public bool NeedsHands => cart != null && cart.Load >= 0.8f &&
            carry.CarrierCount < carry.HandleCount * 0.5f;
        public bool IsStraining => cart != null && cart.Load >= 0.8f && carry.CarrierCount == 1;

        private void Awake() { carry = GetComponent<MultiCarryObject>(); body = GetComponent<Rigidbody>(); }
        private void OnEnable() { carry.HandleReleased += OnReleased; }
        private void OnDisable() { carry.HandleReleased -= OnReleased; }

        public void Configure(WaterCart owner, CarryItemConfig settings)
        {
            cart = owner;
            config = settings;
            lastYaw = transform.eulerAngles.y;
        }

        public void ResetTrip()
        {
            spilledThisTrip = false;
            releaseHold = 0f;
            lastYaw = transform.eulerAngles.y;
            SetCause(CartTiltCause.None, -1);
        }

        public void ApplyState(CartStabilityState value)
        {
            bool announce = value.SpillSequence != State.SpillSequence;
            State = value;
            if (announce) FirstSpill?.Invoke(value.SpillCause, value.SpillResponsible);
        }

        public void RecordSpill()
        {
            if (spilledThisTrip || cart == null || !cart.IsAuthority) return;
            spilledThisTrip = true;
            var next = State;
            next.SpillSequence++;
            next.SpillCause = next.Cause == CartTiltCause.None ? CartTiltCause.Impact : next.Cause;
            next.SpillResponsible = next.Responsible;
            ApplyState(next);
            cart.PublishStability();
        }

        private void SetCause(CartTiltCause cause, int responsible)
        {
            if (State.Cause == cause && State.Responsible == responsible) return;
            var next = State;
            next.Cause = cause;
            next.Responsible = responsible;
            State = next;
            cart?.PublishStability();
        }

        private void FixedUpdate()
        {
            if (config == null || cart == null || !cart.IsAuthority || cart.IsLost) return;
            float dt = Time.fixedDeltaTime;
            controlledSpeed = carry.FlatVelocity.magnitude;
            float yaw = transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(lastYaw, yaw) / dt;
            lastYaw = yaw;
            Vector3 lean = Vector3.zero;
            CartTiltCause cause = CartTiltCause.None;
            int responsible = -1;
            float difference = Disagreement(out int lagger, out Vector3 lagDirection);
            float mismatchTilt = DisagreementDegrees(difference, cart.Load, config);
            if (mismatchTilt > 0f)
            {
                lean = Vector3.Cross(Vector3.up, lagDirection) * mismatchTilt;
                cause = CartTiltCause.Disagreement;
                responsible = PlayerId(carry.CarrierAt(lagger));
            }

            float turn = TurnDegrees(yawRate, carry.FlatVelocity.magnitude, cart.Load, config);
            if (turn > 0.1f)
            {
                Vector3 outward = -transform.right * Mathf.Sign(yawRate);
                lean += Vector3.Cross(Vector3.up, outward) * turn;
                if (turn > mismatchTilt && turn >= config.WarningTilt) { cause = CartTiltCause.Turn; responsible = -1; }
            }
            carry.SetTiltTarget(lean);
            releaseHold = Mathf.Max(0f, releaseHold - dt);
            if (releaseHold > 0f && mismatchTilt < config.ReleaseTilt && turn < config.ReleaseTilt)
            { cause = CartTiltCause.Release; responsible = releasedPlayer; }
            // Keep the last cause while its physical lean is still relaxing above the warning angle.
            if (cause == CartTiltCause.None && carry.TiltAngle >= config.WarningTilt) return;
            SetCause(cause, responsible);
        }

        public static float DisagreementDegrees(float difference, float load, CarryItemConfig settings) =>
            Mathf.Max(0f, (difference - settings.DisagreementStart) /
                Mathf.Max(0.01f, settings.DisagreementFull - settings.DisagreementStart)) *
            settings.DisagreementTilt * Mathf.Lerp(settings.EmptyTiltFraction, 1f, load);

        public static float TurnDegrees(float yawRate, float speed, float load, CarryItemConfig settings) =>
            Mathf.Abs(yawRate) / settings.TurnReferenceDegrees * speed / settings.TurnReferenceSpeed *
            settings.TurnTilt * Mathf.Lerp(settings.EmptyTiltFraction, 1f, load);

        private float Disagreement(out int lagger, out Vector3 lagDirection)
        {
            lagger = -1;
            lagDirection = Vector3.zero;
            if (carry.CarrierCount < 2) return 0f;
            for (int i = 0; i < carry.HandleCount; i++)
                if (carry.IsSettlingAt(i)) return 0f;
            Vector3 mean = Vector3.zero, support = Vector3.zero;
            float largest = 0f;
            for (int i = 0; i < carry.HandleCount; i++)
            {
                if (carry.CarrierAt(i) == null) continue;
                Vector3 sample = carry.TensionAt(i);
                // A short physical response filters packet cadence without concealing a lasting lag.
                tensions[i] = sampledCarriers[i] != carry.CarrierAt(i) ? sample :
                    Vector3.Lerp(tensions[i], sample, 1f - Mathf.Exp(-Time.fixedDeltaTime * 12f));
                sampledCarriers[i] = carry.CarrierAt(i);
                mean += tensions[i];
                support += carry.StationOf(i);
                for (int j = 0; j < i; j++)
                    if (carry.CarrierAt(j) != null)
                        largest = Mathf.Max(largest, Vector3.Distance(tensions[i], tensions[j]));
            }
            Vector3 direction = mean.sqrMagnitude > 0.001f ? mean.normalized : transform.forward;
            float least = float.PositiveInfinity;
            for (int i = 0; i < carry.HandleCount; i++)
            {
                if (carry.CarrierAt(i) == null) continue;
                float progress = Vector3.Dot(tensions[i], direction);
                if (progress < least) { least = progress; lagger = i; }
            }
            lagDirection = Vector3.ProjectOnPlane(carry.StationOf(lagger) - support / carry.CarrierCount,
                Vector3.up).normalized;
            return largest;
        }

        private void OnReleased(int slot, PlayerController player, CarryReleaseReason reason)
        {
            if (config == null || cart == null || !cart.IsAuthority || cart.IsLost ||
                reason == CarryReleaseReason.Thrown || reason == CarryReleaseReason.RoundEnded ||
                carry.FlatVelocity.sqrMagnitude < 0.0225f) return;
            Vector3 side = Vector3.ProjectOnPlane(carry.StationOf(slot) - transform.position, Vector3.up).normalized;
            carry.AddTiltKick(Vector3.Cross(Vector3.up, side) * config.ReleaseTilt * (0.5f + cart.Load));
            releasedPlayer = PlayerId(player);
            releaseHold = 2f;
            SetCause(CartTiltCause.Release, releasedPlayer);
        }

        public static int PlayerId(PlayerController avatar)
        {
            if (avatar == null) return -1;
            var session = SessionScoreboard.Current;
            if (session != null)
                foreach (var player in session.Players)
                    if (player.Avatar == avatar) return player.Id;
            return -1;
        }

        public static string PlayerName(int id) => SessionScoreboard.Current?.FindPlayer(id)?.DisplayName ?? "игрок";

        private void OnCollisionEnter(Collision collision) => OnCollisionStay(collision);

        private void OnCollisionStay(Collision collision)
        {
            if (cart == null || config == null || !cart.IsAuthority || cart.IsLost || carry.IsCarried ||
                carry.InFlight || controlledSpeed > config.UnattendedPushSpeed + 0.1f || body == null) return;
            var player = collision.gameObject.GetComponentInParent<PlayerController>();
            if (player == null) return;
            var game = Igruha.Core.Minigame.MinigameControllerBase.Current as CarryItemMinigame;
            if (game == null || game.TeamOfAvatar(player) == cart.Team) return;
            Vector3 away = Vector3.ProjectOnPlane(transform.position - player.transform.position, Vector3.up).normalized;
            float approach = Vector3.Dot(player.MoveIntent, away);
            // Remote MoveIntent is owner-only; contact with its moving capsule also counts.
            Vector3 velocity = Vector3.ClampMagnitude(carry.FlatVelocity + away * Mathf.Max(0f, approach) * 0.1f,
                config.UnattendedPushSpeed);
            velocity.y = body.linearVelocity.y;
            body.linearVelocity = velocity;
        }
    }
}
