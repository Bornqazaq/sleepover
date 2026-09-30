using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Smooths the displayed waterline, never the authoritative amount of water.</summary>
    public sealed class WaterVolumeVisual : MonoBehaviour
    {
        [SerializeField] private float filledHeight = 0.44f;
        private const float LevelSpeed = 0.8f;
        private const float EmptyLevel = 0.001f;
        private Renderer[] surfaces;
        private float target;
        private float level;
        private bool initialized;

        public Vector3 SurfacePoint => transform.TransformPoint(Vector3.up * filledHeight);

        public void SetLevel(float fraction)
        {
            target = Mathf.Clamp01(fraction);
            if (initialized) return;
            initialized = true;
            surfaces = GetComponentsInChildren<Renderer>();
            level = target;
            Apply();
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            level = Mathf.MoveTowards(level, target, Time.deltaTime * LevelSpeed);
            Apply();
        }

        private void Apply()
        {
            Vector3 scale = transform.localScale;
            scale.y = Mathf.Max(EmptyLevel, level);
            transform.localScale = scale;
            foreach (var surface in surfaces) surface.enabled = level > EmptyLevel;
        }
    }
}
