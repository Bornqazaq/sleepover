using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Igruha.Core.Hub;

namespace Igruha.EditorTools
{
    /// <summary>A continuous stylized yard around the basement, lit by the scene's late sunset.</summary>
    public static class HubBasementYardBuilder
    {
        internal const float Grade = 3.16f;
        private const float HouseEdge = 10.2f, YardEdge = 17f, WellHalfWidth = 1.66f, WellDepth = 1.67f;
        private sealed class Bay
        {
            internal readonly Quaternion Q;
            internal readonly float Offset;
            internal readonly HubOriginalGeometry G;
            internal Bay(HubOriginalGeometry g,int side,float offset) { G=g;Q=Quaternion.Euler(0,side*90,0);Offset=offset; }
            internal Vector3 P(float x,float y,float z) => Q*new Vector3(Offset+x,y,HubBasementWindowPass.WallDepth+z);
            internal void Box(float x,float y,float z,Vector3 size,Material material) => G.Box(P(x,y,z),size,material,Q);
        }

        [MenuItem("Igruha/Хаб/Обновить двор, глубокий закат и ливень")]
        public static void Apply()
        {
            var scene=EditorSceneManager.GetActiveScene();
            if(EditorApplication.isPlaying || scene.path!="Assets/_Project/Scenes/Hub.unity")
                throw new InvalidOperationException("Open Hub outside Play Mode.");
            Build(HubCompactPass.Require(HubCompactPass.RootName).transform);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        internal static void Build(Transform hub)
        {
            var previous=hub.Find("BasementYard");
            if(previous!=null)UnityEngine.Object.DestroyImmediate(previous.gameObject);
            var root=HubCompactPass.Group(hub,"BasementYard");
            var g=new HubOriginalGeometry();
            var grass=Surface("YardGrass","597C4D",.35f);
            var stripe=Surface("YardGrassStripe","52734A",.35f);
            var blades=Surface("YardGrassTips","819658",.2f,true);
            var leaves=Surface("YardFoliage","496A4D",.1f);
            var leavesLight=Surface("YardFoliageLight","65855A",.1f);
            var bark=Surface("YardBark","725843",.08f);
            var fence=Surface("YardFence","D3C4A3",.3f);
            var fenceShade=Surface("YardFenceRail","988B71",.2f);
            var masonry=Surface("WindowMasonry","827F70",.15f);
            var wet=Surface("YardPuddle","536B67",.88f);
            var rainMaterial=Transparent("YardRain",new Color(.56f,.66f,.78f,.58f),true);
            for(int side=0;side<4;side++)
            {
                var offsets=side==2?Array.Empty<float>():HubBasementWindowPass.WindowOffsets(side);
                var bay=new Bay(g,side,0);
                var random=new System.Random(603+side*103);
                float left=-13.79f,right=13.79f;
                Ground(bay,offsets,grass,stripe);
                foreach(float offset in offsets)Well(new Bay(g,side,offset),masonry,wet,side);
                Fence(bay,left,right,fence,fenceShade);
                for(int i=0;i<900;i++)
                {
                    float z=HouseEdge-HubBasementWindowPass.WallDepth+(float)random.NextDouble()*(YardEdge-HouseEdge);
                    float x=Mathf.Lerp(-HouseEdge,HouseEdge,(float)random.NextDouble());
                    if(z<WellDepth && offsets.Any(o=>Mathf.Abs(x-o)<WellHalfWidth))continue;
                    Tuft(g,bay.P(x,Grade+.015f,z),bay.Q,random,blades);
                }
                for(float x=left+.65f;x<right;x+=2.1f)
                {
                    g.Sphere(bay.P(x,Grade+.33f,3.65f),.47f,((int)(x*10)&1)==0?leaves:leavesLight);
                }
                foreach(float x in new[]{-10f,-6.35f,.5f,9.85f})Tree(bay,x,3.6f,bark,leaves,leavesLight);
                Rain(root,bay,rainMaterial,side);
            }
            Corners(g,grass,stripe,blades);
            g.Build(root,"BasementYardGeometry",castShadows:true);
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>()) AssetDatabase.SaveAssetIfDirty(filter.sharedMesh);
            foreach(var renderer in root.GetComponentsInChildren<Renderer>()) AssetDatabase.SaveAssetIfDirty(renderer.sharedMaterial);
            HubYardLanternBuilder.Build(root,Grade);
            ApplyDuskLighting();
        }

        private static void Ground(Bay bay,float[] offsets,Material grass,Material stripe)
        {
            float start=HouseEdge-HubBasementWindowPass.WallDepth,end=YardEdge-HubBasementWindowPass.WallDepth;
            var cuts=new List<float>{-HouseEdge,HouseEdge};
            foreach(float offset in offsets)cuts.AddRange(new[]{offset-WellHalfWidth,offset+WellHalfWidth});
            cuts.Sort();
            for(int i=0;i<cuts.Count-1;i++)
            {
                float left=cuts[i],right=cuts[i+1],mid=(left+right)*.5f;
                bool well=offsets.Any(o=>Mathf.Abs(mid-o)<WellHalfWidth);
                for(int row=0;start+row*.35f<end;row++)
                {
                    float a=Mathf.Max(start+row*.35f,well?WellDepth:start),b=Mathf.Min(start+(row+1)*.35f,end);
                    if(b<=a)continue;
                    bay.G.Quad(bay.P(left,Grade,a),bay.P(left,Grade,b),bay.P(right,Grade,b),bay.P(right,Grade,a),row%2==0?grass:stripe,Vector2.one);
                }
            }
        }

