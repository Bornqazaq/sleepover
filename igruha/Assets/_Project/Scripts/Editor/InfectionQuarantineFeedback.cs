using UnityEditor;
using UnityEngine;
using Igruha.Minigames.Infection;
using static Igruha.EditorTools.InfectionQuarantineAssets;

namespace Igruha.EditorTools
{
    internal static class InfectionQuarantineFeedback
    {
        private const string Audio="Assets/_Project/Audio/Infection/";
        internal static void Build(Transform root)
        {
            var presentation=Group("InfectionPresentation",root).gameObject.AddComponent<InfectionPresentation>();
            var heartbeat=Sound(presentation.transform,"Heartbeat",.16f,true,0,1);
            var finish=Sound(presentation.transform,"Whistle",.22f,false,0,1);
            heartbeat.playOnAwake=false;finish.playOnAwake=false;
            var template=Group("PaintFeedbackTemplate",root).gameObject.AddComponent<InfectionPaintEffects>();
            var source=template.gameObject.AddComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=1;source.minDistance=2;source.maxDistance=16;source.dopplerLevel=0;
            var zeroSource=template.gameObject.AddComponent<AudioSource>();zeroSource.playOnAwake=false;zeroSource.spatialBlend=0;
            var splash=CreatePaint(template.transform,"Splash",new Vector3(0,.9f,0),false);
            var droplets=CreatePaint(template.transform,"FootDrops",Vector3.up*.08f,true);
            Set(template,"splash",splash,"droplets",droplets,"source",source,"splat",Clip("Splat"),"zero",Clip("Zero"),"zeroSource",zeroSource,"wetStep",Clip("WetStep"));
            template.gameObject.SetActive(false);
            Set(presentation,"template",template,"heartbeat",heartbeat,"finish",finish);
            var game=Object.FindFirstObjectByType<InfectionMinigame>();Set(game,"presentation",presentation);
            var wind=Sound(root,"Wind",.12f,true,0,1);wind.transform.localPosition=Vector3.zero;
            foreach(var t in root.GetComponentsInChildren<Transform>())
                if(t.name.StartsWith("FireSite_"))Sound(t,"Fire",.25f,true,1,12);
            var arena=GameObject.Find("_Arena").transform;
            var creak=Sound(root,"Creak",.1f,true,1,12);creak.transform.position=arena.Find("Carousel").position;
            var swing=Sound(root,"Creak",.12f,true,1,8);swing.transform.position=arena.Find("Swings").position;
        }
        private static AudioClip Clip(string name)=>AssetDatabase.LoadAssetAtPath<AudioClip>(Audio+name+".wav");
        private static AudioSource Sound(Transform parent,string name,float volume,bool loop,float spatial,float range)
        {
            var s=Group("Audio_"+name,parent).gameObject.AddComponent<AudioSource>();
            s.clip=Clip(name);s.volume=volume;s.loop=loop;s.playOnAwake=loop;s.spatialBlend=spatial;s.minDistance=2;s.maxDistance=range;s.dopplerLevel=0;
            return s;
        }
        private static ParticleSystem CreatePaint(Transform parent,string name,Vector3 pos,bool trail)
        {
            var p=Group(name,parent).gameObject.AddComponent<ParticleSystem>();p.transform.localPosition=pos;
            p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=p.main;main.playOnAwake=false;main.loop=false;main.duration=1;
            main.startLifetime=new ParticleSystem.MinMaxCurve(trail?.6f:.35f,trail?1.1f:.7f);
            main.startSpeed=new ParticleSystem.MinMaxCurve(trail?.05f:1.4f,trail?.2f:3.2f);
            main.startSize=new ParticleSystem.MinMaxCurve(.055f,trail?.13f:.21f);
            main.startColor=new Color(.3f,1,.045f,1);main.gravityModifier=trail?0:.65f;
            main.maxParticles=trail?40:32;main.simulationSpace=ParticleSystemSimulationSpace.World;
            var emission=p.emission;emission.rateOverTime=0;
            if(!trail)emission.SetBursts(new[]{new ParticleSystem.Burst(0,28)});
            var shape=p.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=trail?.16f:.3f;
            var color=p.colorOverLifetime;color.enabled=true;var gradient=new Gradient();
            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,.55f),new GradientAlphaKey(0,1)});color.color=gradient;
            var r=p.GetComponent<ParticleSystemRenderer>();r.sharedMaterial=InfectionQuarantineEffects.PuffMaterial();
            return p;
        }
        private static void Set(Object target,params object[] fields)
        {
            var so=new SerializedObject(target);
            for(int i=0;i<fields.Length;i+=2)so.FindProperty((string)fields[i]).objectReferenceValue=(Object)fields[i+1];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
