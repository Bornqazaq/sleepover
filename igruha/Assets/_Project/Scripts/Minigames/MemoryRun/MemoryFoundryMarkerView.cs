using UnityEngine;

namespace Igruha.Minigames.MemoryRun
{
    // Runs after the shared marker has followed its target and applied distance scaling.
    [DefaultExecutionOrder(100)]
    public sealed class MemoryFoundryMarkerView : MonoBehaviour
    {
        [SerializeField] private float clearance = 1.35f;
        private Renderer[] visuals;
        private Camera view;

        private void Awake() => visuals = GetComponentsInChildren<Renderer>(true);

        private void LateUpdate()
        {
            if (view == null || !view.isActiveAndEnabled) view = Camera.main;
            if (view == null) return;
            foreach (var visual in visuals)
            {
                var bounds = visual.bounds;
                bool visible = Vector3.Distance(view.transform.position, bounds.center) > bounds.extents.magnitude + clearance;
                visual.enabled = visible;
            }
        }
    }
}
