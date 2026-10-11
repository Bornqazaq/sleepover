using Igruha.Core.Player;
using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>
    /// Pit contact uses the current animated body, including on a host whose camera
    /// faces away. Keep that pose evaluating until the round finishes, then restore
    /// the character's original culling policy. Player prefabs remain untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CircusAnimationCullingScope : MonoBehaviour
    {
        private Animator animator;
        private AnimatorCullingMode previousMode;
        private bool active;

        public bool IsActive => active;

        public static void Bind(PlayerController player)
        {
            if (player == null) return;
            if (!player.TryGetComponent(out CircusAnimationCullingScope scope))
                scope = player.gameObject.AddComponent<CircusAnimationCullingScope>();
            scope.Begin();
        }

        public static void Release(PlayerController player)
        {
            if (player != null && player.TryGetComponent(out CircusAnimationCullingScope scope)) scope.Restore();
        }

        private void Begin()
        {
            if (active) return;
            animator = GetComponentInChildren<Animator>(true);
            if (animator == null) return;
            previousMode = animator.cullingMode;
            active = true;
            enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        private void Restore()
        {
            if (!active) return;
            if (animator != null) animator.cullingMode = previousMode;
            active = false;
        }

        private void OnDisable() => Restore();
        private void OnDestroy() => Restore();
    }
}
