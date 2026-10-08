using System;
using Igruha.Core.Minigame;
using UnityEngine;

namespace Igruha.Minigames.OneBullet
{
    public sealed class OneBulletStorm : MonoBehaviour
    {
        [SerializeField] private OneBulletStormLayout layout;
        [SerializeField] private Transform[] recoveryPoints;
        private OneBulletMinigame game;
        private int[] casualties = Array.Empty<int>();
        private int lastShownStage, lastShownSafe;
        public OneBulletStormState State { get; } = new OneBulletStormState();
        public OneBulletStormLayout Layout => layout;
        public event Action Changed;
        private void Awake() => game = GetComponent<OneBulletMinigame>();
        private void OnEnable() => game.Died += OnDeath;
        private void OnDisable() => game.Died -= OnDeath;
        public void ResetRound()
        {
            State.Reset(game.Round.Records.Count, layout.FinalStage, game.Round.BeginsAt, game.Config.StormIdle);
            casualties = new int[game.Round.Records.Count];
            lastShownStage = State.Stage; lastShownSafe = State.SafeStage;
            if (game.Authority)
                for (int i = 0; i < game.Participants.Count; i++)
                {
                    var p = game.Participants[i]; if (p.Motor == null) continue;
                    int node = layout.StartNode(State.Stage, i);
                    Vector3 point = layout.StandingPoint(node);
                    int next = FirstNeighbour(node);
                    Vector3 facing = next >= 0 ? layout.Position(next) - point : Vector3.forward;
                    facing.y = 0;
                    p.Motor.RequestTeleport(point, Quaternion.LookRotation(facing.normalized, Vector3.up));
                }
            Changed?.Invoke();
        }
        public float StartYaw(int playerId)
        {
            for (int i = 0; i < game.Participants.Count; i++)
            {
                if (game.Participants[i].Player.Id != playerId) continue;
                int node = layout.StartNode(State.Stage, i), next = FirstNeighbour(node);
                if (next < 0) return 0;
                Vector3 direction = layout.Position(next) - layout.StandingPoint(node);
                return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            }
            return 0;
        }
        private int FirstNeighbour(int node)
        {
            foreach (var e in layout.Edges)
            {
                int other = e.A == node ? e.B : e.B == node ? e.A : -1;
                if (layout.Safe(other, State.Stage)) return other;
            }
            return -1;
        }
        private void OnDeath(int id, Vector3 position)
        {
            if (!game.Authority || game.Round.AliveCount < 2 || game.Round.Finished) return;
            State.Elimination(NetworkClock.Now, layout.FinalStage, game.Config.StormWarning, game.Config.StormIdle);
            RefreshPresentation(); game.NotifyChanged();
        }
        public void Tick(double now)
        {
            if (!game.Authority || !game.Round.IsLive(now)) return;
            if (State.Tick(now, layout.FinalStage, game.Config.StormWarning, game.Config.StormIdle))
            {
                game.RelocateUnsafePickup(); RefreshPresentation(); game.NotifyChanged();
            }
            int count = 0; bool changed = false;
            foreach (var p in game.Participants)
            {
                var r = game.Round.Find(p.Player.Id);
                if (p.Motor == null || r?.Alive != true) continue;
                changed |= game.Round.SetDanger(r.Id, !layout.Safe(p.Motor.Position, State.Stage), now);
                if (r.DangerSince >= 0 && now - r.DangerSince >= game.Config.StormExposure)
                    casualties[count++] = r.Id;
            }
            // Evaluate all exposures before ending the round; equal-tick deaths do not manufacture a survivor.
            if (count > 0) game.EliminateFromStorm(casualties, count);
            if (changed) game.NotifyChanged();
        }
        public bool SafeWeapon(Vector3 point) => layout.Safe(point, State.SafeStage);
        public Transform RecoveryPoint(Vector3 position)
        {
            int node = layout.NearestSafe(position, State.SafeStage);
            return node >= 0 && node < recoveryPoints.Length ? recoveryPoints[node] : null;
        }
        public void RefreshPresentation()
        {
            if (lastShownStage == State.Stage && lastShownSafe == State.SafeStage) return;
            lastShownStage = State.Stage; lastShownSafe = State.SafeStage; Changed?.Invoke();
        }
    }
}
