#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Igruha.Minigames.Exam
{
    /// <summary>Explicit isolated Play Mode art recording. Never runs in a player build.</summary>
    public sealed class ExamHatchPreview : MonoBehaviour
    {
        public void Record(string directory) { StartCoroutine(Capture(directory)); }
        private IEnumerator Capture(string directory)
        {
            Directory.CreateDirectory(directory);
            var platform=GameObject.Find("Platform_A").GetComponent<ExamAnswerPlatform>();
            var go=new GameObject("MechanismDetailCamera");var camera=go.AddComponent<Camera>();
            camera.transform.position=new Vector3(-.4f,2.7f,-2.8f);
            camera.transform.rotation=Quaternion.LookRotation(new Vector3(-3.6f,-.7f,2.88f)-camera.transform.position);
            camera.fieldOfView=57;camera.nearClipPlane=.08f;camera.farClipPlane=100;camera.aspect=16f/9f;
            var frames=new List<Texture2D>();var rt=RenderTexture.GetTemporary(1280,720,24,RenderTextureFormat.ARGB32);
            int oldRate=Time.captureFramerate;Time.captureFramerate=30;
            platform.CloseDoors();
            try
            {
                for(int i=0;i<60;i++)
                {
                    if(i==9)platform.OpenDoors(.5f);
                    if(i==48)platform.CloseDoors();
                    yield return new WaitForEndOfFrame();
                    var previous=RenderTexture.active;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                    var frame=new Texture2D(1280,720,TextureFormat.RGB24,false);
                    frame.ReadPixels(new Rect(0,0,1280,720),0,0);frame.Apply();frames.Add(frame);RenderTexture.active=previous;
                }
            }
            finally
            {
                Time.captureFramerate=oldRate;camera.targetTexture=null;RenderTexture.ReleaseTemporary(rt);Destroy(go);
            }
            for(int i=0;i<frames.Count;i++) { File.WriteAllBytes(Path.Combine(directory,"frame-"+i.ToString("D3")+".png"),frames[i].EncodeToPNG());Destroy(frames[i]); }
            Debug.Log("EXAM_HATCH_PREVIEW_COMPLETE frames="+frames.Count+" isolated Play Mode, 30 fps, actual OpenDoors/CloseDoors, detail camera");
            Destroy(gameObject);
        }
    }
}
#endif
