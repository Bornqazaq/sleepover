using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.MemoryRun;

namespace Igruha.EditorTools
{
    internal static class MemoryFoundryAtmosphere
    {
        internal static Material ParticleMaterial(string texture)
        {
            string path = MemoryFoundryAssets.Materials + "/MF_" + texture + "Particle.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(
                MemoryFoundryAssets.Art + "/Textures/MF_" + texture + ".png"));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0); material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material); return material;
        }

        internal static ParticleSystem Create(Transform parent, string name, string texture)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ParticleMaterial(texture);
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return system;
        }

        internal static void Fade(ParticleSystem system, Color color, float alpha)
        {
            var module = system.colorOverLifetime; module.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(color, 0), new GradientColorKey(color, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(alpha, .15f), new GradientAlphaKey(alpha * .65f, .6f), new GradientAlphaKey(0, 1) });
            module.color = gradient;
        }

        internal static void Build(Transform parent, MemoryRunConfig config)
        {
            var group = new GameObject("ShaftAtmosphere").transform; group.SetParent(parent, false);
            for (int i = 0; i < config.Steps; i++)
            {
                // Layered, bounded haze hides the shaft termination without obscuring landings.
                for (int layer = 0; layer < 3; layer++)
                {
                    var mist = Create(group, "ShaftMist_" + i + "_" + layer, "Puff");
                    mist.transform.localPosition = new Vector3(0, -14 - layer * 24, config.StepZ(i));
                    var main = mist.main; main.loop = true; main.playOnAwake = true;
                    main.prewarm = true; main.duration = 16; main.startLifetime = 16;
                    main.startSpeed = .09f; main.startSize = new ParticleSystem.MinMaxCurve(9 + layer * 3, 15 + layer * 4);
                    main.startColor = Color.white; main.maxParticles = 20;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    var emission = mist.emission; emission.rateOverTime = .9f;
                    var shape = mist.shape; shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(config.HallWidth - 6, 2, config.StepPitch);
                    var color = Color.Lerp(new Color(.34f, .40f, .43f), new Color(.065f, .09f, .10f), layer * .5f);
                    Fade(mist, color, layer == 0 ? .55f : .8f);
                    mist.useAutoRandomSeed = false; mist.randomSeed = (uint)(7341 + i * 19 + layer * 131);
                }
                foreach (int side in new[] { -1, 1 })
                {
                    var steam = Create(group, "PipeSteam_" + i + "_" + side, "Puff");
                    steam.transform.localPosition = new Vector3(side * (config.HallWidth / 2 - 3.0f), -8 - (i % 3) * 3, config.StepZ(i));
                    var sm = steam.main; sm.loop = true; sm.playOnAwake = true; sm.prewarm = true;
                    sm.startLifetime = 6; sm.startSize = new ParticleSystem.MinMaxCurve(1.4f, 3.5f);
                    sm.startSpeed = .45f; sm.startColor = Color.white; sm.maxParticles = 24;
                    var se = steam.emission; se.rateOverTime = 2.5f;
                    var ss = steam.shape; ss.shapeType = ParticleSystemShapeType.Cone; ss.angle = 18; ss.radius = .2f;
                    steam.transform.localRotation = Quaternion.Euler(-65, side * -60, 0);
                    Fade(steam, new Color(.49f, .55f, .57f), .38f);
                    steam.useAutoRandomSeed = false; steam.randomSeed = (uint)(4963 + i * 23 + side);
                }
            }
            foreach (var point in new[] { new Vector3(76, 30, -8), new Vector3(86, 30, 54), new Vector3(-57, 30, 63) })
            {
                var smoke = Create(group, "ExteriorStackSmoke", "Puff");
                smoke.transform.localPosition = point; smoke.transform.localRotation = Quaternion.Euler(-70, 30, 0);
                var main = smoke.main; main.loop = true; main.prewarm = true; main.startLifetime = 18;
                main.startSpeed = 1.2f; main.startSize = new ParticleSystem.MinMaxCurve(3, 6); main.maxParticles = 32;
                var emission = smoke.emission; emission.rateOverTime = 1.4f;
                var shape = smoke.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.radius = .8f; shape.angle = 12;
                Fade(smoke, new Color(.31f, .33f, .35f), .4f);
                smoke.useAutoRandomSeed = false; smoke.randomSeed = (uint)(7500 + point.x);
            }
        }
    }
}
