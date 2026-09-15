using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Igruha.Minigames.Circus;

namespace Igruha.EditorTools
{
    /// <summary>Applies original scenery without recreating gameplay objects or their references.</summary>
    internal static class CircusNightBuilder
    {
        private const string RootName = "_CircusNight";
        private const string SceneFolder = "Assets/_Project/Scenes/Minigames/";
        private const string ConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/CircusArenaConfig.asset";
        private static readonly string[] SceneNames = { "Stopwatch", "CansOrder" };
        private static readonly Color Warm = new Color(1f,.84f,.62f);
        private static readonly Color Cool = new Color(.74f,.86f,1f);

        [MenuItem("Igruha/Цирк/Оформить шапито — обе сцены")]
        internal static void ApplyBoth()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Leave Play Mode before applying scene art.");
            var opened = EditorSceneManager.GetActiveScene();
            if (opened.isDirty) throw new InvalidOperationException("Save the open scene before applying both circus scenes.");
            string original = opened.path;
            CircusNightAssets.Import();
            try
            {
                foreach (string name in SceneNames)
                {
                    EditorSceneManager.OpenScene(SceneFolder + name + ".unity",OpenSceneMode.Single);
                    ApplyActive();
                    EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                }
            }
            finally { if (!string.IsNullOrEmpty(original)) EditorSceneManager.OpenScene(original,OpenSceneMode.Single); }
        }

