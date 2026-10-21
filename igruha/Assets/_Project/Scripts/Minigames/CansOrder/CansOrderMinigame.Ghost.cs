using System.Collections.Generic;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using UnityEngine;

namespace Igruha.Minigames.CansOrder
{
    public sealed partial class CansOrderMinigame
    {
        private CansGhostController ghost;
        private readonly List<CansGhostController> ghosts = new List<CansGhostController>(8);
        private readonly CansGhostTrickHistory ghostTricks = new CansGhostTrickHistory();
        private bool localSolvedNoticeShown;
        private float localSolvedNoticeUntil;
        public bool LocalSolveCelebrating => !localSolvedNoticeShown || Time.unscaledTime < localSolvedNoticeUntil;

        private void UpdateSolvedNotice()
        {
            // Solved is already an adjudicated, persistent result. A delayed client
            // can receive it after Reveal has ended; it must not wait another turn.
            if (localSolvedNoticeShown || !GhostGameplay || ghost == null) return;
            var local = FindLocal();
            if (local == null || !local.Entry.Solved) return;
            localSolvedNoticeShown = true;
            localSolvedNoticeUntil = Time.unscaledTime + CansGhostRules.SolvedSeconds;
            ghost.ShowSolved(round.CanCount, NotSolvedCount > 0);
        }
        public IReadOnlyList<CansGhostController> Ghosts => ghosts;
        public CansGhostController LocalGhost
        {
            get
            {
                foreach (var role in ghosts) if (role.IsLocalGhost) return role;
                return null;
            }
        }
        public CansGhostController GetGhost(int owner)
        {
            foreach (var role in ghosts) if (role.State.OwnerId == owner) return role;
            return null;
        }
        private CansGhostController CreateGhost()
        {
            var role = ghosts.Count == 0 ? ghost : ghost.CreatePeer();
            ghosts.Add(role);
            return role;
        }
        private void ResetGhosts()
        {
            localSolvedNoticeShown = false;
            localSolvedNoticeUntil = 0;
            foreach (var role in ghosts)
            {
                role.ResetRound();
                if (role != ghost) Destroy(role.gameObject);
            }
            ghosts.Clear(); ghostTricks.Clear();
            if (ghost != null) ghost.ResetRound();
        }
        private void EndGhosts()
        {
            foreach (var role in ghosts) if (role != null) role.EndRound();
            if (ghost != null && ghosts.Count == 0) ghost.EndRound();
        }
        public void ReceiveGhost(CansGhostSnapshot snapshot)
        {
            if (ghost == null || HasAuthority || Phase == MinigamePhase.Results) return;
            var role = GetGhost(snapshot.State.OwnerId) ?? CreateGhost();
            role.Receive(snapshot.State, snapshot.Pose);
            ghostTricks.Record(snapshot.State.First);
            ghostTricks.Record(snapshot.State.Second);
        }
        public byte NextGhostTrickSerial => ghostTricks.NextSerial;
        public byte AppliedGhostRevision(int id) => ghostTricks.Revision(id, round.Circle, true);
        public byte RequiredGhostRevision(int id) => ghostTricks.Revision(id, round.Circle, false);
        public void CommitGhostTrick(CansGhostTrick trick)
        {
            ghostTricks.Record(trick);
            ApplyPendingGhostTricks();
        }
        private void ApplyPendingGhostTricks()
        {
            if (!GhostGameplay) return;
            while (ghostTricks.TryPeekNext(out var trick))
            {
                if (!ApplyGhostTrick(trick)) return;
                ghostTricks.Consume(trick);
                GetGhost(trick.OwnerId)?.PlayTrick(trick.TargetId);
            }
        }
        public void NotifyGhostVictim() { if (ghost != null) ghost.NotifyVictim(); }
        public bool GhostAuthority => HasAuthority;
        public bool GhostGameplay => Phase == MinigamePhase.Round && !IsPractice && !matchOver;
        public float GhostFlightRadius => arenaConfig != null ? arenaConfig.CageRingRadius + arenaConfig.CageInnerSize : 14f;
        public float GhostFlightCeiling => arenaConfig != null ?
            arenaConfig.GetCageBottomHeight(arenaConfig.MaxLevelSteps) + 3f : 15f;

