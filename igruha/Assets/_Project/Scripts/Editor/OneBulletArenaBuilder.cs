using System;
using System.Collections.Generic;
using System.IO;
using Igruha.Core.Arena;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Spawning;
using Igruha.Core.UI;
using Igruha.Minigames.OneBullet;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    public static class OneBulletArenaBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Minigames/OneBullet.unity";
        private const string Settings = "Assets/_Project/Settings/Gameplay/Minigames/";
        private const string Art = "Assets/_Project/Art/Minigames/OneBullet";
        private const string LayoutPath = "Assets/_Project/Scripts/Minigames/OneBullet/OneBulletLayout.json";
        private const float Width = 2.16f, Size = 43.2f, WallTop = 5.76f, Pixel = .12f;
        private const int Resolution = 360;
        [Serializable] public class Edge { public int a, b; }
        [Serializable] public class Layout { public int grid; public float pitch, width; public Edge[] edges; public int[] rooms, spawns, guns; }
        private static Material grey, gold, wood;
        public static Layout ReadLayout() => JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
        public static Vector3 Position(Layout l, int index)
        {
            float x = (index % l.grid - 4) * l.pitch, z = (index / l.grid - 4) * l.pitch;
            return new Vector3(x, Mathf.Clamp(Mathf.Min(x,z) / 4.8f, 0, 2) * 1.08f, z);
        }
        [MenuItem("Igruha/Minigames/Build One Bullet blockout")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop play mode before building.");
            // Always open the source explicitly. Never clear an arbitrary active arena.
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/MinigameTemplate.unity");
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath, true);
            EditorSceneManager.OpenScene(ScenePath);
            Directory.CreateDirectory(Art); AssetDatabase.Refresh();
            grey = Material("Blockout", new Color(.56f,.56f,.54f));
            gold = Material("GunMetal",new Color(.75f,.65f,.4f));
            wood = Material("Grip",new Color(.38f,.19f,.08f));
            var l = ReadLayout();
            var arena = GameObject.Find("_Arena").transform; Clear(arena);
            Clear(GameObject.Find("_Traps").transform); Clear(GameObject.Find("_Pickups").transform);
            Box(arena, "Foundation", new Vector3(0,-.5f,0), new Vector3(Size,1,Size), grey);
            var walk = new bool[Resolution,Resolution];
            for (int i=0;i<l.grid*l.grid;i++)
            {
                Vector3 p=Position(l,i); float w = Array.IndexOf(l.rooms,i)>=0 ? 5.76f : Width;
                Box(arena,"Landing_"+i,p-Vector3.up*.15f,new Vector3(w,.3f,w),grey);
                Mark(walk,p.x,p.z,w,w);
            }
            foreach (var e in l.edges)
            {
                Vector3 a=Position(l,e.a),b=Position(l,e.b), delta=b-a;
                Vector3 flat=new Vector3(delta.x,0,delta.z).normalized;
                Vector3 from=a+flat*(Width*.5f), to=b-flat*(Width*.5f);
                Vector3 span=to-from;
                var floor=Box(arena,"Passage_"+e.a+"_"+e.b,(from+to)*.5f-Vector3.up*.15f,new Vector3(Width,.3f,span.magnitude+.06f),grey);
                floor.transform.rotation=Quaternion.LookRotation(span,Vector3.up);
                if (Mathf.Abs(span.y)>.01f) floor.AddComponent<WalkableRamp>();
                Mark(walk,(a.x+b.x)*.5f,(a.z+b.z)*.5f, Mathf.Abs(delta.x)+Width,Mathf.Abs(delta.z)+Width);
            }
            var walls=new GameObject("Walls").transform; walls.SetParent(arena,false);
            var used=new bool[Resolution,Resolution]; int count=0;
            for(int z=0;z<Resolution;z++) for(int x=0;x<Resolution;x++)
            {
                if(walk[x,z]||used[x,z]) continue;
                int w=1,h=1;
                while(x+w<Resolution&&!walk[x+w,z]&&!used[x+w,z])w++;
                bool expand=true;
                while(z+h<Resolution&&expand)
                {
                    for(int u=x;u<x+w;u++) if(walk[u,z+h]||used[u,z+h]) {expand=false;break;}
                    if(expand)h++;
                }
                for(int v=z;v<z+h;v++)for(int u=x;u<x+w;u++)used[u,v]=true;
                Box(walls,"Wall_"+(count++),new Vector3((x+w*.5f)*Pixel-Size*.5f,WallTop*.5f,(z+h*.5f)*Pixel-Size*.5f),new Vector3(w*Pixel,WallTop,h*Pixel),grey);
            }
            var spawns=GameObject.Find("_Spawns");
            foreach(var p in spawns.GetComponentsInChildren<SpawnPoint>())Object.DestroyImmediate(p.gameObject);
            var safe=new Transform[l.spawns.Length];
            for(int i=0;i<safe.Length;i++)
            {
                var g=new GameObject("Spawn_"+i);g.transform.SetParent(spawns.transform,false);
                g.transform.position=Position(l,l.spawns[i])+Vector3.up*.08f;g.AddComponent<SpawnPoint>();safe[i]=g.transform;
            }
            Set(spawns.GetComponent<PlayerSpawner>(),"debugPlayerCount",4);
            var guns=new Transform[l.guns.Length];
            var gunPoints=new GameObject("WeaponSpawns").transform;gunPoints.SetParent(arena,false);
            for(int i=0;i<guns.Length;i++)
            {
                guns[i]=new GameObject("WeaponSpawn_"+i).transform;guns[i].SetParent(gunPoints,false);
                guns[i].position=Position(l,l.guns[i])+Vector3.up*.18f;
            }
            var bounds=GameObject.Find("_Bounds");
            var kill=bounds.GetComponentInChildren<KillZone>();
            if(kill!=null){kill.transform.position=new Vector3(0,-5,0);kill.GetComponent<BoxCollider>().size=new Vector3(Size+8,1,Size+8);}
            var manager=GameObject.Find("MinigameManager");
            Object.DestroyImmediate(manager.GetComponent<TemplateMinigame>());
            var game=manager.AddComponent<OneBulletMinigame>();
            manager.AddComponent<OneBulletNetwork>();
            var config=Asset<OneBulletConfig>(Settings+"OneBulletConfig.asset");
            var definition=Asset<MinigameDefinition>(Settings+"OneBullet.asset");
            Set(definition,"displayName","Один патрон");Set(definition,"sceneName","OneBullet");
            Set(definition,"roundDuration",300f);Set(definition,"minPlayers",2);Set(definition,"maxPlayers",8);
            Set(definition,"objective","Найди единственный пистолет. Один патрон — одно решение. Выживи или держи оружие в конце раунда.");
            Strings(definition,"controlHints",new[]{"WASD — бег · Space — прыжок · Ctrl — присед","ЛКМ — выстрел с оружием · ПКМ / Shift — толчок","Оружие подбирается касанием. После выстрела ищи его снова.","На финише держатель оружия первый; затем — убийства и время жизни."});
            Strings(definition,"tutorialSteps",new[]{"Исследуй лабиринт. Запоминай ориентиры и слушай шаги.","Найди револьвер: один выстрел, даже если промахнёшься.","Выживи. Оружие в руке на финише приносит первое место."});
            var ui=GameObject.Find("_UI");var canvas=ui.GetComponentInChildren<Canvas>();
            var hud=ui.GetComponentInChildren<RoundHud>();
            Set(game,"definition",definition);Set(game,"config",config);Set(game,"roundTimer",manager.GetComponent<RoundTimer>());
            Set(game,"hud",hud);Set(game,"tutorialScreen",ui.GetComponentInChildren<TutorialScreen>());
            Refs(game,"weaponSpawns",guns);Refs(game,"safeSpawns",safe);
            Set(manager.GetComponent<MinigameBootstrap>(),"minigame",game);
            var camera=GameObject.Find("_Camera");var spectator=manager.AddComponent<SpectatorCamera>();
            Set(spectator,"cameraController",camera.GetComponentInChildren<MinigameCameraController>());
            Set(spectator,"cameraRig",camera.GetComponentInChildren<ThirdPersonCameraRig>());Set(spectator,"hud",hud);
            Set(game,"spectator",spectator);Set(game,"gameCamera",camera.GetComponentInChildren<Camera>());
            BuildPresentation(game,canvas.transform);
            Register(definition);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());AssetDatabase.SaveAssets();
            Debug.Log("OneBullet blockout built: 81 nodes, 87 passages, 14 T, 4 crossings, 10 dead ends, 12 weapon spawns.");
        }
        private static void BuildPresentation(OneBulletMinigame game,Transform canvas)
        {
            var root=new GameObject("OneBulletPresentation");var view=root.AddComponent<OneBulletPresentation>();
            var gun=new GameObject("Revolver").transform;gun.SetParent(root.transform,false);
            Box(gun,"Barrel",new Vector3(0,.07f,.14f),new Vector3(.08f,.08f,.28f),gold,false);
            Box(gun,"Cylinder",new Vector3(0,.06f,-.025f),new Vector3(.13f,.13f,.14f),gold,false);
            var grip=Box(gun,"Grip",new Vector3(0,-.075f,-.1f),new Vector3(.09f,.21f,.10f),wood,false);grip.transform.localRotation=Quaternion.Euler(-20,0,0);
            var lamp=new GameObject("PickupGlow").AddComponent<Light>();lamp.transform.SetParent(root.transform,false);
            lamp.type=LightType.Point;lamp.range=2.5f;lamp.intensity=1.3f;lamp.color=new Color(1,.58f,.2f);lamp.shadows=LightShadows.Soft;
            var plate=new GameObject("OneRound",typeof(RectTransform),typeof(CanvasGroup));plate.transform.SetParent(canvas,false);
            var rect=(RectTransform)plate.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.18f);rect.sizeDelta=new Vector2(440,65);
            var label=plate.AddComponent<TextMeshProUGUI>();label.text="ОДИН ПАТРОН  •  ЛКМ";label.fontSize=24;label.alignment=TextAlignmentOptions.Center;label.color=new Color(1,.83f,.49f);label.raycastTarget=false;
            var cross=new GameObject("Reticle",typeof(RectTransform)).AddComponent<TextMeshProUGUI>();cross.transform.SetParent(plate.transform,false);
            var cr=(RectTransform)cross.transform;cr.anchorMin=cr.anchorMax=new Vector2(.5f,.5f);cr.anchoredPosition=new Vector2(0,230);cr.sizeDelta=new Vector2(40,40);cross.text="+";cross.fontSize=24;cross.alignment=TextAlignmentOptions.Center;cross.raycastTarget=false;
            var trace=new GameObject("ShotTrace").AddComponent<LineRenderer>();trace.transform.SetParent(root.transform,false);trace.positionCount=2;trace.startWidth=.018f;trace.endWidth=.004f;trace.sharedMaterial=gold;trace.enabled=false;
            Set(view,"game",game);Set(view,"gun",gun);Set(view,"pickupLight",lamp);Set(view,"ammunition",plate.GetComponent<CanvasGroup>());Set(view,"tracer",trace);
            gun.gameObject.SetActive(false);lamp.enabled=false;plate.GetComponent<CanvasGroup>().alpha=0;
        }
        private static void Mark(bool[,] map,float x,float z,float width,float depth)
        {
            int x0=Mathf.RoundToInt((x-width*.5f+Size*.5f)/Pixel),x1=Mathf.RoundToInt((x+width*.5f+Size*.5f)/Pixel);
            int z0=Mathf.RoundToInt((z-depth*.5f+Size*.5f)/Pixel),z1=Mathf.RoundToInt((z+depth*.5f+Size*.5f)/Pixel);
            for(int v=Mathf.Max(0,z0);v<Mathf.Min(Resolution,z1);v++)for(int u=Mathf.Max(0,x0);u<Mathf.Min(Resolution,x1);u++)map[u,v]=true;
        }
        private static void Clear(Transform root){while(root.childCount>0)Object.DestroyImmediate(root.GetChild(0).gameObject);}
        private static GameObject Box(Transform parent,string name,Vector3 pos,Vector3 scale,Material material,bool collide=true)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=pos;go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=material;go.layer=LayerMask.NameToLayer("Ground");
            if(!collide)Object.DestroyImmediate(go.GetComponent<Collider>());return go;
        }
        private static Material Material(string name,Color color)
        {
            string path=Art+"/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",.15f);EditorUtility.SetDirty(material);return material;
        }
        private static T Asset<T>(string path) where T:ScriptableObject
        {var value=AssetDatabase.LoadAssetAtPath<T>(path);if(value==null){value=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(value,path);}return value;}
        public static void Set(Object obj,string name,Object value){var s=new SerializedObject(obj);s.FindProperty(name).objectReferenceValue=value;s.ApplyModifiedPropertiesWithoutUndo();}
        public static void Set(Object obj,string name,float value){var s=new SerializedObject(obj);s.FindProperty(name).floatValue=value;s.ApplyModifiedPropertiesWithoutUndo();}
        public static void Set(Object obj,string name,int value){var s=new SerializedObject(obj);s.FindProperty(name).intValue=value;s.ApplyModifiedPropertiesWithoutUndo();}
        public static void Set(Object obj,string name,string value){var s=new SerializedObject(obj);s.FindProperty(name).stringValue=value;s.ApplyModifiedPropertiesWithoutUndo();}
        private static void Strings(Object obj,string name,string[] values){var s=new SerializedObject(obj);var p=s.FindProperty(name);p.arraySize=values.Length;for(int i=0;i<values.Length;i++)p.GetArrayElementAtIndex(i).stringValue=values[i];s.ApplyModifiedPropertiesWithoutUndo();}
        private static void Refs(Object obj,string name,Transform[] values){var s=new SerializedObject(obj);var p=s.FindProperty(name);p.arraySize=values.Length;for(int i=0;i<values.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=values[i];s.ApplyModifiedPropertiesWithoutUndo();}
        private static void Register(MinigameDefinition definition)
        {
            var scenes=new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);if(!scenes.Exists(s=>s.path==ScenePath))scenes.Add(new EditorBuildSettingsScene(ScenePath,true));EditorBuildSettings.scenes=scenes.ToArray();
            var catalog=AssetDatabase.LoadAssetAtPath<MinigameCatalog>(Settings+"MinigameCatalog.asset");var so=new SerializedObject(catalog);var games=so.FindProperty("games");
            for(int i=0;i<games.arraySize;i++)if(games.GetArrayElementAtIndex(i).objectReferenceValue==definition)return;
            games.arraySize++;games.GetArrayElementAtIndex(games.arraySize-1).objectReferenceValue=definition;so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
