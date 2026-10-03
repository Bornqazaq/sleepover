using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    internal static class CarryCloudSky
    {
        internal static void Apply(Transform arena)
        {
            var horizon=arena.Find("Environment/Horizon");
            foreach(string name in new[]{"Clouds_Low","Clouds_High"})
            {var old=horizon.Find(name);if(old!=null)Object.DestroyImmediate(old.gameObject);}
            const string texturePath=CarrySiteFinish.Art+"/Textures/ConstructionClouds.png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.mipmapEnabled=true;
            importer.wrapModeU=TextureWrapMode.Repeat;importer.wrapModeV=TextureWrapMode.Clamp;
            importer.maxTextureSize=4096;importer.textureCompression=TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            const string skyPath=CarrySiteFinish.Art+"/Materials/CloudSky.mat";
            var shader=Shader.Find("Igruha/CarryItem/ConstructionSky");
            if(shader==null)throw new System.InvalidOperationException("ConstructionSky shader did not import");
            var sky=AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if(sky==null){sky=new Material(shader);AssetDatabase.CreateAsset(sky,skyPath);}else sky.shader=shader;
            sky.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            sky.SetFloat("_Exposure",1.1f);sky.SetFloat("_Rotation",118);
            EditorUtility.SetDirty(sky);AssetDatabase.SaveAssetIfDirty(sky);RenderSettings.skybox=sky;
            CarryCloudSea.Apply(arena);
        }
    }
}
