using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Igruha.Core.CameraSystems;
using Igruha.Core.Player;
using Igruha.Core.UI;
using Igruha.Minigames.Circus;
using Igruha.Minigames.Stopwatch;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Extracts each avatar's own arm skin; original models/controllers/prefabs are read-only.</summary>
    public static class StopwatchButtonHandsBuilder
    {
        private const string Art = "Assets/_Project/Art/StopwatchHands";
        private const string Prefabs = "Assets/_Project/Prefabs/Minigames/Stopwatch/Hands";
        private const string LibraryPath = "Assets/_Project/Settings/Gameplay/Minigames/StopwatchHandLibrary.asset";

        [MenuItem("Igruha/Секундомер/Руки восьми персонажей и крупный план кнопки")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the open scene and leave Play Mode first.");
            Directory.CreateDirectory(Art); Directory.CreateDirectory(Prefabs); AssetDatabase.Refresh();
            var roster=AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            var entries=new List<StopwatchHandLibrary.Entry>();
            foreach(var character in roster.Characters)
            {
                if(!character.IsAvailable) throw new InvalidOperationException("Missing character " + character.DisplayName);
                var hand=BuildHand(character.Prefab);
                entries.Add(new StopwatchHandLibrary.Entry {character=character.DisplayName,
                    sourceMesh=character.Prefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh,hand=hand});
            }
            var library=AssetDatabase.LoadAssetAtPath<StopwatchHandLibrary>(LibraryPath);
            if(library==null){library=ScriptableObject.CreateInstance<StopwatchHandLibrary>();AssetDatabase.CreateAsset(library,LibraryPath);}
            var serialized=new SerializedObject(library);var array=serialized.FindProperty("entries");array.arraySize=entries.Count;
            for(int i=0;i<entries.Count;i++)
            {
                var entry=array.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("character").stringValue=entries[i].character;
                entry.FindPropertyRelative("sourceMesh").objectReferenceValue=entries[i].sourceMesh;
                entry.FindPropertyRelative("hand").objectReferenceValue=entries[i].hand;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            string original=EditorSceneManager.GetActiveScene().path;
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Minigames/Stopwatch.unity");
            ApplyActive();EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());AssetDatabase.SaveAssets();
            if(!string.IsNullOrEmpty(original))EditorSceneManager.OpenScene(original);
            Debug.Log("STOPWATCH_HANDS_BUILT " + entries.Count + " original avatar arm skins");
        }

        internal static void ApplyActive()
        {
            if(EditorSceneManager.GetActiveScene().name!="Stopwatch")return;
            var library=AssetDatabase.LoadAssetAtPath<StopwatchHandLibrary>(LibraryPath);
            if(library==null)return;
            var game=Object.FindFirstObjectByType<StopwatchMinigame>();
            var cameras=Object.FindFirstObjectByType<MinigameCameraController>();
            var cameraData=new SerializedObject(cameras);
            var view=cameraData.FindProperty("fixedRig").objectReferenceValue as CinemachineCamera;
            if(view==null)
            {
                var go=new GameObject("StopwatchButtonCamera");go.transform.SetParent(cameras.transform,false);
                view=go.AddComponent<CinemachineCamera>();
                var lens=view.Lens;lens.FieldOfView=60;lens.NearClipPlane=.05f;view.Lens=lens;
                cameraData.FindProperty("fixedRig").objectReferenceValue=view;
                cameraData.ApplyModifiedPropertiesWithoutUndo();go.SetActive(false);
            }
            var presentation=game.GetComponent<StopwatchButtonPresentation>() ?? game.gameObject.AddComponent<StopwatchButtonPresentation>();
            var so=new SerializedObject(presentation);
            Set(so,"game",game);Set(so,"cameras",cameras);Set(so,"view",view);Set(so,"library",library);
            Set(so,"spectator",Object.FindFirstObjectByType<SpectatorCamera>(FindObjectsInactive.Include));
            Set(so,"attack",Object.FindFirstObjectByType<CircusAttackPresentation>(FindObjectsInactive.Include));
            so.ApplyModifiedPropertiesWithoutUndo();
            var canvas=GameObject.Find("_UI/Canvas").transform;
            var old=canvas.Find("_StopwatchButtonHud");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var root=CircusUiLayout.Rect(canvas,"_StopwatchButtonHud",0,0,0,0);
            root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;root.offsetMin=root.offsetMax=Vector2.zero;
            var hud=root.gameObject.AddComponent<StopwatchButtonHud>();hud.Construct(root,UiFonts.SansMedium);
            var local=Object.FindFirstObjectByType<StopwatchLocalHud>(FindObjectsInactive.Include);
            var hudData=new SerializedObject(local);Set(hudData,"buttonHud",hud);hudData.ApplyModifiedPropertiesWithoutUndo();
            canvas.Find("_HudOverlay")?.SetAsLastSibling();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        private static StopwatchHandRig BuildHand(GameObject player)
        {
            var instance=Object.Instantiate(player);instance.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            var root=new GameObject(player.name+"_ButtonHand");
            try
            {
                var source=instance.GetComponentInChildren<Animator>(true);
                if(source==null || !source.isHuman)throw new InvalidOperationException("Humanoid required: " + player.name);
                var model=Object.Instantiate(source.gameObject,root.transform);
                model.name="ArmSkin";model.transform.localPosition=source.transform.localPosition;
                model.transform.localRotation=source.transform.localRotation;model.transform.localScale=source.transform.lossyScale;
                var animator=model.GetComponent<Animator>();
                PoseFingers(animator);
                var upper=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                var wrist=animator.GetBoneTransform(HumanBodyBones.RightHand);
                var distal=animator.GetBoneTransform(HumanBodyBones.RightIndexDistal);
                var elbow=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                // The source avatars have different authored idle arm bends. The
                // isolated first-person arm reaches forward consistently for each skin.
                upper.rotation=Quaternion.FromToRotation(elbow.position-upper.position,Vector3.forward)*upper.rotation;
                elbow.rotation=Quaternion.FromToRotation(wrist.position-elbow.position,new Vector3(0,.08f,1))*elbow.rotation;
                wrist.rotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);
                // Extend only the off-camera upper-arm end. The hand and forearm
                // keep their real dimensions; no open shoulder seam enters a wide shot.
                Vector3 elbowPosition=elbow.position;
                upper.position-=Vector3.forward*.8f;
                elbow.position=elbowPosition;
                var skin=model.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var mesh=ExtractArm(skin,upper,player.name);
                string meshPath=Art+"/"+player.name+"_RightArm.asset";
                var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if(existing==null){AssetDatabase.CreateAsset(mesh,meshPath);existing=mesh;}
                else{EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);}
                skin.sharedMesh=existing;skin.updateWhenOffscreen=true;
                // These Mixamo rigs deliberately merge several fingers. Their wrist's
                // Y axis follows the fingers and -Z is the back of the right hand;
                // neither the optional middle nor little finger is required.
                Vector3 forward=wrist.up;
                Vector3 up=-wrist.forward;
                Vector3 pad=FindFingerPad(skin,distal,up);
                var contact=new GameObject("IndexFingerPad").transform;
                contact.SetParent(distal,false);contact.position=pad;
                Quaternion basis=Quaternion.LookRotation(forward,up);
                model.transform.rotation=Quaternion.Inverse(basis)*model.transform.rotation;
                model.transform.position-=wrist.position;
                Object.DestroyImmediate(animator);
                foreach(var component in model.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(component);
                foreach(var collider in model.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
                foreach(var body in model.GetComponentsInChildren<Rigidbody>(true))Object.DestroyImmediate(body);
                foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
                    if(renderer!=skin)Object.DestroyImmediate(renderer);
                skin.renderingLayerMask=1;skin.shadowCastingMode=ShadowCastingMode.On;
                var rig=root.AddComponent<StopwatchHandRig>();
                var data=new SerializedObject(rig);Set(data,"fingerContact",contact);data.ApplyModifiedPropertiesWithoutUndo();
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,Prefabs+"/"+player.name+"_ButtonHand.prefab");
                Debug.Log("STOPWATCH_HAND " + player.name + " vertices=" + existing.vertexCount + " indexPad=" + root.transform.InverseTransformPoint(contact.position));
                return prefab.GetComponent<StopwatchHandRig>();
            }
            finally{Object.DestroyImmediate(instance);Object.DestroyImmediate(root);}
        }

        private static void PoseFingers(Animator animator)
        {
            var all=animator.GetComponentsInChildren<Transform>(true);
            var positions=all.Select(t=>t.localPosition).ToArray();var rotations=all.Select(t=>t.localRotation).ToArray();
            var wrist=animator.GetBoneTransform(HumanBodyBones.RightHand);
            var fingerRotations=new Dictionary<Transform,Quaternion>();
            using(var handler=new HumanPoseHandler(animator.avatar,animator.transform))
            {
                var pose=new HumanPose();handler.GetHumanPose(ref pose);
                for(int i=0;i<HumanTrait.MuscleName.Length;i++)
                {
                    string name=HumanTrait.MuscleName[i];if(!name.StartsWith("Right "))continue;
                    if(name.EndsWith(" Stretched"))pose.muscles[i]=name.Contains("Index")?1f:name.Contains("Thumb")?-.3f:-1f;
                    else if(name.EndsWith(" Spread"))pose.muscles[i]=0;
                }
                handler.SetHumanPose(ref pose);
                foreach(var bone in all)if(bone!=wrist && bone.IsChildOf(wrist))fingerRotations[bone]=bone.localRotation;
            }
            for(int i=0;i<all.Length;i++){all[i].localPosition=positions[i];all[i].localRotation=rotations[i];}
            foreach(var pair in fingerRotations)pair.Key.localRotation=pair.Value;
        }

        private static Mesh ExtractArm(SkinnedMeshRenderer skin,Transform upper,string name)
        {
            var source=skin.sharedMesh;var weights=source.boneWeights;
            var isArm=skin.bones.Select(b=>b!=null && (b==upper || b.IsChildOf(upper))).ToArray();
            var selected=new bool[source.vertexCount];
            for(int i=0;i<selected.Length;i++)
            {
                var w=weights[i];float amount=(isArm[w.boneIndex0]?w.weight0:0)+(isArm[w.boneIndex1]?w.weight1:0)
                    +(isArm[w.boneIndex2]?w.weight2:0)+(isArm[w.boneIndex3]?w.weight3:0);
                selected[i]=amount>.55f;
            }
            var remap=new Dictionary<int,int>();var indices=new List<int>();var submeshes=new List<int[]>();
            for(int sub=0;sub<source.subMeshCount;sub++)
            {
                var triangles=source.GetTriangles(sub);var kept=new List<int>();
                for(int i=0;i<triangles.Length;i+=3)
                {
                    if(!selected[triangles[i]] || !selected[triangles[i+1]] || !selected[triangles[i+2]])continue;
                    for(int n=0;n<3;n++)
                    {
                        int original=triangles[i+n];
                        if(!remap.TryGetValue(original,out int mapped)){mapped=indices.Count;remap.Add(original,mapped);indices.Add(original);}
                        kept.Add(mapped);
                    }
                }
                submeshes.Add(kept.ToArray());
            }
            var vertices=source.vertices;var normals=source.normals;var tangents=source.tangents;var uv=source.uv;
            var result=new Mesh{name=name+" original right arm",indexFormat=IndexFormat.UInt32};
            result.vertices=indices.Select(i=>vertices[i]).ToArray();result.normals=indices.Select(i=>normals[i]).ToArray();
            if(tangents.Length==vertices.Length)result.tangents=indices.Select(i=>tangents[i]).ToArray();
            if(uv.Length==vertices.Length)result.uv=indices.Select(i=>uv[i]).ToArray();
            result.boneWeights=indices.Select(i=>weights[i]).ToArray();result.bindposes=source.bindposes;
            result.subMeshCount=submeshes.Count;
            for(int i=0;i<submeshes.Count;i++)result.SetTriangles(submeshes[i],i);
            result.RecalculateBounds();return result;
        }

        private static Vector3 FindFingerPad(SkinnedMeshRenderer skin,Transform distal,Vector3 up)
        {
            // Include the renderer's imported scale compensation so TransformPoint
            // agrees with the rendered skin (the source FBXs use centimetres).
            var baked=new Mesh();skin.BakeMesh(baked,true);
            try
            {
                Vector3 forward=distal.childCount>0?(distal.GetChild(0).position-distal.position).normalized:distal.forward;
                var vertices=baked.vertices;var weights=skin.sharedMesh.boneWeights;
                var finger=skin.bones.Select(b=>b!=null && (b==distal || b.IsChildOf(distal))).ToArray();
                var candidates=new List<Vector3>();float end=float.NegativeInfinity;
                for(int i=0;i<vertices.Length;i++)
                {
                    var w=weights[i];float influence=(finger[w.boneIndex0]?w.weight0:0)+(finger[w.boneIndex1]?w.weight1:0)
                        +(finger[w.boneIndex2]?w.weight2:0)+(finger[w.boneIndex3]?w.weight3:0);
                    if(influence<.4f)continue;
                    Vector3 world=skin.transform.TransformPoint(vertices[i]);candidates.Add(world);
                    end=Mathf.Max(end,Vector3.Dot(world-distal.position,forward));
                }
                if(candidates.Count==0)throw new InvalidOperationException("Index finger skin could not be measured.");
                return candidates.Where(v=>Vector3.Dot(v-distal.position,forward)>end-.018f).OrderBy(v=>Vector3.Dot(v,up)).First();
            }
            finally{Object.DestroyImmediate(baked);}
        }

        private static void Set(SerializedObject data,string name,Object value)=>data.FindProperty(name).objectReferenceValue=value;
    }
}
