using UnityEngine;

namespace Igruha.EditorTools
{
    internal static partial class DuckHuntBarnArt
    {
        static bool AttractionLane(float x)
        {
            foreach(float lane in new[]{-34f,-8f,-3f,1.8f,32.8f,39f,59f,78.5f})
                if(Mathf.Abs(x-lane)<2.5f)return true;
            return false;
        }

        static void FairApproaches(Transform art)
        {
            var paths=Group(art,"Connected attraction approaches");
            // A T junction from the gates splits around the full moving ship volume.
            PathRectangle(paths,"Gate entrance",11.78f,22.78f,-49,-45.5f);
            PathRectangle(paths,"Gate crosswalk",.3f,34.3f,-45.5f,-42.5f);
            PathRectangle(paths,"Ship west bypass",.3f,3.3f,-42.5f,-30.25f);
            PathRectangle(paths,"Ship east bypass",31.3f,34.3f,-42.5f,-30.25f);
            PathRectangle(paths,"Ship west boarding",3.3f,5.28f,-37.5f,-34.5f);
            PathRectangle(paths,"Ship east boarding",29.28f,31.3f,-37.5f,-34.5f);
            // Every approach terminates at a boarding platform, not at a random grass patch.
            PathRectangle(paths,"Wheel approach",-35.5f,-32.5f,-36.1f,-30.25f);
            PathRectangle(paths,"Tea party approach",-9.5f,-6.5f,-40.9f,-30.25f);
            PathRectangle(paths,"Red tower approach",-4.4f,-1.6f,-31.5f,-30.25f);
            PathRectangle(paths,"Blue tower approach",-49.5f,-48,-26,-23);
            PathRectangle(paths,"Horse carousel approach",37.5f,40.5f,-36.265f,-30.25f);
            PathRectangle(paths,"Swing carousel approach",57.5f,60.5f,-35.5f,-30.25f);
            PathRectangle(paths,"Twister approach",77,80,-34.9f,-30.25f);
            foreach(float x in new[]{-34f,-8f,39f,59f,78.5f})
            {
                // A pair of inset marker lights frames each entrance without blocking it.
                foreach(int side in new[]{-1,1})
                {
                    Vector3 p=new Vector3(x+side*1.72f,Ground,-32.4f);
                    Solid(paths,"Approach bollard",p+Vector3.up*.34f,new Vector3(.16f,.68f,.16f),"Iron");
                    Solid(paths,"Approach marker",p+Vector3.up*.69f,new Vector3(.17f,.08f,.17f),"Glow");
                }
            }
        }

        static void PathRectangle(Transform art,string name,float x0,float x1,float z0,float z1)
        {
            // Raised gravel is intentionally above the underlying sand; no coplanar layers.
            var centre=new Vector3((x0+x1)*.5f,Ground+.15f,(z0+z1)*.5f);
            float width=x1-x0,depth=z1-z0;
            SurfaceTiles(art,name,centre,width,depth,"Gravel");
            // Paving at human scale, with open joints; long routes do not read as wooden planks.
            const float pitch=.64f;
            int nx=Mathf.Max(1,Mathf.CeilToInt(width/pitch)),nz=Mathf.Max(1,Mathf.CeilToInt(depth/pitch));
            float sx=width/nx,sz=depth/nz;
            for(int x=0;x<nx;x++)for(int z=0;z<nz;z++)
                Solid(art,"Approach paving stone",new Vector3(x0+(x+.5f)*sx,Ground+.177f,z0+(z+.5f)*sz),
                    new Vector3(sx-.025f,.045f,sz-.025f),(x+z)%5==0?"Stone":"CityConcrete");
        }
    }
}
