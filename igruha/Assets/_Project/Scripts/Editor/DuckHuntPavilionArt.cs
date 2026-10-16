using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Traps;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    // Authored in metres. Functional islands have separate visual and collision children:
    // static mesh batching must never discard their physical cover.
    internal static partial class DuckHuntBarnArt
    {
        static readonly string[] PavilionNames = { "TICKETS & TREATS", "PRIZE EXCHANGE", "GAMES OF SKILL", "THE GRAND PRIZE", "FINAL CHALLENGE" };

        static Transform PavilionProp(Transform floor, string name, Vector3 p, out Transform visual)
        {
            var root = Group(floor, "Pavilion_" + name); root.position = p;
            visual = Group(root, "Crafted visuals"); return root;
        }

        static readonly System.Collections.Generic.Dictionary<Vector3,Mesh> JoineryMeshes = new System.Collections.Generic.Dictionary<Vector3,Mesh>();

        static GameObject Part(Transform g, string name, Vector3 p, Vector3 s, string mat)
        {
            if(!JoineryMeshes.TryGetValue(s,out var mesh))
            {
                var vertices=new System.Collections.Generic.List<Vector3>();var uv=new System.Collections.Generic.List<Vector2>();var triangles=new System.Collections.Generic.List<int>();
                Vector3 h=s*.5f;float bevel=Mathf.Min(.035f,Mathf.Min(h.x,Mathf.Min(h.y,h.z))*.24f);Vector3 inner=h-Vector3.one*bevel;
                Action<Vector3[]> face=points=>
                {
                    Vector3 center=Vector3.zero;foreach(var point in points)center+=point;
                    if(Vector3.Dot(Vector3.Cross(points[1]-points[0],points[2]-points[0]),center)<0)Array.Reverse(points);
                    int first=vertices.Count;
                    foreach(var point in points){vertices.Add(point);uv.Add(new Vector2(point.x+point.z*.27f,point.y+point.z));}
                    for(int j=1;j<points.Length-1;j++){triangles.Add(first);triangles.Add(first+j);triangles.Add(first+j+1);}
                };
                for(int axis=0;axis<3;axis++)foreach(int sign in new[]{-1,1})
                {
                    int a=(axis+1)%3,b=(axis+2)%3;var points=new Vector3[4];int j=0;
                    foreach(var pair in new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)})
                    {var q=Vector3.zero;q[axis]=sign*h[axis];q[a]=pair.x*inner[a];q[b]=pair.y*inner[b];points[j++]=q;}face(points);
                }
                for(int a=0;a<3;a++)for(int b=a+1;b<3;b++)foreach(int sa in new[]{-1,1})foreach(int sb in new[]{-1,1})
                {
                    int c=3-a-b;var p0=Vector3.zero;p0[a]=sa*h[a];p0[b]=sb*inner[b];p0[c]=-inner[c];var p1=p0;p1[c]=inner[c];
                    var p2=p1;p2[a]=sa*inner[a];p2[b]=sb*h[b];var p3=p2;p3[c]=-inner[c];face(new[]{p0,p1,p2,p3});
                }
                foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})
                    face(new[]{new Vector3(x*h.x,y*inner.y,z*inner.z),new Vector3(x*inner.x,y*h.y,z*inner.z),new Vector3(x*inner.x,y*inner.y,z*h.z)});
                mesh=new Mesh{name="Bevelled pavilion joinery"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();JoineryMeshes.Add(s,mesh);
            }
            var go=Group(g,name).gameObject;go.transform.position=g.position+p;go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=Mat(mat);return go;
        }

        static void Body(Transform root, Transform v, Vector3 p, Vector3 size, string color)
        {
            Part(v,"Solid cabinet",p+Vector3.up*size.y*.5f,size,"OakDark");
            Part(v,"Painted inset panels",p+new Vector3(0,size.y*.5f,-size.z*.5f-.015f),new Vector3(size.x-.16f,size.y-.20f,.035f),color);
            foreach(int s in new[]{-1,1})
            {
                Part(v,"Corner stile",p+new Vector3(s*(size.x*.5f-.06f),size.y*.5f,-size.z*.5f-.04f),new Vector3(.12f,size.y,.10f),"OakLight");
                Part(v,"Brass corner shoe",p+new Vector3(s*(size.x*.5f-.07f),.12f,-size.z*.5f-.10f),new Vector3(.14f,.18f,.025f),"Brass");
            }
            foreach(float h in new[]{.12f,size.y-.12f})
                Part(v,"Panel moulding",p+new Vector3(0,h,-size.z*.5f-.045f),new Vector3(size.x-.2f,.035f,.045f),"Brass");
            Part(v,"Overhanging hardwood counter",p+new Vector3(0,size.y+.055f,0),new Vector3(size.x+.18f,.11f,size.z+.17f),"OakLight");
            PropBox(root.gameObject,"Solid cover",p+Vector3.up*size.y*.5f,size);
            PropBox(root.gameObject,"Counter collision",p+new Vector3(0,size.y+.055f,0),new Vector3(size.x+.18f,.11f,size.z+.17f));
        }

        static GameObject Round(Transform g,string name,Vector3 p,Vector3 scale,string mat,Quaternion? rot=null)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);go.name=name;go.transform.SetParent(g,true);
            go.transform.position=g.position+p;go.transform.rotation=rot??Quaternion.identity;go.transform.localScale=scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=Mat(mat);return go;
        }

        static void Toy(Transform g,string key,Vector3 p,float height,int variation=0)
        {
            var toy=Model(key,g,g.position+p,Vector3.one);
            var rs=toy.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
            toy.transform.localScale*=height/Mathf.Max(.01f,b.size.y);
            b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
            toy.transform.position+=g.position+p-new Vector3(b.center.x,b.min.y,b.center.z);
            if(key!="Duck")Retint(toy,"Rose",variation%2==0?"Blue":"Purple");
        }

        static void Sign(Transform v,string text,Vector3 p,float w,float h,string color)
        {
            Part(v,"Framed sign",p,new Vector3(w,h,.12f),"Brass");
            Part(v,"Enamel sign",p+Vector3.back*.075f,new Vector3(w-.10f,h-.10f,.05f),color);
            Label(v,text,v.position+p+Vector3.back*.11f,Mathf.Min(h*.60f,w/Mathf.Max(1,text.Length)*1.4f),"Cream");
        }

        static void Canopy(Transform v,float width,float depth,float y,string color)
        {
            foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
                Part(v,"Canopy post",new Vector3(x*(width*.5f-.10f),y*.5f,z*(depth*.5f-.12f)),new Vector3(.085f,y,.085f),"Brass");
            int n=Mathf.CeilToInt(width/.4f);
            for(int i=0;i<n;i++)
            {
                float x=-width*.5f+(i+.5f)*width/n;
                foreach(int side in new[]{-1,1})
                {
                    var panel=Part(v,"Striped canopy",new Vector3(x,y+.12f,side*depth*.25f),new Vector3(width/n+.008f,.085f,depth*.56f),i%2==0?color:"Cream");
                    panel.transform.rotation=Quaternion.Euler(side*13,0,0);
                }
                Part(v,"Scalloped valance",new Vector3(x,y-.05f,-depth*.5f),new Vector3(width/n-.02f,.24f,.065f),i%2==0?color:"Cream");
            }
            BulbLine(v,v.position+new Vector3(-width*.5f,y-.15f,-depth*.5f-.06f),v.position+new Vector3(width*.5f,y-.15f,-depth*.5f-.06f),.38f);
        }

        static void BulbLine(Transform v,Vector3 a,Vector3 b,float spacing=.45f)
        {
            Beam(v,a,b,.025f,"Iron");int n=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/spacing));
            for(int i=0;i<=n;i++)Model("Bulb",v,Vector3.Lerp(a,b,(float)i/n),Vector3.one*.58f);
        }

        static void Wheels(Transform v,float width,float depth)
        {
            foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
            {
                var p=new Vector3(x*(width*.5f+.08f),.38f,z*depth*.34f);
                Round(v,"Wheel iron tyre",p,new Vector3(.76f,.06f,.76f),"Iron",Quaternion.Euler(0,0,90));
                Round(v,"Wheel painted hub",p+Vector3.right*x*.065f,new Vector3(.62f,.025f,.62f),"Brass",Quaternion.Euler(0,0,90));
                Round(v,"Axle cap",p+Vector3.right*x*.10f,new Vector3(.16f,.035f,.16f),"OakDark",Quaternion.Euler(0,0,90));
                for(int i=0;i<8;i++)
                {
                    float a=i*Mathf.PI/4;
                    Beam(v,v.position+p+Vector3.right*x*.095f,v.position+p+Vector3.right*x*.095f+new Vector3(0,Mathf.Sin(a)*.29f,Mathf.Cos(a)*.29f),.035f,"OakDark");
                }
            }
        }

        static void Parcel(Transform v,Vector3 p,Vector3 size)
        {
            Part(v,"Wrapped stock",p+Vector3.up*size.y*.5f,size,"Cream");
            Part(v,"Parcel ribbon",p+Vector3.up*(size.y+.01f),new Vector3(size.x+.015f,.025f,.055f),"Red");
            Part(v,"Parcel tie",p+new Vector3(0,size.y*.5f,-size.z*.5f-.01f),new Vector3(.045f,size.y,.025f),"Red");
        }

        static void Shelves(Transform floor,Transform decor,int index,float x,bool toys)
        {
            // Mounted against the rear wall, clear of the protected doorway and hatch.
            float y=index*5.76f;var root=PavilionProp(floor,"WallDisplay_"+x,new Vector3(x,y,9.42f),out var v);
            Body(root,v,Vector3.zero,new Vector3(2.70f,.82f,.68f),FloorColors[index]);
            Part(v,"Cabinet back",new Vector3(0,2.15f,.30f),new Vector3(2.70f,2.65f,.12f),"OakDark");
            PropBox(root.gameObject,"Cabinet back",new Vector3(0,2.15f,.30f),new Vector3(2.70f,2.65f,.12f));
            foreach(int s in new[]{-1,1})Part(v,"Bookcase side",new Vector3(s*1.30f,2.13f,0),new Vector3(.12f,2.70f,.70f),FloorColors[index]);
            for(int row=0;row<3;row++)
            {
                float h=.95f+row*.83f;Part(v,"Deep shelf",new Vector3(0,h,0),new Vector3(2.70f,.09f,.74f),"OakLight");
                for(int j=0;j<4;j++)
                    if(toys)Toy(v,(j+row)%3==0?"Rabbit":(j+row)%3==1?"Bear":"Duck",new Vector3(-.96f+j*.64f,h+.05f,-.03f),.57f,j+row);
                    else Parcel(v,new Vector3(-.94f+j*.63f,h+.05f,0),new Vector3(.44f,.32f+(j%2)*.13f,.43f));
            }
            Sign(v,toys?"PRIZES":"SUPPLIES",new Vector3(0,3.66f,-.12f),2.9f,.42f,FloorColors[index]);
            Combine(v,"PavilionShelf_"+index+"_"+Mathf.RoundToInt(x*10));
            if(index==4)
            {
                // On the finale these become shallow wall-mounted exhibits above head height.
                // Floor-standing cabinets would intersect the start ramp and provide bypass steps.
                root.localScale=new Vector3(.85f,.48f,.48f);
                root.position=new Vector3(x,y+4.1f,9.85f);
            }
        }

        static void ServiceCounter(Transform floor,int index,string key,Vector3 p,float width,bool highEnd,bool toys)
        {
            var root=PavilionProp(floor,key,p,out var v);string color=FloorColors[index];
            Body(root,v,Vector3.zero,new Vector3(width,1.15f,1.25f),color);
            Body(root,v,new Vector3(width*.5f-.4f,0,1.15f),new Vector3(.8f,1.15f,1.1f),color);
            if(highEnd)
            {
                var q=new Vector3(-width*.5f+.12f,1.08f,.15f);
                Part(v,"Service privacy wing",q,new Vector3(.18f,2.16f,1.4f),color);
                PropBox(root.gameObject,"Standing cover wing",q,new Vector3(.18f,2.16f,1.4f));
            }
            Sign(v,key,new Vector3(0,.64f,-.72f),width-.45f,.45f,color);
            Part(v,"Cash register base",new Vector3(-width*.28f,1.34f,.10f),new Vector3(.43f,.27f,.36f),"Brass");
            Part(v,"Cash register keys",new Vector3(-width*.28f,1.49f,-.04f),new Vector3(.34f,.04f,.17f),"Iron");
            for(int i=0;i<3;i++)
                if(toys)Toy(v,i==1?"Rabbit":"Duck",new Vector3(i*.58f-.35f,1.21f,.12f),.46f,i);
                else Parcel(v,new Vector3(i*.51f-.3f,1.21f,.2f),new Vector3(.36f,.24f,.4f));
            Model("Stool",v,p+new Vector3(-.45f,0,1.38f),Vector3.one);
            // Stool is tucked into the staff area and has its own simple physical body.
            PropBox(root.gameObject,"Staff stool",new Vector3(-.45f,.35f,1.38f),new Vector3(.4f,.7f,.4f));
            Combine(v,"PavilionCounter_"+index+"_"+key.Replace(" ",""));
        }

        static void TicketBooth(Transform floor)
        {
            var root=PavilionProp(floor,"TicketOffice",new Vector3(8.65f,0,8.65f),out var v);
            Body(root,v,Vector3.zero,new Vector3(2.6f,1.2f,1.4f),"Red");
            foreach(int s in new[]{-1,1})
            {
                Part(v,"Booth side",new Vector3(s*1.23f,1.4f,.1f),new Vector3(.16f,2.8f,1.5f),"Red");
                PropBox(root.gameObject,"Booth side cover",new Vector3(s*1.23f,1.4f,.1f),new Vector3(.16f,2.8f,1.5f));
            }
            Canopy(v,2.95f,1.8f,2.85f,"Red");Sign(v,"TICKETS",new Vector3(0,2.55f,-.90f),2.5f,.56f,"Red");
            for(int i=0;i<4;i++)Round(v,"Ticket roll",new Vector3(-.75f+i*.30f,1.4f,0),new Vector3(.22f,.12f,.22f),"Cream",Quaternion.Euler(90,0,0));
            Combine(v,"PavilionTicketOffice");
        }

        static void PopcornIsland(Transform floor)
        {
            var root=PavilionProp(floor,"PopcornIsland",new Vector3(13.5f,0,4.6f),out var v);
            Body(root,v,new Vector3(0,.40f,0),new Vector3(2.5f,.9f,1.4f),"Red");Wheels(v,2.5f,1.4f);
            Canopy(v,2.95f,1.85f,2.85f,"Red");
            Sign(v,"FRESH POPCORN",new Vector3(0,.85f,-.80f),2.1f,.45f,"Red");
            Part(v,"Popcorn pan",new Vector3(0,1.40f,0),new Vector3(1.65f,.15f,.9f),"Brass");
            for(int i=0;i<120;i++)
            {
                var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name="Popcorn";go.transform.SetParent(v,true);
                go.transform.position=v.position+new Vector3((i%12-5.5f)*.125f,1.50f+(i%3)*.018f,(i/12-4.5f)*.073f);
                go.transform.localScale=new Vector3(.075f,.085f,.07f);Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=Mat("Cream");
            }
            for(int i=0;i<4;i++)Round(v,"Paper cup",new Vector3(.8f,1.52f,-.50f+i*.22f),new Vector3(.15f,.16f,.15f),i%2==0?"Red":"Cream");
            Parcel(v,new Vector3(-1.65f,0,.75f),new Vector3(.5f,.55f,.5f));
            PropBox(root.gameObject,"Stock crate",new Vector3(-1.65f,.275f,.75f),new Vector3(.5f,.55f,.5f));
            Combine(v,"PavilionPopcornIsland");
        }

        static void TokenIsland(Transform floor)
        {
            var root=PavilionProp(floor,"TokenExchange",new Vector3(21,0,6.3f),out var v);
            Body(root,v,Vector3.zero,new Vector3(3.1f,1.18f,1.7f),"Red");
            Part(v,"Central menu back",new Vector3(0,1.9f,.2f),new Vector3(1.15f,1.4f,.18f),"OakDark");
            PropBox(root.gameObject,"Central standing cover",new Vector3(0,1.9f,.2f),new Vector3(1.15f,1.4f,.18f));
            Canopy(v,3.5f,2.05f,3.1f,"Red");Sign(v,"TOKENS",new Vector3(0,2.72f,-1.1f),2.4f,.48f,"Red");
            Sign(v,"PLAY\nWIN\nSMILE",new Vector3(0,1.95f,.08f),.96f,1.15f,"Ink");
            for(int i=0;i<6;i++)Round(v,"Token stack",new Vector3(-1+i*.17f,1.34f,-.32f),new Vector3(.13f,.035f+(i%3)*.025f,.13f),"Brass");
            Combine(v,"PavilionTokenIsland");
        }

        static void PrizeWagon(Transform floor,int index,Vector3 p,bool grand)
        {
            float w=grand?3.6f:2.25f,d=grand?1.65f:1.3f;var root=PavilionProp(floor,grand?"GrandPrizeWagon":"PrizeTrolley",p,out var v);
            Body(root,v,new Vector3(0,.4f,0),new Vector3(w,.85f,d),FloorColors[index]);Wheels(v,w,d);
            Part(v,"Solid tall wagon back",new Vector3(0,1.5f,d*.5f-.05f),new Vector3(w,2.05f,.14f),FloorColors[index]);
            PropBox(root.gameObject,"Standing cover back",new Vector3(0,1.5f,d*.5f-.05f),new Vector3(w,2.05f,.14f));
            foreach(int s in new[]{-1,1})Part(v,"Wagon handle",new Vector3(s*(w*.5f+.08f),1.05f,d*.5f+.5f),new Vector3(.075f,.075f,1.0f),"Brass");
            if(grand)
            {
                Canopy(v,w+.4f,d+.3f,3.05f,"Red");Toy(v,"Duck",new Vector3(0,1.31f,0),1.1f);
                Toy(v,"Bear",new Vector3(-1.2f,1.31f,0),.66f);Toy(v,"Rabbit",new Vector3(1.2f,1.31f,0),.73f);
            }
            else for(int i=0;i<4;i++)Toy(v,i%2==0?"Rabbit":"Bear",new Vector3(-.8f+i*.53f,1.31f,0),.6f,i);
            Sign(v,grand?"GRAND PRIZES":"PRIZE DELIVERY",new Vector3(0,.86f,-d*.5f-.09f),w-.35f,.45f,FloorColors[index]);
            Combine(v,"PavilionWagon_"+index+"_"+Mathf.RoundToInt(p.x*10));
        }

        static void SkillGames(Transform floor)
        {
            var p=new Vector3(11.6f,11.52f,5.4f);var root=PavilionProp(floor,"RingToss",p,out var v);
            Body(root,v,Vector3.zero,new Vector3(3.2f,1.18f,1.65f),"Purple");
            Sign(v,"RING TOSS",new Vector3(0,.65f,-.92f),2.6f,.48f,"Purple");
            for(int x=-2;x<=2;x++)for(int z=0;z<2;z++)
            {
                Round(v,"Bottle body",new Vector3(x*.45f,1.40f,z*.4f-.1f),new Vector3(.17f,.17f,.17f),z==0?"Teal":"Gold");
                Round(v,"Bottle neck",new Vector3(x*.45f,1.62f,z*.4f-.1f),new Vector3(.075f,.08f,.075f),"Brass");
            }
            for(int i=0;i<3;i++)Ring(v,new Vector3(-1.18f,1.30f+i*.035f,-.45f),.19f,.025f,"Brass",Quaternion.Euler(90,0,0));
            Combine(v,"PavilionRingToss");
            ServiceCounter(floor,2,"HIT THE DUCKS",new Vector3(17.7f,11.52f,8.0f),3.8f,true,true);
        }

        static void Ring(Transform v,Vector3 p,float radius,float thickness,string mat,Quaternion rot)
        {
            for(int i=0;i<24;i++)
            {
                float a=i*Mathf.PI/12,b=(i+1)*Mathf.PI/12;
                Beam(v,v.position+p+rot*new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,0),v.position+p+rot*new Vector3(Mathf.Cos(b)*radius,Mathf.Sin(b)*radius,0),thickness,mat);
            }
        }

        static void PavilionFloor(Transform floor,int index)
        {
            var decor=Group(floor,"PavilionArchitecture");float y=index*5.76f;
            if(index==0){TicketBooth(floor);PopcornIsland(floor);TokenIsland(floor);ServiceCounter(floor,0,"GAME EQUIPMENT",new Vector3(26,0,8.2f),2.7f,true,false);DressStartRoom(floor,decor);}
            if(index==1){ServiceCounter(floor,1,"PRIZE EXCHANGE",new Vector3(25,5.76f,6.5f),3.0f,true,true);ServiceCounter(floor,1,"WRAP & COLLECT",new Vector3(19.7f,5.76f,5.8f),2.8f,false,false);PrizeWagon(floor,1,new Vector3(14.4f,5.76f,4.5f),false);}
            if(index==2)SkillGames(floor);
            if(index==3)
            {
                PrizeWagon(floor,3,new Vector3(19.4f,17.28f,4.8f),true);
                PrizeScreen(floor,new Vector3(25.2f,17.28f,5.6f),"WINNERS");
                ServiceCounter(floor,3,"PRIZE PICKUP",new Vector3(13.4f,17.28f,5.2f),2.6f,true,true);
            }
            float[] displays=index==0?new[]{12f,16f,23f}:index==1?new[]{13.7f,17f,20.3f,23.6f}:index==2?new[]{10.5f,13.8f,17.5f}:index==3?new[]{13.4f,19.4f,24.2f}:new[]{9f,13f,23.5f,27f};
            foreach(float x in displays)Shelves(floor,decor,index,x,index!=0);
            // Recessed panelling, signs and light rhythm tie every functional zone together.
            if(index<4)Sign(decor,PavilionNames[index],new Vector3(17.28f,y+4.3f,9.72f),6f,.75f,FloorColors[index]);
            for(int bay=0;bay<6;bay++)
            {
                float x=(bay+.5f)*5.76f;
                float ceiling=index==4?6.15f:4.92f;
                Garland(decor,new Vector3(bay*5.76f,y+ceiling-.05f,.16f),new Vector3((bay+1)*5.76f,y+ceiling-.05f,.16f),.65f);
                BulbLine(decor,new Vector3(bay*5.76f,y-.22f,-.19f),new Vector3((bay+1)*5.76f,y-.22f,-.19f));
                Model("Lantern",decor,new Vector3(x,y+3.9f,9.22f),Vector3.one*.85f);
                // End bays are stairwells: a cross-beam here cuts through the upper flight.
                if(bay>0&&bay<5)Beam(decor,new Vector3(x,y+ceiling,.16f),new Vector3(x,y+ceiling,9.95f),.22f,"OakDark");
            }
            // Numbers belong to the hunter's left facade on every storey.
            FloorPlaque(decor,index,y);
            foreach(float x in new[]{.9f,4.8f,30.0f,33.5f})Model("Lantern",decor,new Vector3(x,y+3.4f,-.40f),Vector3.one*.7f);
            float pendantY=y+(index==4?5.8f:4.55f);
            Model("Lantern",decor,new Vector3(17.28f,pendantY,5.5f),Vector3.one*.9f);
            Beam(decor,new Vector3(17.28f,pendantY+.4f,5.5f),new Vector3(17.28f,y+(index==4?6.25f:4.98f),5.5f),.035f,"Iron");
            if(index==4)PavilionFinal(floor,decor);
            foreach(var lever in floor.GetComponentsInChildren<TrapLever>())CarnivalControl(lever,index);
            Combine(decor,"PavilionArchitecture_"+index);
        }

        static void FloorPlaque(Transform decor,int index,float y)
        {
            Vector3 centre=new Vector3(2.88f,y+2.88f,-.20f);
            Part(decor,"Floor plaque frame",centre,new Vector3(2.7f,2.6f,.12f),"Brass");
            Part(decor,"Floor plaque enamel",centre+Vector3.back*.075f,new Vector3(2.56f,2.46f,.06f),FloorColors[index]);
            CentredPlaqueText(decor,(index+1).ToString("00"),centre+new Vector3(0,.24f,-.13f),1.45f);
            CentredPlaqueText(decor,"FLOOR",centre+new Vector3(0,-.86f,-.13f),.25f);
        }

        static void CentredPlaqueText(Transform parent,string value,Vector3 centre,float size)
        {
            Label(parent,value,centre,size,"Cream");
            var text=parent.GetChild(parent.childCount-1).GetComponent<TextMesh>();
            var bounds=text.GetComponent<Renderer>().bounds;
            text.transform.position+=new Vector3(centre.x-bounds.center.x,centre.y-bounds.center.y,0);
        }

        static void PavilionFinal(Transform floor,Transform decor)
        {
            var pads=floor.Cast<Transform>().Where(t=>t.name.StartsWith("ParkourPad_")||t.name=="ParkourSplitDeck"||t.name=="RaisedFinishDeck").ToArray();
            foreach(var pad in pads)
            {
                var b=pad.GetComponent<Collider>().bounds;float ground=23.04f;
                string name=pad.name=="ParkourSplitDeck"?"START":pad.name=="RaisedFinishDeck"?"FINISH":pad.name.Substring(11);
                Solid(decor,"Painted platform fascia",new Vector3(b.center.x,b.max.y-.25f,b.min.z-.02f),new Vector3(b.size.x,.48f,.10f),"Red");
                Label(decor,name,new Vector3(b.center.x,b.max.y-.29f,b.min.z-.085f),.40f,"Cream");
                foreach(int s in new[]{-1,1})
                {
                    float z=b.center.z+s*(b.extents.z-.20f);
                    Vector3 a=new Vector3(b.min.x+.25f,ground,z),c=new Vector3(b.max.x-.25f,ground,z);
                    float h=b.min.y-ground;
                    Solid(decor,"Trestle foot",a+Vector3.up*h*.5f,new Vector3(.25f,h,.25f),"OakDark");
                    Solid(decor,"Trestle foot",c+Vector3.up*h*.5f,new Vector3(.25f,h,.25f),"OakDark");
                    Beam(decor,a,c+Vector3.up*h,.14f,"Red");Beam(decor,c,a+Vector3.up*h,.14f,"Red");
                    BulbLine(decor,new Vector3(b.min.x+.08f,b.max.y-.12f,z+s*.22f),new Vector3(b.max.x-.08f,b.max.y-.12f,z+s*.22f),.42f);
                }
                foreach(int s in new[]{-1,1})BulbLine(decor,new Vector3(b.center.x+s*(b.extents.x+.03f),b.max.y-.13f,b.min.z+.15f),new Vector3(b.center.x+s*(b.extents.x+.03f),b.max.y-.13f,b.max.z-.15f),.42f);
            }
            foreach(var swing in floor.GetComponentsInChildren<PendulumSwing>())
            {
                var so=new SerializedObject(swing);var pivot=(Transform)so.FindProperty("pivot").objectReferenceValue;
                Beam(decor,new Vector3(pivot.position.x,pivot.position.y,0),new Vector3(pivot.position.x,pivot.position.y,10.08f),.27f,"OakDark");
                var hub=Group(decor,"Pendulum ceiling bearing");hub.position=pivot.position;
                Round(hub,"Brass axle",Vector3.zero,new Vector3(.42f,.25f,.42f),"Brass",Quaternion.Euler(0,0,90));
                // Rod follows the existing synchronized bob rotation and ends exactly at its pivot.
                swing.GetComponent<Renderer>().sharedMaterial=Mat("Red");
                var visual=Group(swing.transform,"Candy details");visual.localPosition=Vector3.zero;
                Ring(visual,Vector3.zero,.53f,.09f,"Cream",Quaternion.identity);
                Ring(visual,Vector3.zero,.53f,.09f,"Brass",Quaternion.Euler(0,90,0));
            }
            var trophy=Group(decor,"Finish trophies");trophy.position=new Vector3(32.65f,25.23f,9.47f);
            Toy(trophy,"Duck",Vector3.zero,.90f);Toy(trophy,"Rabbit",new Vector3(1,0,0),.60f);Toy(trophy,"Bear",new Vector3(-1,0,0),.60f);
            Marquee(decor,"GRAND PRIZE",new Vector3(18.0f,28.15f,9.65f),new Vector2(6.4f,1.1f),.65f);
        }
    }
}
