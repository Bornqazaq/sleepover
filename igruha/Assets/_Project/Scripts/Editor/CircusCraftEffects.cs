using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Core.Minigame;
using Igruha.Minigames.Circus;
using Igruha.Minigames.CansOrder;

namespace Igruha.EditorTools
{
    /// <summary>Small original event effects, wired to the existing authoritative game events.</summary>
    internal static class CircusCraftEffects
    {
        internal static void Build(Transform arena,CircusArenaConfig config)
        {
            var old=arena.Find("Effects");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var root=new GameObject("Effects").transform;root.SetParent(arena,false);
            var soft=Material("CN_EventDust",true);var paper=Material("CN_EventConfetti",false);
            var cages=arena.GetComponentsInChildren<CageStation>(true);
            var descent=new ParticleSystem[cages.Length];
            for(int i=0;i<cages.Length;i++)
            {
                var previous=cages[i].transform.Find("DescentDust");if(previous!=null)Object.DestroyImmediate(previous.gameObject);
                descent[i]=Create(cages[i].transform,"DescentDust",soft,new Color(.59f,.40f,.20f,.25f),1.4f,.20f,.15f,12,true);
                var shape=descent[i].shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(2.7f,.04f,2.7f);
            }
            var landing=Create(root,"LandingBurst",soft,new Color(.66f,.46f,.24f,.30f),1.2f,.35f,1.8f,32);
            var sparks=Create(root,"TauntSparks",paper,new Color(1f,.70f,.23f,.9f),.4f,.025f,2f,9);
            var impact=Create(root,"CatchImpact",soft,new Color(.81f,.62f,.34f,.34f),.48f,.20f,1.1f,20);
            var confetti=Create(root,"Confetti",paper,Color.white,6f,.045f,.4f,140);
            confetti.transform.localPosition=Vector3.up*(config.RiggingHeight-1.5f);
            var main=confetti.main;main.gravityModifier=.10f;main.startRotation3D=true;
            main.startRotationX=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
            main.startRotationY=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
            var colors=new Gradient();colors.SetKeys(new[]{new GradientColorKey(new Color(.9f,.12f,.1f),0),new GradientColorKey(new Color(1f,.78f,.18f),.33f),new GradientColorKey(new Color(.07f,.44f,.52f),.67f),new GradientColorKey(new Color(1f,.91f,.67f),1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
            main.startColor=new ParticleSystem.MinMaxGradient(colors){mode=ParticleSystemGradientMode.RandomColor};
            var spread=confetti.shape;spread.shapeType=ParticleSystemShapeType.Box;spread.scale=new Vector3(10,.1f,10);
            var rotation=confetti.rotationOverLifetime;rotation.enabled=true;rotation.z=new ParticleSystem.MinMaxCurve(-2,2);
            var fx=root.gameObject.AddComponent<CircusEffects>();var data=new SerializedObject(fx);
            SetArray(data,"cages",cages);SetArray(data,"descentDust",descent);
            data.FindProperty("landingBurst").objectReferenceValue=landing;data.FindProperty("tauntSparks").objectReferenceValue=sparks;
            data.FindProperty("catchImpact").objectReferenceValue=impact;data.FindProperty("confetti").objectReferenceValue=confetti;
            data.FindProperty("bear").objectReferenceValue=arena.GetComponentInChildren<PitBear>(true);
            data.FindProperty("controller").objectReferenceValue=Object.FindFirstObjectByType<MinigameControllerBase>(FindObjectsInactive.Include);
            data.ApplyModifiedPropertiesWithoutUndo();
            foreach(var flash in arena.GetComponentsInChildren<CanConfirmFlash>(true))
            {
                foreach(var oldBurst in flash.GetComponentsInChildren<ParticleSystem>(true))
                    if(oldBurst!=null)Object.DestroyImmediate(oldBurst.gameObject);
                var burst=Create(flash.transform,"ConfirmBurst",paper,new Color(1f,.81f,.37f,.8f),.35f,.018f,.5f,8);
                burst.transform.localPosition=Vector3.up*.14f;
                var flashData=new SerializedObject(flash);flashData.FindProperty("burst").objectReferenceValue=burst;flashData.ApplyModifiedPropertiesWithoutUndo();
            }
            var cans=Object.FindFirstObjectByType<CansOrderMinigame>(FindObjectsInactive.Include);
            if(cans!=null)
            {
                var celebration=new GameObject("CN_SolvedFanfare");
                try
                {
                    var burst=Object.Instantiate(confetti,celebration.transform);burst.name="Confetti";burst.transform.localPosition=Vector3.zero;
                    var burstMain=burst.main;burstMain.playOnAwake=true;burstMain.startLifetime=2.4f;
                    var burstShape=burst.shape;burstShape.scale=new Vector3(2,.1f,2);
                    var burstEmission=burst.emission;burstEmission.SetBursts(new[]{new ParticleSystem.Burst(0,45)});
                    var prefab=PrefabUtility.SaveAsPrefabAsset(celebration,CircusNightAssets.Prefabs+"/CN_SolvedFanfare.prefab");
                    var gameData=new SerializedObject(cans);gameData.FindProperty("solvedFanfarePrefab").objectReferenceValue=prefab;gameData.ApplyModifiedPropertiesWithoutUndo();
                }
                finally{Object.DestroyImmediate(celebration);}
            }
        }

        private static ParticleSystem Create(Transform parent,string name,Material material,Color color,float lifetime,float size,float speed,int count,bool loop=false)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);
            var p=go.AddComponent<ParticleSystem>();p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=p.main;main.playOnAwake=false;main.loop=loop;main.duration=loop?2:1;
            main.startLifetime=lifetime;main.startSpeed=new ParticleSystem.MinMaxCurve(speed*.3f,speed);
            main.startSize=new ParticleSystem.MinMaxCurve(size*.5f,size);main.startColor=color;
            main.gravityModifier=.035f;main.maxParticles=200;main.simulationSpace=ParticleSystemSimulationSpace.World;
            var emission=p.emission;emission.rateOverTime=loop?count:0;
            if(!loop)emission.SetBursts(new[]{new ParticleSystem.Burst(0,(short)count)});
            var shape=p.shape;shape.shapeType=ParticleSystemShapeType.Hemisphere;shape.radius=.4f;
            var fade=p.colorOverLifetime;fade.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(0,1)});
            fade.color=gradient;
            var renderer=p.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            return p;
        }