        private static void Corners(HubOriginalGeometry g,Material grass,Material stripe,Material blades)
        {
            var random=new System.Random(1917);
            foreach(float sx in new[]{-1f,1f})foreach(float sz in new[]{-1f,1f})
            {
                float left=sx<0?-YardEdge:HouseEdge,right=sx<0?-HouseEdge:YardEdge;
                float near=sz<0?-YardEdge:HouseEdge,far=sz<0?-HouseEdge:YardEdge;
                for(int row=0;near+row*.35f<far;row++)
                {
                    float a=near+row*.35f,b=Mathf.Min(a+.35f,far);
                    g.Quad(new Vector3(left,Grade,a),new Vector3(left,Grade,b),new Vector3(right,Grade,b),new Vector3(right,Grade,a),row%2==0?grass:stripe,Vector2.one);
                }
                for(int i=0;i<170;i++)Tuft(g,new Vector3(Mathf.Lerp(left,right,(float)random.NextDouble()),Grade+.015f,Mathf.Lerp(near,far,(float)random.NextDouble())),Quaternion.identity,random,blades);
            }
        }

        private static void Tuft(HubOriginalGeometry g,Vector3 p,Quaternion q,System.Random random,Material material)
        {
            float height=.12f+(float)random.NextDouble()*.10f;
            for(int blade=0;blade<3;blade++)
            {
                float angle=(float)random.NextDouble()*Mathf.PI*2;
                var r=q*new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*.035f;
                var tip=p+Vector3.up*height+q*Vector3.right*.035f;
                g.Quad(p-r,tip,p+r,p+r,material,Vector2.one);
            }
        }

        private static void Well(Bay bay,Material masonry,Material wet,int side)
        {
            var g=bay.G;
            float wellBottom=HubBasementWindowPass.WindowCenterHeight(side)-HubBasementWindowPass.GlassHeight*.5f-.38f;
            bay.Box(0,wellBottom-.045f,1.04f,new Vector3(3.12f,.09f,1.12f),masonry);
            foreach(float x in new[]{-1.6f,1.6f})
                bay.Box(x,(Grade+wellBottom)*.5f,.98f,new Vector3(.12f,Grade-wellBottom,1.28f),masonry);
            bay.Box(0,(Grade+wellBottom)*.5f,1.61f,new Vector3(3.30f,Grade-wellBottom,.12f),masonry);
            g.Disc(bay.P(-.9f,Grade+.008f,2.8f),bay.Q*Vector3.right*.44f,bay.Q*Vector3.back*.17f,wet,12);
            g.Disc(bay.P(1.2f,Grade+.008f,3.3f),bay.Q*Vector3.right*.56f,bay.Q*Vector3.back*.22f,wet,12);
        }

