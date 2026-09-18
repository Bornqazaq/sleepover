using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Igruha.Core.Arena;
using Igruha.Core.Traps;
using static Igruha.EditorTools.InfectionQuarantineAssets;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    internal static class InfectionQuarantineBuilder
    {
        internal const string ScenePath = "Assets/_Project/Scenes/Minigames/Infection.unity";
        private const string RootName = "_QuarantineArt";
        [MenuItem("Igruha/Minigames/Infection/Apply Quarantine Art")]
        internal static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first");
            if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
            Prepare();
            foreach(var groupName in new[]{"_Traps","_Pickups"})
            {
                var group=GameObject.Find(groupName);
                if(group==null)continue;
                foreach(Transform child in group.transform.Cast<Transform>().ToArray())
                    if(new[]{"SpringTrap","FallingCrateTrap","TrapButton","PickupCube"}.Contains(child.name))
                        Object.DestroyImmediate(child.gameObject);
            }
            var old = GameObject.Find(RootName); if (old != null) Object.DestroyImmediate(old);
            var arena = GameObject.Find("_Arena").transform;
            foreach (var t in arena.GetComponentsInChildren<Transform>(true).Reverse())
                if (t != null && t.name.StartsWith("Art_")) Object.DestroyImmediate(t.gameObject);
            foreach (var r in arena.GetComponentsInChildren<Renderer>()) Object.DestroyImmediate(r);
            var root = Group(RootName, null);
            Place("Courtyard", root, Vector3.zero);
            Dress(arena.Find("Carousel"), "Carousel");
            Dress(arena.Find("Slide"), "Slide");
            Dress(arena.Find("Climber"), "Climber");
            Dress(arena.Find("Sandbox"), "Sandbox");
            var swings = arena.Find("Swings");
            Dress(swings, "SwingFrame");
            foreach (var swing in swings.GetComponentsInChildren<PendulumSwing>())
            {
                var model = Place("SwingSeat", swing.transform, Vector3.zero);
                var s = swing.transform.lossyScale;
                model.transform.localScale = new Vector3(1 / s.x, 1 / s.y, 1 / s.z);
            }
            foreach (var shell in arena.GetComponentsInChildren<SeeThroughShell>())
            {
                var model = Place("Tube", shell.transform, Vector3.zero);
                var so = new SerializedObject(shell);
                var array = so.FindProperty("shell"); var renderers = model.GetComponentsInChildren<Renderer>();
                array.arraySize = renderers.Length;
                for (int i = 0; i < renderers.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
                so.FindProperty("transparentMaterial").objectReferenceValue = InfectionQuarantineEffects.GhostMaterial();
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            BuildPerimeter(root);
            BuildCity(root);
            InfectionQuarantineEffects.Build(root);
            InfectionQuarantineFeedback.Build(root);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
            Debug.Log("Infection: original quarantine art applied; gameplay components and colliders retained.");
        }
        private static void Dress(Transform parent, string model) => Place(model, parent, Vector3.zero);
        private static void BuildPerimeter(Transform root)
        {
            var iron = Material("INF_Iron", new Color(.085f,.09f,.083f));
            var concrete = Material("INF_Concrete", new Color(.41f,.38f,.31f));
            var yellow = Material("INF_Yellow", new Color(.82f,.53f,.12f));
            var asphalt = Material("Asphalt", new Color(.16f,.15f,.13f));
            var ground = Group("Street", root);
            Box("RoadBed", ground, new Vector3(0,-.32f,0), new Vector3(150,.3f,150), asphalt);
            for(int side=-1;side<=1;side+=2)
            {
                Box("Sidewalk", ground,new Vector3(0,-.11f,side*22.1f),new Vector3(57,.2f,3.7f),concrete);
                Box("Sidewalk", ground,new Vector3(side*28.1f,-.11f,0),new Vector3(3.7f,.2f,40),concrete);
                for(int i=0;i<13;i++) Place("Fence",ground,new Vector3(-24+i*4,0,side*20),0);
                for(int i=0;i<10;i++) Place("Fence",ground,new Vector3(side*26,0,-18+i*4),90);
                for(int i=-7;i<=7;i++)
                {
                    Box("Curb",ground,new Vector3(i*4,-.04f,side*24),new Vector3(3.94f,.27f,.30f),concrete);
                    Box("RoadMarking",ground,new Vector3(i*6,-.156f,side*29),new Vector3(2.5f,.008f,.12f),yellow);
                }
            }
            // Tape along short sections of the perimeter, clear of all gameplay routes.
            for(int i=0;i<24;i++)
            {
                var strip=Box("QuarantineTape",ground,new Vector3(-13+i*.35f,1.42f,19.94f),new Vector3(.36f,.18f,.023f),i%2==0?yellow:iron);
                strip.transform.localRotation=Quaternion.Euler(0,0,Mathf.Sin(i*.25f)*8);
            }
            Combine(ground);
            var props=Group("PerimeterProps",root);
            var random=new System.Random(218);
            for(int side=-1;side<=1;side+=2)
            {
                for(int i=0;i<5;i++)
                {
                    float x=-21+i*10.5f;
                    Place("Tree",props,new Vector3(x+.6f,0,side*23),i*73,.9f+(i%3)*.22f);
                    Place("Weeds",props,new Vector3(x,0,side*20.9f),i*33,1.7f);
                    Place("Debris",props,new Vector3(x+2,0,side*22.3f),i*51);
                }
                Place("Bench",props,new Vector3(-8,0,side*21.6f),side<0?180:0);
                Place("Bench",props,new Vector3(16,0,side*21.6f),side<0?180:0);
                for(int i=0;i<4;i++)Place("Barrel",props,new Vector3(-22+i*.95f,0,side*21.3f));
                Place("Sandbags",props,new Vector3(-13,0,side*21.3f),0,1.2f);
                Place("Tires",props,new Vector3(24,0,side*21.5f),0,1.25f);
                for(int i=0;i<4;i++)Place("Pallet",props,new Vector3(22,.48f*i,side*22.5f),i*4);
                for(int i=0;i<8;i++)
                {
                    float x=-22+i*6.2f;
                    Place("DirtIsland",props,new Vector3(x,0,side*19.1f),i*47,1.1f);
                    Place("Weeds",props,new Vector3(x,0,side*19.45f),i*63,.9f);
                }
            }
            for(int i=0;i<28;i++)
            {
                float x=(float)random.NextDouble()*48-24, z=(float)random.NextDouble()*36-18;
                if(Mathf.Abs(z)>17 || Mathf.Abs(x)>23)Place("Weeds",props,new Vector3(x,0,z),i*39,.65f);
            }
            // A few small storytelling clusters outside the moving carousel and all spawn capsules.
            Place("Debris",props,new Vector3(-12,0,-7)); Place("Debris",props,new Vector3(12,0,5));
            Place("Weeds",props,new Vector3(-21.4f,0,-4),90,.7f);
            Place("Weeds",props,new Vector3(4.8f,0,-12),90,.7f);
            Place("Teddy",props,new Vector3(5.5f,0,-14),-35,.35f);
            Place("Bucket",props,new Vector3(2.4f,.12f,-12.9f),-20,1.5f);
            Place("Bucket",props,new Vector3(-2.9f,.12f,-10.8f),20);
            Place("TornTarp",props,new Vector3(-4,.3f,19.8f));
            Place("TornTarp",props,new Vector3(25.8f,.3f,7),90);
            Place("Barricade",props,new Vector3(1,0,22));
            Place("Barricade",props,new Vector3(4.2f,0,22),10);
            Place("DirtIsland",props,new Vector3(-18,0,-4),0,2.1f);
            Place("DirtIsland",props,new Vector3(13,0,10),0,2.3f);
            Place("DirtIsland",props,new Vector3(4.5f,0,-11),-15,1.5f);
            Place("DirtIsland",props,new Vector3(-15.8f,0,5),0,1.3f);
            Place("DirtIsland",props,new Vector3(17,0,-4),0,2);
            Place("TrashCan",props,new Vector3(-10.5f,0,-21.4f),15);
            Place("TrashCan",props,new Vector3(19,0,21.5f),-30);
            Place("Cart",props,new Vector3(25.9f,0,23.3f),-30);
            Place("Cart",props,new Vector3(-28,0,-11),55);
            Place("Bicycle",props,new Vector3(26.7f,0,-13),5);
            Place("Bicycle",props,new Vector3(-7,0,21.3f),80);
            Combine(props);
            Sign(root,new Vector3(2,2.45f,19.82f),"QUARANTINE",0);
            Sign(root,new Vector3(-18,1.65f,-19.82f),"KEEP OUT",180);
            Sign(root,new Vector3(25.82f,1.9f,4),"SECTOR 13",-90);
        }
        private static void Sign(Transform parent,Vector3 pos,string label,float yaw)
        {
            var sign=Group("WarningSign",parent);sign.localPosition=pos;sign.localRotation=Quaternion.Euler(0,yaw,0);
            Box("EnamelSign",sign,Vector3.zero,new Vector3(3.4f,.68f,.07f),Material("SignIvory",new Color(.75f,.65f,.44f)));
            var text=Group("Lettering",sign).gameObject.AddComponent<TMPro.TextMeshPro>();
            text.transform.localPosition=new Vector3(0,0,-.046f);text.transform.localRotation=Quaternion.Euler(0,180,0);
            text.text=label;text.fontSize=3.2f;text.color=new Color(.13f,.075f,.035f);text.alignment=TMPro.TextAlignmentOptions.Center;
            text.rectTransform.sizeDelta=new Vector2(3.25f,.62f);
        }
        private static void BuildCity(Transform root)
        {
            for(int side=-1;side<=1;side+=2)
            {
                var buildings=Group(side<0?"SouthBlocks":"NorthBlocks",root);
                for(int i=0;i<5;i++)Place("Apartment"+(i%2+1),buildings,new Vector3(-40+i*20,0,side*36),side<0?180:0,1);
                Combine(buildings);
                buildings=Group(side<0?"WestBlocks":"EastBlocks",root);
                for(int i=0;i<3;i++)Place("Apartment"+(i%2+1),buildings,new Vector3(side*43,0,-21+i*21),side<0?-90:90,1);
                Combine(buildings);
            }
            var cars=Group("AbandonedTraffic",root);
            Place("Ambulance",cars,new Vector3(-13,0,27),-70);
            Place("Bus",cars,new Vector3(27,0,27),105);
            Place("Sedan",cars,new Vector3(-29,0,14),15);
            Place("Sedan",cars,new Vector3(12,0,-28),100);
            Place("Sedan",cars,new Vector3(5,0,31),84);
            Place("Bus",cars,new Vector3(-34,0,-20),5);
            Combine(cars);
        }
        [MenuItem("Igruha/Minigames/Infection/Audit Quarantine Art")]
        internal static void Audit()
        {
            var scene=EditorSceneManager.GetActiveScene();
            if(scene.path!=ScenePath)throw new InvalidOperationException("Open Infection first");
            var roots=scene.GetRootGameObjects();
            if(roots.Count(x=>x.name==RootName)!=1)throw new InvalidOperationException("Art root count");
            var arena=GameObject.Find("_Arena");
            int visibleBlockout=arena.GetComponentsInChildren<Renderer>().Count(r=>r.enabled && !r.name.StartsWith("Art_") && !HasArtParent(r.transform));
            if(visibleBlockout!=0)throw new InvalidOperationException("Visible blockout: "+visibleBlockout);
            foreach(var r in roots.SelectMany(x=>x.GetComponentsInChildren<Renderer>()))
                if(r.enabled && r.sharedMaterials.Any(m=>m==null||m.shader==null))throw new InvalidOperationException("Missing material: "+r.name);
            foreach(var shell in arena.GetComponentsInChildren<SeeThroughShell>())
            {
                var array=new SerializedObject(shell).FindProperty("shell");
                for(int i=0;i<array.arraySize;i++) if(array.GetArrayElementAtIndex(i).objectReferenceValue==null)throw new InvalidOperationException("Missing tube renderer");
            }
            var wood=GameObject.Find(RootName).GetComponentsInChildren<Renderer>().First(r=>r.name=="Combined_INF_Wood"&&r.transform.parent.name=="PerimeterProps");
            if(wood.bounds.max.y<6)throw new InvalidOperationException("Imported tree scale lost during placement");
            var dependencies=AssetDatabase.GetDependencies(ScenePath,true);
            if(dependencies.Any(x=>x.Contains("Synty/")||x.Contains("Polygon")))Debug.LogWarning("Infection scene contains purchased dependencies (inspect character dependencies separately)");
            Debug.Log("Quarantine audit: one art root, no visible blockout, tube bindings and materials valid. Arena colliders="+arena.GetComponentsInChildren<Collider>().Length+"; scenery colliders="+GameObject.Find(RootName).GetComponentsInChildren<Collider>().Length);
        }
        private static bool HasArtParent(Transform t)
        { while(t!=null){if(t.name.StartsWith("Art_"))return true;t=t.parent;}return false; }
    }
}
