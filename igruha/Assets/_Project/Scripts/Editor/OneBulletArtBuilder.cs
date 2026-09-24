using System;
using System.Collections.Generic;
using System.IO;
using Igruha.Core.Player;
using Igruha.Minigames.OneBullet;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Igruha.EditorTools
{
    public static class OneBulletArtBuilder
    {
        private const string Root="Assets/_Project/Art/Minigames/OneBullet/";
        private const float Size=43.2f, Pixel=.12f, Top=5.76f;
        private const int N=360;
        private static readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
        private static readonly Dictionary<string,Mesh> meshes=new Dictionary<string,Mesh>();
        private sealed class Batch {public Material Material;public readonly List<CombineInstance> Parts=new List<CombineInstance>();}
        private static readonly Dictionary<string,Batch> batches=new Dictionary<string,Batch>();
        private static System.Random random;
        private static Transform art;
        private static Material[] stone,floor;
        [MenuItem("Igruha/Art/Build One Bullet ruins")]
        public static void Build()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop play mode first");
            EditorSceneManager.OpenScene(OneBulletArenaBuilder.ScenePath);
            Directory.CreateDirectory(Root+"Materials");Directory.CreateDirectory(Root+"Geometry");AssetDatabase.Refresh();
            materials.Clear();meshes.Clear();batches.Clear();random=new System.Random(597);
            var old=GameObject.Find("_OneBulletArt");if(old!=null)Object.DestroyImmediate(old);
            art=new GameObject("_OneBulletArt").transform;
            stone=new Material[7];floor=new Material[5];
            for(int i=0;i<stone.Length;i++)stone[i]=Mat("Stone_"+i,new Color(.59f+i*.018f,.55f+i*.017f,.47f+i*.016f));
            for(int i=0;i<floor.Length;i++)floor[i]=Mat("Paving_"+i,new Color(.64f+i*.02f,.60f+i*.018f,.50f+i*.016f));
            var mortar=Mat("WarmMortar",new Color(.43f,.405f,.35f));
            var sand=Mat("Sand",new Color(.71f,.62f,.44f));
            var arena=GameObject.Find("_Arena").transform;
            foreach(var renderer in arena.GetComponentsInChildren<MeshRenderer>())
            {
                bool wall=renderer.name.StartsWith("Wall_");renderer.sharedMaterial=wall?mortar:sand;
                if(wall)
                {
                    var b=renderer.bounds;
                    Add("__Backing",new Vector3(b.center.x,b.max.y-.04f,b.center.z),Quaternion.identity,new Vector3(b.size.x,.08f,b.size.z),mortar);
                    renderer.enabled=false; // collision volumes are dressed by a separate stone shell
                }
            }
            var l=OneBulletArenaBuilder.ReadLayout();var walk=Map(l);
            // Only dress faces which touch the playable floor, never hidden interior partitions.
            for(int side=0;side<4;side++)for(int line=0;line<N;line++)
            {
                int start=-1;
                for(int along=0;along<=N;along++)
                {
                    int x=side<2?along:line,z=side<2?line:along;
                    bool edge=false;
                    if(along<N&&walk[x,z])
                    {
                        int nx=x+(side==2?1:side==3?-1:0),nz=z+(side==0?1:side==1?-1:0);
                        edge=nx<0||nx>=N||nz<0||nz>=N||!walk[nx,nz];
                    }
                    if(edge&&start<0)start=along;
                    if(!edge&&start>=0)
                    {
                        float a=start*Pixel-Size*.5f,b=along*Pixel-Size*.5f;
                        float axis=(line+(side==0||side==2?1:0))*Pixel-Size*.5f;
                        Vector3 from=side<2?new Vector3(a,0,axis):new Vector3(axis,0,a);
                        Vector3 to=side<2?new Vector3(b,0,axis):new Vector3(axis,0,b);
                        Vector3 inward=side==0?Vector3.back:side==1?Vector3.forward:side==2?Vector3.left:Vector3.right;
                        WallFace(from,to,inward);start=-1;
                    }
                }
            }
            foreach(Transform t in arena)
            {
                if(!t.name.StartsWith("Landing_")&&!t.name.StartsWith("Passage_"))continue;
                float w=t.localScale.x,d=t.localScale.z;
                int columns=Mathf.Max(1,Mathf.CeilToInt(w/1.1f)),rows=Mathf.Max(1,Mathf.CeilToInt(d/1.05f));
                for(int z=0;z<rows;z++)for(int x=0;x<columns;x++)
                {
                    Vector3 p=t.TransformPoint(new Vector3((x+.5f)/columns-.5f,.5f,(z+.5f)/rows-.5f));
                    p+=t.up*.025f;
                    Add("OB_Slab_"+random.Next(3),p,t.rotation,new Vector3(w/columns-.035f,.075f,d/rows-.035f),floor[random.Next(floor.Length)]);
                }
            }
            BuildLandmarks(l);
            Flush();
            DressGun();BuildHud();Lighting();BuildEffects();OneBulletSfx.Build();OneBulletFirstPersonBuilder.Configure();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());AssetDatabase.SaveAssets();
            int triangles=0;foreach(var mf in art.GetComponentsInChildren<MeshFilter>())triangles+=mf.sharedMesh.triangles.Length/3;
            Debug.Log("OneBullet art: "+art.GetComponentsInChildren<Renderer>().Length+" renderers, "+triangles+" triangles; decorative colliders="+art.GetComponentsInChildren<Collider>().Length);
        }
        private static void WallFace(Vector3 a,Vector3 b,Vector3 inward)
        {
            Vector3 tangent=(b-a).normalized;float length=Vector3.Distance(a,b);var rot=Quaternion.LookRotation(-inward);
            Add("__Backing",(a+b)*.5f-inward*.5f+Vector3.up*(Top*.5f),rot,new Vector3(length,Top,.15f),Mat("WarmMortar",new Color(.43f,.405f,.35f)));
            int rows=9;
            for(int row=0;row<rows;row++)
            {
                int count=Mathf.Max(1,Mathf.RoundToInt(length/(row%2==0?1.12f:1.32f)));
                float step=length/count;
                float cursor=0;
                for(int i=0;i<count;i++)
                {
                    float height=Top/rows;
                    float next=i==count-1?length:(i+1)*step+((float)random.NextDouble()-.5f)*step*.38f;
                    float width=next-cursor;
                    var p=a+tangent*(cursor+width*.5f)-inward*(.19f+(float)random.NextDouble()*.01f)+Vector3.up*((row+.5f)*height);
                    cursor=next;
                    int shade=random.Next(stone.Length);
                    // North-east gallery is a little cooler; the sun still unifies it.
                    if(p.x>4&&p.z>4)shade=Mathf.Max(0,shade-2);
                    Add("OB_Stone_"+random.Next(3),p,rot*Quaternion.Euler(0,0,((float)random.NextDouble()-.5f)*1.6f),new Vector3(width-.025f,height-.028f,.48f),stone[shade]);
                }
            }
            if(length>1.6f)
            {
                int chips=Mathf.Clamp(Mathf.RoundToInt(length*.5f),1,5);
                for(int k=0;k<chips;k++)
                {
                    var p=Vector3.Lerp(a,b,.15f+(float)random.NextDouble()*.7f)+inward*(.14f+(float)random.NextDouble()*.12f);
                    p.y=Mathf.Clamp(Mathf.Min(p.x,p.z)/4.8f,0,2)*1.08f+.06f;
                    float size=.10f+(float)random.NextDouble()*.16f;
                    Add("OB_Stone_"+random.Next(3),p,Quaternion.Euler(0,random.Next(360),random.Next(-12,12)),new Vector3(size,.09f,size*.75f),stone[random.Next(stone.Length)]);
                }
            }
            if(length>1.6f&&random.NextDouble()<.7)
            {
                var mid=Vector3.Lerp(a,b,.35f+(float)random.NextDouble()*.3f)+inward*.32f;
                mid.y=Top-3.9f;
                Prop("OB_Ivy",mid,Quaternion.LookRotation(inward),new Vector3(1.15f,1.55f,1f));
            }
        }
        private static void BuildLandmarks(OneBulletArenaBuilder.Layout l)
        {
            var arena=GameObject.Find("_Arena").transform;var previous=arena.Find("LandmarkCover");if(previous!=null)Object.DestroyImmediate(previous.gameObject);
            var cover=new GameObject("LandmarkCover").transform;cover.SetParent(arena,false);
            for(int i=0;i<l.rooms.Length;i++)
            {
                var p=OneBulletArenaBuilder.Position(l,l.rooms[i]);
                Prop(i==0?"OB_BrokenColumn":"OB_Well",p,Quaternion.Euler(0,i*23,0),Vector3.one);
                var c=new GameObject("RoomCover_"+i);c.transform.SetParent(cover,false);c.transform.position=p;c.layer=LayerMask.NameToLayer("Cover");
                if(i==0){var box=c.AddComponent<BoxCollider>();box.center=Vector3.up*1.04f;box.size=new Vector3(.85f,2.08f,.85f);}
                else{var box=c.AddComponent<BoxCollider>();box.center=Vector3.up*.35f;box.size=new Vector3(1.86f,.7f,1.86f);}
                Prop("OB_Backpack",p+new Vector3(1.95f,0,1.92f),Quaternion.Euler(0,-35,0),Vector3.one);
                Prop("OB_Rope",p+new Vector3(1.3f,.02f,2.05f),Quaternion.identity,Vector3.one);
                Prop("OB_Campfire",p+new Vector3(-1.9f,0,-1.8f),Quaternion.identity,Vector3.one);
                Prop("OB_BrokenColumn",p+new Vector3(-1.8f,0,1.8f),Quaternion.Euler(0,25,12),new Vector3(.65f,.8f,.65f));
                var corner=new GameObject("CornerColumnCover_"+i);corner.transform.SetParent(cover,false);corner.transform.position=p+new Vector3(-1.8f,.82f,1.8f);corner.transform.rotation=Quaternion.Euler(0,25,12);corner.layer=LayerMask.NameToLayer("Cover");corner.AddComponent<BoxCollider>().size=new Vector3(.6f,1.64f,.6f);
            }
            for(int i=0;i<l.guns.Length;i++)
            {
                var p=OneBulletArenaBuilder.Position(l,l.guns[i]);
                Prop("OB_Sign",p+new Vector3(.84f,0,.6f),Quaternion.Euler(0,-60,0),Vector3.one*.78f);
                if(i%3==0)Prop("OB_Backpack",p+new Vector3(-.78f,0,.48f),Quaternion.Euler(0,25,0),Vector3.one*.8f);
                if(i%3==1)Prop("OB_Rope",p+new Vector3(-.65f,.01f,-.55f),Quaternion.Euler(0,30,0),Vector3.one*.8f);
            }
        }
        private static void DressGun()
        {
            var view=Object.FindFirstObjectByType<OneBulletPresentation>();var so=new SerializedObject(view);
            var old=(Transform)so.FindProperty("gun").objectReferenceValue;var parent=old.parent;Object.DestroyImmediate(old.gameObject);
            var g=Prop("OB_Revolver",Vector3.zero,Quaternion.identity,Vector3.one*1.6f);g.transform.SetParent(parent,true);g.name="Revolver";g.SetActive(false);
            OneBulletArenaBuilder.Set(view,"gun",g.transform);
            var light=(Light)so.FindProperty("pickupLight").objectReferenceValue;
            light.range=2.1f;light.intensity=1.6f;light.shadows=LightShadows.Soft;
        }
        private static void BuildHud()
        {
            var view=Object.FindFirstObjectByType<OneBulletPresentation>();var so=new SerializedObject(view);var group=(CanvasGroup)so.FindProperty("ammunition").objectReferenceValue;
            var oldText=group.GetComponent<TextMeshProUGUI>();if(oldText!=null)Object.DestroyImmediate(oldText);
            while(group.transform.childCount>0)Object.DestroyImmediate(group.transform.GetChild(0).gameObject);
            var rect=(RectTransform)group.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            var line=new GameObject("Ammo",typeof(RectTransform)).AddComponent<TextMeshProUGUI>();line.transform.SetParent(rect,false);
            var lr=(RectTransform)line.transform;lr.anchorMin=lr.anchorMax=new Vector2(.5f,.16f);lr.sizeDelta=new Vector2(450,80);
            line.text="<size=36>1</size>  /  1\n<size=16>ЛКМ — ВЫСТРЕЛ</size>";line.alignment=TextAlignmentOptions.Center;line.fontSize=24;line.color=new Color(1,.86f,.61f);line.raycastTarget=false;
            var cross=new GameObject("Reticle",typeof(RectTransform)).AddComponent<TextMeshProUGUI>();cross.transform.SetParent(rect,false);
            var cr=(RectTransform)cross.transform;cr.anchorMin=cr.anchorMax=new Vector2(.5f,.5f);cr.sizeDelta=new Vector2(32,32);
            cross.text="+";cross.fontSize=25;cross.alignment=TextAlignmentOptions.Center;cross.color=new Color(1,.91f,.74f);cross.raycastTarget=false;
            group.blocksRaycasts=false;group.interactable=false;
        }
        private static void Lighting()
        {
            var root=GameObject.Find("_Lighting");if(root!=null)Object.DestroyImmediate(root);root=new GameObject("_Lighting");
            // The template carries auxiliary lamps; the ruin needs one coherent sun.
            foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if(light.name!="PickupGlow")Object.DestroyImmediate(light.gameObject);
            var sun=new GameObject("RuinSun").AddComponent<Light>();sun.transform.SetParent(root.transform,false);sun.type=LightType.Directional;sun.transform.rotation=Quaternion.Euler(58,-34,0);
            sun.color=new Color(1f,.89f,.7f);sun.intensity=2.4f;sun.shadows=LightShadows.Soft;sun.shadowStrength=.8f;sun.shadowBias=.025f;sun.shadowNormalBias=.15f;
            RenderSettings.sun=sun;RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.65f,.73f,.83f);RenderSettings.ambientEquatorColor=new Color(.54f,.58f,.65f);RenderSettings.ambientGroundColor=new Color(.42f,.38f,.30f);RenderSettings.ambientIntensity=1;
            var ambient=new SphericalHarmonicsL2();ambient.AddAmbientLight(new Color(.47f,.50f,.55f));RenderSettings.ambientProbe=ambient;
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogColor=new Color(.70f,.76f,.79f);RenderSettings.fogDensity=.004f;
            var sky=Mat("Sky",new Color(.53f,.72f,.85f));sky.shader=Shader.Find("Skybox/Procedural");sky.SetColor("_SkyTint",new Color(.5f,.5f,.5f));sky.SetFloat("_AtmosphereThickness",1f);sky.SetFloat("_Exposure",1.3f);sky.SetFloat("_SunSize",.035f);RenderSettings.skybox=sky;
            var volume=root.AddComponent<Volume>();volume.isGlobal=true;volume.priority=30;var path=Root+"RuinVolume.asset";
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);if(profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,path);}volume.sharedProfile=profile;
            if(!profile.TryGet<Tonemapping>(out var tone))tone=profile.Add<Tonemapping>(true);tone.mode.Override(TonemappingMode.ACES);
            if(!profile.TryGet<ColorAdjustments>(out var color))color=profile.Add<ColorAdjustments>(true);color.postExposure.Override(.1f);color.contrast.Override(4);color.saturation.Override(-4);
            if(!profile.TryGet<Bloom>(out var bloom))bloom=profile.Add<Bloom>(true);bloom.intensity.Override(.12f);bloom.threshold.Override(1.25f);
            if(!profile.TryGet<Vignette>(out var vignette))vignette=profile.Add<Vignette>(true);vignette.intensity.Override(.13f);vignette.smoothness.Override(.4f);
            EditorUtility.SetDirty(profile);var camera=GameObject.Find("_Camera").GetComponentInChildren<Camera>();camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
        }
        private static void BuildEffects()
        {
            var view=Object.FindFirstObjectByType<OneBulletPresentation>();
            var old=GameObject.Find("_OneBulletEffects");if(old!=null)Object.DestroyImmediate(old);
            var root=new GameObject("_OneBulletEffects").transform;
            var material=Mat("Dust",new Color(.68f,.63f,.51f,.25f));material.shader=Shader.Find("Universal Render Pipeline/Particles/Unlit");
            material.SetFloat("_Surface",1);material.SetFloat("_Blend",0);material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);material.SetFloat("_ZWrite",0);material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.renderQueue=3000;
            var builtin=AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");if(builtin!=null&&builtin.mainTexture!=null)material.SetTexture("_BaseMap",builtin.mainTexture);
            foreach(string name in new[]{"smoke","impact"})
            {
                var ps=new GameObject(name).AddComponent<ParticleSystem>();ps.transform.SetParent(root,false);ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=ps.main;main.playOnAwake=false;main.loop=false;main.duration=.8f;main.startLifetime=new ParticleSystem.MinMaxCurve(.18f,.5f);main.startSpeed=new ParticleSystem.MinMaxCurve(.2f,name=="smoke"?.8f:1.5f);main.startSize=new ParticleSystem.MinMaxCurve(.06f,.17f);main.startColor=new Color(.78f,.73f,.60f,.25f);main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=40;
                var emission=ps.emission;emission.enabled=false;var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=24;shape.radius=.04f;
                var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.5f,1,2.7f));
                var color=ps.colorOverLifetime;color.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(.5f,0),new GradientAlphaKey(0,1)});color.color=new ParticleSystem.MinMaxGradient(gradient);
                ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=material;OneBulletArenaBuilder.Set(view,name,ps);
            }
            var flash=new GameObject("MuzzleFlash").AddComponent<Light>();flash.transform.SetParent(root,false);flash.type=LightType.Point;flash.color=new Color(1,.65f,.25f);flash.range=2.5f;flash.intensity=2;flash.enabled=false;
            OneBulletArenaBuilder.Set(view,"muzzleFlash",flash);
        }
        private static bool[,] Map(OneBulletArenaBuilder.Layout l)
        {
            var map=new bool[N,N];
            for(int i=0;i<l.grid*l.grid;i++){var p=OneBulletArenaBuilder.Position(l,i);float w=Array.IndexOf(l.rooms,i)>=0?5.76f:2.16f;Mark(map,p.x,p.z,w,w);}
            foreach(var e in l.edges){var a=OneBulletArenaBuilder.Position(l,e.a);var b=OneBulletArenaBuilder.Position(l,e.b);Mark(map,(a.x+b.x)/2,(a.z+b.z)/2,Mathf.Abs(a.x-b.x)+2.16f,Mathf.Abs(a.z-b.z)+2.16f);}return map;
        }
        private static void Mark(bool[,] map,float x,float z,float w,float d)
        {int x0=Mathf.RoundToInt((x-w/2+Size/2)/Pixel),x1=Mathf.RoundToInt((x+w/2+Size/2)/Pixel),z0=Mathf.RoundToInt((z-d/2+Size/2)/Pixel),z1=Mathf.RoundToInt((z+d/2+Size/2)/Pixel);for(int v=z0;v<z1;v++)for(int u=x0;u<x1;u++)if(u>=0&&u<N&&v>=0&&v<N)map[u,v]=true;}
        private static Mesh UnitMesh(string name)
        {
            if(meshes.TryGetValue(name,out var found))return found;
            if(name=="__Backing")
            {
                var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);var unit=Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(cube);meshes[name]=unit;return unit;
            }
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Models/"+name+".fbx");if(prefab==null)throw new InvalidOperationException("Missing "+name);
            var inputs=new List<CombineInstance>();foreach(var mf in prefab.GetComponentsInChildren<MeshFilter>())inputs.Add(new CombineInstance{mesh=mf.sharedMesh,transform=mf.transform.localToWorldMatrix});
            var mesh=new Mesh();mesh.CombineMeshes(inputs.ToArray(),true,true);var bound=mesh.bounds;var vertices=mesh.vertices;
            for(int i=0;i<vertices.Length;i++){var p=vertices[i]-bound.center;vertices[i]=new Vector3(p.x/bound.size.x,p.y/bound.size.y,p.z/bound.size.z);}mesh.vertices=vertices;var normals=mesh.normals;for(int i=0;i<normals.Length;i++)normals[i]=Vector3.Scale(normals[i],bound.size).normalized;mesh.normals=normals;mesh.RecalculateBounds();meshes[name]=mesh;return mesh;
        }
        private static void Add(string model,Vector3 position,Quaternion rotation,Vector3 scale,Material material)
        {
            int sector=Mathf.Clamp((int)((position.x+Size*.5f)/14.4f),0,2)+3*Mathf.Clamp((int)((position.z+Size*.5f)/14.4f),0,2);
            string key=sector+"_"+material.name;if(!batches.TryGetValue(key,out var batch)){batch=new Batch{Material=material};batches[key]=batch;}
            batch.Parts.Add(new CombineInstance{mesh=UnitMesh(model),transform=Matrix4x4.TRS(position,rotation,scale)});
        }
        private static void Flush()
        {
            foreach(var pair in batches)
            {
                var mesh=new Mesh{indexFormat=IndexFormat.UInt32,name=pair.Key};mesh.CombineMeshes(pair.Value.Parts.ToArray(),true,true);
                string path=Root+"Geometry/"+pair.Key+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(saved==null){AssetDatabase.CreateAsset(mesh,path);saved=mesh;}else{EditorUtility.CopySerialized(mesh,saved);EditorUtility.SetDirty(saved);Object.DestroyImmediate(mesh);}
                var g=new GameObject(pair.Key,typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(art,false);g.GetComponent<MeshFilter>().sharedMesh=saved;g.GetComponent<MeshRenderer>().sharedMaterial=pair.Value.Material;g.isStatic=true;
            }
            foreach(var m in meshes.Values)Object.DestroyImmediate(m);meshes.Clear();
        }
        private static GameObject Prop(string name,Vector3 position,Quaternion rotation,Vector3 scale)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Models/"+name+".fbx");if(prefab==null)throw new InvalidOperationException("Missing "+name);
            // Keep the FBX root conversion (metres and Z-up) inside a placement wrapper.
            var go=new GameObject(name);go.transform.SetParent(art,false);go.transform.position=position;go.transform.rotation=rotation;go.transform.localScale=scale;
            var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab);model.transform.SetParent(go.transform,false);
            foreach(var r in go.GetComponentsInChildren<Renderer>())
            {var list=r.sharedMaterials;for(int i=0;i<list.Length;i++)list[i]=PropMaterial(list[i].name);r.sharedMaterials=list;}
            return go;
        }
        private static Material PropMaterial(string name)
        {
            Color c=new Color(.62f,.58f,.49f);float metallic=0,smooth=.17f;
            if(name.Contains("PaleStone"))c=new Color(.76f,.70f,.59f);
            else if(name.Contains("Crevice"))c=new Color(.13f,.13f,.12f);
            else if(name.Contains("OldWood"))c=new Color(.44f,.25f,.12f);
            else if(name.Contains("CopperLeaf"))c=new Color(.55f,.28f,.13f);
            else if(name.Contains("AmberLeaf"))c=new Color(.70f,.43f,.19f);
            else if(name.Contains("Vine"))c=new Color(.29f,.15f,.07f);
            else if(name.Contains("Canvas"))c=new Color(.43f,.42f,.24f);
            else if(name.Contains("Brass")){c=new Color(.72f,.49f,.22f);metallic=.65f;smooth=.4f;}
            else if(name.Contains("GunSteel")){c=new Color(.25f,.30f,.33f);metallic=.65f;smooth=.38f;}
            else if(name.Contains("Silver")){c=new Color(.73f,.76f,.75f);metallic=.7f;smooth=.38f;}
            var mat=Mat(name,c);mat.SetFloat("_Metallic",metallic);mat.SetFloat("_Smoothness",smooth);if(name.Contains("Leaf"))mat.SetFloat("_Cull",0);return mat;
        }
        private static Material Mat(string name,Color color)
        {
            if(materials.TryGetValue(name,out var found))return found;
            string path=Root+"Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.13f);EditorUtility.SetDirty(m);materials[name]=m;return m;
        }
    }
}
