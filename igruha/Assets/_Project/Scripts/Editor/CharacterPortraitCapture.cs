using System;
using System.IO;
using System.Linq;
using Igruha.Core.Player;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Bakes the existing models once; the selection UI never loads eight live models.</summary>
    public static class CharacterPortraitCapture
    {
        public const string Folder = "Assets/_Project/Art/UI/CharacterSelect/Portraits";

        [MenuItem("Igruha/Хаб/Выбор персонажа — снять портреты")]
        public static void CaptureAll()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            foreach (var character in roster.Characters)
                if (character.IsAvailable) Capture(character);
            AssetDatabase.Refresh();
            Debug.Log("Character portraits captured from the unchanged roster prefabs.");
        }

        public static void Capture(CharacterDefinition character)
        {
            Directory.CreateDirectory(Folder);
            var preview = new PreviewRenderUtility();
            try
            {
                preview.ambientColor = new Color(.57f, .57f, .57f, 1);
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = Color.clear;
                preview.camera.orthographic = true;
                preview.camera.nearClipPlane = .1f;
                preview.camera.farClipPlane = 30f;
                preview.lights[0].type = LightType.Directional;
                preview.lights[0].intensity = 1.3f;
                preview.lights[0].color = new Color(1f, .94f, .84f);
                preview.lights[0].transform.eulerAngles = new Vector3(30, 200, 0);
                preview.lights[1].type = LightType.Directional;
                preview.lights[1].intensity = .8f;
                preview.lights[1].color = new Color(.76f, .89f, 1f);
                preview.lights[1].transform.eulerAngles = new Vector3(15, 115, 0);
                var instance = Object.Instantiate(character.Prefab);
                preview.AddSingleGO(instance);
                instance.hideFlags = HideFlags.HideAndDontSave;
                foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                var animator = instance.GetComponentInChildren<Animator>();
                var idle = animator.runtimeAnimatorController.animationClips.First(c => c.name.ToLowerInvariant().Contains("idle"));
                animator.Rebind();
                idle.SampleAnimation(animator.gameObject, .25f);
                animator.enabled = false;
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, 12, 0));
                var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                Render(preview, bounds.center, Mathf.Max(bounds.extents.y, bounds.extents.x / .8f) * 1.07f,
                    800, 1000, character.DisplayName + "_Body");
                var face = bounds.center + Vector3.up * bounds.size.y * .235f;
                Render(preview, face, bounds.size.y * .255f, 512, 512, character.DisplayName + "_Face");
            }
            finally { preview.Cleanup(); }
        }

        private static void Render(PreviewRenderUtility preview, Vector3 centre, float size, int width, int height, string name)
        {
            preview.camera.orthographicSize = size;
            preview.camera.transform.position = centre + Vector3.forward * 7;
            preview.camera.transform.LookAt(centre);
            preview.BeginPreview(new Rect(0, 0, width, height), GUIStyle.none);
            preview.Render(true, false);
            var rendered = preview.EndPreview() as RenderTexture;
            var previous = RenderTexture.active;
            // PreviewRenderUtility renders at editor DPI (2x on Retina). Resolve the full frame.
            var resolved = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(rendered, resolved);
                RenderTexture.active = resolved;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Folder + "/" + name + ".png", image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(resolved); Object.DestroyImmediate(image); }
        }

        public static Sprite Load(string name, string kind)
        {
            var path = Folder + "/" + name + "_" + kind + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Capture portraits first: " + path);
            if (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.maxTextureSize = 1024;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
