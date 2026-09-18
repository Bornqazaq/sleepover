using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.HoleInWall;

namespace Igruha.EditorTools
{
    /// <summary>A pooled transient water effect; no permanent recovery architecture.</summary>
    internal static class HoleInWallReturnBuilder
    {
        internal static HoleInWallRecovery Build(Transform parent, int slot)
        {
            var root = new GameObject("Water return " + slot);
            root.transform.SetParent(parent, false);
            var sheet = new GameObject("Return water sheet");
            sheet.transform.SetParent(root.transform, false);
            var filter = sheet.AddComponent<MeshFilter>();
            var renderer = sheet.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                HoleInWallStudioAssets.Materials + "/HS_WaterSplash.mat");
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;

            var spray = new GameObject("Return spray");
            spray.transform.SetParent(root.transform, false);
            spray.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var particles = spray.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.useAutoRandomSeed = false;
            particles.randomSeed = (uint)(9321 + slot * 17);
            var main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.2f, .38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.7f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(.035f, .085f);
            main.startColor = new Color(.55f, .91f, .95f, .65f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = .8f;
            main.maxParticles = 24;
            var emission = particles.emission;
            emission.rateOverTime = 30;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35;
            shape.radius = .25f;
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
            var sprayRenderer = particles.GetComponent<ParticleSystemRenderer>();
            sprayRenderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                HoleInWallStudioAssets.Materials + "/HS_WaterDroplets.mat");
            sprayRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            sprayRenderer.cameraVelocityScale = 0;
            sprayRenderer.velocityScale = .025f;
            sprayRenderer.lengthScale = 1.5f;
            sprayRenderer.shadowCastingMode = ShadowCastingMode.Off;

            var jet = root.AddComponent<HoleInWallReturnJet>();
            var data = new SerializedObject(jet);
            data.FindProperty("surface").objectReferenceValue = filter;
            data.FindProperty("surfaceRenderer").objectReferenceValue = renderer;
            data.FindProperty("spray").objectReferenceValue = particles;
            data.ApplyModifiedPropertiesWithoutUndo();
            var recovery = root.AddComponent<HoleInWallRecovery>();
            data = new SerializedObject(recovery);
            data.FindProperty("jet").objectReferenceValue = jet;
            data.ApplyModifiedPropertiesWithoutUndo();
            return recovery;
        }
    }
}
