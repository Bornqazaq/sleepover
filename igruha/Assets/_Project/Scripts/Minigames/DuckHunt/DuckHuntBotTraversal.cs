using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>Solo debug navigation for the revised jumps. Never attached to network players.</summary>
    internal sealed class DuckHuntBotTraversal
    {
        private const float ReachDistance = .28f;
        private const float HeightTolerance = .35f;
        private const float TakeoffMarginWidths = .8f;
        private const float LandingMarginWidths = 3.8f;
        private const float RecoveryDepthWidths = 1.8f;
        private const float RecoveryEntryWidths = 8.1f;
        // x = progress, y = height above floor, z = depth; matches the blockout pads.
        private static readonly Vector3[] FinalRoute =
        {
            new Vector3(8.5f, .3f, 12.3f), new Vector3(14.5f, 3, 11.5f),
            new Vector3(15.1f, 3, 11.5f), new Vector3(19.4f, 3, 9.5f),
            new Vector3(20.1f, 3, 9.5f), new Vector3(24.4f, 3, 7),
            new Vector3(25.1f, 3, 7), new Vector3(29.4f, 3, 5.5f),
            new Vector3(30.1f, 3, 5.5f), new Vector3(34.4f, 3, 7.5f),
            new Vector3(35.1f, 3, 7.5f), new Vector3(39.4f, 3, 9.5f),
            new Vector3(40.1f, 3, 9.5f), new Vector3(44.4f, 3, 11)
        };

        private readonly DuckHuntArena arena;
        private readonly PlayerController motor;
        private readonly PlayerInputReader input;
        private int finalStep;
        private int recoveryStep = -1;

        public DuckHuntBotTraversal(DuckHuntArena arena, PlayerController motor)
        {
            this.arena = arena;
            this.motor = motor;
            input = motor.GetComponent<PlayerInputReader>();
        }

        public bool TryGetTarget(DuckProgress progress, out Vector3 target)
        {
            target = default;
            int floor = progress.Floor;
            if (floor != arena.FloorCount - 1)
            {
                finalStep = 0;
                recoveryStep = -1;
                float gap = floor == 1 ? 34 : floor == 2 ? 13 : floor == 3 ? 10 : -1;
                if (gap < 0 || progress.Progress < gap - 1.3f || progress.Progress > gap + 2.8f)
                    return false;
                // Preserve the current lane while airborne; don't turn towards a sample inside a gap.
                float depth = arena.transform.InverseTransformPoint(motor.Position).z / arena.Config.CharacterWidth;
                target = arena.GetWorldPoint(floor, gap + LandingMarginWidths, depth);
                if (motor.IsGrounded && progress.Progress >= gap - TakeoffMarginWidths && progress.Progress < gap)
                    input.DriveJump();
                return true;
            }

            if (finalStep >= 2 && motor.IsGrounded && motor.Position.y < arena.GetFloorBaseY(floor) + HeightTolerance)
            {
                finalStep = 0;
                recoveryStep = 0;
            }
            if (recoveryStep >= 0)
            {
                target = arena.GetWorldPoint(floor,
                    recoveryStep == 0 ? progress.Progress : RecoveryEntryWidths,
                    recoveryStep < 2 ? RecoveryDepthWidths : 11.5f);
                if (Reached(target) && ++recoveryStep > 2) recoveryStep = -1;
                return true;
            }

            Vector3 point = FinalRoute[finalStep];
            target = arena.GetWorldPoint(floor, point.x, point.z, point.y);
            if (!Reached(target) || finalStep == FinalRoute.Length - 1) return true;
            if (finalStep >= 2 && finalStep % 2 == 0) input.DriveJump();
            point = FinalRoute[++finalStep];
            target = arena.GetWorldPoint(floor, point.x, point.z, point.y);
            return true;
        }

        private bool Reached(Vector3 target)
        {
            Vector3 delta = target - motor.Position;
            float height = Mathf.Abs(delta.y);
            delta.y = 0;
            return motor.IsGrounded && height < HeightTolerance && delta.sqrMagnitude < ReachDistance * ReachDistance;
        }

        public void DriveTarget(Vector3 target)
        {
            if (motor.MovementLocked || motor.IsKnockedDown)
            {
                input.DriveMove(Vector2.zero);
                return;
            }
            Vector3 delta = target - motor.Position;
            delta.y = 0;
            input.DriveMove(motor.WorldToMoveInput(delta.normalized));
        }
    }
}
