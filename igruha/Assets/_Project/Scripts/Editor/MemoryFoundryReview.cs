using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Igruha.EditorTools
{
    public static class MemoryFoundryReview
    {
        public static string Benchmark(string label)
        {
            const int warmup = 12, samples = 36;
            var go = new GameObject("TemporaryFoundryBenchmark"); var camera = go.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 3, -25); camera.transform.LookAt(new Vector3(0, 1, 10));
            camera.fieldOfView = 55; camera.nearClipPlane = .1f; camera.farClipPlane = 180; camera.aspect = 16f / 9;
            go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            var rt = RenderTexture.GetTemporary(1600, 900, 24, RenderTextureFormat.ARGB32);
            var pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
            var old = RenderTexture.active; camera.targetTexture = rt;
            double sum = 0;
            var watch = new System.Diagnostics.Stopwatch();
            try
            {
                for (int i = 0; i < warmup + samples; i++)
                {
                    watch.Restart(); camera.Render(); RenderTexture.active = rt;
                    pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false); watch.Stop();
                    if (i >= warmup) sum += watch.Elapsed.TotalMilliseconds;
                }
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(pixel); Object.DestroyImmediate(go);
            }
            string result = "Static environment, no players, 1600x900; position=(0,3,-25), target=(0,1,10), FOV55; "
                + warmup + " warmup, " + samples + " synchronized render+readback samples; meanMs=" + (sum / samples).ToString("F2");
            System.IO.File.WriteAllText("../docs/art/memory-run/" + label + "-render.txt", result);
            return result;
        }

        public static string Capture(string label, Vector3 position, Vector3 target, bool actualCamera = false)
        {
            var go = new GameObject("TemporaryFoundryReview");
            var camera = go.AddComponent<Camera>();
            camera.transform.position = position; camera.transform.LookAt(target);
            camera.fieldOfView = 55; camera.nearClipPlane = .1f; camera.farClipPlane = 180;
            var data = go.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            if (actualCamera)
            {
                camera.transform.SetPositionAndRotation(Camera.main.transform.position, Camera.main.transform.rotation);
                camera.fieldOfView = Camera.main.fieldOfView;
            }
            var texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            var rt = RenderTexture.GetTemporary(1600, 900, 24, RenderTextureFormat.ARGB32);
            var old = RenderTexture.active; camera.targetTexture = rt; camera.aspect = 16f / 9;
            string path = Path.GetFullPath("../docs/art/memory-run/" + label + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                camera.Render(); RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = old;
                RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(texture); Object.DestroyImmediate(go);
            }
            return path;
        }
    }
}
