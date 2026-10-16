using System;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Ambient;

namespace Igruha.EditorTools
{
    internal static partial class DuckHuntBarnArt
    {
        static readonly Vector3 HorseSite=new Vector3(39,Ground,-42);
        static readonly Vector3 RedTowerSite=new Vector3(-3,Ground,-34);
        static readonly Vector3 SwingSite=new Vector3(59,Ground,-43);
        static readonly Vector3 TeaSite=new Vector3(-8,Ground,-48);
        static readonly Vector3 ShipSite=new Vector3(17.28f,Ground,-36.5f);
        static readonly Vector3 OrbiterSite=new Vector3(78.5f,Ground,-43);

        static void AttractionSignSupports(Transform art)
        {
            var previous=art.Find("Attraction sign supports");
            if(previous!=null)UnityEngine.Object.DestroyImmediate(previous.gameObject);
            var posts=Group(art,"Attraction sign supports");
            foreach(var site in new[]{new Vector3(TeaSite.x,7.1f,TeaSite.z),new Vector3(OrbiterSite.x,8.1f,OrbiterSite.z)})
                foreach(int side in new[]{-1,1})
                {
                    var foot=new Vector3(site.x+side*site.y*.32f,Ground,site.z+site.y+.76f);
                    Beam(posts,foot,foot+Vector3.up*1.65f,.12f,"Brass");
                }
            Beam(posts,ShipSite+new Vector3(0,11,3.3f),ShipSite+new Vector3(0,12,3.5f),.16f,"Brass");
            Combine(posts,"AttractionSignSupports");
        }

        // Small receivers let URP select street lamps and moving headlights locally.
        static void SurfaceTiles(Transform art,string name,Vector3 top,float width,float depth,string material)
        {
            int nx=Mathf.CeilToInt(width/9),nz=Mathf.CeilToInt(depth/6);
            float sx=width/nx,sz=depth/nz;
            for(int x=0;x<nx;x++)for(int z=0;z<nz;z++)
                Solid(art,name,top+new Vector3(-width*.5f+(x+.5f)*sx,-.08f,-depth*.5f+(z+.5f)*sz),new Vector3(sx,.16f,sz),material);
        }

        static Light Spot(Transform parent,string name,Vector3 local,Quaternion rotation,Color color,float intensity,float range,float angle)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=local;go.transform.localRotation=rotation;
            var light=go.AddComponent<Light>();light.type=LightType.Spot;light.color=color;light.intensity=intensity;
            light.range=range;light.spotAngle=angle;light.innerSpotAngle=angle*.65f;light.shadows=LightShadows.None;
            light.renderMode=LightRenderMode.ForcePixel;return light;
        }

        static void RoadLight(Transform art,Vector3 position,Vector3 target)
        {
            Spot(art,"Road illumination",art.InverseTransformPoint(position),Quaternion.LookRotation(target-position),new Color(1,.81f,.54f),85,25,116);
        }

        static Bounds[] RideEnvelopes()=>new[]{
            new Bounds(new Vector3(-34,15,-44),new Vector3(35,37,9)),
            new Bounds(HorseSite+Vector3.up*4,new Vector3(13,10,13)),
            new Bounds(SwingSite+Vector3.up*6,new Vector3(17,13,17)),
            new Bounds(new Vector3(-51,8,-26),new Vector3(6,24,5)),
            new Bounds(RedTowerSite+Vector3.up*9,new Vector3(6,20,5)),
            new Bounds(TeaSite+Vector3.up*3,new Vector3(15,6,15)),
            new Bounds(ShipSite+Vector3.up*8,new Vector3(23,17,8)),
            new Bounds(OrbiterSite+Vector3.up*3,new Vector3(18,7,18))};

        static void RidePlaza(Transform art,Vector3 foot,float radius,string title)
        {
            Round(art,title+" base",foot-art.position+Vector3.up*.10f,new Vector3(radius*2,.10f,radius*2),"Stone");
            Ring(art,foot-art.position+Vector3.up*.25f,radius-.25f,.13f,"Brass",Quaternion.Euler(90,0,0));
            for(int i=0;i<48;i++)
            {
                float a=i*Mathf.PI/24;
                Solid(art,title+" edge light",foot+new Vector3(Mathf.Cos(a)*(radius-.12f),.29f,Mathf.Sin(a)*(radius-.12f)),Vector3.one*.16f,i%6<3?"Glow":"GlowBlue");
            }
            // Lamps are outside the moving diameter, and signs face the ducks (+Z).
            foreach(int side in new[]{-1,1})
            {
                var p=foot+new Vector3(side*(radius+.7f),0,0);LampPost(art,p,4.5f);
                PointLight(art,p+Vector3.up*4,new Color(1,.74f,.43f),30,14);
            }
            var sign=Group(art,title+" sign");sign.position=foot+new Vector3(0,1.4f,radius+.7f);
            Sign(sign,title,Vector3.zero,radius*.95f,.8f,"Red");sign.localRotation=Quaternion.Euler(0,180,0);
        }

