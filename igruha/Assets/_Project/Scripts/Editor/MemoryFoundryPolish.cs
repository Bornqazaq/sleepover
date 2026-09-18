using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Igruha.Minigames.MemoryRun;

namespace Igruha.EditorTools
{
    internal static class MemoryFoundryPolish
    {
        private const float TrussTop = -6.9f;
        internal static void Build(Transform arena, MemoryRunConfig config, GameObject manager)
        {
            var structure = new GameObject("PlatformStructure").transform;
            structure.SetParent(arena, false);
            for (int row = 0; row < config.Steps; row++)
            {
                float z = config.StepZ(row);
                for (int lane = 0; lane < MemoryRunConfig.LaneCount; lane++)
                {
                    var support = MemoryFoundryAssets.Place(structure, "PlateSupport", new Vector3(config.LaneX(lane), 0, z));
                    support.gameObject.layer = LayerMask.NameToLayer("Ground");
                    var col = support.gameObject.AddComponent<BoxCollider>();
                    col.center = new Vector3(0, -3.85f, 0); col.size = new Vector3(.62f, 5.9f, .62f);
                }
                foreach (float y in new[] { TrussTop, TrussTop - 1.35f })
                {
                    var beam = MemoryFoundryBuilder.Box(structure, "CrossTrussChord", new Vector3(0,y,z),
                        new Vector3(config.HallWidth, .18f, .4f), "Iron");
                    SolidBox(beam.transform);
                }
                int sections = Mathf.CeilToInt(config.HallWidth / 2);
                float pitch = config.HallWidth / sections;
                for (int section = 0; section < sections; section++)
                {
                    float x = -config.HallWidth / 2 + (section + .5f) * pitch;
                    var web = MemoryFoundryBuilder.Box(structure, "TrussDiagonal", new Vector3(x,TrussTop-.675f,z),
                        new Vector3(Mathf.Sqrt(pitch*pitch+1.35f*1.35f),.13f,.2f), "Steel");
                    web.transform.localRotation = Quaternion.Euler(0,0,(section%2==0?1:-1)*Mathf.Atan2(1.35f,pitch)*Mathf.Rad2Deg);
                }
                foreach (int side in new[] {-1,1})
                {
                    var anchor = MemoryFoundryBuilder.Box(structure, "WallBearing", new Vector3(side*(config.HallWidth/2-.45f),TrussTop-.6f,z), new Vector3(.9f,2.2f,1.4f), "Concrete");
                    SolidBox(anchor.transform);
                }
            }
            var environment = arena.Find("_Environment");
            // Preserve the real holes: static mesh collision follows masonry and steel exactly.
            foreach (var mesh in environment.Find("FracturedShell").GetComponentsInChildren<MeshFilter>())
            {
                if (!mesh.sharedMesh.name.StartsWith("MF_Ruin")) continue;
                mesh.gameObject.layer = LayerMask.NameToLayer("Ground");
                var col = mesh.gameObject.AddComponent<MeshCollider>(); col.sharedMesh = mesh.sharedMesh;
            }
            foreach (string facade in new[] {"StartFacade", "ExitFacade"})
            foreach (Transform child in environment.Find(facade))
            {
                if (child.name == "MasonryPier" || child.name == "MainColumn" || child.name == "DoorSurround"
                    || child.name == "UpperPortalMasonry") SolidBox(child);
                if (child.name == "MF_Switchboard")
                {
                    child.gameObject.layer = LayerMask.NameToLayer("Ground");
                    var col = child.gameObject.AddComponent<BoxCollider>();
                    col.center = new Vector3(0,1.05f,0); col.size = new Vector3(2.2f,2.1f,.6f);
                }
            }
            BuildShafts(environment);
            MemoryRunBodyCollisionBuilder.Build(arena);
            foreach (var light in environment.GetComponentsInChildren<Light>())
            {
                if (light.type != LightType.Spot || light.shadows == LightShadows.None) continue;
                var data = light.GetComponent<UniversalAdditionalLightData>();
                if (data == null) data = light.gameObject.AddComponent<UniversalAdditionalLightData>();
                data.usePipelineSettings = false;
                var settings = new SerializedObject(data);
                settings.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            var presentation = manager.GetComponent<MemoryRunFallPresentation>();
            if (presentation == null) presentation = manager.AddComponent<MemoryRunFallPresentation>();
            var so = new SerializedObject(presentation);
            so.FindProperty("fallingClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/_Project/Art/Animations/Karlan(Fbx without color)@Flying Back Death.fbx");
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SolidBox(Transform transform)
        {
            transform.gameObject.layer = LayerMask.NameToLayer("Ground");
            if (transform.GetComponent<Collider>() == null) transform.gameObject.AddComponent<BoxCollider>();
        }

        private static void BuildShafts(Transform environment)
        {
            const string meshPath = MemoryFoundryAssets.Art + "/Models/MF_LightShaft.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                var vertices = new Vector3[12]; var uv = new Vector2[12]; var triangles = new int[18];
                for (int i=0;i<3;i++)
                {
                    Vector3 right = Quaternion.Euler(0,0,i*60)*Vector3.right;
                    int v=i*4,t=i*6;
                    vertices[v]=-right*.28f; vertices[v+1]=right*.28f;
                    vertices[v+2]=right*2.5f+Vector3.forward; vertices[v+3]=-right*2.5f+Vector3.forward;
                    uv[v]=Vector2.zero;uv[v+1]=Vector2.right;uv[v+2]=Vector2.one;uv[v+3]=Vector2.up;
                    triangles[t]=v;triangles[t+1]=v+1;triangles[t+2]=v+2;triangles[t+3]=v;triangles[t+4]=v+2;triangles[t+5]=v+3;
                }
                mesh = new Mesh { name="MF_LightShaft",vertices=vertices,uv=uv,triangles=triangles };
                mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh,meshPath);
            }
            const string matPath = MemoryFoundryAssets.Materials + "/MF_WindowShaft.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if(material==null) { material=new Material(Shader.Find("Igruha/CryingAngels/LightShaft"));AssetDatabase.CreateAsset(material,matPath); }
            material.shader = Shader.Find("Igruha/CryingAngels/LightShaft");
            material.shaderKeywords = new string[0];
            material.renderQueue = (int)RenderQueue.Transparent - 10;
            material.SetColor("_BaseColor",new Color(.56f,.71f,.82f,.09f)); EditorUtility.SetDirty(material);
            for (int i=0;i<4;i++)
            {
                var beam = new GameObject("WindowLightShaft_"+i);beam.transform.SetParent(environment,false);
                beam.transform.localPosition = new Vector3(i%2==0?16.7f:-16.7f,13.8f,-12+i*9);
                Vector3 direction = new Vector3(i%2==0?-13:13,-17,5);
                beam.transform.localRotation=Quaternion.LookRotation(direction);
                beam.transform.localScale=new Vector3(1.2f,1.2f,direction.magnitude);
                beam.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=beam.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                var lamp = new GameObject("WindowPool").AddComponent<Light>();lamp.transform.SetParent(environment,false);
                lamp.transform.localPosition=beam.transform.localPosition;lamp.transform.localRotation=beam.transform.localRotation;
                lamp.type=LightType.Spot;lamp.spotAngle=38;lamp.innerSpotAngle=24;lamp.range=31;
                lamp.intensity=24;lamp.color=new Color(.64f,.77f,.92f);lamp.shadows=LightShadows.Soft;
                lamp.shadowStrength=.9f;lamp.shadowBias=.025f;lamp.shadowNormalBias=.15f;
            }
        }
    }
}
