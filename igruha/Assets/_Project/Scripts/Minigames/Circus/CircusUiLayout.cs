using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.Circus
{
    /// <summary>Shared construction vocabulary for the two circus interfaces.</summary>
    public static class CircusUiLayout
    {
        public static readonly Color Wine=new Color(.28f,.055f,.10f);
        public static readonly Color Ink=new Color(.105f,.035f,.055f);
        public static readonly Color Cream=new Color(1f,.94f,.79f);
        public static readonly Color Gold=new Color(.94f,.67f,.28f);
        public static readonly Color Muted=new Color(.74f,.62f,.49f);
        public static readonly Color Mint=new Color(.40f,.84f,.65f);
        public static readonly Color Red=new Color(1f,.37f,.27f);

        public static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
        {
            var go=new GameObject(name,typeof(RectTransform));var r=(RectTransform)go.transform;r.SetParent(parent,false);
            r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);Place(r,x,y,w,h);return r;
        }
        public static void Place(RectTransform r,float x,float y,float w,float h)
        {r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);}
        public static CircusUiGraphic Icon(Transform parent,string name,CircusUiGraphic.Sign sign,float x,float y,float w,float h,Color color)
        {var g=Rect(parent,name,x,y,w,h).gameObject.AddComponent<CircusUiGraphic>();g.Kind=sign;g.color=color;g.raycastTarget=false;return g;}
        public static RectTransform Card(Transform parent,string name,float x,float y,float w,float h,Color fill)
        {
            var root=Rect(parent,name,x,y,w,h);
            Icon(root,"Shadow",CircusUiGraphic.Sign.Ticket,0,-4,w+2,h+2,new Color(0,0,0,.42f));
            Icon(root,"Brass",CircusUiGraphic.Sign.Ticket,0,0,w,h,Gold);
            Icon(root,"Enamel",CircusUiGraphic.Sign.Ticket,0,0,w-3,h-3,fill);return root;
        }
        public static TMP_Text Text(Transform parent,string name,TMP_FontAsset font,string value,float x,float y,float w,float h,float size,Color color,TextAlignmentOptions align=TextAlignmentOptions.Center)
        {
            var t=Rect(parent,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=value;
            t.fontSize=size;t.color=color;t.alignment=align;t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.NoWrap;
            t.overflowMode=TextOverflowModes.Ellipsis;t.richText=false;return t;
        }
        public static Image Portrait(Transform parent,float x,float y,float size)
        {var i=Rect(parent,"Portrait",x,y,size,size).gameObject.AddComponent<Image>();i.preserveAspect=true;i.raycastTarget=false;return i;}
        public static RectTransform Key(Transform parent,TMP_FontAsset font,string label,float x,float y,float width=52)
        {
            var r=Card(parent,"Key_"+label,x,y,width,46,new Color(.65f,.48f,.27f));
            Icon(r,"KeyFace",CircusUiGraphic.Sign.Ticket,0,3,width-4,38,Cream);
            Text(r,"Letter",font,label,0,5,width-6,36,21,Ink).fontStyle=FontStyles.Bold;return r;
        }
        public static void Show(GameObject go,bool visible){if(go!=null && go.activeSelf!=visible)go.SetActive(visible);}
        public static void Set(TMP_Text text,string value){if(text!=null && text.text!=value)text.text=value;}
    }
}