        static void TeaCupRide(Transform art,Transform rides,Vector3 foot)
        {
            RidePlaza(art,foot,7.1f,"TEA PARTY");
            var platter=Ride(rides,"TeaParty",foot+Vector3.up*.35f);
            Round(platter,"Tea dance floor",Vector3.zero,new Vector3(13,.15f,13),"Teal");
            Combine(platter,"TeaPartyFloor");
            // Separate pivots keep every cup spinning relative to the rotating platter.
            for(int i=0;i<6;i++)
            {
                float a=i*Mathf.PI/3;var cup=Ride(platter,"TeaCup_"+i,platter.position+new Vector3(Mathf.Cos(a)*4.4f,.23f,Mathf.Sin(a)*4.4f));
                Round(cup,"Saucer",Vector3.zero,new Vector3(2.7f,.08f,2.7f),"Cream");
                Round(cup,"Cup floor",Vector3.up*.18f,new Vector3(1.95f,.08f,1.95f),"OakLight");
                for(int j=0;j<24;j++)
                {
                    float b=j*Mathf.PI/12;var panel=Part(cup,"Cup curved wall",new Vector3(Mathf.Sin(b)*1.05f,.75f,Mathf.Cos(b)*1.05f),new Vector3(.29f,1.1f,.15f),i%2==0?"Red":"Gold");
                    panel.transform.rotation=Quaternion.Euler(0,b*Mathf.Rad2Deg,0);
                }
                Ring(cup,Vector3.up*1.32f,1.08f,.09f,"Cream",Quaternion.Euler(90,0,0));
                Ring(cup,new Vector3(1.33f,.77f,0),.50f,.12f,"Cream",Quaternion.identity);
                Round(cup,"Tea steering table",Vector3.up*.7f,new Vector3(.65f,.07f,.65f),"Brass");
                Combine(cup,"TeaCup_"+i);Motion(cup,AmbientMotion.Mode.Spin,Vector3.up,0,9+i*.7f);
            }
            Motion(platter,AmbientMotion.Mode.Spin,Vector3.up,0,34);
        }

        static void PirateShipRide(Transform art,Transform rides,Vector3 foot)
        {
            SurfaceTiles(art,"Ship deck plaza",foot+Vector3.up*.20f,24,9,"Stone");
            Vector3 axle=foot+Vector3.up*11;
            foreach(int side in new[]{-1,1})
            {
                foreach(int end in new[]{-1,1})Beam(art,foot+new Vector3(end*6,0,side*3.3f),axle+Vector3.forward*side*3.3f,.42f,"Teal");
                Beam(art,axle+Vector3.forward*side*3.3f,axle+Vector3.forward*-side*3.3f,.34f,"Brass");
                BulbLine(art,foot+new Vector3(-6,0,side*3.3f),axle+Vector3.forward*side*3.3f,.65f);
            }
            var ship=Ride(rides,"PirateShip",axle);
            var points=new List<Vector3>();var tris=new List<int>();
            float[] xs={-6,-4,-2,0,2,4,6},widths={.2f,1.5f,2.1f,2.2f,2.1f,1.5f,.2f},heights={1.8f,.5f,0,0,0,.5f,1.8f};
            for(int i=0;i<xs.Length;i++)
            {
                float x=xs[i],y=-7+heights[i],w=widths[i];
                points.Add(new Vector3(x,y,-w));points.Add(new Vector3(x,y-1.0f,-w*.72f));points.Add(new Vector3(x,y-1.5f,0));points.Add(new Vector3(x,y-1.0f,w*.72f));points.Add(new Vector3(x,y,w));
            }
            for(int i=0;i<xs.Length-1;i++)for(int j=0;j<5;j++)
            {
                int a=i*5+j,b=i*5+(j+1)%5,c=(i+1)*5+(j+1)%5,d=(i+1)*5+j;
                tris.AddRange(new[]{a,c,b,a,d,c});
            }
            tris.AddRange(new[]{0,1,2,0,2,3,0,3,4,30,32,31,30,33,32,30,34,33});
            var mesh=new Mesh{name="Curved timber ship hull"};mesh.SetVertices(points);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            var hull=new GameObject("Curved ship hull");hull.transform.SetParent(ship,false);hull.AddComponent<MeshFilter>().sharedMesh=mesh;hull.AddComponent<MeshRenderer>().sharedMaterial=Mat("Red");
            foreach(int side in new[]{-1,1})
            {
                Beam(ship,axle+new Vector3(0,0,side*2),axle+new Vector3(-4,-6.5f,side*1.5f),.18f,"Brass");
                Beam(ship,axle+new Vector3(0,0,side*2),axle+new Vector3(4,-6.5f,side*1.5f),.18f,"Brass");
                for(int i=0;i<6;i++)BulbLine(ship,axle+new Vector3(xs[i],-6.75f+heights[i],side*widths[i]),axle+new Vector3(xs[i+1],-6.75f+heights[i+1],side*widths[i+1]),.5f);
            }
            for(int i=-3;i<=3;i++)
            {
                Part(ship,"Bench",new Vector3(i*1.05f,-6.6f,0),new Vector3(.55f,.15f,3.0f),"Cream");
                Part(ship,"Bench back",new Vector3(i*1.05f+.25f,-6.2f,0),new Vector3(.12f,.75f,3.0f),"Gold");
            }
            Combine(ship,"PirateShip");Motion(ship,AmbientMotion.Mode.Sway,Vector3.forward,38,8);
            var sign=Group(art,"Pirate sign");sign.position=foot+new Vector3(0,11.9f,3.5f);Sign(sign,"PIRATE BAY",Vector3.zero,7,1,"Teal");sign.rotation=Quaternion.Euler(0,180,0);
        }

