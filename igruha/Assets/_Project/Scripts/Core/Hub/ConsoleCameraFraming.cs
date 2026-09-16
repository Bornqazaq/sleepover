using Unity.Cinemachine;
using UnityEngine;

namespace Igruha.Core.Hub
{
    /// <summary>Frames the physical TV at any viewport aspect, only while its fixed rig is active.</summary>
    [RequireComponent(typeof(CinemachineCamera))]
    public sealed class ConsoleCameraFraming : MonoBehaviour
    {
        [SerializeField] private Camera outputCamera;
        [SerializeField] private RectTransform screen;
        [SerializeField, Min(0.5f)] private float distance = 2f;
        [SerializeField, Range(0.5f, 0.95f)] private float screenFill = 0.88f;
        private const float ScreenClearance = 0.15f;
        private CinemachineCamera rig;
        private float lastAspect;

        private void Awake() => rig = GetComponent<CinemachineCamera>();
        private void OnEnable() => lastAspect = 0f;

        private void Update()
        {
            if (outputCamera == null || screen == null || rig == null) return;
            float aspect = outputCamera.aspect;
            if (Mathf.Approximately(aspect, lastAspect)) return;
            lastAspect = aspect;
            Vector3 scale = screen.lossyScale;
            Vector2 size = new Vector2(screen.rect.width * scale.x, screen.rect.height * scale.y);
            transform.SetPositionAndRotation(screen.position - screen.forward * distance, screen.rotation);
            LensSettings lens = rig.Lens;
            lens.FieldOfView = FitFieldOfView(size, aspect, distance, screenFill);
            // Other avatars can stand at the console: clip the foreground, not their shared renderers.
            lens.NearClipPlane = distance - ScreenClearance;
            rig.Lens = lens;
            rig.PreviousStateIsValid = false;
        }

        public static float FitFieldOfView(Vector2 screenSize, float aspect, float distance, float fill)
        {
            float height = Mathf.Max(screenSize.y, screenSize.x / Mathf.Max(aspect, 0.1f));
            return 2f * Mathf.Atan(height / (2f * distance * fill)) * Mathf.Rad2Deg;
        }
    }
}
