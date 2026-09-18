using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Igruha.EditorTools
{
    internal static partial class DuckHuntBarnArt
    {
        const string FairgroundRoot="Assets/_Project/Art/DuckHuntFairground";
        static readonly string[] FairgroundModels={"PrizeCart","WheelStand","PrizeDisplay","TargetPanel","ServiceRack","Swag","Bench","QueuePost","Barrel","Tent","Carousel","Grass","PaintedBoard"};
        static void FairgroundPreflight()
        {
            foreach(string key in FairgroundModels)
            {
                string p=FairgroundRoot+"/Models/DHF_"+key+".fbx";
                EnsureReadableModel(p);
                if(AssetDatabase.LoadAssetAtPath<GameObject>(p)==null)throw new InvalidOperationException("Export duck_hunt_fairground.py: "+key);
            }
        }

        static void FairgroundFloorDressing(Transform floor,Transform art,int index)
        {
            float y=index*5.76f;
            // Shallow rear relief remains behind the circulation strip. None is a cover.
            float[] bays=index%2==0?new[]{10.2f,15.3f,20.6f,25.8f}:new[]{8.6f,13.7f,19f,24.2f};
            for(int i=0;i<bays.Length;i++)
            {
                if(index==4 && (i==1 || i==2))continue; // Clear the GRAND PRIZE lettering.
                string module=(i+index)%3==0?"PrizeDisplay":((i+index)%3==1?"TargetPanel":"ServiceRack");
                Model(module,art,new Vector3(bays[i],y+.12f,9.73f),new Vector3(.99f,1.08f,.65f));
                Model("Swag",art,new Vector3(bays[i],y+4.98f,9.20f),new Vector3(.72f,.70f,1));
                Model("Lantern",art,new Vector3(bays[i]+2.20f,y+3.75f,9.03f),Vector3.one*.68f);
                if(i==0 || i==2)PointLight(art,new Vector3(bays[i],y+3.85f,7.7f),new Color(1,.70f,.37f),3.2f,8.5f);
            }
            for(int x=0;x<=48;x+=2)
                Model("Bulb",art,new Vector3(x*.72f,y-.18f,-.32f),Vector3.one*.70f);
            for(int j=0;j<5;j++)
                if(index!=4 || j!=2)
                Model("Swag",art,new Vector3(3.46f+j*6.91f,y+5.22f,.12f),new Vector3(1.12f,.68f,1));
            if(index==0)DressStartRoom(floor,art);
            if(index==4)DressFinalStage(floor,art);
        }

        static void DressStartRoom(Transform floor,Transform art)
        {
            // Interior horizontal detail doesn't change the eight spawn capsules.
            for(int i=0;i<8;i++)
            {
                float z=Mathf.Lerp(1.08f,9f,i/7f);
                Model("Lantern",art,new Vector3(.23f,2.6f,z),Vector3.one*.50f,Quaternion.Euler(0,90,0));
                var footprint=Solid(art,"Start arrow",new Vector3(1.55f,.012f,z),new Vector3(.50f,.013f,.09f),"Brass");
                foreach(int s in new[]{-1,1})Beam(art,new Vector3(1.63f,.023f,z+s*.14f),new Vector3(1.83f,.023f,z),.025f,"Brass");
            }
            PointLight(art,new Vector3(3,3.6f,4.7f),new Color(1,.74f,.43f),4,9);
            for(int i=0;i<4;i++)
            {
                Model("Wheel",art,new Vector3(.65f+i*1.35f,1.5f,-.20f),Vector3.one*.35f);
                Model("Duck",art,new Vector3(.65f+i*1.35f,1.35f,-.42f),Vector3.one*.30f);
            }
        }

        static void DressFinalStage(Transform floor,Transform art)
        {
            Label(art,"05",new Vector3(2.9f,26.5f,-.16f),1.7f,"Cream");
            foreach(Transform pad in floor.Cast<Transform>().Where(t=>t.name.StartsWith("ParkourPad_")))
            {
                int number=int.Parse(pad.name.Substring("ParkourPad_".Length));
                Bounds b=pad.GetComponent<Renderer>().bounds;Vector3 centre=b.center;
                string color=number%3==0?"Blue":number%3==1?"Red":"Teal";
                // Square landing top and every jump gap stay exactly as authored.
                for(int side=-1;side<=1;side+=2)
                {
                    Solid(art,"Painted podium fascia",new Vector3(centre.x,b.max.y-.30f,centre.z+side*(b.extents.z-.03f)),new Vector3(b.size.x,.48f,.09f),color);
                    Solid(art,"Podium side",new Vector3(centre.x+side*(b.extents.x-.03f),b.max.y-.30f,centre.z),new Vector3(.09f,.48f,b.size.z),"OakLight");
                }
                for(int j=0;j<6;j++)Model("Bulb",art,new Vector3(b.min.x+.15f+j*(b.size.x-.30f)/5,b.max.y-.19f,b.min.z-.015f),Vector3.one*.48f);
                Model(number%2==0?"Wheel":"Duck",art,new Vector3(centre.x,b.max.y-.55f,b.min.z-.06f),Vector3.one*.28f);
                // Timber trestles sit below the platform; don't provide footholds.
                foreach(int side in new[]{-1,1})
                {
                    Vector3 a=new Vector3(centre.x+side*.78f,23.12f,centre.z);
                    Beam(art,a,new Vector3(centre.x-side*.78f,b.min.y-.12f,centre.z),.11f,"OakDark");
                }
            }
            // Prize alcove behind the finish, with an unobstructed landing apron.
            Vector3 p=new Vector3(32.55f,25.20f,9.55f);
            Model("Duck",art,p,Vector3.one*1.35f);
            Model("Bear",art,p+new Vector3(-1,0,0),Vector3.one*.75f);
            Model("Rabbit",art,p+new Vector3(1,0,0),Vector3.one*.68f);
            Model("Swag",art,p+new Vector3(0,2.15f,.1f),new Vector3(.53f,1.5f,1));
        }

        static void Rope(Transform art,Vector3 a,Vector3 b,float sag,string color="Red")
        {
            Vector3 last=a;
            for(int i=1;i<=10;i++)
            {
                float t=i/10f;Vector3 next=Vector3.Lerp(a,b,t)-Vector3.up*(Mathf.Sin(t*Mathf.PI)*sag);
                Beam(art,last,next,.032f,color);last=next;
            }
        }

        static void Queue(Transform art,Vector3 start,int count,Vector3 direction)
        {
            for(int i=0;i<count;i++)
            {
                Vector3 p=start+direction*i;Model("QueuePost",art,p,Vector3.one);
                if(i>0)Rope(art,p+Vector3.up, p-direction+Vector3.up,.23f);
            }
        }

        static void FairgroundSurroundings(Transform art)
        {
            // All medium/tall scenery is outside the convex firing sector:
            // tower x 0..34.56 z >=0, hunter x17.28 z=-11.52.
            Transform near=Group(art,"Foreground midway");
            for(int x=0;x<12;x++)for(int z=0;z<3;z++)
                Model("Deck",near,new Vector3(-4+x*3.5f,-2.91f,-23-z*3.4f),new Vector3(3.5f,1,3.4f));
            Queue(near,new Vector3(0,-3,-23),7,new Vector3(2.3f,0,0));
            Queue(near,new Vector3(22,-3,-23),6,new Vector3(2.3f,0,0));
            Queue(near,new Vector3(10,-3,-28),6,new Vector3(2.3f,0,0));
            Model("Booth",near,new Vector3(7,-3,-29),Vector3.one*1.7f,Quaternion.Euler(0,-8,0));
            Model("PrizeDisplay",near,new Vector3(34,-3,-31),new Vector3(.8f,.7f,.9f));
            Model("TicketStand",near,new Vector3(30,-3,-28.9f),Vector3.one*1.5f);
            foreach(float x in new[]{-1f,20f,35f})
            {
                Model("Bench",near,new Vector3(x,-3,-31),Vector3.one*1.2f);
                Model("Barrel",near,new Vector3(x+1.5f,-3,-31),Vector3.one*.85f);
            }
            // Two side neighbourhoods; keep the 35 m facade dominant.
            foreach(int side in new[]{-1,1})
            {
                float x=side<0?-13:47;
                Model("Tent",art,new Vector3(x,-3,0),Vector3.one*1.2f,Quaternion.Euler(0,side*20,0));
                Model("Tent",art,new Vector3(x+side*8,-3,19),Vector3.one*.94f);
                Model("Booth",art,new Vector3(x+side*3,-3,-12),Vector3.one*2.0f,Quaternion.Euler(0,side*18,0));
                Model("PopcornCart",art,new Vector3(x-side*2,-3,-9),Vector3.one*1.4f);
                Model("Barrel",art,new Vector3(x-side*4,-3,5),Vector3.one);
                Model("CrateCover",art,new Vector3(x+side*4,-3,11),Vector3.one*.8f);
                for(int i=0;i<4;i++)
                {
                    Vector3 p=new Vector3(x+side*3,-3,-18+i*10);
                    Beam(art,p,p+Vector3.up*5,.10f,"OakDark");Model("Lantern",art,p+Vector3.up*4.3f,Vector3.one*.85f);
                    // Side strings used to pass through nearby tent roofs.
                    // Lantern posts keep the rhythm without impossible rigging.
                }
            }
            Model("Carousel",art,new Vector3(-22,-3,-13),Vector3.one*1.7f);
            // Rear skyline: taller rides seen around and above the main building.
            BuildScenicWheel(art,new Vector3(-20,12,30),13);
            for(int i=0;i<5;i++)Model("Tent",art,new Vector3(-25+i*19,-3,42),Vector3.one*(1.2f+i%2*.35f));
            for(int i=0;i<2;i++)
            {
                float x=i==0?-38:62;
                for(int j=0;j<7;j++)
                {
                    float y=-3+j*4;
                    foreach(int side in new[]{-1,1})Beam(art,new Vector3(x+side*1.0f,y,35),new Vector3(x+side*1.0f,y+4,35),.16f,"Blue");
                    Beam(art,new Vector3(x-1,y,35),new Vector3(x+1,y+4,35),.10f,"Cream");
                    Beam(art,new Vector3(x+1,y,35),new Vector3(x-1,y+4,35),.10f,"Cream");
                }
                Model("Booth",art,new Vector3(x,24,35),Vector3.one*.85f);
            }
            var random=new System.Random(1819);
            for(int i=0;i<90;i++)
            {
                float x=-30+(float)random.NextDouble()*95,z=-40+(float)random.NextDouble()*85;
                if(x>-2&&x<37&&z>-21&&z<12)continue;
                Model("Grass",art,new Vector3(x,-3.01f,z),Vector3.one*(.9f+(float)random.NextDouble()*1.3f));
            }
            // Low-poly ridges soften the tree line without adding heavy terrain data.
            for(int i=0;i<10;i++)
            {
                var hill=GameObject.CreatePrimitive(PrimitiveType.Sphere);hill.name="Distant wooded hill";hill.transform.SetParent(art,false);
                hill.transform.position=new Vector3(-100+i*25,-12,88+i%3*14);hill.transform.localScale=new Vector3(50,27+i%4*8,48);
                Object.DestroyImmediate(hill.GetComponent<Collider>());hill.GetComponent<Renderer>().sharedMaterial=Mat(i%2==0?"Leaf":"LeafLight");
            }
        }

        static void BuildScenicWheel(Transform art,Vector3 centre,float radius)
        {
            for(int i=0;i<32;i++)
            {
                float a=i*Mathf.PI/16,b=(i+1)*Mathf.PI/16;
                Vector3 v=centre+new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,0),w=centre+new Vector3(Mathf.Cos(b)*radius,Mathf.Sin(b)*radius,0);
                Beam(art,v,w,.17f,"Cream");Model("Bulb",art,v,Vector3.one);
                if(i%4==0)
                {
                    Beam(art,centre,v,.08f,"Brass");var gondola=Model("TicketStand",art,v-Vector3.up*1.2f,Vector3.one*.85f);
                }
            }
            foreach(int s in new[]{-1,1})Beam(art,new Vector3(centre.x+s*6,-3,centre.z),centre,.34f,"OakDark");
        }
    }
}
