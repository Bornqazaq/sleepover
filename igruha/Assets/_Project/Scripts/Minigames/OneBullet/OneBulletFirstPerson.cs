using Igruha.Core.CameraSystems;
using Igruha.Core.Player;
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
        [SerializeField] private CharacterRoster roster;
        [SerializeField] private OneBulletViewArms[] armPrefabs = System.Array.Empty<OneBulletViewArms>();
        [SerializeField] private float hipFieldOfView = 72f, aimFieldOfView = 48f, aimSpeed = 7f;
        [SerializeField] private Vector3 hipPosition = new Vector3(.17f, -.16f, .46f);
        [SerializeField] private Vector3 aimPosition = new Vector3(0, -.094f, .46f);
        private const float ViewScale = 1f, PitchLimit = 80f;
        private static readonly Vector3 StudioPosition = new Vector3(1000, 1000, 1000);
        private Camera weaponCamera;
        private UniversalAdditionalCameraData outputData;
        private CinemachineCamera lensCamera;
        private Transform viewGun;
        private OneBulletParticipant tracked;
        private float standingEye, aimBlend;
        private OneBulletViewArms arms;
        private float equipBlend, gait, movement, airborne, landing, recoil;
        private float punchElapsed = 10f, punchImpact = .25f;
        private bool leftPunch, wasGrounded, wasArmed;
        private Quaternion previousLook;
        private Vector3 sway;
        private const float EquipSpeed = 5f, MotionDamping = 9f, GaitRate = 10f, PunchRecovery = .28f;
        private const float RecoilRecovery = 6f, JumpResponse = 5f, AimMotion = .12f, LowerWeaponDistance = .18f;
        private const float TurnLag = .0002f, MaximumSway = .025f, StrideSide = .008f, StrideRise = .009f;
        private const float BreathRise = .002f, BreathRate = .45f, AirRise = .045f, LandingDip = .04f, RecoilPitch = 13f;
        private const float IdleGait = 1.6f, SwingRise = .012f, SwingReach = .022f, IdleSupportDrop = .09f;
        private const float AirRoll = 3f, VisibleBlend = .01f, MinimumDelta = .001f;
        private static readonly Vector3 RecoilTravel = new Vector3(0, .025f, -.065f);
        private static readonly Vector3 GripWrist = new Vector3(.028f, -.06f, -.115f);
        private static readonly Vector3 SupportWrist = new Vector3(-.047f, -.085f, -.075f);
        private static readonly Vector3 PunchTarget = new Vector3(0, -.06f, .61f);
        private static readonly Vector3 LeftGuard = new Vector3(-.19f, -.23f, .40f), RightGuard = new Vector3(.19f, -.23f, .40f);
        private static readonly Quaternion LeftPalm = Quaternion.LookRotation(Vector3.forward, Vector3.left);
        private static readonly Quaternion RightPalm = Quaternion.LookRotation(Vector3.forward, Vector3.right);
        public bool IsAiming { get; private set; }
        public float AimBlend => aimBlend;
        public bool WeaponVisible => weaponCamera != null && weaponCamera.enabled && viewGun.gameObject.activeSelf;
        public bool HandsVisible => weaponCamera != null && weaponCamera.enabled && arms != null;

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
            game.Changed += BindLocal;
            game.Shot += OnShot;
            previousLook = rig.transform.rotation;
        }
        private void Start() => BindLocal();
        private void BindLocal()
        {
            var local = game.LocalParticipant;
            if (local == tracked) return;
            if (tracked?.Push != null) tracked.Push.PunchStarted -= OnPunch;
            tracked = local;
            if (arms != null) Destroy(arms.gameObject);
            arms = null;
            if (tracked?.Capsule == null) return;
            standingEye = OneBulletMinigame.EyeHeight(tracked.Capsule);
            wasGrounded = tracked.Motor.IsGrounded;
            if (tracked.Push != null) tracked.Push.PunchStarted += OnPunch;
            int index = tracked.Player.CharacterIndex;
            if (index < 0 && roster != null)
                for (int i = 0; i < roster.Characters.Count; i++)
                    if (tracked.Motor.name.StartsWith(roster.Characters[i].Prefab.name, System.StringComparison.Ordinal)) { index = i; break; }
            if (index >= 0 && index < armPrefabs.Length && armPrefabs[index] != null)
                arms = Instantiate(armPrefabs[index], weaponCamera.transform, false);
        }
        private void OnPunch()
        {
            if (tracked?.Motor == null) return;
            punchElapsed = 0;
            punchImpact = tracked.Motor.Config.PunchImpactDelay;
            leftPunch = game.LocalArmed || !leftPunch;
        }
        private void OnShot(Vector3 origin, Vector3 end, bool hit)
        {
            if (wasArmed && tracked?.Motor != null && (origin - OneBulletMinigame.ShotOrigin(tracked)).sqrMagnitude < 1f) recoil = 1f;
        }
        private void Update()
        {
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
            float dt = Time.deltaTime;
            bool armed = game.LocalArmed;
            wasArmed = armed;
            equipBlend = Mathf.MoveTowards(equipBlend, armed || recoil > VisibleBlend ? 1 : 0, dt * EquipSpeed);
            punchElapsed += dt; recoil = Mathf.MoveTowards(recoil, 0, dt * RecoilRecovery);
            weaponCamera.enabled = viewingSelf && (arms != null || equipBlend > VisibleBlend);
            viewGun.gameObject.SetActive(equipBlend > VisibleBlend);
            if (viewingSelf)
            {
                float targetSpeed = game.LocalInputAvailable && !tracked.Motor.MovementLocked ? tracked.Motor.NormalizedSpeed : 0;
                movement = Mathf.Lerp(movement, targetSpeed, 1 - Mathf.Exp(-MotionDamping * dt));
                gait += dt * Mathf.Lerp(IdleGait, GaitRate, movement);
                bool grounded = tracked.Motor.IsGrounded;
                if (grounded && !wasGrounded) landing = airborne;
                wasGrounded = grounded;
                airborne = Mathf.MoveTowards(airborne, grounded ? 0 : 1, dt * JumpResponse);
                landing = Mathf.MoveTowards(landing, 0, dt * JumpResponse);
            }
            if (reticle != null) reticle.alpha = 1f - aimBlend;
        }
        private void LateUpdate()
        {
            if (!weaponCamera.enabled) { previousLook = rig.transform.rotation; return; }
            weaponCamera.fieldOfView = lensCamera.Lens.FieldOfView;
            weaponCamera.aspect = output.aspect;
            weaponCamera.transform.rotation = rig.transform.rotation;
            float blend = Mathf.SmoothStep(0, 1, aimBlend), quiet = Mathf.Lerp(1, AimMotion, blend);
            Vector3 turn = (Quaternion.Inverse(previousLook) * rig.transform.rotation).eulerAngles;
            previousLook = rig.transform.rotation;
            float dt = Mathf.Max(MinimumDelta, Time.deltaTime);
            Vector3 lag = new Vector3(-Mathf.DeltaAngle(0, turn.y), Mathf.DeltaAngle(0, turn.x), 0) * (TurnLag / dt);
            sway = Vector3.Lerp(sway, Vector3.ClampMagnitude(lag, MaximumSway), 1 - Mathf.Exp(-MotionDamping * Time.deltaTime));
            Vector3 motion = sway + new Vector3(Mathf.Sin(gait) * StrideSide * movement,
                Mathf.Cos(gait * 2) * StrideRise * movement + Mathf.Sin(gait * BreathRate) * BreathRise + airborne * AirRise - landing * LandingDip, 0);
            motion *= quiet;
            viewGun.localScale = Vector3.one * ViewScale;
            viewGun.localPosition = Vector3.Lerp(hipPosition, aimPosition, blend) + motion +
                Vector3.down * ((1 - equipBlend) * LowerWeaponDistance) + RecoilTravel * recoil;
            viewGun.localRotation = Quaternion.Slerp(Quaternion.Euler(0, -7f, -7f), Quaternion.Euler(-2f, 0, 0), blend) *
                Quaternion.Euler(-recoil * RecoilPitch, 0, airborne * AirRoll * quiet);
            if (arms == null) return;
            Vector3 left = LeftGuard + motion, right = RightGuard + motion;
            left += new Vector3(0, Mathf.Sin(gait) * SwingRise, Mathf.Cos(gait) * SwingReach) * movement * (1 - equipBlend);
            right += new Vector3(0, -Mathf.Sin(gait) * SwingRise, -Mathf.Cos(gait) * SwingReach) * movement * (1 - equipBlend);
            Vector3 grip = weaponCamera.transform.InverseTransformPoint(viewGun.TransformPoint(GripWrist));
            Vector3 support = weaponCamera.transform.InverseTransformPoint(viewGun.TransformPoint(SupportWrist));
            right = Vector3.Lerp(right, grip, equipBlend);
            float supportBlend = equipBlend * Mathf.SmoothStep(0, 1, aimBlend);
            left += Vector3.down * (IdleSupportDrop * equipBlend * (1 - supportBlend));
            left = Vector3.Lerp(left, support, supportBlend);
            Quaternion rightRotation = Quaternion.Slerp(RightPalm, viewGun.localRotation * RightPalm, equipBlend);
            Quaternion leftRotation = Quaternion.Slerp(LeftPalm, viewGun.localRotation * LeftPalm, supportBlend);
            float punch = PunchReach(punchElapsed, punchImpact);
            Vector3 strike = PunchTarget;
            if (leftPunch)
            {
                left = Vector3.LerpUnclamped(left, strike, punch);
                leftRotation = Quaternion.Slerp(leftRotation, Quaternion.LookRotation(Vector3.forward, Vector3.up), punch);
                supportBlend *= 1 - punch;
            }
            else
            {
                right = Vector3.LerpUnclamped(right, strike, punch);
                rightRotation = Quaternion.Slerp(rightRotation, Quaternion.LookRotation(Vector3.forward, Vector3.up), punch);
            }
            arms.Pose(weaponCamera.transform, left, leftRotation, right, rightRotation, equipBlend, supportBlend);
        }
        public static float PunchReach(float elapsed, float impact)
        {
            if (elapsed < 0) return 0;
            return elapsed <= impact ? Mathf.SmoothStep(0, 1, elapsed / Mathf.Max(.01f, impact)) :
                1 - Mathf.SmoothStep(0, 1, (elapsed - impact) / PunchRecovery);
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
            if (tracked?.Push != null) tracked.Push.PunchStarted -= OnPunch;
            if (game != null) { game.Changed -= BindLocal; game.Shot -= OnShot; }
            if (outputData != null && weaponCamera != null) outputData.cameraStack.Remove(weaponCamera);
        }
    }
}