        public bool TryGetGhostPlayer(int id, out PlayerController avatar, out CanShelf shelf, out bool local)
        {
            var c = Find(id);
            avatar = c?.Session?.Avatar; shelf = c?.Shelf; local = c != null && c.LocallyControlled;
            return c != null && avatar != null;
        }

        public bool IsGhostSolverPresent(int id)
        {
            var c = Find(id);
            return c != null && c.Entry.Alive && c.Entry.Solved && c.Session?.Avatar != null;
        }

        public void PrepareGhostBody(int id)
        {
            var c = Find(id);
            if (c == null) return;
            if (c.ShelfLocked && c.Session?.Avatar != null)
            {
                c.Session.Avatar.MovementLocked = c.MovementWasLocked;
                c.ShelfLocked = false;
            }
            if (c.Bot != null) c.Bot.Disarm();
            if (c.LocallyControlled) shelfVisibility?.SetActive(false);
        }

        private void AwardSolvedGhosts()
        {
            if (ghost == null || !GhostGameplay) return;
            if (NotSolvedCount == 0) { EndGhosts(); return; }
            foreach (var c in contestants)
            {
                if (!c.Entry.Alive || !c.Entry.Solved || GetGhost(c.Entry.PlayerId) != null) continue;
                CreateGhost().Award(c.Entry.PlayerId, c.Cage.transform.position +
                    c.Cage.transform.forward * 3.4f + Vector3.up * 1.2f, c.Cage.transform.eulerAngles.y);
            }
        }

        public bool CanGhostTrick(int sender, int target, Vector3 ghostPosition, double now)
        {
            var c = Find(target);
            var role = GetGhost(sender);
            return GhostGameplay && role != null && c?.Shelf?.Board != null &&
                CansGhostRules.CanTrick(role.State, sender, target, round.Circle,
                    Stage == StagePlacement, c.Entry.Alive, c.Entry.Solved, c.Entry.Confirmed,
                    round.CanCount, stageState.StageRemaining,
                    Vector3.Distance(ghostPosition, c.Shelf.Board.position), now);
        }

        public int GhostTarget(Vector3 position, Vector3 forward)
        {
            int target = -1;
            float best = float.MinValue;
            foreach (var c in contestants)
            {
                if (!c.Entry.Alive || c.Entry.Solved || c.Shelf?.Board == null) continue;
                Vector3 delta = c.Shelf.Board.position + Vector3.up * .6f - position;
                float distance = delta.magnitude;
                float facing = distance > .01f ? Vector3.Dot(forward, delta / distance) : 1f;
                if (distance > CansGhostRules.Reach || facing < .15f) continue;
                float score = facing * 3f - distance * .15f;
                if (score <= best) continue;
                best = score; target = c.Entry.PlayerId;
            }
            return target;
        }

        public string GhostTargetName(int id)
        {
            var c = Find(id);
            return c != null ? DuelName(c.Cage) : string.Empty;
        }

        public bool ApplyGhostTrick(CansGhostTrick trick)
        {
            var c = Find(trick.TargetId);
            // The roster can remove a departed target before a delayed trick snapshot
            // is processed. Consume that record so the second trick can still arrive.
            if (c == null) return true;
            if (c.Shelf == null || c.Shelf.SlotCount == 0 || round.Circle < trick.Circle) return false;
            // Other peers have decorative shelves. They animate the same slots without receiving the private answer.
            if (!c.Shelf.ApplyGhostSwap(trick.FirstSlot, trick.SecondSlot)) return false;
            if (c.LocallyControlled)
            {
                NotifyGhostVictim();
                localHud?.RefreshResult();
            }
            return true;
        }
    }
}
