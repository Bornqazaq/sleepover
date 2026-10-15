using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Igruha.Core.Ambient;
using Igruha.Minigames.DuckHunt;

namespace Igruha.EditorTools
{
    internal static partial class DuckHuntBarnArt
    {
        // Batches have a local footprint: URP Forward selects lights per renderer.
        // One city-sized batch left the fair's stalls outside their own lamp selection.
        static void CombineSpatial(Transform root,string prefix,float cell)
        {
            var groups=new Dictionary<Vector2Int,Transform>();
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>().ToArray())
            {
                if(!renderer.enabled||renderer.GetComponent<TextMesh>()!=null||renderer.GetComponent<MeshFilter>()==null)continue;
                var p=renderer.bounds.center;
                var key=new Vector2Int(Mathf.FloorToInt(p.x/cell),Mathf.FloorToInt(p.z/cell));
                if(!groups.TryGetValue(key,out var chunk))
                {
                    chunk=Group(root,prefix+" cell "+key.x+" "+key.y);groups.Add(key,chunk);
                }
                renderer.transform.SetParent(chunk,true);
            }
            foreach(var pair in groups)Combine(pair.Value,prefix+"_"+pair.Key.x+"_"+pair.Key.y);
            CleanEmpty(root);
        }

        static void LightFairground(Transform art)
        {
            // Light the actual attractions, counters and promenade, not just the bulbs.
            foreach(var p in new[]{new Vector3(-34,4,-44),HorseSite+Vector3.up*6,SwingSite+Vector3.up*7,
                new Vector3(-51,4,-26),RedTowerSite+Vector3.up*7,new Vector3(47,3,-6),TeaSite+Vector3.up*6,ShipSite+Vector3.up*8,OrbiterSite+Vector3.up*6})
                PointLight(art,p,new Color(1,.65f,.34f),38f,23f);
            PointLight(art,new Vector3(-34,18,-38),new Color(1,.76f,.45f),120,35);
            PointLight(art,SwingSite+new Vector3(0,13,3),new Color(1,.76f,.45f),80,18);
            foreach(float x in new[]{-43f,-28f,-13f,2f,32f,47f,62f,77f})
                foreach(float z in new[]{-19f,-31f})
                    PointLight(art,new Vector3(x,1.2f,z),new Color(1,.78f,.49f),18f,12f);

            // Place festoon lights on the measured roof/eave of each authored stall.
            foreach(Transform prop in art.Cast<Transform>().ToArray())
            {
                if(prop.name!="Tent"&&prop.name!="Booth"&&prop.name!="SnackBar"&&prop.name!="PopcornCart")continue;
                var rs=prop.GetComponentsInChildren<Renderer>();if(rs.Length==0)continue;
                Bounds b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
                if(b.center.y>5||InFiringSector(b.center.x,b.center.z))continue;
                float y=b.min.y+b.size.y*.63f;
                if(prop.name=="Tent")
                {
                    float radius=Mathf.Max(b.extents.x,b.extents.z)*.91f;
                    for(int n=0;n<32;n++)
                    {
                        float a=n*Mathf.PI/16;
                        Solid(art,"Tent eave lamp",new Vector3(b.center.x+Mathf.Sin(a)*radius,y,b.center.z+Mathf.Cos(a)*radius),Vector3.one*.16f,"Glow");
                    }
                }
                else foreach(int side in new[]{-1,1})
                    for(float x=b.min.x+.15f;x<b.max.x;x+=.45f)
                        Solid(art,"Stall canopy lamp",new Vector3(x,y,b.center.z+side*(b.extents.z+.06f)),Vector3.one*.13f,"Glow");
                PointLight(art,new Vector3(b.center.x,y+.3f,b.center.z),new Color(1,.74f,.42f),12f,8f);
            }
            AuditRideClearances(art);
        }

