using UnityEngine;
using static Igruha.EditorTools.HubCompactPass;

namespace Igruha.EditorTools
{
    internal static class HubCompactArchitecture
    {
        internal static void Build(Transform root)
        {
            var geo=new HubOriginalGeometry();
            Floor(geo,-10,10,3.5f,10,0);Floor(geo,-10,10,-10,-3.5f,0);Floor(geo,-10,-4,-3.5f,3.5f,0);Floor(geo,4,10,-3.5f,3.5f,0);Floor(geo,-4,4,-3.5f,3.5f,-.7f);
            geo.Build(root,"OakFloor",castShadows:false);
            geo=new HubOriginalGeometry();
            for(int side=0;side<4;side++)
            {
                Quaternion q=Quaternion.Euler(0,side*90,0);
                for(int i=0;i<52;i++)geo.Box(q*new Vector3(-9.75f+(i+.5f)*19.5f/52,1.95f,9.785f),new Vector3(19.5f/52-.012f,3.86f,.035f),M("WallOak"),q);
                foreach(float y in new[]{.16f,3.62f})geo.Box(q*new Vector3(0,y,9.70f),new Vector3(19.6f,y<1?.27f:.25f,.17f),M("Walnut"),q);
                geo.Box(q*new Vector3(0,.315f,9.66f),new Vector3(19.6f,.035f,.20f),M("Oak"),q);
            }
            for(int row=0;row<5;row++)for(int col=0;col<8;col++)
                geo.Box(new Vector3(-8.54f+col*2.44f,3.875f,-7.8f+row*3.90f),new Vector3(2.425f,.035f,3.88f),HubOriginalAssets.Mat("CeilingLinen","D3C2A2",.075f));
            geo.Build(root,"PanelledRoom",castShadows:false);
            var beams=new HubOriginalGeometry();
            foreach(float x in new[]{-5.2f,5.2f})beams.Box(new Vector3(x,3.77f,0),new Vector3(.25f,.26f,19.6f),M("Walnut"));
            foreach(float z in new[]{-7.2f,-1.3f,4.5f,9.4f})beams.Box(new Vector3(0,3.73f,z),new Vector3(19.6f,.27f,.23f),M("Walnut"));
            foreach(float x in new[]{-5.2f,5.2f})foreach(float z in new[]{-6.8f,4.5f})
            {
                geo=new HubOriginalGeometry();geo.Box(new Vector3(x,1.82f,z),new Vector3(.27f,3.64f,.27f),M("Walnut"));
                geo.Box(new Vector3(x,.14f,z),new Vector3(.40f,.28f,.40f),M("Oak"));geo.Box(new Vector3(x,3.47f,z),new Vector3(.49f,.20f,.49f),M("Oak"));geo.Build(root,"Column_"+x+"_"+z);
                Solid(root,"ColumnCollider",new Vector3(x,1.82f,z),new Vector3(.40f,3.64f,.40f));
            }
            beams.Build(root,"CeilingBeams",true);
            geo=new HubOriginalGeometry();
            // Pit edge is narrow and below the running surface; no new obstacle on the ramps.
            foreach(float x in new[]{-4f,4f})geo.Box(new Vector3(x,-.015f,0),new Vector3(.17f,.055f,7.10f),M("Oak"));
            foreach(float z in new[]{-3.5f,3.5f})geo.Box(new Vector3(0,-.015f,z),new Vector3(8.1f,.055f,.17f),M("Oak"));
            geo.Build(root,"LoungeRim");
            Staircase(root);
            PitSteps(root);
            Windows(root);
        }
        private static void Floor(HubOriginalGeometry geo,float xmin,float xmax,float zmin,float zmax,float y)
        {
            const float pitch=.42f,length=2.35f;
            int ix=0;
            for(float x=xmin;x<xmax-.01f;x+=pitch,ix++)
            {
                float width=Mathf.Min(pitch,xmax-x);
                for(float z=zmin-length+(ix%3)*length/3;z<zmax;z+=length)
                {
                    float a=Mathf.Max(z,zmin),b=Mathf.Min(z+length,zmax);if(b-a<.02f)continue;
                    geo.Box(new Vector3(x+width*.5f,y+.008f,(a+b)*.5f),new Vector3(width-.008f,.014f,b-a-.009f),M("FloorOak"));
                }
            }
        }
        private static void Staircase(Transform root)
        {
            var stairs=Group(root,"EntryStairs");var g=new HubOriginalGeometry();
            for(int i=0;i<8;i++)
            {
                float height=(i+1)*.1875f;var p=new Vector3(-4.8f-(i+.5f)*.3125f,height*.5f,-8.55f);var size=new Vector3(.3125f,height,2.5f);
                g.Box(p,size,M("Walnut"));g.Box(new Vector3(p.x,height+.01f,p.z),new Vector3(.325f,.021f,2.49f),M("FloorOak"));Solid(stairs,"Step_"+i,p,size);
            }
            g.Box(new Vector3(-8.55f,1.40f,-8.55f),new Vector3(2.5f,.20f,2.5f),M("Oak"));Solid(stairs,"Landing",new Vector3(-8.55f,1.4f,-8.55f),new Vector3(2.5f,.20f,2.5f));
            g.Box(new Vector3(-8.55f,.7f,-7.39f),new Vector3(2.5f,1.4f,.2f),M("WallOak"));Solid(stairs,"LandingSupport",new Vector3(-8.55f,.7f,-7.39f),new Vector3(2.5f,1.4f,.2f));
            for(int i=0;i<7;i++)
            {float x=-4.98f-i*.75f;float top=1.12f+Mathf.Min(1.4f,i*.47f);g.Box(new Vector3(x,top-.45f,-7.34f),new Vector3(.085f,.9f,.085f),M("Walnut"));}
            g.Line(new Vector3(-4.98f,1.12f,-7.34f),new Vector3(-7.3f,2.52f,-7.34f),.095f,M("Oak"));g.Line(new Vector3(-7.3f,2.52f,-7.34f),new Vector3(-9.75f,2.52f,-7.34f),.095f,M("Oak"));
            // Continuous invisible rail follows the visible handrail; stops walking off the platform edge.
            var rail=Solid(stairs,"SlopedRail",new Vector3(-6.14f,1.32f,-7.34f),new Vector3(2.70f,.9f,.09f));rail.transform.localRotation=Quaternion.Euler(0,0,-31);
            Solid(stairs,"LandingRail",new Vector3(-8.55f,2.07f,-7.34f),new Vector3(2.50f,.90f,.09f));
            g.Box(new Vector3(-9.72f,2.49f,-8.55f),new Vector3(.08f,1.96f,1.02f),M("Sage"));
            foreach(float z in new[]{-9.12f,-7.98f})g.Box(new Vector3(-9.67f,2.52f,z),new Vector3(.16f,2.08f,.11f),M("Oak"));
            g.Box(new Vector3(-9.67f,3.54f,-8.55f),new Vector3(.16f,.12f,1.26f),M("Oak"));
            g.Build(stairs,"StairWood");
        }
        private static void PitSteps(Transform root)
        {
            var g=new HubOriginalGeometry();
            foreach(int side in new[]{-1,1})
            {
                float x=side<0?-2.7f:3.1f,width=side<0?1.6f:1.25f;
                for(int i=0;i<3;i++)
                {
                    float top=-.175f*(i+1),height=top+.7f;
                    var p=new Vector3(x,-.7f+height*.5f,side*(3.30f-i*.40f));var size=new Vector3(width,height,.40f);
                    Solid(root,"LoungeStep_"+side+"_"+i,p,size);g.Box(p,size,M("Walnut"));
                    g.Box(new Vector3(x,top+.008f,p.z),new Vector3(width+.025f,.016f,.405f),M("Oak"));
                }
            }
            g.Build(root,"LoungeSteps");
        }
        private static void Windows(Transform root)
        {
            var g=new HubOriginalGeometry();var blue=HubOriginalAssets.Mat("NightWindow","315D96",.55f);
            foreach(int side in new[]{0,1,3})foreach(float x in new[]{-6.7f,0,6.7f})
            {
                if(side==0&&x>6)continue;var q=Quaternion.Euler(0,side*90,0);var p=q*new Vector3(x,3.12f,9.69f);
                g.Box(p,new Vector3(2.15f,.74f,.18f),M("Oak"),q);g.Box(p-q*Vector3.forward*.101f,new Vector3(1.92f,.53f,.015f),blue,q);
                foreach(float dx in new[]{-.64f,0,.64f})g.Box(p+q*new Vector3(dx,0,-.13f),new Vector3(.035f,.56f,.03f),M("Cream"),q);
                g.Box(p+q*new Vector3(0,-.38f,-.06f),new Vector3(2.3f,.07f,.35f),M("Cream"),q);
            }
            g.Build(root,"BasementWindows");
        }
    }
}
