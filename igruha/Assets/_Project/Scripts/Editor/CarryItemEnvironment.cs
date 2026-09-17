using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Igruha.Minigames.CarryItem;

namespace Igruha.EditorTools
{
    /// <summary>Authored high-rise composition. All dimensions here are metres.</summary>
    internal static class CarryItemEnvironment
    {
        private const float HalfWidth = 14.4f;
        private const float End = 27.36f;
        private const float FloorHeight = 5.4f;
        internal static void Build(Transform arena, CarryItemConfig config, System.Random rng)
        {
            var root = Group(arena, "Environment");
            var structure = Group(root, "Structure");
            var props = Group(root, "WorkAreas");
            var backdrop = Group(root, "Horizon");
            BuildStructure(structure);
            BuildProps(props);
            BuildDepth(backdrop);
            BuildSkyline(backdrop);
            BuildLight();
        }
        internal static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
        }
        internal static Transform Place(Transform parent, string model, Vector3 pos, float yaw=0, bool solid=true)
        {
            var t = CarrySkyscraperAssets.Place(parent,model,pos,yaw);
            if(solid) AddSolid(t,model);
            return t;
        }
        private static void AddSolid(Transform t,string model)
        {
            var b=CarryItemDress.BoundsOf(t.gameObject);
            var c=t.gameObject.AddComponent<BoxCollider>();
            // Smooth envelopes avoid catching fingers, scaffold braces and handles.
            c.center=t.InverseTransformPoint(b.center);
            var size=b.size;
            if(Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y,90))<1 || Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y,270))<1)
                size=new Vector3(size.z,size.y,size.x);
            c.size=size;
            if(model=="Scaffold") { c.center=new Vector3(0,3.7f,0);c.size=new Vector3(3.12f,7.4f,1.3f); }
            if(model=="Column") { c.center=new Vector3(0,3.25f,0);c.size=new Vector3(.74f,6.5f,.74f); }
            if(model=="Guardrail") {c.center=new Vector3(0,.62f,0);c.size=new Vector3(3,1.24f,.24f);}
            foreach(var tr in t.GetComponentsInChildren<Transform>())tr.gameObject.layer=LayerMask.NameToLayer("Cover");
        }
        private static void BuildStructure(Transform root)
        {
            // Columns sit outside the circulation corridors and leave an open view between bays.
            foreach(float x in new[]{-25.8f,-16.0f,-3.6f,7.5f,18.0f,25.8f})
            foreach(float z in new[]{-13.5f,13.5f}) Place(root,"Column",new Vector3(x,0,z));
            // A roof over the start and strips along the sides frame the sky; no low ceiling.
            foreach(float x in new[]{-25.3f,-21.3f,-17.3f})
            for(int z=0;z<7;z++) Slab(root,new Vector3(x,7.0f,-12+z*4),new Vector3(1,1,1),true);
            foreach(float x in new[]{-2.8f,1.2f,5.2f,19.8f,23.8f})
            foreach(float z in new[]{-12.2f,12.2f}) Slab(root,new Vector3(x,7.0f,z),Vector3.one,true);
            // Short unfinished walls, rather than a continuous opaque enclosure.
            foreach(float z in new[]{-12.7f,12.7f})
            {
                Place(root,"Wall",new Vector3(-23,0,z),z>0?0:180);
                Place(root,"Wall",new Vector3(22.5f,0,z),z>0?0:180);
            }
            // Continuous rail on the outer edge, with visible physical barrier only where shown.
            for(float x=-25.5f;x<27;x+=3)
            foreach(float z in new[]{-HalfWidth,HalfWidth}) Place(root,"Guardrail",new Vector3(x,0,z),z>0?0:180);
            for(float z=-12.9f;z<14;z+=3)
            foreach(float x in new[]{-End,End}) Place(root,"Guardrail",new Vector3(x,0,z),90);
        }
        private static void Slab(Transform root, Vector3 pos, Vector3 scale,bool solid)
        {
            var t=Place(root,"Slab",pos,0,false);t.localScale=scale;
            if(solid) {var c=t.gameObject.AddComponent<BoxCollider>();c.center=new Vector3(0,-.36f,0);c.size=new Vector3(4,.72f,4);t.gameObject.layer=LayerMask.NameToLayer("Ground");}
        }
        private static void BuildDepth(Transform root)
        {
            // Six visible levels with the same two service voids; no collider below the kill volumes.
            for(int level=1;level<=6;level++)
            {
                float y=-level*FloorHeight;
                foreach(float x in new[]{-25.2f,-21.2f,-17.2f,-3.0f,1f,5f,19.5f,23.5f})
                for(int iz=0;iz<7;iz++)Slab(root,new Vector3(x,y,-12+iz*4),Vector3.one,false);
                foreach(float x in new[]{-25.8f,-15.4f,-4.6f,8.8f,17.2f,25.8f})
                foreach(float z in new[]{-13.5f,-7.5f,0,7.5f,13.5f})
                {
                    var t=Place(root,"Column",new Vector3(x,y,z),0,false);t.localScale=new Vector3(1,FloorHeight/6.5f,1);
                }
                foreach(float x in new[]{-14.7f,8.5f})
                foreach(float z in new[]{-10.8f,10.8f}) Place(root,"Scaffold",new Vector3(x,y,z),90,false).localScale=Vector3.one*.7f;
            }
        }
        private static void BuildProps(Transform root)
        {
            foreach(int sign in new[]{-1,1})
            {
                float z=sign*10.7f;
                Place(root,"Scaffold",new Vector3(-15.4f,0,z),sign>0?0:180);
                Place(root,"Scaffold",new Vector3(7.4f,0,z),sign>0?0:180);
                Place(root,"Lumber",new Vector3(-23f,0,z),90);
                Place(root,"CementBags",new Vector3(-18.5f,0,z));
                Place(root,"CableReel",new Vector3(19.0f,0,z));
                Place(root,"Workbench",new Vector3(23.3f,0,sign*10.5f),90);
                Place(root,"BrickStack",new Vector3(6.8f,0,sign*8.1f));
                Place(root,"Bucket",new Vector3(-22.3f,0,sign*8.7f));
                Place(root,"Cone",new Vector3(-14.5f,0,sign*8.2f));
                Place(root,"Barricade",new Vector3(-14.35f,0,sign*11.5f),90);
                Place(root,"Barricade",new Vector3(9.0f,0,sign*8f),90);
                Place(root,"Puddle",new Vector3(-16.2f,.012f,sign*7.7f),30,false).localScale=new Vector3(1.35f,1,1.6f);
                Place(root,"Puddle",new Vector3(20,.012f,sign*3.0f),-30,false);
                Place(root,"CableCoil",new Vector3(24.4f,0,sign*8.7f),0,false);
            }
            Place(root,"SiteCabin",new Vector3(-24.4f,0,0),90);
            Place(root,"Generator",new Vector3(22.3f,0,-11.1f),15);
            Place(root,"Mixer",new Vector3(-18.2f,0,12.25f),-30);
            Place(root,"Wheelbarrow",new Vector3(-20,0,-8.7f),115);
            Place(root,"Puddle",new Vector3(-24,0,8.3f),0,false).localScale=new Vector3(1.4f,1,1.3f);
        }
        private static void BuildSkyline(Transform root)
        {
            Place(root,"Crane",new Vector3(18,-2,-25),-35,false);
            Place(root,"Crane",new Vector3(-28,-8,33),50,false).localScale=Vector3.one*1.25f;
            Place(root,"Crane",new Vector3(53,-16,27),145,false).localScale=Vector3.one*1.6f;
            var rng=new System.Random(535);
            for(int i=0;i<40;i++)
            {
                float a=i*Mathf.PI*2/40, radius=105+(float)rng.NextDouble()*90;
                float height=.55f+(float)rng.NextDouble()*1.6f;
                var t=Place(root,i%5==0?"CityFrame":"CityTower",new Vector3(Mathf.Cos(a)*radius,-55,Mathf.Sin(a)*radius),i*37,false);
                t.localScale=new Vector3(.7f+(float)rng.NextDouble()*.8f,height,.7f+(float)rng.NextDouble()*.8f);
            }
        }
        private static void BuildLight()
        {
            Light sun=null;
            foreach(var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(l.type==LightType.Directional && l.name!="CarryItemSkyFill"){sun=l;break;}
            if(sun==null){sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;}
            sun.color=new Color(1,.96f,.88f);sun.intensity=1.65f;sun.shadows=LightShadows.Soft;
            sun.shadowStrength=1;sun.shadowBias=.035f;sun.shadowNormalBias=.16f;
            sun.transform.rotation=Quaternion.Euler(44,-38,0);
            RenderSettings.sun=sun;RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.65f,.72f,.80f);
            RenderSettings.ambientEquatorColor=new Color(.48f,.54f,.61f);
            RenderSettings.ambientGroundColor=new Color(.30f,.32f,.36f);
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;
            RenderSettings.fogColor=new Color(.57f,.73f,.85f);RenderSettings.fogStartDistance=45;RenderSettings.fogEndDistance=225;
            string skyPath=CarrySkyscraperAssets.Materials+"/CS_Sky.mat";
            var sky=AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if(sky==null){sky=new Material(Shader.Find("Skybox/Procedural"));AssetDatabase.CreateAsset(sky,skyPath);}
            sky.SetColor("_SkyTint",new Color(.5f,.5f,.5f));sky.SetColor("_GroundColor",new Color(.43f,.53f,.62f));
            sky.SetFloat("_Exposure",1.3f);sky.SetFloat("_AtmosphereThickness",1f);sky.SetFloat("_SunSize",.025f);
            RenderSettings.skybox=sky;EditorUtility.SetDirty(sky);
            var lighting=GameObject.Find("_Lighting");
            var fillObject=GameObject.Find("CarryItemSkyFill");
            if(fillObject==null)fillObject=new GameObject("CarryItemSkyFill");
            fillObject.transform.SetParent(lighting.transform,false);
            var fill=fillObject.GetComponent<Light>();
            if(fill==null)fill=fillObject.AddComponent<Light>();
            fill.type=LightType.Directional;fill.color=new Color(.70f,.82f,1f);
            fill.intensity=.55f;fill.shadows=LightShadows.None;
            fill.transform.rotation=Quaternion.Euler(25,142,0);
            foreach(var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(v.gameObject.scene==UnityEngine.SceneManagement.SceneManager.GetActiveScene())Object.DestroyImmediate(v.gameObject);
            var vol=new GameObject("CarryItemDaylight").AddComponent<Volume>();vol.transform.SetParent(lighting.transform,false);vol.isGlobal=true;
            string path=CarrySkyscraperAssets.Materials+"/CS_Daylight.asset";
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if(profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,path);}
            if(!profile.TryGet<Tonemapping>(out var tone))tone=profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            if(!profile.TryGet<ColorAdjustments>(out var color))color=profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(.05f);color.contrast.Override(8);color.saturation.Override(-3);
            foreach(var component in profile.components)
                if(!AssetDatabase.Contains(component))AssetDatabase.AddObjectToAsset(component,profile);
            vol.sharedProfile=profile;EditorUtility.SetDirty(profile);
            foreach(var camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                if(camera.gameObject.scene!=UnityEngine.SceneManagement.SceneManager.GetActiveScene())continue;
                var data=camera.GetComponent<UniversalAdditionalCameraData>();
                if(data==null)data=camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing=true; data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }
        }
    }
}
