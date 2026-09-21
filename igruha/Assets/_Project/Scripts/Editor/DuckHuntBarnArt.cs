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
            var original = arena.GetComponentsInChildren<MeshRenderer>().ToArray();
            foreach (var renderer in original)
            {
                if (renderer.transform.GetComponentInParent<TrapLever>() != null || renderer.name.StartsWith("Cover_")) continue;
                string n = renderer.name;
                if (n == "Ceiling") { renderer.sharedMaterial = Mat("OakDark"); DressCeiling(renderer,art); continue; }
                if (n == "Panel" && renderer.GetComponentInParent<DoorTrap>() != null)
                { DressDoor(renderer); continue; }
                if (n == "Section" && renderer.GetComponentInParent<CollapsingFloorTrap>() != null)
                { DressCollapse(renderer); continue; }
                if (n == "Foot" || n == "ChestBand" || n == "ChestBandSmall" || n == "OnePersonFootprint") continue;
                if (n.Contains("Wall") || n.StartsWith("ProtectedFront") || n == "ExitPartition" || n == "ExitHeader" || n == "SpawnShield" || n == "ArrivalGuard")
                { CarnivalWall(renderer, art); continue; }
                if (n.StartsWith("Link_") || n.StartsWith("ReturnLane_") || n == "TakeoffStripe")
                { renderer.sharedMaterial = Mat(n.StartsWith("Link") ? "Red" : "Brass"); continue; }
                if (n.StartsWith("PadSupport") || n.StartsWith("Rail_"))
                { renderer.sharedMaterial = Mat("Iron"); continue; }
                if (n == "FinishPad") { renderer.sharedMaterial = Mat("TealLight"); continue; }
                if (n == "Deck") continue; // Moving lift art stays on the moving platform.
                if (renderer.GetComponent<BoxCollider>() != null)
                    DressDeck(renderer, art, n.StartsWith("Parkour") || n == "RaisedFinishDeck");
            }
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
            Combine(art, "Architecture");
            Physics.SyncTransforms();
            Debug.Log("Duck Hunt carnival installed: original Blender fairground kit, protected start, five themed floors, 4/3/2/1 functional furnishings, exposed mechanical controls.");
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
                {"Iron",new Color(.105f,.14f,.14f)}, {"Brass",new Color(.78f,.54f,.24f)}, {"Red",new Color(.79f,.23f,.19f)},
                {"Rose",new Color(.84f,.34f,.43f)}, {"Blue",new Color(.19f,.52f,.64f)}, {"Purple",new Color(.57f,.35f,.66f)}, {"Gold",new Color(.97f,.68f,.20f)},
                {"Glow",new Color(1,.79f,.40f)}, {"Leaf",new Color(.19f,.32f,.235f)}, {"LeafLight",new Color(.32f,.43f,.25f)},
                {"Stone",new Color(.39f,.42f,.36f)}, {"Ink",new Color(.035f,.075f,.072f)}, {"Earth",new Color(.32f,.34f,.20f)},
                {"Sand",new Color(.60f,.52f,.38f)}, {"Gravel",new Color(.47f,.45f,.40f)}
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
                if (entry.Key == "Glow")
                {
                    m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(1,.77f,.42f)*5.8f);
                    // Без этого флага материал остаётся EmissiveIsBlack, Unity
                    // вычищает ключевое слово при сохранении, и лампы гаснут:
                    // ровно так свечение и пропало в прошлый раз.
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
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
            Transform skin=Group(panel.transform,"CopperwoodHatch"); skin.position=Vector3.zero;skin.rotation=Quaternion.identity;
            skin.localScale=new Vector3(1/panel.transform.lossyScale.x,1/panel.transform.lossyScale.y,1/panel.transform.lossyScale.z);
            DressDeck(panel,skin,true);
            Bounds hatch=panel.bounds;
            // Three readable mechanical leaves inside the original floor trigger.
            for(int section=0;section<3;section++)
            {
                float z0=hatch.min.z+section*hatch.size.z/3f,z1=z0+hatch.size.z/3f;
                foreach(float z in new[]{z0+.035f,z1-.035f})
                    Solid(skin,"Hatch transverse iron",new Vector3(hatch.center.x,hatch.max.y+.018f,z),new Vector3(hatch.size.x,.035f,.065f),"Iron");
                foreach(float x in new[]{hatch.min.x+.10f,hatch.max.x-.10f})
                {
                    Solid(skin,"Leaf edge",new Vector3(x,hatch.max.y+.015f,(z0+z1)*.5f),new Vector3(.055f,.03f,z1-z0-.07f),"Brass");
                    foreach(float z in new[]{z0+.38f,z1-.38f})
                        Solid(skin,"Hatch hinge",new Vector3(x,hatch.max.y+.04f,z),new Vector3(.21f,.07f,.25f),"Iron");
                }
            }
            panel.enabled=false;
            Combine(skin,"CollapseHatch");
            var so=new SerializedObject(panel.GetComponentInParent<CollapsingFloorTrap>());
            var renderers=skin.GetComponentsInChildren<Renderer>(); var list=so.FindProperty("floorRenderers");list.arraySize=renderers.Length;
            for(int i=0;i<renderers.Length;i++)list.GetArrayElementAtIndex(i).objectReferenceValue=renderers[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void DressLift(Transform shaft)
        {
            Transform platform=shaft.Find("Platform"), moving=Group(platform,"CopperwoodLift");
            var deck=platform.Find("Deck").GetComponent<MeshRenderer>(); DressDeck(deck,moving,true);
            Vector3 p=platform.position;
            // Keep guide rails and mechanism behind the hunter/camera, never across the gun sight.
            Model("Hoist",moving,p+new Vector3(0,1.45f,-2.15f),Vector3.one);
            Beam(moving,p+new Vector3(-1.3f,.2f,-1.65f),p+new Vector3(1.3f,.2f,-1.65f),.20f,"OakLight");
            Transform frame=Group(shaft,"CopperwoodGantry");
            for(int side=-1;side<=1;side+=2)
                Model("Timber",frame,new Vector3(17.28f+side*1.8f,-2,-14.6f),new Vector3(2,37,2));
            Beam(frame,new Vector3(15,35,-14.6f),new Vector3(19.55f,35,-14.6f),.40f,"OakDark");
            Model("Hoist",frame,new Vector3(17.28f,34.5f,-14.6f),Vector3.one*1.2f);
            Beam(frame,new Vector3(17.28f,-1.7f,-14.8f),new Vector3(17.28f,34.4f,-14.8f),.033f,"Iron");
            Combine(moving,"MovingLift");Combine(frame,"Gantry");
        }

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

        static void Lighting(Transform art)
        {
            foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if(light.type!=LightType.Directional)continue;
                // Low westward sun: in the front establishing view it sits to the
                // left of the tower and is already half below the horizon.
                light.transform.rotation=Quaternion.Euler(3,-112,0);light.color=new Color(1,.62f,.38f);light.intensity=1.05f;light.shadows=LightShadows.Soft;
            }
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.57f,.68f,.72f);RenderSettings.ambientEquatorColor=new Color(.56f,.55f,.44f);RenderSettings.ambientGroundColor=new Color(.35f,.29f,.21f);
            // Туман отодвинут: сцена выросла с поляны до ярмарки с дорогой и окраиной
            // города, и на прежних 78..220 м всё дальше ворот тонуло в песочной пелене.
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.78f,.66f,.52f);
            RenderSettings.fogStartDistance=115;RenderSettings.fogEndDistance=430;
            string skyPath=Root+"/Materials/DHB_Sky.mat";
            var sky=AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if(sky==null) { sky=new Material(Shader.Find("Skybox/Procedural"));AssetDatabase.CreateAsset(sky,skyPath); }
            sky.SetColor("_SkyTint",new Color(.59f,.55f,.57f));sky.SetColor("_GroundColor",new Color(.58f,.44f,.31f));
            sky.SetFloat("_AtmosphereThickness",1.1f);sky.SetFloat("_Exposure",1.05f);RenderSettings.skybox=sky;EditorUtility.SetDirty(sky);
            // Emission alone does not light characters or floorboards.  These
            // compact real lamps follow the visible lantern/bulb rhythm without
            // turning every decoration into a shadow-casting cost.
            Transform illumination=Group(art,"Working illumination");
            for(int floor=0;floor<5;floor++)
                for(int bay=0;bay<3;bay++)
                    PointLight(illumination,new Vector3(5.4f+bay*11.5f,floor*5.76f+3.75f,8.25f),new Color(1,.62f,.30f),2.0f,6.5f);
            for(int i=0;i<7;i++)
                PointLight(illumination,new Vector3(i*5.76f,29.45f,-.32f),new Color(1,.66f,.34f),1.65f,5.5f);
            // Фонари аллеи и ворот: сами лампы входят в склеенный статический
            // меш, поэтому реальный свет им выдаётся отдельно и по новым местам.
            for(float x=-44f;x<=78f;x+=22f)
                foreach(int side in new[]{-1,1})
                    PointLight(illumination,new Vector3(x,1.3f,PromenadeZ+side*(PromenadeHalf+.9f)),new Color(1,.58f,.26f),2.1f,9f);
            foreach(int side in new[]{-1,1})
                PointLight(illumination,new Vector3(FairCentre.x+side*5.6f,1.6f,GateZ-.9f),new Color(1,.62f,.32f),2.4f,10f);
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
