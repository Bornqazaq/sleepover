using Igruha.Core.Player;
using Igruha.Core.Session;
using UnityEngine;

namespace Igruha.Minigames.SumoRing
{
    public sealed class SumoParticipant
    {
        public SessionPlayer Player { get; }
        public PlayerController Motor => Player.Avatar;
        public PlayerInputReader Input { get; }
        public PlayerPushAbility Push { get; }
        public PlayerElimination Elimination { get; }
        public bool Dead { get; private set; }
        public Vector3 LastPosition { get; private set; }
        public double AimUntil { get; set; }
        private readonly PlayerRespawner respawner;
        private readonly StuckDetector stuck;
        private readonly Rigidbody body;
        private readonly bool wasKinematic;
        private readonly Transform previousRespawn;
        private readonly IPushButtonOverride previousOverride;
        private readonly Vector3? previousFacing;
        private readonly float previousCooldown, previousFlight, previousHide;
        private readonly bool ownsElimination, inputEnabled, pushEnabled, motorEnabled, stuckEnabled, immune;
        public SumoParticipant(SessionPlayer player, IPushButtonOverride button, SumoConfig config, Transform recovery)
        {
            Player = player;
            Input = Motor.GetComponent<PlayerInputReader>();
            Push = Motor.GetComponent<PlayerPushAbility>();
            respawner = Motor.GetComponent<PlayerRespawner>();
            stuck = Motor.GetComponent<StuckDetector>();
            body = Motor.GetComponent<Rigidbody>(); wasKinematic = body != null && body.isKinematic;
            inputEnabled = Input != null && Input.enabled; pushEnabled = Push != null && Push.enabled;
            motorEnabled = Motor.enabled; stuckEnabled = stuck != null && stuck.enabled;
            immune = Motor.ImpulseImmune; previousFacing = Motor.FacingOverride;
            if (Push != null)
            {
                previousCooldown = Push.CooldownOverride; previousOverride = Push.ButtonOverride;
                Push.CancelPendingPush();
                Push.CooldownOverride = config.PushCooldown; Push.ButtonOverride = button;
            }
            if (respawner != null) { previousRespawn = respawner.RespawnPoint; respawner.SetRespawnPoint(recovery); }
            Elimination = Motor.GetComponent<PlayerElimination>();
            if (Elimination == null) { Elimination = Motor.gameObject.AddComponent<PlayerElimination>(); ownsElimination = true; }
            previousFlight = Elimination.FlightDuration; previousHide = Elimination.BodyHideDelay;
            Elimination.ConfigureTiming(config.FallSeconds, 0);
            RememberPosition();
        }
        public void RememberPosition() { if (Motor != null) LastPosition = Motor.Position; }
        public void Eliminate()
        {
            if (Dead) return;
            Dead = true;
            if (Motor == null) return;
            RememberPosition();
            if (Input != null) Input.enabled = false;
            if (Push != null) { Push.CancelPendingPush(); Push.enabled = false; }
            if (stuck != null) stuck.enabled = false;
            Motor.FacingOverride = null; Motor.MovementLocked = true;
            // Existing fall clips, selected by motion relative to facing. No ragdoll.
            Vector3 outward = new Vector3(LastPosition.x, 0, LastPosition.z).normalized;
            var kind = Vector3.Dot(outward, Motor.Facing) < 0 ? KnockdownType.FlyBack : KnockdownType.FallForward;
            Motor.Knockdown(kind);
            Elimination.Eliminate(LastPosition, outward * .1f);
            Motor.ImpulseImmune = true;
        }
        public void Restore()
        {
            if (Motor == null) return;
            Elimination.Restore();
            if (body != null) body.isKinematic = wasKinematic;
            Motor.enabled = motorEnabled; Motor.MovementLocked = false; Motor.ImpulseImmune = immune; Motor.FacingOverride = previousFacing;
            if (Input != null) Input.enabled = inputEnabled;
            if (Push != null) { Push.CancelPendingPush(); Push.enabled = pushEnabled; Push.CooldownOverride = previousCooldown; Push.ButtonOverride = previousOverride; }
            if (stuck != null) stuck.enabled = stuckEnabled;
            if (respawner != null) respawner.SetRespawnPoint(previousRespawn);
            if (ownsElimination) Object.Destroy(Elimination);
            else Elimination.ConfigureTiming(previousFlight, previousHide);
        }
    }
}
