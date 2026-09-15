using UnityEngine;

namespace Igruha.Core.UI
{
    /// <summary>Fits the complete selection board inside its existing HUD canvas.</summary>
    public sealed class CharacterSelectionLayout : MonoBehaviour
    {
        private RectTransform root;
        private Vector2 lastSize;
        private void OnEnable() { root = (RectTransform)transform; lastSize = Vector2.zero; Fit(); }
        private void LateUpdate() { Fit(); }
        private void Fit()
        {
            if (root == null || !(root.parent is RectTransform parent)) return;
            Vector2 size = parent.rect.size;
            if (size == lastSize) return;
            lastSize = size;
            float scale = Mathf.Min(size.x / 1280f, size.y / 720f);
            root.localScale = Vector3.one * Mathf.Max(.01f, scale);
        }
    }
}
