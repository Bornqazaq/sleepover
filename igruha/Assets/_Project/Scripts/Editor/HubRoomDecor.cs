using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    /// <summary>Wall graphics, practical fixtures and finishes for the existing activity zones.</summary>
    internal static class HubRoomDecor
    {
        internal static void Apply(Transform parent)
        {
            Stage(parent);
            Gallery(parent);
            Sconces(parent);
            Garlands(parent);
        }

        private static void Stage(Transform parent)
        {
            var wood = HubCozyMaterials.Surface("HR_StageWood", "E5BC89", .12f, "PolygonShops_Building_Wood_01");
            var green = HubRoomPass.Surface("StageGreen", "3F594B");
            var brass = HubRoomPass.Surface("AgedBrass", "A6844E");
            var geo = new HubRoomGeometry();
            foreach (string name in new[] { "Base_1", "Base_2" })
            {
                var platform = HubRoomPass.Require("_Zones/Pedestal/" + name);
                HubCozyMaterials.Assign(platform, wood);
                Bounds b = platform.GetComponent<Renderer>().bounds;
                // Thin facing and stair nosing follow the measured existing solids.
                geo.Box(new Vector3(b.center.x, b.center.y, b.min.z - .022f),
                    new Vector3(b.size.x, b.size.y - .075f, .016f), green);
                geo.Box(new Vector3(b.center.x, b.max.y - .013f, b.min.z - .034f),
                    new Vector3(b.size.x, .024f, .018f), brass);
            }
            geo.Box(new Vector3(8.35f, 1.93f, 11.75f), new Vector3(6.1f, 2.84f, .08f), green);
            foreach (float x in new[] { 5.3f, 11.4f })
                geo.Box(new Vector3(x, 1.93f, 11.685f), new Vector3(.075f, 2.91f, .055f), wood);
            foreach (float y in new[] { .48f, 3.38f })
                geo.Box(new Vector3(8.35f, y, 11.685f), new Vector3(6.16f, .075f, .055f), wood);
            var letters = HubRoomPass.Require("_Zones/_Decor/SleepoverSign");
            HubCozyMaterials.Assign(letters, HubRoomPass.Surface("SignWarmWhite", "FFDEA4", 1.2f));
            Label(parent, "ClubSubtitle", "GOOD GAMES  /  GREAT COMPANY", new Vector3(8.3f, 1.84f, 11.66f), 0,
                4.7f, .3f, 2.1f, "DCC397");
            geo.Build(parent, "ClubStage", false);
        }

        private static void Gallery(Transform parent)
        {
            HubRoomPass.Require("_Zones/_Clutter/Art_N").GetComponent<Renderer>().enabled = false;
            HubRoomPass.Require("_Zones/_Clutter/Art_E").GetComponent<Renderer>().enabled = false;
            Poster(parent, "Arcade", new Vector3(-3.7f, 2.67f, 11.73f), 0, "ONE MORE", "ROUND", 0, "3C6058");
            Poster(parent, "Bowling", new Vector3(.8f, 2.60f, 11.73f), 0, "GOOD TIMES", "SOCIAL CLUB", 1, "AC6345");
            Poster(parent, "Racquet", new Vector3(11.73f, 2.64f, 3.65f), 90, "PLAY", "TOGETHER", 2, "AD884A");
            Poster(parent, "Vinyl", new Vector3(11.73f, 2.62f, .50f), 90, "AFTER HOURS", "SIDE A / SIDE B", 3, "46636A");
            Poster(parent, "Games", new Vector3(11.73f, 2.72f, -5.85f), 90, "GAME NIGHT", "EVERYONE IS IN", 0, "A86049");
            Poster(parent, "Welcome", new Vector3(2.1f, 2.58f, -11.73f), 180, "STAY", "A LITTLE LONGER", 3, "3C6058");
            Poster(parent, "Friends", new Vector3(4f, 2.70f, -11.73f), 180, "GOOD COMPANY", "SLEEPOVER CLUB", 2, "AD884A");
            Poster(parent, "WestGames", new Vector3(-11.73f, 2.54f, -5.3f), -90, "TAKE YOUR", "BEST SHOT", 1, "46636A");
        }

        private static void Poster(Transform parent, string name, Vector3 center, float yaw,
            string title, string footer, int motif, string color)
        {
            var holder = new GameObject("FramedPrint_" + name).transform;
            holder.SetParent(parent, false);
            var geo = new HubRoomGeometry();
            var frame = HubRoomPass.Surface("PrintFrame", "73543B");
            var paper = HubRoomPass.Surface("PrintPaper", "E9D7B6");
            var ink = HubRoomPass.Surface("PrintInk", "343F39");
            var accent = HubRoomPass.Surface("Print_" + color, color);
            var red = HubRoomPass.Surface("PrintCoral", "BF674C");
            const float width = .95f, height = 1.32f;
            geo.Box(Vector3.zero, new Vector3(width + .075f, height + .075f, .045f), frame);
            geo.Box(new Vector3(0, 0, -.027f), new Vector3(width, height, .010f), paper);
            geo.Box(new Vector3(0, 0, -.034f), new Vector3(width - .08f, height - .08f, .006f), accent);
            // Flat native shapes keep the print crisp at close range and need no imported texture atlas.
            if (motif == 0)
            {
                geo.Disc(new Vector3(0, -.01f, -.04f), Vector3.right * .34f, Vector3.up * .34f, paper);
                geo.Box(new Vector3(0, -.015f, -.047f), new Vector3(.55f, .26f, .007f), ink);
                geo.Disc(new Vector3(-.23f, -.03f, -.05f), Vector3.right * .10f, Vector3.up * .145f, ink);
                geo.Disc(new Vector3(.23f, -.03f, -.05f), Vector3.right * .10f, Vector3.up * .145f, ink);
                geo.Box(new Vector3(-.16f, -.015f, -.058f), new Vector3(.14f, .044f, .005f), paper);
                geo.Box(new Vector3(-.16f, -.015f, -.058f), new Vector3(.044f, .14f, .005f), paper);
                foreach (var p in new[] { new Vector3(.14f, .02f, -.06f), new Vector3(.23f, -.04f, -.06f) })
                    geo.Disc(p, Vector3.right * .035f, Vector3.up * .035f, red);
            }
            else if (motif == 1)
            {
                foreach (float x in new[] { -.22f, 0, .22f })
                {
                    geo.Disc(new Vector3(x, -.08f, -.043f), Vector3.right * .083f, Vector3.up * .19f, paper);
                    geo.Box(new Vector3(x, .09f, -.043f), new Vector3(.075f, .23f, .005f), paper);
                    geo.Disc(new Vector3(x, .24f, -.043f), Vector3.right * .063f, Vector3.up * .074f, paper);
                    geo.Box(new Vector3(x, .13f, -.05f), new Vector3(.078f, .045f, .005f), red);
                }
                geo.Disc(new Vector3(.12f, -.23f, -.06f), Vector3.right * .16f, Vector3.up * .16f, ink);
                for (int i = 0; i < 3; i++) geo.Disc(new Vector3(.085f + i * .039f, -.18f + (i % 2) * .04f, -.063f),
                    Vector3.right * .014f, Vector3.up * .014f, paper, 16);
            }
            else if (motif == 2)
            {
                geo.Box(new Vector3(-.03f, -.20f, -.044f), new Vector3(.075f, .3f, .007f), paper, Quaternion.Euler(0, 0, -32));
                geo.Disc(new Vector3(-.14f, .04f, -.05f), Vector3.right * .19f, Vector3.up * .225f, paper);
                geo.Disc(new Vector3(-.14f, .04f, -.055f), Vector3.right * .16f, Vector3.up * .195f, red);
                geo.Disc(new Vector3(.23f, .18f, -.05f), Vector3.right * .075f, Vector3.up * .075f, paper);
                geo.Box(new Vector3(.17f, -.26f, -.044f), new Vector3(.25f, .018f, .007f), paper);
            }
            else
            {
                geo.Disc(new Vector3(0, -.005f, -.044f), Vector3.right * .32f, Vector3.up * .32f, paper);
                geo.Disc(new Vector3(0, -.005f, -.049f), Vector3.right * .295f, Vector3.up * .295f, ink);
                geo.Disc(new Vector3(0, -.005f, -.054f), Vector3.right * .115f, Vector3.up * .115f, red);
                geo.Disc(new Vector3(0, -.005f, -.059f), Vector3.right * .025f, Vector3.up * .025f, paper);
                for (int i = 0; i < 3; i++) geo.Segment(new Vector3(-.21f + i * .045f, .17f, -.059f),
                    new Vector3(-.11f + i * .045f, .24f, -.059f), .009f, paper);
            }
            geo.Build(holder, "Print_" + name, false);
            Label(holder, "Title", title, new Vector3(0, .48f, -.045f), 0, .82f, .2f, 1.65f, "F4E1BF");
            Label(holder, "Footer", footer, new Vector3(0, -.47f, -.045f), 0, .82f, .16f, 1.1f, "F4E1BF");
            holder.position = center; holder.rotation = Quaternion.Euler(0, yaw, 0);
        }

        private static void Label(Transform parent, string name, string text, Vector3 localPosition,
            float yaw, float width, float height, float size, string color)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition; go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var label = go.AddComponent<TextMeshPro>(); label.font = HubBarAssets.LetteringFont();
            label.text = text; label.fontSize = size; label.characterSpacing = 3;
            label.alignment = TextAlignmentOptions.Center; label.textWrappingMode = TextWrappingModes.NoWrap;
            label.color = HubCozyMaterials.Hex(color); label.rectTransform.sizeDelta = new Vector2(width, height);
            label.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off; label.ForceMeshUpdate();
        }

        private static void Sconces(Transform parent)
        {
            var brass = HubRoomPass.Surface("AgedBrass", "A6844E");
            var glass = HubRoomPass.Surface("SconceGlass", "FFDCA0", .85f);
            var dark = HubRoomPass.Surface("BeamShadow", "553A29");
            var positions = new[] { new Vector3(-5.5f, 2.55f, 11.73f), new Vector3(4.55f, 2.55f, 11.73f),
                new Vector3(11.73f, 2.50f, 5.7f), new Vector3(11.73f, 2.50f, -3.1f),
                new Vector3(-11.73f, 2.55f, -7.4f), new Vector3(.25f, 2.55f, -11.73f) };
            for (int i = 0; i < positions.Length; i++)
            {
                var holder = new GameObject("WallSconce_" + i).transform; holder.SetParent(parent, false);
                var geo = new HubRoomGeometry();
                geo.Box(Vector3.zero, new Vector3(.20f, .44f, .055f), dark);
                geo.Box(new Vector3(0, 0, -.11f), new Vector3(.11f, .27f, .15f), glass);
                foreach (float y in new[] { -.16f, .16f })
                    geo.Box(new Vector3(0, y, -.10f), new Vector3(.18f, .045f, .23f), brass);
                geo.Build(holder, "Sconce_" + i);
                holder.position = positions[i]; holder.rotation = Quaternion.Euler(0, i < 2 ? 0 : i < 4 ? 90 : i == 4 ? -90 : 180, 0);
            }
        }

        private static void Garlands(Transform parent)
        {
            var wire = HubRoomPass.Surface("GarlandWire", "393326");
            var glow = HubRoomPass.Surface("GarlandGold", "FFD08B", 1.6f);
            var globe = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/_Project/Art/Hub/Cozy/HC_Globe.asset");
            // Small emissive globes, grouped into two meshes; no realtime light per bulb.
            for (int side = 0; side < 2; side++)
            {
                var geo = new HubRoomGeometry();
                Quaternion q = Quaternion.Euler(0, side * 90, 0);
                const int steps = 116;
                Vector3 last = Vector3.zero;
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    float y = 3.62f - .16f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3));
                    Vector3 p = q * new Vector3(-10.7f + 21.4f * t, y, 10.90f);
                    if (i > 0) geo.Segment(last, p, .014f, wire);
                    if (i % 4 == 2)
                    {
                        geo.Box(p + Vector3.down * .026f, new Vector3(.025f, .055f, .025f), wire);
                        var c = p + Vector3.down * .083f;
                        geo.Instance(globe, c, new Vector3(.094f, .11f, .094f), glow);
                    }
                    last = p;
                }
                geo.Build(parent, "PerimeterGarland_" + side, false);
            }
        }
    }
}
