using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Continuous water column ends at the water surface or floor, with an impact spray.</summary>
    public sealed class WaterTapStream : MonoBehaviour
    {
        private const int Segments = 12;
        private const float MaxLength = 2f;
        private const float Ripple = 0.006f;
        private const float SurfaceBottom = 0.48f;
        private const float SurfaceDepth = 0.44f;
        [SerializeField] private LineRenderer stream;
        [SerializeField] private Transform splash;
        private readonly RaycastHit[] hits = new RaycastHit[12];
        private Collider surfaceCollider;
        private WaterCart surfaceCart;
        private float length;
        private float nextSample;

        private void CacheSurface(Collider surface)
        {
            surfaceCollider = surface;
            surfaceCart = surface != null ? surface.GetComponent<WaterCart>() : null;
        }

        private void Update()
        {
            if (Time.time >= nextSample)
            {
                nextSample = Time.time + 0.05f;
                length = MaxLength;
                int count = Physics.RaycastNonAlloc(transform.position, Vector3.down, hits, MaxLength, ~0, QueryTriggerInteraction.Ignore);
                Collider closest = null;
                for (int i = 0; i < count; i++)
                {
                    if (hits[i].distance >= length) continue;
                    length = hits[i].distance;
                    closest = hits[i].collider;
                }
                if (closest != surfaceCollider) CacheSurface(closest);
                if (surfaceCart != null)
                    length = transform.position.y - (surfaceCart.transform.position.y + SurfaceBottom + SurfaceDepth * surfaceCart.Load);
                length = Mathf.Max(0.03f, length);
            }
            float time = Time.time * 18f;
            for (int i = 0; i < Segments; i++)
            {
                float t = i / (float)(Segments - 1);
                stream.SetPosition(i, new Vector3(Mathf.Sin(time - t * 12f) * Ripple * t, -length * t,
                    Mathf.Cos(time * 0.8f - t * 9f) * Ripple * t));
            }
            splash.localPosition = Vector3.down * length;
        }
    }
}
