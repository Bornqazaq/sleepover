using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>Dims the tent bulbs locally without modifying any shared material.</summary>
    public sealed class CircusDuelPracticalLights : MonoBehaviour
    {
        [SerializeField] private Renderer[] fixtures = System.Array.Empty<Renderer>();
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        private sealed class Bulb
        {
            public Renderer Renderer;
            public int Slot;
            public Color Color;
            public MaterialPropertyBlock Original, Working;
        }
        private readonly List<Bulb> bulbs = new List<Bulb>();
        private bool captured;

        private void Awake()
        {
            foreach (var fixture in fixtures)
            {
                if (fixture == null) continue;
                var materials = fixture.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null || !material.HasProperty(Emission) || material.GetColor(Emission).maxColorComponent <= 0) continue;
                    bulbs.Add(new Bulb { Renderer = fixture, Slot = i, Color = material.GetColor(Emission),
                        Original = new MaterialPropertyBlock(), Working = new MaterialPropertyBlock() });
                }
            }
        }

        public void Dim(float level)
        {
            foreach (var bulb in bulbs)
            {
                if (bulb.Renderer == null) continue;
                if (!captured)
                {
                    bulb.Renderer.GetPropertyBlock(bulb.Original, bulb.Slot);
                    bulb.Renderer.GetPropertyBlock(bulb.Working, bulb.Slot);
                }
                bulb.Working.SetColor(Emission, bulb.Color * level);
                bulb.Renderer.SetPropertyBlock(bulb.Working, bulb.Slot);
            }
            captured = true;
        }

        public void Restore()
        {
            if (!captured) return;
            foreach (var bulb in bulbs)
                if (bulb.Renderer != null) bulb.Renderer.SetPropertyBlock(bulb.Original, bulb.Slot);
            captured = false;
        }

        private void OnDisable() => Restore();
    }
}
