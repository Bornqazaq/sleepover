using System.Collections.Generic;
using UnityEngine;
using Igruha.Minigames.DuckHunt;

namespace Igruha.EditorTools
{
    internal static partial class DuckHuntBarnArt
    {
        static void CraftedHunterLift(Transform shaft)
        {
            Transform platform=shaft.Find("Platform"), moving=Group(platform,"CopperwoodLift");
            DressDeck(platform.Find("Deck").GetComponent<MeshRenderer>(),moving,true);
            Vector3 p=platform.position;
            // Steel brackets transfer the entire deck into the rear guide carriage.
            foreach(int side in new[]{-1,1})
            {
                float x=p.x+side*1.15f;
                Beam(moving,new Vector3(x,p.y-.23f,p.z+1.25f),new Vector3(x,p.y-.23f,-14.65f),.22f,"Iron");
                Beam(moving,new Vector3(x,p.y-.23f,p.z+1.25f),new Vector3(x,p.y-1.15f,-14.65f),.18f,"Brass");
                Beam(moving,new Vector3(p.x+side*1.8f,p.y-1.15f,-14.65f),new Vector3(p.x+side*1.8f,p.y+2.65f,-14.65f),.22f,"Iron");
                foreach(float y in new[]{-.70f,1.5f})
                {
                    Round(moving,"Guide roller",new Vector3(side*1.8f,y,-3.08f),new Vector3(.48f,.10f,.48f),"Brass",Quaternion.Euler(90,0,0));
                    Solid(moving,"Roller bearing",new Vector3(p.x+side*1.8f,p.y+y,-14.82f),new Vector3(.18f,.18f,.18f),"Iron");
                }
                Beam(moving,new Vector3(x,p.y-.12f,p.z-1.25f),new Vector3(p.x+side*1.8f,p.y+.65f,-14.65f),.18f,"Brass");
            }
            foreach(float y in new[]{-1.1f,.65f,2.6f})Beam(moving,new Vector3(p.x-1.8f,p.y+y,-14.65f),new Vector3(p.x+1.8f,p.y+y,-14.65f),.20f,"Iron");
            foreach(int side in new[]{-1,1})Beam(moving,new Vector3(p.x+side*1.1f,p.y+2.6f,-14.65f),new Vector3(p.x+side*1.1f,p.y+2.6f,-14.08f),.13f,"Brass");
            Solid(moving,"Lift motor housing",p+new Vector3(0,.3f,-2.5f),new Vector3(.86f,.62f,.48f),"Teal");
            foreach(int side in new[]{-1,1})
            {
                Beam(moving,p+new Vector3(side*1.25f,.08f,-1.25f),p+new Vector3(side*1.25f,2.75f,-1.25f),.09f,"Brass");
                Beam(moving,p+new Vector3(side*1.25f,2.75f,-1.25f),p+new Vector3(side*.93f,2.75f,-1.25f),.07f,"Brass");
                Model("Lantern",moving,p+new Vector3(side*.93f,2.50f,-1.25f),Vector3.one*.8f);
                PointLight(moving,p+new Vector3(side*.93f,2.35f,-1.05f),new Color(1,.77f,.45f),14,6);
            }
            Spot(moving,"Hunter working light",new Vector3(0,2.78f,-.45f),Quaternion.Euler(86,0,0),new Color(1,.83f,.60f),20,6,95);
            Combine(moving,"MovingLift");

            var frame=Group(shaft,"CopperwoodGantry");
            foreach(int side in new[]{-1,1})
            {
                float x=p.x+side*1.8f;
                Solid(frame,"Gantry concrete footing",new Vector3(x,-2.85f,-16.4f),new Vector3(1.2f,.5f,5.5f),"Stone");
                Beam(frame,new Vector3(x,-2.6f,-14.65f),new Vector3(x,38.7f,-14.65f),.27f,"Red");
                Beam(frame,new Vector3(x,-2.6f,-19),new Vector3(x,38.7f,-19),.27f,"Red");
                Beam(frame,new Vector3(x,38,-14.65f),new Vector3(x,38,-19),.25f,"Iron");
                Beam(frame,new Vector3(p.x+side*1.5f,-2.6f,-18.12f),new Vector3(p.x+side*1.5f,37.7f,-18.12f),.13f,"Iron");
                for(float y=-2.4f;y<37;y+=4)
                {
                    Beam(frame,new Vector3(x,y,-14.65f),new Vector3(x,y+4,-19),.12f,"Cream");
                    Beam(frame,new Vector3(x,y,-19),new Vector3(x,y+4,-14.65f),.12f,"Cream");
                }
            }
            // Cross-bracing is behind the camera; guide surfaces touch the moving rollers.
            foreach(float z in new[]{-14.65f,-17.6f})
                Beam(frame,new Vector3(p.x-2.3f,38,z),new Vector3(p.x+2.3f,38,z),.36f,"Iron");
            for(float y=-2.4f;y<37;y+=4)
            {
                Beam(frame,new Vector3(p.x-1.8f,y,-19),new Vector3(p.x+1.8f,y+4,-19),.13f,"Cream");
                Beam(frame,new Vector3(p.x+1.8f,y,-19.15f),new Vector3(p.x-1.8f,y+4,-19.15f),.13f,"Brass");
            }
            var pulleys=new List<Transform>();
            foreach(int side in new[]{-1,1})
            {
                float x=p.x+side*1.1f;
                foreach(float z in new[]{-14.6f,-17.6f})
                {
                    var pulley=Group(shaft,"Lift sheave");pulley.position=new Vector3(x,38,z);
                    Round(pulley,"Grooved wheel",Vector3.zero,new Vector3(1.04f,.12f,1.04f),"Brass",Quaternion.Euler(0,0,90));
                    Round(pulley,"Sheave hub",Vector3.zero,new Vector3(.26f,.22f,.26f),"Iron",Quaternion.Euler(0,0,90));
                    for(int j=0;j<6;j++)
                    {
                        float a=j*Mathf.PI/3;Beam(pulley,pulley.position,pulley.position+new Vector3(0,Mathf.Sin(a)*.45f,Mathf.Cos(a)*.45f),.07f,"Iron");
                    }
                    Combine(pulley,"LiftSheave_"+side+"_"+Mathf.RoundToInt(z*10));pulleys.Add(pulley);
                }
                Beam(frame,new Vector3(x,38.52f,-14.6f),new Vector3(x,38.52f,-17.6f),.035f,"Iron");
                for(int j=0;j<12;j++)
                {
                    float a=j*Mathf.PI/24,b=(j+1)*Mathf.PI/24;
                    Beam(frame,new Vector3(x,38+Mathf.Sin(a)*.52f,-14.6f+Mathf.Cos(a)*.52f),new Vector3(x,38+Mathf.Sin(b)*.52f,-14.6f+Mathf.Cos(b)*.52f),.035f,"Iron");
                    Beam(frame,new Vector3(x,38+Mathf.Sin(a)*.52f,-17.6f-Mathf.Cos(a)*.52f),new Vector3(x,38+Mathf.Sin(b)*.52f,-17.6f-Mathf.Cos(b)*.52f),.035f,"Iron");
                }
            }
            Combine(frame,"Gantry");
            var weight=Group(shaft,"LiftCounterweight");weight.position=new Vector3(p.x,34.12f,-18.12f);
            Solid(weight,"Counterweight steel cage",weight.position,new Vector3(2.7f,1.8f,.6f),"Iron");
            for(int i=0;i<6;i++)Solid(weight,"Counterweight blocks",weight.position+new Vector3(0,-.73f+i*.29f,-.33f),new Vector3(2.25f,.22f,.12f),"Stone");
            foreach(int side in new[]{-1,1})Round(weight,"Counterweight guide roller",new Vector3(side*1.5f,0,0),new Vector3(.28f,.07f,.28f),"Brass",Quaternion.Euler(90,0,0));
            Combine(weight,"LiftCounterweight");
            var suspension=new List<Transform>();var returns=new List<Transform>();
            for(int i=0;i<2;i++)
            {
                suspension.Add(Solid(shaft,"Lift suspension cable",Vector3.zero,Vector3.one,"Iron").transform);
                returns.Add(Solid(shaft,"Lift return cable",Vector3.zero,Vector3.one,"Iron").transform);
            }
            shaft.gameObject.AddComponent<DuckHuntLiftRig>().Configure(platform,weight,suspension.ToArray(),returns.ToArray(),pulleys.ToArray(),-1.44f,33.12f);
        }
    }
}