        private static void Fence(Bay bay,float left,float right,Material wood,Material rails)
        {
            float z=4.05f;
            foreach(float y in new[]{Grade+.45f,Grade+1.14f})
                bay.Box((left+right)*.5f,y,z+.06f,new Vector3(right-left,.07f,.08f),rails);
            for(float x=left+.10f;x<right;x+=.22f)
            {
                bay.Box(x,Grade+.675f,z,new Vector3(.15f,1.35f,.085f),wood);
                var a=bay.P(x-.075f,Grade+1.35f,z-.0425f);var b=bay.P(x,Grade+1.47f,z-.0425f);var c=bay.P(x+.075f,Grade+1.35f,z-.0425f);
                var back=bay.Q*Vector3.forward*.085f;
                bay.G.Quad(a,b,c,c,wood,Vector2.one);bay.G.Quad(c+back,b+back,a+back,a+back,wood,Vector2.one);
                bay.G.Quad(a,a+back,b+back,b,wood,Vector2.one);bay.G.Quad(b,b+back,c+back,c,wood,Vector2.one);
            }
            int count=Mathf.CeilToInt((right-left)/2.1f);
            for(int i=0;i<=count;i++)
            {
                float x=Mathf.Lerp(left,right,i/(float)count);
                bay.Box(x,Grade+.76f,z+.035f,new Vector3(.19f,1.52f,.16f),rails);
                bay.G.Sphere(bay.P(x,Grade+1.54f,z+.035f),.115f,wood);
            }
        }
        private static void Tree(Bay bay,float x,float z,Material bark,Material dark,Material light)
        {
            var ground=bay.P(x,Grade,z);
            bay.G.Line(ground,ground+Vector3.up*1.78f,.19f,bark);
            bay.G.Line(ground+Vector3.up*1.05f,ground+Vector3.up*2.07f+bay.Q*Vector3.right*.45f,.105f,bark);
            bay.G.Sphere(ground+Vector3.up*2.1f,.91f,dark);
            bay.G.Sphere(ground+Vector3.up*2.50f+bay.Q*Vector3.right*.40f,.72f,light);
            bay.G.Sphere(ground+Vector3.up*2.03f-bay.Q*Vector3.right*.65f,.61f,dark);
        }
        private static void Rain(Transform root,Bay bay,Material material,int side)
        {
            // Four disjoint strips surround an empty, dry house footprint.
            const float inner=HouseEdge+.35f;
            float width=side%2==0?YardEdge*2:inner*2,depth=YardEdge-inner;
            var go=HubCompactPass.Group(root,"Downpour_"+side).gameObject;
            go.transform.localPosition=bay.P(0,Grade+6f,(inner+YardEdge)*.5f-HubBasementWindowPass.WallDepth);go.transform.localRotation=bay.Q;
            var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.duration=2;main.loop=true;main.prewarm=true;main.playOnAwake=true;main.startLifetime=.47f;main.startSpeed=0;main.startSize=new ParticleSystem.MinMaxCurve(.017f,.027f);main.maxParticles=Mathf.CeilToInt(width*depth*18*.47f*1.15f);main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.startColor=Color.white;
            ps.useAutoRandomSeed=false;ps.randomSeed=(uint)(731+side*23);
            var emission=ps.emission;emission.rateOverTime=width*depth*18;
            var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(width,.04f,depth);
            var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;velocity.y=-13.5f;velocity.x=-.65f;velocity.z=.2f;
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.renderMode=ParticleSystemRenderMode.Stretch;renderer.lengthScale=5;renderer.velocityScale=.025f;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
        internal static Material Transparent(string name,Color tint,bool streak)
        {
            string path=HubOriginalAssets.Folder+"/Materials/HO_"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find("Igruha/Hub/Weather Glass"));AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_Tint",tint);material.SetFloat("_Streak",streak?1:0);EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);return material;
        }
        private static Material Surface(string name,string hex,float smoothness,bool doubleSided=false)
        {
            var material=HubOriginalAssets.Mat(name,hex);material.SetFloat("_Smoothness",smoothness);
            if(doubleSided)material.SetFloat("_Cull",0);
            EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);return material;
        }
        [MenuItem("Igruha/Хаб/Разделить свет комнаты и закат двора")]
        public static void ApplyLighting()
        {
            var scene=EditorSceneManager.GetActiveScene();
            if(EditorApplication.isPlaying || scene.path!="Assets/_Project/Scenes/Hub.unity")
                throw new InvalidOperationException("Open Hub outside Play Mode.");
            ApplyDuskLighting();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        internal static void ApplyDuskLighting()
        {
            var hub=HubCompactPass.Require(HubCompactPass.RootName).transform;
            HubCompactLighting.ApplyRoomAmbient(hub);
            var yard=hub.Find("BasementYard");
            if(yard==null)return;
            var ambient=yard.GetComponent<HubYardAmbient>();
            if(ambient==null)ambient=yard.gameObject.AddComponent<HubYardAmbient>();
            ambient.Apply();EditorUtility.SetDirty(ambient);
            var sunTransform=yard.Find("YardSunsetLight");
            if(sunTransform==null)sunTransform=HubCompactPass.Group(yard,"YardSunsetLight");
            var sun=sunTransform.GetComponent<Light>();
            if(sun==null)sun=sunTransform.gameObject.AddComponent<Light>();
            sun.type=LightType.Directional;sun.color=HubCozyMaterials.Hex("FFC391");sun.intensity=.12f;
            sun.transform.localRotation=Quaternion.Euler(2.5f,90,0);
            sun.shadows=LightShadows.Soft;sun.shadowStrength=.7f;sun.shadowBias=.025f;sun.shadowNormalBias=.06f;
            var lightData=sun.GetComponent<UniversalAdditionalLightData>();
            if(lightData==null)lightData=sun.gameObject.AddComponent<UniversalAdditionalLightData>();
            lightData.renderingLayers=HubYardAmbient.RenderingLayer;
            lightData.customShadowLayers=true;lightData.shadowRenderingLayers=HubYardAmbient.RenderingLayer|1u;
            RenderSettings.sun=sun;
            EditorUtility.SetDirty(sun);EditorUtility.SetDirty(lightData);
            string path=HubOriginalAssets.Folder+"/Materials/HO_DuskSky.mat";
            var sky=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(sky==null){sky=new Material(Shader.Find("Igruha/Hub/Dusk Sky"));AssetDatabase.CreateAsset(sky,path);}
            sky.SetVector("_SunDirection",(Vector4)(-sun.transform.forward));
            RenderSettings.skybox=sky;EditorUtility.SetDirty(sky);AssetDatabase.SaveAssetIfDirty(sky);DynamicGI.UpdateEnvironment();
        }
    }
}
