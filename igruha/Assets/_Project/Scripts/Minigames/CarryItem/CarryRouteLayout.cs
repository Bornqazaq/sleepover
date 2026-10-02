using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Metres. Shared by the authored roads and physical route acceptance.</summary>
    public static class CarryRouteLayout
    {
        public const float UpperEnd = 18.8f, UpperFlat = 6.2f, UpperHeight = 3.8f;
        public const float GalleryWidth = 4.2f, GalleryOutside = 25f;
        public const float GalleryEntrance = -18.5f, GalleryExit = 19.4f;
        public static Vector3[] Gallery(int side) => new[]
        {
            new Vector3(GalleryEntrance,0,side*10), new Vector3(GalleryEntrance,0,side*(GalleryOutside-3.2f)),
            new Vector3(-15.3f,0,side*GalleryOutside), new Vector3(16.2f,0,side*GalleryOutside),
            new Vector3(GalleryExit,0,side*(GalleryOutside-3.2f)), new Vector3(GalleryExit,0,side*10)
        };
        public static Vector3[] Upper => new[]
        {
            new Vector3(-UpperEnd,0,0),new Vector3(-UpperFlat,UpperHeight,0),
            new Vector3(UpperFlat,UpperHeight,0),new Vector3(UpperEnd,0,0)
        };
        public static float UpperY(float x) => UpperHeight*Mathf.Clamp01((UpperEnd-Mathf.Abs(x))/(UpperEnd-UpperFlat));
        public static Vector3[] Delivery(int route, int side)
        {
            var start=new Vector3(-20.88f,0,side*7.2f);var finish=new Vector3(19.59f,0,side*11);
            if(route==0) return new[]{start,new Vector3(-15.2f,0,side*5.04f),new Vector3(-3.2f,0,side*5.04f),
                new Vector3(-3.2f,0,side*1.35f),new Vector3(7.2f,0,side*1.35f),
                new Vector3(7.2f,0,side*5.04f),new Vector3(17.8f,0,side*5.04f),finish};
            if(route==1)return new[]{start,new Vector3(-21,0,0),Upper[0],Upper[1],Upper[2],Upper[3],new Vector3(21,0,0),finish};
            var middle=Gallery(side);var result=new Vector3[middle.Length+1];
            result[0]=start;System.Array.Copy(middle,0,result,1,middle.Length-1);result[result.Length-1]=finish;return result;
        }
        public static float Length(Vector3[] points)
        {float total=0;for(int i=1;i<points.Length;i++)total+=Vector3.Distance(points[i-1],points[i]);return total;}
    }
}
