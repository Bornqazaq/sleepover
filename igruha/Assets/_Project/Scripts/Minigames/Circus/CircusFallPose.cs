using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Igruha.Core.Player;

namespace Igruha.Minigames.Circus
{
    /// <summary>Airborne portion of an existing humanoid clip, scoped to an opened cage.</summary>
    [DefaultExecutionOrder(140)]
    public sealed class CircusFallPose : MonoBehaviour
    {
        private PlayerController player;
        private Animator animator;
        private PlayableGraph graph;
        private AnimationClipPlayable pose;
        private float startedAt, floorY;
        private bool airborne;
        public bool IsPresenting => graph.IsValid();

        public void Begin(AnimationClip clip, float cageFloorY)
        {
            Clear();
            player = GetComponent<PlayerController>();
            animator = GetComponentInChildren<Animator>();
            if (clip == null || animator == null) return;
            floorY = cageFloorY;
            startedAt = Time.time;
            airborne = false;
            graph = PlayableGraph.Create("Circus cage fall");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            pose = AnimationClipPlayable.Create(graph, clip);
            pose.SetApplyFootIK(false);
            AnimationPlayableOutput.Create(graph, "Airborne", animator).SetSourcePlayable(pose);
            graph.Play();
        }

        private void LateUpdate()
        {
            if (!graph.IsValid()) return;
            airborne |= transform.position.y < floorY - .2f;
            bool landed = airborne && (player.enabled ? player.IsGrounded :
                Physics.Raycast(transform.position + Vector3.up * .12f, Vector3.down, .23f,
                    LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore));
            if (landed || Time.time - startedAt > 4 ||
                (TryGetComponent(out CircusKnockout knockout) && knockout.IsEliminated))
            { Clear(); return; }
            // Skip takeoff and ground impact; release back to the regular controller on landing.
            pose.SetTime(Mathf.Min(.64f, .18f + (Time.time - startedAt) * .65f));
            graph.Evaluate(0);
        }

        public void Clear()
        {
            if (!graph.IsValid()) return;
            graph.Destroy();
            if (animator != null) animator.Update(0);
        }
        private void OnDisable() => Clear();
    }
}
