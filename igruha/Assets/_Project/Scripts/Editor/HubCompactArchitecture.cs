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
            HubBasementWallOpenings.BuildPanels(root);
            geo=new HubOriginalGeometry();
            for(int row=0;row<5;row++)for(int col=0;col<8;col++)
                geo.Box(new Vector3(-8.54f+col*2.44f,3.875f+CeilingRaise,-7.8f+row*3.90f),new Vector3(2.425f,.035f,3.88f),HubOriginalAssets.Mat("CeilingLinen","D3C2A2",.075f));
            geo.Build(root,"PanelledRoom",castShadows:false);
            var beams=new HubOriginalGeometry();
            foreach(float x in new[]{-5.2f,5.2f})beams.Box(new Vector3(x,3.77f+CeilingRaise,0),new Vector3(.25f,.26f,19.6f),M("Walnut"));
            foreach(float z in new[]{-7.2f,-1.3f,4.5f,9.4f})beams.Box(new Vector3(0,3.73f+CeilingRaise,z),new Vector3(19.6f,.27f,.23f),M("Walnut"));
            foreach(float x in new[]{-5.2f,5.2f})foreach(float z in new[]{-6.8f,4.5f})
            {
                geo=new HubOriginalGeometry();geo.Box(new Vector3(x,1.82f+CeilingRaise*.5f,z),new Vector3(.27f,3.64f+CeilingRaise,.27f),M("Walnut"));
                geo.Box(new Vector3(x,.14f,z),new Vector3(.40f,.28f,.40f),M("Oak"));geo.Box(new Vector3(x,3.47f+CeilingRaise,z),new Vector3(.49f,.20f,.49f),M("Oak"));geo.Build(root,"Column_"+x+"_"+z);
                Solid(root,"ColumnCollider",new Vector3(x,1.82f+CeilingRaise*.5f,z),new Vector3(.40f,3.64f+CeilingRaise,.40f));
            }
            beams.Build(root,"CeilingBeams",true);
            geo=new HubOriginalGeometry();
            // Pit edge is narrow and below the running surface; no new obstacle on the ramps.
            foreach(float x in new[]{-4f,4f})geo.Box(new Vector3(x,-.015f,0),new Vector3(.17f,.055f,7.10f),M("Oak"));
            foreach(float z in new[]{-3.5f,3.5f})geo.Box(new Vector3(0,-.015f,z),new Vector3(8.1f,.055f,.17f),M("Oak"));
            geo.Build(root,"LoungeRim");
            HubEntryStairsBuilder.Build(root);
            PitSteps(root);
            HubBasementWindowPass.Build(root);
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
    }
}
