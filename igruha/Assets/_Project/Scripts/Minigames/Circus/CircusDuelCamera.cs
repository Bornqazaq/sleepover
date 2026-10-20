using Igruha.Core.CameraSystems;
using Igruha.Minigames.CansOrder;
using Igruha.Core.Minigame;
using Unity.Cinemachine;
using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>Three brief positions of the minigame's existing fixed camera; the player rig is untouched.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class CircusDuelCamera : MonoBehaviour
    {
        [SerializeField] private MinigameControllerBase game;
        [SerializeField] private MinigameStageState stage;
        [SerializeField] private byte introStage;
        [SerializeField] private MinigameCameraController cameras;
        [SerializeField] private CinemachineCamera view;
        [SerializeField] private CanShelf[] privateShelves = System.Array.Empty<CanShelf>();
        private const float ZoomSeconds = .27f;
        private const float CloseDistance = 3.7f;
        private const float CloseHeight = .85f;
        private const float WideDistance = 11f;
        private const float WideHeight = 2.8f;
        private const float AimHeight = 1.25f;
        private const float CloseTravel = .3f;
        private const float CloseSideOffset = .7f;
        private const float WideTravel = .5f;
        private CameraMode returnMode;
        private Transform returnTarget;
        private Vector3 returnPosition;
        private Quaternion returnRotation;
        private Vector3 openingPosition;
        private Quaternion openingRotation;
        private bool active;
        private int cutReturnFrame = -1, lastShot = -1;
        public bool IsActive => active;

        private void OnEnable()
        {
            if (stage != null) stage.StageStarted += StageStarted;
            CinemachineCore.BlendCreatedEvent.AddListener(CutBlend);
        }

        private void StageStarted(byte next)
        {
            // Runs before the rules open the shelf/button view using this same fixed rig.
            if (next != introStage) Restore();
        }

        private void CutBlend(CinemachineCore.BlendEventParams args)
        {
            if (view == null || args.Blend == null) return;
            if (active && ReferenceEquals(args.Blend.CamB, view) ||
                Time.frameCount <= cutReturnFrame && ReferenceEquals(args.Blend.CamA, view))
                args.Blend.Duration = 0;
        }

        public void Render(CageStation first, CageStation second, int cue, float elapsed)
        {
            if (cue < 0 || cue >= 4 || game == null || game.Phase != MinigamePhase.Round || stage.Stage != introStage)
            { Restore(); return; }
            if (cameras == null || view == null || first == null || second == null) return;
            if (!active)
            {
                returnMode = cameras.CurrentMode; returnTarget = cameras.CurrentTarget;
                returnPosition = view.transform.position; returnRotation = view.transform.rotation;
                var main = Camera.main;
                openingPosition = main != null ? main.transform.position : view.transform.position;
                openingRotation = main != null ? main.transform.rotation : view.transform.rotation;
                active = true; lastShot = -1;
                for (int i = 0; i < privateShelves.Length; i++) privateShelves[i]?.SetPresentationHidden(true);
            }

            int shot = cue <= 1 ? 0 : cue == 2 ? 1 : 2;
            float shotStart = cue == 1 ? CircusDuelState.BlackoutSeconds :
                cue == 2 ? CircusDuelState.SecondSpotAt : CircusDuelState.BothSpotsAt;
            float shotEnd = cue == 1 ? CircusDuelState.SecondSpotAt :
                cue == 2 ? CircusDuelState.BothSpotsAt : CircusDuelState.IntroSeconds;
            float progress = Mathf.SmoothStep(0, 1, (elapsed - shotStart) / (shotEnd - shotStart));
            Vector3 aim, position;
            if (shot < 2)
            {
                Vector3 floor = (shot == 0 ? first : second).transform.position;
                Vector3 radial = new Vector3(floor.x, 0, floor.z).normalized;
                aim = floor + Vector3.up * AimHeight;
                position = floor - radial * (CloseDistance + CloseTravel * (1f - progress)) +
                    Vector3.Cross(radial, Vector3.up) * CloseSideOffset + Vector3.up * CloseHeight;
            }
            else
            {
                Vector3 a = first.transform.position, b = second.transform.position;
                Vector3 side = Vector3.Cross(b - a, Vector3.up).normalized;
                aim = (a + b) * .5f + Vector3.up * AimHeight;
                position = (a + b) * .5f + side * (WideDistance + WideTravel * (1f - progress)) + Vector3.up * WideHeight;
            }
            Quaternion rotation = Quaternion.LookRotation(aim - position);
            if (cue == 0)
            {
                float zoom = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / ZoomSeconds), 3);
                position = Vector3.Lerp(openingPosition, position, zoom);
                rotation = Quaternion.Slerp(openingRotation, rotation, zoom);
            }
            if (elapsed >= CircusDuelState.ReturnAt)
            {
                float back = Mathf.SmoothStep(0, 1, (elapsed - CircusDuelState.ReturnAt) /
                    (CircusDuelState.IntroSeconds - CircusDuelState.ReturnAt));
                position = Vector3.Lerp(position, openingPosition, back);
                rotation = Quaternion.Slerp(rotation, openingRotation, back);
            }
            view.transform.SetPositionAndRotation(position, rotation);
            if (lastShot != shot || cameras.CurrentMode != CameraMode.Fixed)
            {
                lastShot = shot;
                view.PreviousStateIsValid = false;
                cameras.Apply(CameraMode.Fixed, view.transform);
            }
        }

        public void Restore()
        {
            if (!active) return;
            active = false; cutReturnFrame = Time.frameCount + 1;
            for (int i = 0; i < privateShelves.Length; i++) privateShelves[i]?.SetPresentationHidden(false);
            if (view != null) view.transform.SetPositionAndRotation(returnPosition, returnRotation);
            if (cameras != null && returnTarget != null) cameras.Apply(returnMode, returnTarget);
        }

        private void OnDisable()
        {
            Restore();
            if (stage != null) stage.StageStarted -= StageStarted;
            CinemachineCore.BlendCreatedEvent.RemoveListener(CutBlend);
        }
    }
}
