using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Igruha.Minigames.Infection
{
    /// <summary>Scene-scoped shadow quality. Never edits the shared pipeline asset or camera rig.</summary>
    public sealed class InfectionSceneLighting : MonoBehaviour
    {
        [SerializeField] private UniversalRenderPipelineAsset pipeline;
        private RenderPipelineAsset previous;
        private UniversalRenderPipelineAsset instance;
        private UniversalAdditionalCameraData cameraData;
        private bool previousPostProcessing;

        private void Awake()
        {
            previous = QualitySettings.renderPipeline;
            instance = Instantiate(pipeline);
            QualitySettings.renderPipeline = instance;
        }

        private IEnumerator Start()
        {
            // Bootstrap binds the ordinary party camera during scene initialization.
            yield return null;
            yield return null;
            // The editor's preview SH probe is not serialized into the scene. Set the
            // scene's diffuse fill after pipeline initialization as well as in the builder.
            var fill = new SphericalHarmonicsL2();
            fill.AddAmbientLight(new Color(.27f, .32f, .39f));
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = fill;
            var camera = Camera.main;
            if (camera == null) yield break;
            cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null) yield break;
            previousPostProcessing = cameraData.renderPostProcessing;
            cameraData.renderPostProcessing = true;
        }

        private void OnDestroy()
        {
            if (cameraData != null) cameraData.renderPostProcessing = previousPostProcessing;
            if (QualitySettings.renderPipeline == instance) QualitySettings.renderPipeline = previous;
            if (instance != null) Destroy(instance);
        }
    }
}
