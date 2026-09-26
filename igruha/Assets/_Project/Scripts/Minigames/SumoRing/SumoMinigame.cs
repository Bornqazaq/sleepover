using System;
using System.Collections.Generic;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.UI;
using UnityEngine;

namespace Igruha.Minigames.SumoRing
{
    public sealed class SumoMinigame : MinigameControllerBase, IPushButtonOverride
    {
        [SerializeField] private SumoConfig config;
        [SerializeField] private SumoArena arena;
        [SerializeField] private Transform recovery;
        [SerializeField] private Camera gameCamera;
        [SerializeField] private SpectatorCamera spectator;
        [SerializeField] private GameObject timerPlate;
        private readonly SumoRound round = new SumoRound();
        private readonly List<SumoParticipant> participants = new List<SumoParticipant>(8);
        private readonly List<SessionPlayer> alive = new List<SessionPlayer>(8);
        private readonly List<int> ids = new List<int>(8), fallen = new List<int>(8), leaving = new List<int>(8);
        private SumoParticipant local;
        private bool restored, countdownFinished;
        private int shownSecond = -1, shownAlive = -1, shownRing = -1;
        private string[,] collapseStatus;
        private string[] warningStatus, finalStatus;
        private const float AimReleaseGrace = .1f;
        public SumoConfig Config => config;
        public SumoRound Round => round;
        public IReadOnlyList<SumoParticipant> Participants => participants;
        public bool LocalRosterReady => participants.Count > 0;
        public bool Running => RoundActive && round.Ready;
        public double Elapsed => round.Ready ? NetworkClock.Now - round.BeginsAt : -config.Countdown;
        public event Action Changed;
        public event Action<int, Vector3> Eliminated;
        public event Action FightStarted;
        public event Action FightEnded;
        protected override float RoundDuration => 0;
        protected override void OnPlayersReady()
        {
            participants.Clear(); alive.Clear(); ids.Clear(); leaving.Clear(); local = null; restored = false;
            foreach (var player in Players)
            {
                ids.Add(player.Id);
                if (player.Avatar == null) continue;
                var p = new SumoParticipant(player, this, config, recovery);
                participants.Add(p); alive.Add(player); p.Elimination.BodyHidden += OnBodyHidden;
                if (p.Input != null && p.Input.LocallyControlled) local = p;
            }
            // Build HUD text outside the frame loop; only swap cached strings during play.
            int maxSeconds = Mathf.CeilToInt(config.CollapseAt(config.RingCount - 1) + config.Countdown);
            collapseStatus = new string[maxSeconds + 1, ids.Count + 1];
            warningStatus = new string[ids.Count + 1]; finalStatus = new string[ids.Count + 1];
            for (int n = 0; n <= ids.Count; n++)
            {
                string suffix = $"   ·   НА РИНГЕ {n}";
                warningStatus[n] = "КРАЙ ОСЫПАЕТСЯ — К ЦЕНТРУ!" + suffix;
                finalStatus[n] = "ФИНАЛЬНЫЙ КРУГ" + suffix;
                for (int s = 0; s <= maxSeconds; s++) collapseStatus[s, n] = $"ОБВАЛ ЧЕРЕЗ {s} С" + suffix;
            }
        }
        protected override void OnRoundStarted()
        {
            countdownFinished = false; shownSecond = shownAlive = shownRing = -1;
            if (HasAuthority) round.Reset(ids, NetworkClock.Now + config.Countdown);
            Timer?.StopTimer(); arena.ResetArena();
            if (timerPlate != null) timerPlate.SetActive(false);
            foreach (var p in participants) if (p.Motor != null) p.Motor.MovementLocked = true;
            Changed?.Invoke();
        }
        private void Update()
        {
            if (!Running) return;
            double elapsed = Elapsed;
            if (!countdownFinished)
            {
                Hud?.ShowCountdown(Mathf.Max(0, (float)-elapsed));
                if (elapsed >= 0)
                {
                    countdownFinished = true; Hud?.HideCountdown();
                    foreach (var p in participants) if (!p.Dead && p.Motor != null) p.Motor.MovementLocked = false;
                    FightStarted?.Invoke();
                }
            }
            if (local?.Motor != null && local.AimUntil > 0 && NetworkClock.Now > local.AimUntil)
            { local.Motor.FacingOverride = null; local.AimUntil = 0; }
            int ring = config.NextRing(elapsed);
            int seconds = ring < config.RingCount ? Mathf.CeilToInt((float)(config.CollapseAt(ring) - elapsed)) : 0;
            if (shownSecond == seconds && shownAlive == round.AliveCount && shownRing == ring) return;
            shownSecond = seconds; shownAlive = round.AliveCount; shownRing = ring;
            int count = Mathf.Clamp(shownAlive, 0, ids.Count);
            Hud?.ShowStatus(ring == config.RingCount ? finalStatus[count] : seconds <= config.Warning ? warningStatus[count] : collapseStatus[Mathf.Clamp(seconds, 0, collapseStatus.GetLength(0) - 1), count]);
        }
        private void FixedUpdate()
        {
            if (!Running) return;
            foreach (var p in participants) p.RememberPosition();
            if (!HasAuthority) return;
            double now = NetworkClock.Now;
            if (round.Finished)
            {
                if (now >= round.FinishedAt + config.FallSeconds) EndMinigame();
                return;
            }
            fallen.Clear(); fallen.AddRange(leaving); leaving.Clear();
            foreach (var p in participants)
            {
                if (round.Find(p.Player.Id)?.Alive != true) continue;
                if (p.Motor == null || (Elapsed >= 0 && config.HasFallen(p.LastPosition, Elapsed))) fallen.Add(p.Player.Id);
            }
            bool changed = round.Eliminate(fallen, now);
            // Solo practice remains playable until the only player falls.
            if ((ids.Count > 1 && round.AliveCount <= 1) || round.AliveCount == 0)
            { round.Finish(now); changed = true; }
            if (!changed) return;
            ApplySnapshotPresentation(); Changed?.Invoke();
        }
        public bool HandlePushButton(PlayerController motor)
        {
            if (!Running || !countdownFinished || round.Finished) return true;
            foreach (var p in participants)
            {
                if (p.Motor != motor) continue;
                if (p.Dead) return true;
                if (gameCamera != null && p.Input != null && p.Input.LocallyControlled)
                {
                    Vector3 forward = gameCamera.transform.forward; forward.y = 0;
                    if (forward.sqrMagnitude > .001f) motor.FacingOverride = forward.normalized;
                    p.AimUntil = NetworkClock.Now + motor.Config.PunchImpactDelay + AimReleaseGrace;
                }
                return false;
            }
            return true;
        }
        public void ApplySnapshotPresentation()
        {
            if (!RoundActive) return;
            foreach (var p in participants)
            {
                if (p.Dead || round.Find(p.Player.Id)?.Alive != false) continue;
                alive.Remove(p.Player); p.Eliminate(); Eliminated?.Invoke(p.Player.Id, p.LastPosition);
            }
        }
        public void Leave(int id)
        { if (HasAuthority && RoundActive && !leaving.Contains(id)) leaving.Add(id); }
        protected override void OnPlayerLeftRound(int playerId) => Leave(playerId);
        private void OnBodyHidden(PlayerElimination elimination)
        { if (local != null && local.Elimination == elimination && RoundActive) spectator?.Activate(alive); }
        public override void BeginViewing() => spectator?.Activate(alive);
        protected override void OnRoundEnded()
        {
            if (HasAuthority) round.Finish(NetworkClock.Now);
            RestorePlayers(); Hud?.HideCountdown(); Hud?.HideStatus(); Changed?.Invoke(); FightEnded?.Invoke();
        }
        private void RestorePlayers()
        {
            if (restored) return;
            restored = true; spectator?.Deactivate();
            foreach (var p in participants)
            {
                if (p.Elimination != null) p.Elimination.BodyHidden -= OnBodyHidden;
                p.Restore();
            }
        }
        protected override void OnDisable() { RestorePlayers(); base.OnDisable(); }
        public override string ResultMetricTitle => "НА РИНГЕ";
        public override RoundResultDetail GetResultDetail(int playerId)
        {
            var p = round.Find(playerId);
            return p != null ? new RoundResultDetail($"{p.Life:0.0} с", p.Alive ? "Устоял до конца" : "Выбыл за край") : new RoundResultDetail("—");
        }
        protected override void CollectResults(MinigameResults results) => round.Collect(results);
    }
}