        private static Material Material(string name,bool soft)
        {
            string path=CircusNightAssets.Materials+"/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));AssetDatabase.CreateAsset(material,path);}
            material.SetFloat("_Surface",1);material.SetFloat("_Blend",0);material.SetFloat("_ZWrite",0);material.SetFloat("_Cull",0);
            material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.renderQueue=3000;
            if(soft)
            {
                string texturePath=CircusNightAssets.Art+"/Textures/CN_SoftParticle.asset";
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if(texture==null)
                {
                    const int n=64;texture=new Texture2D(n,n,TextureFormat.RGBA32,false){name="CN_SoftParticle",wrapMode=TextureWrapMode.Clamp};
                    var pixels=new Color[n*n];
                    for(int y=0;y<n;y++)for(int x=0;x<n;x++)
                    {float radius=new Vector2((x+.5f)/n*2-1,(y+.5f)/n*2-1).magnitude;pixels[y*n+x]=new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-radius),2));}
                    texture.SetPixels(pixels);texture.Apply();AssetDatabase.CreateAsset(texture,texturePath);
                }
                material.SetTexture("_BaseMap",texture);
            }
            EditorUtility.SetDirty(material);return material;
        }

        private static void SetArray(SerializedObject data,string name,Object[] values)
        {var array=data.FindProperty(name);array.arraySize=values.Length;for(int i=0;i<values.Length;i++)array.GetArrayElementAtIndex(i).objectReferenceValue=values[i];}
    }
}
