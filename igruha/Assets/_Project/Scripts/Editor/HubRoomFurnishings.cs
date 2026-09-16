using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Textile zones and shelf-top details. Never adds obstacles to the room's circulation.</summary>
    internal static class HubRoomFurnishings
    {
        internal static void Apply(Transform parent)
        {
            Rug(parent, "ArcadeRug", new Vector3(-7.9f, .014f, 9.0f), new Vector2(6.7f, 4.2f));
            Rug(parent, "GamesRunner", new Vector3(10.05f, .014f, -2.6f), new Vector2(2.95f, 10.2f));
            Shelves(parent);
        }

        private static void Rug(Transform parent, string name, Vector3 center, Vector2 size)
        {
            var geo = new HubRoomGeometry();
            var green = HubRoomPass.Surface("WovenGreen", "677D70");
            var border = HubRoomPass.Surface("RugBorder", "3F584C");
            var cream = HubRoomPass.Surface("RugStitch", "C7BB96");
            geo.Box(center, new Vector3(size.x, .008f, size.y), green);
            foreach (float sign in new[] { -1f, 1f })
            {
                geo.Box(center + new Vector3(sign * (size.x * .5f - .12f), .005f, 0), new Vector3(.13f, .002f, size.y - .1f), border);
                geo.Box(center + new Vector3(0, .005f, sign * (size.y * .5f - .12f)), new Vector3(size.x - .1f, .002f, .13f), border);
                geo.Box(center + new Vector3(sign * (size.x * .5f - .245f), .006f, 0), new Vector3(.016f, .002f, size.y - .48f), cream);
                geo.Box(center + new Vector3(0, .006f, sign * (size.y * .5f - .245f)), new Vector3(size.x - .48f, .002f, .016f), cream);
                int stitches = Mathf.FloorToInt(size.x / .16f);
                for (int i = 0; i < stitches; i++)
                    geo.Box(center + new Vector3(-size.x * .5f + .08f + i * .16f, -.001f, sign * (size.y * .5f + .033f)),
                        new Vector3(.045f, .002f, .064f), cream);
            }
            geo.Build(parent, name, false);
        }

        private static void Shelves(Transform parent)
        {
            var shelf = HubRoomPass.Require("_Zones/BoardGamesShelf/Shelf");
            HubCozyMaterials.Assign(shelf, HubRoomPass.Surface("ShelfSage", "536B5A"));
            var colors = new[] { "BE8558", "657D87", "C9AA6C", "A86753", "718466", "9C8C72" };
            for (int i = 0; i < colors.Length; i++)
                HubCozyMaterials.Assign(HubRoomPass.Require("_Zones/BoardGamesShelf/GameBox_" + i),
                    HubRoomPass.Surface("BoardGame_" + i, colors[i]));
            HubBarAssets.Begin();
            float top = shelf.GetComponent<Renderer>().bounds.max.y;
            Plant(parent, "ShelfPlant_A", new Vector3(10.60f, top, -7.35f), .33f);
            Plant(parent, "ShelfPlant_B", new Vector3(10.60f, top, -5.72f), .27f);
            var geo = new HubRoomGeometry();
            var wood = HubRoomPass.Surface("OakBeam", "855738");
            var brass = HubRoomPass.Surface("AgedBrass", "A6844E");
            // The east-wall ledge is above the robot footprint; the west one is above the laundry corner.
            foreach (var p in new[] { new Vector3(11.56f, 2.54f, 7.75f), new Vector3(-11.56f, 2.25f, -3.55f) })
            {
                geo.Box(p, new Vector3(.39f, .065f, .82f), wood);
                foreach (float dz in new[] { -.28f, .28f })
                    geo.Box(p + new Vector3(0, -.07f, dz), new Vector3(.27f, .10f, .025f), brass);
                Plant(parent, p.x > 0 ? "EastLedgePlant" : "WestLedgePlant", p + Vector3.up * .033f, .31f);
            }
            geo.Build(parent, "PlantLedges");
        }

        private static void Plant(Transform parent, string name, Vector3 bottom, float scale)
        {
            // Use the existing hub's plant model, baked through the shared static-prop copier.
            var prop = HubBarAssets.Prop("Props/SM_Prop_Planter_Plant_01.prefab", parent, name);
            HubBarAssets.Place(prop, bottom, scale, 90);
        }
    }
}
