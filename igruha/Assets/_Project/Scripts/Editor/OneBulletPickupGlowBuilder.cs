using Igruha.Minigames.OneBullet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    /// <summary>Local, depth-tested pickup glow: visible along a corridor, never through walls.</summary>
    public static class OneBulletPickupGlowBuilder
    {
        private const string MaterialPath = "Assets/_Project/Art/Minigames/OneBullet/Materials/PickupGlints.mat";
        [MenuItem("Igruha/Art/Improve One Bullet pickup glow")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop play mode first");
            EditorSceneManager.OpenScene(OneBulletArenaBuilder.ScenePath);
            Configure(Object.FindFirstObjectByType<OneBulletPresentation>());
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
        }
        public static void Configure(OneBulletPresentation view)
        {
            var so = new SerializedObject(view);
            var light = (Light)so.FindProperty("pickupLight").objectReferenceValue;
            light.range = 3.2f; light.intensity = 2.8f;
            var ring = (LineRenderer)so.FindProperty("pickupRing").objectReferenceValue;
            ring.startWidth = ring.endWidth = .035f;
            for (int i = 0; i < ring.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2f / ring.positionCount;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * .42f);
            }
            var child = view.transform.Find("PickupGlints");
            var particles = child != null ? child.GetComponent<ParticleSystem>() :
                new GameObject("PickupGlints").AddComponent<ParticleSystem>();
            particles.transform.SetParent(view.transform, false);
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false; main.loop = true; main.duration = 2;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.8f, 1.4f);
            main.startSpeed = 0; main.startSize = new ParticleSystem.MinMaxCurve(.045f, .09f);
            main.startColor = Color.white; main.maxParticles = 24;
            // Local space makes relocation immediate; no glow is left in the closing territory.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = particles.emission; emission.rateOverTime = 14;
            var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(.45f, .1f, .45f);
            var velocity = particles.velocityOverLifetime; velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = velocity.z = new ParticleSystem.MinMaxCurve(0, 0);
            velocity.y = new ParticleSystem.MinMaxCurve(.45f, .75f);
            var fade = particles.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            { material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(material, MaterialPath); }
            material.SetColor("_BaseColor", new Color(3f, 1.7f, .3f, .7f));
            material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue = 3000;
            material.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat").mainTexture);
            EditorUtility.SetDirty(material);
            var renderer = particles.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            OneBulletArenaBuilder.Set(view, "pickupGlow", particles);
        }
    }
}
