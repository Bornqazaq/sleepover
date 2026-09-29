using Igruha.Core.Player;
using Igruha.Core.Audio;
using Igruha.Core.Session;
using UnityEngine;

namespace Igruha.Minigames.OneBullet
{
    /// <summary>Cached avatar dependencies and round-scoped changes, restored before returning to the hub.</summary>
    public sealed class OneBulletParticipant
    {
        public SessionPlayer Player { get; }
        public PlayerController Motor => Player.Avatar;
        public PlayerInputReader Input { get; }
        public PlayerPushAbility Push { get; }
        public PlayerElimination Elimination { get; }
        public PlayerRespawner Respawner { get; }
        public CapsuleCollider Capsule { get; }
        public Transform Hand { get; }
        private readonly bool ownsElimination;
        private readonly bool motorWasEnabled, pushWasEnabled, inputWasEnabled;
        private readonly float previousFlight, previousHide, previousStepRange;
        private readonly CharacterFootsteps footsteps;
        private readonly string previousStepSlot;
        private readonly IPushButtonOverride previousOverride;
        private readonly Transform previousRespawn;
        public bool Dead { get; private set; }
        public Vector3 LastPosition { get; private set; }
        public void RememberPosition() { if (Motor != null) LastPosition = Motor.Position; }
        public OneBulletParticipant(SessionPlayer player, IPushButtonOverride button, float deathSeconds, MinigameSfxLibrary soundLibrary)
        {
            Player = player;
            var avatar = player.Avatar;
            LastPosition = avatar.Position;
            Input = avatar.GetComponent<PlayerInputReader>();
            Push = avatar.GetComponent<PlayerPushAbility>();
            Respawner = avatar.GetComponent<PlayerRespawner>();
            Capsule = avatar.GetComponent<CapsuleCollider>();
            Elimination = avatar.GetComponent<PlayerElimination>();
            if (Elimination == null) { Elimination = avatar.gameObject.AddComponent<PlayerElimination>(); ownsElimination = true; }
            previousFlight = Elimination.FlightDuration; previousHide = Elimination.BodyHideDelay;
            Elimination.ConfigureTiming(deathSeconds, 0f);
            pushWasEnabled = Push != null && Push.enabled;
            inputWasEnabled = Input != null && Input.enabled;
            footsteps = avatar.GetComponent<CharacterFootsteps>();
            if (footsteps != null)
            {
                previousStepRange = footsteps.RangeOverride; previousStepSlot = footsteps.SlotOverride;
                avatar.GetComponent<MinigameAudioPlayer>()?.AddLibrary(soundLibrary);
                footsteps.SetSlotOverride("ob_step"); footsteps.SetRangeOverride(5.76f);
            }
            motorWasEnabled = avatar.enabled;
            previousOverride = Push != null ? Push.ButtonOverride : null;
            previousRespawn = Respawner != null ? Respawner.RespawnPoint : null;
            if (Push != null) Push.ButtonOverride = button;
            var animator = avatar.GetComponentInChildren<Animator>();
            Hand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        }
        public void SetDead(Vector3 direction)
        {
            if (Dead) return;
            Dead = true;
            if (Motor == null) return;
            LastPosition = Motor.Position;
            bool front = Vector3.Dot(direction, Motor.Facing) < 0f;
            Motor.Knockdown(front ? KnockdownType.FlyBack : KnockdownType.FallForward);
            Motor.MovementLocked = true;
            if (Input != null) Input.enabled = false;
            if (Push != null) Push.enabled = false;
            Elimination.Eliminate(Motor.Position, direction);
        }
        public void Restore()
        {
            if (Motor == null) return;
            Elimination.Restore();
            Motor.enabled = motorWasEnabled;
            Motor.MovementLocked = false;
            Motor.FacingOverride = null;
            if (Input != null) Input.enabled = inputWasEnabled;
            if (footsteps != null) { footsteps.SetSlotOverride(previousStepSlot); footsteps.SetRangeOverride(previousStepRange); }
            if (Push != null) { Push.enabled = pushWasEnabled; Push.ButtonOverride = previousOverride; }
            if (Respawner != null) Respawner.SetRespawnPoint(previousRespawn);
            if (ownsElimination) Object.Destroy(Elimination);
            else Elimination.ConfigureTiming(previousFlight, previousHide);
        }
    }
}
