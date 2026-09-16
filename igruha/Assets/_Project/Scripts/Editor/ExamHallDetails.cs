using UnityEditor;
using UnityEngine;
using Igruha.Minigames.Exam;

namespace Igruha.EditorTools
{
    internal static class ExamHallDetails
    {
        internal static void Apply(Transform art)
        {
            Prop(art,"Globe",new Vector3(-6.15f,0,6.55f),-18,1.12f,new Vector3(1.9f,3.25f,1.9f));
            Prop(art,"CuriosityCabinet",new Vector3(7.8f,0,8.15f),-12,.85f,new Vector3(2.2f,2.85f,.9f));
            Prop(art,"BookTower",new Vector3(6.1f,0,6.2f),-12,1.05f,new Vector3(.85f,1.13f,.7f));
            var owl=Prop(art,"OwlPrize",new Vector3(6.1f,1.15f,6.2f),-12,1.15f);
            ExamHallBuilder.Label(owl,"PrizeCaption","ЗА ПОПЫТКУ",new Vector3(0,.15f,-.34f),.68f,.13f,.065f,new Color(.93f,.84f,.60f));
            Prop(art,"AstronomyMap",new Vector3(-7.05f,0,-3.55f),-35,1.12f,new Vector3(2.50f,3.1f,.95f));
            Prop(art,"CoatStand",new Vector3(-4.75f,0,-8.85f),0,1.1f,new Vector3(1.65f,2.5f,.8f));
            Prop(art,"AstronomyMap",new Vector3(4.5f,0,-8.9f),165,.9f,new Vector3(2.1f,2.5f,.8f));
            Prop(art,"FloorCompass",new Vector3(0,0,-3.65f),0,1.12f);
            for(int side=-1;side<=1;side+=2)
            {
                // Low, clustered foreground islands flank an unobstructed six-metre approach.
                var desk=Prop(art,"SchoolDesk",new Vector3(side*5.65f,0,-3.15f),side*18,1.1f,new Vector3(1.7f,1.15f,1.75f));
                Prop(desk,"BookTower",new Vector3(-.39f,1.02f,.05f),-14,.68f);
                Prop(desk,"Abacus",new Vector3(.34f,1.02f,.22f),0,.65f);
                Prop(desk,"Stationery",new Vector3(.31f,1.04f,-.2f),0,.8f);
                Prop(art,"BookTower",new Vector3(side*6.1f,0,-1.95f),side*12,1.05f,new Vector3(.85f,1.2f,.75f));
                Prop(art,"BookTower",new Vector3(side*5.25f,0,-3.95f),-side*8,.70f,new Vector3(.65f,.83f,.53f));
                for(int row=0;row<3;row++)
                {
                    Prop(art,"Stationery",new Vector3(side*8.25f,1.015f,4.15f-row*2.05f),180,1);
                    if(row%2==0)Prop(art,"Abacus",new Vector3(side*8.55f,1.015f,4.45f-row*2.05f),180,.7f);
                }
            }
        }
        private static Transform Prop(Transform parent,string model,Vector3 pos,float yaw,float scale,Vector3 size=default(Vector3))
        {
            var t=ExamHallAssets.Place(parent,model,pos,yaw);t.localScale=Vector3.one*scale;
            if(size!=Vector3.zero)
            {
                var c=new GameObject("Collision");c.transform.SetParent(t,false);c.layer=LayerMask.NameToLayer("Cover");
                var box=c.AddComponent<BoxCollider>();box.size=size/scale;box.center=new Vector3(0,size.y/scale*.5f,0);
            }
            return t;
        }
        internal static void Mechanism(Transform platform,string side)
        {
            var previous=platform.GetComponent<ExamHatchMechanism>();if(previous!=null)Object.DestroyImmediate(previous);
            for(int i=platform.childCount-1;i>=0;i--)
            {
                var child=platform.GetChild(i);
                if(child.name=="EH_PistonBarrel"||child.name=="EH_PistonRod"||child.name=="EH_Gear"||child.name=="EH_HatchBeam")Object.DestroyImmediate(child.gameObject);
            }
            var mechanism=platform.gameObject.AddComponent<ExamHatchMechanism>();
            mechanism.LeftPivot=platform.Find("DoorLeft");mechanism.RightPivot=platform.Find("DoorRight");
            var rams=new System.Collections.Generic.List<ExamHatchMechanism.Ram>();
            for(int s=-1;s<=1;s+=2)
            {
                var pivot=s<0?mechanism.LeftPivot:mechanism.RightPivot;
                var visual=new GameObject("LeafVisualMotion").transform;visual.SetParent(pivot,false);
                pivot.Find("EH_DoorLeaf"+side).SetParent(visual,false);
                if(s<0)mechanism.LeftVisual=visual;else mechanism.RightVisual=visual;
                for(int end=-1;end<=1;end+=2)
                {
                    var barrel=ExamHallAssets.Place(platform,"PistonBarrel",new Vector3(s*3.05f,-1.8f,end*1.4f));
                    var rod=ExamHallAssets.Place(platform,"PistonRod",Vector3.zero);
                    var gear=ExamHallAssets.Place(platform,"Gear",new Vector3(s*2.88f,-.12f,end*2.29f));
                    rams.Add(new ExamHatchMechanism.Ram{Leaf=visual,Barrel=barrel,Rod=rod,Gear=gear,
                        Anchor=new Vector3(s*3.12f,-1.8f,end*1.4f),Attachment=new Vector3(-s*.94f,-.24f,end*1.4f)});
                }
                ExamHallAssets.Place(platform,"HatchBeam",new Vector3(0,0,s*2.24f),s<0?0:180);
            }
            mechanism.Rams=rams.ToArray();
            // Pose the rams immediately in edit mode as well.
            foreach(var ram in mechanism.Rams)
            {
                var start=platform.TransformPoint(ram.Anchor);var end=ram.Leaf.TransformPoint(ram.Attachment);var delta=end-start;
                var rotation=Quaternion.FromToRotation(Vector3.up,delta.normalized);
                ram.Barrel.SetPositionAndRotation(start,rotation);ram.Rod.SetPositionAndRotation(start+delta.normalized*.62f,rotation);
                ram.Rod.localScale=new Vector3(1,delta.magnitude-.62f,1);
            }
        }
    }
}
