using Igruha.Core.CameraSystems;
using Igruha.Core.Hub;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    public static class HubConsoleCameraPass
    {
        private const float ViewDistance = 2f;

        public static void Apply()
        {
            Transform pit = HubCompactPass.Require("_Pit").transform;
            RectTransform screen = pit.Find("TvScreen").GetComponent<RectTransform>();
            Transform focus = pit.Find("TvCameraFocus");
            if (focus == null)
            {
                focus = new GameObject("TvCameraFocus").transform;
                focus.SetParent(pit, false);
            }
            focus.SetPositionAndRotation(screen.position, screen.rotation);
            Transform cameraRoot = HubCompactPass.Require("_Camera").transform;
            var rig = cameraRoot.Find("TvCameraRig").GetComponent<CinemachineCamera>();
            rig.transform.SetPositionAndRotation(screen.position - screen.forward * ViewDistance, screen.rotation);
            rig.Target.TrackingTarget = focus;
            var framing = rig.GetComponent<ConsoleCameraFraming>();
            if (framing == null) framing = rig.gameObject.AddComponent<ConsoleCameraFraming>();
            var framingData = new SerializedObject(framing);
            framingData.FindProperty("outputCamera").objectReferenceValue = cameraRoot.Find("Main Camera").GetComponent<Camera>();
            framingData.FindProperty("screen").objectReferenceValue = screen;
            framingData.ApplyModifiedPropertiesWithoutUndo();
            var menu = new SerializedObject(HubCompactPass.Require("HubManager").GetComponent<ConsoleMenu>());
            menu.FindProperty("tvCameraFocus").objectReferenceValue = focus;
            menu.FindProperty("cameraController").objectReferenceValue = cameraRoot.GetComponent<MinigameCameraController>();
            menu.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
