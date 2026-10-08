using System.IO;
using System.Linq;
using Igruha.Core.Player;
using Igruha.Minigames.CansOrder;
using Igruha.Minigames.Circus;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>UI layout review with synthetic public results. Never changes or saves the open scene.</summary>
    public static class CircusInterfaceReview
    {
        public static void Capture(string path,int players)
        {
            var root=new GameObject("Temporary circus UI review");
            var target=new RenderTexture(1800,720,24);
            var pixels=new Texture2D(1800,720,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try
            {
                var canvas=new GameObject("Canvas",typeof(RectTransform),typeof(Canvas));canvas.transform.SetParent(root.transform);
                canvas.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
                ((RectTransform)canvas.transform).sizeDelta=new Vector2(576,216);canvas.transform.localScale=Vector3.one*.01f;
                var cameraObject=new GameObject("Review camera");cameraObject.transform.SetParent(root.transform);
                var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.transform.position=new Vector3(0,0,-10);
                camera.orthographic=true;camera.orthographicSize=1.25f;camera.aspect=2.5f;camera.cullingMask=1<<31;
                camera.backgroundColor=new Color(.045f,.015f,.024f);camera.clearFlags=CameraClearFlags.SolidColor;camera.targetTexture=target;
                var roster=AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
                var portraits=roster.Characters.Select(c=>AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/UI/CharacterSelect/Portraits/"+c.DisplayName+"_Face.png")).ToArray();
                var palette=AssetDatabase.LoadAssetAtPath<CansOrderConfig>("Assets/_Project/Settings/Gameplay/Minigames/CansOrderConfig.asset");
                var view=canvas.AddComponent<CircusBoardView>();view.Construct(new[]{(RectTransform)canvas.transform},TMP_Settings.defaultFontAsset,portraits,palette);
                view.Header("РАУНД 2 · КРУГ 3","ЛУЧШИЕ ПОПЫТКИ · СОВПАДЕНИЯ / БАНКИ");view.Begin(players,Mathf.Min(players,3));
                var samples=new[]{new[]{0,4,3,2,1},new[]{1,0,4,2,3},new[]{4,3,2,1,0}};
                for(int i=0;i<players;i++)view.CansRow(i,i,new[]{"Карло","Босс","ОченьДлинноеИмяИгрока","Фат","МойБой","Алиса","Майлз","Аза"}[i],i<3?samples[i]:null,Mathf.Max(0,3-i),i!=players-1 || players<=3,i==3);
                foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
                Canvas.ForceUpdateCanvases();
                foreach(var text in root.GetComponentsInChildren<TMP_Text>(true))text.ForceMeshUpdate(true,true);
                foreach(var graphic in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))graphic.Rebuild(UnityEngine.UI.CanvasUpdate.PreRender);
                camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,1800,720),0,0);pixels.Apply();Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally { RenderTexture.active=previous;Object.DestroyImmediate(root);Object.DestroyImmediate(target);Object.DestroyImmediate(pixels); }
        }
    }
}
