using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Igruha.EditorTools
{
    /// <summary>Furniture finishes and small props placed on measured existing surfaces.</summary>
    internal static class HubEntryWorkshop
    {
        private const string Town = "Assets/Synty/PolygonTown/Prefabs/";
        private const string Clutter = "_Zones/_Clutter/";

        internal static void Apply(Transform root)
        {
            var wood = HubEntrySurfaces.Wood("WorkbenchOak");
            var green = HubEntryPass.Surface("WorkbenchGreen", "526C5B");
            HubEntrySurfaces.Dress(Clutter + "Workbench", wood, green, false);
            Pegboard(root);
            Shelves(root);
            Worktop(root);
            Laundry(root);
            Runner(root);
        }

        private static void Pegboard(Transform root)
        {
            HubRoomPass.Require(Clutter + "ToolBoard").GetComponent<Renderer>().enabled = false;
            var tools = HubBarAssets.Prop("Props/SM_Prop_ToolBoard_01_Combined.prefab", root, "FurnishedPegboard", Town);
            HubBarAssets.Place(tools, new Vector3(-11.70f, 1.188f, -7), 1);
            var geo = new HubRoomGeometry();
            var oak = HubEntryPass.Surface("ShelfOak", "91643D");
            foreach (float y in new[] { 1.177f, 2.168f }) geo.Box(new Vector3(-11.685f, y, -7), new Vector3(.064f, .042f, 1.95f), oak);
            foreach (float z in new[] { -7.957f, -6.043f }) geo.Box(new Vector3(-11.685f, 1.67f, z), new Vector3(.064f, 1.025f, .042f), oak);
            geo.Build(root, "PegboardFrame", true, HubEntryPass.Folder);
        }

        private static void Shelves(Transform root)
        {
            var geo = new HubRoomGeometry();
            var wood = HubEntryPass.Surface("ShelfOak", "91643D");
            var brass = HubEntryPass.Surface("AgedBrass", "B99A60", .3f);
            var glow = HubCozyMaterials.Surface("HE_ShelfGlow", "FFE3B3", .1f, glow: 1.1f);
            geo.Box(new Vector3(-11.48f, 2.23f, -7), new Vector3(.58f, .066f, 2.30f), wood);
            geo.Box(new Vector3(-11.25f, 2.191f, -7), new Vector3(.025f, .012f, 2.14f), glow);
            foreach (float z in new[] { -7.96f, -6.04f })
            {
                geo.Box(new Vector3(-11.73f, 2.10f, z), new Vector3(.045f, .26f, .05f), brass);
                geo.Box(new Vector3(-11.49f, 2.177f, z), new Vector3(.49f, .035f, .05f), brass);
            }
            geo.Build(root, "WorkshopShelf", true, HubEntryPass.Folder);
            StorageBin(root, "SmallBox_A", new Vector3(-11.48f, 2.398f, -6.28f), new Vector3(.42f, .27f, .43f), "8A6B4A", "SPARES");
            StorageBin(root, "SmallBox_B", new Vector3(-11.48f, 2.408f, -6.89f), new Vector3(.42f, .29f, .43f), "687F68", "KITS");
            var plant = HubBarAssets.Prop("Props/SM_Prop_Planter_Plant_01.prefab", root, "WorkshopPlant");
            HubBarAssets.Place(plant, new Vector3(-11.48f, 2.263f, -7.96f), .23f);
            // Existing lower shelf is y=.416 and occupies z=-6.967..-5.665, x=-11.568..-10.435.
            StorageBin(root, "ShelfBox_A", new Vector3(-11.02f, .561f, -6.63f), new Vector3(.68f, .29f, .52f), "A17A50", "GEAR");
            StorageBin(root, "ShelfBox_B", new Vector3(-11.02f, .561f, -6.01f), new Vector3(.68f, .29f, .52f), "8E6550", "GAMES");
            var lightGo = new GameObject("ShelfTaskLight"); lightGo.transform.SetParent(root, false);
            lightGo.transform.position = new Vector3(-11.16f, 2.177f, -7);
            lightGo.transform.rotation = Quaternion.LookRotation(new Vector3(-.48f, -1, 0));
            var light = lightGo.AddComponent<Light>(); light.type = LightType.Spot;
            light.color = HubCozyMaterials.Hex("FFDEAD"); light.intensity = 1.25f; light.range = 3.8f;
            light.spotAngle = 135; light.innerSpotAngle = 100; light.shadows = LightShadows.None;
            lightGo.AddComponent<UniversalAdditionalLightData>();
        }

        private static void StorageBin(Transform root, string name, Vector3 center, Vector3 size, string color, string label)
        {
            var geo = new HubRoomGeometry();
            var body = HubEntryPass.Surface("Bin_" + color, color);
            var edge = HubEntryPass.Surface("BinEdge", "C8AB7E");
            var dark = HubEntryPass.Surface("HandleInset", "3C4437");
            geo.Box(center, size, body);
            geo.Box(center + Vector3.up * (size.y * .5f - .014f), new Vector3(size.x + .012f, .028f, size.z + .012f), edge);
            geo.Box(center + Vector3.right * (size.x * .5f + .002f), new Vector3(.006f, .065f, size.z * .39f), dark);
            geo.Build(root, name, true, HubEntryPass.Folder);
            HubBarDecor.Lettering(root, name + "_Label", label, center + new Vector3(size.x * .5f + .006f, -.063f, 0),
                size.z * .76f, .08f, HubCozyMaterials.Hex("E7D6B5"), .64f, 1);
        }

        private static void Worktop(Transform root)
        {
            const float top = .954f;
            var radio = HubBarAssets.Prop("Props/SM_Prop_Computer_Radio_01.prefab", root, "WorkshopRadio");
            HubBarAssets.Place(radio, new Vector3(-11.25f, top, -7.76f), .94f);
            var kit = HubBarAssets.Prop("Items/SM_Item_ToolBox_01.prefab", root, "WorkshopToolbox", Town);
            HubBarAssets.Place(kit, new Vector3(-11.20f, top, -6.15f), .84f);
            var geo = new HubRoomGeometry();
            var mat = HubEntryPass.Surface("CuttingMat", "3F625B");
            var paper = HubEntryPass.Surface("SketchPaper", "E0CEAA");
            var ink = HubEntryPass.Surface("SketchInk", "687865");
            geo.Box(new Vector3(-10.85f, top + .003f, -7.05f), new Vector3(.47f, .006f, .62f), mat);
            geo.Box(new Vector3(-10.84f, top + .008f, -7.07f), new Vector3(.31f, .004f, .43f), paper, Quaternion.Euler(0, -8, 0));
            geo.Box(new Vector3(-10.88f, top + .011f, -7.08f), new Vector3(.12f, .002f, .20f), ink, Quaternion.Euler(0, -8, 0));
            geo.Box(new Vector3(-10.91f, top + .017f, -6.79f), new Vector3(.24f, .012f, .012f), HubEntryPass.Surface("PencilOchre", "BC904C"), Quaternion.Euler(0, 9, 0));
            geo.Build(root, "WorkbenchDetails", false, HubEntryPass.Folder);
        }

        private static void Laundry(Transform root)
        {
            var enamel = HubEntryPass.Surface("WarmEnamel", "D7CDB7", .26f);
            enamel.SetFloat("_SpecularHighlights", 1);
            HubRoomPass.Require(Clutter + "Washer").GetComponent<Renderer>().sharedMaterial = enamel;
            HubRoomPass.Require(Clutter + "LaundryBin").GetComponent<Renderer>().sharedMaterial = HubEntryPass.Surface("LaundryBasket", "AD8655");
            HubRoomPass.Require(Clutter + "LaundryBin/SM_Prop_LaundryBin_01_Clothes").GetComponent<Renderer>().sharedMaterial = HubEntryPass.Surface("FreshLinen", "C9C8B2");
            var geo = new HubRoomGeometry();
            geo.Box(new Vector3(-11.25f, 1.247f, -4.6f), new Vector3(.99f, .034f, .97f), HubEntryPass.Surface("ShelfOak", "91643D"));
            geo.Build(root, "WasherTop", true, HubEntryPass.Folder);
            string[] colors = { "CCBEA0", "7C9783", "BE9870" };
            for (int i = 0; i < colors.Length; i++)
            {
                var towel = HubBarAssets.Prop("Props/SM_Prop_Towel_01.prefab", root, "RolledTowel_" + i);
                HubBarAssets.Place(towel, new Vector3(-11.27f, 1.264f, -4.87f + i * .27f), 1);
                HubCozyMaterials.Assign(towel, HubEntryPass.Surface("Towel_" + i, colors[i]));
            }
        }

        private static void Runner(Transform root)
        {
            var geo = new HubRoomGeometry();
            var green = HubEntryPass.Surface("RunnerSage", "778873");
            var dark = HubEntryPass.Surface("RunnerBorder", "4E6758");
            var cream = HubEntryPass.Surface("RunnerCream", "C8B99C");
            geo.Box(new Vector3(-9.79f, .010f, -6.12f), new Vector3(1.06f, .012f, 3.86f), green);
            foreach (float x in new[] { -10.23f, -9.35f }) geo.Box(new Vector3(x, .017f, -6.12f), new Vector3(.075f, .002f, 3.71f), dark);
            foreach (float z in new[] { -7.96f, -4.28f })
            {
                geo.Box(new Vector3(-9.79f, .017f, z), new Vector3(.96f, .002f, .10f), cream);
                geo.Box(new Vector3(-9.79f, .017f, z + (z < -6 ? .14f : -.14f)), new Vector3(.96f, .002f, .035f), dark);
            }
            geo.Build(root, "WorkshopRunner", false, HubEntryPass.Folder);
        }
    }
}
