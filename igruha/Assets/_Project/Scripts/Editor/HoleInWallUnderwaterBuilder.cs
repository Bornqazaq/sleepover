using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.HoleInWall;
using static Igruha.EditorTools.HoleInWallStudioAssets;

namespace Igruha.EditorTools
{
    internal static class HoleInWallUnderwaterBuilder
    {
        internal static void Build(Transform arena,HoleInWallConfig c,HoleInWallTrack[] tracks)
        {
            var old=arena.Find("_Underwater");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var root=new GameObject("_Underwater").transform;root.SetParent(arena,false);
            var ceramic=Material("PoolCeramic","Igruha/HoleInWall/Pool Ceramic");
            ceramic.SetColor("_BaseColor",new Color(.68f,.83f,.77f));ceramic.SetFloat("_WaterLevel",c.WaterSurfaceY);
            arena.Find("Pool/PoolFloor").GetComponent<Renderer>().sharedMaterial=ceramic;
            var bubble=Material("AirBubbles","Igruha/HoleInWall/Bubble");bubble.SetFloat("_WaterLevel",c.WaterSurfaceY);
            var shaft=Material("UnderwaterSunlight","Igruha/CryingAngels/LightShaft");
            shaft.SetColor("_BaseColor",new Color(.48f,.84f,.73f,.09f));shaft.renderQueue=3012;
            var cookie=CausticCookie();
            var systems=new ParticleSystem[c.TrackCount*2];var recoveries=new HoleInWallRecovery[systems.Length];
            var lights=new Light[c.TrackCount];
            var beam=Beam();
            for(int lane=0;lane<c.TrackCount;lane++)
            {
                float x=c.TrackCenterX(lane);
                var lightGo=new GameObject("Submerged caustic light "+lane);lightGo.transform.SetParent(root,false);
                lightGo.transform.position=new Vector3(x,c.WaterSurfaceY-.025f,-2.7f);
                lightGo.transform.rotation=Quaternion.Euler(90,0,0);
                var light=lightGo.AddComponent<Light>();light.type=LightType.Spot;
                light.color=new Color(.44f,.83f,.76f);light.intensity=1.7f;light.range=7;light.spotAngle=145;
                light.shadows=LightShadows.None;light.cookie=cookie;lights[lane]=light;
                for(int ray=0;ray<3;ray++)
                {
                    var go=new GameObject("Sunlight in pool");go.transform.SetParent(root,false);
                    go.transform.position=new Vector3(x-2+ray*2,c.WaterSurfaceY-.08f,-4.4f+ray*.4f);
                    go.AddComponent<MeshFilter>().sharedMesh=beam;
                    var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=shaft;renderer.shadowCastingMode=ShadowCastingMode.Off;
                }
                for(int slot=0;slot<2;slot++)
                {
                    int key=lane*2+slot;
                    systems[key]=Bubbles(root,key,bubble);
                    recoveries[key]=HoleInWallReturnBuilder.Build(root,key);
                }
            }
            var game=Object.FindFirstObjectByType<HoleInWallMinigame>();
            var data=new SerializedObject(game);Fill(data.FindProperty("recoveries"),recoveries);data.ApplyModifiedPropertiesWithoutUndo();
            var environment=root.gameObject.AddComponent<HoleInWallUnderwater>();data=new SerializedObject(environment);
            data.FindProperty("game").objectReferenceValue=game;data.FindProperty("config").objectReferenceValue=c;
            Fill(data.FindProperty("bubbles"),systems);Fill(data.FindProperty("caustics"),lights);data.ApplyModifiedPropertiesWithoutUndo();
            var configData=new SerializedObject(c);
            configData.FindProperty("fallSeconds").floatValue=.8f;
            configData.FindProperty("splashSeconds").floatValue=1.8f;
            configData.FindProperty("minSplashSeconds").floatValue=1.2f;
            configData.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ceramic);EditorUtility.SetDirty(bubble);EditorUtility.SetDirty(shaft);
        }

        private static ParticleSystem Bubbles(Transform parent,int key,Material material)
        {
            var go=new GameObject("Breath bubbles "+key);go.transform.SetParent(parent,false);go.transform.rotation=Quaternion.Euler(-90,0,0);
            var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.loop=true;main.playOnAwake=false;main.startLifetime=new ParticleSystem.MinMaxCurve(.7f,1.6f);
            main.startSpeed=new ParticleSystem.MinMaxCurve(.5f,.9f);main.startSize=new ParticleSystem.MinMaxCurve(.045f,.13f);
            main.startColor=new Color(.75f,.95f,1,.75f);main.maxParticles=40;main.simulationSpace=ParticleSystemSimulationSpace.World;
            var emission=ps.emission;emission.rateOverTime=13;
            var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=12;shape.radius=.16f;
            var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;
            velocity.x=new ParticleSystem.MinMaxCurve(-.08f,.08f);velocity.y=new ParticleSystem.MinMaxCurve(.1f,.25f);velocity.z=new ParticleSystem.MinMaxCurve(-.08f,.08f);
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.renderMode=ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode=ShadowCastingMode.Off;return ps;
        }

        private static Material Material(string name,string shader)
        {
            string path=Materials+"/HS_"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(material,path);}return material;
        }

        private static Mesh Beam()
        {
            string path=Art+"/Meshes/HS_UnderwaterBeam.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh==null){mesh=new Mesh{name="Underwater sunlight ribbon"};AssetDatabase.CreateAsset(mesh,path);}
            mesh.Clear();mesh.vertices=new[]{new Vector3(-.12f,0,0),new Vector3(.12f,0,0),new Vector3(1.25f,-2.05f,0),new Vector3(.25f,-2.05f,0)};
            mesh.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);return mesh;
        }

        private static Texture2D CausticCookie()
        {
            const int size=128;string path=Art+"/Textures/HS_CausticCookie.asset";
            System.IO.Directory.CreateDirectory(Art+"/Textures");AssetDatabase.Refresh();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(texture==null){texture=new Texture2D(size,size,TextureFormat.RGBA32,false,true){name="Pool caustic cookie",wrapMode=TextureWrapMode.Repeat};AssetDatabase.CreateAsset(texture,path);}
            var colors=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float u=x/(float)size*7,v=y/(float)size*7;
                float wave=Mathf.Sin(u*2+Mathf.Sin(v*2))+Mathf.Sin(v*2.5f+Mathf.Sin(u*1.7f));
                float line=Mathf.Pow(Mathf.Clamp01(1-Mathf.Abs(wave)*3),3);
                colors[y*size+x]=new Color(line,line,line,line);
            }
            texture.SetPixels(colors);texture.Apply();EditorUtility.SetDirty(texture);return texture;
        }

        private static void Fill(SerializedProperty array,Object[] values)
        {
            array.arraySize=values.Length;for(int i=0;i<values.Length;i++)array.GetArrayElementAtIndex(i).objectReferenceValue=values[i];
        }
    }
}