        static void OrbiterRide(Transform art,Transform rides,Vector3 foot)
        {
            RidePlaza(art,foot,8.1f,"STAR TWISTER");
            var rotor=Ride(rides,"StarTwister",foot+Vector3.up*.6f);
            Round(rotor,"Central housing",Vector3.up*.5f,new Vector3(2.3f,.7f,2.3f),"Purple");
            for(int i=0;i<4;i++)
            {
                float a=i*Mathf.PI*.5f;Vector3 p=new Vector3(Mathf.Cos(a)*4.4f,.6f,Mathf.Sin(a)*4.4f);
                Beam(rotor,rotor.position,rotor.position+p,.32f,"Brass");
            }
            Combine(rotor,"StarTwisterArms");
            for(int i=0;i<4;i++)
            {
                float a=i*Mathf.PI*.5f;var hub=Ride(rotor,"TwisterHub_"+i,rotor.position+new Vector3(Mathf.Cos(a)*4.4f,.6f,Mathf.Sin(a)*4.4f));
                Round(hub,"Hub cap",Vector3.zero,new Vector3(1,.25f,1),"Gold");
                for(int j=0;j<3;j++)
                {
                    float b=j*Mathf.PI*2/3;Vector3 p=new Vector3(Mathf.Cos(b)*1.6f,0,Mathf.Sin(b)*1.6f);
                    Beam(hub,hub.position,hub.position+p,.17f,"Brass");
                    var cabin=Group(hub,"Twister cabin");cabin.position=hub.position+p;
                    Part(cabin,"Pod base",Vector3.zero,new Vector3(1.65f,.45f,1.45f),i%2==0?"Teal":"Purple");
                    Part(cabin,"Pod back",new Vector3(0,.57f,.61f),new Vector3(1.65f,.9f,.20f),"Gold");
                    foreach(int side in new[]{-1,1})Part(cabin,"Pod side",new Vector3(side*.72f,.43f,0),new Vector3(.18f,.65f,1.45f),"Red");
                    Part(cabin,"Seat cushion",Vector3.up*.35f,new Vector3(1.2f,.15f,.85f),"Cream");
                    Part(cabin,"Pod lamp",new Vector3(0,.10f,-.75f),new Vector3(1.1f,.10f,.08f),"GlowPink");
                    cabin.rotation=Quaternion.Euler(0,90-b*Mathf.Rad2Deg,0);
                }
                Combine(hub,"TwisterHub_"+i);Motion(hub,AmbientMotion.Mode.Spin,Vector3.down,0,7+i*.5f);
            }
            Motion(rotor,AmbientMotion.Mode.Spin,Vector3.up,0,24);
        }

        static void PrizeScreen(Transform floor,Vector3 foot,string title)
        {
            var root=PavilionProp(floor,"PrizePortraitScreen",foot,out var visual);
            Body(root,visual,Vector3.zero,new Vector3(2.6f,.65f,1.05f),"Gold");
            Part(visual,"Prize portrait backing",new Vector3(0,1.55f,0),new Vector3(2.5f,1.8f,.30f),"Teal");
            PropBox(root.gameObject,"Standing prize cover",new Vector3(0,1.55f,0),new Vector3(2.5f,1.8f,.30f));
            Sign(visual,title,new Vector3(0,2.64f,-.05f),2.7f,.42f,"Gold");
            for(int i=-1;i<=1;i++)Toy(visual,i==0?"Duck":"Bear",new Vector3(i*.72f,.71f,-.31f),.6f,i+1);
            Combine(visual,"PavilionPrizePortrait");
        }
    }
}
