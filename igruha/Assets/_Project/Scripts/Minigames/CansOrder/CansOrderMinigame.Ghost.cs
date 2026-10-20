using Igruha.Core.Minigame;
using Igruha.Core.Player;
using UnityEngine;

namespace Igruha.Minigames.CansOrder
{
    public sealed partial class CansOrderMinigame
    {
        private CansGhostController ghost;
        public CansGhostController Ghost => ghost;
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
            c.Bot?.Disarm();
            if (c.LocallyControlled) shelfVisibility?.SetActive(false);
        }

        private void AwardFirstGhost()
        {
            if (ghost == null || !GhostGameplay || ghost.State.Active || ghost.HasAwarded) return;
            Contestant first = null;
            foreach (var c in contestants)
                if (c.Entry.Alive && c.Entry.Solved && (first == null ||
                    c.Entry.ConfirmTime < first.Entry.ConfirmTime ||
                    c.Entry.ConfirmTime == first.Entry.ConfirmTime && c.Entry.PlayerId < first.Entry.PlayerId)) first = c;
            if (first != null && NotSolvedCount > 0)
                ghost.Award(first.Entry.PlayerId, first.Cage.transform.position +
                    first.Cage.transform.forward * 3.4f + Vector3.up * 1.2f, first.Cage.transform.eulerAngles.y);
        }

        public bool CanGhostTrick(int sender, int target, Vector3 ghostPosition, double now)
        {
            var c = Find(target);
            return GhostGameplay && ghost != null && c?.Shelf?.Board != null &&
                CansGhostRules.CanTrick(ghost.State, sender, target, round.Circle,
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
                if (c.Entry.PlayerId == ghost.State.OwnerId || !c.Entry.Alive || c.Entry.Solved || c.Shelf?.Board == null) continue;
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
            c.Shelf.ApplyGhostSwap(trick.FirstSlot, trick.SecondSlot);
            if (c.LocallyControlled)
            {
                ghost.NotifyVictim();
                localHud?.RefreshResult();
            }
            return true;
        }
    }
}
