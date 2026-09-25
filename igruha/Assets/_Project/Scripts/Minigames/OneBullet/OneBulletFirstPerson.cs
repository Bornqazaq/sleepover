using Igruha.Core.CameraSystems;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Igruha.Minigames.OneBullet
{
    /// <summary>Scene-local first-person presentation; gameplay and hits remain server authoritative.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class OneBulletFirstPerson : MonoBehaviour
    {
        [SerializeField] private OneBulletMinigame game;
        [SerializeField] private FirstPersonCameraRig rig;
        [SerializeField] private Camera output;
        [SerializeField] private Transform gunSource;
        [SerializeField] private CanvasGroup reticle;
        [SerializeField] private float hipFieldOfView = 72f, aimFieldOfView = 48f, aimSpeed = 7f;
        [SerializeField] private Vector3 hipPosition = new Vector3(.17f, -.22f, .46f);
        [SerializeField] private Vector3 aimPosition = new Vector3(0, -.094f, .46f);
        private const float ViewScale = 1f, PitchLimit = 80f;
        private static readonly Vector3 StudioPosition = new Vector3(10000, 10000, 10000);
        private Camera weaponCamera;
        private UniversalAdditionalCameraData outputData;
        private CinemachineCamera lensCamera;
        private Transform viewGun;
        private OneBulletParticipant tracked;
        private float standingEye, aimBlend;
        public bool IsAiming { get; private set; }
        public float AimBlend => aimBlend;
        public bool WeaponVisible => weaponCamera != null && weaponCamera.enabled;

        private void Awake()
        {
            lensCamera = rig.GetComponent<CinemachineCamera>();
            rig.SetMaxTurnSpeed(0);
            rig.SetPitchLimits(-PitchLimit, PitchLimit);
            rig.SetOwnModelVisible(false);
            rig.SetShoulderOffset(Vector3.zero);
            // An isolated, depth-clearing overlay prevents the close-up gun clipping into walls.
            // Its tiny frustum is outside the arena, so no shared layers or camera masks are changed.
            var host = new GameObject("OneBulletWeaponCamera");
            host.transform.SetParent(transform, false);
            host.transform.position = StudioPosition;
            weaponCamera = host.AddComponent<Camera>();
            weaponCamera.nearClipPlane = .01f;
            weaponCamera.farClipPlane = 2f;
            weaponCamera.cullingMask = 1 << LayerMask.NameToLayer("Ignore Raycast");
            weaponCamera.allowHDR = output.allowHDR;
            weaponCamera.allowMSAA = output.allowMSAA;
            var data = weaponCamera.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Overlay;
            data.renderShadows = false;
            outputData = output.GetUniversalAdditionalCameraData();
            outputData.cameraStack.Add(weaponCamera);
            viewGun = Instantiate(gunSource.gameObject, host.transform).transform;
            viewGun.name = "LocalRevolver";
            viewGun.gameObject.SetActive(true);
            foreach (var part in viewGun.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
            foreach (var renderer in viewGun.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            weaponCamera.enabled = false;
        }
        private void Update()
        {
            var local = game.LocalParticipant;
            if (local != tracked)
            {
                tracked = local;
                if (tracked?.Capsule != null) standingEye = OneBulletMinigame.EyeHeight(tracked.Capsule);
            }
            bool viewingSelf = rig.isActiveAndEnabled && tracked?.Motor != null && !tracked.Dead && game.GameplayActive;
            rig.SetViewDrivenExternally(!viewingSelf || !game.LocalInputAvailable);
            if (viewingSelf)
            {
                // The shared rig caches standing height. Follow this game's crouching capsule locally.
                rig.SetShoulderOffset(Vector3.up * (OneBulletMinigame.EyeHeight(tracked.Capsule) - standingEye));
            }
            IsAiming = viewingSelf && game.LocalArmed && game.LocalInputAvailable && !tracked.Motor.MovementLocked &&
                Mouse.current?.rightButton.isPressed == true;
            aimBlend = Mathf.MoveTowards(aimBlend, IsAiming ? 1f : 0f, Time.unscaledDeltaTime * aimSpeed);
            var lens = lensCamera.Lens;
            lens.FieldOfView = Mathf.Lerp(hipFieldOfView, aimFieldOfView, Mathf.SmoothStep(0, 1, aimBlend));
            lensCamera.Lens = lens;
            weaponCamera.enabled = viewingSelf && game.LocalArmed;
            if (reticle != null) reticle.alpha = 1f - aimBlend;
        }
        private void LateUpdate()
        {
            if (!weaponCamera.enabled) return;
            weaponCamera.fieldOfView = lensCamera.Lens.FieldOfView;
            weaponCamera.aspect = output.aspect;
            weaponCamera.transform.rotation = rig.transform.rotation;
            viewGun.localScale = Vector3.one * ViewScale;
            float blend = Mathf.SmoothStep(0, 1, aimBlend);
            viewGun.localPosition = Vector3.Lerp(hipPosition, aimPosition, blend);
            viewGun.localRotation = Quaternion.Slerp(Quaternion.Euler(0, -7f, -7f), Quaternion.Euler(-2f, 0, 0), blend);
        }
        private void OnDisable()
        {
            IsAiming = false; aimBlend = 0;
            if (weaponCamera != null) weaponCamera.enabled = false;
            if (lensCamera != null)
            {
                var lens = lensCamera.Lens; lens.FieldOfView = hipFieldOfView; lensCamera.Lens = lens;
            }
            if (rig != null) { rig.SetViewDrivenExternally(false); rig.SetShoulderOffset(Vector3.zero); }
        }
        private void OnDestroy()
        {
            if (outputData != null && weaponCamera != null) outputData.cameraStack.Remove(weaponCamera);
        }
    }
}
