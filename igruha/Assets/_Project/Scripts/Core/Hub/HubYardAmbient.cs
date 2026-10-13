using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.Core.Hub
{
    /// <summary>Local outdoor ambient, independent of the basement's warm room lighting.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class HubYardAmbient : MonoBehaviour
    {
        public const uint RenderingLayer = 1u << 7;

        [SerializeField] private Color ambientColor = new Color(.16f, .20f, .27f);

        private void OnEnable() => Apply();
        private void OnValidate() => Apply();

        public void Apply()
        {
            var ambient = new SphericalHarmonicsL2();
            ambient.AddAmbientLight(ambientColor.linear);
            var coefficients = new[] { ambient };
            var properties = new MaterialPropertyBlock();
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.renderingLayerMask = RenderingLayer;
                renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
                renderer.GetPropertyBlock(properties);
                properties.CopySHCoefficientArraysFrom(coefficients);
                renderer.SetPropertyBlock(properties);
            }
        }
    }
}
