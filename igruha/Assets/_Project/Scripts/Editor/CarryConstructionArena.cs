using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Igruha.Minigames.CarryItem;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Construction site pass: purposeful route choices, concrete structure and circulation.</summary>
    internal static class CarryConstructionArena
    {
        private const string Art="Assets/_Project/Art/CarryItem/HeistRoutes";
        private const string Mats=Art+"/Materials";
        private static readonly Dictionary<string,Material> materialCache=new Dictionary<string,Material>();
        private const float SlabThickness=.32f, DeckTop=0f, RailHeight=1.14f;
        internal static void Apply()
        {
            Transform arena=GameObject.Find("_Arena").transform;
            OpenGalleryEntrances(arena);
            ClearExpandedBackground(arena);
            CleanOldWayfinding(arena);
            DressConcrete(arena);
            LightSite();
            var root=arena.Find("HeistRoutes");
            BuildWayfinding(root);
            CarryArenaCollision.Apply(arena);
            CarrySiteFinish.Apply(arena);
        }
        internal static void BuildGalleries(Transform root)
        {
            materialCache.Clear();
            foreach(int side in new[]{-1,1})
            {
                var points=CarryRouteLayout.Gallery(side);
                points[0].z=side*14.4f;points[points.Length-1].z=side*14.4f;
                var left=new Vector3[points.Length];var right=new Vector3[points.Length];
                for(int i=0;i<points.Length;i++)
                {
                    var before=(points[i]-points[Mathf.Max(0,i-1)]).normalized;
                    var after=(points[Mathf.Min(points.Length-1,i+1)]-points[i]).normalized;
                    if(i==0)before=after;if(i==points.Length-1)after=before;
                    Vector3 normal=Vector3.Cross(Vector3.up,(before+after).normalized);
                    float projection=Vector3.Dot(normal,Vector3.Cross(Vector3.up,before));
                    var offset=normal*(CarryRouteLayout.GalleryWidth*.5f/projection);
                    left[i]=points[i]+offset+Vector3.up*DeckTop;right[i]=points[i]-offset+Vector3.up*DeckTop;
                }
                var gallery=Group(root,side>0?"Concrete gallery north":"Concrete gallery south");
                var mesh=Ribbon(left,right,side);
                gallery.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
                gallery.gameObject.AddComponent<MeshRenderer>().sharedMaterial=Concrete();
                gallery.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;
                gallery.gameObject.layer=LayerMask.NameToLayer("Ground");
                for(int i=1;i<points.Length;i++)
                {
                    Rail(gallery,left[i-1],left[i]);Rail(gallery,right[i-1],right[i]);
                    Beam(gallery,left[i-1]-Vector3.up*.4f,left[i]-Vector3.up*.4f,.16f,.28f,Metal());
                    Beam(gallery,right[i-1]-Vector3.up*.4f,right[i]-Vector3.up*.4f,.16f,.28f,Metal());
                    Vector3 d=points[i]-points[i-1];int count=Mathf.Max(1,Mathf.CeilToInt(d.magnitude/4));
                    for(int j=0;j<count;j++)
                    {
                        Vector3 p=Vector3.Lerp(points[i-1],points[i],(j+.5f)/count);
                        CarryRoadArena.Chevron(gallery,p+Vector3.up*.029f,Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg,Paint("Safe",new Color(.31f,.51f,.42f)),.48f);
                        if(j>0)
                        {
                            Vector3 n=Vector3.Cross(Vector3.up,d.normalized)*(CarryRouteLayout.GalleryWidth*.5f-.12f);
                            Beam(gallery,p-n+Vector3.up*.021f,p+n+Vector3.up*.021f,.018f,.006f,Metal(),false);
                        }
                    }
                }
                // Outriggers extend to the lower structural storey; the deck has visible support.
                foreach(float x in new[]{-15.3f,-7.5f,0,8,16.2f})
                {
                    var top=new Vector3(x,-.25f,side*CarryRouteLayout.GalleryOutside);
                    Beam(gallery,top+new Vector3(0,-5.15f,-side*(CarryRouteLayout.GalleryOutside-14.4f)),top,.24f,.24f,Metal());
                    Beam(gallery,top+new Vector3(0,-.22f,-side*(CarryRouteLayout.GalleryOutside-14.4f)),top,.24f,.24f,Metal());
                }
            }
        }
        private static Mesh Ribbon(Vector3[] left,Vector3[] right,int side)
        {
            string path=Art+"/Gallery"+(side>0?"North":"South")+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool fresh=mesh==null;
            if(fresh)mesh=new Mesh{name="Continuous concrete gallery"};else mesh.Clear();
            var v=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            Action<Vector3,Vector3,Vector3,Vector3> quad=(a,b,c,d)=>
            {
                int n=v.Count;v.Add(a);v.Add(b);v.Add(c);v.Add(d);
                uv.Add(new Vector2(a.x,a.z));uv.Add(new Vector2(b.x,b.z));uv.Add(new Vector2(c.x,c.z));uv.Add(new Vector2(d.x,d.z));
                triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
            };
            Vector3 down=Vector3.down*SlabThickness;
            for(int i=1;i<left.Length;i++)
            {
                quad(left[i-1],right[i-1],right[i],left[i]);
                quad(left[i-1]+down,left[i]+down,right[i]+down,right[i-1]+down);
                quad(left[i-1],left[i],left[i]+down,left[i-1]+down);
                quad(right[i-1]+down,right[i]+down,right[i],right[i-1]);
            }
            quad(right[0],left[0],left[0]+down,right[0]+down);
            int last=left.Length-1;quad(left[last],right[last],right[last]+down,left[last]+down);
            mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            if(fresh)AssetDatabase.CreateAsset(mesh,path);else{EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);}return mesh;
        }
        private static void OpenGalleryEntrances(Transform arena)
        {
            var structure=arena.Find("Environment/Structure");
            foreach(Transform t in structure)
            {
                if(t.name=="CS_Column"&&Mathf.Abs(t.position.y)<.1f&&Mathf.Abs(t.position.x-18)<.1f)
                    t.position=new Vector3(23.6f,t.position.y,t.position.z);
                if(t.name=="CS_Column"&&Mathf.Abs(t.position.y)<.1f&&Mathf.Abs(t.position.x+16)<.1f)
                    t.position=new Vector3(-22.6f,t.position.y,t.position.z);
            }
            for(int i=structure.childCount-1;i>=0;i--)
            {
                var t=structure.GetChild(i);
                if(t.name=="CS_Guardrail"&&Mathf.Abs(t.position.y)<.1f&&Mathf.Abs(Mathf.Abs(t.position.z)-14.4f)<.1f)
                    Object.DestroyImmediate(t.gameObject);
            }
            var edge=Group(arena.Find("HeistRoutes"),"Perimeter with gallery openings");
            foreach(int side in new[]{-1,1})
            {
                float z=side*14.4f;
                Rail(edge,new Vector3(-27.3f,0,z),new Vector3(-21,0,z));
                Rail(edge,new Vector3(-16,0,z),new Vector3(16.9f,0,z));
                Rail(edge,new Vector3(21.9f,0,z),new Vector3(27.3f,0,z));
            }
            foreach(var t in arena.Find("Roadworks").GetComponentsInChildren<Transform>())
                if(t.name=="RW_Debris"&&t.position.x>17)t.position=new Vector3(25.8f,.005f,Mathf.Sign(t.position.z)*5f);
            foreach(var t in arena.Find("HeistRoutes/WorkIslands").GetComponentsInChildren<Transform>())
                if(t.name=="RW_Debris"&&t.position.x> -21)t.position=new Vector3(-25.8f,.005f,Mathf.Sign(t.position.z)*12.5f);
            foreach(var t in arena.Find("BotRoutes").GetComponentsInChildren<Transform>())
            {
                float side=Mathf.Sign(t.position.z);var p=t.position;
                if(t.name=="Plank1_Far"||t.name=="Neck_In")p.x=-3.2f;
                if(t.name=="Plank2_Near"||t.name=="Neck_Out")p.x=7.2f;
                t.position=p;
            }
            // Storage belongs behind the loading bay, not across its new branch.
            foreach(var t in arena.Find("Environment/WorkAreas").GetComponentsInChildren<Transform>())
                if(t.name=="CS_CementBags"&&t.parent==arena.Find("Environment/WorkAreas"))t.position=new Vector3(-26,0,Mathf.Sign(t.position.z)*8.5f);
        }
        private static void ClearExpandedBackground(Transform arena)
        {
            foreach(Transform t in arena.Find("Environment/Horizon"))
            {
                if(t.name=="CS_CraneMast"||t.name=="CS_CraneTop")
                {
                    if(Mathf.Abs(t.position.x-18)<.1f&&Mathf.Abs(t.position.z+25)<.1f)t.position+=new Vector3(24,0,-17);
                    if(Mathf.Abs(t.position.x+34)<.1f&&Mathf.Abs(t.position.z-36)<.1f)t.position+=new Vector3(-26,0,16);
                }
                // Lower-storey columns used to poke through the playable slab without a body.
                if(t.name=="CS_Column"&&Mathf.Abs(t.position.y+5.4f)<.1f)
                {
                    var bounds=CarryItemDress.BoundsOf(t.gameObject);
                    if(bounds.max.y>.02f){var scale=t.localScale;scale.y*=5.4f/(bounds.max.y-t.position.y);t.localScale=scale;}
                }
            }
        }
        private static void CleanOldWayfinding(Transform arena)
        {
            var decor=arena.Find("Environment/Decor");
            for(int i=decor.childCount-1;i>=0;i--)
            {
                var t=decor.GetChild(i);
                if(t.name=="CS_ArrowA"||t.name=="CS_ArrowB")Object.DestroyImmediate(t.gameObject);
            }
            var routes=arena.Find("Roadworks/Routes");
            for(int i=routes.childCount-1;i>=0;i--)
            {
                var t=routes.GetChild(i);
                if(t.name=="RW_SmoothSign"||t.name=="RoadPaint"&&Mathf.Abs(t.position.z)>6)Object.DestroyImmediate(t.gameObject);
            }
        }
        private static void BuildWayfinding(Transform parent)
        {
            var root=Group(parent,"Route markings");
            foreach(int side in new[]{-1,1})
            {
                // The safe branch is longer because it goes around the building, not an arbitrary slowdown.
                CarryRoadArena.Polyline(root,new[]{new Vector3(-18.5f,0,side*9),new Vector3(-18.5f,0,side*14.2f)},Paint("Safe",new Color(.31f,.51f,.42f)),.16f);
                CarryRoadArena.Place(root,"SmoothSign",new Vector3(-21.5f,0,side*11.7f),side>0?0:180,true);
                Sign(root,new Vector3(-16.9f,0,side*8.2f),side,"КОРОТКО","ТРЯСКИЙ НАСТИЛ",false);
                Sign(root,new Vector3(-21.5f,0,side*10.4f),side,"В ОБХОД","РОВНО · ДОЛЬШЕ",true);
                CarryRoadArena.Chevron(root,new Vector3(-16.2f,.028f,side*5.04f),90,Paint("Rough",new Color(.69f,.35f,.12f)),.65f);
                CarryRoadArena.Chevron(root,new Vector3(-18.5f,.028f,side*12.5f),side>0?0:180,Paint("Safe",new Color(.31f,.51f,.42f)),.65f);
            }
            Sign(root,new Vector3(-23.2f,0,.8f),1,"ПО ВЕРХУ","ПРОПУСТИ БАЛКУ",false);
        }
        private static void Sign(Transform root,Vector3 p,int side,string title,string subtitle,bool safe)
        {
            var sign=Group(root,"Construction route sign");sign.position=p;
            // Boards face the loading area; type complements the shape and colour on the road.
            sign.rotation=Quaternion.Euler(0,-90,0);
            Block(sign,"Foot",new Vector3(0,.06f,0),new Vector3(.7f,.12f,.55f),Concrete(),true);
            Block(sign,"Post",new Vector3(0,.88f,0),new Vector3(.09f,1.75f,.09f),Metal(),true);
            Block(sign,"Board",new Vector3(0,1.75f,0),new Vector3(2.0f,.92f,.10f),Paint("Board",new Color(.73f,.73f,.68f)),true);
            Block(sign,"Stripe",new Vector3(-.84f,1.75f,.06f),new Vector3(.14f,.76f,.015f),safe?Paint("Safe",new Color(.31f,.51f,.42f)):Paint("Rough",new Color(.69f,.35f,.12f)),false);
            Label(sign,title,new Vector3(.03f,1.93f,.065f),.24f);
            Label(sign,subtitle,new Vector3(.03f,1.59f,.065f),.13f);
        }
        private static void Label(Transform parent,string text,Vector3 p,float size)
        {
            var t=Group(parent,"Route label");t.localPosition=p;t.localRotation=Quaternion.Euler(0,180,0);
            var label=t.gameObject.AddComponent<TMPro.TextMeshPro>();label.text=text;label.fontSize=size*10;label.color=new Color(.09f,.105f,.11f);
            label.alignment=TMPro.TextAlignmentOptions.Center;label.rectTransform.sizeDelta=new Vector2(1.72f,.42f);
        }
        private static void DressConcrete(Transform arena)
        {
            var map=new Dictionary<string,Material>
            {
                {"RW_Teal",Metal()},{"RW_Steel",Metal()},{"RW_Concrete",Concrete()},{"RW_Floor",Concrete()},
                {"RW_Brick",Tint("Block",new Color(.48f,.48f,.46f))},{"RW_Ivory",Tint("Chalk",new Color(.72f,.72f,.68f))},
                {"CS_Concrete",Concrete()},{"CS_ConcreteEdge",Tint("Concrete edge",new Color(.40f,.41f,.40f))},
                {"CS_CabinBlue",Tint("Equipment",new Color(.32f,.36f,.37f))},{"CS_TarpBlue",Tint("Tarpaulin",new Color(.33f,.36f,.34f))},
                {"CS_Terracotta",Tint("Block",new Color(.48f,.48f,.46f))},{"CS_Galvanized",Metal()},
                {"CS_Timber",Tint("Timber",new Color(.45f,.39f,.30f))},{"CS_Plywood",Tint("Shuttering",new Color(.37f,.32f,.25f))}
            };
            foreach(var renderer in arena.GetComponentsInChildren<MeshRenderer>())
            {
                // Keep cart, water, team equipment and the distant city unchanged.
                if(renderer.transform.IsChildOf(arena.Find("TeamProps"))||renderer.transform.IsChildOf(arena.Find("Environment/Horizon")))continue;
                var materials=renderer.sharedMaterials;bool changed=false;
                for(int i=0;i<materials.Length;i++)
                {
                    if(materials[i]==null)continue;
                    bool deck=renderer.transform.parent!=null&&renderer.transform.parent.name=="HR_Deck";
                    if(deck&&materials[i].name=="RW_Steel"){materials[i]=Concrete();changed=true;}
                    else if(map.TryGetValue(materials[i].name,out var replacement)){materials[i]=replacement;changed=true;}
                }
                if(changed)renderer.sharedMaterials=materials;
            }
        }
        private static void LightSite()
        {
            var sun=RenderSettings.sun;
            if(sun!=null){sun.color=new Color(.98f,.965f,.92f);sun.intensity=1.35f;sun.transform.rotation=Quaternion.Euler(32,118,0);}
            var fill=GameObject.Find("CarryItemSkyFill");
            if(fill!=null){var light=fill.GetComponent<Light>();light.color=new Color(.69f,.77f,.88f);light.intensity=.30f;}
            RenderSettings.ambientSkyColor=new Color(.55f,.63f,.71f);
            RenderSettings.ambientEquatorColor=new Color(.57f,.59f,.60f);RenderSettings.ambientGroundColor=new Color(.28f,.28f,.29f);
            RenderSettings.fogColor=new Color(.70f,.74f,.78f);
            string skyPath=Mats+"/Site_Sky.mat";var sky=AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if(sky==null){sky=new Material(RenderSettings.skybox);AssetDatabase.CreateAsset(sky,skyPath);}
            sky.SetColor("_SkyTint",new Color(.50f,.56f,.64f));sky.SetColor("_GroundColor",new Color(.58f,.61f,.63f));
            sky.SetFloat("_AtmosphereThickness",1.05f);sky.SetFloat("_Exposure",1.1f);RenderSettings.skybox=sky;
            EditorUtility.SetDirty(sky);AssetDatabase.SaveAssetIfDirty(sky);
            string profilePath=Art+"/Site_Daylight.asset";
            var profile=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(profilePath);
            if(profile==null)
            {
                profile=ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();AssetDatabase.CreateAsset(profile,profilePath);
                var source=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(CarrySkyscraperAssets.Materials+"/CS_Daylight.asset");
                foreach(var component in source.components){var clone=Object.Instantiate(component);profile.components.Add(clone);AssetDatabase.AddObjectToAsset(clone,profile);}
            }
            if(profile.TryGet<UnityEngine.Rendering.Universal.WhiteBalance>(out var white)){white.temperature.Override(0);white.tint.Override(0);}
            if(profile.TryGet<UnityEngine.Rendering.Universal.ColorAdjustments>(out var color)){color.postExposure.Override(.05f);color.contrast.Override(15);color.saturation.Override(-4);}
            if(profile.TryGet<UnityEngine.Rendering.Universal.SplitToning>(out var tone))
            {tone.shadows.Override(new Color(.46f,.49f,.52f));tone.highlights.Override(new Color(.52f,.51f,.48f));tone.balance.Override(0);}
            foreach(var component in profile.components)EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);
            var volume=GameObject.Find("CarryItemGoldenHour");if(volume!=null)volume.GetComponent<UnityEngine.Rendering.Volume>().sharedProfile=profile;
        }
        internal static Material Concrete()
        {
            var m=Tint("Concrete",new Color(.68f,.69f,.68f));
            if(m.GetTexture("_BaseMap")!=null&&Mathf.Approximately(m.GetFloat("_Smoothness"),.09f))return m;
            m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/CarryItem/Original/Textures/CS_Concrete.png"));
            m.SetFloat("_Smoothness",.09f);EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);return m;
        }
        private static Material Metal(){var m=Tint("Galvanized",new Color(.33f,.36f,.36f));if(m.GetFloat("_Metallic")>.5f&&Mathf.Approximately(m.GetFloat("_Smoothness"),.24f))return m;m.SetFloat("_Metallic",.55f);m.SetFloat("_Smoothness",.24f);EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);return m;}
        private static Material Paint(string n,Color c)=>Tint(n,c);
        private static Material Tint(string n,Color color)
        {
            if(materialCache.TryGetValue(n,out var cached))return cached;
            System.IO.Directory.CreateDirectory(Mats);string path=Mats+"/Site_"+n.Replace(' ','_')+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
            m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.13f);m.enableInstancing=true;
            EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);materialCache[n]=m;return m;
        }
        private static void Rail(Transform parent,Vector3 a,Vector3 b)
        {
            Vector3 d=b-a;int count=Mathf.CeilToInt(d.magnitude/4);float part=d.magnitude/count;
            for(int i=0;i<count;i++)
            {
                var t=Group(parent,"Site railing");t.position=Vector3.Lerp(a,b,(i+.5f)/count);t.rotation=Quaternion.FromToRotation(Vector3.right,d.normalized);
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(Art+"/Models/HR_Rail.fbx");
                var visual=(GameObject)PrefabUtility.InstantiatePrefab(model,t);visual.transform.localScale=new Vector3(part/4,1,1);
                var box=t.gameObject.AddComponent<BoxCollider>();box.center=new Vector3(0,RailHeight*.5f,0);box.size=new Vector3(part,RailHeight,.1f);t.gameObject.layer=LayerMask.NameToLayer("Cover");
            }
        }
        private static void Beam(Transform parent,Vector3 a,Vector3 b,float width,float height,Material mat,bool solid=true)
        {
            var t=Block(parent,solid?"Structural member":"Expansion paint",Vector3.zero,new Vector3(width,height,Vector3.Distance(a,b)),mat,solid);
            t.position=(a+b)*.5f;t.rotation=Quaternion.LookRotation(b-a);
        }
        private static Transform Block(Transform parent,string name,Vector3 p,Vector3 scale,Material mat,bool solid)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=p;g.transform.localScale=scale;
            g.GetComponent<Renderer>().sharedMaterial=mat;
            if(solid)g.layer=LayerMask.NameToLayer("Cover");else Object.DestroyImmediate(g.GetComponent<Collider>());return g.transform;
        }
        private static Transform Group(Transform root,string name){var t=new GameObject(name).transform;t.SetParent(root,false);return t;}
    }
}
