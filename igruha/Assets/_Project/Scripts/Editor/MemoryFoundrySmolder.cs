using UnityEngine;

namespace Igruha.EditorTools
{
    internal static class MemoryFoundrySmolder
    {
        internal static void Build(Transform parent, Vector3 position, float scale, int seed)
        {
            var root = new GameObject("EnvironmentalSmolder").transform;
            root.SetParent(parent, false); root.localPosition = position; root.localScale = Vector3.one * scale;
            MemoryFoundryAssets.Place(root, "EmberDebris", Vector3.zero);
            var flame = MemoryFoundryAtmosphere.Create(root, "SmallFlames", "Flame");
            Configure(flame, seed, .9f, .55f, 18, 32);
            var main = flame.main; main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(.3f, .7f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(.7f, 1.5f); main.startSizeZ = 1;
            main.startColor = new Color(2.5f, .48f, .035f, .85f);
            flame.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.VerticalBillboard;
            var size = flame.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .55f), new Keyframe(.2f, 1), new Keyframe(1, .1f)));
            MemoryFoundryAtmosphere.Fade(flame, Color.white, .8f);
            var smoke = MemoryFoundryAtmosphere.Create(root, "SmolderSmoke", "Puff");
            smoke.transform.localPosition = Vector3.up * .7f;
            Configure(smoke, seed + 1, 6, .8f, 3, 24);
            main = smoke.main; main.startSize = new ParticleSystem.MinMaxCurve(1.0f, 1.8f);
            var growth = smoke.sizeOverLifetime; growth.enabled = true;
            growth.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .5f, 1, 2.5f));
            MemoryFoundryAtmosphere.Fade(smoke, new Color(.17f, .18f, .19f), .65f);
            var sparks = MemoryFoundryAtmosphere.Create(root, "RisingEmbers", "Puff");
            Configure(sparks, seed + 2, 2.3f, 1.4f, 2, 8);
            main = sparks.main; main.startSize = new ParticleSystem.MinMaxCurve(.025f, .07f);
            main.startColor = new Color(4, .45f, .035f, 1);
            MemoryFoundryAtmosphere.Fade(sparks, Color.white, 1);
            var lamp = new GameObject("EmberBounce").AddComponent<Light>();
            lamp.transform.SetParent(root, false); lamp.transform.localPosition = Vector3.up * .65f;
            lamp.type = LightType.Point; lamp.color = new Color(1, .27f, .035f);
            lamp.intensity = 4.5f * scale; lamp.range = 6.5f * scale; lamp.shadows = LightShadows.None;
        }

        private static void Configure(ParticleSystem system, int seed, float life, float speed, float rate, int maximum)
        {
            system.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var main = system.main; main.loop = true; main.prewarm = true; main.playOnAwake = true;
            main.duration = 6; main.startLifetime = new ParticleSystem.MinMaxCurve(life * .7f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * .6f, speed);
            main.startColor = Color.white; main.maxParticles = maximum;
            var emission = system.emission; emission.rateOverTime = rate;
            var shape = system.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 12; shape.radius = .32f;
            system.useAutoRandomSeed = false; system.randomSeed = (uint)Mathf.Max(1, seed);
        }
    }
}
