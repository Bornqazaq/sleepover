using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Splits the actual wall boards and backing around the six window apertures.</summary>
    internal static class HubBasementWallOpenings
    {
        internal static void BuildPanels(Transform root)
        {
            var g=new HubOriginalGeometry();
            for(int side=0;side<4;side++)
            {
                for(int i=0;i<52;i++)
                {
                    float center=-9.75f+(i+.5f)*19.5f/52, half=(19.5f/52-.012f)*.5f;
                    Segment(g,side,center-half,center+half,.02f,3.88f+HubCompactPass.CeilingRaise,9.785f,.035f,HubCompactPass.M("WallOak"));
                }
                Segment(g,side,-9.8f,9.8f,.025f,.295f,9.7f,.17f,HubCompactPass.M("Walnut"));
                Segment(g,side,-9.8f,9.8f,3.495f+HubCompactPass.CeilingRaise,3.745f+HubCompactPass.CeilingRaise,9.7f,.17f,HubCompactPass.M("Walnut"),true);
                Segment(g,side,-9.8f,9.8f,.2975f,.3325f,9.66f,.20f,HubCompactPass.M("Oak"));
            }
            g.Build(root,"PanelledRoom",castShadows:false);
            g=new HubOriginalGeometry();
            var backing=HubOriginalAssets.Mat("WindowMasonry","827F70");
            for(int side=0;side<4;side++)
                Segment(g,side,-10.2f,10.2f,0,HubCompactPass.RoomHeight,10,.4f,backing);
            g.Build(root,"BasementWallBacking");
        }

        private static void Segment(HubOriginalGeometry g,int side,float left,float right,float bottom,float top,
            float z,float depth,Material material,bool trim=false)
        {
            var cuts=side==2?new float[0]:HubBasementWindowPass.WindowOffsets(side);
            float width=trim?HubBasementWindowPass.Width+.04f:HubBasementWindowPass.GlassWidth+.06f;
            float centerY=HubBasementWindowPass.WindowCenterHeight(side);
            float low=centerY-HubBasementWindowPass.GlassHeight*.5f-.03f;
            float high=centerY+HubBasementWindowPass.GlassHeight*.5f+.03f;
            var edges=new List<float>{left,right};
            foreach(float center in cuts)
                foreach(float edge in new[]{center-width*.5f,center+width*.5f})
                    if(edge>left && edge<right)edges.Add(edge);
            edges.Sort();
            Quaternion rotation=Quaternion.Euler(0,side*90,0);
            for(int i=0;i<edges.Count-1;i++)
            {
                float a=edges[i],b=edges[i+1],mid=(a+b)*.5f;
                if(cuts.Any(center=>Mathf.Abs(mid-center)<width*.5f) && top>low && bottom<high)
                {
                    Box(g,rotation,a,b,bottom,Mathf.Min(top,low),z,depth,material);
                    Box(g,rotation,a,b,Mathf.Max(bottom,high),top,z,depth,material);
                }
                else Box(g,rotation,a,b,bottom,top,z,depth,material);
            }
        }
        private static void Box(HubOriginalGeometry g,Quaternion q,float left,float right,float bottom,float top,float z,float depth,Material material)
        {
            if(top-bottom<.001f || right-left<.001f)return;
            g.Box(q*new Vector3((left+right)*.5f,(bottom+top)*.5f,z),new Vector3(right-left,top-bottom,depth),material,q);
        }
    }
}
