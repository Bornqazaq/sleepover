using System;
using System.Collections.Generic;
using Igruha.Core.CameraSystems;
using Igruha.Core.Interaction;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Minigames.Circus;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.Minigames.Stopwatch
{
    /// <summary>Owner-only button shot. Input and measurement remain entirely in CageButton.</summary>
    [DefaultExecutionOrder(20)]
    public sealed class StopwatchButtonPresentation : MonoBehaviour
    {
        [SerializeField] private StopwatchMinigame game;
        [SerializeField] private MinigameCameraController cameras;
        [SerializeField] private CinemachineCamera view;
        [SerializeField] private SpectatorCamera spectator;
        [SerializeField] private CircusAttackPresentation attack;
        [SerializeField] private StopwatchHandLibrary library;
        private CageButton button;
        private MinigameStageState stage;
        private PlayerController owner;
        private PlayerInteractor interactor;
        private IInteractable previousOverride;
        private StopwatchHandRig hand;
        private ViewVisibility visibility;
        private Transform cap;
        private Vector3 capRest, capTopLocal;
        private Vector3 returnPosition;
        private Quaternion returnRotation;
        private bool movementWasLocked, active, leaving;
        private float blend, press, stoppedAge, returnFov;
        public bool IsActive => active;
        public bool IsCloseUp => active && blend >= .55f;
        public StopwatchHandRig Hand => hand;
        public float PressAmount => press;

        private void Awake() => stage = GetComponent<MinigameStageState>();
        private void OnEnable()
        {
            CinemachineCore.BlendCreatedEvent.AddListener(ConfigureBlend);
            if (stage != null) stage.StageStarted += StageStarted;
        }

        private void StageStarted(byte next)
        {
            // A network stage can arrive before physics/coroutines. Release the
            // owner immediately, not at the end of that frame in LateUpdate.
            if (next != StopwatchMinigame.StageMeasure) EndView();
        }

        private void ConfigureBlend(CinemachineCore.BlendEventParams args)
        {
            if (view == null || args.Blend == null) return;
            // This shot already animates its own entry and exit. A second default
            // two-second brain blend can point away from the button while E is held.
            if (ReferenceEquals(args.Blend.CamB, view) ||
                (ReferenceEquals(args.Blend.CamA, view) && cameras.CurrentMode == CameraMode.ThirdPerson &&
                 (attack == null || !attack.OwnsCamera))) args.Blend.Duration = 0;
        }

        private void LateUpdate()
        {
            var next = game != null ? game.LocalButton : null;
            if (next != button) Bind(next);
            if (button == null || owner == null || hand == null || cap == null) return;
            bool offered = button.WindowOpen && (stage == null || stage.Stage == StopwatchMinigame.StageMeasure) && owner.gameObject.activeInHierarchy &&
                (spectator == null || !spectator.IsActive) && (attack == null || !attack.OwnsCamera);
            if (!offered) { EndView(); return; }
            if (!active)
            {
                if (!button.Completed) BeginView();
                else return;
            }
            if (!active) return;
            float dt = Time.deltaTime;
            bool held = button.State == CageButton.ButtonState.Running;
            press = Mathf.MoveTowards(press, held ? 1 : 0, dt / (held ? .14f : .12f));
            if (button.Completed) stoppedAge += dt; else stoppedAge = 0;
            if (stoppedAge > .28f) leaving = true;
            blend = Mathf.MoveTowards(blend, leaving ? 0 : 1, dt / .34f);
            float eased = Mathf.SmoothStep(0, 1, blend);
            visibility?.SetCloseUp(IsCloseUp);
            Vector3 forward = button.transform.forward;
            Vector3 right = button.transform.right;
            cap.localPosition = capRest - Vector3.up * (.035f * press);
            Vector3 surface = cap.TransformPoint(capTopLocal);
            Vector3 cameraPosition = surface - forward * .72f - right * .16f + Vector3.up * .54f;
            Quaternion cameraRotation = Quaternion.LookRotation(surface + forward * .10f - cameraPosition, Vector3.up);
            view.transform.SetPositionAndRotation(Vector3.Lerp(returnPosition, cameraPosition, eased),
                Quaternion.Slerp(returnRotation, cameraRotation, eased));
            var lens = view.Lens; lens.FieldOfView = Mathf.Lerp(returnFov, 60, eased); view.Lens = lens;
            // The finger travels down to the actual moving cap, then remains still
            // for an arbitrarily long hold. No cycle can become a timing aid.
            Vector3 contact = surface + right * (.15f * (1-press)) + Vector3.up * (.13f * (1-press))
                - forward * (.06f * (1-press));
            Quaternion orientation = Quaternion.LookRotation((forward - right * .75f - Vector3.up * .12f).normalized, Vector3.up);
            hand.Place(contact, orientation);
            if (leaving && blend <= 0) EndView();
        }

        private void Bind(CageButton next)
        {
            EndView();
            visibility?.Dispose(); visibility = null;
            if (button != null) button.HasLocalPresentation = false;
            if (hand != null) Destroy(hand.gameObject);
            hand = null; cap = null; button = next;
            owner = button != null ? button.Owner : null;
            if (owner == null || library == null || cameras == null || view == null) return;
            var prefab = library.Find(owner);
            if (prefab == null) { Debug.LogError("Stopwatch: missing hand skin for " + owner.name, this); return; }
            hand = Instantiate(prefab, transform);
            hand.name = "LocalButtonHand";
            hand.gameObject.SetActive(false);
            interactor = owner.GetComponent<PlayerInteractor>();
            var surface = button.PressSurface;
            if (surface == null) return;
            cap = surface.transform; capRest = cap.localPosition;
            capTopLocal = cap.InverseTransformPoint(surface.bounds.center + Vector3.up * surface.bounds.extents.y);
            button.HasLocalPresentation = true;
            visibility = new ViewVisibility(owner, hand, cameras, view);
        }

        private void BeginView()
        {
            var brain = CinemachineCore.FindPotentialTargetBrain(view);
            var output = brain != null ? brain.OutputCamera : Camera.main;
            if (output == null) return;
            returnPosition = output.transform.position; returnRotation = output.transform.rotation;
            returnFov = output.fieldOfView;
            movementWasLocked = owner.MovementLocked; owner.MovementLocked = true;
            previousOverride = interactor != null ? interactor.ButtonOverride : null;
            if (interactor != null) interactor.ButtonOverride = button;
            active = true; leaving = false; blend = 0; press = 0; stoppedAge = 0;
            view.transform.SetPositionAndRotation(returnPosition, returnRotation);
            cameras.Apply(CameraMode.Fixed, view.transform);
            hand.gameObject.SetActive(true);
            visibility?.Activate(output);
        }

        private void EndView()
        {
            if (!active) return;
            active = false; leaving = false; blend = 0; press = 0;
            if (cap != null) cap.localPosition = capRest;
            visibility?.Deactivate();
            if (hand != null) hand.gameObject.SetActive(false);
            if (interactor != null && ReferenceEquals(interactor.ButtonOverride, button))
                interactor.ButtonOverride = previousOverride;
            if (owner != null) owner.MovementLocked = movementWasLocked;
            if (cameras != null && cameras.CurrentMode == CameraMode.Fixed && cameras.CurrentTarget == view.transform && owner != null)
                cameras.Apply(CameraMode.ThirdPerson, owner.transform);
        }

        private void OnDisable()
        {
            EndView(); visibility?.Dispose(); visibility = null;
            CinemachineCore.BlendCreatedEvent.RemoveListener(ConfigureBlend);
            if (stage != null) stage.StageStarted -= StageStarted;
            if (button != null) button.HasLocalPresentation = false;
            if (hand != null) Destroy(hand.gameObject);
            hand = null; button = null;
        }

        /// <summary>Neither a scene camera nor another player's view sees the detached arm.</summary>
        private sealed class ViewVisibility : IDisposable
        {
            private readonly Renderer[] body, arm;
            private readonly bool[] bodyBefore, armBefore;
            private readonly MinigameCameraController cameras;
            private readonly CinemachineCamera view;
            private readonly List<Camera> stack = new List<Camera>(4);
            private Camera output;
            private bool subscribed, applied, closeUp;
            public void SetCloseUp(bool value) => closeUp = value;
            public ViewVisibility(PlayerController player, StopwatchHandRig hand, MinigameCameraController cameras, CinemachineCamera view)
            {
                this.cameras = cameras; this.view = view;
                body = player.GetComponentsInChildren<Renderer>(true); arm = hand.GetComponentsInChildren<Renderer>(true);
                bodyBefore = new bool[body.Length]; armBefore = new bool[arm.Length];
                foreach (var r in arm) r.forceRenderingOff = true;
            }
            public void Activate(Camera camera)
            {
                if (subscribed) return;
                output = camera; subscribed = true;
                RenderPipelineManager.beginCameraRendering += Begin;
                RenderPipelineManager.endCameraRendering += End;
                RenderPipelineManager.endContextRendering += EndContext;
            }
            private void Begin(ScriptableRenderContext context, Camera camera) { stack.Add(camera); Apply(); }
            private void End(ScriptableRenderContext context, Camera camera)
            {
                if (stack.Count > 0 && stack[stack.Count-1] == camera) stack.RemoveAt(stack.Count-1);
                else stack.Clear();
                Apply();
            }
            private void EndContext(ScriptableRenderContext context, List<Camera> contextCameras) { stack.Clear(); Restore(); }
            private void Apply()
            {
                Restore();
                if (!closeUp || stack.Count == 0 || stack[stack.Count-1] != output || cameras == null ||
                    cameras.CurrentMode != CameraMode.Fixed || cameras.CurrentTarget != view.transform) return;
                for (int i=0;i<body.Length;i++) if(body[i]!=null) { bodyBefore[i]=body[i].forceRenderingOff; body[i].forceRenderingOff=true; }
                for (int i=0;i<arm.Length;i++) if(arm[i]!=null) { armBefore[i]=arm[i].forceRenderingOff; arm[i].forceRenderingOff=false; }
                applied = true;
            }
            private void Restore()
            {
                if (!applied) return;
                for(int i=0;i<body.Length;i++) if(body[i]!=null) body[i].forceRenderingOff=bodyBefore[i];
                for(int i=0;i<arm.Length;i++) if(arm[i]!=null) arm[i].forceRenderingOff=armBefore[i];
                applied=false;
            }
            public void Deactivate()
            {
                if (subscribed)
                {
                    RenderPipelineManager.beginCameraRendering -= Begin;
                    RenderPipelineManager.endCameraRendering -= End;
                    RenderPipelineManager.endContextRendering -= EndContext;
                    subscribed=false;
                }
                stack.Clear(); Restore();
            }
            public void Dispose() => Deactivate();
        }
    }
}
