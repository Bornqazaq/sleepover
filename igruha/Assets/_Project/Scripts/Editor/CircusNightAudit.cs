using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Igruha.Minigames.Circus;

namespace Igruha.EditorTools
{
    /// <summary>Regression checks for scene wiring, imported skinning, and the stationary taunt.</summary>
    internal static class CircusNightAudit
    {
        [MenuItem("Igruha/Цирк/Проверить оформление шапито")]
        internal static void CheckMenu() => Debug.Log(CheckBoth());

        internal static string CheckBoth()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Run the audit outside Play Mode.");
            if(EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save scene changes before auditing both maps.");
            string original=EditorSceneManager.GetActiveScene().path;
            var result=new StringBuilder();
            try
            {
                foreach(string name in new[]{"Stopwatch","CansOrder"})
                {
                    var scene=EditorSceneManager.OpenScene("Assets/_Project/Scenes/Minigames/"+name+".unity",OpenSceneMode.Single);
                    var roots=scene.GetRootGameObjects();
                    Require(roots.Count(r=>r.name=="_CircusNight")==1,"Exactly one circus art root");
                    var bear=roots.SelectMany(r=>r.GetComponentsInChildren<PitBear>(true)).Single();
                    var animator=bear.GetComponentInChildren<Animator>();
                    Require(animator!=null && animator.runtimeAnimatorController!=null,"Bear animator assigned");
                    Require(!animator.isHuman,"Original generic bear skeleton");
                    Require(bear.GetComponentsInChildren<SkinnedMeshRenderer>().Length==1,"One skinned renderer");
                    Require(bear.GetComponentsInChildren<Transform>().All(t=>t.name!="BeastBody" && t.name!="BeastMask"),"No werewolf visual");
                    Require(GameObject.Find("_CircusNight").GetComponentsInChildren<Collider>(true).Length==0,"Scenery adds no gameplay collisions");
                    var game=GameObject.Find("MinigameManager").GetComponents<MonoBehaviour>().Single(m=>m.GetType().Name==name+"Minigame");
                    var data=new SerializedObject(game);
                    foreach(string field in new[]{"bear","scoreboard"})Require(data.FindProperty(field).objectReferenceValue!=null,name+"."+field);
                    var cages=data.FindProperty("cages");Require(cages.arraySize==8,"Eight cage bindings");
                    for(int i=0;i<cages.arraySize;i++)Require(cages.GetArrayElementAtIndex(i).objectReferenceValue!=null,"Cage "+i+" bound");
                    int missing=roots.Sum(r=>r.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)));
                    Require(missing==0,"No missing scene scripts");
                    int shadowLights=GameObject.Find("_CircusNight").GetComponentsInChildren<Light>().Count(l=>l.enabled && l.shadows!=LightShadows.None);
                    Require(shadowLights==4,"Four shadow lights keep the local light budget bounded");
                    var camera=GameObject.Find("_Camera").GetComponentInChildren<Camera>(true);
                    var cameraData=camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                    Require(cameraData!=null && cameraData.renderPostProcessing,"Camera actually applies the grade");
                    var bulb=CircusNightAssets.Material("CN_WarmBulb");
                    Require(bulb!=null && bulb.IsKeywordEnabled("_EMISSION") && (bulb.globalIlluminationFlags & MaterialGlobalIlluminationFlags.AnyEmissive)!=0,"Bulb emission survives URP material validation");
                    CheckCraft(scene.path);
                    result.AppendLine(name+": 8 cage bindings, scoreboard, bear, original art, camera post processing and missing-script checks PASS.");
                    CheckBear(bear,result);
                }
            }
            finally{if(!string.IsNullOrEmpty(original))EditorSceneManager.OpenScene(original,OpenSceneMode.Single);}
            return result.ToString();
        }

        private static void CheckBear(PitBear original,StringBuilder result)
        {
            var copy=UnityEngine.Object.Instantiate(original.gameObject);copy.hideFlags=HideFlags.HideAndDontSave;
            var mesh=new Mesh();
            try
            {
                var bear=copy.GetComponent<PitBear>();var animator=copy.GetComponentInChildren<Animator>();
                // Serialized scene bear is still in patrol, so this exercises the actual state transition.
                Vector3 before=copy.transform.position;
                for(int i=0;i<30;i++)bear.Tick(1f/30,null,true);
                Require(Vector3.Distance(before,copy.transform.position)<.0001f,"Taunting bear stays planted");
                Require(bear.State==PitBear.BearState.Taunt && Mathf.Approximately(bear.AnimatorSpeed,0),"Taunt speed matches replicated state");
                for(int step=0;step<30;step++)bear.Tick(1f/30,null,false);Require(Vector3.Distance(before,copy.transform.position)>.01f,"Patrol resumes after taunt");
                var clips=animator.runtimeAnimatorController.animationClips.Distinct().ToArray();Require(clips.Length==6,"Six original clips");
                var skin=copy.GetComponentInChildren<SkinnedMeshRenderer>();
                var head=skin.bones.Single(b=>b.name=="Head");
                var pelvis=skin.bones.Single(b=>b.name=="Pelvis");
                foreach(var clip in clips)
                {
                    float min=float.MaxValue,max=float.MinValue;
                    for(int sample=0;sample<24;sample++)
                    {
                        clip.SampleAnimation(animator.gameObject,clip.length*sample/24);
                        if(clip.name=="Bruno_Walk" || clip.name=="Bruno_Run")
                            Require(copy.transform.InverseTransformPoint(head.position).z>copy.transform.InverseTransformPoint(pelvis.position).z,"Gait faces the movement direction (+Z)");
                        skin.BakeMesh(mesh);
                        foreach(Vector3 v in mesh.vertices)
                        {
                            Vector3 point=copy.transform.InverseTransformPoint(skin.transform.TransformPoint(v));
                            Require(!float.IsNaN(point.y) && !float.IsInfinity(point.y),"Finite skinned vertices");
                            min=Mathf.Min(min,point.y);max=Mathf.Max(max,point.y);
                        }
                    }
                    Require(max<5 && min>-.06f,"Bear motion stays inside presentation bounds: "+clip.name);
                    result.AppendLine("  "+clip.name+": "+clip.length.ToString("F2")+" s, sampled y="+min.ToString("F2")+".."+max.ToString("F2"));
                }
                result.AppendLine("  Stationary taunt, resumed patrol and 144 skinned animation samples PASS.");
            }
            finally{UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(copy);}
        }

        private static void CheckCraft(string scenePath)
        {
            Require(!AssetDatabase.GetDependencies(scenePath).Any(CircusCraftBuilder.IsBought),"No bought circus art dependencies, including hidden renderers and effects");
            Require(GameObject.Find("_UI/Canvas/StopwatchHud")==null,"No duplicate screen scoreboard");
            var arena=GameObject.Find("_Arena");
            var board=arena.GetComponentInChildren<Igruha.Core.UI.WorldScoreboard>();
            var faces=new SerializedObject(board).FindProperty("faces");
            Require(faces.arraySize==4,"Four world faces registered");
            foreach(var cage in arena.GetComponentsInChildren<CageStation>(true))
            {
                Require(cage.transform.Find("CN_CageWindow")!=null,"Original viewing window on every cage");
                Require(cage.transform.Find("Wall_1").GetComponentsInChildren<Renderer>(true).All(r=>!r.enabled),"Front bars do not obscure results");
                Require(cage.transform.Find("Wall_1").GetComponentInChildren<Collider>().enabled,"Viewing opening preserves the safety collider");
            }
            var chains=arena.GetComponentsInChildren<CageChain>(true);
            Require(chains.Length==16,"Two side chains per cage, no duplicate suspension after rebuilding");
            foreach(var chain in chains)
            {
                var data=new SerializedObject(chain);var links=data.FindProperty("links");
                Require(data.FindProperty("target").objectReferenceValue!=null,"Chain follows its cage roof");
                Require(links.arraySize*data.FindProperty("segmentLength").floatValue>=12.24f,"Chain reaches the lowest cage level");
                for(int i=0;i<links.arraySize;i++)Require(links.GetArrayElementAtIndex(i).objectReferenceValue!=null,"Every chain segment is assigned");
            }
            var effects=arena.GetComponentInChildren<CircusEffects>();var fx=new SerializedObject(effects);
            Require(fx.FindProperty("descentDust").arraySize==8,"Eight cage dust effects");
            foreach(string field in new[]{"controller","bear","landingBurst","tauntSparks","catchImpact","confetti"})
                Require(fx.FindProperty(field).objectReferenceValue!=null,"Original effect binding: "+field);
        }

        private static void Require(bool value,string message)
        {if(!value)throw new InvalidOperationException("Circus Night audit: "+message);}
    }
}
