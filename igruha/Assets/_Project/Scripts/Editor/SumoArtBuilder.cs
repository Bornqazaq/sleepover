using System;
using System.Collections.Generic;
using System.IO;
using Igruha.Minigames.SumoRing;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
using static Igruha.EditorTools.SumoArenaBuilder;

namespace Igruha.EditorTools
{
    /// <summary>Original Blender kit and scene-local lighting. Never changes the shared camera or characters.</summary>
    public static class SumoArtBuilder
    {
        private static readonly Dictionary<string, Material> palette = new Dictionary<string, Material>();
        private static Transform root;
        [MenuItem("Igruha/Minigames/Art Sumo Ring")]
        public static void Build()
        {
            RequireScene();
            var old = GameObject.Find("_SumoArt"); if (old != null) Object.DestroyImmediate(old);
            root = new GameObject("_SumoArt").transform;
            Palette();
            Material("Sand", new Color(.83f,.73f,.52f)); Material("Clay", new Color(.44f,.235f,.115f));
            Material("Rope", new Color(.83f,.73f,.52f)); Material("Crack", new Color(.19f,.07f,.027f));
            var floor = GameObject.Find("SafeSand"); floor.transform.localScale = new Vector3(54,.4f,54);
            floor.GetComponent<Renderer>().sharedMaterial = Material("FloorSand", new Color(.55f,.46f,.34f));
            var config = AssetDatabase.LoadAssetAtPath<SumoConfig>(Settings + "SumoConfig.asset");
            // Center uses the same clay section as every shrinking ring; broad warm top is continuous.
            var centre = GameObject.Find("PermanentCentre");
            var centerMesh = Disk(config.CentreRadius,config.Height);
            centerMesh=SaveMesh(centerMesh,"Centre"); centre.transform.localPosition = Vector3.zero; centre.transform.localScale = Vector3.one;
            centre.GetComponent<MeshFilter>().sharedMesh = centerMesh; centre.GetComponent<MeshCollider>().sharedMesh = centerMesh;
            centre.GetComponent<Renderer>().sharedMaterials = new[] { palette["Sand"], palette["Clay"] };
            Model("SM_Canopy",new Vector3(0,8.6f,0),0);
            foreach (float x in new[] {-9.65f,9.65f}) foreach(float z in new[] {-9.65f,9.65f})
            {
                Model("SM_Tassel",new Vector3(x,6.1f,z),0);
                Model("SM_Chain",new Vector3(x,8.7f,z),0);
            }
            // Lantern cadence leaves the middle of every viewing axis open.
            for (int side=0;side<4;side++) for (int i=0;i<7;i++)
            {
                Vector3 p=Quaternion.Euler(0,side*90,0)*new Vector3(-7.5f+i*2.5f,7.4f,9.45f);
                Model("SM_Lantern",p,side*90,1.35f);
            }
            // Clear apron: all decorative objects sit beyond the falling edge, with no camera colliders.
            for (int side=0;side<4;side++)
            {
                float angle=45+side*90;
                Model("SM_Taiko",Polar(12.2f,angle),180-angle,1.35f);
                Model("SM_Banner",Polar(14.2f,angle-9),180-angle,1.15f);
                Model("SM_Bench",Polar(12.6f,angle+30),-angle-30,1.05f);
                Model("SM_Bucket",Polar(10.4f,angle+7),angle,1.15f);
            }
            Model("SM_Bell",new Vector3(11.5f,0,-3.8f),-70,1.2f);
            Model("SM_RopeCoil",new Vector3(-6.5f,.015f,-9.8f),-15,1.3f);
            Model("SM_Bucket",new Vector3(-7.4f,0,-10.2f),-30);
            // Tiny pebbles and shallow raked arcs break up the apron without competing with the ring.
            for (int i=0;i<16;i++)
            {
                float a=i*22.5f+6;var points=new Vector3[18];
                for(int j=0;j<points.Length;j++) points[j]=Polar(10.05f+(i%3)*.16f,a+j*.7f)+Vector3.up*.012f;
                Line(root,"RakedSand",points,.017f,palette["SandShade"]);
            }
            Stands(); ClaySections(config); RopeBoundaries(config); Dust(config); Lighting();
            Save(); Debug.Log("SUMO ART: original 11-model kit, suspended canopy, 28 lanterns, audience, rope and collapse dust.");
        }
        private static void Palette()
        {
            palette.Clear();
            void P(string name,float r,float g,float b) => palette[name]=Material(name,new Color(r,g,b));
            P("Wood",.24f,.095f,.042f);P("WoodEdge",.40f,.19f,.08f);P("Straw",.68f,.45f,.19f);P("StrawLight",.82f,.62f,.30f);P("StrawShadow",.45f,.27f,.10f);
            P("Rope",.83f,.73f,.52f);P("Paper",.95f,.75f,.39f);P("Ivory",.84f,.77f,.59f);P("Ink",.07f,.045f,.03f);P("Iron",.105f,.095f,.085f);P("Bronze",.26f,.25f,.135f);
            P("Cloth",.61f,.43f,.21f);P("Vermilion",.43f,.095f,.042f);P("Audience",.07f,.067f,.08f);P("Sand",.83f,.73f,.52f);P("Clay",.44f,.235f,.115f);P("SandShade",.31f,.23f,.135f);P("Hall",.009f,.011f,.018f);
            var paper=palette["Paper"];paper.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;paper.SetColor("_EmissionColor",new Color(1f,.65f,.28f)*3f);paper.EnableKeyword("_EMISSION");EditorUtility.SetDirty(paper);
            palette["Bronze"].SetFloat("_Metallic",.55f);palette["Iron"].SetFloat("_Metallic",.4f);
            // Two-sided cloth keeps banners legible from inside and outside the ring.
            palette["Cloth"].SetFloat("_Cull",0);
        }
        private static GameObject Model(string name,Vector3 p,float yaw,float scale=1)
        {
            string path=Art+"/Models/"+name+".fbx";
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(prefab==null)throw new InvalidOperationException("Missing Blender model: "+path);
            var go=new GameObject(name);go.transform.SetParent(root,false);
            // Preserve the FBX's metre/unit conversion and authored axis rotation on its child.
            var imported=(GameObject)PrefabUtility.InstantiatePrefab(prefab);imported.transform.SetParent(go.transform,false);
            go.transform.localPosition=p;go.transform.localRotation=Quaternion.Euler(0,yaw,0);go.transform.localScale=Vector3.one*scale;
            foreach(var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats=r.sharedMaterials;
                for(int i=0;i<mats.Length;i++)
                {
                    string key=mats[i].name.Replace("SM_","");int suffix=key.IndexOf('.');if(suffix>=0)key=key.Substring(0,suffix);
                    if(!palette.TryGetValue(key,out var material))throw new InvalidOperationException("Unknown Blender material: "+key);
                    mats[i]=material;
                }
                r.sharedMaterials=mats;
                if(name=="SM_Canopy")r.shadowCastingMode=ShadowCastingMode.Off;
            }
            return go;
        }
        private static Vector3 Polar(float r,float degrees) { float a=degrees*Mathf.Deg2Rad;return new Vector3(Mathf.Sin(a)*r,0,Mathf.Cos(a)*r); }
        private static void ClaySections(SumoConfig config)
        {
            // The support stays exact; only the lower clay silhouette bulges and softens.
            for(int ring=0;ring<config.RingCount;ring++)
            {
                var original=AssetDatabase.LoadAssetAtPath<Mesh>(Art+"/Meshes/Ring_"+ring+".asset");
                var vertices=new List<Vector3>(original.vertices);var top=new List<int>(original.GetTriangles(0));var sides=new List<int>(original.GetTriangles(1));
                float outer=config.OuterRadius(ring);
                // Remove the straight external wall; add a gently rounded profile with shared smooth normals.
                for(int k=sides.Count-3;k>=0;k-=3)
                {
                    bool external=true;
                    for(int n=0;n<3;n++){var p=vertices[sides[k+n]];external &= Mathf.Abs(new Vector2(p.x,p.z).magnitude-outer)<.01f;}
                    if(external)sides.RemoveRange(k,3);
                }
                int start=vertices.Count;float[] heights={0,.2f,.7f,config.Height-.13f,config.Height};float[] bulge={.14f,.22f,.12f,.018f,0};
                for(int row=0;row<heights.Length;row++)for(int i=0;i<=12;i++)
                {float a=i*Mathf.PI/144;float r=outer+bulge[row];vertices.Add(new Vector3(Mathf.Cos(a)*r,heights[row],Mathf.Sin(a)*r));}
                for(int row=0;row<heights.Length-1;row++)for(int i=0;i<12;i++)
                {int a=start+row*13+i;sides.AddRange(new[]{a,a+13,a+14,a,a+14,a+1});}
                var mesh=new Mesh{name="SculptedClayRing",subMeshCount=2};mesh.SetVertices(vertices);mesh.SetTriangles(top,0);mesh.SetTriangles(sides,1);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh=SaveMesh(mesh,"ClayArt_"+ring);
                foreach(var segment in Object.FindObjectsByType<SumoRingSegment>(FindObjectsSortMode.None))
                    if(segment.Ring==ring)segment.transform.Find("ClayVisual").GetComponent<MeshFilter>().sharedMesh=mesh;
            }
            // Traditional starting strokes stay on the permanent centre.
            var parent=GameObject.Find("PermanentCentre").transform;
            foreach(float z in new[]{-.55f,.55f})
            {
                string name=z<0?"StartStroke_A":"StartStroke_B";var old=parent.Find(name);if(old!=null)Object.DestroyImmediate(old.gameObject);
                Line(parent,name,new[]{new Vector3(-.6f,config.Height+.009f,z),new Vector3(.6f,config.Height+.009f,z)},.055f,palette["Ivory"]);
            }
        }
        private static void Stands()
        {
            for(int side=0;side<12;side++)
            {
                float angle=side*30;var wall=Box(root,"HallWall",Polar(25,angle)+Vector3.up*9,new Vector3(13.5f,18,.45f),palette["Hall"],false);wall.transform.localRotation=Quaternion.Euler(0,angle,0);
                // A generous doorway/aisle on every cardinal axis.
                if(side%3==0)continue;
                for(int tier=0;tier<3;tier++)
                {
                    float radius=16.8f+tier*1.8f,height=.55f+tier*.62f;
                    var step=Box(root,"AudienceTier",Polar(radius,angle)+Vector3.up*height*.5f,new Vector3(8.6f,height,1.7f),palette["Wood"],false);step.transform.localRotation=Quaternion.Euler(0,angle,0);
                    for(int seat=0;seat<8;seat++)
                    {
                        Vector3 offset=Quaternion.Euler(0,angle,0)*new Vector3((seat-3.5f)*1.03f,0,0);
                        var spectator=Model("SM_Spectator",Polar(radius,angle)+offset+Vector3.up*height,angle, .85f+(seat+side)%4*.07f);
                        foreach(var r in spectator.GetComponentsInChildren<Renderer>())r.shadowCastingMode=ShadowCastingMode.Off;
                    }
                }
                var rail=Box(root,"FrontRail",Polar(15.8f,angle)+Vector3.up*.5f,new Vector3(8.9f,.8f,.16f),palette["WoodEdge"],false);rail.transform.localRotation=Quaternion.Euler(0,angle,0);
            }
        }
        private static void RopeBoundaries(SumoConfig config)
        {
            var arena=GameObject.Find("_Arena").transform;
            for(int ring=0;ring<=config.RingCount;ring++)
            {
                var boundary=arena.Find("Boundary_"+ring);if(boundary==null)continue;
                var line=boundary.GetComponent<LineRenderer>();if(line!=null)Object.DestroyImmediate(line);
                Clear(boundary);
                float radius=(ring<config.RingCount?config.OuterRadius(ring):config.CentreRadius)-.065f;
                var mesh=SaveMesh(RopeMesh(radius,config.Height+.057f),"Rope_"+ring);
                var filter=boundary.GetComponent<MeshFilter>();if(filter==null)filter=boundary.gameObject.AddComponent<MeshFilter>();filter.sharedMesh=mesh;
                var renderer=boundary.GetComponent<MeshRenderer>();if(renderer==null)renderer=boundary.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=palette["Rope"];
            }
        }
        private static Mesh RopeMesh(float radius,float height)
        {
            int steps=Mathf.CeilToInt(radius*80),sides=5;var vs=new List<Vector3>();var tris=new List<int>();
            for(int strand=0;strand<3;strand++)
            {
                int start=vs.Count;
                for(int i=0;i<=steps;i++)
                {
                    float a=i*Mathf.PI*2/steps;Vector3 radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));float twist=a*radius*12+strand*Mathf.PI*2/3;
                    Vector3 center=radial*(radius+Mathf.Cos(twist)*.026f)+Vector3.up*(height+Mathf.Sin(twist)*.026f);
                    for(int j=0;j<sides;j++){float t=j*Mathf.PI*2/sides;vs.Add(center+radial*Mathf.Cos(t)*.030f+Vector3.up*Mathf.Sin(t)*.030f);}
                }
                for(int i=0;i<steps;i++)for(int j=0;j<sides;j++)
                {int a=start+i*sides+j,b=start+i*sides+(j+1)%sides;tris.AddRange(new[]{a,b,b+sides,a,b+sides,a+sides});}
            }
            var mesh=new Mesh{name="BraidedDohyoRope",indexFormat=IndexFormat.UInt32};mesh.SetVertices(vs);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static Mesh Disk(float radius,float height)
        {
            var vs=new List<Vector3>();var top=new List<int>();var side=new List<int>();
            for(int i=0;i<96;i++)
            {
                float a=i*Mathf.PI/48,b=(i+1)*Mathf.PI/48;Vector3 p=new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius),q=new Vector3(Mathf.Cos(b)*radius,0,Mathf.Sin(b)*radius);
                int n=vs.Count;vs.AddRange(new[]{Vector3.up*height,q+Vector3.up*height,p+Vector3.up*height,p,p+Vector3.up*height,q+Vector3.up*height,q});
                top.AddRange(new[]{n,n+1,n+2});side.AddRange(new[]{n+3,n+4,n+5,n+3,n+5,n+6});
            }
            var mesh=new Mesh{name="PermanentClayCentre",subMeshCount=2};mesh.SetVertices(vs);mesh.SetTriangles(top,0);mesh.SetTriangles(side,1);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static Mesh SaveMesh(Mesh mesh,string name)
        {
            string path=Art+"/Meshes/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(old!=null) { EditorUtility.CopySerialized(mesh,old); Object.DestroyImmediate(mesh); return old; }
            else AssetDatabase.CreateAsset(mesh,path);
            return mesh;
        }
        private static void Dust(SumoConfig config)
        {
            var material=Material("Dust",new Color(.62f,.43f,.23f));
            material.shader=Shader.Find("Universal Render Pipeline/Particles/Unlit");
            string texturePath=Art+"/Meshes/DustFalloff.asset";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if(texture==null)
            {
                texture=new Texture2D(32,32,TextureFormat.RGBA32,false){name="SoftDustFalloff",wrapMode=TextureWrapMode.Clamp};
                for(int y=0;y<32;y++)for(int x=0;x<32;x++)
                {float radius=new Vector2((x-15.5f)/15.5f,(y-15.5f)/15.5f).magnitude;texture.SetPixel(x,y,new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-radius),1.5f)));}
                texture.Apply();AssetDatabase.CreateAsset(texture,texturePath);
            }
            material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);
            material.SetFloat("_Surface",1);material.SetFloat("_Blend",0);material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);material.SetFloat("_ZWrite",0);material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.renderQueue=3000;
            var go=new GameObject("CollapseDust");
                var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=ps.main;main.loop=false;main.playOnAwake=false;main.duration=.4f;main.startLifetime=new ParticleSystem.MinMaxCurve(.45f,.85f);main.startSpeed=new ParticleSystem.MinMaxCurve(.4f,1.2f);main.startSize=new ParticleSystem.MinMaxCurve(.08f,.23f);main.startColor=new Color(.78f,.63f,.40f,.55f);main.gravityModifier=.4f;main.maxParticles=24;main.simulationSpace=ParticleSystemSimulationSpace.World;
                var emission=ps.emission;emission.rateOverTime=0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,14)});
                var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.35f;
                var color=ps.colorOverLifetime;color.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(.65f,0),new GradientAlphaKey(0,1)});color.color=gradient;
                var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
            Directory.CreateDirectory(Art+"/Prefabs");AssetDatabase.Refresh();
            var prefab=PrefabUtility.SaveAsPrefabAsset(go,Art+"/Prefabs/CollapseDust.prefab");Object.DestroyImmediate(go);
            foreach(var segment in Object.FindObjectsByType<SumoRingSegment>(FindObjectsSortMode.None))
            {
                var old=segment.transform.Find("CollapseDust");if(old!=null)Object.DestroyImmediate(old.gameObject);
                var dust=(GameObject)PrefabUtility.InstantiatePrefab(prefab);dust.transform.SetParent(segment.transform,false);
                float a=7.5f*Mathf.Deg2Rad,r=config.OuterRadius(segment.Ring)-config.RingWidth*.5f;
                dust.transform.localPosition=new Vector3(Mathf.Cos(a)*r,config.Height-.1f,Mathf.Sin(a)*r);
                Set(segment,"dust",dust.GetComponent<ParticleSystem>());
            }
        }
        private static void Lighting()
        {
            foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))light.enabled=false;
            RenderSettings.skybox=null;RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.38f,.39f,.45f);RenderSettings.ambientEquatorColor=new Color(.31f,.27f,.22f);RenderSettings.ambientGroundColor=new Color(.16f,.12f,.08f);
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.025f,.022f,.031f);RenderSettings.fogStartDistance=28;RenderSettings.fogEndDistance=65;
            Light New(string name,Vector3 p,Color color,float intensity,LightType type)
            {var l=new GameObject(name).AddComponent<Light>();l.transform.SetParent(root,false);l.transform.localPosition=p;l.type=type;l.color=color;l.intensity=intensity;return l;}
            var fill=New("WarmSoftFill",new Vector3(0,12,0),new Color(1f,.90f,.75f),1.2f,LightType.Directional);fill.transform.rotation=Quaternion.Euler(60,-25,0);fill.shadows=LightShadows.Soft;fill.shadowStrength=.72f;fill.shadowBias=.03f;fill.shadowNormalBias=.3f;
            for(int i=0;i<4;i++)
            {
                var p=Polar(5,45+i*90)+Vector3.up*8;
                var light=New("CanopyPool",p,new Color(1f,.85f,.63f),35,LightType.Spot);light.transform.rotation=Quaternion.Euler(90,0,0);light.range=19;light.spotAngle=105;light.innerSpotAngle=75;
                light.shadows=i==0?LightShadows.Soft:LightShadows.None;light.shadowStrength=.65f;
                var lantern=New("LanternGlow",Polar(9,i*90)+Vector3.up*7.4f,new Color(1f,.52f,.18f),5,LightType.Point);lantern.range=7;
            }
            var volume=root.gameObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=30;
            string path=Art+"/SumoVolume.asset";var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);if(profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,path);}volume.sharedProfile=profile;
            if(!profile.TryGet<Tonemapping>(out var tone))tone=profile.Add<Tonemapping>(true);tone.mode.Override(TonemappingMode.ACES);
            if(!profile.TryGet<Bloom>(out var bloom))bloom=profile.Add<Bloom>(true);bloom.intensity.Override(.22f);bloom.threshold.Override(1.1f);bloom.scatter.Override(.55f);
            if(!profile.TryGet<Vignette>(out var vignette))vignette=profile.Add<Vignette>(true);vignette.intensity.Override(.16f);vignette.smoothness.Override(.5f);
            EditorUtility.SetDirty(profile);DynamicGI.UpdateEnvironment();
        }
        public static void Capture()
        {
            RequireScene();string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../docs/art/previews"));Directory.CreateDirectory(folder);
            var go=new GameObject("SumoReviewCamera");var camera=go.AddComponent<Camera>();camera.transform.position=new Vector3(15.5f,11.6f,-18.7f);camera.transform.LookAt(new Vector3(0,4.5f,0));camera.fieldOfView=65;camera.nearClipPlane=.1f;camera.farClipPlane=100;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.018f,.017f,.026f);camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            var rt=new RenderTexture(1600,1100,24,RenderTextureFormat.ARGB32);camera.targetTexture=rt;camera.Render();
            var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);
            // Camera.Render readback is linear on Metal; PNG/display needs sRGB.
            if(QualitySettings.activeColorSpace==ColorSpace.Linear){var pixels=image.GetPixels();for(int i=0;i<pixels.Length;i++)pixels[i]=pixels[i].gamma;image.SetPixels(pixels);}
            image.Apply();File.WriteAllBytes(Path.Combine(folder,"sumo-ring-overview.png"),image.EncodeToPNG());RenderTexture.active=old;Object.DestroyImmediate(image);Object.DestroyImmediate(go);rt.Release();Object.DestroyImmediate(rt);
            string coverPath=HubConsoleAssets.Folder+"/Covers/SumoRing.png";
            File.Copy(Path.Combine(folder,"sumo-ring-overview.png"),coverPath,true);AssetDatabase.ImportAsset(coverPath);
            var importer=(TextureImporter)AssetImporter.GetAtPath(coverPath);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.maxTextureSize=2048;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Trilinear;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
            var library=AssetDatabase.LoadAssetAtPath<Igruha.Core.Hub.ConsoleArtworkLibrary>(HubConsoleAssets.Folder+"/ConsoleArtwork.asset");
            var definition=AssetDatabase.LoadAssetAtPath<Igruha.Core.Minigame.MinigameDefinition>(Settings+"SumoRing.asset");
            var so=new SerializedObject(library);var entries=so.FindProperty("entries");int index=entries.arraySize;
            for(int i=0;i<entries.arraySize;i++)if(entries.GetArrayElementAtIndex(i).FindPropertyRelative("game").objectReferenceValue==definition){index=i;break;}
            if(index==entries.arraySize)entries.arraySize++;
            var entry=entries.GetArrayElementAtIndex(index);entry.FindPropertyRelative("game").objectReferenceValue=definition;entry.FindPropertyRelative("cover").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Sprite>(coverPath);
            entry.FindPropertyRelative("accent").colorValue=new Color(.906f,.729f,.455f);entry.FindPropertyRelative("genre").stringValue="ОСТАНЬСЯ НА РИНГЕ";entry.FindPropertyRelative("summary").stringValue="Выталкивай соперников с глиняного ринга. Край трещит и осыпается — займи центр и останься последним на ногах.";
            so.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();
        }
    }
}
