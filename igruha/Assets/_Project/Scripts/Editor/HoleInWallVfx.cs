using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.HoleInWall;

namespace Igruha.EditorTools
{
    /// <summary>Original procedural confetti, splash droplets and tension sparks.
    /// Uses the existing replicated gameplay events through HoleInWallEffects.</summary>
    internal static class HoleInWallVfx
    {
        private const int SlotsPerTrack = 2;
        internal static void Build(Transform arena, HoleInWallConfig config, HoleInWallTrack[] tracks)
        {
            Transform previous = arena.Find("_Effects");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            var root = new GameObject("_Effects");
            root.transform.SetParent(arena, false);
            EnsureParticleMaterial();
            int n = tracks.Length;
            var splashes = new HoleInWallSplash[n * SlotsPerTrack];
            var impacts = new ParticleSystem[splashes.Length];
            var flashes = new ParticleSystem[splashes.Length];
            var tethers = new ParticleSystem[n];
            var mirrors = new ParticleSystem[n];
            var morphs = new ParticleSystem[n];
            for (int i = 0; i < n; i++)
            {
                for (int slot = 0; slot < SlotsPerTrack; slot++)
                {
                    int key = i * SlotsPerTrack + slot;
                    splashes[key] = MakeSplash(root.transform, key);
                    impacts[key] = Make(root.transform, "Impact_" + key, new Color(1,.78f,.32f), 20, 3, .065f, .35f, false);
                    flashes[key] = Make(root.transform, "Flash_" + key, new Color(.15f,1,.85f), 32, 2.4f, .09f, .8f, false);
                }
                tethers[i] = Make(root.transform, "Tether_" + i, HoleInWallPalette.LaneAccent(i), 24, 1.7f, .06f, .3f, true);
                mirrors[i] = Make(root.transform, "Mirror_" + i, new Color(1,.25f,.55f), 55, 3, .09f, .7f, false);
                morphs[i] = Make(root.transform, "Morph_" + i, new Color(.2f,.75f,1), 55, 3, .1f, .7f, false);
            }
            Wire(root, config, tracks, splashes, impacts, flashes, tethers, mirrors, morphs);
        }

        private static ParticleSystem Make(Transform parent, string name, Color color, int count,
            float speed, float size, float life, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = (uint)(1977 + parent.childCount * 17);
            var main = ps.main;
            main.duration = life;
            main.loop = loop;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * .6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * .5f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * .5f, size);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count * 3;
            main.gravityModifier = .12f;
            var emission = ps.emission;
            emission.rateOverTime = loop ? count : 0;
            if (!loop) emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)count) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 55;
            shape.radius = .2f;
            var scale = ps.sizeOverLifetime;
            scale.enabled = true;
            scale.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-3, 3);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = HoleInWallStudioAssets.Mesh("Panel");
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ParticleMaterialPath);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        private const string ParticleMaterialPath = HoleInWallStudioAssets.Materials + "/HS_Particles.mat";

        private static HoleInWallSplash MakeSplash(Transform parent, int index)
        {
            var go = new GameObject("Splash_" + index);
            go.transform.SetParent(parent, false);
            var surface = new GameObject("Crown and ripples");
            surface.transform.SetParent(go.transform, false);
            var filter = surface.AddComponent<MeshFilter>();
            var sheet = surface.AddComponent<MeshRenderer>();
            sheet.sharedMaterial = WaterMaterial(false);
            sheet.shadowCastingMode = ShadowCastingMode.Off;
            sheet.receiveShadows = false;
            sheet.enabled = false;

            var spray = new GameObject("Water droplets");
            spray.transform.SetParent(go.transform, false);
            spray.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var ps = spray.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = (uint)(1977 + index * 17);
            var main = ps.main;
            main.duration = 1.1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.50f, .95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.3f, 6.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(.045f, .105f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(.3f, .78f, .84f, .8f), new Color(.8f, .97f, 1, .95f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1.2f;
            main.maxParticles = 48;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)32), new ParticleSystem.Burst(.045f, (short)12) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 48;
            shape.radius = .38f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0,.6f), new Keyframe(.15f,1), new Keyframe(1,.35f)));
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white,0), new GradientColorKey(Color.white,1) },
                new[] { new GradientAlphaKey(1,0), new GradientAlphaKey(.9f,.6f), new GradientAlphaKey(0,1) });
            fade.color = gradient;
            var renderer = spray.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.sharedMaterial = WaterMaterial(true);
            renderer.cameraVelocityScale = 0;
            renderer.velocityScale = .025f;
            renderer.lengthScale = 1.7f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var splash = go.AddComponent<HoleInWallSplash>();
            var so = new SerializedObject(splash);
            so.FindProperty("droplets").objectReferenceValue = ps;
            so.FindProperty("surface").objectReferenceValue = filter;
            so.FindProperty("surfaceRenderer").objectReferenceValue = sheet;
            so.ApplyModifiedPropertiesWithoutUndo();
            return splash;
        }

        private static Material WaterMaterial(bool spray)
        {
            string path = HoleInWallStudioAssets.Materials + (spray ? "/HS_WaterDroplets.mat" : "/HS_WaterSplash.mat");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Igruha/HoleInWall/Splash"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetFloat("_Droplet", spray ? 1 : 0);
            material.SetFloat("_WaterLevel", -1000);
            EditorUtility.SetDirty(material);
            return material;
        }
        private static void EnsureParticleMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(ParticleMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(material, ParticleMaterialPath);
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_ColorMode", 0);
            EditorUtility.SetDirty(material);
        }

        private static void Wire(GameObject root, HoleInWallConfig config, HoleInWallTrack[] tracks,
            HoleInWallSplash[] splashes, ParticleSystem[] impacts, ParticleSystem[] flashes,
            ParticleSystem[] tethers, ParticleSystem[] mirrors, ParticleSystem[] morphs)
        {
            var effects = root.AddComponent<HoleInWallEffects>();
            var game = Object.FindFirstObjectByType<HoleInWallMinigame>(FindObjectsInactive.Include);
            if (game == null)
            {
                Debug.LogWarning("В сцене нет HoleInWallMinigame — эффекты не на что вешать");
            }

            var so = new SerializedObject(effects);
            so.FindProperty("game").objectReferenceValue = game;
            so.FindProperty("config").objectReferenceValue = config;

            FillArray(so.FindProperty("tracks"), tracks);
            FillArray(so.FindProperty("splashes"), splashes);
            FillArray(so.FindProperty("impacts"), impacts);
            FillArray(so.FindProperty("flashes"), flashes);
            FillArray(so.FindProperty("tethers"), tethers);
            FillArray(so.FindProperty("mirrors"), mirrors);
            FillArray(so.FindProperty("morphs"), morphs);

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void FillArray(SerializedProperty property, Object[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }
    }
}
