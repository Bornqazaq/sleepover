using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Igruha.Core.Traps;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    internal static partial class DuckHuntBarnArt
    {
        const string CarnivalRoot="Assets/_Project/Art/DuckHuntCarnival";
        static readonly string[] CarnivalModels={"Bear","Rabbit","Duck","CrateCover","LeverBase","LeverArm","Garland","Wheel","Cans","Balloons","PopcornCart","Marquee","Bulb","Booth","SnackBar","PrizeRack","TicketStand","MenuBoard","Stool"};
        static readonly string[] FloorColors={"Red","Blue","Purple","Gold","Red"};

        static void CarnivalPreflight()
        {
            foreach(string key in CarnivalModels)
            {
                EnsureReadableModel(CarnivalRoot+"/Models/DHC_"+key+".fbx");
                if(AssetDatabase.LoadAssetAtPath<GameObject>(CarnivalRoot+"/Models/DHC_"+key+".fbx")==null)
                    throw new InvalidOperationException("Export duck_hunt_carnival.py first: "+key);
            }
        }

        static void Retint(GameObject go,string from,string to)
        {
            foreach(var r in go.GetComponentsInChildren<MeshRenderer>())
                r.sharedMaterials=r.sharedMaterials.Select(m=>m==Mat(from)?Mat(to):m).ToArray();
        }

        static void StripedWall(Transform art,Vector3 origin,float width,float height,Quaternion rotation,int floor)
        {
            int n=Mathf.CeilToInt(width/.72f);
            for(int i=0;i<n;i++)
            {
                var piece=Model("Cladding",art,origin+rotation*new Vector3(-width/2+(i+.5f)*width/n,0,0),new Vector3(width/n,height,1),rotation);
                string color=i%2==0?FloorColors[Mathf.Clamp(floor,0,4)]:"Cream";
                Retint(piece,"Teal",color);Retint(piece,"TealLight",color);
            }
            for(int i=0;i<=Mathf.CeilToInt(width/5.76f);i++)
            {
                float x=Mathf.Min(width,i*5.76f)-width/2;
                Beam(art,origin+rotation*new Vector3(x,0,-.07f),origin+rotation*new Vector3(x,height,-.07f),.18f,"OakDark");
            }
            foreach(float h in new[]{.16f,height-.16f})
                Beam(art,origin+rotation*new Vector3(-width/2,h,-.045f),origin+rotation*new Vector3(width/2,h,-.045f),.14f,"OakLight");
        }

        static void CarnivalWall(MeshRenderer old,Transform art)
        {
            old.sharedMaterial=Mat("OakDark");Bounds b=old.bounds;bool alongX=b.size.x>b.size.z;
            StripedWall(art,alongX?new Vector3(b.center.x,b.min.y,b.min.z-.015f):new Vector3(b.min.x-.015f,b.min.y,b.center.z),
                alongX?b.size.x:b.size.z,b.size.y,alongX?Quaternion.identity:Quaternion.Euler(0,90,0),Mathf.RoundToInt(b.min.y/5.76f));
        }

        static void Garland(Transform art,Vector3 a,Vector3 b,float sagScale=1)
        {
            Vector3 axis=b-a;
            var rotation=Quaternion.LookRotation(Vector3.Cross(axis,Vector3.up).normalized,Vector3.up);
            Model("Garland",art,(a+b)*.5f,new Vector3(axis.magnitude/6,sagScale,1),rotation);
        }

        static void Marquee(Transform art,string text,Vector3 p,Vector2 size,float textSize)
        {
            Model("Marquee",art,p,new Vector3(size.x,size.y,1));
            Label(art,text,p+Vector3.back*.10f,textSize,"Cream");
            int nx=Mathf.CeilToInt(size.x/.40f),ny=Mathf.CeilToInt(size.y/.40f);
            for(int i=0;i<=nx;i++)foreach(int side in new[]{-1,1})
                Model("Bulb",art,p+new Vector3(-size.x*.445f+size.x*.89f*i/nx,side*size.y*.445f,-.11f),Vector3.one);
            for(int i=1;i<ny;i++)foreach(int side in new[]{-1,1})
                Model("Bulb",art,p+new Vector3(side*size.x*.445f,-size.y*.445f+size.y*.89f*i/ny,-.11f),Vector3.one);
        }

        static void CarnivalFloor(Transform floor,Transform art,int index)
        {
            float y=index*5.76f;
            for(int side=-1;side<=1;side+=2)
                StripedWall(art,new Vector3(side<0?-.31f:34.87f,y,5.04f),10.08f,index==4?6.5f:5.76f,Quaternion.Euler(0,side<0?90:-90,0),index);
            foreach(Transform slot in floor.Cast<Transform>().Where(t=>t.name.StartsWith("SingleCover_")).ToArray())
                Object.DestroyImmediate(slot.gameObject);
            FurnishPlayableFloor(floor,index);
            // Unequal, separated attractions, not a repeated wall of prizes.
            for(int bay=0;bay<2;bay++)
            {
                float x=9.6f+bay*13.6f+(index%2==0?0:1.2f);
                Solid(art,"Prize shelf",new Vector3(x,y+1.28f,9.70f),new Vector3(3.0f,.13f,.46f),"OakDark");
                Solid(art,"Shelf gilded edge",new Vector3(x,y+1.29f,9.44f),new Vector3(3.04f,.11f,.055f),"Brass");
                if((bay+index)%3==0)
                {
                    Model("Wheel",art,new Vector3(x,y+2.95f,9.72f),Vector3.one*1.18f);
                    Model("Cans",art,new Vector3(x-1.55f,y+1.36f,9.52f),Vector3.one*.80f);
                    Model("Duck",art,new Vector3(x+1.6f,y+1.36f,9.52f),Vector3.one*.70f);
                }
                else
                {
                    Solid(art,"Upper prize shelf",new Vector3(x,y+3.04f,9.70f),new Vector3(3.0f,.13f,.46f),"OakDark");
                    for(int row=0;row<2;row++)for(int i=0;i<3;i++)
                    {
                        string key=(i+row+index)%3==0?"Rabbit":((i+bay)%3==0?"Duck":"Bear");
                        var toy=Model(key,art,new Vector3(x-.9f+i*.9f,y+1.36f+row*1.76f,9.63f),Vector3.one*(row==0?.75f:.64f),Quaternion.Euler(0,(i-1)*9,0));
                        Retint(toy,key=="Rabbit"?"Blue":"Rose",new[]{"Rose","Blue","Gold","Purple","TealLight"}[(i+row+index)%5]);
                    }
                }
                Model("Lantern",art,new Vector3(x+2.55f,y+3.65f,9.2f),Vector3.one*.67f);
                PointLight(art,new Vector3(x,y+3.5f,6.5f),new Color(1,.69f,.32f),2.4f,8);
            }
            // Elevation trim, bulbs and cloth read from both roles, without a chest-height front barrier.
            Beam(art,new Vector3(0,y-.25f,-.08f),new Vector3(34.56f,y-.25f,-.08f),.37f,"OakDark");
            Beam(art,new Vector3(0,y-.11f,-.30f),new Vector3(34.56f,y-.11f,-.30f),.10f,"Brass");
            for(int segment=0;segment<6;segment++)
            {
                Garland(art,new Vector3(segment*5.76f,y+5.33f,.06f),new Vector3((segment+1)*5.76f,y+5.33f,.06f),.78f);
                if(segment>0&&segment<5)Garland(art,new Vector3(segment*5.76f,y+5.30f,.2f),new Vector3(segment*5.76f,y+5.30f,9.7f));
            }
            if(index<4)
            {
                float stairX=index%2==0?31.68f:2.88f;
                Label(art,(index+1).ToString("00"),new Vector3(stairX,y+3.1f,-.17f),1.6f,"Cream");
                Label(art,"NEXT FLOOR",new Vector3(stairX,y+1.84f,-.17f),.23f,"Cream");
            }
            foreach(var lever in floor.GetComponentsInChildren<TrapLever>())CarnivalControl(lever,index);
            if(index==4)
            {
                Marquee(art,"GRAND\nPRIZE",new Vector3(17.3f,y+4.05f,9.68f),new Vector2(7.8f,3.4f),1.4f);
                Model("Bear",art,new Vector3(32.7f,y+2.17f,9.5f),Vector3.one*1.35f);
                Marquee(art,"FINISH",new Vector3(32.4f,y+4.65f,7.97f),new Vector2(3.4f,1.08f),.36f);
                Label(art,"BACK TO START",new Vector3(17.28f,y+.035f,1.0f),.24f,"Brass",Quaternion.Euler(90,0,0));
            }
        }

        // These are actual pieces of a midway, not boxes with a cosmetic skin.
        // Progress follows the snake route; depth and facing deliberately vary.
        static void FurnishPlayableFloor(Transform floor,int index)
        {
            if(index==0)
            {
                Furnishing(floor,index,"TicketStand",11,3.05f,-12);
                Furnishing(floor,index,"PopcornCart",19,5.25f,16);
                Furnishing(floor,index,"MenuBoard",21.5f,4.60f,-19);
                Furnishing(floor,index,"PrizeRack",35,5.65f,11);
            }
            if(index==1)
            {
                Furnishing(floor,index,"PrizeRack",12,4.90f,-16);
                Furnishing(floor,index,"SnackBar",23,3.10f,9);
                Furnishing(floor,index,"TicketStand",30,5.30f,19);
            }
            if(index==2)
            {
                Furnishing(floor,index,"PopcornCart",18,3.20f,-11);
                Furnishing(floor,index,"PrizeRack",31,5.45f,18);
            }
            if(index==3)Furnishing(floor,index,"SnackBar",17,4.10f,-17);
        }

        static void PropBox(GameObject prop,string name,Vector3 centre,Vector3 size)
        {
            var body=new GameObject(name);body.transform.SetParent(prop.transform,false);
            body.layer=LayerMask.NameToLayer("Cover");
            var box=body.AddComponent<BoxCollider>();box.center=centre;box.size=size;
        }

        static void Furnishing(Transform floor,int index,string key,float progress,float depth,float yaw)
        {
            Vector3 p=new Vector3((index%2==0?progress:48-progress)*.72f,index*5.76f,depth);
            Quaternion facing=Quaternion.Euler(0,yaw,0);
            // Keep interactive solids outside the static architectural mesh combine.
            var prop=Model(key,floor,p,Vector3.one,facing);prop.name="Furnishing_"+key;
            if(key=="PopcornCart")
            {
                PropBox(prop,"Solid cart body",new Vector3(0,.71f,0),new Vector3(1.22f,.64f,.65f));
                PropBox(prop,"Counter",new Vector3(0,1.06f,0),new Vector3(1.30f,.10f,.73f));
                foreach(int s in new[]{-1,1})PropBox(prop,"Wheel",new Vector3(s*.66f,.30f,0),new Vector3(.08f,.58f,.58f));
            }
            else if(key=="SnackBar"||key=="TicketStand")
            {
                bool bar=key=="SnackBar";
                PropBox(prop,"Crouch cover body",new Vector3(0,.57f,0),new Vector3(bar?1.13f:1.08f,1.10f,bar?.57f:.67f));
                PropBox(prop,"Counter",new Vector3(0,bar?1.15f:1.17f,0),new Vector3(bar?1.34f:1.31f,.10f,bar?.79f:.84f));
                var stool=Model("Stool",floor,p+facing*new Vector3(.90f,0,.65f),Vector3.one,Quaternion.Euler(0,yaw+24,0));
                PropBox(stool,"Seat",new Vector3(0,.62f,0),new Vector3(.4f,.10f,.4f));
                PropBox(stool,"Legs",new Vector3(0,.30f,0),new Vector3(.30f,.60f,.30f));
            }
            else if(key=="PrizeRack")
            {
                PropBox(prop,"Solid back",new Vector3(0,.98f,.22f),new Vector3(1.02f,1.96f,.08f));
                PropBox(prop,"Plinth",new Vector3(0,.17f,0),new Vector3(1.02f,.34f,.57f));
                foreach(float h in new[]{.35f,.91f,1.47f})PropBox(prop,"Shelf",new Vector3(0,h,0),new Vector3(1.02f,.08f,.57f));
            }
            else PropBox(prop,"Menu panel",new Vector3(0,1,-.10f),new Vector3(.97f,1.76f,.10f));
            if(key=="TicketStand"||key=="PrizeRack")
                Model("Balloons",floor,p+facing*new Vector3(.48f,1.55f,.25f),Vector3.one*.57f,facing);
        }

        static void CarnivalControl(TrapLever lever,int floor)
        {
            Transform pedestal=lever.transform.Find("Pedestal"),handle=lever.transform.Find("Handle");
            Vector3 p=pedestal.position;p.y=floor*5.76f;
            // Small asymmetric placement, still beside the tested exposed central interaction station.
            Vector3 shift=floor==2?new Vector3(-.42f,0,.15f):new Vector3(.30f,0,-.25f);
            p+=shift;pedestal.position=p+Vector3.up*.39f;pedestal.localScale=new Vector3(.62f,.78f,.59f);
            pedestal.GetComponent<Renderer>().enabled=false;
            Quaternion orientation=Quaternion.Euler(0,floor==2?-18:23,0);
            Model("LeverBase",lever.transform,p,Vector3.one,orientation);
            handle.position=p+Vector3.up*.78f;
            // The rod swings about its cross axle (X), not its grip centre.
            var ls=new SerializedObject(lever);ls.FindProperty("readyEuler").vector3Value=new Vector3(-32,floor==2?-18:23,0);
            ls.FindProperty("firedEuler").vector3Value=new Vector3(38,floor==2?-18:23,0);ls.FindProperty("flipDuration").floatValue=.32f;ls.ApplyModifiedPropertiesWithoutUndo();
            handle.localRotation=Quaternion.Euler(ls.FindProperty("readyEuler").vector3Value);
            handle.Find("Bar").GetComponent<Renderer>().enabled=false;
            Model("LeverArm",handle,handle.position,Vector3.one,handle.rotation);
            var lamp=Solid(lever.transform,"Ready lamp",p+orientation*new Vector3(0,.57f,-.165f),Vector3.one*.075f,"Glow");
            var bs=new SerializedObject(lever.GetComponent<TrapActivationButton>());bs.FindProperty("indicator").objectReferenceValue=lamp.GetComponent<Renderer>();bs.ApplyModifiedPropertiesWithoutUndo();
        }

        static void CarnivalLift(Transform shaft)
        {
            DressLift(shaft);
            Transform decor=Group(shaft,"MidwayLiftTrim");
            for(int side=-1;side<=1;side+=2)
            {
                float x=17.28f+side*2.0f;
                Beam(decor,new Vector3(x,-2,-14.65f),new Vector3(x,35,-14.65f),.24f,"Red");
                for(int j=0;j<9;j++)
                {
                    float y=-1+j*4;
                    Beam(decor,new Vector3(15.3f,y,-14.63f),new Vector3(19.25f,y+4,-14.63f),.15f,side<0?"Cream":"Red");
                    Beam(decor,new Vector3(19.25f,y,-14.65f),new Vector3(15.3f,y+4,-14.65f),.15f,"Cream");
                }
            }
            Transform platform=shaft.Find("Platform");var canopy=Group(platform,"StripedHunterCanopy");Vector3 p=platform.position;
            // Above head/camera; never across the aim ray.
            for(int i=0;i<10;i++)Solid(canopy,"Awning stripe",p+new Vector3(-1.8f+i*.4f,3.0f,-.1f),new Vector3(.395f,.13f,3.7f),i%2==0?"Red":"Cream");
            Garland(canopy,p+new Vector3(-2,2.95f,1.77f),p+new Vector3(2,2.95f,1.77f),.65f);
            foreach(int side in new[]{-1,1})Beam(canopy,p+new Vector3(side*1.75f,.1f,-1.7f),p+new Vector3(side*1.75f,3,-1.7f),.105f,"Brass");
            Combine(decor,"CarnivalGantry");Combine(canopy,"HunterCanopy");
        }

        static void CarnivalRoof(Transform art)
        {
            for(int side=-1;side<=1;side+=2)
            {
                Vector3 a=new Vector3(17.28f,30.12f,5.04f+side*5.95f),b=new Vector3(17.28f,32.15f,5.04f);
                var roof=Solid(art,"Tin canopy",(a+b)/2,new Vector3(36.2f,.17f,Vector3.Distance(a,b)),"Teal");roof.transform.rotation=Quaternion.LookRotation(b-a,Vector3.up);
                Beam(art,a-Vector3.right*18.1f,a+Vector3.right*18.1f,.25f,"Brass");
            }
            Marquee(art,"DUCK HUNT",new Vector3(17.28f,30.9f,-1.1f),new Vector2(12,2.0f),1.4f);
            Marquee(art,"D\nU\nC\nK\n\nH\nU\nN\nT",new Vector3(35.27f,17,-.50f),new Vector2(2.1f,19.6f),1.6f);
            for(int i=0;i<=6;i++)Model("Timber",art,new Vector3(i*5.76f,-2.2f,.12f),new Vector3(2,1.5f,2));
            for(int i=0;i<6;i++)Garland(art,new Vector3(i*5.76f,30,-.45f),new Vector3((i+1)*5.76f,30,-.45f),.65f);
        }

        static void CarnivalGrounds(Transform art)
        {
            Model("Booth",art,new Vector3(10,-3,-17),Vector3.one*1.8f,Quaternion.Euler(0,-12,0));
            Model("PopcornCart",art,new Vector3(29,-3,-14),Vector3.one*1.5f,Quaternion.Euler(0,-30,0));
            Model("Balloons",art,new Vector3(29,-1.2f,-14),Vector3.one*1.3f);
            // Distant wheel is scenery behind the arena, not a new walkable route.
            Vector3 centre=new Vector3(-16,11,22);const float radius=12;
            for(int i=0;i<24;i++)
            {
                float a=i*Mathf.PI/12,b=(i+1)*Mathf.PI/12;
                Vector3 v=centre+new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,0),w=centre+new Vector3(Mathf.Cos(b)*radius,Mathf.Sin(b)*radius,0);
                Beam(art,v,w,.18f,"Cream");if(i%2==0){Beam(art,centre,v,.07f,"Brass");Solid(art,"Gondola",v-Vector3.up*.75f,new Vector3(1.25f,.8f,1.05f),i%4==0?"Red":"Blue");}
            }
            foreach(int side in new[]{-1,1})Beam(art,new Vector3(-16+side*6,-3,22),centre,.35f,"OakDark");
            for(int i=0;i<5;i++)
            {
                Vector3 p=new Vector3(-11+i*12,-3,30);
                Model("Booth",art,p,Vector3.one*2,Quaternion.identity);
                if(i<4)Garland(art,p+new Vector3(0,5,0),p+new Vector3(12,5,0),2);
            }
        }

        static void CarnivalGrade(Transform art)
        {
            EnsureFolder(CarnivalRoot+"/Materials");
            string path=CarnivalRoot+"/Materials/MidwayAtmosphere.asset";
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if(profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,path);}
            GradeEffect<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var grade=GradeEffect<ColorAdjustments>(profile);grade.postExposure.Override(.10f);grade.contrast.Override(5);grade.saturation.Override(-4);
            var bloom=GradeEffect<Bloom>(profile);bloom.threshold.Override(.9f);bloom.intensity.Override(.28f);bloom.scatter.Override(.62f);
            var volume=Group(art,"Midway atmosphere").gameObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=5;volume.sharedProfile=profile;
            foreach(var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                var data=camera.GetComponent<UniversalAdditionalCameraData>()??camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;data.antialiasingQuality=AntialiasingQuality.High;camera.allowHDR=true;
            }
            EditorUtility.SetDirty(profile);
        }

        static T GradeEffect<T>(VolumeProfile profile) where T:VolumeComponent
        {
            if(profile.TryGet<T>(out var effect))return effect;
            effect=profile.Add<T>(true);AssetDatabase.AddObjectToAsset(effect,profile);return effect;
        }
    }
}
