using System.IO;
using Igruha.Core.Hub;
using Igruha.Core.Minigame;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Igruha.EditorTools
{
    public static class OneBulletCoverBuilder
    {
        private const string Path="Assets/_Project/Art/Hub/Console/Covers/OneBullet.png";
        [MenuItem("Igruha/Art/Capture One Bullet cover")]
        public static void Build()
        {
            EditorSceneManager.OpenScene(OneBulletArenaBuilder.ScenePath);
            var go=new GameObject("TemporaryCoverCamera");var camera=go.AddComponent<Camera>();
            camera.transform.position=new Vector3(-11.1f,2.05f,-11.6f);camera.transform.LookAt(new Vector3(-8.8f,1.8f,-8.8f));camera.fieldOfView=68;camera.nearClipPlane=.03f;camera.farClipPlane=100;
            var data=go.AddComponent<UniversalAdditionalCameraData>();data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var rt=RenderTexture.GetTemporary(1672,941,24);var previous=RenderTexture.active;var texture=new Texture2D(1672,941,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1672,941),0,0);texture.Apply();File.WriteAllBytes(Path,texture.EncodeToPNG());
            }
            finally
            {camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(texture);Object.DestroyImmediate(go);}
            AssetDatabase.ImportAsset(Path,ImportAssetOptions.ForceUpdate);
            var importer=(TextureImporter)AssetImporter.GetAtPath(Path);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.maxTextureSize=2048;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
            var library=AssetDatabase.LoadAssetAtPath<ConsoleArtworkLibrary>("Assets/_Project/Art/Hub/Console/ConsoleArtwork.asset");
            var game=AssetDatabase.LoadAssetAtPath<MinigameDefinition>("Assets/_Project/Settings/Gameplay/Minigames/OneBullet.asset");
            var so=new SerializedObject(library);var entries=so.FindProperty("entries");int index=-1;
            for(int i=0;i<entries.arraySize;i++)if(entries.GetArrayElementAtIndex(i).FindPropertyRelative("game").objectReferenceValue==game)index=i;
            if(index<0){index=entries.arraySize;entries.arraySize++;}var entry=entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("game").objectReferenceValue=game;entry.FindPropertyRelative("cover").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Sprite>(Path);
            entry.FindPropertyRelative("accent").colorValue=new Color(.937f,.757f,.545f);entry.FindPropertyRelative("genre").stringValue="ОДИН ВЫСТРЕЛ — ОДИН ШАНС";
            entry.FindPropertyRelative("summary").stringValue="Затеряйся в каменном лабиринте. Найди единственный револьвер, слушай шаги и реши, когда потратить последний патрон.";
            so.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();
        }
    }
}
