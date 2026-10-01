using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Igruha.Minigames.CarryItem;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Repeatable roadworks composition layered on the accepted skyscraper.
    /// Coordinates are metres; gameplay joints and their visible lips are the same object.</summary>
    internal static class CarryRoadArena
    {
        private const string Art = "Assets/_Project/Art/CarryItem/Roadworks";
        private const string Mats = Art + "/Materials";
        [Serializable] private class Palette { public Entry[] materials; }
        [Serializable] private class Entry { public string name; public float[] color; public float roughness, metallic; }
        [MenuItem("Igruha/Minigames/Polish Carry Item Roads")]
        internal static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().name != "CarryItem")
                throw new InvalidOperationException("Open CarryItem in Edit mode.");
            Import();
            Transform arena = GameObject.Find("_Arena").transform;
            var previous = arena.Find("Roadworks");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            Transform root = Group(arena, "Roadworks");
            ClearOldHazards();
            OpenBypasses(arena);
            DressFloors(arena);
            BuildRoads(root);
            BuildWorkAreas(root);
            var definition = AssetDatabase.LoadAssetAtPath<Igruha.Core.Minigame.MinigameDefinition>("Assets/_Project/Settings/Gameplay/Minigames/CarryItem.asset");
            var definitionSo = new SerializedObject(definition);
            definitionSo.FindProperty("tutorialSteps").GetArrayElementAtIndex(2).stringValue =
                "На стыках сбавляйте ход; сверху берегитесь балки. Чужую свободную тележку можно украсть по E и слить у своего насоса.";
            definitionSo.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssetIfDirty(definition);
            CarryHeistArena.Apply();
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("Carry roads: 6 central seams, 8 gentle bridge transitions; 4.5 m outer lanes; 10 original models.");
        }
        private static void Import()
        {
            Directory.CreateDirectory(Mats); AssetDatabase.Refresh();
            var palette = JsonUtility.FromJson<Palette>(File.ReadAllText(Art + "/palette.json"));
            var map = new Dictionary<string, Material>();
            foreach (var e in palette.materials)
            {
                string path = Mats + "/" + e.name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
                m.SetColor("_BaseColor", new Color(e.color[0], e.color[1], e.color[2]));
                m.SetFloat("_Metallic", e.metallic); m.SetFloat("_Smoothness", 1f - e.roughness); m.enableInstancing = true;
                EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); map.Add(e.name, m);
            }
            foreach (var path in Directory.GetFiles(Art + "/Models", "*.fbx"))
            {
                var i = (ModelImporter)AssetImporter.GetAtPath(path);
                i.bakeAxisConversion = true; i.addCollider = false; i.importCameras = i.importLights = i.importAnimation = false;
                i.animationType = ModelImporterAnimationType.None; i.isReadable = false;
                foreach (var m in map) i.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.Key), m.Value);
                i.SaveAndReimport();
            }
            string floorPath = Mats + "/RW_Floor.mat";
            var floor = AssetDatabase.LoadAssetAtPath<Material>(floorPath);
            if (floor == null) { floor = new Material(map["RW_Concrete"]); AssetDatabase.CreateAsset(floor, floorPath); }
            floor.SetColor("_BaseColor", Color.white);
            floor.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/Textures/RW_Concrete.png"));
            floor.SetFloat("_Smoothness", .13f); floor.enableInstancing = true;
            EditorUtility.SetDirty(floor); AssetDatabase.SaveAssetIfDirty(floor);
        }
        private static void ClearOldHazards()
        {
            // The former beam, spring plate and invisible push jet compete with the
            // road's cause/effect and split the already narrow two-team passing lane.
            var previewTap = GameObject.Find("WaterTap");
            if (previewTap != null && previewTap.transform.parent == null) Object.DestroyImmediate(previewTap);
            var traps = GameObject.Find("_Traps/CarryItemTraps");
            if (traps != null) Object.DestroyImmediate(traps);
            var jet = GameObject.Find("_Arena/VFX/PipeJet");
            if (jet != null) Object.DestroyImmediate(jet);
            foreach (var p in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (p.name == "PipeJet" || p.name == "BarrowBurst") Object.DestroyImmediate(p.gameObject);
        }
        private static void OpenBypasses(Transform arena)
        {
            var stashes = GameObject.Find("_Pickups/Stashes");
            if(stashes != null) foreach(Transform t in stashes.transform)
            {
                float s=Mathf.Sign(t.position.z);
                t.position=new Vector3(t.name.EndsWith("Near",StringComparison.Ordinal)?-.7f:5f, t.position.y, s*7.2f);
            }
            foreach(var t in arena.Find("BotRoutes").GetComponentsInChildren<Transform>())
                if(t.name == "Neck_In" || t.name == "Neck_Out")
                    t.position=new Vector3(t.position.x,t.position.y,Mathf.Sign(t.position.z)*1.35f);
            Transform rubble = arena.Find("Rubble");
            foreach (Transform t in rubble)
            {
                float sign = Mathf.Sign(t.position.z);
                var p = t.position; p.z = sign * 6.14f; t.position = p;
                var scale = t.localScale; scale.z = 6.52f; t.localScale = scale;
            }
            var work = arena.Find("Environment/WorkAreas");
            for (int i = work.childCount - 1; i >= 0; i--)
            {
                var t = work.GetChild(i);
                if ((t.position.x > -5.1f && t.position.x < 9.5f) ||
                    t.name == "CS_Lumber" || t.name == "CS_Workbench" || t.name == "CS_Mixer") Object.DestroyImmediate(t.gameObject);
            }
            var decor = arena.Find("Environment/Decor");
            for (int i = decor.childCount - 1; i >= 0; i--)
            {
                var t = decor.GetChild(i);
                if (t.position.x > -5.1f && t.position.x < 9.5f) Object.DestroyImmediate(t.gameObject);
            }
        }
        private static void DressFloors(Transform arena)
        {
            Material floor = Mat("Floor");
            foreach (var r in arena.Find("Floors").GetComponentsInChildren<MeshRenderer>())
                if (r.name.StartsWith("CS_Slab", StringComparison.Ordinal)) r.sharedMaterial = floor;
        }
        private static void BuildRoads(Transform root)
        {
            var road = Group(root, "Routes");
            // Central shortcut: patches with oblique bolted lips, no hidden trigger area.
            for (int i = 0; i < 6; i++)
            {
                float x = -1.8f + i * 1.55f;
                Joint(road, new Vector3(x, .008f, 0), 5.4f, i % 2 == 0 ? -4 : 4, 1f);
                if (i < 5) foreach (float z in new[] { -1.34f, 1.34f })
                    Place(road, "RepairPlate", new Vector3(x + .76f, .006f, z), 0, false);
            }
            // First/last boards introduce the interaction softly before the central choice.
            foreach (float z in new[] { -5.04f, 5.04f })
            foreach (float x in new[] { -12.9f, -5.8f, 10.15f, 15.75f })
                Joint(road, new Vector3(x, .296f, z), 2.5f, 0, .32f);
            foreach (int sign in new[] { -1, 1 })
            {
                float s = sign;
                Place(road, "RoughSign", new Vector3(-.55f, 0, s * 4.1f), 90, true);
                Place(road, "SmoothSign", new Vector3(-.55f, 0, s * 9.45f), 90, true);
                // Two painted edges lead through a 4.5 m clear outer bypass.
                Polyline(road, new[] { new Vector3(-3.4f,0,s*6.5f), new Vector3(-2.7f,0,s*11.5f),
                    new Vector3(6.9f,0,s*11.5f), new Vector3(8f,0,s*6.5f) }, Mat("Teal"), .10f);
                for (float x = -1.7f; x < 6f; x += 2.1f)
                    Chevron(road, new Vector3(x, .025f, s * 11.5f), 90, Mat("Ivory"), .48f);
                foreach (float x in new[] { -3.4f, 7.55f })
                    Chevron(road, new Vector3(x, .027f, s * 1.6f), 90, Mat("Ochre"), .64f);
                for (float x = -1.2f; x < 6f; x += 1.0f)
                    Strip(road, new Vector3(x, .018f, s * 2.72f), new Vector3(.55f,.012f,.08f), Mat("Ochre"));
                // Work zones sit on the inaccessible cores, not on the bypass.
                foreach (float z in new[] { 4.5f, 7.7f })
                    Place(root, "Curb", new Vector3(2.15f, 2.16f, s * z), 0, false);
                Place(root, "BrickPallet", new Vector3(1.3f,2.16f,s*5.7f), 4*sign, false);
                Place(root, "PipeCradle", new Vector3(2.9f,2.16f,s*7.5f), 0, false);
                CarryItemEnvironment.Place(root, "Tarp", new Vector3(1.35f,2.16f,s*8.5f), sign*18, false).localScale=Vector3.one*.7f;
                for (float x = .6f; x < 4.3f; x += 2f)
                    Place(root, "Drain", new Vector3(x,.008f,s*13.75f), 90, false);
            }
        }
        private static void BuildWorkAreas(Transform root)
        {
            // Authored islands: supply apron, pipe workshop, maintenance corner.
            foreach (int sign in new[] { -1, 1 })
            {
                float s = sign;
                Place(root, "BrickPallet", new Vector3(-18.8f,0,s*12.4f), sign*8, true);
                Place(root, "PipeCradle", new Vector3(-20.7f,0,s*11.55f), 90, true);
                Place(root, "ToolBench", new Vector3(22.6f,0,s*8.8f), 90, true);
                Place(root, "Debris", new Vector3(-16.2f,.003f,s*12.6f), sign*15, false);
                Place(root, "Debris", new Vector3(18.4f,.003f,s*12.5f), -sign*20, false);
                Place(root, "Curb", new Vector3(-18.3f,0,s*13.6f), 0, true);
                // Loading and pumping have their own outlined floor bays, with room for hands.
                Outline(root, new Vector3(-20.88f,0,s*7.2f), new Vector2(3.6f,3.4f),
                    CarrySkyscraperAssets.Material(sign>0?"TeamA":"TeamB"));
                Outline(root, new Vector3(19.6f,0,-s*5.04f), new Vector2(3.6f,3.2f),
                    CarrySkyscraperAssets.Material(sign>0?"TeamA":"TeamB"));
                foreach (float x in new[] { -26.5f, -23.5f, -20.5f, -17.5f, 18f, 21f, 24f })
                    Place(root, "Drain", new Vector3(x,.012f,s*13.85f), 90, false);
                for (float x=-19;x<-14;x+=1.35f)
                    Chevron(root,new Vector3(x,.025f,s*5.04f),90,CarrySkyscraperAssets.Material(sign>0?"TeamA":"TeamB"),.40f);
            }
        }
        internal static void Joint(Transform root, Vector3 p, float width, float yaw, float severity)
        {
            var t = Place(root, "Joint", p, yaw, false);
            t.GetChild(0).localScale = Vector3.Scale(t.GetChild(0).localScale, new Vector3(1,1,width/5.4f));
            t.gameObject.AddComponent<CartRoadJoint>().Configure(width,severity);
        }
        internal static Transform Place(Transform root, string name, Vector3 p, float yaw, bool solid)
        {
            var t=Group(root,"RW_"+name); t.localPosition=p; t.localRotation=Quaternion.Euler(0,yaw,0);
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Art+"/Models/RW_"+name+".fbx"),t);
            if(solid)
            {
                Quaternion rotation=t.rotation;t.rotation=Quaternion.identity;
                Bounds b=CarryItemDress.BoundsOf(t.gameObject);
                var c=t.gameObject.AddComponent<BoxCollider>(); c.center=t.InverseTransformPoint(b.center);
                c.size=b.size;t.rotation=rotation;
                t.gameObject.layer=LayerMask.NameToLayer("Cover");
            }
            return t;
        }
        internal static void Outline(Transform root,Vector3 center,Vector2 size,Material mat)
        {
            for(int s=-1;s<=1;s+=2)
            {
                Strip(root,center+new Vector3(0,.018f,s*size.y*.5f),new Vector3(size.x,.012f,.07f),mat);
                Strip(root,center+new Vector3(s*size.x*.5f,.018f,0),new Vector3(.07f,.012f,size.y),mat);
            }
        }
        internal static void Polyline(Transform root,Vector3[] points,Material mat,float width)
        {
            for(int i=1;i<points.Length;i++)
            {
                Vector3 d=points[i]-points[i-1];var t=Strip(root,(points[i]+points[i-1])*.5f+Vector3.up*.018f,new Vector3(width,.012f,d.magnitude),mat);
                t.rotation=Quaternion.LookRotation(d);
            }
        }
        internal static void Chevron(Transform root,Vector3 p,float yaw,Material mat,float size)
        {
            var q=Quaternion.Euler(0,yaw,0);
            Polyline(root,new[]{p+q*new Vector3(-size,0,-size*.55f),p+q*Vector3.forward*size*.3f,p+q*new Vector3(size,0,-size*.55f)},mat,.11f);
        }
        private static Transform Strip(Transform root,Vector3 p,Vector3 scale,Material mat)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name="RoadPaint";g.transform.SetParent(root,false);g.transform.localPosition=p;g.transform.localScale=scale;
            Object.DestroyImmediate(g.GetComponent<Collider>());var r=g.GetComponent<Renderer>();r.sharedMaterial=mat;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return g.transform;
        }
        private static Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>(Mats+"/RW_"+name+".mat");
        private static Transform Group(Transform p,string name){var t=new GameObject(name).transform;t.SetParent(p,false);return t;}
    }
}
