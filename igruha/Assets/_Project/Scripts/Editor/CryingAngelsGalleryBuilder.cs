using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Build the authored night gallery and cover arrangement; preserve spawns and game rules.</summary>
    public static class CryingAngelsGalleryBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Minigames/CryingAngels.unity";
        private const string GalleryName = "_MoonlitGallery";
        private const int BayCount = 16;
        private const float ArtRadius = 28.08f;
        // Every fourth bay keeps its moon glass; the rest are boarded with dark panes.
        private const int LitBayStep = 4;
        private static readonly string[] HighModels = { "CA_WeepingAngel", "CA_PrayingAngel", "CA_WarningAngel" };
        private static readonly string[] LowModels = { "CA_FallenVisage", "CA_Reliquary", "CA_BrokenPlinth" };

        [MenuItem("Igruha/Minigames/Crying Angels/Apply Moonlit Gallery Art")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before dressing the gallery.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            CryingAngelsGalleryAssets.Import();
            Apply(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("CryingAngels gallery applied: " + Audit());
        }

        internal static void Apply(Scene scene)
        {
            Transform arena = Root(scene, "_Arena").transform;
            var previous = Root(scene, GalleryName, false);
            if (previous != null) Object.DestroyImmediate(previous);
            var gallery = new GameObject(GalleryName);
            SceneManager.MoveGameObjectToScene(gallery, scene);
            var environment = Group(gallery.transform, "Architecture");
            var covers = arena.Find("Covers");
            Transform floor = arena.Find("Floor");
            CryingAngelsGalleryLayout.Build(covers,floor.localScale.x*.5f);
            // A dark mortar surface remains underneath the thin Blender paving mesh.
            floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(CryingAngelsGalleryAssets.Materials + "/CA_BlueSlate.mat");
            float radius = floor.localScale.x * .5f;
            float ratio = radius / ArtRadius;
            var paving = Group(gallery.transform, "MarblePaving");
            paving.position = Vector3.up * floor.GetComponent<Collider>().bounds.max.y;
            paving.localScale = new Vector3(ratio, 1f, ratio);
            for (int i = 0; i < BayCount; i++)
            {
                float angle = i * 360f / BayCount;
                float rad = angle * Mathf.Deg2Rad;
                // Blender X,Y,Z converts to Unity X,Z,-Y with these imported FBX axes.
                Vector3 outward = new Vector3(Mathf.Cos(rad), 0f, -Mathf.Sin(rad));
                var bay = CryingAngelsGalleryAssets.Place("CA_ArchedWallBay", environment,
                    outward * (radius + .42f) + Vector3.down * .10f, Quaternion.Euler(0f, 90f + angle, 0f));
                bay.transform.localScale = new Vector3(ratio, 1f, 1f);
                if(i%LitBayStep!=0)
                {
                    var darkGlass=AssetDatabase.LoadAssetAtPath<Material>(CryingAngelsGalleryAssets.Materials+"/CA_Crevices.mat");
                    foreach(var renderer in bay.GetComponentsInChildren<Renderer>())
                    {
                        var shared=renderer.sharedMaterials;
                        for(int slot=0;slot<shared.Length;slot++) if(shared[slot].name=="CA_MoonGlass") shared[slot]=darkGlass;
                        renderer.sharedMaterials=shared;
                    }
                }

                float columnAngle = rad + Mathf.PI / BayCount;
                Vector3 columnOut = new Vector3(Mathf.Cos(columnAngle), 0f, -Mathf.Sin(columnAngle));
                CryingAngelsGalleryAssets.Place("CA_FlutedColumn", environment, columnOut * (radius + 1.12f) + Vector3.down * .1f, Quaternion.Euler(0, angle, 0));
                var dome = CryingAngelsGalleryAssets.Place("CA_DomeSector", environment, Vector3.zero, Quaternion.Euler(0, angle, 0));
                dome.transform.localScale = new Vector3(ratio,1f,ratio);
                foreach (var renderer in dome.GetComponentsInChildren<Renderer>()) renderer.shadowCastingMode = ShadowCastingMode.Off;
                CryingAngelsGalleryAssets.Place("CA_FloorSector_" + i.ToString("00"), paving, Vector3.zero, Quaternion.identity);
                if (i % 2 == 0)
                {
                    var web = CryingAngelsGalleryAssets.Place("CA_CornerWeb", environment, outward * (radius-.20f) + Vector3.up*.12f, Quaternion.Euler(0,90f+angle,0));
                    web.transform.localScale = Vector3.one * 1.7f;
                }
            }
            var pedestal = arena.Find("Pedestal");
            pedestal.GetComponent<Renderer>().enabled = false;
            CryingAngelsGalleryAssets.Place("CA_KeeperDais", gallery.transform, Vector3.zero, Quaternion.identity);
            foreach (Transform wall in arena.Find("Wall")) wall.GetComponent<Renderer>().enabled = false;
            SetupLighting(scene, gallery.transform, radius);
            SetupAtmosphere(gallery.transform, radius);
            SetupNightSky(gallery.transform);
            CryingAngelsGalleryEffects.Build(gallery.transform, radius);
            CryingAngelsGalleryLayout.AddDetails(gallery.transform, radius);
            SetupKeeperArt();
            Physics.SyncTransforms();
        }

        internal static void FitToUnitBox(GameObject visual)
        {
            // Compute bounds in the parent's local space, before inherited nonuniform blockout scaling.
            var filters = visual.GetComponentsInChildren<MeshFilter>();
            Bounds b = new Bounds(); bool first = true;
            foreach (var filter in filters)
            {
                Matrix4x4 matrix = visual.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Bounds mb = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = mb.center + Vector3.Scale(mb.extents, new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    p = matrix.MultiplyPoint3x4(p);
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                }
            }
            if (first || b.size.x <= 0 || b.size.y <= 0 || b.size.z <= 0) throw new InvalidOperationException("Invalid gallery mesh bounds.");
            Vector3 scale = new Vector3(1f/b.size.x,1f/b.size.y,1f/b.size.z);
            visual.transform.localScale = scale;
            visual.transform.localPosition = -Vector3.Scale(b.center,scale);
        }

        private static void SetupLighting(Scene scene, Transform parent, float radius)
        {
            var existing = Root(scene, "_Lighting");
            foreach (var light in existing.GetComponentsInChildren<Light>(true)) light.enabled = false;
            var lights = Group(parent, "Moonlight");
            var moon = AddLight(lights, "ColdMoon", LightType.Directional, new Vector3(0,12,0), new Color(.66f,.74f,.92f), 2.0f);
            moon.transform.rotation = Quaternion.Euler(48f,-32f,0f);
            moon.shadows = LightShadows.Soft;
            moon.shadowBias = .025f;
            moon.shadowNormalBias = .10f;
            RenderSettings.sun = moon;
            RenderSettings.skybox = null;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = CryingAngelsGalleryAssets.EnsureNightReflection();
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.22f,.27f,.38f);
            RenderSettings.ambientEquatorColor = new Color(.14f,.17f,.26f);
            RenderSettings.ambientGroundColor = new Color(.05f,.06f,.10f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(.010f,.018f,.036f);
            RenderSettings.fogDensity = .017f;
            for (int i = 0; i < BayCount / LitBayStep; i++)
            {
                // Same bay convention as the architecture loop: Blender +X,+Y maps to Unity +X,-Z.
                float a = i * LitBayStep * 360f / BayCount * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a),0f,-Mathf.Sin(a));
                var window = AddLight(lights,"WindowBounce_"+i,LightType.Spot,dir*(radius-1.2f)+Vector3.up*8.5f,new Color(.55f,.72f,1f),20f);
                window.range = 21f; window.spotAngle = 58f; window.innerSpotAngle = 24f;
                window.transform.rotation = Quaternion.LookRotation(dir*(-6f)+Vector3.down*8f);
                window.shadows = LightShadows.None;
            }
            // Moon through the oculus: a soft pool on the dais so the hall has a readable centre.
            var oculus = AddLight(lights,"OculusMoon",LightType.Spot,new Vector3(0f,17.5f,0f),new Color(.62f,.76f,1f),42f);
            oculus.range = 24f; oculus.spotAngle = 78f; oculus.innerSpotAngle = 30f;
            oculus.transform.rotation = Quaternion.Euler(90f,0f,0f);
            oculus.shadows = LightShadows.None;
            var volume = Group(parent, "GalleryGrade").gameObject.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 10;
            string path = CryingAngelsGalleryAssets.Materials + "/CA_GalleryGrade.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile,path); }
            var bloom = Ensure<Bloom>(profile);
            bloom.intensity.Override(.34f); bloom.threshold.Override(.95f); bloom.scatter.Override(.6f);
            Ensure<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var vignette = Ensure<Vignette>(profile);
            vignette.intensity.Override(.34f); vignette.smoothness.Override(.46f); vignette.color.Override(Color.black);
            var grain = Ensure<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Medium1); grain.intensity.Override(.26f); grain.response.Override(.78f);
            var grade = Ensure<ColorAdjustments>(profile);
            grade.saturation.Override(-4f); grade.contrast.Override(12f); grade.colorFilter.Override(new Color(.92f,.95f,1f));
            volume.sharedProfile = profile; EditorUtility.SetDirty(profile);
        }

        private static T Ensure<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component;
            if (!profile.TryGet(out component)) { component = profile.Add<T>(); AssetDatabase.AddObjectToAsset(component, profile); }
            return component;
        }

        private static void SetupAtmosphere(Transform parent,float radius)
        {
            var mat = CryingAngelsGalleryAssets.EnsureMaterial("CA_GroundMist","Igruha/CryingAngels/GalleryMist");
            mat.SetColor("_BaseColor",new Color(.07f,.10f,.16f,.24f)); EditorUtility.SetDirty(mat);
            var go = Group(parent,"FloorMist").gameObject;
            go.transform.localPosition = Vector3.up*.26f;
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed=false; ps.randomSeed=913;
            var main=ps.main; main.loop=true; main.prewarm=true; main.startLifetime=26f; main.startSpeed=.045f;
            main.startSize=new ParticleSystem.MinMaxCurve(3.5f,6.5f); main.startColor=new Color(.74f,.84f,1f,.7f);
            main.maxParticles=80; main.simulationSpace=ParticleSystemSimulationSpace.Local;
            var emission=ps.emission; emission.rateOverTime=2.4f;
            var shape=ps.shape; shape.shapeType=ParticleSystemShapeType.Circle; shape.radius=radius*.86f; shape.rotation=new Vector3(90,0,0); shape.radiusThickness=.75f;
            var size=ps.sizeOverLifetime; size.enabled=true;
            size.size=new ParticleSystem.MinMaxCurve(1f,new AnimationCurve(new Keyframe(0,0),new Keyframe(.15f,1),new Keyframe(.85f,1),new Keyframe(1,0)));
            var renderer=ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=mat;
            renderer.renderMode=ParticleSystemRenderMode.HorizontalBillboard; renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
        }

        /// <summary>Through the oculus the camera would see its clear colour; give it a night sky and a moon instead.</summary>
        private static void SetupNightSky(Transform parent)
        {
            var sky = Group(parent, "NightSky");
            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backdrop.name = "SkyBackdrop"; backdrop.transform.SetParent(sky, false);
            backdrop.transform.localPosition = Vector3.up * 70f; backdrop.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            backdrop.transform.localScale = new Vector3(320f, 320f, 1f);
            var night = CryingAngelsGalleryAssets.EnsureMaterial("CA_NightSky", "Universal Render Pipeline/Unlit");
            night.SetColor("_BaseColor", new Color(.010f, .016f, .034f)); EditorUtility.SetDirty(night);
            Decorate(backdrop, night);
            var moon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            moon.name = "MoonDisc"; moon.transform.SetParent(sky, false);
            // Off-centre so the disc shows through the oculus from the far side of the hall, not straight above the dais.
            moon.transform.localPosition = new Vector3(-9f, 62f, 12f); moon.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            moon.transform.localScale = new Vector3(7.5f, 1f, 7.5f);
            var glow = CryingAngelsGalleryAssets.EnsureMaterial("CA_MoonDisc", "Universal Render Pipeline/Unlit");
            glow.SetColor("_BaseColor", new Color(.86f, .91f, 1f) * 2.4f); EditorUtility.SetDirty(glow);
            Decorate(moon, glow);
        }

        private static void Decorate(GameObject go, Material material)
        {
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }

        private static void SetupKeeperArt()
        {
            const string configPath="Assets/_Project/Settings/Gameplay/Minigames/CryingAngelsConfig.asset";
            var config=AssetDatabase.LoadAssetAtPath<ScriptableObject>(configPath);
            var serialized=new SerializedObject(config);
            serialized.FindProperty("beamColorIdle").colorValue=new Color(1f,.79f,.46f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            const string keeperPath="Assets/_Project/Prefabs/Minigames/CryingAngels/KeeperRig.prefab";
            var root=PrefabUtility.LoadPrefabContents(keeperPath);
            try
            {
                var light=root.GetComponentInChildren<Light>(true);
                light.color=new Color(1f,.79f,.46f);
                light.shadows=LightShadows.Soft;
                light.shadowBias=.01f; light.shadowNormalBias=.06f;
                light.intensity=720f; light.innerSpotAngle=24f; light.shadowCustomResolution=2048;
                light.cookie=CryingAngelsGalleryAssets.EnsureTorchCookie();
                var beam=root.GetComponentInChildren<Igruha.Minigames.CryingAngels.KeeperBeam>(true);
                var beamSo=new SerializedObject(beam); beamSo.FindProperty("intensity").floatValue=720f; beamSo.ApplyModifiedPropertiesWithoutUndo();
                var cone=root.GetComponentInChildren<Igruha.Minigames.CryingAngels.KeeperBeamCone>(true);
                var coneSo=new SerializedObject(cone);coneSo.FindProperty("alpha").floatValue=.16f;coneSo.ApplyModifiedPropertiesWithoutUndo();
                cone.GetComponent<MeshRenderer>().sharedMaterial=CryingAngelsGalleryAssets.EnsureMaterial("CA_KeeperBeam","Igruha/CryingAngels/KeeperBeam");
                SetupBeamDust(cone.transform);
                PrefabUtility.SaveAsPrefabAsset(root,keeperPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void SetupBeamDust(Transform cone)
        {
            var existing=cone.Find("BeamDust");
            if(existing!=null) Object.DestroyImmediate(existing.gameObject);
            var go=Group(cone,"BeamDust").gameObject;
            var ps=go.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed=false; ps.randomSeed=517;
            var main=ps.main; main.loop=true; main.prewarm=true; main.startLifetime=new ParticleSystem.MinMaxCurve(3f,6f);
            main.startSpeed=0f; main.startSize=new ParticleSystem.MinMaxCurve(.035f,.09f); main.startColor=new ParticleSystem.MinMaxGradient(new Color(1f,.9f,.7f,.55f),new Color(1f,.8f,.55f,.9f));
            main.maxParticles=260; main.simulationSpace=ParticleSystemSimulationSpace.Local; main.gravityModifier=-.002f;
            var emission=ps.emission; emission.rateOverTime=36f;
            var shape=ps.shape; shape.shapeType=ParticleSystemShapeType.ConeVolume; shape.radius=.12f; shape.angle=15f; shape.length=22f; shape.arc=360f;
            var velocity=ps.velocityOverLifetime; velocity.enabled=true; velocity.space=ParticleSystemSimulationSpace.Local;
            velocity.x=new ParticleSystem.MinMaxCurve(-.05f,.05f); velocity.y=new ParticleSystem.MinMaxCurve(-.04f,.06f); velocity.z=new ParticleSystem.MinMaxCurve(-.04f,.04f);
            var color=ps.colorOverLifetime; color.enabled=true;
            var gradient=new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0f),new GradientColorKey(Color.white,1f)},
                new[]{new GradientAlphaKey(0f,0f),new GradientAlphaKey(1f,.18f),new GradientAlphaKey(1f,.78f),new GradientAlphaKey(0f,1f)});
            color.color=gradient;
            var renderer=ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial=CryingAngelsGalleryAssets.EnsureMaterial("CA_BeamDust","Igruha/CryingAngels/GalleryDust");
            renderer.renderMode=ParticleSystemRenderMode.Billboard; renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            // A mote drifting past the lens must never become a screen-sized disc.
            renderer.maxParticleSize=.012f;
            SetupTorchLens(go.transform);
        }

        private static void SetupTorchLens(Transform parent)
        {
            // Tiny emissive lens at the apex: bloom turns it into the glare runners look for.
            var lens=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lens.name="TorchLens"; lens.transform.SetParent(parent,false);
            lens.transform.localScale=Vector3.one*.09f; lens.transform.localPosition=Vector3.forward*.22f;
            Object.DestroyImmediate(lens.GetComponent<Collider>());
            var material=CryingAngelsGalleryAssets.EnsureMaterial("CA_TorchLens","Universal Render Pipeline/Unlit");
            material.SetColor("_BaseColor",new Color(1f,.82f,.52f)*6f); EditorUtility.SetDirty(material);
            var renderer=lens.GetComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
        }

        public static string Audit()
        {
            var scene=SceneManager.GetSceneByPath(ScenePath);
            var arena=Root(scene,"_Arena");
            var covers=arena.transform.Find("Covers");
            int high=0,low=0,missing=0;
            foreach(Transform cover in covers)
            {
                if(cover.name.EndsWith("High",StringComparison.Ordinal)) high++; else low++;
                if(cover.Find("_GalleryVisual")==null || cover.GetComponent<Collider>()==null || cover.GetComponent<Renderer>().enabled) missing++;
            }
            int extra=Root(scene,GalleryName).GetComponentsInChildren<Collider>(true).Length;
            int pink=0;
            foreach(var root in scene.GetRootGameObjects()) foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach(var material in renderer.sharedMaterials) if(material==null || material.shader==null || material.shader.name=="Hidden/InternalErrorShader") pink++;
            if(missing>0 || extra>0 || pink>0) throw new InvalidOperationException("Gallery audit failed: missing="+missing+", decor colliders="+extra+", invalid materials="+pink);
            return "covers="+covers.childCount+" (high="+high+", low="+low+"), decor colliders="+extra+", invalid materials="+pink;
        }

        private static Light AddLight(Transform parent,string name,LightType type,Vector3 position,Color color,float intensity)
        {
            var light=Group(parent,name).gameObject.AddComponent<Light>();
            light.type=type; light.transform.localPosition=position; light.color=color; light.intensity=intensity;
            return light;
        }
        private static Transform Group(Transform parent,string name)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);return go.transform;
        }
        private static GameObject Root(Scene scene,string name,bool required=true)
        {
            foreach(var root in scene.GetRootGameObjects()) if(root.name==name) return root;
            if(required) throw new InvalidOperationException("Missing scene root: "+name);
            return null;
        }
    }
}
