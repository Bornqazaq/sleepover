using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.HoleInWall;
using static Igruha.EditorTools.HoleInWallStudioAssets;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Reproducible pavilion finish; adds no gameplay colliders or camera settings.</summary>
    internal static class HoleInWallPavilionDetail
    {
        private const string ConfigPath="Assets/_Project/Settings/Gameplay/Minigames/HoleInWallConfig.asset";
        private const string ReflectionPath=Art+"/PavilionReflection.exr";
        [MenuItem("Igruha/Дырка в стене/Детали павильона")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
            var arena=GameObject.Find("_Arena").transform;
            var c=AssetDatabase.LoadAssetAtPath<HoleInWallConfig>(ConfigPath);
            string[] models=Enumerable.Range(3,4).SelectMany(i=>new[]{"Fan"+i+"_Idle","Fan"+i+"_Cheer"}).ToArray();
            Import(models);
            var studio=arena.Find("_Studio");
            foreach(string n in new[]{"Audience tiers","Original audience"})
                if(studio.Find(n)!=null)Object.DestroyImmediate(studio.Find(n).gameObject);
            HoleInWallStudioBuilder.Audience(studio,c);
            // Only the accepted foreground shader's lane print parameters change.
            for(int i=0;i<c.TrackCount;i++)
            {
                var m=Mat("WaitingPanel"+i);
                m.SetColor("_AccentColor",Color.Lerp(new Color(.12f,.34f,.29f),HoleInWallPalette.LaneAccent(i),.83f));
                m.SetFloat("_LaneSymbol",i);EditorUtility.SetDirty(m);
            }
            Build(arena,c);
            BakeReflection(arena,c);
            EditorSceneManager.MarkSceneDirty(arena.gameObject.scene);
            EditorSceneManager.SaveScene(arena.gameObject.scene);AssetDatabase.SaveAssets();
        }
        internal static void Build(Transform arena,HoleInWallConfig c)
        {
            var old=arena.Find("_PavilionDetail");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var root=new GameObject("_PavilionDetail").transform;root.SetParent(arena,false);
            var ceramic=Material("Tessera","Igruha/HoleInWall/Tessera");
            float half=c.ArenaWidth*.5f+9, near=c.ArenaNearZ-9, far=c.ArenaFarZ+9;
            foreach(float z in new[]{near,far})
            {
                bool front=z==far;
                var mosaic=new Surface();
                float step=.27f;
                for(float y=.25f;y<8.8f;y+=step)
                    for(float x=-half+.3f;x<half-.3f;x+=step)
                    {
                        float yn=y/9f,xn=x/half;
                        if(xn*xn+yn*yn>.955f)continue;
                        float r=Vector2.Distance(new Vector2(x,y),new Vector2(0,3.9f));
                        float a=Mathf.Atan2(y-3.9f,x);
                        Color color=new Color(.64f,.40f,.18f);
                        if(r<1.5f)color=new Color(.96f,.80f,.44f);
                        else if(r<1.68f)color=new Color(.16f,.43f,.37f);
                        else if(y>2.0f && Mathf.Cos(a*18+r*.13f)>.50f)color=new Color(.93f,.84f,.61f);
                        float wave=1.7f+.42f*Mathf.Sin(x*.24f);
                        if(y<wave)color=new Color(.16f,.43f,.37f);
                        else if(y<wave+.26f)color=new Color(.77f,.86f,.70f);
                        else if(y<wave+.57f)color=new Color(.30f,.62f,.53f);
                        if(xn*xn+yn*yn>.875f)color=new Color(.79f,.87f,.71f);
                        color*=.94f+.10f*Mathf.Repeat(Mathf.Sin(x*17+y*53)*1423,1);
                        mosaic.Tile(new Vector3(front?x:-x,14.4f+y,z+(front?-.22f:.22f)),front?Vector3.right:Vector3.left,Vector3.up,step-.016f,step-.016f,color);
                    }
                mosaic.Save(root,"Vault mosaic "+(front?"far":"near"),ceramic);
            }
            // Continuous cream/green wave frieze softens the plaster/ceramic junction.
            var frieze=new Surface();
            for(int side=-1;side<=1;side+=2)
            {
                for(float u=near+.45f;u<far-.45f;u+=.22f)
                    for(int row=0;row<3;row++)
                        frieze.Tile(new Vector3(side*(half-.54f),2.59f+row*.17f,u),Vector3.forward*-side,Vector3.up,.207f,.157f,FriezeColor(u,row));
                float z=side<0?near:far;
                for(float u=-half+.5f;u<half-.5f;u+=.22f)
                    for(int row=0;row<3;row++)
                        frieze.Tile(new Vector3(u,2.59f+row*.17f,z-side*.54f),Vector3.right*side,Vector3.up,.207f,.157f,FriezeColor(u,row));
            }
            frieze.Save(root,"Ceramic wave frieze",ceramic);
            Floats(root,c);
            Sunlight(root,c);
            Water(arena,c);
        }
        private static Color FriezeColor(float u,int row)=> row==1 && Mathf.Sin(u*2.2f)>.1f ? new Color(.25f,.53f,.44f):new Color(.90f,.84f,.63f);
        private static void Floats(Transform root,HoleInWallConfig c)
        {
            var mesh=new Surface();
            for(int lane=0;lane<c.TrackCount-1;lane++)
            {
                float x=(c.TrackCenterX(lane)+c.TrackCenterX(lane+1))*.5f;
                // Entire separator sits inside the gap, below the wall base, with no collider.
                for(float z=c.ArenaNearZ+1;z<c.WallStartZ+.5f;z+=.42f)
                    mesh.Float(new Vector3(x,c.WaterSurfaceY+.035f,z),.14f,.29f,
                        ((int)((z-c.ArenaNearZ)/.42f)%3)==0?new Color(.94f,.89f,.72f):HoleInWallPalette.LaneAccent(lane));
            }
            mesh.Save(root,"Lane float separators",Mat("White"),true);
        }
        private static void Sunlight(Transform root,HoleInWallConfig c)
        {
            var beam=Material("AirSunlight","Igruha/HoleInWall/Sunbeam");beam.SetColor("_Color",new Color(1,.86f,.55f,.065f));beam.SetFloat("_Surface",0);
            var patches=Material("RoofSunPatches","Igruha/HoleInWall/Sunbeam");patches.SetColor("_Color",new Color(1,.84f,.50f,.24f));patches.SetFloat("_Surface",1);
            var air=new Surface();
            for(int i=0;i<5;i++)
            {
                float x=-c.ArenaWidth*.5f+5+i*9;
                Vector3 top=new Vector3(x,18,15), bottom=new Vector3(x-5,c.WaterSurfaceY,2);
                // Crossed, feathered sheets; broad edges vanish rather than forming cones.
                air.Quad(top+Vector3.left*1.5f,top+Vector3.right*1.5f,bottom+Vector3.right*2.3f,bottom+Vector3.left*2.3f,Color.white);
            }
            air.Save(root,"Sun through vault",beam);
            var floor=new Surface();
            for(int i=0;i<c.TrackCount;i++)
            {
                float x=c.TrackCenterX(i);
                floor.Quad(new Vector3(x-4,.047f,c.PlatformBackZ),new Vector3(x+4,.047f,c.PlatformBackZ),new Vector3(x+4,.047f,c.PlatformFrontZ),new Vector3(x-4,.047f,c.PlatformFrontZ),Color.white);
            }
            floor.Save(root,"Glazing light on decks",patches);
        }
        private static void Water(Transform arena,HoleInWallConfig c)
        {
            var surface=arena.Find("Pool/Water");
            if(surface.GetComponent<HoleInWallWaterReflection>()==null)surface.gameObject.AddComponent<HoleInWallWaterReflection>();
            var material=surface.GetComponent<Renderer>().sharedMaterial;
            material.shader=Shader.Find("Igruha/HoleInWall/Pavilion Water");
            material.SetTexture("_PavilionEnvironment",AssetDatabase.LoadAssetAtPath<Cubemap>(ReflectionPath));
            material.SetFloat("_ReflectionStrength",.9f);
            material.SetFloat("_FresnelStrength",0);
            material.SetFloat("_Opacity",.30f);
            material.SetVector("_ProbePosition",new Vector4(0,c.WaterSurfaceY+.1f,(c.ArenaNearZ+c.ArenaFarZ)*.5f,0));
            material.SetVector("_RoomMin",new Vector4(-c.ArenaWidth*.5f-8.5f,c.PoolBottomY,c.ArenaNearZ-8.5f,0));
            material.SetVector("_RoomMax",new Vector4(c.ArenaWidth*.5f+8.5f,23.4f,c.ArenaFarZ+8.5f,0));
            EditorUtility.SetDirty(material);
        }
        internal static void BakeReflection(Transform arena,HoleInWallConfig c)
        {
            var water=arena.Find("Pool/Water").GetComponent<Renderer>();
            var existing=arena.Find("Pavilion reflection bake");
            var go=existing!=null?existing.gameObject:new GameObject("Pavilion reflection bake");
            go.transform.SetParent(arena,false);
            var probe=go.GetComponent<ReflectionProbe>();
            if(probe==null)probe=go.AddComponent<ReflectionProbe>();
            go.transform.position=new Vector3(0,c.WaterSurfaceY+.1f,(c.ArenaNearZ+c.ArenaFarZ)*.5f);
            probe.enabled=false; // Offline bake: never register a temporary probe in the live URP atlas.
            probe.resolution=512;probe.hdr=true;probe.clearFlags=ReflectionProbeClearFlags.Skybox;
            probe.farClipPlane=150;probe.nearClipPlane=.1f;
            bool enabled=water.enabled;water.enabled=false;
            try {if(!Lightmapping.BakeReflectionProbe(probe,ReflectionPath))throw new InvalidOperationException("Pavilion reflection bake failed");}
            finally{water.enabled=enabled;}
            AssetDatabase.ImportAsset(ReflectionPath);Water(arena,c);
        }
        private static Material Material(string name,string shader)
        {
            string path=Materials+"/HS_"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(m,path);}else m.shader=Shader.Find(shader);
            EditorUtility.SetDirty(m);return m;
        }
        private sealed class Surface
        {
            readonly List<Vector3> v=new List<Vector3>();readonly List<int> t=new List<int>();readonly List<Color> c=new List<Color>();readonly List<Vector2> uv=new List<Vector2>();
            internal void Quad(Vector3 a,Vector3 b,Vector3 d,Vector3 e,Color color)
            {int n=v.Count;v.AddRange(new[]{a,b,d,e});t.AddRange(new[]{n,n+2,n+1,n,n+3,n+2});for(int i=0;i<4;i++)c.Add(color);uv.AddRange(new[]{new Vector2(0,1),new Vector2(1,1),new Vector2(1,0),new Vector2(0,0)});}
            internal void Tile(Vector3 p,Vector3 right,Vector3 up,float w,float h,Color color)=>Quad(p-right*w*.5f-up*h*.5f,p+right*w*.5f-up*h*.5f,p+right*w*.5f+up*h*.5f,p-right*w*.5f+up*h*.5f,color);
            internal void Float(Vector3 p,float radius,float length,Color color)
            {for(int i=0;i<12;i++){float a=i*Mathf.PI/6,b=(i+1)*Mathf.PI/6;Vector3 u=new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,0),w=new Vector3(Mathf.Cos(b)*radius,Mathf.Sin(b)*radius,0);Quad(p+u-Vector3.forward*length*.5f,p+u+Vector3.forward*length*.5f,p+w+Vector3.forward*length*.5f,p+w-Vector3.forward*length*.5f,color);
                Quad(p-Vector3.forward*length*.5f,p+w-Vector3.forward*length*.5f,p+u-Vector3.forward*length*.5f,p-Vector3.forward*length*.5f,color);
                Quad(p+Vector3.forward*length*.5f,p+u+Vector3.forward*length*.5f,p+w+Vector3.forward*length*.5f,p+Vector3.forward*length*.5f,color);}}
            internal void Save(Transform root,string name,Material material,bool vertexColor=false)
            {
                string path=Art+"/Meshes/HS_"+name.Replace(' ','_')+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(mesh==null){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}mesh.Clear();mesh.name=name;mesh.indexFormat=IndexFormat.UInt32;mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.SetColors(c);mesh.SetUVs(0,uv);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
                var go=new GameObject(name);go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=vertexColor?Material("Tessera","Igruha/HoleInWall/Tessera"):material;r.shadowCastingMode=ShadowCastingMode.Off;
            }
        }
    }
}
