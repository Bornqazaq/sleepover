using System;
using System.Linq;
using Igruha.Minigames.Circus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Repeatable lighting-only pass. Does not rebuild cages, scenery or bear assets.</summary>
    public static class CircusShowLightingBuilder
    {
        private const string RigName = "ShowSpotlights";
        private const float TrussRadius = 10.4f;
        private const float TrussHeight = 13.6f;
        private const float BeamLength = 23f;
        private const string Materials = CircusNightAssets.Materials;

        [MenuItem("Igruha/Цирк/Движущиеся прожекторы — обе сцены")]
        public static void ApplyBoth()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the open scene and exit Play Mode first.");
            string opened = EditorSceneManager.GetActiveScene().path;
            try
            {
                foreach (string name in new[] { "Stopwatch", "CansOrder" })
                {
                    var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Minigames/" + name + ".unity");
                    ApplyActive();
                    EditorSceneManager.SaveScene(scene);
                }
                AssetDatabase.SaveAssets();
            }
            finally { if (!string.IsNullOrEmpty(opened)) EditorSceneManager.OpenScene(opened); }
        }

        internal static void ApplyActive()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != "Stopwatch" && scene.name != "CansOrder")
                throw new InvalidOperationException("Show lighting requires a circus scene.");
            var art = GameObject.Find("_CircusNight");
            if (art == null) throw new InvalidOperationException("Missing original circus scenery.");
            Transform previous = art.transform.Find(RigName);
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            // The old static cards float separately from the actual lamps.
            foreach (Transform child in art.transform)
                if (child.name.StartsWith("StageShaft_")) child.gameObject.SetActive(false);
            TuneExistingLights(art.transform.Find("Lighting"));
            var rig = new GameObject(RigName).transform;
            rig.SetParent(art.transform, false);
            var iron = CircusNightAssets.Material("CN_BlackIron");
            var brass = CircusNightAssets.Material("CN_AgedBrass");
            var bears = Object.FindObjectsByType<PitBear>(FindObjectsSortMode.None).OrderBy(b => b.name).ToArray();
            var cages = Object.FindObjectsByType<CageStation>(FindObjectsSortMode.None).OrderBy(c => c.name).ToArray();
            BuildTruss(rig, iron);
            for (int i = 0; i < 12; i++) BuildFixture(rig, i, iron, brass, bears, cages);
            BuildAirDust(rig);
            var oldDust = art.transform.Find("SawdustInTheLight");
            if (oldDust != null) oldDust.gameObject.SetActive(false);
            foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (camera.gameObject.scene != scene) continue;
                var data = camera.GetUniversalAdditionalCameraData();
                data.requiresDepthTexture = true;
                EditorUtility.SetDirty(data);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            DynamicGI.UpdateEnvironment();
        }

        private static void TuneExistingLights(Transform lighting)
        {
            if (lighting == null) throw new InvalidOperationException("Missing circus lighting rig.");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.17f,.18f,.22f);
            RenderSettings.ambientEquatorColor = new Color(.075f,.07f,.085f);
            RenderSettings.ambientGroundColor = new Color(.035f,.028f,.025f);
            RenderSettings.reflectionIntensity = .28f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .010f;
            RenderSettings.fogColor = new Color(.05f,.045f,.065f);
            foreach (var light in lighting.GetComponentsInChildren<Light>(true))
            {
                string name = light.name;
                if (name == "CanvasBounce") light.intensity = .24f;
                else if (name == "Player_SoftFill") light.intensity = .32f;
                else if (name == "Bruno_SoftFill") light.intensity = .15f;
                else if (name == "Bruno_WarmKey") light.intensity = 40f;
                else if (name == "Bruno_Rim") light.intensity = 25f;
                else if (name == "PitReadability") light.intensity = 5f;
                else if (name.StartsWith("CagePool_")) light.intensity = 44f;
                else if (name.StartsWith("CupolaBounce_")) light.intensity = 42f;
                else if (name.StartsWith("CanvasWash_")) light.intensity = 30f;
                else if (name.StartsWith("FairgroundPool_")) light.intensity = 42f;
                else if (name.StartsWith("LanternBounce_")) light.intensity = 14f;
                else if (name.StartsWith("StallPractical_")) light.intensity = 7f;
                else if (name == "MarqueeWarmth") light.intensity = 24f;
                // The two follow spots own the moving shadows, with no extra
                // point-light cubemaps or project-wide rendering changes.
                light.shadows = LightShadows.None;
                EditorUtility.SetDirty(light);
            }
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Materials + "/CN_Grade.asset");
            if (profile != null)
            {
                if (profile.TryGet<ColorAdjustments>(out var grade))
                { grade.postExposure.Override(0f); grade.contrast.Override(12f); grade.saturation.Override(6f); }
                if (profile.TryGet<Bloom>(out var bloom))
                { bloom.intensity.Override(.38f); bloom.threshold.Override(1.08f); }
                if (profile.TryGet<Vignette>(out var vignette)) vignette.intensity.Override(.12f);
                EditorUtility.SetDirty(profile);
            }
        }

        private static void BuildTruss(Transform parent, Material iron)
        {
            const int sides = 24;
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides;
                float b = (i + 1) * Mathf.PI * 2 / sides;
                Vector3 p = new Vector3(Mathf.Sin(a)*TrussRadius,TrussHeight,Mathf.Cos(a)*TrussRadius);
                Vector3 q = new Vector3(Mathf.Sin(b)*TrussRadius,TrussHeight,Mathf.Cos(b)*TrussRadius);
                Rod(parent,"TrussLower_"+i,p,q,.045f,iron);
                Rod(parent,"TrussUpper_"+i,p+Vector3.up*.34f,q+Vector3.up*.34f,.04f,iron);
                Rod(parent,"TrussBrace_"+i,p,q+Vector3.up*.34f,.024f,iron);
                if (i%4 == 0) Rod(parent,"Suspension_"+i,p+Vector3.up*.34f,p+Vector3.up*2.7f,.018f,iron);
            }
        }

        private static void BuildFixture(Transform rig, int index, Material iron, Material brass,
            PitBear[] bears, CageStation[] cages)
        {
            bool follow = index < 4;
            bool rim = index >= 2 && index < 4;
            float degrees = index == 0 ? 15f : index == 1 ? 195f : index == 2 ? 165f : index == 3 ? 345f : (index-4)*45f;
            float angle = degrees*Mathf.Deg2Rad;
            Vector3 radial = new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
            float radius = follow ? 6.2f : TrussRadius;
            if (follow)
            {
                Vector3 inner = radial*radius+Vector3.up*TrussHeight;
                Vector3 outer = radial*TrussRadius+Vector3.up*TrussHeight;
                Rod(rig,"BearSpotArm_"+index,outer,inner,.045f,iron);
                Rod(rig,"BearSpotBrace_"+index,outer+Vector3.up*.34f,inner,.026f,iron);
            }
            var mount = new GameObject((follow ? "BearFollow_" : "SweepSpot_") + (index+1)).transform;
            mount.SetParent(rig,false);
            // Keep animal lights inside the cage ring so suspended floors do
            // not steal their pools of light before they reach the pit.
            mount.localPosition = radial*radius+Vector3.up*(TrussHeight-.65f);
            Rod(mount,"TrussClamp",Vector3.up*.25f,Vector3.up*.67f,.055f,iron);
            var head = new GameObject("MovingHead").transform; head.SetParent(mount,false);
            Color tint = index < 2 ? new Color(1f,.84f,.61f) : rim ? new Color(.48f,.71f,1f) : index%2 == 0 ? new Color(.57f,.76f,1f) : new Color(1f,.86f,.62f);
            var lens = LensMaterial(rim || index%2 == 0 && index >= 4 ? "Cool" : "Warm",tint);
            Primitive(head,"Barrel",PrimitiveType.Cylinder,new Vector3(0,0,.04f),new Vector3(.57f,.32f,.57f),Quaternion.Euler(90,0,0),iron);
            Primitive(head,"LensRim",PrimitiveType.Cylinder,new Vector3(0,0,.37f),new Vector3(.62f,.04f,.62f),Quaternion.Euler(90,0,0),brass);
            Primitive(head,"FresnelLens",PrimitiveType.Cylinder,new Vector3(0,0,.42f),new Vector3(.51f,.012f,.51f),Quaternion.Euler(90,0,0),lens);
            for (int n = 0; n < 3; n++)
                Primitive(head,"CoolingRing_"+n,PrimitiveType.Cylinder,new Vector3(0,0,-.18f+n*.13f),new Vector3(.63f,.015f,.63f),Quaternion.Euler(90,0,0),iron);
            for (int side = -1; side <= 1; side += 2)
            {
                Primitive(head,"Yoke_"+side,PrimitiveType.Cube,new Vector3(side*.37f,.18f,0),new Vector3(.07f,.53f,.12f),Quaternion.identity,brass);
                Primitive(head,"SideFlag_"+side,PrimitiveType.Cube,new Vector3(side*.35f,0,.58f),new Vector3(.22f,.56f,.025f),Quaternion.Euler(0,side*-34f,0),iron);
                Primitive(head,"TopFlag_"+side,PrimitiveType.Cube,new Vector3(0,side*.35f,.58f),new Vector3(.56f,.22f,.025f),Quaternion.Euler(side*34f,0,0),iron);
            }
            var emitter = new GameObject("SpotLight").transform; emitter.SetParent(head,false);emitter.localPosition=Vector3.forward*.46f;
            var light = emitter.gameObject.AddComponent<Light>();
            light.type = LightType.Spot; light.color = tint; light.intensity = index<2 ? 950f : rim ? 650f : 420f;
            light.range = follow ? BeamLength : 32f; light.spotAngle = index<2 ? 36f : rim ? 30f : 22f; light.innerSpotAngle = light.spotAngle*.6f;
            if (!follow) light.cookie = GoboTexture();
            light.shadows = index<2 ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = .82f;light.shadowBias = .018f;light.shadowNormalBias = .08f;
            light.shadowNearPlane = .25f;light.shadowCustomResolution = 1024;
            var data = emitter.gameObject.AddComponent<UniversalAdditionalLightData>();
            data.renderingLayers = 7;data.customShadowLayers = true;data.shadowRenderingLayers = 7;
            data.usePipelineSettings = false;
            BuildBeam(emitter,tint,light.spotAngle,follow,rim);
            var motion = mount.gameObject.AddComponent<CircusSpotlight>();
            var so = new SerializedObject(motion);
            so.FindProperty("head").objectReferenceValue = head;
            so.FindProperty("spotlight").objectReferenceValue = light;
            so.FindProperty("fixtureIndex").intValue = index;
            so.FindProperty("followBear").boolValue = follow;
            so.FindProperty("phraseSeconds").floatValue = 4.63f+(index*1.319f)%4.7f;
            so.FindProperty("turnDegreesPerSecond").floatValue = follow ? 48f : 38f+index%3*7f;
            so.FindProperty("phaseOffset").floatValue = 27.43f+index*13.79f;
            SetArray(so,"bears",bears);SetArray(so,"cages",cages);
            so.ApplyModifiedPropertiesWithoutUndo();
            head.rotation = Quaternion.LookRotation(motion.EvaluateAim(0) - head.position);
        }

        private static void BuildBeam(Transform emitter, Color tint, float cone, bool follow, bool rim)
        {
            float length = follow ? BeamLength : 32f;
            float radius = Mathf.Tan(cone*.5f*Mathf.Deg2Rad)*length;
            string key = rim ? "Rim" : follow ? "Follow" : tint.b>tint.r ? "Cool" : "Warm";
            string path = Materials + "/CN_ShowBeam_"+key+".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Igruha/Circus/SpotlightBeam");
                if (shader == null) throw new InvalidOperationException("Compile the circus beam shader first.");
                material = new Material(shader); AssetDatabase.CreateAsset(material,path);
            }
            material.SetColor("_BaseColor",tint);material.SetFloat("_Density",follow?.048f:.058f);
            material.SetFloat("_Radius",radius);material.SetFloat("_Length",length);material.SetFloat("_SourceRadius",.18f);
            EditorUtility.SetDirty(material);
            var go = new GameObject("HazeBeam"); go.transform.SetParent(emitter,false);
            go.AddComponent<MeshFilter>().sharedMesh = BeamBounds(radius,length,key);
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static Mesh BeamBounds(float r,float length,string key)
        {
            string path = CircusNightAssets.Art+"/CN_ShowBeam_"+key+".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) {mesh = new Mesh {name="Spotlight volume bounds"};AssetDatabase.CreateAsset(mesh,path);}
            mesh.Clear();
            // A tight cone proxy avoids shading the empty corners of large boxes.
            const int sides=32;
            var vertices=new Vector3[sides*2+2];
            var triangles=new int[sides*12];
            vertices[sides*2+1]=Vector3.forward*length;
            for(int i=0;i<sides;i++)
            {
                float angle=i*Mathf.PI*2/sides;
                Vector3 radial=new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0);
                vertices[i]=radial*.18f;vertices[i+sides]=radial*r+Vector3.forward*length;
                int next=(i+1)%sides,k=i*12;
                triangles[k]=i;triangles[k+1]=next;triangles[k+2]=i+sides;
                triangles[k+3]=next;triangles[k+4]=next+sides;triangles[k+5]=i+sides;
                triangles[k+6]=sides*2;triangles[k+7]=next;triangles[k+8]=i;
                triangles[k+9]=sides*2+1;triangles[k+10]=i+sides;triangles[k+11]=next+sides;
            }
            mesh.vertices=vertices;mesh.triangles=triangles;
            mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);return mesh;
        }

        private static Texture2D GoboTexture()
        {
            const int size=128;
            string path=CircusNightAssets.Art+"/CN_SpotGobo.asset";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(texture != null) return texture;
            texture=new Texture2D(size,size,TextureFormat.RGBA32,true,true)
            {name="Soft theatrical breakup",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            var pixels=new Color[size*size];
            for(int y=0;y<size;y++) for(int x=0;x<size;x++)
            {
                float u=(x+.5f)/size*2-1, v=(y+.5f)/size*2-1;
                float radius=Mathf.Sqrt(u*u+v*v), angle=Mathf.Atan2(v,u);
                float lobes=.5f+.5f*Mathf.Sin(angle*5+radius*7);
                float pattern=Mathf.Lerp(.34f,1f,Mathf.SmoothStep(0,1,lobes));
                float value=pattern*(1-Mathf.SmoothStep(.68f,1f,radius));
                pixels[y*size+x]=new Color(value,value,value,1);
            }
            texture.SetPixels(pixels);texture.Apply();AssetDatabase.CreateAsset(texture,path);return texture;
        }

        private static void BuildAirDust(Transform rig)
        {
            var go=new GameObject("DustInMovingBeams");go.transform.SetParent(rig,false);
            go.transform.localPosition=new Vector3(0,5.5f,0);
            var particles=go.AddComponent<ParticleSystem>();
            particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.useAutoRandomSeed=false;particles.randomSeed=1793;
            var main=particles.main;main.loop=true;main.prewarm=true;main.duration=18;
            main.startLifetime=18;main.startSpeed=.065f;main.startSize=new ParticleSystem.MinMaxCurve(.018f,.043f);
            main.maxParticles=280;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.startColor=Color.clear;main.playOnAwake=true;
            var emission=particles.emission;emission.rateOverTime=15;
            var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(18,9,18);
            var velocity=particles.velocityOverLifetime;velocity.enabled=true;
            velocity.x=.035f;velocity.y=-.022f;velocity.z=.015f;
            var noise=particles.noise;noise.enabled=true;noise.strength=.13f;noise.frequency=.24f;noise.scrollSpeed=.08f;
            var renderer=go.GetComponent<ParticleSystemRenderer>();
            string path=Materials+"/CN_BeamDust.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find("Igruha/Circus/BeamDust"));AssetDatabase.CreateAsset(material,path);}
            renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false;renderer.lightProbeUsage=LightProbeUsage.Off;
            var scatter=go.AddComponent<CircusAirDust>();
            var so=new SerializedObject(scatter);
            SetArray(so,"spotlights",rig.GetComponentsInChildren<CircusSpotlight>());
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material LensMaterial(string key,Color tint)
        {
            string path = Materials+"/CN_SpotLens_"+key+".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) {material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_BaseColor",tint);material.SetColor("_EmissionColor",tint*4f);
            material.EnableKeyword("_EMISSION");material.SetFloat("_Smoothness",.8f);
            EditorUtility.SetDirty(material);return material;
        }

        private static void SetArray(SerializedObject so,string name,Object[] values)
        {
            var array=so.FindProperty(name);array.arraySize=values.Length;
            for(int i=0;i<values.Length;i++)array.GetArrayElementAtIndex(i).objectReferenceValue=values[i];
        }

        private static GameObject Primitive(Transform parent,string name,PrimitiveType shape,Vector3 at,Vector3 scale,Quaternion rotation,Material material)
        {
            var go=GameObject.CreatePrimitive(shape);go.name=name;go.transform.SetParent(parent,false);
            go.transform.localPosition=at;go.transform.localRotation=rotation;go.transform.localScale=scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var renderer=go.GetComponent<Renderer>();renderer.sharedMaterial=material;renderer.renderingLayerMask=3;
            renderer.shadowCastingMode=ShadowCastingMode.Off;return go;
        }

        private static void Rod(Transform parent,string name,Vector3 a,Vector3 b,float radius,Material material)
        {
            Vector3 delta=b-a;
            Primitive(parent,name,PrimitiveType.Cylinder,(a+b)*.5f,new Vector3(radius*2,delta.magnitude*.5f,radius*2),
                Quaternion.FromToRotation(Vector3.up,delta),material);
        }
    }
}