        static void AuditRideClearances(Transform art)
        {
            // Conservative full-motion envelopes, including cabins and projecting seats.
            var zones=RideEnvelopes();
            int checkedCount=0;
            foreach(Transform prop in art)
            {
                if(prop.name!="Tent"&&prop.name!="Booth"&&prop.name!="SnackBar"&&prop.name!="Car"&&prop.name!="Barrel"&&prop.name!="Bench"&&prop.name!="PopcornCart"&&prop.name!="ServiceRack"&&prop.name!="PrizeDisplay")continue;
                var rs=prop.GetComponentsInChildren<Renderer>();if(rs.Length==0)continue;
                Bounds b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);b.Expand(.8f);
                foreach(var zone in zones)if(b.Intersects(zone))throw new InvalidOperationException("Fairground stall intersects full ride envelope: "+prop.name+" at "+b.center);
                checkedCount++;
            }
            Debug.Log("Duck Hunt living fair: "+checkedCount+" independent props clear of eight full-motion ride envelopes.");
        }

        static void SwingCarousel(Transform art,Transform rides,Vector3 foot)
        {
            Solid(art,"Swing ride plinth",foot+Vector3.up*.12f,new Vector3(15,.24f,15),"Stone");
            Round(art,"Swing mast",foot+Vector3.up*4,new Vector3(.7f,4,.7f),"Cream");
            var rotor=Ride(rides,"SwingCarousel",foot+Vector3.up*7.7f);
            Round(rotor,"Swing crown",Vector3.zero,new Vector3(9,.30f,9),"Red");
            for(int i=0;i<24;i++)
            {
                float a=i*Mathf.PI/12,b=(i+1)*Mathf.PI/12;
                var mesh=new Mesh{name="Swing canopy panel"};
                var top=new Vector3(0,1.7f,0);var left=new Vector3(Mathf.Sin(b)*4.5f,.31f,Mathf.Cos(b)*4.5f);var right=new Vector3(Mathf.Sin(a)*4.5f,.31f,Mathf.Cos(a)*4.5f);
                mesh.vertices=new[]{top,left,right,top-Vector3.up*.06f,left-Vector3.up*.06f,right-Vector3.up*.06f};
                mesh.triangles=new[]{0,2,1,3,4,5,0,1,4,0,4,3,1,2,5,1,5,4,2,0,3,2,3,5};mesh.RecalculateNormals();
                var panel=new GameObject("Swing striped canopy");panel.transform.SetParent(rotor,false);
                panel.AddComponent<MeshFilter>().sharedMesh=mesh;panel.AddComponent<MeshRenderer>().sharedMaterial=Mat(i%2==0?"Cream":"Red");
            }
            Ring(rotor,Vector3.up*.32f,4.5f,.10f,"Brass",Quaternion.Euler(90,0,0));
            for(int i=0;i<72;i++)
            {
                float a=i*Mathf.PI/36;Solid(rotor,"Swing fascia bulb",rotor.position+new Vector3(Mathf.Sin(a)*4.55f,.05f,Mathf.Cos(a)*4.55f),Vector3.one*.16f,i%6<3?"Glow":"GlowPink");
            }
            for(int i=0;i<12;i++)
            {
                float a=i*Mathf.PI/6;
                Vector3 radial=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));
                Vector3 upper=rotor.position+radial*4.2f;
                Vector3 lower=foot+Vector3.up*2.8f+radial*6.5f;
                Beam(rotor,rotor.position,upper,.14f,"Brass");
                Vector3 tangent=Vector3.Cross(Vector3.up,radial)*.24f;
                Beam(rotor,upper+tangent,lower+tangent,.035f,"Iron");
                Beam(rotor,upper-tangent,lower-tangent,.035f,"Iron");
                var seat=Solid(rotor,"Swing seat",lower,new Vector3(.8f,.18f,.75f),i%2==0?"Blue":"Gold");
                seat.transform.rotation=Quaternion.LookRotation(radial);
                Solid(rotor,"Swing crown lamp",upper,Vector3.one*.20f,i%2==0?"Glow":"GlowBlue");
            }
            Combine(rotor,"SwingCarousel");Motion(rotor,AmbientMotion.Mode.Spin,Vector3.up,0,21);
        }

        static void LivingCity(Transform art,Transform rides)
        {
            var rng=new System.Random(20261015);
            // Ordered parcels separated by streets. Every facade has windows, including +Z toward the fair.
            for(int i=0;i<8;i++)ResidentialBlock(art,new Vector3(-90+i*32,Ground,-90),22,18,8+i%3*2,i,rng);
            for(int i=0;i<6;i++)ResidentialBlock(art,new Vector3(-82+i*41,Ground,-140),25,23,14+i%3*3,i+8,rng);
            for(int i=0;i<4;i++)ResidentialBlock(art,new Vector3(-70+i*65,Ground,-202),26,25,22+i%3*4,i+14,rng);
            foreach(float z in new[]{-116f,-173f})CityBoulevard(art,z);
            foreach(float x in new[]{-112f,150f})
            {
                Solid(art,"City cross street",new Vector3(x,Ground+.06f,-140),new Vector3(11,.18f,152),"Ink");
                for(float z=-70;z>-210;z-=12)Solid(art,"Cross street dash",new Vector3(x,Ground+.17f,z),new Vector3(.22f,.025f,5),"Cream");
            }
            foreach(float z in new[]{RoadZ,-116f})
                foreach(int direction in new[]{-1,1})
                    for(int i=0;i<6;i++)TrafficCar(rides,z,direction,i);
        }

        static void ResidentialBlock(Transform art,Vector3 foot,float width,float depth,int floors,int index,System.Random rng)
        {
            string wall=index%3==0?"CityBrick":"CityIvory";
            float height=4.2f+floors*3.05f;
            float podiumWidth=width+2;
            Solid(art,"City parcel pavement",foot+Vector3.up*.04f,new Vector3(width+7,.28f,depth+8),"CityConcrete");
            Solid(art,"Residential podium",foot+Vector3.up*2.1f,new Vector3(podiumWidth,4.2f,depth+1.6f),"CitySlate");
            Solid(art,"Residential tower",foot+Vector3.up*(height*.5f+2.1f),new Vector3(width,height-4.2f,depth),wall);
            // Setback upper volume and visible plant-room make a deliberate skyline.
            Solid(art,"Penthouse setback",foot+Vector3.up*(height+1.4f),new Vector3(width*.70f,2.8f,depth*.76f),"CitySlate");
            Solid(art,"Roof cornice",foot+Vector3.up*(height+.12f),new Vector3(width+.7f,.24f,depth+.7f),"CityIvory");
            foreach(int side in new[]{-1,1})
            {
                float face=foot.z+side*(depth*.5f+.035f);
                for(int c=0;c<6;c++)
                {
                    float x=foot.x-width*.5f+(c+.5f)*width/6;
                    Solid(art,"Vertical window recess",new Vector3(x,foot.y+(height+4.2f)*.5f,face),new Vector3(2.5f,height-4.25f,.10f),"CitySlate");
                    for(int r=0;r<floors;r++)
                    {
                        float y=foot.y+5.65f+r*3.05f;
                        string window=rng.Next(100)<58?(rng.Next(4)==0?"CityWindowCool":"CityWindowWarm"):"CityGlass";
                        Solid(art,"Apartment glazing",new Vector3(x,y,face+side*.08f),new Vector3(1.95f,1.85f,.075f),window);
                        Solid(art,"Window mullion",new Vector3(x,y,face+side*.14f),new Vector3(.07f,1.85f,.06f),"CitySlate");
                        if(c==0||c==5)
                        {
                            float balconyZ=face+side*.48f;
                            Solid(art,"Balcony slab",new Vector3(x,y-1.02f,balconyZ),new Vector3(2.65f,.16f,1.05f),"CityIvory");
                            Solid(art,"Balcony parapet",new Vector3(x,y-.55f,face+side*.97f),new Vector3(2.65f,.78f,.10f),wall);
                            foreach(int edge in new[]{-1,1})Solid(art,"Balcony return",new Vector3(x+edge*1.28f,y-.55f,balconyZ),new Vector3(.10f,.78f,1.05f),wall);
                        }
                    }
                }
                for(int r=0;r<=floors;r+=3)Solid(art,"Facade belt",new Vector3(foot.x,foot.y+4.2f+r*3.05f,face+side*.19f),new Vector3(width,.16f,.35f),"CityIvory");
                Solid(art,"Illuminated crown",new Vector3(foot.x,foot.y+height-.25f,face+side*.16f),new Vector3(width-.5f,.08f,.08f),"CityWindowWarm");
                for(int shop=0;shop<4;shop++)
                {
                    float x=foot.x-width*.5f+(shop+.5f)*width/4;
                    Solid(art,"Shopfront",new Vector3(x,foot.y+1.8f,foot.z+side*(depth*.5f+.86f)),new Vector3(width/4-.7f,2.9f,.08f),"CityWindowWarm");
                    Solid(art,"Store awning",new Vector3(x,foot.y+3.45f,foot.z+side*(depth*.5f+1.15f)),new Vector3(width/4-.3f,.20f,1.25f),"CitySlate");
                }
            }
            foreach(int side in new[]{-1,1})
                for(int c=0;c<4;c++)for(int r=0;r<floors;r++)
                {
                    float z=foot.z-depth*.5f+(c+.5f)*depth/4;
                    Solid(art,"Side apartment frame",new Vector3(foot.x+side*(width*.5f+.04f),foot.y+5.65f+r*3.05f,z),new Vector3(.10f,2.1f,2.2f),"CitySlate");
                    Solid(art,"Side apartment glazing",new Vector3(foot.x+side*(width*.5f+.11f),foot.y+5.65f+r*3.05f,z),new Vector3(.07f,1.85f,1.95f),rng.Next(100)<55?"CityWindowWarm":"CityGlass");
                }
            // Paired entrance lamps, street trees and planting troughs face the fair.
            for(int side=-1;side<=1;side+=2)
            {
                Vector3 tree=foot+new Vector3(side*(width*.5f+2.0f),0,depth*.5f+2.8f);
                Solid(art,"City planter",tree+Vector3.up*.35f,new Vector3(1.7f,.7f,1.7f),"CitySlate");
                Model("Pine",art,tree+Vector3.up*.5f,Vector3.one*1.1f);
            }
        }

        static void CityBoulevard(Transform art,float z)
        {
            SurfaceTiles(art,"City boulevard",new Vector3(20,Ground+.14f,z),285,12,"RoadAsphalt");
            foreach(int side in new[]{-1,1})
            {
                Solid(art,"City sidewalk",new Vector3(20,Ground+.10f,z+side*7.2f),new Vector3(285,.28f,2.4f),"CityConcrete");
                for(float x=-108;x<154;x+=26)
                {
                    Vector3 foot=new Vector3(x,Ground,z+side*6.7f);LampPost(art,foot,6);
                    RoadLight(art,foot+Vector3.up*5.3f,new Vector3(x,Ground,z));
                }
            }
            for(float x=-120;x<162;x+=10)Solid(art,"City lane dash",new Vector3(x,Ground+.16f,z),new Vector3(4,.025f,.22f),"Cream");
        }

        static void TrafficCar(Transform rides,float road,int direction,int index)
        {
            var car=Ride(rides,"CityTraffic_"+road+"_"+direction+"_"+index,Vector3.zero);
            string colour=new[]{"Red","CityIvory","Blue","CitySlate","Gold","Teal"}[index];
            Part(car,"Car body",new Vector3(0,.66f,0),new Vector3(1.85f,.65f,4.1f),colour);
            Part(car,"Car cabin",new Vector3(0,1.20f,-.12f),new Vector3(1.62f,.65f,2.15f),"CityGlass");
            Part(car,"Car roof",new Vector3(0,1.55f,-.20f),new Vector3(1.67f,.12f,1.8f),colour);
            foreach(int side in new[]{-1,1})
            {
                foreach(float z in new[]{-1.25f,1.25f})Round(car,"Road wheel",new Vector3(side*.94f,.39f,z),new Vector3(.70f,.12f,.70f),"Iron",Quaternion.Euler(0,0,90));
                Part(car,"Headlamp",new Vector3(side*.59f,.79f,2.07f),new Vector3(.39f,.18f,.06f),"Glow");
                var beam=Spot(car,"Headlight beam",new Vector3(side*.59f,.81f,2.12f),Quaternion.Euler(5,side*3,0),new Color(1,.91f,.74f),90,27,48);
                Part(car,"Tail lamp",new Vector3(side*.59f,.79f,-2.07f),new Vector3(.36f,.14f,.06f),"GlowRed");
            }
            Combine(car,"TrafficCar_"+road+"_"+direction+"_"+index);
            Vector3 from=new Vector3(direction>0?-150:190,Ground+.08f,road+direction*2.5f);
            Vector3 to=new Vector3(direction>0?190:-150,Ground+.08f,road+direction*2.5f);
            car.gameObject.AddComponent<DuckHuntAmbientTraffic>().Configure(from,to,road==RoadZ?9:7,index/6f);
            car.position=Vector3.Lerp(from,to,index/6f);car.rotation=Quaternion.LookRotation(to-from);
        }
    }
}
