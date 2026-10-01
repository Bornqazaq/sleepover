using UnityEditor;
using UnityEngine;
using Igruha.Minigames.CarryItem;

namespace Igruha.EditorTools
{
    /// <summary>Reproducible cart presentation, rolling contact and tap water.</summary>
    internal static class CarryWaterArt
    {
        private const string Folder = "Assets/_Project/Materials/Minigames/CarryItem/Original/";
        internal static PhysicsMaterial RollingMaterial()
        {
            const string path = Folder + "CartRolling.physicMaterial";
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (material == null)
            {
                material = new PhysicsMaterial("CartRolling");
                AssetDatabase.CreateAsset(material, path);
            }
            // Acceleration and braking already model rolling resistance in MultiCarryObject.
            // Default floor friction otherwise consumes the entire acceleration every tick.
            material.staticFriction = 0f;
            material.dynamicFriction = 0f;
            material.frictionCombine = PhysicsMaterialCombine.Minimum;
            material.bounciness = 0f;
            EditorUtility.SetDirty(material);
            return material;
        }

        internal static Material WaterMaterial()
        {
            const string path = Folder + "CW_Water.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Igruha/Carry Water"));
                AssetDatabase.CreateAsset(material, path);
            }
            ConfigureWaterMaterial(material);
            return material;
        }

        internal static void ConfigureWaterMaterial(Material material)
        {
            material.shader = Shader.Find("Igruha/Carry Water");
            material.SetColor("_Tint", new Color(0.075f, 0.29f, 0.34f, 1f));
            material.SetColor("_DepthTint", new Color(0.035f, 0.16f, 0.21f, 1f));
            material.SetFloat("_Opacity", 0.78f);
            material.SetFloat("_Density", 2.2f);
            material.SetFloat("_RippleStrength", 0.08f);
            material.SetFloat("_Flow", 0f);
            // Rear glass faces otherwise paint over the water and wash out its depth.
            material.renderQueue = 3010;
            EditorUtility.SetDirty(material);
        }

        internal static Transform[] Wheels(Transform parent)
        {
            var result = new Transform[4];
            for (int i = 0; i < result.Length; i++)
                result[i] = CarrySkyscraperAssets.Place(parent, "WaterCartWheel",
                    new Vector3(i % 2 == 0 ? -0.48f : 0.48f, 0.22f, i < 2 ? -0.4f : 0.4f));
            return result;
        }

        internal static void Presentation(GameObject root, Transform tub, Transform[] wheels)
        {
            var so = new SerializedObject(root.AddComponent<WaterCartPresentation>());
            so.FindProperty("tub").objectReferenceValue = tub;
            var array = so.FindProperty("wheels"); array.arraySize = wheels.Length;
            for (int i = 0; i < wheels.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = wheels[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void TapStream(Transform root, Vector3 nozzle)
        {
            var go = new GameObject("TapStream"); go.transform.SetParent(root, false);
            go.transform.localPosition = nozzle;
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = WaterMaterial();
            line.useWorldSpace = false; line.positionCount = 12;
            line.startWidth = 0.063f; line.endWidth = 0.037f;
            line.numCapVertices = 3; line.generateLightingData = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            for (int i = 0; i < 12; i++) line.SetPosition(i, Vector3.down * nozzle.y * i / 11f);
            var spray = new GameObject("ImpactSpray"); spray.transform.SetParent(go.transform, false);
            spray.transform.localPosition = Vector3.down * nozzle.y;
            spray.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var particles = spray.AddComponent<ParticleSystem>();
            var main = particles.main; main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.033f);
            main.startLifetime = 0.24f; main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.15f);
            main.gravityModifier = 1f; main.maxParticles = 64; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 65f; shape.radius = 0.055f;
            var emission = particles.emission; emission.rateOverTime = 75f;
            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = WaterMaterial();
            var so = new SerializedObject(go.AddComponent<WaterTapStream>());
            so.FindProperty("stream").objectReferenceValue = line;
            so.FindProperty("splash").objectReferenceValue = spray.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
