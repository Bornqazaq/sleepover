using System;
using System.IO;
using System.Linq;
using System.Text;
using Igruha.Core.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Same roster, idle frame and portrait framing under the active scene's actual lights and grade.</summary>
    public static class CircusCharacterLightingReview
    {
        private const int ReviewLayer=31;
        private const int Width=480;
        private const int Height=640;
        private const int Columns=4;
        private const string RosterPath="Assets/_Project/Settings/Gameplay/CharacterRoster.asset";

        [MenuItem("Igruha/Цирк/Естественные цвета персонажей — обе сцены")]
        public static void ApplyBoth()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and stop Play first.");
            string opened=EditorSceneManager.GetActiveScene().path;
            try
            {
                foreach(string name in new[]{"Stopwatch","CansOrder"})
                {
                    var scene=EditorSceneManager.OpenScene("Assets/_Project/Scenes/Minigames/"+name+".unity");
                    CircusNightBuilder.ApplyNaturalLighting();
                    EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
                }
                AssetDatabase.SaveAssets();
            }
            finally { if(!string.IsNullOrEmpty(opened)) EditorSceneManager.OpenScene(opened); }
        }

        /// <summary>
        /// Read-only scene review. Example: Capture("/tmp/circus-before", new Vector3(0,8.69f,7.2f)).
        /// Run again at the same position after ApplyBoth; run in Hub or MemoryRun for the usual appearance.
        /// Nothing is saved to the scene or to the roster. The camera renders only temporary review models,
        /// but uses the scene's real lights, ambient, reflections and post-processing.
        /// </summary>
        public static void Capture(string folder,Vector3 standingPosition,bool validateLightLayers=false)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before the character lighting review.");
            Directory.CreateDirectory(folder);
            var roster=AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
            var characters=roster.Characters.Where(c=>c.IsAvailable).ToArray();
            int rows=Mathf.CeilToInt((float)characters.Length/Columns);
            var sheet=new Texture2D(Width*Columns,Height*rows,TextureFormat.RGB24,false);
            var cameraObject=new GameObject("TemporaryCircusColourReview"){hideFlags=HideFlags.HideAndDontSave};
            var camera=cameraObject.AddComponent<Camera>();
            camera.enabled=false;camera.cullingMask=1<<ReviewLayer;
            camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=40f;
            camera.aspect=(float)Width/Height;camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.075f,.075f,.075f);camera.allowHDR=true;
            var data=cameraObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing=true;
            data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality=AntialiasingQuality.High;
            var report=new StringBuilder();
            report.AppendLine("Scene: "+EditorSceneManager.GetActiveScene().name);
            report.AppendLine("Standing position: "+standingPosition);
            report.AppendLine("Pose: each unchanged roster idle at 0.25 s; facing 180 degrees; frontal orthographic camera.");
            report.AppendLine("Contact sheet order: left to right, top to bottom.");
            try
            {
                for(int i=0;i<characters.Length;i++)
                {
                    var character=characters[i];
                    var avatar=Object.Instantiate(character.Prefab);
                    try
                    {
                        avatar.hideFlags=HideFlags.HideAndDontSave;
                        foreach(var behaviour in avatar.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled=false;
                        foreach(var node in avatar.GetComponentsInChildren<Transform>(true)) node.gameObject.layer=ReviewLayer;
                        var animator=avatar.GetComponentInChildren<Animator>();
                        var idle=animator.runtimeAnimatorController.animationClips.First(c=>c.name.ToLowerInvariant().Contains("idle"));
                        animator.Rebind();idle.SampleAnimation(animator.gameObject,.25f);animator.enabled=false;
                        avatar.transform.SetPositionAndRotation(standingPosition,Quaternion.Euler(0,180,0));
                        var renderers=avatar.GetComponentsInChildren<SkinnedMeshRenderer>();
                        Bounds bounds=renderers[0].bounds;
                        foreach(var renderer in renderers)
                        {
                            bounds.Encapsulate(renderer.bounds);
                            if(renderer.renderingLayerMask!=1u)
                                throw new InvalidOperationException(character.DisplayName+" has an unexpected rendering layer; do not change the roster to pass the review.");
                            foreach(var material in renderer.sharedMaterials)
                            {
                                if(material==null) throw new InvalidOperationException(character.DisplayName+" has a missing material.");
                                report.AppendLine(character.DisplayName+" | "+AssetDatabase.GetAssetPath(material)+" | "+material.name+" | "+material.shader.name);
                            }
                        }
                        camera.orthographicSize=Mathf.Max(bounds.extents.y,bounds.extents.x/camera.aspect)*1.08f;
                        camera.transform.position=bounds.center+Vector3.back*7f;
                        camera.transform.LookAt(bounds.center);
                        Physics.SyncTransforms();
                        var image=Render(camera);
                        try
                        {
                            File.WriteAllBytes(Path.Combine(folder,character.DisplayName+".png"),image.EncodeToPNG());
                            sheet.SetPixels((i%Columns)*Width,(rows-1-i/Columns)*Height,Width,Height,image.GetPixels());
                            if(validateLightLayers) ValidateLayerIsolation(camera,bounds,image,character.DisplayName,report);
                        }
                        finally { Object.DestroyImmediate(image); }
                    }
                    finally { Object.DestroyImmediate(avatar); }
                }
                sheet.Apply();File.WriteAllBytes(Path.Combine(folder,"roster.png"),sheet.EncodeToPNG());
                File.WriteAllText(Path.Combine(folder,"materials.txt"),report.ToString());
                Debug.Log("Circus character colour review: "+characters.Length+" unchanged roster models captured to "+folder);
            }
            finally { Object.DestroyImmediate(sheet);Object.DestroyImmediate(cameraObject); }
        }

        private static void ValidateLayerIsolation(Camera camera,Bounds bounds,Texture2D baseline,string name,StringBuilder report)
        {
            var probeObject=new GameObject("TemporaryMagentaLayerProof"){hideFlags=HideFlags.HideAndDontSave};
            try
            {
                var light=probeObject.AddComponent<Light>();
                light.type=LightType.Point;light.color=new Color(1,0,1);light.intensity=20;light.range=10;
                light.shadows=UnityEngine.LightShadows.None;
                light.transform.position=bounds.center+Vector3.back*1.5f;
                var data=probeObject.AddComponent<UniversalAdditionalLightData>();
                data.renderingLayers=2u;
                var excluded=Render(camera);
                float excludedDifference;
                try { excludedDifference=MeanPixelDifference(baseline,excluded); }
                finally { Object.DestroyImmediate(excluded); }
                data.renderingLayers=1u;
                var included=Render(camera);
                float includedDifference;
                try { includedDifference=MeanPixelDifference(baseline,included); }
                finally { Object.DestroyImmediate(included); }
                report.AppendLine(name+" | magenta light excluded-layer mean RGB delta="+excludedDifference.ToString("F6")
                    +"; included-layer delta="+includedDifference.ToString("F6"));
                if(excludedDifference>.002f || includedDifference<.005f)
                    throw new InvalidOperationException(name+": light-layer isolation proof failed. Excluded="+excludedDifference+", included="+includedDifference);
            }
            finally { Object.DestroyImmediate(probeObject); }
        }

        private static float MeanPixelDifference(Texture2D first,Texture2D second)
        {
            var a=first.GetPixels32();var b=second.GetPixels32();double total=0;
            for(int i=0;i<a.Length;i++) total+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);
            return (float)(total/(a.Length*3.0*255.0));
        }

        private static Texture2D Render(Camera camera)
        {
            var previous=RenderTexture.active;
            var target=RenderTexture.GetTemporary(Width,Height,24,RenderTextureFormat.ARGB32);
            var image=new Texture2D(Width,Height,TextureFormat.RGB24,false);
            camera.targetTexture=target;
            try
            {
                // The first render updates the volume stack and colour-grading LUT.
                camera.Render();camera.Render();RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,Width,Height),0,0);image.Apply();return image;
            }
            catch { Object.DestroyImmediate(image);throw; }
            finally { camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target); }
        }
    }
}
