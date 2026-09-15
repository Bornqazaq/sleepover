using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static Igruha.EditorTools.HubCompactPass;

namespace Igruha.EditorTools
{
    /// <summary>Local light pools and a scene-owned volume; the shared pipeline and orbit rig are unchanged.</summary>
    internal static class HubCompactLighting
    {
        internal static void Apply(Transform root)
        {
            var lights=Group(root,"Lighting");
            Pendant(lights,"Lounge",new Vector3(0,3.10f,-.45f),20,true);
            Pendant(lights,"Bar",new Vector3(-7.1f,3.02f,1.25f),16,true);
            Pendant(lights,"Bowling",new Vector3(-5.6f,3.12f,7.55f),13,false);
            Pendant(lights,"TableTennis",new Vector3(-.9f,3.12f,6.35f),13,true);
            Pendant(lights,"Billiards",new Vector3(6.8f,3.05f,1.6f),15,true);
            Pendant(lights,"Arcade",new Vector3(7.1f,3.12f,-6.6f),10,false);
            Pendant(lights,"Entry",new Vector3(-1.6f,3.12f,-6.4f),10,false);
            Pendant(lights,"Crown",new Vector3(6.8f,3.12f,7.6f),11,true);
            var fill=Make(lights,"RoomFill",new Vector3(0,3,0),LightType.Directional,"BDCEDD",.20f,0);fill.transform.rotation=Quaternion.Euler(52,-26,0);
            var glow=Make(lights,"ScreenBounce",new Vector3(0,.86f,2.3f),LightType.Point,"8BBDDC",1.35f,4.5f);
            Make(lights,"BarShelfWarmth",new Vector3(-8.95f,2.52f,1.15f),LightType.Point,"FFCD89",1.4f,3.5f);
            Make(lights,"FridgeGlow",new Vector3(-8.26f,1.35f,4.37f),LightType.Point,"B9E4E3",.8f,2.1f);
            foreach(float x in new[]{-5.2f,5.2f})
            {
                var bounce=Make(lights,"CeilingBounce",new Vector3(x,1.9f,0),LightType.Spot,"E7CBA2",6f,11);bounce.transform.rotation=Quaternion.Euler(-90,0,0);bounce.spotAngle=145;bounce.innerSpotAngle=95;
            }
            foreach(var entry in new[]{new Vector3(0,2.75f,6.9f),new Vector3(-7.4f,2.75f,1),new Vector3(7.2f,2.75f,0),new Vector3(1,2.75f,-7.1f)})
            {
                var wash=Make(lights,"WallWarmth",entry,LightType.Spot,"F5DCB7",5.5f,6);
                var target=Mathf.Abs(entry.z)>6?new Vector3(entry.x,1.6f,Mathf.Sign(entry.z)*9.7f):new Vector3(Mathf.Sign(entry.x)*9.7f,1.6f,entry.z);
                wash.transform.rotation=Quaternion.LookRotation(target-entry);wash.spotAngle=145;wash.innerSpotAngle=100;
            }
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=HubCozyMaterials.Hex("898A82");RenderSettings.ambientEquatorColor=HubCozyMaterials.Hex("817968");RenderSettings.ambientGroundColor=HubCozyMaterials.Hex("51504A");RenderSettings.ambientIntensity=1;
            RenderSettings.reflectionIntensity=.35f;RenderSettings.sun=fill;DynamicGI.UpdateEnvironment();
            var camera=Require("_Camera/Main Camera").GetComponent<Camera>();
            var data=camera.GetComponent<UniversalAdditionalCameraData>()??camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;data.antialiasingQuality=AntialiasingQuality.High;camera.allowHDR=true;
            string path=HubOriginalAssets.Folder+"/HubWarmVolume.asset";var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if(profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,path);}
            var tone=Effect<Tonemapping>(profile);tone.mode.Override(TonemappingMode.ACES);
            var color=Effect<ColorAdjustments>(profile);color.postExposure.Override(.10f);color.contrast.Override(7);color.saturation.Override(5);
            var bloom=Effect<Bloom>(profile);bloom.threshold.Override(1.05f);bloom.intensity.Override(.16f);bloom.scatter.Override(.55f);
            var volume=Group(root,"WarmAtmosphere").gameObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=1;volume.sharedProfile=profile;
            EditorUtility.SetDirty(profile);
        }
        private static T Effect<T>(VolumeProfile profile) where T:VolumeComponent
        {if(profile.TryGet<T>(out var effect))return effect;effect=profile.Add<T>(true);AssetDatabase.AddObjectToAsset(effect,profile);return effect;}
        private static void Pendant(Transform root,string name,Vector3 position,float intensity,bool shadows)
        {
            var shade=HubOriginalAssets.Prop(root,"Pendant",position);
            // The broad shade is a camera obstacle, with no physical contacts against players.
            var obstacle=Group(shade.transform,"ShadeCameraObstacle").gameObject;obstacle.layer=LayerMask.NameToLayer("CameraOnly");
            var collider=obstacle.AddComponent<BoxCollider>();collider.center=new Vector3(0,.19f,0);collider.size=new Vector3(.96f,.39f,.96f);collider.excludeLayers=~0;
            var light=Make(root,name+"_Pool",position+Vector3.down*.12f,LightType.Spot,"FFDEAE",intensity,8.5f);light.transform.rotation=Quaternion.Euler(90,0,0);light.spotAngle=118;light.innerSpotAngle=82;
            light.shadows=shadows?LightShadows.Soft:LightShadows.None;light.shadowStrength=.88f;light.shadowBias=.035f;light.shadowNormalBias=.10f;
            if(shadows){var so=new SerializedObject(light.GetComponent<UniversalAdditionalLightData>());so.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue=name=="Lounge"?UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh:UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium;so.ApplyModifiedPropertiesWithoutUndo();}
        }
        private static Light Make(Transform root,string name,Vector3 position,LightType type,string color,float intensity,float range)
        {var go=Group(root,name).gameObject;go.transform.position=position;var l=go.AddComponent<Light>();l.type=type;l.color=HubCozyMaterials.Hex(color);l.intensity=intensity;l.range=range;go.AddComponent<UniversalAdditionalLightData>();return l;}
    }
}
