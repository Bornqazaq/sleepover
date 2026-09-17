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
                var cloud = Burst(root, "MineBlast_" + i, "Smoke", 22, 1.15f, 2.8f, 1.15f, new Color(1, .70f, .32f));
                Burst(cloud.transform, "PressureRing", "Ring", 1, .7f, 0, 1, new Color(1, .81f, .39f), 7);
                Burst(cloud.transform, "SmokeTail", "Smoke", 9, 1.6f, 1.1f, .95f, new Color(.40f, .36f, .30f));
                pool[i] = cloud;
            }
            var soot = new GameObject("SootTemplate"); soot.transform.SetParent(root, false);
            var smudge = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            smudge.name = "Smudge"; Object.DestroyImmediate(smudge.GetComponent<Collider>());
            smudge.transform.SetParent(soot.transform, false);
            smudge.transform.localPosition = new Vector3(0, .05f, 0);
            smudge.transform.localScale = new Vector3(.30f, .26f, .30f);
            string sootPath = MemoryFoundryAssets.Materials + "/MF_Soot.mat";
            var sootMaterial = AssetDatabase.LoadAssetAtPath<Material>(sootPath);
            if (sootMaterial == null)
            {
                sootMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(sootMaterial, sootPath);
            }
            Igruha.Minigames.HoleInWall.HoleInWallMaterials.ConfigureTransparent(sootMaterial, new Color(.10f, .085f, .075f, .62f), .1f);
            EditorUtility.SetDirty(sootMaterial);
            smudge.GetComponent<Renderer>().sharedMaterial = sootMaterial;
            smudge.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var wisp = MemoryFoundryAtmosphere.Create(soot.transform, "Wisp", "Puff");
            wisp.transform.localPosition = new Vector3(0, .22f, 0);
            wisp.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var wm = wisp.main; wm.loop = true; wm.playOnAwake = true;
            wm.startLifetime = 1.2f; wm.startSpeed = .55f; wm.startSize = .22f;
            wm.startColor = new Color(.15f, .13f, .11f); wm.maxParticles = 12;
            var we = wisp.emission; we.rateOverTime = 6;
            var ws = wisp.shape; ws.shapeType = ParticleSystemShapeType.Cone; ws.radius = .045f; ws.angle = 10;
            MemoryFoundryAtmosphere.Fade(wisp, new Color(.16f, .14f, .12f), .8f);
            soot.SetActive(false);
            var effects = manager.GetComponent<MemoryRunEffects>();
            if (effects == null) effects = manager.AddComponent<MemoryRunEffects>();
            var so = new SerializedObject(effects);
            so.FindProperty("game").objectReferenceValue = manager.GetComponent<MemoryRunMinigame>();
            so.FindProperty("sootTemplate").objectReferenceValue = soot;
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
        internal static string Report() => "Four original pooled smoke blasts with pressure rings; no fire, debris or marks on plates.";
    }
}