        internal static void ApplyActive()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!SceneNames.Contains(scene.name)) throw new InvalidOperationException("Open Stopwatch or CansOrder.");
            var config = AssetDatabase.LoadAssetAtPath<CircusArenaConfig>(ConfigPath);
            Transform arena = GameObject.Find("_Arena").transform;
            // Gameplay collision is invariant. Environment contains the old fairground's
            // mesh colliders outside the pit; they leave with the replaced decoration.
            string physicsBefore = PhysicsSignature(arena);
            var old = GameObject.Find(RootName); if (old != null) UnityEngine.Object.DestroyImmediate(old);
            Transform root = new GameObject(RootName).transform;
            foreach (string name in new[] { "PitFloor", "Sawdust", "PitRim", "TentFloor", "TentWall", "Canopy" }) Hide(arena.Find(name));
            foreach (string name in new[] { "PitBarrier", "Overhead", "WallDecor" }) Hide(arena.Find("Environment/" + name));
            foreach(var mesh in arena.GetComponentsInChildren<MeshFilter>(true))
                if(mesh.sharedMesh!=null && mesh.sharedMesh.name.StartsWith("SM_Prop_Cobwebs_"))
                    Hide(mesh.transform);
            var oldAmbient=arena.Find("Effects/Ambient");
            if(oldAmbient!=null)oldAmbient.gameObject.SetActive(false);
            var environment=arena.Find("Environment");
            if(environment!=null)foreach(var light in environment.GetComponentsInChildren<Light>(true))light.enabled=false;

            foreach (string file in Directory.GetFiles(CircusNightAssets.Prefabs,"*.prefab"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if(!(name.StartsWith("CN_Deck_") || name.StartsWith("CN_CanvasBay_") || name.StartsWith("CN_Garland_") ||
                    new[]{"CN_PitMasonry","CN_PitSawdust","CN_CupolaCrown","CN_PerimeterBunting","CN_PaperScatter"}.Contains(name)))continue;
                Place(root,name,Vector3.zero);
            }
            for (int i=0;i<32;i++)
            {
                float angle=i*360f/32; Vector3 dir=Quaternion.Euler(0,angle,0)*Vector3.forward;
                if (i%8==0) continue;
                Place(root,"CN_RingBarrier",dir*9.70f+Vector3.up*config.TentFloorHeight,angle);
            }
            for (int i=0;i<16;i++)
            {
                float angle=i*360f/16+11.25f;Vector3 dir=Quaternion.Euler(0,angle,0)*Vector3.forward;
                Place(root,"CN_Lantern",dir*11.10f+Vector3.up*config.TentFloorHeight,angle);
            }
            Place(root,"CN_Entrance",new Vector3(0,config.TentFloorHeight,17.30f));
            AddLabel(root,"ЦИРК БРУНО",new Vector3(0,7.57f,17.08f),.46f);
            AddLabel(root,"ПРЕДСТАВЛЕНИЕ",new Vector3(0,8.36f,17.07f),.19f);
            DressCages(arena);
            CircusNightProps.Apply(arena);
            CircusBeast.RedressInScene();
            BuildLighting(root,config);
            BuildAtmosphere(root);
            CircusCraftBuilder.Apply(root,arena,config);
            if (PhysicsSignature(arena) != physicsBefore) throw new InvalidOperationException("The art pass changed arena collision geometry.");
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Grand Chapiteau applied to " + scene.name + ": striped Blender tent, theatrical lighting and Bruno; gameplay colliders preserved.");
        }

        private static void Hide(Transform t)
        {
            if (t == null) return;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true)) r.enabled=false;
        }

        private static void DressCages(Transform arena)
        {
            foreach (var r in arena.Find("Cages").GetComponentsInChildren<MeshRenderer>(true))
            {
                string n=r.name;
                string material = n.StartsWith("Bar_") || n.StartsWith("Post_") || n.StartsWith("Rail_") ? "CN_BlackIron" : n.StartsWith("Slat_") ? "CN_WalnutLight" : null;
                if (material==null) continue;
                r.sharedMaterial=CircusNightAssets.Material(material);r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;
            }
            foreach (var r in arena.Find("Chains").GetComponentsInChildren<MeshRenderer>(true))
            { r.sharedMaterial=CircusNightAssets.Material("CN_BlackIron");r.shadowCastingMode=ShadowCastingMode.Off; }
        }

        private static GameObject Place(Transform parent,string name,Vector3 position,float yaw=0)
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(CircusNightAssets.Prefabs+"/"+name+".prefab");
            if (asset==null) throw new InvalidOperationException("Missing prefab: "+name);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(asset,parent);
            go.transform.localPosition=position;go.transform.localRotation=Quaternion.Euler(0,yaw,0);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            { r.shadowCastingMode=name.Contains("Canvas")?ShadowCastingMode.Off:ShadowCastingMode.On;r.receiveShadows=true; }
            return go;
        }

        private static void AddLabel(Transform root,string title,Vector3 position,float size)
        {
            var go=new GameObject(title);go.transform.SetParent(root,false);go.transform.localPosition=position;
            var label=go.AddComponent<TextMeshPro>();label.text=title;label.fontSize=size*10;
            label.alignment=TextAlignmentOptions.Center;label.color=new Color(.92f,.74f,.42f);
            label.rectTransform.sizeDelta=new Vector2(5.6f,1);label.fontStyle=FontStyles.Bold;
        }

        private static Light Light(Transform root,string name,LightType type,Vector3 position,Vector3 aim,Color color,float intensity,float range,float cone=70,bool shadows=false)
        {
            var go=new GameObject(name);go.transform.SetParent(root,false);go.transform.position=position;
            go.transform.rotation=Quaternion.LookRotation(aim-position);
            var light=go.AddComponent<Light>();light.type=type;light.color=color;light.intensity=intensity;light.range=range;
            light.spotAngle=cone;light.innerSpotAngle=cone*.55f;light.shadows=shadows?LightShadows.Soft:LightShadows.None;
            light.shadowStrength=.85f;light.shadowBias=.025f;light.shadowNormalBias=.12f;
            if(name.StartsWith("Bruno_"))
            {light.shadowStrength=.9f;light.shadowBias=.015f;light.shadowNormalBias=.07f;light.shadowResolution=LightShadowResolution.High;}
            if(shadows)go.AddComponent<UniversalAdditionalLightData>().usePipelineSettings=false;
            return light;
        }

        private static void BuildLighting(Transform root,CircusArenaConfig config)
        {
            var rig=new GameObject("Lighting").transform;rig.SetParent(root,false);
            RenderSettings.skybox=null;RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.68f,.63f,.53f);
            RenderSettings.ambientEquatorColor=new Color(.38f,.33f,.27f);
            RenderSettings.ambientGroundColor=new Color(.24f,.195f,.145f);
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;
            RenderSettings.fogColor=new Color(.30f,.235f,.17f);RenderSettings.fogDensity=.004f;
            RenderSettings.defaultReflectionMode=DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture=EnsureReflection();RenderSettings.reflectionIntensity=.65f;
            foreach (var old in GameObject.Find("_Lighting").GetComponentsInChildren<Light>(true)) old.enabled=false;
            var canvasBounce=Light(rig,"CanvasBounce",LightType.Directional,new Vector3(-5,14,-8),Vector3.zero,new Color(1f,.93f,.80f),.55f,50);
            RenderSettings.sun=canvasBounce;
            // Two low key lights sit below the scoreboard: its box cannot shadow the whole pit.
            Light(rig,"Bruno_WarmKey",LightType.Spot,new Vector3(-4,6.8f,-3),new Vector3(0,0,1),Warm,245,19,102,true);
            Light(rig,"Bruno_Rim",LightType.Spot,new Vector3(4,7,4),new Vector3(0,1,-1),Cool,175,20,96,true);
            Light(rig,"PitReadability",LightType.Point,new Vector3(-2,3.4f,2.8f),Vector3.zero,new Color(1f,.92f,.78f),48,17);
            for (int i=0;i<config.CageAnchorCount;i++)
            {
                Vector3 dir=Quaternion.Euler(0,config.GetAnchorAngle(i),0)*Vector3.forward;
                Light(rig,"CagePool_"+i,LightType.Spot,dir*8.2f+Vector3.up*15.4f,dir*config.CageRingRadius+Vector3.up*2,
                    new Color(1f,.93f,.81f),135,23,48);
            }
            for (int i=0;i<8;i++)
            {
                Vector3 dir=Quaternion.Euler(0,i*45+11.25f,0)*Vector3.forward;
                Light(rig,"CupolaBounce_"+i,LightType.Spot,dir*12.5f+Vector3.up*10,dir*7f+Vector3.up*18.5f,
                    new Color(1f,.90f,.73f),280,22,100);
            }
            for (int i=0;i<8;i++)
            {
                Vector3 dir=Quaternion.Euler(0,i*45+22.5f,0)*Vector3.forward;
                Light(rig,"LanternBounce_"+i,LightType.Point,dir*11.1f+Vector3.up*4.72f,Vector3.zero,Warm,35,9);
                Light(rig,"CanvasWash_"+i,LightType.Spot,dir*14.8f+Vector3.up*3.5f,dir*17.7f+Vector3.up*9,
                    new Color(1f,.94f,.82f),160,16,78);
                Light(rig,"StallPractical_"+i,LightType.Point,dir*15.1f+Vector3.up*(config.TentFloorHeight+2.65f),Vector3.zero,
                    new Color(1f,.87f,.66f),12,4.8f);
            }
            for(int i=0;i<4;i++)
            {
                Vector3 dir=Quaternion.Euler(0,i*90+22.5f,0)*Vector3.forward;
                Light(rig,"FairgroundPool_"+i,LightType.Spot,dir*12.8f+Vector3.up*9,
                    dir*13.8f+Vector3.up*config.TentFloorHeight,new Color(1f,.92f,.79f),125,16,115,i%2==0);
            }
            Light(rig,"MarqueeWarmth",LightType.Point,new Vector3(0,7.6f,15.5f),Vector3.zero,Warm,49,10);
            var volume=new GameObject("CircusGrade").AddComponent<Volume>();volume.transform.SetParent(root,false);
            volume.isGlobal=true;volume.priority=10;
            string path=CircusNightAssets.Materials+"/CN_Grade.asset";
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,path);}
            var bloom=Ensure<Bloom>(profile);bloom.intensity.Override(.24f);bloom.threshold.Override(1.2f);bloom.scatter.Override(.55f);
            Ensure<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var grade=Ensure<ColorAdjustments>(profile);grade.postExposure.Override(.10f);grade.contrast.Override(5);grade.saturation.Override(6);
            var vignette=Ensure<Vignette>(profile);vignette.intensity.Override(.075f);vignette.smoothness.Override(.45f);
            volume.sharedProfile=profile;EditorUtility.SetDirty(profile);
            foreach (var camera in GameObject.Find("_Camera").GetComponentsInChildren<Camera>(true))
            {
                var data=camera.GetComponent<UniversalAdditionalCameraData>();
                if(data==null)data=camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing=true;
                data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality=AntialiasingQuality.High;
                camera.allowHDR=true;
            }
            AssetDatabase.SaveAssets();
        }

        private static T Ensure<T>(VolumeProfile profile) where T:VolumeComponent
        {
            T c;if(!profile.TryGet(out c)){c=profile.Add<T>();AssetDatabase.AddObjectToAsset(c,profile);}return c;
        }

        private static Cubemap EnsureReflection()
        {
            string path=CircusNightAssets.Materials+"/CN_Reflection.asset";
            var cube=AssetDatabase.LoadAssetAtPath<Cubemap>(path);bool created=cube==null;
            const int size=16;if(created)cube=new Cubemap(size,TextureFormat.RGBAHalf,false){name="CN_Reflection"};
            var pixels=new Color[size*size];
            foreach(CubemapFace face in Enum.GetValues(typeof(CubemapFace)))
            {
                if(face==CubemapFace.Unknown)continue;
                Color tint=face==CubemapFace.PositiveY?new Color(.28f,.245f,.185f):new Color(.15f,.12f,.075f);
                for(int i=0;i<pixels.Length;i++)pixels[i]=tint;cube.SetPixels(pixels,face);
            }
            cube.Apply();if(created)AssetDatabase.CreateAsset(cube,path);else EditorUtility.SetDirty(cube);return cube;
        }

        private static void BuildAtmosphere(Transform root)
        {
            BuildShafts(root);
            var go=new GameObject("SawdustInTheLight");go.transform.SetParent(root,false);go.transform.localPosition=new Vector3(0,7,0);
            var particles=go.AddComponent<ParticleSystem>();var main=particles.main;
            main.startLifetime=18;main.startSpeed=.018f;main.startSize=new ParticleSystem.MinMaxCurve(.012f,.033f);
            main.startColor=new Color(.67f,.49f,.28f,.19f);main.maxParticles=160;main.simulationSpace=ParticleSystemSimulationSpace.World;
            var emission=particles.emission;emission.rateOverTime=7;
            var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(15,11,15);
            var renderer=go.GetComponent<ParticleSystemRenderer>();
            string path=CircusNightAssets.Materials+"/CN_Dust.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));AssetDatabase.CreateAsset(material,path);}
            material.SetFloat("_Surface",1);material.SetFloat("_Blend",0);material.SetFloat("_ZWrite",0);
            material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.renderQueue=3000;renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;EditorUtility.SetDirty(material);
        }

        private static void BuildShafts(Transform root)
        {
            string path=CircusNightAssets.Art+"/CN_StageShaft.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh==null)
            {
                var vertices=new Vector3[12];var uv=new Vector2[12];var indices=new int[18];
                for(int i=0;i<3;i++)
                {
                    Vector3 side=Quaternion.Euler(0,0,i*60)*Vector3.right;
                    int v=i*4;vertices[v]=-side*.09f;vertices[v+1]=side*.09f;
                    vertices[v+2]=side*2.4f+Vector3.forward;vertices[v+3]=-side*2.4f+Vector3.forward;
                    uv[v]=Vector2.zero;uv[v+1]=Vector2.right;uv[v+2]=Vector2.one;uv[v+3]=Vector2.up;
                    int k=i*6;indices[k]=v;indices[k+1]=v+1;indices[k+2]=v+2;indices[k+3]=v;indices[k+4]=v+2;indices[k+5]=v+3;
                }
                mesh=new Mesh{name="CN_StageShaft",vertices=vertices,uv=uv,triangles=indices};mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);
            }
            string matPath=CircusNightAssets.Materials+"/CN_StageShaft.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if(material==null){material=new Material(Shader.Find("Igruha/CryingAngels/LightShaft"));AssetDatabase.CreateAsset(material,matPath);}
            material.SetColor("_BaseColor",new Color(1f,.84f,.58f,.018f));EditorUtility.SetDirty(material);
            for(int i=0;i<4;i++)
            {
                Vector3 dir=Quaternion.Euler(0,i*90+22.5f,0)*Vector3.forward;
                Vector3 at=dir*10.3f+Vector3.up*14.6f;Vector3 delta=dir*3.3f+Vector3.up*1.8f-at;
                var go=new GameObject("StageShaft_"+i);go.transform.SetParent(root,false);go.transform.position=at;
                go.transform.rotation=Quaternion.LookRotation(delta);go.transform.localScale=new Vector3(1,1,delta.magnitude);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;
                r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
            }
        }

        private static string PhysicsSignature(Transform root)
        {
            var decoration=root.Find("Environment");
            return string.Join("|",root.GetComponentsInChildren<Collider>(true)
                .Where(c=>decoration==null || !c.transform.IsChildOf(decoration))
                .Select(c => c.GetInstanceID()+":"+EditorJsonUtility.ToJson(c)+":"+c.transform.position.ToString("F4")+":"+c.transform.rotation.ToString("F4")+":"+c.transform.lossyScale.ToString("F4")).ToArray());
        }
    }
}
