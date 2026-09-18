using UnityEditor;
using UnityEngine;
using Igruha.Minigames.MemoryRun;

namespace Igruha.EditorTools
{
    internal static class MemoryFoundryRuins
    {
        internal static void BuildShell(Transform parent, MemoryRunConfig config)
        {
            var shell = new GameObject("FracturedShell").transform; shell.SetParent(parent, false);
            foreach (string model in new[] { "RuinWallLeft", "RuinWallRight", "RuinCorner", "RuinRoof" })
                MemoryFoundryAssets.Place(shell, model, Vector3.zero);
            var rng = new System.Random(170926);
            foreach (int side in new[] { -1, 1 })
            {
                for (int i = 0; i < 10; i++)
                {
                    float z = -29.16f + i * 6.48f;
                    MemoryFoundryAssets.Place(shell, "ShaftServices" + ((i + (side > 0 ? 1 : 0)) % 3),
                        new Vector3(side * 17.5f, 0, z), side * 90);
                    if (z > -15)
                    {
                        float height = side < 0 ? (i % 3 == 0 ? 3.1f : 2.1f) : (i % 2 == 0 ? 1.7f : 2.7f);
                        var deck = MemoryFoundryAssets.Place(shell, "Deck", new Vector3(side * 16.05f, height, z));
                        deck.localScale = new Vector3(.75f, 1, 1.6f);
                        var railing = MemoryFoundryAssets.Place(shell, "Railing", new Vector3(side * 14.95f, height, z), 90);
                        railing.localScale = new Vector3(1.6f, 1, 1);
                        var debris = MemoryFoundryAssets.Place(shell, "Rubble" + (i % 3), new Vector3(side * 16, height + .03f, z), i * 39);
                        debris.localScale = Vector3.one * (.65f + (float)rng.NextDouble() * .4f);
                    }
                    if (i % 2 == 0)
                    {
                        var outfall = MemoryFoundryAssets.Place(shell, "BrokenOutfall",
                            new Vector3(side * 16.1f, -6 - i * 1.6f, z), side * 90);
                        outfall.localScale = Vector3.one * (.8f + (float)rng.NextDouble() * .65f);
                    }
                }
            }
            // Rubble sits against the start wall, not in the jump or waiting corridor.
            foreach (float x in new[] { -15f, -7, 7.5f, 15 })
            {
                var debris = MemoryFoundryAssets.Place(shell, "Rubble" + rng.Next(3), new Vector3(x, config.StartZoneLift, -31.4f), rng.Next(360));
                debris.localScale = Vector3.one * .8f;
                var collider = debris.gameObject.AddComponent<BoxCollider>();
                collider.center = new Vector3(0, .18f, 0); collider.size = new Vector3(2.8f, .55f, 1.3f);
                debris.gameObject.layer = LayerMask.NameToLayer("Ground");
            }
            BuildWrecks(parent);
            BuildExterior(parent);
        }

        private static void BuildWrecks(Transform parent)
        {
            var group = new GameObject("ShaftWreckage").transform; group.SetParent(parent, false);
            var random = new System.Random(170917);
            foreach (int side in new[] { -1, 1 })
            for (int i = 0; i < 14; i++)
            {
                float z = -16 + (i % 7) * 5.7f + (float)random.NextDouble() * 2.0f;
                float y = i < 7 ? -3.6f - (float)random.NextDouble() * 8 : -19 - (float)random.NextDouble() * 30;
                var wreck = MemoryFoundryAssets.Place(group, "ShaftWreck" + ((i + (side > 0 ? 1 : 0)) % 4),
                    new Vector3(side * 16.9f, y, z), side * 90 + random.Next(-9, 10));
                wreck.localScale = new Vector3(.65f + (float)random.NextDouble() * .5f,
                    .8f + (float)random.NextDouble() * .55f, .6f + (float)random.NextDouble() * .75f);
                if (i % 3 == 0)
                    MemoryFoundrySmolder.Build(wreck, new Vector3(1.25f, .25f, -1.1f), .55f + (i % 4) * .2f, 110 + i + side * 19);
            }
            // Persistent burn sites are environmental damage, never marks on game plates.
            foreach (var point in new[] { new Vector3(16.1f, 6.7f, 7.5f), new Vector3(-16.1f, 7.9f, -1), new Vector3(14.5f, 13.2f, 31.8f) })
            {
                var ledge = MemoryFoundryAssets.Place(group, "Rubble1", point);
                ledge.localScale = new Vector3(1.3f, .5f, .8f);
                MemoryFoundrySmolder.Build(group, point + Vector3.up * .18f, .85f, 490 + Mathf.RoundToInt(point.x));
            }
        }

