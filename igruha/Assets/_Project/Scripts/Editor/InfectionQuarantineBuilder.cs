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
            InfectionCourtyardLayout.ApplyProps(arena);
            var root = Group(RootName, null);
            Place("Courtyard", root, Vector3.zero);
            Dress(arena.Find("Carousel"), "Carousel");
            Dress(arena.Find("Carousel"), "CarouselDetail");
            Dress(arena.Find("Slide"), "Slide");
            Dress(arena.Find("Slide"), "SlideDetail");
            Dress(arena.Find("Climber"), "Climber");
            Dress(arena.Find("Climber"), "ClimberDetail");
            Dress(arena.Find("Sandbox"), "Sandbox");
            Dress(arena.Find("Sandbox"), "SandboxDetail");
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
            InfectionRuinedCourtyard.Build(root);
            InfectionQuarantineEffects.Build(root);
            InfectionQuarantineFeedback.Build(root);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
            Debug.Log("Infection: original quarantine art applied; gameplay components and colliders retained.");
        }
        private static void Dress(Transform parent, string model) => Place(model, parent, Vector3.zero);
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
            var wood=GameObject.Find(RootName).GetComponentsInChildren<Renderer>().First(r=>r.name=="Combined_INF_Wood"&&r.transform.parent.name=="CourtyardDetails");
            if(wood.bounds.max.y<6)throw new InvalidOperationException("Imported tree scale lost during placement");
            InfectionCourtyardAudit.Check();
            InfectionCityBackdrop.Audit();
            var dependencies=AssetDatabase.GetDependencies(ScenePath,true);
            if(dependencies.Any(x=>x.Contains("Synty/")||x.Contains("Polygon")))Debug.LogWarning("Infection scene contains purchased dependencies (inspect character dependencies separately)");
            Debug.Log("Quarantine audit: one art root, no visible blockout, tube bindings and materials valid. Arena colliders="+arena.GetComponentsInChildren<Collider>().Length+"; scenery colliders="+GameObject.Find(RootName).GetComponentsInChildren<Collider>().Length);
        }
        private static bool HasArtParent(Transform t)
        { while(t!=null){if(t.name.StartsWith("Art_"))return true;t=t.parent;}return false; }
    }
}
