using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Unity.Cinemachine;
using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>Scene-owned camera positions. The shared orbit rig and its settings stay untouched.</summary>
    [DefaultExecutionOrder(-50)]
    public sealed class HoleInWallCamera : MonoBehaviour
    {
        [SerializeField] private HoleInWallMinigame game;
        [SerializeField] private CinemachineCamera view;
        [SerializeField] private MinigameCameraController controller;
        private const float FollowDistance = 5.5f;
        private const float DeckHeight = 2.65f;
        private const float FollowRate = 7f;
        private const float PoolWallClearance = .6f;
        private Vector3 aim;
        private bool initialized, resultsView;
        private int resultTrack;
        private Transform avatarRoot, head, hips;
        private Rigidbody avatarBody;

        private void OnEnable() { if (game != null) game.ResultsReported += ShowResults; }
        private void OnDisable() { if (game != null) game.ResultsReported -= ShowResults; }

        private void ShowResults(MinigameResults results)
        {
            // Use the first winning lane, independent of who owns this screen or fell last.
            resultTrack = 0;
            foreach (var entry in results.Entries)
                if (entry.Place == 1 && game.TrackOf(entry.PlayerId) != null)
                { resultTrack = game.TrackOf(entry.PlayerId).Index; break; }
            resultsView = true;
            initialized = false;
            PositionView();
        }

        private void LateUpdate() => PositionView();

        private void PositionView()
        {
            var config = game.Config;
            var local = SessionScoreboard.Current?.LocalPlayer;
            var avatar = local?.Avatar;
            var track = local != null ? game.TrackOf(local.Id) : null;
            float x = config.TrackCenterX(track != null ? track.Index : 0);
            if (avatar != null && avatarRoot != avatar.transform)
            {
                avatarRoot = avatar.transform;
                avatarBody = avatar.GetComponent<Rigidbody>();
                var animator = avatar.GetComponentInChildren<Animator>();
                head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
                hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            }
            Vector3 position, target;
            bool onDeck = false;
            if (resultsView)
            {
                // Face the returned participants. Leave the right side for the standings.
                x = config.TrackCenterX(resultTrack) - 1.75f;
                position = new Vector3(x, 2.25f, 5.2f);
                target = new Vector3(x, 1.0f, config.CheckLineZ);
            }
            else
            {
                Vector3 player = avatar != null ? avatar.Position : new Vector3(x, 0, 0);
                bool falling = player.y < config.PlatformSurfaceY - .55f;
                onDeck = !falling;
                x = player.x;
                float targetY = falling ? Mathf.Max(config.PoolBottomY + .7f, player.y + .55f) : 1f;
                target = new Vector3(x, targetY, Mathf.Clamp(player.z, config.ArenaNearZ + 1.8f, config.PlatformFrontZ));
                if (falling && head != null && hips != null)
                {
                    // Falling clips extend sideways from the upright root. Frame the body,
                    // rather than its feet, especially next to the near pool wall.
                    target = (head.position + hips.position) * .5f + (player - avatarRoot.position);
                    target.y = Mathf.Max(config.PoolBottomY + .6f, target.y);
                }
                float z = Mathf.Max(config.ArenaNearZ + PoolWallClearance,
                    Mathf.Min(config.PlatformBackZ - 3.7f, target.z - FollowDistance));
                float backwards = Mathf.Max(0, target.z - z);
                float side = falling ? Mathf.Sqrt(Mathf.Max(0, 3.6f * 3.6f - backwards * backwards)) : 0;
                // Stay inside the pool. Where the back wall limits retreat, step sideways
                // towards the arena centre to keep a full body in view.
                x = target.x + side * (config.TrackCenterX(track != null ? track.Index : 0) < 0 ? 1 : -1);
                x = Mathf.Clamp(x, -config.ArenaWidth * .5f + .7f, config.ArenaWidth * .5f - .7f);
                float y = falling ? Mathf.Max(config.PoolBottomY + 1.1f, target.y + .35f) : DeckHeight;
                if (falling && avatarBody != null && avatarBody.linearVelocity.y > 0)
                    y = Mathf.Min(DeckHeight, Mathf.Max(y, player.y + .9f + avatarBody.linearVelocity.y * .2f));
                position = new Vector3(x, y, z);
            }
            float blend = initialized ? 1f - Mathf.Exp(-FollowRate * Time.deltaTime) : 1f;
            transform.position = Vector3.Lerp(transform.position, position, blend);
            // Recovery may cross deck height in one tick; never damp the view through
            // the underside after the avatar has already landed above it.
            if (onDeck && transform.position.y < DeckHeight - .25f)
                transform.position = new Vector3(transform.position.x, DeckHeight - .25f, transform.position.z);
            aim = Vector3.Lerp(initialized ? aim : target, target, initialized ? 1f - Mathf.Exp(-20f * Time.deltaTime) : 1f);
            transform.rotation = Quaternion.LookRotation(aim - transform.position, Vector3.up);
            if (!initialized) view.PreviousStateIsValid = false;
            initialized = true;
            // Bootstrap also selects Fixed. Reassert only if a late local binding/spectator
            // handoff changed modes; no edits to ThirdPersonCameraRig or its collision mask.
            if (controller.CurrentMode != CameraMode.Fixed) controller.Apply(CameraMode.Fixed, avatar != null ? avatar.transform : null);
        }
    }
}
