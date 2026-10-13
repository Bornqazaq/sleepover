using System.Collections.Generic;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Unity.Cinemachine;
using UnityEngine;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>Local table view. Its own Cinemachine override leaves the shared camera rigs intact.</summary>
    public sealed class PingPongCamera : MonoBehaviour
    {
        [SerializeField] private PingPongTable table;
        [SerializeField] private CinemachineCamera tableRig;
        [SerializeField] private CinemachineBrain brain;
        [SerializeField] private MinigameCameraController cameraController;
        private const int OverridePriority = 100;
        private const float EyeDistance = 2.5f;
        private const float EyeHeight = 1.68f;
        private const float FocusHeight = 1.04f;
        private int overrideId = -1;
        private int activeSide = -1;
        private readonly List<Renderer> hiddenOwnModel = new List<Renderer>();

        public bool IsActive => overrideId >= 0 && brain != null && ReferenceEquals(brain.ActiveVirtualCamera, tableRig);
        public int ViewSide => activeSide;

        private void Update()
        {
            int side = table != null ? table.LocalSide : -1;
            // A console menu or another camera mode takes precedence over the seated activity.
            if (side < 0 || brain == null || tableRig == null ||
                (cameraController != null && cameraController.CurrentMode != CameraMode.ThirdPerson))
            {
                ReleaseView();
                return;
            }
            if (activeSide != side)
            {
                activeSide = side;
                Vector3 eye = transform.TransformPoint(new Vector3(side == 0 ? -EyeDistance : EyeDistance, EyeHeight, 0));
                Vector3 focus = transform.TransformPoint(Vector3.up * FocusHeight);
                tableRig.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye, transform.up));
                tableRig.PreviousStateIsValid = false;
                tableRig.enabled = true;
                HideOwnModel();
            }
            // Cut directly to the paddle: a room-wide blend could cross the television or another avatar.
            overrideId = brain.SetCameraOverride(overrideId, OverridePriority, null, tableRig, 1, Time.deltaTime);
        }

        private void OnDisable() => ReleaseView();

        private void ReleaseView()
        {
            if (overrideId >= 0 && brain != null) brain.ReleaseCameraOverride(overrideId);
            overrideId = -1;
            activeSide = -1;
            if (tableRig != null) tableRig.enabled = false;
            RestoreOwnModel();
        }

        private void RestoreOwnModel()
        {
            for (int i = 0; i < hiddenOwnModel.Count; i++)
                if (hiddenOwnModel[i] != null) hiddenOwnModel[i].forceRenderingOff = false;
            hiddenOwnModel.Clear();
        }

        private void HideOwnModel()
        {
            RestoreOwnModel();
            var own = SessionScoreboard.Current?.LocalPlayer?.Avatar;
            if (own == null) return;
            own.GetComponentsInChildren(true, hiddenOwnModel);
            for (int i = hiddenOwnModel.Count - 1; i >= 0; i--)
            {
                Renderer renderer = hiddenOwnModel[i];
                if (renderer.forceRenderingOff || renderer.GetComponentInParent<KeepVisibleInFirstPerson>() != null)
                    hiddenOwnModel.RemoveAt(i);
                else renderer.forceRenderingOff = true;
            }
        }
    }
}
