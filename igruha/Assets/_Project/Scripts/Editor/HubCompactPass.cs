using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Approved layout B. Replaces static store art while retaining TV, player, session and camera bindings.</summary>
    public static class HubCompactPass
    {
        internal const string RootName = "_HubOriginal";
        internal const float RoomSize = 20, InnerWall = 9.8f;
        private const string ScenePath = "Assets/_Project/Scenes/Hub.unity";
        internal static Transform Root;
        internal static Material M(string name) => HubOriginalAssets.Mat(name);

        [MenuItem("Igruha/Хаб/Компактный B — оригинальная комната")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if(EditorApplication.isPlaying || scene.path != ScenePath) throw new InvalidOperationException("Open Hub outside Play Mode.");
            HubOriginalAssets.Import();
            // Nothing under _Zones carries gameplay: all interaction is the preserved ConsoleTerminal/TV.
            var zones = Require("_Zones").transform;
            foreach(var script in zones.GetComponentsInChildren<MonoBehaviour>(true))
                if(script != null) throw new InvalidOperationException("Unexpected behaviour in static zones: " + script.GetType().Name);
            foreach(Transform t in zones.Cast<Transform>().ToArray()) Object.DestroyImmediate(t.gameObject);
            foreach(string name in new[]{RootName,"_HubCozy","_HubBarCozy","_HubRoomCozy","_HubLoungeFinish","_HubEntryCozy","_HubCameraOcclusion"})
            {var go=GameObject.Find(name);if(go!=null)Object.DestroyImmediate(go);}
            foreach(Transform t in Require("_Lighting").transform.Cast<Transform>().ToArray())Object.DestroyImmediate(t.gameObject);
            var keep = new[]{"PitFloor","PitWall_N","PitWall_S","PitWall_W","PitWall_E","TvScreen","SofaZone"};
            foreach(Transform t in Require("_Pit").transform.Cast<Transform>().ToArray())if(!keep.Contains(t.name))Object.DestroyImmediate(t.gameObject);
            Root=new GameObject(RootName).transform;
            Shell(); HubCompactArchitecture.Build(Root); HubCompactFurniture.Build(Root); HubCompactLighting.Apply(Root); Spawns();
            // Layout controllers may dirty this template at edit-time while rendering the world-space TV.
            foreach(var t in Require("_Pit/TvScreen").GetComponentsInChildren<RectTransform>(true))if(t.name=="Card_00")
            { t.localPosition=Vector3.zero;t.anchorMin=Vector2.zero;t.anchorMax=Vector2.zero;t.anchoredPosition=Vector2.zero;t.sizeDelta=Vector2.zero;t.pivot=Vector2.one*.5f; }
            Physics.SyncTransforms(); AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Compact Hub B: shell 24x24 -> 20x20 m; pit 8x7 m retained. " + Audit());
        }

        private static void Shell()
        {
            TransformShape("_Room/Floor_N",new Vector3(0,-.1f,6.75f),new Vector3(20,.2f,6.5f));
            TransformShape("_Room/Floor_S",new Vector3(0,-.1f,-6.75f),new Vector3(20,.2f,6.5f));
            TransformShape("_Room/Floor_W",new Vector3(-7,-.1f,0),new Vector3(6,.2f,7));
            TransformShape("_Room/Floor_E",new Vector3(7,-.1f,0),new Vector3(6,.2f,7));
            TransformShape("_Room/Wall_N",new Vector3(0,2,10),new Vector3(20,4,.4f));
            TransformShape("_Room/Wall_S",new Vector3(0,2,-10),new Vector3(20,4,.4f));
            TransformShape("_Room/Wall_W",new Vector3(-10,2,0),new Vector3(.4f,4,20));
            TransformShape("_Room/Wall_E",new Vector3(10,2,0),new Vector3(.4f,4,20));
            TransformShape("_Room/Ceiling",new Vector3(0,4,0),new Vector3(20,.2f,20));
            foreach(var r in Require("_Room").GetComponentsInChildren<MeshRenderer>())r.sharedMaterial=M(r.name=="Ceiling"?"Cream":"Walnut");
            foreach(var r in Require("_Pit").GetComponentsInChildren<MeshRenderer>())
                if(r.name.StartsWith("Pit")||r.name.StartsWith("Ramp"))r.sharedMaterial=M("Walnut");
        }
        private static void TransformShape(string path,Vector3 position,Vector3 size) {var t=Require(path).transform;t.position=position;t.localScale=size;}
        private static void Spawns()
        {
            var points=Require("_Spawns").transform.Cast<Transform>().Where(t=>t.GetComponent<Igruha.Core.Spawning.SpawnPoint>()!=null).OrderBy(t=>t.name,StringComparer.Ordinal).ToArray();
            if(points.Length!=8)throw new InvalidOperationException("Hub must have eight spawn points.");
            for(int i=0;i<8;i++) {points[i].position=new Vector3(-3.3f+(i%4)*1.8f,0,-7.1f+(i/4)*1.35f);points[i].rotation=Quaternion.LookRotation(new Vector3(0,0,1)-new Vector3(points[i].position.x,0,points[i].position.z));}
        }
        internal static GameObject Require(string path) => GameObject.Find(path) ?? throw new InvalidOperationException("Missing Hub object: "+path);
        internal static Transform Group(Transform parent,string name){var t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
        internal static BoxCollider Solid(Transform parent,string name,Vector3 center,Vector3 size)
        {var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=center;go.layer=LayerMask.NameToLayer("Ground");var c=go.AddComponent<BoxCollider>();c.size=size;return c;}
        internal static void ModelBox(GameObject go,Vector3 center,Vector3 size) {Solid(go.transform,"BodyCollider",center,size);}
        public static string Audit()
        {
            var scene=EditorSceneManager.GetActiveScene();var roots=scene.GetRootGameObjects();
            int missing=roots.Sum(go=>go.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)));
            int store=0;int meshes=0;long tris=0;
            foreach(var r in roots.SelectMany(go=>go.GetComponentsInChildren<MeshRenderer>(true)))
            {
                if(!r.enabled)continue;var f=r.GetComponent<MeshFilter>();if(f==null)continue;
                if(f.sharedMesh==null)throw new InvalidOperationException("Missing mesh: "+r.name);
                meshes++;tris+=f.sharedMesh.triangles.Length/3;
                if(AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith("Assets/Synty/"))store++;
                foreach(var mat in r.sharedMaterials)if(mat==null||mat.shader==null||!mat.shader.isSupported)throw new InvalidOperationException("Bad material: "+r.name);
            }
            if(missing!=0||store!=0)throw new InvalidOperationException("missingScripts="+missing+" Synty meshes="+store);
            return "Missing scripts: "+missing+"; direct Synty render meshes: "+store+"; renderers: "+meshes+"; triangles: "+tris+"; colliders: "+roots.Sum(go=>go.GetComponentsInChildren<Collider>(true).Length)+".";
        }
    }
}
