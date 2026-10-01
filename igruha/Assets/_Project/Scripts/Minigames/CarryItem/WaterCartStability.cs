using System;
using Igruha.Core.Items;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    public enum CartTiltCause : byte { None, Disagreement, Turn, Release, Impact, Brake }

    public struct CartStabilityState : INetworkSerializable, IEquatable<CartStabilityState>
    {
        public CartTiltCause Cause;
        public int Responsible;
        public ushort SpillSequence;
        public CartTiltCause SpillCause;
        public int SpillResponsible;
        public Vector2 Wave;
        // World-space slope of the suspended tub; positive X raises its right edge.
        public Vector2 BodySlope;
        public float Disagreement;
        public float Outflow, Risk, SpillAlong, SpillWidth;
        public byte SpillSide;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Cause); serializer.SerializeValue(ref Responsible);
            serializer.SerializeValue(ref SpillSequence); serializer.SerializeValue(ref SpillCause);
            serializer.SerializeValue(ref SpillResponsible); serializer.SerializeValue(ref Wave);
            serializer.SerializeValue(ref BodySlope); serializer.SerializeValue(ref Disagreement);
            serializer.SerializeValue(ref Outflow); serializer.SerializeValue(ref Risk);
            serializer.SerializeValue(ref SpillAlong); serializer.SerializeValue(ref SpillWidth);
            serializer.SerializeValue(ref SpillSide);
        }
        public bool Equals(CartStabilityState o) => Cause == o.Cause && Responsible == o.Responsible &&
            SpillSequence == o.SpillSequence && SpillCause == o.SpillCause && SpillResponsible == o.SpillResponsible &&
            Wave == o.Wave && BodySlope == o.BodySlope && Disagreement == o.Disagreement &&
            Outflow == o.Outflow && Risk == o.Risk && SpillSide == o.SpillSide &&
            SpillAlong == o.SpillAlong && SpillWidth == o.SpillWidth;
    }

    /// <summary>One authoritative wave drives the visible surface, overflow and volume loss.</summary>
    [DefaultExecutionOrder(20), RequireComponent(typeof(MultiCarryObject))]
    public sealed class WaterCartStability : MonoBehaviour
    {
        private MultiCarryObject carry;
        private WaterCart cart;
        private CarryItemConfig config;
        private Rigidbody body;
        private Vector3 lastVelocity, filteredAcceleration, lastPosition;
        private CartTiltCause lastCause;
        private Vector2 wave, waveVelocity;
        private float publishTimer, impactHold, controlledSpeed;
        private bool spilledThisTrip;
        private readonly CartTeamRocking rocking = new CartTeamRocking();
        public CartStabilityState State { get; private set; }
        public event Action<CartTiltCause, int> FirstSpill;
        public bool NeedsHands => cart != null && cart.Load >= 0.8f && carry.CarrierCount == 1 && carry.HandleCount > 1;
        public bool IsStraining => NeedsHands && carry.FlatVelocity.sqrMagnitude > 0.04f;
        public bool IsSpilling => State.Outflow > 0f && cart != null && cart.Water > 0 && !cart.IsLost;

        private void Awake() { carry = GetComponent<MultiCarryObject>(); body = GetComponent<Rigidbody>(); }
        public void Configure(WaterCart owner, CarryItemConfig settings)
        { cart = owner; config = settings; lastVelocity = carry.FlatVelocity; lastPosition = transform.position; }

        public void ResetTrip()
        {
            spilledThisTrip = false; wave = waveVelocity = Vector2.zero;
            rocking.Reset();
            filteredAcceleration = Vector3.zero; lastVelocity = carry.FlatVelocity; lastPosition = transform.position; impactHold = 0f; lastCause = CartTiltCause.None;
            var next = State; next.Wave = Vector2.zero; next.Outflow = next.Risk = 0f;
            next.BodySlope = Vector2.zero; next.Disagreement = 0f;
            next.Cause = CartTiltCause.None; next.Responsible = -1; ApplyState(next);
            cart?.PublishStability();
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
            var next = State; next.SpillSequence++; next.SpillCause = next.Cause;
            next.SpillResponsible = -1; ApplyState(next); cart.PublishStability();
        }
        public void Impact(Vector3 toward, float strength = 1f)
        {
            if (config == null || cart == null || !cart.IsAuthority || cart.IsLost) return;
            toward = Vector3.ProjectOnPlane(toward, Vector3.up).normalized;
            if (toward.sqrMagnitude < 0.01f) toward = transform.forward;
            waveVelocity += new Vector2(toward.x, toward.z) * config.ImpactWaveImpulse * Mathf.Clamp(strength, 0.2f, 2f);
            waveVelocity = Vector2.ClampMagnitude(waveVelocity, config.ImpactWaveImpulse * 2f);
            impactHold = 1.2f;
        }
        private void FixedUpdate()
        {
            if (config == null || cart == null || !cart.IsAuthority || cart.IsLost) return;
            float dt = Time.fixedDeltaTime;
            Vector3 velocity = carry.FlatVelocity;
            controlledSpeed = velocity.magnitude;
            if ((transform.position - lastPosition).sqrMagnitude > 2.25f)
            { wave = waveVelocity = Vector2.zero; rocking.Reset(); filteredAcceleration = Vector3.zero; lastVelocity = velocity; }
            lastPosition = transform.position;
            Vector3 acceleration = Vector3.ClampMagnitude((velocity - lastVelocity) / dt, 18f);
            Vector3 direction = lastVelocity.sqrMagnitude > 0.04f ? lastVelocity.normalized : transform.forward;
            lastVelocity = velocity;
            filteredAcceleration = Vector3.Lerp(filteredAcceleration, acceleration, 1f - Mathf.Exp(-dt * 10f));
            float longitudinal = Vector3.Dot(filteredAcceleration, direction);
            Vector3 lateral = filteredAcceleration - direction * longitudinal;
            float braking = Mathf.Max(0f, -longitudinal - config.GentleBrakeLimit);
            Vector3 forcing = -lateral * config.TurnWaveGain + direction * braking * config.BrakeWaveGain;
            // A gentle start moves the water slightly, leaving plenty of freeboard.
            forcing -= direction * Mathf.Max(0f, longitudinal) * 0.01f;
            impactHold = acceleration.magnitude > 8f ? 1.2f : Mathf.Max(0f, impactHold - dt);
            CartTiltCause cause = impactHold > 0f ? CartTiltCause.Impact :
                braking > 0.35f ? CartTiltCause.Brake : lateral.magnitude > 0.6f ? CartTiltCause.Turn : lastCause;
            rocking.Step(carry, config, dt);
            // A rising rim displaces the water toward the other side. The wave lags
            // behind the suspension, so correcting the controls leaves a settling swing.
            Vector2 rockWave = -rocking.Velocity * config.TeamRockWaveGain - rocking.Slope * 0.1f;
            forcing += new Vector3(rockWave.x, 0f, rockWave.y);
            if (impactHold <= 0f && (rocking.Strain > 0.08f || rocking.Slope.sqrMagnitude > 0.0004f))
                cause = CartTiltCause.Disagreement;
            lastCause = cause;
            if (cart.Water == 0) { wave = waveVelocity = Vector2.zero; }
            else
            {
                Vector2 target = Vector2.ClampMagnitude(new Vector2(forcing.x, forcing.z), CartWaterSurface.MaxSlope);
                waveVelocity += ((target - wave) * config.WaveResponse - waveVelocity * config.WaveDamping) * dt;
                wave = Vector2.ClampMagnitude(wave + waveVelocity * dt, CartWaterSurface.MaxSlope);
            }
            Vector2 localWave = CartWaterSurface.InHeading(wave, transform.rotation);
            Vector2 localBody = CartWaterSurface.InHeading(rocking.Slope, transform.rotation);
            float rate = CartWaterSurface.Overflow(cart.Load, localWave, config.OverflowRate,
                out byte side, out float along, out float width, out float risk, localBody);
            if (risk < 0.45f && impactHold == 0f) cause = CartTiltCause.None;
            var next = State;
            next.Wave = wave; next.Outflow = rate; next.Risk = risk;
            next.BodySlope = rocking.Slope; next.Disagreement = rocking.Strain;
            next.SpillSide = side; next.SpillAlong = along; next.SpillWidth = width;
            next.Cause = cause; next.Responsible = -1;
            bool edge = (State.Outflow > 0f) != (rate > 0f);
            ApplyState(next);
            // The collider and grips stay on the chassis. The presentation rocks the tub only.
            carry.SetTiltTarget(Vector3.zero);
            cart.DrainOverflow(rate, dt);
            publishTimer -= dt;
            if (publishTimer <= 0f || edge) { publishTimer = 0.05f; cart.PublishStability(); }
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
