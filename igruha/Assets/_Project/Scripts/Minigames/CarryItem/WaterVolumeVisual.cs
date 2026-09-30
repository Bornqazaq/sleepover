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
        private HorizontalCartWater horizontal;

        public Vector3 SurfacePoint => horizontal != null ? horizontal.SurfacePoint :
            transform.TransformPoint(Vector3.up * filledHeight);

        public void SetLevel(float fraction)
        {
            target = Mathf.Clamp01(fraction);
            if (initialized) return;
            initialized = true;
            surfaces = GetComponentsInChildren<Renderer>();
            horizontal = GetComponent<HorizontalCartWater>();
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
            scale.y = horizontal != null ? 1f : Mathf.Max(EmptyLevel, level);
            transform.localScale = scale;
            if (horizontal != null) horizontal.SetLevel(level);
            foreach (var surface in surfaces) surface.enabled = level > EmptyLevel;
        }
    }
}
