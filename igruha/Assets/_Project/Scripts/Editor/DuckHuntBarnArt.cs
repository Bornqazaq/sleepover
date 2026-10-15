using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Core.Traps;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    // Original Copperwood kit. All dimensions are metres. Only this scene's art is touched.
    internal static partial class DuckHuntBarnArt
    {
        const string Root = "Assets/_Project/Art/DuckHuntBarn";
        static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        static readonly string[] Models = { "FieldCabinet", "Deck", "Cladding", "Timber", "Lantern", "Console", "Lever", "TrapDoor", "Window", "Sign", "Pine", "Rocks", "Hoist" };

        public static void Preflight()
        {
            CarnivalPreflight(); FairgroundPreflight();
            foreach (string key in Models)
            {
                EnsureReadableModel(Root + "/Models/DHB_" + key + ".fbx");
                if (AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/DHB_" + key + ".fbx") == null)
                    throw new InvalidOperationException("Export tools/blender/duck_hunt_barn.py first: " + key);
            }
        }

        static void EnsureReadableModel(string path)
        {
            var importer=AssetImporter.GetAtPath(path) as ModelImporter;
            if(importer!=null && !importer.isReadable){importer.isReadable=true;importer.SaveAndReimport();}
        }

        public static void Apply(GameObject arena)
        {
            Preflight(); SetupMaterials();
            // Called after the authoritative layout rebuild, not over arbitrary user objects.
            Transform art = Group(arena.transform, "CopperwoodArt");
            Transform surroundings = art;
            var shells = new Dictionary<Transform,Transform>();
            var original = arena.GetComponentsInChildren<MeshRenderer>().ToArray();
            foreach (var renderer in original)
            {
                // A world-sized combined mesh selects the wrong local lights in URP Forward.
                // Keep the five storeys in separate batches so each receives its own lamps.
                Transform owner=renderer.transform;
                while(owner!=null&&!owner.name.StartsWith("Floor_"))owner=owner.parent;
                art=surroundings;
                if(owner!=null)
                {
                    if(!shells.TryGetValue(owner,out art)){art=Group(owner,"PavilionShell");shells.Add(owner,art);}
                }
                if (renderer.transform.GetComponentInParent<TrapLever>() != null || renderer.name.StartsWith("Cover_")) continue;
                string n = renderer.name;
                if (n == "Ceiling") { renderer.sharedMaterial = Mat("OakDark"); DressCeiling(renderer,art); continue; }
                if (n == "Panel" && renderer.GetComponentInParent<DoorTrap>() != null)
                { DressDoor(renderer); continue; }
                if ((n == "Section" || n.StartsWith("Section_")) && renderer.GetComponentInParent<CollapsingFloorTrap>() != null)
                { DressCollapse(renderer); continue; }
                if (n.StartsWith("CollapseWarning_")) { renderer.sharedMaterial = Mat("GlowRed"); continue; }
                if (n.Contains("_VisualStep_")) { renderer.sharedMaterial = Mat("OakLight"); continue; }
                if (n.Contains("_Curb_")) { renderer.sharedMaterial = Mat("Brass"); continue; }
                if (n.StartsWith("CandyPendulum_")) { renderer.sharedMaterial = Mat("Rose"); continue; }
                if (n == "CandyPendulumStem") { renderer.sharedMaterial = Mat("Cream"); continue; }
                if (n == "Foot" || n == "ChestBand" || n == "ChestBandSmall" || n == "OnePersonFootprint") continue;
                if (n.Contains("Wall") || n.StartsWith("ProtectedFront") || n == "ExitPartition" || n == "ExitHeader" || n == "SpawnShield" || n == "ArrivalGuard")
                { CarnivalWall(renderer, art); continue; }
                if (n.StartsWith("ReturnLane_") || n == "TakeoffStripe")
                { renderer.sharedMaterial = Mat("Brass"); continue; }
                if (n.Contains("PadSupport") || n.StartsWith("ParkourSupport_") || n == "SplitDeckSupport" || n.StartsWith("Rail_"))
                { renderer.sharedMaterial = Mat("Iron"); continue; }
                if (n == "FinishPad") { renderer.sharedMaterial = Mat("TealLight"); continue; }
                if (n == "Deck") continue; // Moving lift art stays on the moving platform.
                if (renderer.GetComponent<BoxCollider>() != null)
                    DressDeck(renderer, art,
                        n.StartsWith("ParkourPad_") || n.StartsWith("FastParkour") || n == "ParkourSplitDeck" || n == "RaisedFinishDeck");
            }
            foreach(var shell in shells)Combine(shell.Value,"PavilionShell_"+shell.Key.name);
            art=surroundings;
            foreach (Transform floor in arena.transform.Find("Tower"))
            {
                if (!floor.name.StartsWith("Floor_")) continue;
                int index = int.Parse(floor.name.Substring(6)) - 1;
                CarnivalFloor(floor, art, index);
            }
            CarnivalLift(arena.transform.Find("ElevatorShaft"));
            // Аттракционы живут вне CopperwoodArt: Combine склеивает всё, до чего
            // дотянется, в статические меши и удаляет исходники — вращаться
            // после этого нечему. Внутри корня каждый аттракцион склеивается
            // отдельно, своим вызовом Combine.
            Transform rides = Group(arena.transform, "FairgroundRides");
            CarnivalRoof(art); FairgroundSurroundings(art, rides); Landscape(art); Lighting(art); CarnivalGrade(art);
            CombineSpatial(art, "Architecture", 18f);
            Physics.SyncTransforms();
            Debug.Log("Duck Hunt carnival installed: original Blender fairground kit, protected start, five themed floors, 4/3/2/1 functional furnishings, sheltered mechanical controls.");
        }

        static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
        }

        static GameObject Model(string key, Transform parent, Vector3 p, Vector3 scale, Quaternion rotation)
        {
            var wrapper = Group(parent, key);
            wrapper.position = p; wrapper.rotation = rotation;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(FairgroundModels.Contains(key)
                ? FairgroundRoot + "/Models/DHF_" + key + ".fbx" : CarnivalModels.Contains(key)
                ? CarnivalRoot + "/Models/DHC_" + key + ".fbx" : Root + "/Models/DHB_" + key + ".fbx");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, wrapper);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Transform f = go.transform.Find("ForwardMarker"), u = go.transform.Find("UpMarker");
            Vector3 forward = go.transform.InverseTransformPoint(f.position), up = go.transform.InverseTransformPoint(u.position);
            go.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up));
            // Markers are one metre from the origin, independent of FBX import unit settings.
            float unit = Vector3.Distance(go.transform.position, f.position);
            go.transform.localScale *= 1f / unit;
            Object.DestroyImmediate(f.gameObject); Object.DestroyImmediate(u.gameObject);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                r.sharedMaterials = r.sharedMaterials.Select(m => Mat(m.name.Replace("DHB_", ""))).ToArray();
            wrapper.localScale = Vector3.Scale(wrapper.localScale, scale);
            return wrapper.gameObject;
        }

        static GameObject Model(string key, Transform parent, Vector3 p, Vector3 scale)
            => Model(key, parent, p, scale, Quaternion.identity);

        static Material Mat(string key) => Materials[key];

        static void SetupMaterials()
        {
            Materials.Clear(); EnsureFolder(Root + "/Materials"); EnsureFolder(Root + "/Meshes");
            var colors = new Dictionary<string, Color>
            {
                {"Oak", new Color(.79f,.63f,.43f)}, {"OakLight",new Color(.85f,.70f,.50f)}, {"OakDark",new Color(.43f,.30f,.18f)},
                {"Teal",new Color(.20f,.37f,.33f)}, {"TealLight",new Color(.26f,.44f,.37f)}, {"Cream",new Color(.88f,.79f,.58f)},
                {"Iron",new Color(.105f,.14f,.14f)}, {"Brass",new Color(.78f,.54f,.24f)}, {"Red",new Color(.43f,.075f,.06f)},
                {"Rose",new Color(.84f,.34f,.43f)}, {"Blue",new Color(.075f,.26f,.27f)}, {"Purple",new Color(.26f,.13f,.31f)}, {"Gold",new Color(.66f,.38f,.10f)},
                {"Glow",new Color(1,.79f,.40f)}, {"GlowRed",new Color(1,.20f,.16f)}, {"GlowBlue",new Color(.16f,.58f,1f)},
                {"GlowGreen",new Color(.25f,1f,.44f)}, {"GlowPink",new Color(1f,.25f,.68f)},
                {"Leaf",new Color(.19f,.32f,.235f)}, {"LeafLight",new Color(.32f,.43f,.25f)},
                {"Stone",new Color(.39f,.42f,.36f)}, {"Ink",new Color(.035f,.075f,.072f)}, {"Earth",new Color(.32f,.34f,.20f)},
                {"Sand",new Color(.60f,.52f,.38f)}, {"Gravel",new Color(.47f,.45f,.40f)},
                {"CityIvory",new Color(.78f,.78f,.71f)}, {"CityBrick",new Color(.42f,.24f,.19f)},
                {"CitySlate",new Color(.17f,.24f,.31f)}, {"CityGlass",new Color(.075f,.16f,.23f)},
                {"CityConcrete",new Color(.42f,.46f,.49f)}, {"CityWindowWarm",new Color(1,.63f,.28f)},
                {"RoadAsphalt",new Color(.22f,.25f,.27f)},
                {"CityWindowCool",new Color(.40f,.66f,.88f)}
            };
            Texture wood = AssetDatabase.LoadAssetAtPath<Texture>("Assets/_Project/Art/Hub/Lounge/Textures/HL_OakGrain.png");
            foreach (var entry in colors)
            {
                string path = Root + "/Materials/DHB_" + entry.Key + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m,path); }
                m.color = entry.Value; m.SetFloat("_Smoothness", entry.Key == "Brass" ? .48f : .23f);
                m.SetFloat("_Metallic", entry.Key == "Iron" || entry.Key == "Brass" ? .5f : 0);
                m.SetTexture("_BaseMap",null);
                if (entry.Key.StartsWith("Oak") || entry.Key=="Red" || entry.Key=="Blue" || entry.Key=="Teal" || entry.Key=="Cream")
                { m.SetTexture("_BaseMap", wood); m.SetTextureScale("_BaseMap", new Vector2(.65f, .65f)); }
                if (entry.Key.StartsWith("Glow"))
                {
                    m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", entry.Value * 5.8f);
                    // Без этого флага материал остаётся EmissiveIsBlack, Unity
                    // вычищает ключевое слово при сохранении, и лампы гаснут:
                    // ровно так свечение и пропало в прошлый раз.
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                if(entry.Key.StartsWith("City"))
                {
                    m.EnableKeyword("_EMISSION");
                    float glow=entry.Key.StartsWith("CityWindow")?2.2f:entry.Key=="CityGlass"?.10f:.24f;
                    m.SetColor("_EmissionColor",entry.Value*glow);
                    m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    m.SetFloat("_Smoothness",entry.Key=="CityGlass"?.65f:.25f);
                }
                EditorUtility.SetDirty(m); Materials.Add(entry.Key,m);
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); EnsureFolder(path.Substring(0,slash));
            AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));
        }

        static void DressDeck(MeshRenderer old, Transform parent, bool gilded = false)
        {
            Transform t = old.transform;
            // The old blockout mesh remains the authoritative collider, but it
            // must not render under another surface: that was the source of the
            // black speckles and camera-dependent Z-fighting on every landing.
            old.enabled = false;
            Vector3 size = t.lossyScale;
            const float boardThickness = .055f;
            float baseHeight = Mathf.Max(.025f,size.y-boardThickness);
            var basePlate=Solid(parent,"Deck structure",
                t.TransformPoint(new Vector3(0,-.5f+baseHeight/(2*size.y),0)),
                new Vector3(size.x,baseHeight,size.z),"OakDark");
            basePlate.transform.rotation=t.rotation;

            // Continuous boards have deliberate gaps and no decorative nail
            // planes. Their top is exactly the collider top, never above it.
            int boards = Mathf.Max(1,Mathf.CeilToInt(size.z/.72f));
            float pitch=size.z/boards;
            for (int iz=0;iz<boards;iz++)
            {
                float localZ=-.5f+(iz+.5f)/boards;
                var board=Solid(parent,"Floorboard",
                    t.TransformPoint(new Vector3(0,.5f-boardThickness/(2*size.y),localZ)),
                    new Vector3(Mathf.Max(.02f,size.x-.012f),boardThickness,Mathf.Max(.02f,pitch-.012f)),
                    iz%3==0?"OakLight":"Oak");
                board.transform.rotation=t.rotation;
            }
            if (gilded)
            {
                var rim = Beam(parent,t.TransformPoint(new Vector3(-.5f,.28f,-.5f)),t.TransformPoint(new Vector3(.5f,.28f,-.5f)),.085f,"Brass");
                rim.name = "LandingEdge";
            }
        }

        static void DressCeiling(MeshRenderer old, Transform art)
        {
            Bounds b=old.bounds;
            // The ceiling itself renders once. Beams sit visibly below it;
            // there is no second near-coplanar deck skin.
            old.sharedMaterial=Mat("Oak");
            int nx=Mathf.Max(1,Mathf.CeilToInt(b.size.x/3));
            for(int x=0;x<nx;x++)
                Beam(art,new Vector3(b.min.x+(x+.5f)*b.size.x/nx,b.min.y-.16f,b.min.z),new Vector3(b.min.x+(x+.5f)*b.size.x/nx,b.min.y-.16f,b.max.z),.19f,"OakDark");
        }

        static void DressWall(MeshRenderer old, Transform art)
        {
            old.sharedMaterial = Mat("OakDark");
            Bounds b = old.bounds; bool alongX = b.size.x > b.size.z;
            float width = alongX ? b.size.x : b.size.z;
            Vector3 origin = alongX ? new Vector3(b.center.x,b.min.y,b.min.z-.012f) : new Vector3(b.min.x-.012f,b.min.y,b.center.z);
            Quaternion rot = alongX ? Quaternion.identity : Quaternion.Euler(0,90,0);
            int count = Mathf.Max(1,Mathf.CeilToInt(width/2.9f));
            for (int i=0;i<count;i++)
            {
                Vector3 p=origin+rot*new Vector3(-width/2+(i+.5f)*width/count,0,0);
                Model("Cladding",art,p,new Vector3(width/count,b.size.y,1),rot);
            }
            // Trim stays at the solid wall: it adds no freestanding cover along the shooting lane.
            for (int i=0;i<=count;i++)
            {
                Vector3 p=origin+rot*new Vector3(-width/2+i*width/count,0,.055f);
                Model("Timber",art,p,new Vector3(.75f,b.size.y, .75f),rot);
            }
            Beam(art,origin+rot*new Vector3(-width/2,.15f,-.025f),origin+rot*new Vector3(width/2,.15f,-.025f),.14f,"OakLight");
            Beam(art,origin+rot*new Vector3(-width/2,b.size.y-.1f,0),origin+rot*new Vector3(width/2,b.size.y-.1f,0),.16f,"OakLight");
        }

        static void DressFloor(Transform floor, Transform art, int index)
        {
            float y=index*5.76f;
            // Finish the external ends too: no bare blockout slab in the establishing view.
            for(int side=-1;side<=1;side+=2)
            {
                float outerX=side<0?-.31f:34.87f, height=index==4?6.5f:5.76f;
                Quaternion facing=Quaternion.Euler(0,side<0?90:-90,0);
                for(int bay=0;bay<4;bay++)
                    Model("Cladding",art,new Vector3(outerX,y,1.26f+bay*2.52f),new Vector3(2.52f,height,1),facing);
                for(int bay=0;bay<=2;bay++)
                    Model("Timber",art,new Vector3(outerX+side*.055f,y,bay*5.04f),new Vector3(1.2f,height,1.2f));
                for(int bay=0;bay<2;bay++)
                {
                    Beam(art,new Vector3(outerX+side*.08f,y+.30f,bay*5.04f+.25f),new Vector3(outerX+side*.08f,y+height-.30f,(bay+1)*5.04f-.25f),.14f,"OakLight");
                    Beam(art,new Vector3(outerX+side*.08f,y+height-.30f,bay*5.04f+.25f),new Vector3(outerX+side*.08f,y+.30f,(bay+1)*5.04f-.25f),.14f,"OakLight");
                }
            }
            foreach (Transform slot in floor.Cast<Transform>().Where(t=>t.name.StartsWith("SingleCover_")).ToArray())
            {
                while (slot.childCount>0) Object.DestroyImmediate(slot.GetChild(0).gameObject);
                var cabinet=Model("FieldCabinet",slot,slot.position,Vector3.one,slot.rotation);
                var c=cabinet.AddComponent<BoxCollider>(); c.center=new Vector3(0,.96f,0); c.size=new Vector3(.89f,1.92f,.57f);
                cabinet.layer=LayerMask.NameToLayer("Cover");
                foreach (Transform t in cabinet.GetComponentsInChildren<Transform>()) t.gameObject.layer=cabinet.layer;
            }
            // Rear-wall depth, numbered bays and warm light form the view along the duck's route.
            for (int bay=0;bay<6;bay++)
            {
                float x=2.88f+bay*5.76f;
                Model("Window",art,new Vector3(x,y+1.85f,10.00f),Vector3.one);
                Model("Lantern",art,new Vector3(x+1.75f,y+3.55f,9.6f),Vector3.one);
                Beam(art,new Vector3(x+1.75f,y+4.42f,9.65f),new Vector3(x+1.75f,y+4.42f,10.15f),.07f,"Iron");
                if (bay%2==0) PointLight(art,new Vector3(x+1.75f,y+3.95f,8.5f),new Color(1,.73f,.42f),2.3f,8);
            }
            Beam(art,new Vector3(0,y-.34f,-.04f),new Vector3(34.56f,y-.34f,-.04f),.38f,"OakDark");
            Beam(art,new Vector3(0,y-.14f,-.17f),new Vector3(34.56f,y-.14f,-.17f),.095f,"Brass");
            for (int i=0;i<12;i++)
                Beam(art,new Vector3(i*2.88f+.35f,y-.52f,-.04f),new Vector3(i*2.88f+.35f,y-.52f,10.08f),.22f,"OakDark");
            if(index<4)
            {
                float stairX=index%2==0?31.68f:2.88f;
                Label(art,(index+1).ToString("00"),new Vector3(stairX,y+3.0f,-.16f),1.4f,"Cream");
                Label(art,"NEXT FLOOR",new Vector3(stairX,y+1.75f,-.16f),.27f,"Brass");
            }
            foreach (var lever in floor.GetComponentsInChildren<TrapLever>()) DressControl(lever,art,index);
            if (index==4)
            {
                Label(art,"FINISH",new Vector3(32.4f,y+4.6f,7.9f),.5f,"Cream");
                Label(art,"MISSED?  BACK TO THE RAMP",new Vector3(17.28f,y+.04f,1.0f),.23f,"Brass",Quaternion.Euler(90,0,0));
            }
        }

        static string ColorToKey(int index) => index>1 ? "Brass" : "Cream";

        static void DressControl(TrapLever lever, Transform art, int floor)
        {
            Transform pedestal=lever.transform.Find("Pedestal"), handle=lever.transform.Find("Handle");
            Vector3 p=pedestal.position; p.y=floor*5.76f;
            pedestal.GetComponent<Renderer>().enabled=false;
            Model("Console",art,p,Vector3.one);
            // Existing interaction pivot and state remain authoritative; only its visible grip changes.
            Transform bar=handle.Find("Bar"); bar.GetComponent<Renderer>().enabled=false;
            var grip=Model("Lever",handle,handle.position,Vector3.one,handle.rotation);
            var button=lever.GetComponent<TrapActivationButton>();
            var so=new SerializedObject(button);
            // Indicator is a tiny light, not an entire multi-material handle being flattened to one material.
            var indicator=Solid(handle,"State lamp",handle.position+handle.rotation*new Vector3(0,.43f,-.055f),Vector3.one*.055f,"Glow");
            so.FindProperty("indicator").objectReferenceValue=indicator.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void DressDoor(MeshRenderer panel)
        {
            Bounds b=panel.bounds;panel.enabled=false;
            Transform skin=Group(panel.transform,"CopperwoodDoor"); skin.position=b.center;skin.rotation=Quaternion.identity;skin.localScale=new Vector3(1/panel.transform.lossyScale.x,1/panel.transform.lossyScale.y,1/panel.transform.lossyScale.z);
            Model("TrapDoor",skin,new Vector3(b.center.x,b.min.y,b.center.z),new Vector3(b.size.z,b.size.y,b.size.x/.14f),Quaternion.Euler(0,90,0));
        }

        static void DressCollapse(MeshRenderer panel)
        {
            // The trap now consists of several independently disappearing panels.
            // Keep every skin local to its panel: combining world-space geometry
            // into one shared mesh made all panels render on top of the final one.
            panel.enabled=true;
            panel.sharedMaterial=Mat("OakDark");
            Transform skin=Group(panel.transform,"CopperwoodHatch");
            skin.localPosition=Vector3.zero;skin.localRotation=Quaternion.identity;skin.localScale=Vector3.one;

            float sx=Mathf.Abs(panel.transform.lossyScale.x);
            float sy=Mathf.Abs(panel.transform.lossyScale.y);
            float sz=Mathf.Abs(panel.transform.lossyScale.z);
            float top=.5f+.018f/sy;

            foreach(float z in new[]{-.49f,-1f/6f,1f/6f,.49f})
                LocalSolid(skin,"Hatch transverse iron",new Vector3(0,top,z),
                    new Vector3(.98f,.035f/sy,.065f/sz),"Iron");
            foreach(float x in new[]{-.42f,.42f})
            {
                LocalSolid(skin,"Leaf edge",new Vector3(x,top,0f),
                    new Vector3(.055f/sx,.03f/sy,.94f),"Brass");
                foreach(float z in new[]{-.34f,.34f})
                    LocalSolid(skin,"Hatch hinge",new Vector3(x,top+.035f/sy,z),
                        new Vector3(.21f/sx,.07f/sy,.25f/sz),"Iron");
            }
        }

        static void DressLift(Transform shaft) => CraftedHunterLift(shaft);

        static void Roof(Transform art)
        {
            const float y=30.12f;
            // Long low barn roof; both slopes are outside the playable ceiling.
            for(int side=-1;side<=1;side+=2)
            {
                Vector3 a=new Vector3(17.28f,y,5.04f+side*5.95f),b=new Vector3(17.28f,y+3.0f,5.04f);
                Quaternion q=Quaternion.LookRotation(b-a,Vector3.up);
                var roof=Solid(art,"Standing seam roof",(a+b)/2,new Vector3(36.2f,.18f,Vector3.Distance(a,b)),"Teal");roof.transform.rotation=q;
                for(int i=0;i<=36;i++)Beam(art,a+new Vector3(i-18,0,0),b+new Vector3(i-18,0,0),.06f,"Iron");
                Beam(art,a-new Vector3(18.1f,0,0),a+new Vector3(18.1f,0,0),.24f,"OakLight");
            }
            Beam(art,new Vector3(-.8f,y+3.1f,5.04f),new Vector3(35.36f,y+3.1f,5.04f),.22f,"Brass");
            Model("Sign",art,new Vector3(17.28f,30.7f,-1.05f),Vector3.one*1.55f);
            for(int i=0;i<5;i++)Model("Lantern",art,new Vector3(5.5f+i*5.9f,28.4f,-.28f),Vector3.one*.8f);
            // Feet and battered foundation frame make the tall attraction sit in its clearing.
            for(int i=0;i<=6;i++)Model("Timber",art,new Vector3(i*5.76f,-2.2f,.12f),new Vector3(2,1.5f,2));
        }

        static void Landscape(Transform art)
        {
            // Ровная плита «поляны» и разбросанные по ней сосны переехали в
            // FairgroundTerrain и Treeline: там у земли есть рельеф, а у деревьев —
            // правила, куда им нельзя.
            // The playable tower stays at its tested world height; a substantial
            // timber plinth closes the visual gap down to the fairground ground.
            Solid(art,"Tower foundation",new Vector3(17.28f,-1.86f,7),new Vector3(35.2f,2.28f,14.5f),"OakDark");
            Solid(art,"Lift foundation",new Vector3(17.28f,-2.5f,-11.52f),new Vector3(3.8f,1f,4.8f),"OakDark");
            for(int i=0;i<12;i++)
            {
                float x=-5+i*4;
                Model("Timber",art,new Vector3(x,-3,16),new Vector3(1.1f,2,1.1f));
                if(i<11)for(int h=0;h<2;h++)Beam(art,new Vector3(x,-2.25f+h*.65f,16),new Vector3(x+4,-2.25f+h*.65f,16),.13f,"OakLight");
            }
        }

        static GameObject Beam(Transform root, Vector3 a, Vector3 b, float width, string material)
        {
            var go=Solid(root,"Joinery",(a+b)*.5f,new Vector3(width,width,Vector3.Distance(a,b)),material);
            go.transform.rotation=Quaternion.LookRotation(b-a,Vector3.up);return go;
        }

        static GameObject Solid(Transform root,string name,Vector3 p,Vector3 size,string material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root,true);go.transform.position=p;go.transform.localScale=size;
            Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=Mat(material);return go;
        }

        static GameObject LocalSolid(Transform root,string name,Vector3 localPosition,Vector3 localScale,string material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root,false);
            go.transform.localPosition=localPosition;go.transform.localRotation=Quaternion.identity;go.transform.localScale=localScale;
            Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=Mat(material);return go;
        }

        static void Label(Transform parent,string words,Vector3 p,float size,string material,Quaternion? rotation=null)
        {
            var go=new GameObject(words);go.transform.SetParent(parent,false);go.transform.position=p;go.transform.rotation=rotation??Quaternion.identity;
            var text=go.AddComponent<TextMesh>();text.text=words;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.fontSize=80;text.characterSize=size/8;
            text.color=Mat(material).color;text.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            string path=Root+"/Materials/DHB_WorldType.mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null) { m=new Material(Shader.Find("DuckHunt/WorldType"));m.mainTexture=text.GetComponent<Renderer>().sharedMaterial.mainTexture;AssetDatabase.CreateAsset(m,path); }
            text.GetComponent<Renderer>().sharedMaterial=m;
        }

        static void PointLight(Transform root,Vector3 p,Color color,float intensity,float range)
        {
            var go=Group(root,"Warm lamp");go.position=p;var light=go.gameObject.AddComponent<Light>();
            light.type=LightType.Point;light.color=color;light.intensity=intensity;light.range=range;light.shadows=LightShadows.None;
        }

        static void MovingSpotlight(Transform root,Vector3 p,Vector3 euler,Color color,float phase)
        {
            var go=Group(root,"Moving carnival spotlight");go.position=p;go.rotation=Quaternion.Euler(euler);
            var light=go.gameObject.AddComponent<Light>();light.type=LightType.Spot;light.color=color;
            light.intensity=5.2f;light.range=24f;light.spotAngle=27f;light.innerSpotAngle=15f;light.shadows=LightShadows.None;
            Motion(go,Igruha.Core.Ambient.AmbientMotion.Mode.Sway,Vector3.up,28f,6.8f,phase);
        }

        static void Lighting(Transform art)
        {
            foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if(light.type!=LightType.Directional)continue;
                light.transform.rotation=Quaternion.Euler(8,-112,0);light.color=new Color(.49f,.62f,1f);light.intensity=.32f;light.shadows=LightShadows.Soft;
            }
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.18f,.24f,.38f);RenderSettings.ambientEquatorColor=new Color(.13f,.16f,.23f);RenderSettings.ambientGroundColor=new Color(.065f,.052f,.04f);
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.045f,.075f,.14f);RenderSettings.fogStartDistance=100;RenderSettings.fogEndDistance=350;
            string skyPath=Root+"/Materials/DHB_Sky.mat";
            var sky=AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if(sky==null){sky=new Material(Shader.Find("Skybox/Procedural"));AssetDatabase.CreateAsset(sky,skyPath);}
            sky.SetFloat("_SunDisk",0);sky.SetColor("_SkyTint",new Color(.12f,.20f,.40f));sky.SetColor("_GroundColor",new Color(.035f,.05f,.09f));sky.SetFloat("_AtmosphereThickness",.7f);sky.SetFloat("_Exposure",.35f);RenderSettings.skybox=sky;EditorUtility.SetDirty(sky);
            Transform illumination=Group(art,"Pavilion warm illumination");Color warm=new Color(1,.65f,.32f);
            for(int floor=0;floor<5;floor++)
            {
                float y=floor*5.76f;
                for(int bay=0;bay<5;bay++)
                {
                    float x=3+bay*7.1f;
                    PointLight(illumination,new Vector3(x,y+3.8f,7.8f),warm,12f,9.0f);
                    PointLight(illumination,new Vector3(x,y+3.6f,-1.0f),new Color(1,.80f,.58f),14f,10.0f);
                }
                var pendant=Group(illumination,"Pendant pool "+floor);pendant.position=new Vector3(17.28f,y+(floor==4?5.7f:4.4f),5.5f);pendant.rotation=Quaternion.Euler(90,0,0);
                var key=pendant.gameObject.AddComponent<Light>();key.type=LightType.Spot;key.color=new Color(1,.80f,.55f);key.intensity=20;key.range=10;key.spotAngle=125;key.innerSpotAngle=85;key.shadows=LightShadows.Soft;key.shadowCustomResolution=512;key.shadowBias=.035f;key.shadowNormalBias=.15f;
            }
            for(int i=0;i<6;i++)PointLight(illumination,new Vector3(3+i*5.7f,30.7f,-.6f),warm,2.6f,6.2f);
            for(float x=-44f;x<=78f;x+=22f)foreach(int side in new[]{-1,1})PointLight(illumination,new Vector3(x,1.3f,PromenadeZ+side*(PromenadeHalf+.9f)),warm,3f,10f);
            foreach(int side in new[]{-1,1})PointLight(illumination,new Vector3(FairCentre.x+side*5.6f,1.6f,GateZ-.9f),warm,3.1f,10f);
        }


        static void Combine(Transform group,string name)
        {
            // Batch by material, but never cross moving trap/lift parents or eat TextMesh/lights.
            var renderers=group.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled && r.GetComponent<TextMesh>()==null && r.GetComponent<MeshFilter>()!=null).ToArray();
            var buckets=new Dictionary<Material,List<CombineInstance>>();
            foreach(var r in renderers)
            {
                Mesh mesh=r.GetComponent<MeshFilter>().sharedMesh;
                for(int i=0;i<mesh.subMeshCount;i++)
                {
                    Material m=r.sharedMaterials[i];if(!buckets.ContainsKey(m))buckets[m]=new List<CombineInstance>();
                    buckets[m].Add(new CombineInstance{mesh=mesh,subMeshIndex=i,transform=group.worldToLocalMatrix*r.localToWorldMatrix});
                }
            }
            foreach(var pair in buckets)
            {
                var mesh=new Mesh{name=name+"_"+pair.Key.name,indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(pair.Value.ToArray(),true,true);
                if(mesh.vertexCount==0)throw new InvalidOperationException("Empty combined source geometry: "+mesh.name);
                Compact(mesh);
                string path=Root+"/Meshes/"+mesh.name+".asset";var previous=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(previous==null)AssetDatabase.CreateAsset(mesh,path);
                else
                {
                    // Native Mesh buffers must be uploaded, not merely serialized over a cached GPU mesh.
                    previous.Clear();previous.indexFormat=IndexFormat.UInt32;
                    previous.vertices=mesh.vertices;previous.normals=mesh.normals;previous.uv=mesh.uv;
                    previous.triangles=mesh.triangles;previous.RecalculateBounds();previous.UploadMeshData(false);
                    EditorUtility.SetDirty(previous);Object.DestroyImmediate(mesh);mesh=previous;
                }
                var go=Group(group,mesh.name);go.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;go.gameObject.AddComponent<MeshRenderer>().sharedMaterial=pair.Key;
            }
            foreach(var r in renderers)Object.DestroyImmediate(r.gameObject);
            CleanEmpty(group);
        }

        static void Compact(Mesh mesh)
        {
            // CombineMeshes copies unused vertices from other FBX material slots too.
            // Remap only referenced vertices before serializing each material batch.
            var old=mesh.vertices;var normals=mesh.normals;var uv=mesh.uv;var indices=mesh.triangles;
            var map=new int[old.Length];for(int i=0;i<map.Length;i++)map[i]=-1;
            var points=new List<Vector3>();var outNormals=new List<Vector3>();var outUV=new List<Vector2>();
            for(int i=0;i<indices.Length;i++)
            {
                int source=indices[i];
                if(map[source]<0)
                {
                    map[source]=points.Count;points.Add(old[source]);
                    outNormals.Add(normals.Length>source?normals[source]:Vector3.up);
                    outUV.Add(uv.Length>source?uv[source]:Vector2.zero);
                }
                indices[i]=map[source];
            }
            mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;mesh.SetVertices(points);mesh.SetNormals(outNormals);mesh.SetUVs(0,outUV);mesh.triangles=indices;mesh.RecalculateBounds();
        }

        static void CleanEmpty(Transform root)
        {
            foreach(Transform child in root.Cast<Transform>().ToArray())
            {
                CleanEmpty(child);
                if(child.childCount==0 && child.GetComponents<Component>().Length==1)Object.DestroyImmediate(child.gameObject);
            }
        }
    }
}
