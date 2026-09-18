using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static Igruha.EditorTools.InfectionQuarantineAssets;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Asymmetric ruined neighbourhood and small, legible storytelling clusters.</summary>
    internal static class InfectionRuinedCourtyard
    {
        internal static void Build(Transform root)
        {
            BuildOutline(root);
            BuildNeighbourhood(root);
            BuildDetails(root);
            BuildGroundShading(root);
        }

        private static void BuildOutline(Transform root)
        {
            var street = Group("Street", root);
            var asphalt = Material("Asphalt", new Color(.13f,.145f,.15f));
            var concrete = Material("StreetConcrete", new Color(.39f,.38f,.34f));
            Box("RoadBed",street,new Vector3(0,-.3f,0),new Vector3(150,.3f,150),asphalt);
            var points=InfectionCourtyardLayout.Load().boundary;
            for(int edge=0;edge<points.Length;edge++)
            {
                Vector3 a=points[edge].Position, b=points[(edge+1)%points.Length].Position;
                Vector3 direction=(b-a).normalized, outward=new Vector3(direction.z,0,-direction.x);
                float length=Vector3.Distance(a,b),yaw=-Mathf.Atan2(direction.z,direction.x)*Mathf.Rad2Deg;
                int count=Mathf.CeilToInt(length/4);
                for(int i=0;i<count;i++)
                {
                    var fence=Place("Fence",street,Vector3.Lerp(a,b,(i+.5f)/count),yaw);
                    fence.transform.localScale=new Vector3(length/count/4,1.22f,1);
                    var curb=Box("BrokenCurb",street,Vector3.Lerp(a,b,(i+.5f)/count)+outward*1.8f-Vector3.up*.02f,
                        new Vector3(length/count-.12f,.24f,.32f),concrete);
                    curb.transform.rotation=Quaternion.Euler(0,yaw+(i%3-1)*1.8f,0);
                }
                var walk=Box("Sidewalk",street,(a+b)*.5f+outward*.85f-Vector3.up*.09f,new Vector3(length, .12f,1.7f),concrete);
                walk.transform.rotation=Quaternion.Euler(0,yaw,0);
            }
            Combine(street);
        }

        private static void BuildNeighbourhood(Transform root)
        {
            var near=Group("NearRuins",root);
            Place("BrokenBlock1",near,new Vector3(-14,0,25),5,1.13f);
            Place("BrokenBlock2",near,new Vector3(-28,0,29),-38,.94f);
            Place("BrokenBlock2",near,new Vector3(3,0,28),-6,1.1f);
            Place("BrokenBlock1",near,new Vector3(22,0,24),-24,1.05f);
            Place("BrokenBlock1",near,new Vector3(-29,0,10),-84,.9f);
            Place("BrokenBlock2",near,new Vector3(-29,0,-11),-99,.70f);
            Place("BrokenBlock2",near,new Vector3(30,0,9),86,1);
            Place("BrokenBlock1",near,new Vector3(27,0,-13),110,.85f);
            Combine(near);
            var far=Group("DistantCity",root);
            Place("Apartment2",far,new Vector3(-20,0,47),12,.9f);
            Place("Apartment1",far,new Vector3(8,0,51),-11,1.25f);
            Place("Apartment2",far,new Vector3(37,0,43),-37,.8f);
            Place("Apartment1",far,new Vector3(-49,0,-6),-90,1.1f);
            Place("Apartment2",far,new Vector3(43,0,-39),140,.85f);
            Place("BrokenBlock1",far,new Vector3(-15,0,-36),176,.85f);
            Place("BrokenBlock2",far,new Vector3(9,0,-40),195,.85f);
            Combine(far);
            var traffic=Group("AbandonedTraffic",root);
            Place("Bus",traffic,new Vector3(20,0,20),121,1.2f);
            Place("Ambulance",traffic,new Vector3(-10,0,21),-65,1.22f);
            Place("Sedan",traffic,new Vector3(-23,0,-1),19,1.08f);
            Place("Sedan",traffic,new Vector3(13,0,-23),71,1.1f);
            Place("Sedan",traffic,new Vector3(-18,0,23),-25,.95f);
            Combine(traffic);
        }

        private static void BuildDetails(Transform root)
        {
            var detail=Group("CourtyardDetails",root);
            // Distinct clusters: abandoned checkpoint, crashed bus, fire escape and broken southern wall.
            Place("CheckpointGate",detail,new Vector3(0,0,19),-5);
            Place("Canopy",detail,new Vector3(-5,0,20),-14,1.1f);
            Place("Barricade",detail,new Vector3(.5f,0,18.7f),11);
            Place("Barricade",detail,new Vector3(3.4f,0,20),-25);
            Place("Sandbags",detail,new Vector3(-2,0,18.2f),-9,1.4f);
            Place("Dumpster",detail,new Vector3(-15,0,20.2f),13,1.15f);
            Place("TrashCan",detail,new Vector3(-12.4f,0,18.8f),30);
            Place("Tree",detail,new Vector3(-17.6f,0,16.2f),26,1.2f);
            Place("Tree",detail,new Vector3(14.2f,0,18.5f),-35,1.38f);
            Place("Tree",detail,new Vector3(-22.2f,0,-1),21,1.1f);
            Place("Tree",detail,new Vector3(21.3f,0,-9),-15,1.35f);
            Place("Tree",detail,new Vector3(-11,0,-19.8f),80,1.1f);
            Place("Tree",detail,new Vector3(10,0,-21.5f),-20,.9f);
            Place("StreetLamp",detail,new Vector3(-18.3f,0,14.8f),110);
            Place("StreetLamp",detail,new Vector3(17.5f,0,14.2f),-35);
            Place("StreetLamp",detail,new Vector3(-18.5f,0,-13),65);
            Place("CableFlags",detail,new Vector3(-.6f,4.5f,18.5f),-4,1.3f);
            Place("CableFlags",detail,new Vector3(-17,4,-7),75,1.7f);
            Place("Cart",detail,new Vector3(18.6f,0,15.6f),-31,1.25f);
            Place("Bicycle",detail,new Vector3(18,0,12.3f),28,1.2f);
            Place("TornTarp",detail,new Vector3(18.9f,.5f,5),-87,1.25f);
            Place("TornTarp",detail,new Vector3(-12.5f,.45f,16.8f),24,1.35f);
            Place("TornTarp",detail,new Vector3(-15.6f,.3f,-15.4f),-45,.9f);
            Place("BrokenWall",detail,new Vector3(-6,0,-20),-6,1.5f);
            Place("BrokenWall",detail,new Vector3(3,0,-21),12,1.2f);
            Place("BrokenWall",detail,new Vector3(-24,0,5),90,1.3f);
            Place("BrokenWall",detail,new Vector3(24,0,-7),-86,1.1f);
            var random=new System.Random(91826);
            Vector3[] heaps={new Vector3(-19,0,20),new Vector3(23,0,19),new Vector3(-22,0,10),
                new Vector3(23,0,-10),new Vector3(-5,0,-21),new Vector3(7,0,-21),new Vector3(-24,0,-9)};
            for(int i=0;i<heaps.Length;i++)
            {
                Place("RubblePile",detail,heaps[i],i*53,1+(i%3)*.35f);
                Place("Litter",detail,heaps[i]+new Vector3(1,0,-1),i*43,1.5f);
                Place("Weeds",detail,heaps[i]+new Vector3(-1,0,1),i*29,1.4f);
            }
            for(int i=0;i<6;i++)
            {
                Place("Barrel",detail,new Vector3(15.2f+(i%3)*.85f,0,16.3f+(i/3)*.85f),i*38,1.1f);
                Place("Pallet",detail,new Vector3(20.6f,.4f*i,13.2f),-24+i*3);
            }
            Place("Tires",detail,new Vector3(17.7f,0,15.1f),6,1.2f);
            Place("Tires",detail,new Vector3(-18.3f,0,-15.3f),15,1.4f);
            Place("Cart",detail,new Vector3(-17.3f,0,-15.5f),-62,1.15f);
            Place("Bench",detail,new Vector3(-9,0,-18.5f),176,1.1f);
            Place("Bicycle",detail,new Vector3(-7.2f,0,-18.4f),155,1.15f);
            // Walkable surface damage occupies margins and prop bases, rather than a uniform scatter.
            var points=InfectionCourtyardLayout.Load().boundary;
            for(int i=0;i<points.Length;i++)
            {
                Vector3 a=points[i].Position,b=points[(i+1)%points.Length].Position;
                var direction=(b-a).normalized;var inward=new Vector3(-direction.z,0,direction.x);
                for(int j=0;j<4;j++)
                {
                    Vector3 p=Vector3.Lerp(a,b,(j+.5f)/4)+inward*.9f;
                    Place("DirtIsland",detail,p,i*31+j*44,1.3f+(float)random.NextDouble());
                    Place("Weeds",detail,p,i*73+j*28,1.1f);
                    if(j%2==0)Place("Litter",detail,p+inward*.6f,i*56+j*31,.8f);
                }
            }
            foreach(var placement in InfectionCourtyardLayout.Load().props)
            {
                Place("DirtIsland",detail,new Vector3(placement.x+1,0,placement.z),placement.yaw,1.4f);
                Place("Weeds",detail,new Vector3(placement.x+2.9f,0,placement.z+1.3f),placement.yaw,.7f);
            }
            Place("Litter",detail,new Vector3(-4,0,6),19,1.2f);
            Place("Litter",detail,new Vector3(7,0,-8),-20,1.1f);
            Place("Bucket",detail,new Vector3(1.5f,.12f,-10.6f),-17,1.25f);
            Place("Bucket",detail,new Vector3(-2,.12f,-9.5f),47);
            var bear=Place("Teddy",detail,new Vector3(3.3f,.18f,-12.3f),-18,.7f);
            bear.transform.localRotation=Quaternion.Euler(90,-18,0);
            var fallenBike=Place("Bicycle",detail,new Vector3(7,.08f,-12),-25,1.3f);
            fallenBike.transform.localRotation=Quaternion.Euler(90,-25,0);
            // Reachable solid storytelling props receive their own simple collision outside the art batching.
            Solid("Bench",detail,root,new Vector3(-16,0,8.8f),-33,new Vector3(2.8f,1.25f,.95f),new Vector3(0,.625f,.15f));
            Solid("Sandbags",detail,root,new Vector3(16.4f,0,9.7f),-38,new Vector3(3.1f,.8f,.65f),new Vector3(0,.4f,0));
            Solid("Barrel",detail,root,new Vector3(-4.8f,0,15.5f),0,new Vector3(.9f,1.15f,.9f),new Vector3(0,.575f,0));
            Solid("Barrel",detail,root,new Vector3(-5.8f,0,15.2f),14,new Vector3(.9f,1.15f,.9f),new Vector3(0,.575f,0));
            Combine(detail);
            Sign(root,new Vector3(0,3.5f,18.87f),"QUARANTINE  /  13",-5,4.5f);
            Sign(root,new Vector3(-13.6f,1.85f,16.48f),"NO ENTRY",24,2.2f);
            Sign(root,new Vector3(18.75f,1.8f,2),"INFECTED ZONE",-87,2.7f);
        }

        private static void Solid(string model,Transform detail,Transform root,Vector3 position,float yaw,Vector3 size,Vector3 center)
        {
            Place(model,detail,position,yaw);
            var collision=Group("Collision_"+model,root);
            collision.position=position;collision.rotation=Quaternion.Euler(0,yaw,0);
            collision.gameObject.layer=LayerMask.NameToLayer("Ground");
            var box=collision.gameObject.AddComponent<BoxCollider>();box.size=size;box.center=center;
        }

        private static void Sign(Transform root,Vector3 position,string label,float yaw,float width)
        {
            var sign=Group("QuarantineNotice",root);sign.position=position;sign.rotation=Quaternion.Euler(0,yaw,0);
            Box("Board",sign,Vector3.zero,new Vector3(width,.56f,.05f),Material("SignIvory",new Color(.67f,.59f,.41f)));
            var text=Group("Letters",sign).gameObject.AddComponent<TMPro.TextMeshPro>();
            text.transform.localPosition=new Vector3(0,0,-.031f);text.transform.localRotation=Quaternion.Euler(0,180,0);
            text.text=label;text.fontSize=2.6f;text.color=new Color(.18f,.06f,.035f);text.alignment=TMPro.TextAlignmentOptions.Center;
            text.rectTransform.sizeDelta=new Vector2(width-.1f,.52f);
        }

        private static void BuildGroundShading(Transform root)
        {
            string path=Art+"/Materials/GroundShade.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(InfectionQuarantineEffects.PuffMaterial());AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_BaseColor",new Color(.02f,.027f,.03f,.52f));
            EditorUtility.SetDirty(material);
            foreach(var prop in InfectionCourtyardLayout.Load().props)
            {
                var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.name="ContactShade_"+prop.name.Replace('/','_');
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.SetParent(root,false);quad.transform.position=new Vector3(prop.x,.005f,prop.z);
                quad.transform.rotation=Quaternion.Euler(90,prop.yaw,0);quad.transform.localScale=new Vector3(9,8,1);
                var renderer=quad.GetComponent<Renderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
            }
        }
    }
}
