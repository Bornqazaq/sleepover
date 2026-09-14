using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Portable Blender assets shared by Stopwatch and CansOrder.</summary>
    internal static class CircusNightAssets
    {
        internal const string Art = "Assets/_Project/Art/CircusNight";
        internal const string Materials = "Assets/_Project/Materials/Minigames/CircusNight";
        internal const string Prefabs = "Assets/_Project/Prefabs/Minigames/CircusNight";
        internal const string BearModel = Art + "/Models/CN_Bruno.fbx";
        internal const string Controller = Art + "/Bruno.controller";
        private static readonly string[] Names = { "CN_Walnut", "CN_WalnutLight", "CN_OxbloodVelvet", "CN_MidnightCanvas", "CN_AgedBrass", "CN_BlackIron", "CN_PitStone", "CN_StoneEdge", "CN_Sawdust", "CN_WarmBulb", "CN_Seams", "CN_Parchment", "CN_UmberFur", "CN_FurTips", "CN_Muzzle", "CN_Nose", "CN_AmberEyes", "CN_Claws", "CN_Mouth", "CN_DarkFur", "CN_TinPaint", "CN_TinSteel", "CN_CanvasRed", "CN_CanvasIvory", "CN_HempRope" };
        private static readonly Color[] Colors = {
            new Color(.20f,.095f,.048f), new Color(.32f,.18f,.09f),new Color(.58f,.028f,.041f),new Color(.045f,.27f,.30f),
            new Color(.47f,.30f,.11f),new Color(.045f,.059f,.069f),new Color(.29f,.27f,.23f),new Color(.39f,.35f,.27f),
            new Color(.43f,.30f,.14f),new Color(1f,.61f,.22f),new Color(.023f,.022f,.020f),new Color(.69f,.52f,.29f),
            new Color(.235f,.105f,.039f),new Color(.30f,.15f,.064f),new Color(.36f,.235f,.125f),new Color(.018f,.011f,.008f),
            new Color(.085f,.039f,.010f),new Color(.61f,.49f,.31f),new Color(.083f,.018f,.014f),new Color(.054f,.028f,.012f),Color.white,new Color(.52f,.56f,.59f),
            new Color(.72f,.045f,.062f),new Color(.92f,.83f,.65f),new Color(.52f,.37f,.19f)
        };

        internal static void Import()
        {
            Directory.CreateDirectory(Materials); Directory.CreateDirectory(Prefabs);
            var grain = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/Textures/CN_FurGrain.png");
            var normalPath=Art+"/Textures/CN_FurNormal.png";
            var normalImporter=AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if(normalImporter!=null && normalImporter.textureType!=TextureImporterType.NormalMap)
            {normalImporter.textureType=TextureImporterType.NormalMap;normalImporter.SaveAndReimport();}
            var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            var map = new Dictionary<string, Material>();
            for (int i = 0; i < Names.Length; i++)
            {
                Material m = EnsureMaterial(Names[i]);
                m.SetColor("_BaseColor", Colors[i]);
                m.SetFloat("_Metallic", i == 4 ? .72f : i == 5 ? .65f : i == 21 ? .8f : i == 20 ? .18f : 0f);
                m.SetFloat("_Smoothness", i == 4 ? .46f : i == 5 ? .32f : i == 15 || i == 16 ? .55f : .14f);
                bool fabric=i==2 || i==3 || i==22 || i==23;
                m.SetFloat("_Cull", fabric ? 0f : 2f);
                m.SetTexture("_BaseMap", i >= 12 && i <= 19 && i != 15 && i != 16 && i != 17 && i != 18 ? grain : null);
                bool coat=i==12 || i==13 || i==14 || i==19;
                m.SetTexture("_BumpMap",coat?normal:null);m.SetFloat("_BumpScale",.28f);
                if(coat && normal!=null)m.EnableKeyword("_NORMALMAP");else m.DisableKeyword("_NORMALMAP");
                string surface = i < 2 ? "CN_WoodGrain" : fabric ? "CN_Fabric" : i == 6 || i == 7 || i == 8 ? "CN_Sand" : null;
                if (surface != null) m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/Textures/"+surface+".png"));
                if (i == 9)
                {
                    // URP rebuilds keywords from GI flags during import/Play Mode validation.
                    // Leaving EmissiveIsBlack on the material silently switches these bulbs off.
                    m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
                    m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",new Color(1f,.77f,.44f) * 6f);
                }
                m.enableInstancing = true; EditorUtility.SetDirty(m); map.Add(Names[i], m);
            }
            foreach (string path in Directory.GetFiles(Art + "/Models", "*.fbx").Select(p => p.Replace('\\','/')))
            {
                bool bear = path == BearModel;
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.bakeAxisConversion = true; importer.addCollider = false;
                importer.importCameras = false; importer.importLights = false;
                importer.importAnimation = bear; importer.animationType = bear ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
                importer.isReadable = false;
                if (bear)
                {
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    importer.motionNodeName = "Root";
                    var clips = importer.defaultClipAnimations;
                    foreach (var clip in clips)
                    {
                        string[] titles = { "Idle", "Walk", "Run", "Strike", "Roar", "Alert" };
                        foreach (string title in titles) if (clip.name.Contains("Bruno_" + title)) clip.name = "Bruno_" + title;
                        clip.loopTime = clip.name == "Bruno_Idle" || clip.name == "Bruno_Walk" || clip.name == "Bruno_Run";
                        clip.lockRootRotation = true; clip.lockRootPositionXZ = true; clip.lockRootHeightY = true;
                    }
                    importer.clipAnimations = clips;
                }
                foreach (var entry in map) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.Key),entry.Value);
                importer.SaveAndReimport();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) throw new InvalidOperationException("Missing original model: " + path);
                if (bear) continue;
                var wrapper = new GameObject(Path.GetFileNameWithoutExtension(path));
                try
                {
                    PrefabUtility.InstantiatePrefab(model, wrapper.transform);
                    PrefabUtility.SaveAsPrefabAsset(wrapper, Prefabs + "/" + wrapper.name + ".prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(wrapper); }
            }
            BuildAnimator(); CircusNightProps.BuildCanPrefab(); AssetDatabase.SaveAssets();
        }

        internal static Material EnsureMaterial(string name)
        {
            string path = Materials + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is missing");
            m = new Material(shader) { name = name };AssetDatabase.CreateAsset(m,path);return m;
        }

        internal static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>(Materials + "/" + name + ".mat");

        internal static Animator BuildBear(Transform parent)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BearModel);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
            if (model == null || controller == null) throw new InvalidOperationException("Import Circus Night assets before dressing the bear.");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model,parent);go.name = "Bruno";
            // The imported bones face Unity +Z with bakeAxisConversion enabled.
            go.transform.localRotation = Quaternion.identity;
            go.transform.localPosition = Vector3.zero;go.transform.localScale = Vector3.one;
            var animator = go.GetComponent<Animator>();
            if (animator == null) animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;renderer.receiveShadows = true;
                var skin = renderer as SkinnedMeshRenderer;
                if (skin != null)
                {
                    // Includes the full authored rear-up pose, not just the imported rest pose.
                    // The skin has an FBX pre-rotation: bounds must enclose the rear-up in
                    // mesh space too, where height can be the negative Z axis.
                    skin.localBounds = new Bounds(Vector3.zero,Vector3.one*10f);
                }
            }
            return animator;
        }

        internal static void BuildAnimator()
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(BearModel).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            Func<string,AnimationClip> clip = title => clips.FirstOrDefault(c => c.name == "Bruno_" + title) ?? throw new InvalidOperationException("Missing bear action: " + title);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(Controller);
            var machine = controller.layers[0].stateMachine;
            foreach (var t in machine.anyStateTransitions) machine.RemoveAnyStateTransition(t);
            foreach (var state in machine.states) machine.RemoveState(state.state);
            for (int i = controller.parameters.Length - 1; i >= 0; i--) controller.RemoveParameter(i);
            foreach (var old in AssetDatabase.LoadAllAssetsAtPath(Controller).OfType<BlendTree>()) UnityEngine.Object.DestroyImmediate(old,true);
            controller.AddParameter("Speed",AnimatorControllerParameterType.Float);
            controller.AddParameter("Strike",AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Roar",AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Alert",AnimatorControllerParameterType.Trigger);
            var tree = new BlendTree { name="BearGait",blendType=BlendTreeType.Simple1D,blendParameter="Speed",useAutomaticThresholds=false };
            AssetDatabase.AddObjectToAsset(tree,controller);tree.AddChild(clip("Idle"),0);tree.AddChild(clip("Walk"),2.1f);tree.AddChild(clip("Run"),5.5f);
            var idle = machine.AddState("Locomotion");idle.motion = tree;machine.defaultState = idle;
            foreach (string title in new[] { "Strike", "Roar", "Alert" })
            {
                var state = machine.AddState(title);state.motion = clip(title);
                var enter = machine.AddAnyStateTransition(state);enter.AddCondition(AnimatorConditionMode.If,0,title);
                enter.hasExitTime=false;enter.duration=.10f;enter.canTransitionToSelf=false;
                var exit = state.AddTransition(idle);exit.hasExitTime=true;exit.exitTime=1f;exit.duration=.12f;
                if (title == "Roar" || title == "Alert")
                {
                    // A newly fallen player interrupts the display, so the bear can start chasing.
                    var chase = state.AddTransition(idle);chase.hasExitTime=false;chase.duration=.15f;
                    chase.AddCondition(AnimatorConditionMode.Greater,.1f,"Speed");
                }
            }
            EditorUtility.SetDirty(controller);
        }
    }
}
