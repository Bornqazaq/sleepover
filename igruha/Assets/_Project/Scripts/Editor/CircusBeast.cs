using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Igruha.Minigames.Circus;

namespace Igruha.EditorTools
{
    /// <summary>Original Blender brown bear; both scene dressing paths use the same asset.</summary>
    internal static class CircusBeast
    {
        internal static Animator Build(Transform visual, float height)
        {
            // Authored in metres: scaling a quadruped to the old standing man's height distorts reach.
            return CircusNightAssets.BuildBear(visual);
        }

        [MenuItem("Igruha/Цирк/Переодеть зверя в яме")]
        internal static void RedressInScene()
        {
            var bear=Object.FindFirstObjectByType<PitBear>(FindObjectsInactive.Include);
            if(bear==null)throw new System.InvalidOperationException("Open Stopwatch or CansOrder.");
            Transform visual=bear.VisualRoot;
            if(visual==null){visual=new GameObject("Visual").transform;visual.SetParent(bear.transform,false);}
            // Validate the replacement before touching the existing character.
            var replacement=new GameObject("BrunoReplacement").transform;replacement.SetParent(bear.transform,false);
            Animator animator;
            try{animator=Build(replacement,0);}
            catch{Object.DestroyImmediate(replacement.gameObject);throw;}
            for(int i=visual.childCount-1;i>=0;i--)Object.DestroyImmediate(visual.GetChild(i).gameObject);
            animator.transform.SetParent(visual,false);Object.DestroyImmediate(replacement.gameObject);
            var serialized=new SerializedObject(bear);
            serialized.FindProperty("visualRoot").objectReferenceValue=visual;
            serialized.FindProperty("animator").objectReferenceValue=animator;
            serialized.FindProperty("turnSpeed").floatValue=150f;
            serialized.FindProperty("acceleration").floatValue=6f;
            serialized.FindProperty("attackContactTime").floatValue=PitBear.ContactSeconds;
            serialized.FindProperty("attackDuration").floatValue=PitBear.StrikeSeconds;
            serialized.FindProperty("attackRecovery").floatValue=.3f;
            serialized.FindProperty("alertParameter").stringValue="Alert";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            CircusBearPolishBuilder.Dress(bear,animator);
            EditorSceneManager.MarkSceneDirty(bear.gameObject.scene);
        }
    }
}
