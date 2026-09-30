using UnityEditor;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.EditorTools
{
    public static class MosquitoesArtReview
    {
        public static void Room()
        {
            var go = new GameObject("MosquitoesReviewCamera");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.transform.position = new Vector3(.95f, 2.3f, -3.02f);
                camera.transform.LookAt(new Vector3(0, 1.35f, .65f));
                camera.fieldOfView = 73; camera.nearClipPlane = .03f;
                Capture(camera, "compact-room", 1600, 1200);
            }
            finally { Object.DestroyImmediate(go); }
        }
        public static void Characters(bool back = false)
        {
            var root = new GameObject("MosquitoesCharacterReview");
            try
            {
                var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
                for (int i = 0; i < roster.Characters.Count; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MosquitoCharacterBuilder.Folder + "/" + roster.Characters[i].DisplayName + ".prefab");
                    var instance = Object.Instantiate(prefab, root.transform);
                    instance.transform.position = new Vector3((i % 4) * .42f, 5f - (i / 4) * .40f, 0);
                    instance.transform.rotation = Quaternion.Euler(0, back ? 155 : -15, 0);
                }
                var light = new GameObject("ReviewLight").AddComponent<Light>();
                light.transform.SetParent(root.transform); light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(35, 160, 0); light.intensity = 2;
                var camera = new GameObject("ReviewCamera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform); camera.transform.position = new Vector3(.63f, 4.88f, 1.7f);
                camera.transform.LookAt(new Vector3(.63f, 4.8f, 0)); camera.orthographic = true; camera.orthographicSize = .48f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.11f, .14f, .20f);
                Capture(camera, back ? "roster-back" : "roster-front", 1500, 850);
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static void Capture(Camera camera, string name, int width, int height)
        {
            var target = new RenderTexture(width, height, 24);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                System.IO.Directory.CreateDirectory("Captures/Mosquitoes");
                System.IO.File.WriteAllBytes("Captures/Mosquitoes/" + name + ".png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                Object.DestroyImmediate(target); Object.DestroyImmediate(image);
            }
        }
    }
}
