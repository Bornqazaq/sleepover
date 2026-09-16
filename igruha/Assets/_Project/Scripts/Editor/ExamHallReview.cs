using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;
using Igruha.Minigames.Exam;

namespace Igruha.EditorTools
{
    /// <summary>Matched static captures and synchronous render comparison, without changing the saved scene.</summary>
    public static class ExamHallReview
    {
        public static string Capture(string label, string rigName = "HallCameraRig", bool benchmark = false)
        {
            var rig=GameObject.Find(rigName).transform;
            var go=new GameObject("TemporaryExamReview");var camera=go.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(rig.position,rig.rotation);camera.fieldOfView=40;
            camera.nearClipPlane=.1f;camera.farClipPlane=120;camera.aspect=16f/9f;
            go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=true;
            var board=GameObject.Find("BoardCanvas").GetComponent<ExamBoard>();
            board.ShowQuestion(1,8,1,"Кто опаснее в школьной столовой?","Повар с половником","Завуч без обеда");
            Canvas.ForceUpdateCanvases();
            var rt=RenderTexture.GetTemporary(1600,900,24,RenderTextureFormat.ARGB32);
            var old=RenderTexture.active;camera.targetTexture=rt;
            var texture=new Texture2D(1600,900,TextureFormat.RGB24,false);
            var pixel=new Texture2D(1,1,TextureFormat.RGB24,false);
            string directory=Path.GetFullPath("../docs/art/exam");Directory.CreateDirectory(directory);
            string result="";
            try
            {
                camera.Render();RenderTexture.active=rt;
                texture.ReadPixels(new Rect(0,0,1600,900),0,0);texture.Apply();
                File.WriteAllBytes(Path.Combine(directory,label+".png"),texture.EncodeToPNG());
                if(benchmark)
                {
                    double sum=0;const int warmup=12,samples=48;
                    var watch=new System.Diagnostics.Stopwatch();
                    for(int i=0;i<warmup+samples;i++)
                    {
                        watch.Restart();camera.Render();RenderTexture.active=rt;
                        // One-pixel readback synchronizes the GPU; this is a render comparison,
                        // not a claim about gameplay frame rate or input/network cost.
                        pixel.ReadPixels(new Rect(0,0,1,1),0,0,false);watch.Stop();
                        if(i>=warmup)sum+=watch.Elapsed.TotalMilliseconds;
                    }
                    result="static room, HallCameraRig FOV40, 1600x900, 12 warmup + 48 synchronized renders; mean render+readback="+(sum/samples).ToString("F2")+" ms";
                    File.WriteAllText(Path.Combine(directory,label+"-render.txt"),result);
                }
            }
            finally
            {
                camera.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(pixel);UnityEngine.Object.DestroyImmediate(go);
                board.ShowWaiting(0,0,0);
            }
            return result;
        }
    }
}