        private static void BuildExterior(Transform parent)
        {
            var yard = new GameObject("ExteriorFoundryDistrict").transform; yard.SetParent(parent, false);
            MemoryFoundryBuilder.Box(yard, "EastYard", new Vector3(65, -2, 16), new Vector3(86, .6f, 150), "Concrete");
            MemoryFoundryBuilder.Box(yard, "NorthYard", new Vector3(0, -2, 91), new Vector3(150, .6f, 90), "Concrete");
            MemoryFoundryBuilder.Box(yard, "WestYard", new Vector3(-62, -2, 15), new Vector3(70, .6f, 145), "Concrete");
            MemoryFoundryBuilder.Box(yard, "AccessRoad", new Vector3(35, -1.66f, 20), new Vector3(12, .04f, 140), "Recess");
            for (int i = 0; i < 23; i++)
                MemoryFoundryBuilder.Box(yard, "RoadMark", new Vector3(35, -1.63f, -46 + i * 6), new Vector3(.18f, .015f, 2.2f), "Lettering");
            var positions = new[] { new Vector3(42,-1.7f,-19), new Vector3(41,-1.7f,12), new Vector3(37,-1.7f,39),
                new Vector3(21,-1.7f,57), new Vector3(-14,-1.7f,66), new Vector3(-38,-1.7f,17), new Vector3(-47,-1.7f,46),
                new Vector3(56,-1.7f,64), new Vector3(-30,-1.7f,83), new Vector3(65,-1.7f,18) };
            for (int i = 0; i < positions.Length; i++)
            {
                var workshop = MemoryFoundryAssets.Place(yard, "ExteriorWorkshop", positions[i], i % 2 == 0 ? 0 : 90);
                workshop.localScale = new Vector3(1.05f + i % 3 * .28f, 1.9f + i % 3 * .45f, 1.3f);
            }
            for (int i = 0; i < 5; i++)
            {
                var vessel = MemoryFoundryAssets.Place(yard, "PressureVessel", new Vector3(27, -1.7f, -5 + i * 8));
                vessel.localScale = new Vector3(1.5f, 1.8f + i % 2, 1.5f);
                MemoryFoundryBuilder.Box(yard, "StreetLampPost", new Vector3(29, 3.4f, -8 + i * 12), new Vector3(.16f, 10.2f, .16f), "Iron");
                MemoryFoundryBuilder.Box(yard, "StreetLamp", new Vector3(28.6f, 8.5f, -8 + i * 12), new Vector3(.85f, .18f, .5f), "Opal");
            }
            MemoryFoundryAssets.Place(yard, "ExteriorCrane", new Vector3(72, -1.7f, 27), -20);
            var distantCrane = MemoryFoundryAssets.Place(yard, "ExteriorCrane", new Vector3(-71, -1.7f, 56), 28);
            distantCrane.localScale = Vector3.one * .85f;
            foreach (var p in new[] { new Vector3(76, -1.7f, -8), new Vector3(86, -1.7f, 54), new Vector3(-57, -1.7f, 63) })
                MemoryFoundryAssets.Place(yard, "ExteriorStack", p);
        }

        internal static void ConfigureSky()
        {
            string path = MemoryFoundryAssets.Materials + "/MF_BreachSky.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_SkyTint", new Color(.35f, .39f, .43f));
            material.SetColor("_GroundColor", new Color(.007f, .009f, .012f));
            material.SetFloat("_AtmosphereThickness", 1.35f); material.SetFloat("_Exposure", .55f);
            material.SetFloat("_SunSize", .022f);
            RenderSettings.skybox = material; EditorUtility.SetDirty(material);
        }
    }
}
