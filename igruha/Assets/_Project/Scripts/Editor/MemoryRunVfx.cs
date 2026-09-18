using UnityEditor;
using UnityEngine;
using Igruha.Minigames.MemoryRun;

namespace Igruha.EditorTools
{
    internal static class MemoryRunVfx
    {
        private const int PoolSize = 4;
        internal static void Build(Transform arena, GameObject manager)
        {
            var root = new GameObject("_Effects").transform; root.SetParent(arena, false);
            var pool = new ParticleSystem[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var cloud = Burst(root, "MineBlast_" + i, "Smoke", 16, .8f, 3.6f, .7f, new Color(.48f, .40f, .30f));
                Burst(cloud.transform, "PressureFlash", "Puff", 1, .13f, 0, 2.2f, new Color(3.5f, 2.3f, .9f), 1.3f);
                var ring = Burst(cloud.transform, "PressureRing", "Ring", 1, .35f, 0, 1, new Color(1.4f, 1.0f, .5f), 6);
                var rr = ring.GetComponent<ParticleSystemRenderer>();
                rr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                var sparks = Burst(cloud.transform, "HotFragments", "Puff", 18, .45f, 7, .085f, new Color(2.8f, 1.25f, .24f), .3f);
                var sm = sparks.main; sm.gravityModifier = 1.4f;
                var sr = sparks.GetComponent<ParticleSystemRenderer>();
                sr.renderMode = ParticleSystemRenderMode.Stretch; sr.lengthScale = 2; sr.velocityScale = .06f;
                Burst(cloud.transform, "SmokeTail", "Smoke", 7, 1.1f, 1.4f, .7f, new Color(.40f, .36f, .30f));
                pool[i] = cloud;
            }
            var effects = manager.GetComponent<MemoryRunEffects>();
            if (effects == null) effects = manager.AddComponent<MemoryRunEffects>();
            var so = new SerializedObject(effects);
            so.FindProperty("game").objectReferenceValue = manager.GetComponent<MemoryRunMinigame>();
            var blasts = so.FindProperty("blasts"); blasts.arraySize = PoolSize;
            for (int i = 0; i < PoolSize; i++) blasts.GetArrayElementAtIndex(i).objectReferenceValue = pool[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ParticleSystem Burst(Transform parent, string name, string texture, short count,
            float life, float speed, float size, Color color, float growth = 2.3f)
        {
            var ps = MemoryFoundryAtmosphere.Create(parent, name, texture);
            var main = ps.main; main.loop = false; main.playOnAwake = false; main.duration = .5f;
            main.startLifetime = life; main.startSpeed = speed; main.startSize = size;
            main.startColor = color; main.maxParticles = 48;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission; emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, count) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .35f;
            var scale = ps.sizeOverLifetime; scale.enabled = true;
            scale.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, .25f, 1, growth));
            MemoryFoundryAtmosphere.Fade(ps, color, .95f);
            return ps;
        }
        internal static string Report() => "Four pooled pressure flashes, shock rings, hot particles and short smoke; no persistent marks on plates.";
    }
}
