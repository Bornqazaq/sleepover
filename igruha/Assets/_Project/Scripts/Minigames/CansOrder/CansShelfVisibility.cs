using System;
using System.Collections.Generic;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>
    /// The shelf shot can sit inside any of the eight character models. Hide
    /// that avatar only while its shelf camera renders, leaving scene cameras,
    /// other clients, collision and the renderer's enabled state untouched.
    /// </summary>
    public sealed class CansShelfVisibility : IDisposable
    {
        private readonly MinigameCameraController cameras;
        private readonly Transform shelfRig;
        private readonly CinemachineCamera shelfCamera;
        private readonly List<Renderer> renderers = new List<Renderer>(16);
        private readonly List<bool> previousForceRenderingOff = new List<bool>(16);
        private readonly List<Camera> renderStack = new List<Camera>(4);
        private Camera outputCamera;
        private bool subscribed;
        private bool hidden;

        public CansShelfVisibility(MinigameCameraController cameras, Transform shelfRig)
        {
            this.cameras = cameras;
            this.shelfRig = shelfRig;
            shelfCamera = shelfRig != null ? shelfRig.GetComponent<CinemachineCamera>() : null;
        }

        public void Bind(Transform avatar)
        {
            Dispose();
            if (avatar == null) return;
            avatar.GetComponentsInChildren(true, renderers);
            for (int i = 0; i < renderers.Count; i++) previousForceRenderingOff.Add(false);
        }

        public void SetActive(bool active)
        {
            if (!active)
            {
                Unsubscribe();
                return;
            }

            if (cameras == null || shelfCamera == null || renderers.Count == 0) return;
            if (outputCamera == null)
            {
                var brain = CinemachineCore.FindPotentialTargetBrain(shelfCamera);
                outputCamera = brain != null ? brain.OutputCamera : null;
            }
            if (outputCamera == null || subscribed) return;

            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
            RenderPipelineManager.endContextRendering += EndContext;
            subscribed = true;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            renderStack.Add(camera);
            ApplyForCurrentCamera();
        }

        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            int last = renderStack.Count - 1;
            if (last >= 0 && renderStack[last] == camera) renderStack.RemoveAt(last);
            else renderStack.Clear();
            ApplyForCurrentCamera();
        }

        private void EndContext(ScriptableRenderContext context, List<Camera> contextCameras)
        {
            // Also recover if a render is interrupted before its end callback.
            renderStack.Clear();
            Restore();
        }

        private void ApplyForCurrentCamera()
        {
            Restore();
            int last = renderStack.Count - 1;
            if (last < 0 || renderStack[last] != outputCamera || cameras == null || shelfRig == null ||
                cameras.CurrentMode != CameraMode.Fixed || cameras.CurrentTarget != shelfRig ||
                !shelfRig.gameObject.activeInHierarchy) return;

            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                previousForceRenderingOff[i] = renderer.forceRenderingOff;
                renderer.forceRenderingOff = true;
            }
            hidden = true;
        }

        private void Restore()
        {
            if (!hidden) return;
            for (int i = 0; i < renderers.Count; i++)
                if (renderers[i] != null) renderers[i].forceRenderingOff = previousForceRenderingOff[i];
            hidden = false;
        }

        private void Unsubscribe()
        {
            if (subscribed)
            {
                RenderPipelineManager.beginCameraRendering -= BeginCamera;
                RenderPipelineManager.endCameraRendering -= EndCamera;
                RenderPipelineManager.endContextRendering -= EndContext;
                subscribed = false;
            }
            renderStack.Clear();
            Restore();
            outputCamera = null;
        }

        public void Dispose()
        {
            Unsubscribe();
            renderers.Clear();
            previousForceRenderingOff.Clear();
        }
    }
}
