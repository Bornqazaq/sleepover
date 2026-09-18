using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static Igruha.EditorTools.InfectionQuarantineAssets;

namespace Igruha.EditorTools
{
    internal static class InfectionQuarantineEffects
    {
        internal static Material GhostMaterial()
        {
            var m=Material("TubeGhost",new Color(.65f,.29f,.12f,.17f));
            m.SetFloat("_Surface",1);m.SetFloat("_ZWrite",0);
            m.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);m.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType","Transparent");m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.renderQueue=3000;
            EditorUtility.SetDirty(m);return m;
        }
        internal static void Build(Transform root)
        {
            foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))light.enabled=false;
            var sun=new GameObject("DustSun").AddComponent<Light>();sun.transform.SetParent(root,false);
            sun.type=LightType.Directional;sun.transform.rotation=Quaternion.Euler(46,-38,0);
            sun.color=new Color(1,.87f,.66f);sun.intensity=1.8f;sun.shadows=LightShadows.Soft;
            sun.shadowBias=.035f;sun.shadowNormalBias=.22f;RenderSettings.sun=sun;
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.66f,.61f,.51f);
            RenderSettings.ambientEquatorColor=new Color(.48f,.44f,.37f);
            RenderSettings.ambientGroundColor=new Color(.21f,.19f,.16f);
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;
            RenderSettings.fogColor=new Color(.61f,.55f,.45f);RenderSettings.fogDensity=.011f;
            string skyPath=Art+"/Materials/DustSky.mat";
            var sky=AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if(sky==null){sky=new Material(Shader.Find("Skybox/Procedural"));AssetDatabase.CreateAsset(sky,skyPath);}
            sky.SetColor("_SkyTint",new Color(.63f,.51f,.35f));sky.SetColor("_GroundColor",RenderSettings.fogColor);
            sky.SetFloat("_AtmosphereThickness",1.8f);sky.SetFloat("_Exposure",.8f);sky.SetFloat("_SunSize",.035f);
            RenderSettings.skybox=sky;EditorUtility.SetDirty(sky);DynamicGI.UpdateEnvironment();
            var probe=new SphericalHarmonicsL2();probe.AddAmbientLight(new Color(.45f,.40f,.32f));RenderSettings.ambientProbe=probe;
            foreach(var volume in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))volume.enabled=false;
            string profilePath=Art+"/DustVolume.asset";
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if(profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,profilePath);}
            if(!profile.TryGet<Bloom>(out var bloom)){bloom=profile.Add<Bloom>();AssetDatabase.AddObjectToAsset(bloom,profile);}
            bloom.threshold.Override(1.1f);bloom.intensity.Override(.24f);bloom.scatter.Override(.62f);
            if(!profile.TryGet<ColorAdjustments>(out var grade)){grade=profile.Add<ColorAdjustments>();AssetDatabase.AddObjectToAsset(grade,profile);}
            grade.contrast.Override(8);grade.saturation.Override(-6);grade.postExposure.Override(.18f);
            if(!profile.TryGet<Tonemapping>(out var tone)){tone=profile.Add<Tonemapping>();AssetDatabase.AddObjectToAsset(tone,profile);}
            tone.mode.Override(TonemappingMode.ACES);
            var v=Group("QuarantineVolume",root).gameObject.AddComponent<Volume>();v.isGlobal=true;v.priority=30;v.sharedProfile=profile;
            EditorUtility.SetDirty(profile);
            foreach(var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                var data=camera.GetComponent<UniversalAdditionalCameraData>();
                if(data==null)data=camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing=true;
            }
            var effects=Group("SmokeFireDust",root);
            Fire(effects,new Vector3(28,1.1f,28),2.1f,21);
            Fire(effects,new Vector3(-31,.8f,14),1.4f,48);
            Fire(effects,new Vector3(12,.8f,-28),1.2f,62);
            Fire(effects,new Vector3(-21,0,22),.8f,91);
            // Low drifting haze stays around the outer street; no opaque layer at player eye height.
            for(int i=0;i<4;i++)
            {
                var p=Particles(effects,"StreetDust",new Vector3(-26+i*17,.2f,23),10,.13f,1.1f,18,100+i);
                var main=p.main;main.startSize=new ParticleSystem.MinMaxCurve(3,6);
                var shape=p.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(14,.2f,2);
                var vel=p.velocityOverLifetime;vel.enabled=true;vel.x=.5f;vel.y=.06f;
                Fade(p,new Color(.65f,.54f,.37f),.13f);
            }
        }
        private static void Fire(Transform parent,Vector3 position,float scale,int seed)
        {
            var root=Group("FireSite_"+seed,parent);root.localPosition=position;root.localScale=Vector3.one*scale;
            var flame=Particles(root,"Flame",Vector3.up*.3f,1.1f,1.6f,23,45,seed);
            var main=flame.main;main.startSize3D=true;main.startSizeX=new ParticleSystem.MinMaxCurve(.4f,.95f);
            main.startSizeY=new ParticleSystem.MinMaxCurve(1.3f,2.8f);main.startSizeZ=1;
            flame.GetComponent<ParticleSystemRenderer>().renderMode=ParticleSystemRenderMode.VerticalBillboard;
            Fade(flame,new Color(1,.45f,.045f),.95f);
            string firePath=Art+"/Materials/Fire.mat";
            var fireMaterial=AssetDatabase.LoadAssetAtPath<Material>(firePath);
            if(fireMaterial==null){fireMaterial=new Material(PuffMaterial());AssetDatabase.CreateAsset(fireMaterial,firePath);}
            fireMaterial.shader=Shader.Find("Igruha/Infection/Atmosphere");
            fireMaterial.SetColor("_BaseColor",new Color(4,2,.6f,1));
            fireMaterial.SetShaderPassEnabled("DepthNormalsOnly",false);
            flame.GetComponent<ParticleSystemRenderer>().sharedMaterial=fireMaterial;EditorUtility.SetDirty(fireMaterial);
            var size=flame.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,1,1,.08f));
            var smoke=Particles(root,"Smoke",Vector3.up*1.8f,10,1.1f,3.5f,42,seed+1);
            main=smoke.main;main.startSize=new ParticleSystem.MinMaxCurve(2.0f,3.0f);
            main.startRotation=new ParticleSystem.MinMaxCurve(0,6.28f);
            var growth=smoke.sizeOverLifetime;growth.enabled=true;growth.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.8f,1,2.4f));
            var velocity=smoke.velocityOverLifetime;velocity.enabled=true;velocity.x=.38f;
            Fade(smoke,new Color(.09f,.075f,.06f),.92f);
            var sparks=Particles(root,"Embers",Vector3.up*.9f,2.3f,2.2f,4,16,seed+2);
            main=sparks.main;main.startSize=new ParticleSystem.MinMaxCurve(.025f,.065f);
            Fade(sparks,new Color(4,1,.08f),1);
            var lamp=Group("FireBounce",root).gameObject.AddComponent<Light>();lamp.type=LightType.Point;
            lamp.transform.localPosition=Vector3.up*1.3f;lamp.color=new Color(1,.27f,.035f);lamp.intensity=4;
            lamp.range=7;lamp.shadows=LightShadows.None;
        }
        private static ParticleSystem Particles(Transform parent,string name,Vector3 pos,float life,float speed,float rate,int max,int seed)
        {
            var go=Group(name,parent);go.localPosition=pos;go.localRotation=Quaternion.Euler(-90,0,0);
            var p=go.gameObject.AddComponent<ParticleSystem>();p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=p.main;main.loop=true;main.prewarm=true;main.duration=12;main.startLifetime=new ParticleSystem.MinMaxCurve(life*.8f,life);
            main.startSpeed=new ParticleSystem.MinMaxCurve(speed*.6f,speed);main.maxParticles=max;main.playOnAwake=true;
            main.simulationSpace=ParticleSystemSimulationSpace.World;
            var emission=p.emission;emission.rateOverTime=rate;
            var shape=p.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=12;shape.radius=.55f;
            p.useAutoRandomSeed=false;p.randomSeed=(uint)seed;
            var r=p.GetComponent<ParticleSystemRenderer>();r.sharedMaterial=PuffMaterial();r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
            return p;
        }
        private static void Fade(ParticleSystem p,Color color,float alpha)
        {
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(color,0),new GradientColorKey(color,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(alpha,.18f),new GradientAlphaKey(alpha,.72f),new GradientAlphaKey(0,1)});
            var over=p.colorOverLifetime;over.enabled=true;over.color=gradient;
        }
        internal static Material PuffMaterial()
        {
            string path=Art+"/Materials/SoftParticle.mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m!=null){m.shader=Shader.Find("Igruha/Infection/Atmosphere");return m;}
            m=new Material(Shader.Find("Igruha/Infection/Atmosphere"));
            m.SetFloat("_Surface",1);m.SetFloat("_ZWrite",0);m.SetFloat("_Cull",0);
            m.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);m.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.SetOverrideTag("RenderType","Transparent");m.renderQueue=3000;
            const int n=128;var tex=new Texture2D(n,n,TextureFormat.RGBA32,false);
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                float dx=(x+.5f)/n*2-1,dy=(y+.5f)/n*2-1;
                float d=Mathf.Sqrt(dx*dx+dy*dy);float noise=Mathf.PerlinNoise(x*.055f,y*.055f);
                float a=Mathf.Pow(Mathf.Clamp01(1-d),1.4f)*Mathf.Lerp(.55f,1,noise);
                tex.SetPixel(x,y,new Color(1,1,1,a));
            }
            tex.Apply();string texturePath=Art+"/Textures/SmokePuff.png";File.WriteAllBytes(texturePath,tex.EncodeToPNG());Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texturePath);m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            m.SetShaderPassEnabled("DepthNormalsOnly",false);m.SetColor("_BaseColor",Color.white);AssetDatabase.CreateAsset(m,path);return m;
        }
    }
}
