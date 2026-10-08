using System;
using Igruha.Core.Items;
using Igruha.Core.Minigame;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Igruha.Minigames.OneBullet
{
    public struct OneBulletCanState
    {
        public bool Active;
        public Vector3 Position, Velocity, ImpactPoint;
        public double LaunchedAt, UpdatedAt, ImpactAt;
        public int Bounces;
        public float ImpactSpeed;
    }

    /// <summary>Pocket inventory and server-simulated noise props. Cans never interact with players.</summary>
    public sealed class OneBulletDecoys : MonoBehaviour
    {
        [SerializeField] private GameObject canModel;
        private OneBulletMinigame game;
        private OneBulletCanState[] states = Array.Empty<OneBulletCanState>();
        private Transform[] models = Array.Empty<Transform>();
        private Vector3[] pending = Array.Empty<Vector3>();
        private double nextSnapshot;
        private const double SnapshotInterval = .1, ImpactInterval = .13;
        private const float AudibleImpactSpeed = .7f, SpinRate = 240f;
        private static readonly Vector3 ReleaseOffset = new Vector3(.2f, -.12f, .3f);
        public event Action<int, OneBulletCanState> StateChanged;
        public event Action<Vector3, float> Impact;
        public event Action<Vector3> ThrowRequested;
        public int Count => states.Length;
        public OneBulletCanState GetState(int index) => states[index];
        private void Awake() => game = GetComponent<OneBulletMinigame>();

        public void ResetRound()
        {
            Clear();
            int count = game.Round.Records.Count * game.Config.CansPerRound;
            if (states.Length != count)
            {
                foreach (var model in models) if (model != null) Destroy(model.gameObject);
                states = new OneBulletCanState[count]; models = new Transform[count];
                pending = new Vector3[game.Round.Records.Count];
                if (canModel != null)
                    for (int i = 0; i < count; i++)
                    {
                        models[i] = Instantiate(canModel, transform).transform;
                        models[i].gameObject.SetActive(false);
                    }
            }
            nextSnapshot = 0;
        }
        public void Clear()
        {
            Array.Clear(states, 0, states.Length); Array.Clear(pending, 0, pending.Length);
            foreach (var model in models) if (model != null) model.gameObject.SetActive(false);
        }
        private void Update()
        {
            if (game.LocalInputAvailable && game.Round.IsLive(NetworkClock.Now) && Keyboard.current?.qKey.wasPressedThisFrame == true)
            {
                if (ThrowRequested != null) ThrowRequested.Invoke(game.LocalAim);
                else QueueThrow(game.LocalId, game.LocalAim);
            }
            double now = NetworkClock.Now;
            for (int i = 0; i < states.Length; i++)
            {
                if (models[i] == null) continue;
                var s = states[i];
                bool visible = game.GameplayActive && s.Active && now < s.LaunchedAt + game.Config.CanLifetime;
                if (models[i].gameObject.activeSelf != visible) models[i].gameObject.SetActive(visible);
                if (!visible) continue;
                // Bounded extrapolation between authoritative snapshots; never simulate replica collisions.
                float dt = Mathf.Clamp((float)(now - s.UpdatedAt), 0, (float)SnapshotInterval);
                models[i].position = s.Position + s.Velocity * dt;
                if (s.Velocity.sqrMagnitude > .01f)
                    models[i].rotation = Quaternion.Euler((float)(now - s.LaunchedAt) * SpinRate, i * 47, 35);
            }
        }
        public void QueueThrow(int id, Vector3 direction)
        {
            if (!game.Authority || !OneBulletMinigame.ValidDirection(direction)) return;
            for (int i = 0; i < game.Round.Records.Count && i < pending.Length; i++)
                if (game.Round.Records[i].Id == id) { pending[i] = direction.normalized; return; }
        }
        private void FixedUpdate()
        {
            if (!game.Authority || !game.GameplayActive || !game.Round.IsLive(NetworkClock.Now)) return;
            double now = NetworkClock.Now;
            for (int i = 0; i < pending.Length; i++)
            {
                if (pending[i] == Vector3.zero) continue;
                TryThrow(i, pending[i], now); pending[i] = Vector3.zero;
            }
            bool snapshot = now >= nextSnapshot;
            if (snapshot) nextSnapshot = now + SnapshotInterval;
            var c = game.Config;
            for (int i = 0; i < states.Length; i++)
            {
                var s = states[i]; if (!s.Active) continue;
                if (now >= s.LaunchedAt + c.CanLifetime)
                { s.Active = false; states[i] = s; StateChanged?.Invoke(i, s); continue; }
                bool contact = BouncingBallistics.Step(ref s.Position, ref s.Velocity, Time.fixedDeltaTime,
                    c.CanRadius, c.CanBounce, c.CanDrag, c.CanCollisionMask, out var hit, out float speed);
                bool audible = contact && speed >= AudibleImpactSpeed && now - s.ImpactAt >= ImpactInterval;
                if (audible)
                {
                    s.Bounces++; s.ImpactPoint = hit.point; s.ImpactAt = now; s.ImpactSpeed = speed;
                    Impact?.Invoke(hit.point, speed);
                }
                s.UpdatedAt = now; states[i] = s;
                if (snapshot || audible) StateChanged?.Invoke(i, s);
            }
        }
        private void TryThrow(int playerIndex, Vector3 aim, double now)
        {
            var r = game.Round.Records[playerIndex];
            var player = game.Find(r.Id);
            if (player?.Motor == null || player.Dead || player.Motor.IsKnockedDown || player.Motor.MovementLocked) return;
            var c = game.Config;
            Vector3 origin = OneBulletMinigame.ShotOrigin(player);
            // Start beside the eye so the full-size can visibly leaves the lower-right view.
            // Sweep from the authoritative eye to keep the offset on this side of nearby walls.
            if (Physics.CheckSphere(origin, c.CanRadius, c.CanCollisionMask, QueryTriggerInteraction.Ignore)) return;
            Vector3 right = Vector3.Cross(Vector3.up, aim);
            if (right.sqrMagnitude < .01f) right = player.Motor.transform.right;
            Vector3 offset = right.normalized * ReleaseOffset.x + Vector3.up * ReleaseOffset.y + aim * ReleaseOffset.z;
            if (Physics.SphereCast(origin, c.CanRadius, offset.normalized, out var obstruction,
                offset.magnitude, c.CanCollisionMask, QueryTriggerInteraction.Ignore))
                offset = offset.normalized * Mathf.Max(0, obstruction.distance - .01f);
            origin += offset;
            if (!game.Round.ThrowCan(r.Id, now, c.CanCooldown)) return;
            int index = playerIndex * c.CansPerRound + c.CansPerRound - r.Cans - 1;
            var s = new OneBulletCanState { Active = true, Position = origin,
                Velocity = aim * c.CanSpeed + Vector3.up * c.CanLift, LaunchedAt = now, UpdatedAt = now };
            states[index] = s; StateChanged?.Invoke(index, s); game.NotifyChanged();
        }
        public void ApplyState(int index, OneBulletCanState state)
        {
            if (index < 0 || index >= states.Length || state.LaunchedAt < game.Round.BeginsAt) return;
            var old = states[index]; states[index] = state;
            if (state.Bounces > old.Bounces && NetworkClock.Now - state.ImpactAt < .3)
                Impact?.Invoke(state.ImpactPoint, state.ImpactSpeed);
        }
    }
}
