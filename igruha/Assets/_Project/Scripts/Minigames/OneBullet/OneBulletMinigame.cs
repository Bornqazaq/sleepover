using System;
using System.Collections.Generic;
using Igruha.Core.CameraSystems;
using Igruha.Core.Audio;
using Igruha.Core.Combat;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Igruha.Minigames.OneBullet
{
    public sealed class OneBulletMinigame : MinigameControllerBase, IPushButtonOverride
    {
        [SerializeField] private OneBulletConfig config;
        [SerializeField] private MinigameSfxLibrary soundLibrary;
        [SerializeField] private Transform[] weaponSpawns;
        [SerializeField] private Transform[] safeSpawns;
        [SerializeField] private SpectatorCamera spectator;
        [SerializeField] private Camera gameCamera;
        [SerializeField] private FirstPersonCameraRig firstPersonRig;
        private readonly OneBulletRound round = new OneBulletRound();
        private readonly List<OneBulletParticipant> participants = new List<OneBulletParticipant>(8);
        private readonly List<SessionPlayer> alive = new List<SessionPlayer>(8);
        private readonly List<int> ids = new List<int>(8);
        private readonly RaycastHit[] aimHits = new RaycastHit[64];
        private HitscanWeapon weapon;
        private OneBulletNetwork network;
        public bool LocalRosterReady => participants.Count > 0;
        private OneBulletParticipant local;
        private bool countdownFinished, restored, shotPending;
        private int pendingShooter, shownAlive = -1;
        private Vector3 pendingAim;
        private double showResultsAt;
        private const float AimHeight = 0.95f;
        private const float RecoveryFloor = -3f;
        public OneBulletRound Round => round;
        public OneBulletConfig Config => config;
        public IReadOnlyList<OneBulletParticipant> Participants => participants;
        public int LocalId => local != null ? local.Player.Id : OneBulletRound.Nobody;
        public bool LocalArmed => RoundActive && round.Holder == LocalId && local != null && !local.Dead;
        public OneBulletParticipant LocalParticipant => local;
        public bool LocalInputAvailable => RoundActive && local?.Input != null && local.Input.enabled &&
            !local.Input.Suspended && !local.Dead && local.Motor != null && !local.Motor.IsKnockedDown &&
            !Igruha.Core.UI.TutorialScreen.PointerInputActive && Igruha.Core.UI.PauseScreen.Current?.IsPaused != true;
        public Vector3 PickupPosition => round.Pickup >= 0 && round.Pickup < weaponSpawns.Length ? weaponSpawns[round.Pickup].position : Vector3.zero;
        public event Action<Vector3, Vector3, bool> Shot;
        public event Action<int> PickedUp;
        public event Action<int, Vector3> Died;
        public event Action Changed;
        protected override float RoundDuration => config != null ? config.Duration : base.RoundDuration;
        protected override void Awake()
        {
            base.Awake();
            network = GetComponent<OneBulletNetwork>();
            weapon = gameObject.AddComponent<HitscanWeapon>();
        }
        protected override void OnPlayersReady()
        {
            // Elimination must capture the real model before the local camera hides it.
            firstPersonRig?.SetOwnModelVisible(true);
            participants.Clear(); ids.Clear(); alive.Clear(); local = null; restored = false;
            foreach (var p in Players)
            {
                ids.Add(p.Id);
                if (p.Avatar == null) continue;
                var entry = new OneBulletParticipant(p, this, config.DeathSeconds, soundLibrary);
                participants.Add(entry); alive.Add(p);
                if (entry.Input != null && entry.Input.LocallyControlled) local = entry;
            }
            firstPersonRig?.SetOwnModelVisible(false);
        }
        protected override void OnRoundStarted()
        {
            countdownFinished = false; shotPending = false; shownAlive = -1; showResultsAt = 0;
            round.Reset(ids, NetworkClock.Now + config.Countdown, config.Duration, config.FirstSpawnDelay);
            weapon.Configure(1, 0f, config.Duration + config.RespawnDelay, config.ShotRange, config.HitMask);
            weapon.SpreadAngle = 0f;
            if (HasAuthority) Timer.StopTimer();
            foreach (var p in participants) if (p.Motor != null) p.Motor.MovementLocked = true;
            Changed?.Invoke();
            network?.RefreshPresentation();
        }
        private void Update()
        {
            if (!RoundActive || config == null) return;
            double now = NetworkClock.Now;
            if (!countdownFinished)
            {
                Hud?.ShowCountdown(Mathf.Max(0f, (float)(round.BeginsAt - now)));
                if (now >= round.BeginsAt)
                {
                    countdownFinished = true; Hud?.HideCountdown();
                    foreach (var p in participants) if (p.Motor != null && !p.Dead) p.Motor.MovementLocked = false;
                    if (HasAuthority) Timer.StartTimer(config.Duration);
                }
            }
            if (shownAlive != round.AliveCount)
            {
                shownAlive = round.AliveCount;
                Hud?.ShowStatus($"В лабиринте: {shownAlive}  •  Слушай шаги");
            }
            if (LocalInputAvailable &&
                ((!LocalArmed && Mouse.current?.rightButton.wasPressedThisFrame == true) || Keyboard.current?.leftShiftKey.wasPressedThisFrame == true || Keyboard.current?.rightShiftKey.wasPressedThisFrame == true))
                local.Push?.RequestPush();
        }
        private void FixedUpdate()
        {
            if (!RoundActive || config == null) return;
            foreach (var participant in participants) participant.RememberPosition();
            if (!HasAuthority)
            {
                if (local?.Motor != null && !local.Dead) UpdateRecovery(local);
                return;
            }
            double now = NetworkClock.Now;
            if (round.Finished)
            {
                if (showResultsAt > 0 && now >= showResultsAt) EndMinigame();
                return;
            }
            if (now >= round.EndsAt) { EndMinigame(); return; }
            if (!round.IsLive(now)) return;
            if (shotPending)
            {
                shotPending = false;
                ResolveShot(pendingShooter, pendingAim, now);
                if (!RoundActive) return;
            }
            if (round.CanSpawn(now) && weaponSpawns.Length > 1)
            {
                int next = UnityEngine.Random.Range(0, weaponSpawns.Length - (round.PreviousPickup >= 0 ? 1 : 0));
                if (round.PreviousPickup >= 0 && next >= round.PreviousPickup) next++;
                round.Spawn(next, now); Changed?.Invoke();
            }
            foreach (var p in participants)
            {
                if (p.Motor == null || round.Find(p.Player.Id)?.Alive != true) continue;
                UpdateRecovery(p);
                if (round.Pickup < 0) continue;
                Vector3 pickup = PickupPosition;
                Vector3 closest = p.Capsule.ClosestPoint(pickup);
                if ((closest - pickup).sqrMagnitude > config.PickupRadius * config.PickupRadius) continue;
                Vector3 chest = p.Motor.Position + Vector3.up * AimHeight;
                if (Physics.Linecast(chest, pickup, out RaycastHit hit, config.HitMask, QueryTriggerInteraction.Ignore) &&
                    hit.collider != p.Capsule) continue;
                if (round.Take(p.Player.Id, now)) { weapon.ResetWeapon(); PickedUp?.Invoke(p.Player.Id); Changed?.Invoke(); }
            }
        }
        private void UpdateRecovery(OneBulletParticipant p)
        {
            Transform nearest = null; float distance = float.PositiveInfinity;
            foreach (var spawn in safeSpawns)
            {
                float d = (spawn.position - p.Motor.Position).sqrMagnitude;
                if (d < distance) { distance = d; nearest = spawn; }
            }
            if (nearest == null) return;
            p.Respawner?.SetRespawnPoint(nearest);
            Vector3 position = p.Motor.Position;
            if (position.y < RecoveryFloor || Mathf.Abs(position.x) > 21.6f || Mathf.Abs(position.z) > 21.6f)
                p.Respawner?.Respawn();
        }
        public bool HandlePushButton(PlayerController player)
        {
            var p = Find(player);
            if (p == null || p.Dead || !round.IsLive(NetworkClock.Now)) return true;
            if (round.Holder != p.Player.Id) return false;
            if (gameCamera == null) return true;
            Ray cameraRay = gameCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
            Vector3 target = cameraRay.GetPoint(config.ShotRange);
            int count = Physics.RaycastNonAlloc(cameraRay, aimHits, config.ShotRange, config.HitMask, QueryTriggerInteraction.Ignore);
            float closest = config.ShotRange;
            for (int i = 0; i < count; i++)
                if (aimHits[i].collider != p.Capsule && aimHits[i].distance < closest)
                { closest = aimHits[i].distance; target = aimHits[i].point; }
            Vector3 direction = (target - ShotOrigin(p)).normalized;
            if (network != null && network.Active) network.RequestShot(direction);
            else QueueShot(p.Player.Id, direction);
            return true;
        }
        public void QueueShot(int id, Vector3 direction)
        {
            if (!HasAuthority || shotPending || !round.CanFire(id, NetworkClock.Now) || !ValidDirection(direction)) return;
            var p = Find(id);
            if (p == null || p.Motor == null || p.Motor.IsKnockedDown || p.Motor.MovementLocked) return;
            pendingShooter = id; pendingAim = direction.normalized; shotPending = true;
        }
        public static bool ValidDirection(Vector3 direction) =>
            float.IsFinite(direction.x) && float.IsFinite(direction.y) && float.IsFinite(direction.z) && direction.sqrMagnitude > .5f && direction.sqrMagnitude < 1.5f;
        public const float EyeDrop = .15f;
        public static float EyeHeight(CapsuleCollider capsule) => Mathf.Max(capsule.center.y + capsule.height * .5f - EyeDrop, capsule.radius);
        public static Vector3 ShotOrigin(OneBulletParticipant player) => player.Motor.Position + Vector3.up * EyeHeight(player.Capsule);
        private void ResolveShot(int id, Vector3 direction, double now)
        {
            if (!round.CanFire(id, now)) return;
            var shooter = Find(id);
            if (shooter?.Motor == null || shooter.Motor.IsKnockedDown) return;
            Vector3 origin = ShotOrigin(shooter);
            if (!weapon.TryFire(origin, direction, out HitscanWeapon.HitResult hit)) return;
            int victim = OneBulletRound.Nobody;
            foreach (var p in participants)
                if (p.Player.Id != id && p.Capsule == hit.Collider && round.Find(p.Player.Id)?.Alive == true) victim = p.Player.Id;
            if (!round.Fire(id, victim, now, config.RespawnDelay)) return;
            Shot?.Invoke(origin, hit.Point, victim >= 0);
            network?.PublishShot(origin, hit.Point, victim >= 0);
            if (victim >= 0) ApplyDeath(victim, direction * config.DeathImpulse);
            Changed?.Invoke();
            CheckEnd();
        }
        private void ApplyDeath(int id, Vector3 direction)
        {
            var p = Find(id);
            if (p == null || p.Dead) return;
            alive.Remove(p.Player);
            // Disconnect may remove the avatar before the callback; the event must still reach presentation.
            Vector3 facing = p.Motor != null ? p.Motor.Facing : Vector3.forward;
            // Release the camera's renderer ownership before elimination hides the body.
            // Otherwise disabling the first-person rig in spectator mode reveals it again.
            if (p == local) firstPersonRig?.SetOwnModelVisible(true);
            p.SetDead(facing * (id % 2 == 0 ? -config.DeathImpulse : config.DeathImpulse));
            if (p.Elimination != null) p.Elimination.BodyHidden += OnBodyHidden;
            Died?.Invoke(id, p.LastPosition);
        }
        public void ApplySnapshotPresentation(int previousHolder)
        {
            if (RoundActive)
                foreach (var record in round.Records)
                    if (!record.Alive) ApplyDeath(record.Id, Vector3.zero);
            if (RoundActive && round.Holder >= 0 && round.Holder != previousHolder) PickedUp?.Invoke(round.Holder);
            Changed?.Invoke();
        }
        public void ApplyShotPresentation(Vector3 origin, Vector3 end, bool hit) => Shot?.Invoke(origin, end, hit);
        private void OnBodyHidden(PlayerElimination elimination)
        {
            if (local != null && local.Elimination == elimination && RoundActive) spectator?.Activate(alive);
        }
        public void Leave(int id)
        {
            if (!HasAuthority || !RoundActive || !round.Leave(id, NetworkClock.Now, config.RespawnDelay)) return;
            ApplyDeath(id, Vector3.down * config.DeathImpulse); Changed?.Invoke(); CheckEnd();
        }
        private void CheckEnd()
        {
            if (round.AliveCount >= 2 || round.Finished) return;
            // Let the final fall and its replicated presentation finish before the result phase.
            round.Finish(NetworkClock.Now);
            showResultsAt = NetworkClock.Now + config.DeathSeconds;
            Timer.StopTimer();
            Changed?.Invoke();
        }
        protected override void OnPlayerLeftRound(int playerId) => Leave(playerId);
        public override void BeginViewing() => spectator?.Activate(alive);
        protected override void OnRoundEnded()
        {
            if (HasAuthority) round.Finish(NetworkClock.Now);
            RestorePlayers();
            Hud?.HideCountdown(); Hud?.HideStatus(); Changed?.Invoke();
        }
        private void RestorePlayers()
        {
            if (restored) return;
            firstPersonRig?.SetOwnModelVisible(true);
            restored = true; spectator?.Deactivate(); shotPending = false;
            foreach (var p in participants)
            {
                if (p.Elimination != null) p.Elimination.BodyHidden -= OnBodyHidden;
                p.Restore();
            }
            firstPersonRig?.SetOwnModelVisible(false);
        }
        protected override void OnDisable() { RestorePlayers(); base.OnDisable(); }
        protected override void CollectResults(MinigameResults results) => round.Collect(results);
        public OneBulletParticipant Find(int id) { foreach (var p in participants) if (p.Player.Id == id) return p; return null; }
        private OneBulletParticipant Find(PlayerController motor) { foreach (var p in participants) if (p.Motor == motor) return p; return null; }
    }
}
