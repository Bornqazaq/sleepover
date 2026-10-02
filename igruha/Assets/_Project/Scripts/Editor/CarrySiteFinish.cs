using System.Collections.Generic;
using System.IO;
using Igruha.Minigames.CarryItem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Original mesh kit: supported construction shafts, broken concrete and embedded site fittings.</summary>
    internal static class CarrySiteFinish
    {
        internal const string Art = "Assets/_Project/Art/CarryItem/SiteFinish";
        private static Material concrete, steel, dark, rust, paint;
        internal static void Apply(Transform arena)
        {
            Directory.CreateDirectory(Art + "/Meshes"); Directory.CreateDirectory(Art + "/Materials");
            AssetDatabase.Refresh();
            var old = arena.Find("SiteFinish"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = Group(arena, "SiteFinish");
            concrete = CarryConstructionArena.Concrete();
            steel = Material("Oxidized steel", new Color(.23f,.26f,.27f), .65f);
            dark = Material("Crevices", new Color(.19f,.20f,.20f));
            rust = Material("Rebar", new Color(.32f,.22f,.15f), .5f);
            paint = Material("Warning ochre", new Color(.83f,.51f,.10f));
            Shafts(arena, Group(root, "Structural shafts"));
            SeatOldDetails(arena, Group(root, "Existing detail supports"));
            FloorDetails(Group(root, "Broken concrete"));
            SiteFittings(Group(root, "Site fittings"));
            SoftenDeckContacts(arena, Group(root, "Continuous road contacts"));
            CarryCloudSky.Apply(arena);
            var config = AssetDatabase.LoadAssetAtPath<CarryItemConfig>("Assets/_Project/Settings/Gameplay/Minigames/CarryItemConfig.asset");
            var so = new SerializedObject(config);
            so.FindProperty("overflowRate").floatValue = 700;
            so.FindProperty("turnWaveGain").floatValue = .23f;
            so.FindProperty("waveDamping").floatValue = 3.8f;
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config);
        }
        private static void SoftenDeckContacts(Transform arena, Transform root)
        {
            // Connected slabs share a flat driving surface. Internal vertical
            // walls are omitted only where another deck touches the same edge.
            var boxes=new List<BoxCollider>();var bounds=new List<Bounds>();
            foreach(string group in new[]{"Floors","Planks"})
            foreach(var box in arena.Find(group).GetComponentsInChildren<BoxCollider>())
            {
                if(box.isTrigger||box.gameObject.layer!=LayerMask.NameToLayer("Ground"))continue;
                boxes.Add(box);bounds.Add(new Bounds(box.transform.TransformPoint(box.center),Vector3.Scale(box.size,box.transform.lossyScale)));
            }
            for(int i=0;i<boxes.Count;i++)
            {
                var kit=new Kit();kit.ConnectedDeck(bounds[i],bounds);boxes[i].enabled=false;
                kit.SaveCollision(root,"DeckContact_"+boxes[i].name);
            }
            foreach(var collider in arena.Find("HeistRoutes").GetComponentsInChildren<MeshCollider>())
            {
                if(!collider.name.StartsWith("Concrete gallery"))continue;
                var source=collider.GetComponent<MeshFilter>().sharedMesh;
                var triangles=new List<int>(source.triangles);
                // Ribbon's final two quads are the end walls against the main slab.
                triangles.RemoveRange(triangles.Count-12,12);
                collider.sharedMesh=SaveMesh("Contact_"+collider.name.Replace(' ','_'),new List<Vector3>(source.vertices),new List<Vector2>(source.uv),triangles);
            }
        }

        private static void SeatOldDetails(Transform arena, Transform root)
        {
            // Old lower-storey props were authored over the holes rather than over its actual slabs.
            foreach(Transform t in arena.Find("Environment/Horizon"))
            {
                var p=t.position;
                if(t.name=="CS_CementBags"&&p.y<0&&p.x> -13.68f&&p.x< -5.04f){p.x=-17.2f;t.position=p;}
                if(t.name=="CS_WeldCart"&&p.y<0&&p.x>9.36f&&p.x<16.56f){p.x=20f;t.position=p;}
                if(t.name=="CS_NetPanel"&&p.y<0&&p.y> -6){p.y=-3.65f;t.position=p;}
            }
            var concreteKit=new Kit();var steelKit=new Kit();
            foreach(int side in new[]{-1,1})
            {
                concreteKit.Box(new Vector3(8.12f,-5.72f,side*10.8f),new Vector3(2.5f,.64f,4f));
                // Continuous attachment rail explains why the safety nets hang beyond the slab.
                steelKit.Box(new Vector3(0,-.86f,side*14.57f),new Vector3(54.7f,.16f,.12f));
                for(float x=-26;x<27;x+=3.4f)steelKit.Box(new Vector3(x,-.86f,side*14.25f),new Vector3(.1f,.16f,.8f));
            }
            concreteKit.Save(root,"Scaffold service ledges",concrete,true);steelKit.Save(root,"Safety net attachments",steel,true);
            // These supported shortcuts now read as concrete site crossings, not timber carrying concrete potholes.
            foreach(var renderer in arena.Find("Planks").GetComponentsInChildren<MeshRenderer>())
            {
                var mats=renderer.sharedMaterials;
                for(int i=0;i<mats.Length;i++)if(mats[i]!=null&&(mats[i].name.Contains("Timber")||mats[i].name.Contains("Plywood")))mats[i]=concrete;
                renderer.sharedMaterials=mats;
            }
        }
        private static void Shafts(Transform arena, Transform root)
        {
            // The old shallow pit floors hid the storeys and made everything look suspended over a tray.
            foreach (Transform floor in arena.Find("Floors"))
                if (floor.name.StartsWith("Floor_Chasm")) floor.gameObject.SetActive(false);
            for (int shaft = 0; shaft < 2; shaft++)
            {
                float left = shaft == 0 ? -13.68f : 9.36f, right = shaft == 0 ? -5.04f : 16.56f;
                float middle = (left + right) * .5f, span = right - left;
                var masonry = new Kit(); var metal = new Kit(); var bars = new Kit(); var warning = new Kit();
                foreach (float x in new[] { left, right })
                {
                    float inward = x == left ? 1 : -1;
                    // Flush concrete reveals, lower ring beams and vertical columns connect every deck to the building.
                    masonry.Box(new Vector3(x-inward*.26f,-.49f,0),new Vector3(.52f,.9f,28.8f));
                    foreach (float z in new[] { -13.6f,-8f,0f,8f,13.6f })
                        masonry.Box(new Vector3(x-inward*.3f,-12.4f,z),new Vector3(.7f,24f,.7f));
                    for (int storey = 1; storey <= 4; storey++)
                    {
                        float y = -5.4f*storey;
                        masonry.Box(new Vector3(x-inward*.28f,y,0),new Vector3(.66f,.65f,28.8f));
                        // Shuttering ribs and form ties make the inside faces readable at player height.
                        foreach (float z in new[] { -11f,-6f,-1f,4f,9f })
                        {
                            masonry.Box(new Vector3(x-inward*.2f,y+2.45f,z),new Vector3(.24f,4.2f,2.5f));
                            for (int tie=0; tie<3; tie++)
                                metal.Box(new Vector3(x+inward*.015f,y+1.1f+tie*1.1f,z),new Vector3(.045f,.1f,.1f));
                        }
                    }
                    for (int i=0;i<26;i++)
                    {
                        float z=-13.2f+i*1.04f;
                        // Rebar protrudes horizontally below the lip, never into the drive lane.
                        Vector3 a=new Vector3(x-inward*.08f,-.35f,z), b=a+new Vector3(inward*.48f,-.11f,0);
                        bars.Tube(a,b,.018f);bars.Tube(b,b+new Vector3(inward*.12f,-.18f,0),.018f);
                    }
                    // Cable/conduit hangs down the inner wall, clamped to its concrete reveals.
                    foreach(float z in new[]{-10f,10f})
                    {
                        metal.Tube(new Vector3(x+inward*.11f,-.45f,z),new Vector3(x+inward*.11f,-17,z),.065f);
                        for(int i=1;i<=8;i++) metal.Box(new Vector3(x+inward*.08f,-i*2,z),new Vector3(.24f,.08f,.22f));
                    }
                }
                foreach(float z in new[]{-5.04f,5.04f})
                {
                    foreach(float side in new[]{-1f,1f})
                    {
                        float track=z+side*1.18f;
                        // Visible I-sections under each bridge: web and two flanges, seated on wall brackets.
                        metal.Box(new Vector3(middle,-.51f,track),new Vector3(span+.34f,.5f,.07f));
                        foreach(float y in new[]{-.28f,-.76f})metal.Box(new Vector3(middle,y,track),new Vector3(span+.34f,.055f,.3f));
                        foreach(float x in new[]{left,right})
                        {
                            float sign=x==left?1:-1;
                            metal.Box(new Vector3(x,-.88f,track),new Vector3(.8f,.19f,.65f));
                            metal.Tube(new Vector3(x,-4.9f,track),new Vector3(x+sign*2.4f,-.78f,track),.12f);
                            metal.Box(new Vector3(x,-4.92f,track),new Vector3(.26f,.45f,.45f));
                        }
                    }
                    // Cross-bracing visibly joins the paired girders.
                    for(float x=left+.7f;x<right;x+=1.7f)
                        metal.Box(new Vector3(x,-.66f,z),new Vector3(.12f,.17f,2.65f));
                }
                // Deep service floor and perimeter beams; still far below the fall trigger.
                masonry.Box(new Vector3(middle,-24.8f,0),new Vector3(span,.65f,28.8f));
                foreach(float z in new[]{-13.7f,13.7f})
                {
                    for(int floor=1;floor<=4;floor++)
                        masonry.Box(new Vector3(middle,-5.4f*floor,z),new Vector3(span,.65f,.65f));
                    // Bright identification on the shaft edge, not a new obstacle in the bridge.
                    for(int k=0;k<9;k++) warning.Box(new Vector3(middle-span*.4f+k*span*.1f,-.28f,z),new Vector3(span*.05f,.12f,.05f));
                }
                masonry.Save(root,"Shaft"+shaft+"_Concrete",concrete,true);
                metal.Save(root,"Shaft"+shaft+"_Steel",steel,true);
                bars.Save(root,"Shaft"+shaft+"_Rebar",rust,true);
                warning.Save(root,"Shaft"+shaft+"_Paint",paint,false);
            }
        }
        private static void FloorDetails(Transform root)
        {
            Mesh patch=BuildPatch();
            foreach(int sign in new[]{-1,1})
            {
                foreach(var p in new[]{new Vector3(-17.25f,.012f,sign*5.9f),new Vector3(-9.5f,.012f,sign*5.04f),
                    new Vector3(12.7f,.012f,sign*5.04f),new Vector3(2.45f,.012f,sign*1.4f)})
                {
                    var t=Group(root,"Rough concrete patch");t.position=p;t.rotation=Quaternion.Euler(0,sign*9,0);
                    t.gameObject.AddComponent<MeshFilter>().sharedMesh=patch;
                    t.gameObject.AddComponent<MeshRenderer>().sharedMaterial=Material("Exposed aggregate",new Color(.47f,.46f,.42f));
                    t.gameObject.AddComponent<CartRoughSurface>();
                    // Its backing belongs below the continuous slab. Wheel response samples the visible crowns.
                    t.gameObject.layer=LayerMask.NameToLayer("Ground");
                    var c=t.gameObject.AddComponent<BoxCollider>();c.center=new Vector3(0,-.16f,0);c.size=new Vector3(2.5f,.2f,2.24f);
                }
            }
            var cracks=new Kit();var flecks=new Kit();var random=new System.Random(699);
            foreach(Transform t in root)
            {
                for(int line=0;line<4;line++)
                {
                    float x=-.9f+line*.58f;Vector3 previous=Vector3.zero;
                    for(int j=0;j<12;j++)
                    {
                        float z=-.9f+j*.16f;float xx=x+(float)(random.NextDouble()-.5)*.16f;
                        var point=t.TransformPoint(new Vector3(xx,CartRoughSurface.Height(xx,z)+.004f,z));
                        if(j>0) cracks.Beam(previous,point,.018f,.006f);
                        previous=point;
                    }
                }
                for(int i=0;i<42;i++)
                {
                    float x=(float)(random.NextDouble()*2-1),z=(float)(random.NextDouble()*1.7-.85);
                    if(CartRoughSurface.Envelope(x,z)<.5f)continue;
                    var p=t.TransformPoint(new Vector3(x,CartRoughSurface.Height(x,z)+.003f,z));
                    flecks.Box(p,new Vector3(.025f+(float)random.NextDouble()*.04f,.008f,.027f));
                }
            }
            cracks.Save(root,"Concrete cracks",dark,false);flecks.Save(root,"Aggregate flecks",concrete,false);
        }
        private static Mesh BuildPatch()
        {
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int>();
            const int segments=64,rings=12;
            for(int ring=0;ring<=rings;ring++) for(int i=0;i<segments;i++)
            {
                float a=i*Mathf.PI*2/segments,scale=(float)ring/rings;
                float outline=1f+.06f*Mathf.Sin(a*7)+.035f*Mathf.Sin(a*13);
                float x=Mathf.Cos(a)*CartRoughSurface.HalfLength*scale*outline,z=Mathf.Sin(a)*CartRoughSurface.HalfWidth*scale*outline;
                vertices.Add(new Vector3(x,CartRoughSurface.Height(x,z),z));uv.Add(new Vector2(x,z));
                if(ring==0)continue;
                int n=ring*segments+i,next=ring*segments+(i+1)%segments;
                indices.AddRange(new[]{n,n-segments,next-segments,n,next-segments,next});
            }
            return SaveMesh("BrokenConcrete",vertices,uv,indices);
        }
        private static void SiteFittings(Transform root)
        {
            var metal=new Kit();var yellow=new Kit();var cement=new Kit();
            foreach(int side in new[]{-1,1})
            {
                // Rebar cutting station and a stack of shuttering are outside all three delivery corridors.
                Vector3 p=new Vector3(-25.3f,0,side*4.7f);
                for(int level=0;level<6;level++)
                {
                    cement.Box(p+new Vector3(0,.12f+level*.16f,0),new Vector3(2.8f,.13f,1.2f));
                    foreach(int s in new[]{-1,1})metal.Box(p+new Vector3(s*.9f,.12f+level*.16f,0),new Vector3(.065f,.14f,1.3f));
                }
                foreach(int s in new[]{-1,1})cement.Box(p+new Vector3(s*1f,.06f,0),new Vector3(.24f,.12f,1.3f));
                // Threaded shoring jacks, safely stored along the outer working wall.
                for(int i=0;i<4;i++)
                {
                    Vector3 q=new Vector3(25.9f,0,side*(1.8f+i*.63f));
                    metal.Box(q+Vector3.up*.055f,new Vector3(.42f,.11f,.42f));
                    metal.Tube(q+Vector3.up*.1f,q+Vector3.up*2.4f,.055f);
                    yellow.Tube(q+Vector3.up*.4f,q+Vector3.up*1.7f,.074f);
                    metal.Box(q+Vector3.up*2.4f,new Vector3(.38f,.1f,.32f));
                    metal.Tube(q+new Vector3(-.17f,1.72f,0),q+new Vector3(.17f,1.72f,0),.025f);
                }
                // A service line is clamped to the outer wall, with feet and elbow connection.
                Vector3 start=new Vector3(-9.1f,-.95f,side*14.15f),end=new Vector3(7.5f,-.95f,side*14.15f);
                metal.Tube(start,end,.12f);
                foreach(float x in new[]{-8f,-4,0,4,7})metal.Box(new Vector3(x,-.8f,side*14.15f),new Vector3(.09f,.45f,.38f));
            }
            cement.Save(root,"Stored formwork",concrete,true);metal.Save(root,"Shoring and services",steel,true);yellow.Save(root,"Jack collars",paint,true);
        }
        private static Material Material(string name,Color color,float metallic=0)
        {
            string path=Art+"/Materials/"+name.Replace(' ','_')+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
            m.SetColor("_BaseColor",color);m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",.16f);m.enableInstancing=true;
            if(metallic==0)m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/CarryItem/Original/Textures/CS_Concrete.png"));
            EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);return m;
        }
        private static Transform Group(Transform parent,string name){var t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
        private static Mesh SaveMesh(string name,List<Vector3> vertices,List<Vector2> uv,List<int> indices)
        {
            string path=Art+"/Meshes/"+name.Replace(' ','_')+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool fresh=mesh==null;
            if(fresh)mesh=new Mesh{name=name};else mesh.Clear();
            mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            if(fresh)AssetDatabase.CreateAsset(mesh,path);else{EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);}return mesh;
        }
        private sealed class Kit
        {
            private readonly List<Vector3> v=new List<Vector3>();private readonly List<Vector2> uv=new List<Vector2>();private readonly List<int> tri=new List<int>();
            private void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            {
                int n=v.Count;v.AddRange(new[]{a,b,c,d});float w=Vector3.Distance(a,b),h=Vector3.Distance(b,c);
                uv.AddRange(new[]{Vector2.zero,new Vector2(w,0),new Vector2(w,h),new Vector2(0,h)});tri.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
            }
            internal void Box(Vector3 p,Vector3 size)=>OrientedBox(p,size,Quaternion.identity);
            internal void ConnectedDeck(Bounds b,List<Bounds> neighbours)
            {
                var top=new[]{new Vector3(b.min.x,b.max.y,b.min.z),new Vector3(b.min.x,b.max.y,b.max.z),
                    new Vector3(b.max.x,b.max.y,b.max.z),new Vector3(b.max.x,b.max.y,b.min.z)};
                Vector3 down=Vector3.down*b.size.y;
                Quad(top[0],top[1],top[2],top[3]);Quad(top[3]+down,top[2]+down,top[1]+down,top[0]+down);
                for(int side=0;side<4;side++)
                {
                    Vector3 from=top[side],to=top[(side+1)%4];bool fixedX=side%2==0;
                    float constant=fixedX?from.x:from.z;
                    float min=fixedX?b.min.z:b.min.x,max=fixedX?b.max.z:b.max.x;
                    var gaps=new List<Vector2>();
                    foreach(var other in neighbours)
                    {
                        if(other==b||Mathf.Abs(other.max.y-b.max.y)>.001f)continue;
                        float touching=side==0?other.max.x:side==2?other.min.x:side==1?other.min.z:other.max.z;
                        if(Mathf.Abs(constant-touching)>.002f)continue;
                        float low=fixedX?other.min.z:other.min.x,high=fixedX?other.max.z:other.max.x;
                        if(high>min&&low<max)gaps.Add(new Vector2(Mathf.Max(min,low),Mathf.Min(max,high)));
                    }
                    if(!fixedX&&Mathf.Abs(Mathf.Abs(constant)-14.4f)<.002f)
                    foreach(float x in new[]{CarryRouteLayout.GalleryEntrance,CarryRouteLayout.GalleryExit})
                    {
                        float low=x-CarryRouteLayout.GalleryWidth*.5f,high=x+CarryRouteLayout.GalleryWidth*.5f;
                        if(high>min&&low<max)gaps.Add(new Vector2(Mathf.Max(min,low),Mathf.Min(max,high)));
                    }
                    gaps.Sort((a,c)=>a.x.CompareTo(c.x));float cursor=min;
                    foreach(var gap in gaps){if(gap.x>cursor)DeckWall(from,to,down,fixedX,cursor,gap.x);cursor=Mathf.Max(cursor,gap.y);}
                    if(cursor<max)DeckWall(from,to,down,fixedX,cursor,max);
                }
            }
            private void DeckWall(Vector3 from,Vector3 to,Vector3 down,bool fixedX,float low,float high)
            {
                bool ascending=(fixedX?to.z-from.z:to.x-from.x)>0;
                if(fixedX){from.z=ascending?low:high;to.z=ascending?high:low;}
                else{from.x=ascending?low:high;to.x=ascending?high:low;}
                Quad(from+down,to+down,to,from);
            }
            private void OrientedBox(Vector3 p,Vector3 size,Quaternion q)
            {
                Vector3 h=size*.5f;var a=new Vector3[8];for(int i=0;i<8;i++)a[i]=p+q*new Vector3((i&1)==0?-h.x:h.x,(i&2)==0?-h.y:h.y,(i&4)==0?-h.z:h.z);
                Quad(a[0],a[4],a[6],a[2]);Quad(a[1],a[3],a[7],a[5]);Quad(a[0],a[1],a[5],a[4]);
                Quad(a[2],a[6],a[7],a[3]);Quad(a[0],a[2],a[3],a[1]);Quad(a[4],a[5],a[7],a[6]);
            }
            internal void Beam(Vector3 a,Vector3 b,float width,float height)=>OrientedBox((a+b)*.5f,new Vector3(width,height,(b-a).magnitude),Quaternion.LookRotation(b-a));
            internal void Tube(Vector3 a,Vector3 b,float radius)
            {
                Quaternion q=Quaternion.LookRotation(b-a);const int sides=10;
                for(int i=0;i<sides;i++)
                {
                    float p=i*Mathf.PI*2/sides,n=(i+1)*Mathf.PI*2/sides;
                    Vector3 x=q*new Vector3(Mathf.Cos(p),Mathf.Sin(p),0)*radius,y=q*new Vector3(Mathf.Cos(n),Mathf.Sin(n),0)*radius;
                    Quad(a+x,a+y,b+y,b+x);Quad(a,a+y,a+x,a);Quad(b,b+x,b+y,b);
                }
            }
            internal void Save(Transform root,string name,Material mat,bool solid)
            {
                var mesh=SaveMesh(name,v,uv,tri);var t=Group(root,name);t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
                t.gameObject.AddComponent<MeshRenderer>().sharedMaterial=mat;
                if(solid){t.gameObject.layer=LayerMask.NameToLayer("Cover");t.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;}
            }
            internal void SaveCollision(Transform root,string name)
            {
                var t=Group(root,name);t.gameObject.layer=LayerMask.NameToLayer("Ground");
                t.gameObject.AddComponent<MeshCollider>().sharedMesh=SaveMesh(name,v,uv,tri);
            }
        }
    }
}
