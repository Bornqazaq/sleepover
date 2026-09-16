using UnityEngine;
using Igruha.Core.Arena;

namespace Igruha.Minigames.Exam
{
    /// <summary>Purely local mechanical dressing, driven by the existing hatch on each peer.</summary>
    public sealed class ExamHatchMechanism : MonoBehaviour
    {
        [System.Serializable] public struct Ram
        {
            public Transform Leaf, Barrel, Rod, Gear;
            public Vector3 Anchor, Attachment;
        }
        public Transform LeftPivot, RightPivot, LeftVisual, RightVisual;
        public Ram[] Rams;
        public ParticleSystem Papers, Steam;
        private HingedFloorHatch hatch;
        private void Awake() { hatch = GetComponent<HingedFloorHatch>(); }
        private void OnEnable()
        {
            if (hatch == null) hatch = GetComponent<HingedFloorHatch>();
            if (hatch != null) hatch.Released += Release;
        }
        private void OnDisable() { if (hatch != null) hatch.Released -= Release; }
        private void Release(HingedFloorHatch source)
        {
            if (Papers != null) Papers.Play();
            if (Steam != null) Steam.Play();
        }
        private static float Dress(Transform pivot, Transform visual)
        {
            float signed = Mathf.DeltaAngle(0, pivot.localEulerAngles.z);
            float angle = Mathf.Abs(signed);
            // Up to the physical release at 25 degrees the visible surface is exact.
            // Afterwards it can fall ahead of the disabled collider and settle on its stop.
            float t = Mathf.Clamp01((angle - 25) / 85);
            float eased = 1 - Mathf.Pow(1 - t, 3);
            float desired = angle <= 25 ? angle : 25 + 85 * eased + 9 * Mathf.Sin(t * Mathf.PI) * Mathf.Sin(t * Mathf.PI);
            visual.localRotation = Quaternion.Euler(0, 0, Mathf.Sign(signed) * (desired - angle));
            return desired;
        }
        private void LateUpdate()
        {
            if (LeftPivot == null || RightPivot == null) return;
            float angle = Dress(LeftPivot, LeftVisual); Dress(RightPivot, RightVisual);
            if (Rams == null) return;
            foreach (var ram in Rams)
            {
                Vector3 start = transform.TransformPoint(ram.Anchor);
                Vector3 end = ram.Leaf.TransformPoint(ram.Attachment);
                Vector3 delta = end - start;
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
                ram.Barrel.SetPositionAndRotation(start, rotation);
                ram.Rod.SetPositionAndRotation(start + delta.normalized * .62f, rotation);
                ram.Rod.localScale = new Vector3(1, Mathf.Max(.08f, delta.magnitude - .62f), 1);
                if (ram.Gear != null) ram.Gear.localRotation = Quaternion.Euler(0, 0, angle * 1.6f);
            }
        }
    }
}
