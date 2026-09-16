using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.Exam;

namespace Igruha.EditorTools
{
    /// <summary>Original school hall composition; physics is explicit by purpose.</summary>
    internal static class ExamHallBuilder
    {
        internal const float BoardWidth = 10.45f;
        internal const float BoardHeight = 2.95f;
        internal const float BoardCenter = 3.90f;
        private static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); return t;
        }
        internal static void Apply(GameObject arena, ExamConfig config)
        {
            var root = arena.transform;
            var old = root.Find("OriginalHall");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var art = Group(root, "OriginalHall");
            foreach(string enamel in new[]{"EnamelA","EnamelB"})
            {
                var m=ExamHallAssets.Material(enamel);m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
                m.SetColor("_EmissionColor",m.GetColor("_BaseColor")*.22f);EditorUtility.SetDirty(m);
            }
            foreach (var t in new[] { "Floor_Far", "Floor_Near", "Floor_Left", "Floor_Right", "Podium", "GapThreshold", "ReturnZone" })
                Hide(root.Find(t));
            // Only one floor collider; return paint has no independent step.
            var returnCollider = root.Find("ReturnZone").GetComponent<Collider>();
            if (returnCollider != null) Object.DestroyImmediate(returnCollider);
            foreach (var t in new[] { "Wall_Far", "Wall_Near", "Wall_Left", "Wall_Right", "Ceiling" })
                Paint(root.Find(t), "Plaster");
            foreach (var t in new[] { "PitFloor", "PitWall_Far", "PitWall_Near", "PitWall_Left", "PitWall_Right" }) Paint(root.Find(t), "PitStone");
            Paint(root.Find("Board"), "Chalkboard");
            for (int i = 0; i < 4; i++) ExamHallAssets.Place(art, "Parquet_" + i, Vector3.zero);
            foreach (string model in new[] { "SideArchitecture_-1", "SideArchitecture_1", "EndArchitecture_Front", "EndArchitecture_Rear", "CeilingLongitudinals", "PitMasonry", "PitFloorDetail" })
                ExamHallAssets.Place(art, model, Vector3.zero);
            for (int i = 0; i < 5; i++) ExamHallAssets.Place(art, "CeilingRib_" + i, Vector3.zero);
            float far = config.HallDepth * .5f;
            float podiumZ = far - config.PodiumDepth * .5f;
            float platformsZ = root.Find("Platform_A").position.z;
            ExamHallAssets.Place(art, "Dais", new Vector3(0, 0, podiumZ));
            var lectern = ExamHallAssets.Place(art, "Lectern", new Vector3(0, config.PodiumHeight, podiumZ - 1.0f));
            Solid(lectern, new Vector3(0, .61f, 0), new Vector3(1.65f, 1.22f, .9f));
            ExamHallAssets.Place(art, "BoardFrame", new Vector3(0, BoardCenter, far - .42f));
            ExamHallAssets.Place(art, "GapBridge", new Vector3(0, 0, platformsZ));
            // Board has one live canvas; no public copy of the host's private fields.
            root.Find("BoardCanvas").GetComponent<ExamBoard>().ShowWaiting(0, 0, 0);
            foreach (var text in root.Find("BoardCanvas").GetComponentsInChildren<TMP_Text>())
            { text.fontStyle = FontStyles.Normal; text.color = new Color(.95f, .92f, .79f); }
            for (int side = -1; side <= 1; side += 2)
            {
                for (int bay = 0; bay < 4; bay++)
                    ExamHallAssets.Place(art, "ArchedWindow", new Vector3(side * 9.40f, 1.94f, -6 + bay * 4), side < 0 ? -90 : 90);
                // Desks stop before the spawn band, leaving all eight spawns clear.
                for (int row = 0; row < 4; row++)
                {
                    var desk = ExamHallAssets.Place(art, "SchoolDesk", new Vector3(side * 8.25f, 0, 4.2f - row * 2.05f), 180);
                    Solid(desk, new Vector3(0, .49f, -.24f), new Vector3(1.50f, .98f, 1.53f));
                }
                for (int row = 0; row < 2; row++)
                {
                    var shelf = ExamHallAssets.Place(art, "Bookcase", new Vector3(side * (6.9f + row * 1.9f), 0, -9.38f), 180);
                    Solid(shelf, new Vector3(0, 1.28f, 0), new Vector3(1.92f, 2.56f, .7f));
                }
                // Front side bays remain decor, away from the board's text silhouette.
                if(side<0){ var frontShelf = ExamHallAssets.Place(art, "Bookcase", new Vector3(side * 7.2f, 0, far - .75f));
                Solid(frontShelf, new Vector3(0, 1.28f, 0), new Vector3(1.92f, 2.56f, .7f));
                }
                for (int i = 0; i < 3; i++)
                {
                    var sconce = ExamHallAssets.Place(art, "Sconce", new Vector3(side * 9.05f, 3.45f, -4 + i * 4), side < 0 ? -90 : 90);
                    Point(art, "SconceLight", sconce.position + new Vector3(-side * .48f, .1f, 0), new Color(1,.76f,.43f), 1.6f, 4.8f);
                }
                Platform(root, side < 0 ? "A" : "B", config);
                Sign(art, side < 0 ? "A" : "B", new Vector3(side * 6.25f, 3.5f, platformsZ + .8f));
                for (int z = 0; z < 2; z++)
                {
                    var lampPos = new Vector3(side * 4.8f, 6.35f, -3.8f + z * 9.2f);
                    ExamHallAssets.Place(art, "Chandelier", lampPos);
                    Point(art, "OpalLight", lampPos + Vector3.down, new Color(1,.86f,.62f), 5.8f, 12f);
                }
                Point(art, "WindowFill", new Vector3(side * 7.3f, 4.1f, 2), new Color(.64f,.80f,1), 4f, 12f);
                var key = Point(art, "PlatformKey", new Vector3(side * 3.6f, 5.5f, 3.8f), new Color(1,.90f,.72f), 14f, 13f);
                key.type = LightType.Spot; key.spotAngle = 90; key.innerSpotAngle = 52;
                key.transform.rotation = Quaternion.Euler(80, 180, 0); key.shadows = LightShadows.Soft; key.shadowBias = .25f; key.shadowNormalBias = .6f;
                Point(art, "PitLamp", new Vector3(side * 3.6f, -5.4f, platformsZ), new Color(.75f,.56f,.29f), 3.2f, 5.4f);
            }
            for(int side=-1;side<=1;side+=2)
            {
                ExamHallAssets.Place(art,"FrontPilaster",new Vector3(side*5.9f,0,far-.38f));
                ExamHallAssets.Place(art,"PortraitFrame",new Vector3(side*7.5f,4.38f,far-.45f));
                var canvas=GameObject.CreatePrimitive(PrimitiveType.Quad);canvas.name="OriginalPortrait";canvas.transform.SetParent(art,false);
                canvas.transform.localPosition=new Vector3(side*7.5f,4.38f,far-.47f);canvas.transform.localScale=new Vector3(1.5f,1.96f,1);
                Object.DestroyImmediate(canvas.GetComponent<Collider>());
                string matPath=ExamHallAssets.Materials+"/EH_Portrait"+(side<0?"1":"2")+".mat";
                var portrait=AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if(portrait==null){portrait=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(portrait,matPath);}
                portrait.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Exam/Portraits/Exam_Portrait_0"+(side<0?"1":"3")+".png"));
                portrait.SetFloat("_Smoothness",.05f);canvas.GetComponent<Renderer>().sharedMaterial=portrait;EditorUtility.SetDirty(portrait);
            }
            ExamHallAssets.Place(art,"EntranceDoor",new Vector3(0,0,-far+.35f),180);
            ExamHallAssets.Place(art, "Clock", new Vector3(0, 5.95f, far - .62f));
            var inscription=Label(art, "HallInscription", "ЭКЗАМЕНАЦИОННЫЙ ЗАЛ", new Vector3(0, 3.99f, -far + .40f), 4.5f, .3f, .21f, new Color(.42f,.28f,.13f));
            inscription.localRotation=Quaternion.Euler(0,180,0);
            // The safe approach is outlined inlaid brass, no extra collider or obstruction.
            for (int s = -1; s <= 1; s += 2)
                Box(art, "AisleInlay", new Vector3(s * 6.75f, .031f, -3.05f), new Vector3(.027f,.01f,7.4f), "Brass");
            ExamHallDetails.Apply(art);
            Lighting(root);
        }
        private static void Platform(Transform root, string side, ExamConfig config)
        {
            Transform platform = root.Find("Platform_" + side);
            foreach (string doorName in new[] { "DoorLeft", "DoorRight" })
            {
                Transform door = platform.Find(doorName);
                var motion=door.Find("LeafVisualMotion");if(motion!=null)Object.DestroyImmediate(motion.gameObject);
                var previous = door.Find("EH_DoorLeaf" + side); if (previous != null) Object.DestroyImmediate(previous.gameObject);
                Hide(door.Find("Leaf"));
                float x = doorName == "DoorLeft" ? config.PlatformWidth * .25f : -config.PlatformWidth * .25f;
                ExamHallAssets.Place(door, "DoorLeaf" + side, new Vector3(x,0,0));
                var hinge = door.Find("EH_Hinge"); if (hinge != null) Object.DestroyImmediate(hinge.gameObject);
                ExamHallAssets.Place(door, "Hinge", Vector3.zero);
            }
            ExamHallDetails.Mechanism(platform,side);
            var existing = platform.Find("HatchBand"); if (existing != null) Object.DestroyImmediate(existing.gameObject);
            var bands = Group(platform, "HatchBand");
            for (int s = -1; s <= 1; s += 2)
            {
                Box(bands, "Band_End", new Vector3(0,.016f,s*(config.PlatformDepth*.5f+.035f)),new Vector3(config.PlatformWidth+.14f,.025f,.065f),side=="A"?"EnamelA":"EnamelB");
                Box(bands, "Band_Side", new Vector3(s*(config.PlatformWidth*.5f+.035f),.016f,0),new Vector3(.065f,.025f,config.PlatformDepth),side=="A"?"EnamelA":"EnamelB");
            }
            foreach (var text in platform.Find("LetterCanvas").GetComponentsInChildren<TMP_Text>())
            { text.color = new Color(.94f,.87f,.64f); text.fontStyle = FontStyles.Bold; }
        }
        private static void Sign(Transform parent, string side, Vector3 pos)
        {
            var model = ExamHallAssets.Place(parent, "Sign"+side,pos);
            Label(model,"Letter",side=="A"?"А":"Б",new Vector3(0,0,-.15f),1.25f,.80f,.67f,new Color(.97f,.91f,.72f));
            var back = Label(model,"LetterReverse",side=="A"?"А":"Б",new Vector3(0,0,.08f),1.25f,.80f,.67f,new Color(.97f,.91f,.72f));
            back.localRotation=Quaternion.Euler(0,180,0);
        }
        internal static Transform Label(Transform parent,string name,string value,Vector3 pos,float width,float height,float size,Color color)
        {
            var go=new GameObject(name,typeof(TextMeshPro));go.transform.SetParent(parent,false);go.transform.localPosition=pos;
            var t=go.GetComponent<TextMeshPro>();t.font=ExamHallAssets.Font();t.text=value;t.fontSize=size*10;t.color=color;t.alignment=TextAlignmentOptions.Center;
            t.rectTransform.sizeDelta=new Vector2(width,height);t.textWrappingMode=TextWrappingModes.NoWrap;t.fontStyle=FontStyles.Normal;
            return go.transform;
        }
        internal static Renderer Box(Transform parent,string name,Vector3 pos,Vector3 size,string material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=pos;go.transform.localScale=size;
            Object.DestroyImmediate(go.GetComponent<Collider>());var r=go.GetComponent<Renderer>();r.sharedMaterial=ExamHallAssets.Material(material);return r;
        }
        private static void Solid(Transform parent,Vector3 center,Vector3 size)
        {
            var go=Group(parent,"Collision").gameObject;go.layer=LayerMask.NameToLayer("Cover");
            var c=go.AddComponent<BoxCollider>();c.center=center;c.size=size;
        }
        private static void Hide(Transform t) { if(t==null)return; if(t.TryGetComponent<Renderer>(out var r))Object.DestroyImmediate(r); if(t.TryGetComponent<MeshFilter>(out var f))Object.DestroyImmediate(f); }
        private static void Paint(Transform t,string name) { if(t!=null && t.TryGetComponent<Renderer>(out var r))r.sharedMaterial=ExamHallAssets.Material(name); }
        private static Light Point(Transform parent,string name,Vector3 pos,Color color,float intensity,float range)
        {
            var t=Group(parent,name);t.position=pos;var l=t.gameObject.AddComponent<Light>();l.type=LightType.Point;l.color=color;l.intensity=intensity;l.range=range;l.shadows=LightShadows.None;return l;
        }
        private static void Lighting(Transform root)
        {
            var old=root.Find("CeilingLights");if(old!=null)Object.DestroyImmediate(old.gameObject);
            ExamHallLighting.Apply(root);
        }
    }
}
