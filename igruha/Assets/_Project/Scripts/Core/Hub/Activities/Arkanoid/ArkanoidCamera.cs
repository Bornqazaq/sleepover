using System.Collections.Generic;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Unity.Cinemachine;
using UnityEngine;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>Local, reversible screen view. Shared camera rig assets are never modified.</summary>
    public sealed class ArkanoidCamera : MonoBehaviour
    {
        [SerializeField] private ArkanoidStation station;
        [SerializeField] private CinemachineCamera screenRig;
        [SerializeField] private CinemachineBrain brain;
        [SerializeField] private MinigameCameraController cameraController;
        private const float EnterSeconds = .65f, ExitSeconds = .4f;
        private readonly List<Renderer> hidden = new List<Renderer>();
        private int overrideId = -1;
        private float blend;
        public bool IsActive => overrideId >= 0;
        public bool ReadyForInput => IsActive && blend >= .99f && station.IsLocal;

        private void Update()
        {
            if (brain == null || screenRig == null || station == null ||
                cameraController != null && cameraController.CurrentMode != CameraMode.ThirdPerson)
            { ReleaseView(); return; }
            bool occupied = station.IsLocal;
            if (occupied && overrideId < 0)
            {
                screenRig.enabled = true;
                screenRig.PreviousStateIsValid = false;
                if (cameraController != null) cameraController.SetTutorialLookSuspended(true);
                HideOwnModel();
            }
            blend = Mathf.MoveTowards(blend, occupied ? 1 : 0,
                Time.unscaledDeltaTime / (occupied ? EnterSeconds : ExitSeconds));
            if (blend <= 0) { ReleaseView(); return; }
            overrideId = brain.SetCameraOverride(overrideId, 100, null, screenRig,
                Mathf.SmoothStep(0, 1, blend), Time.deltaTime);
        }

        private void OnDisable() => ReleaseView();

        private void ReleaseView()
        {
            if (overrideId >= 0)
            {
                if (brain != null) brain.ReleaseCameraOverride(overrideId);
                if (cameraController != null) cameraController.SetTutorialLookSuspended(false);
            }
            overrideId = -1;
            blend = 0;
            if (screenRig != null) screenRig.enabled = false;
            for (int i = 0; i < hidden.Count; i++)
                if (hidden[i] != null) hidden[i].forceRenderingOff = false;
            hidden.Clear();
        }

        private void HideOwnModel()
        {
            var own = SessionScoreboard.Current?.LocalPlayer?.Avatar ?? station.LocalPlayer;
            if (own == null) return;
            own.GetComponentsInChildren(true, hidden);
            for (int i = hidden.Count - 1; i >= 0; i--)
            {
                Renderer renderer = hidden[i];
                if (renderer.forceRenderingOff || renderer.GetComponentInParent<KeepVisibleInFirstPerson>() != null)
                    hidden.RemoveAt(i);
                else renderer.forceRenderingOff = true;
            }
        }
    }
}
