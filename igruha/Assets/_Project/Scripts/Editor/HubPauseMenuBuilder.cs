using Igruha.Core.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.EditorTools
{
    public static class HubPauseMenuBuilder
    {
        private static readonly Color Ink = Hex("0E1C1D"), Cream = Hex("F4ECD9"), Gold = Hex("F0BB76"), Muted = Hex("A7B8AD");
        private static TMP_FontAsset bold, regular;

        [MenuItem("Igruha/Хаб/Оформить меню паузы")]
        public static void Build()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (Application.isPlaying || scene.path != "Assets/_Project/Scenes/Hub.unity")
                throw new System.InvalidOperationException("Open saved Hub in Edit Mode before building its pause menu.");
            var pause = Object.FindFirstObjectByType<PauseScreen>(FindObjectsInactive.Include);
            if (pause == null) throw new System.InvalidOperationException("Hub PauseScreen missing");
            bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HubConsoleAssets.Folder + "/Fonts/ConsoleHDBold.asset");
            regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HubConsoleAssets.Folder + "/Fonts/ConsoleHDRegular.asset");
            if (bold == null || regular == null) throw new System.InvalidOperationException("Console HD fonts missing");
            var data = new SerializedObject(pause);
            var panel = (GameObject)data.FindProperty("panel").objectReferenceValue;
            var parent = panel.transform.parent;
            int index = panel.transform.GetSiblingIndex();
            Object.DestroyImmediate(panel);
            var root = Rect(parent, "PausePanel", 0, 0, 0, 0);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            root.SetSiblingIndex(index);
            var wash = root.gameObject.AddComponent<Image>(); wash.color = new Color(.012f,.026f,.025f,.38f);
            var view = root.gameObject.AddComponent<PauseMenuView>();
            var card = Rect(root, "PauseCard", 0, 0, 680, 900);
            card.anchorMin = card.anchorMax = new Vector2(.045f,.5f); card.pivot = new Vector2(0,.5f); card.anchoredPosition = Vector2.zero;
            Box(card, "Shadow", 12, 14, 680, 900, new Color(0,0,0,.22f));
            Box(card, "Surface", 0, 0, 680, 900, new Color(Ink.r,Ink.g,Ink.b,.97f));
            Box(card, "GoldEdge", 0, 0, 4, 900, Gold);
            Label(card, "Brand", "SLEEPOVER", 58, 45, 550, 46, 32, Cream, true).characterSpacing = 3;
            Label(card, "BrandNote", "ОСТАВАЙСЯ ЕЩЁ НА ОДИН РАУНД", 60, 95, 550, 24, 14, Muted, true).characterSpacing = 2;
            Box(card, "Rule", 60, 146, 560, 1, new Color(.8f,.8f,.65f,.22f));
            var status = Label(card, "Status", "ИГРА НА ПАУЗЕ", 60, 181, 560, 27, 16, Gold, true);
            status.characterSpacing = 2;
            var title = Label(card, "Title", "Небольшой\nперерыв", 56, 224, 574, 158, 61, Cream, true);
            var message = Label(card, "Message", "Устраивайся поудобнее.\nПродолжим, когда будешь готов.", 60, 406, 560, 76, 22, Muted);
            TMP_Text primaryText, secondaryText;
            var primary = Button(card, "Resume", "ПРОДОЛЖИТЬ", 530, out primaryText);
            var secondary = Button(card, "Exit", "ВЫЙТИ ИЗ ИГРЫ", 638, out secondaryText);
            primary.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = secondary, selectOnDown = secondary };
            secondary.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = primary, selectOnDown = primary };
            Box(card, "FooterRule", 60, 784, 560, 1, new Color(.8f,.8f,.65f,.22f));
            var footer = Label(card, "Controls", "↑ ↓  ВЫБОР     ENTER  ВЫБРАТЬ     ESC  В ИГРУ", 60, 811, 560, 28, 15, Muted, true);
            var v = new SerializedObject(view);
            Set(v,"title",title); Set(v,"message",message); Set(v,"status",status); Set(v,"footer",footer);
            Set(v,"primaryLabel",primaryText); Set(v,"secondaryLabel",secondaryText);
            Set(v,"primary",primary); v.ApplyModifiedPropertiesWithoutUndo();
            Set(data,"panel",root.gameObject); Set(data,"resumeButton",primary); Set(data,"exitButton",secondary); Set(data,"presentation",view);
            data.ApplyModifiedPropertiesWithoutUndo();
            root.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Hub pause menu saved; existing gameplay and network bindings preserved.");
        }
        private static Button Button(Transform parent, string name, string text, float y, out TMP_Text label)
        {
            var r = Rect(parent,name,60,y,560,90);
            var surface = r.gameObject.AddComponent<Image>(); surface.color = Hex("182F30");
            var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = surface; b.transition = Selectable.Transition.None;
            var edge = Box(r,"Accent",0,0,3,90,Gold);
            label = Label(r,"Label",text,27,0,466,90,24,Cream,true);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            var arrow = Label(r,"Arrow","→",497,0,40,90,26,Cream,true); arrow.alignment = TextAlignmentOptions.Center;
            var f = new SerializedObject(r.gameObject.AddComponent<PauseMenuButton>());
            Set(f,"surface",surface); Set(f,"accent",edge); Set(f,"label",label); Set(f,"marker",arrow); f.ApplyModifiedPropertiesWithoutUndo();
            return b;
        }
        private static void Set(SerializedObject s,string key,Object value) => s.FindProperty(key).objectReferenceValue = value;
        private static RectTransform Rect(Transform p,string n,float x,float y,float w,float h)
        {
            var r = new GameObject(n,typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(p,false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0,1); r.anchoredPosition = new Vector2(x,-y); r.sizeDelta = new Vector2(w,h); return r;
        }
        private static Image Box(Transform p,string n,float x,float y,float w,float h,Color c)
        {
            var i = Rect(p,n,x,y,w,h).gameObject.AddComponent<Image>(); i.color=c; i.raycastTarget=false; return i;
        }
        private static TMP_Text Label(Transform p,string n,string text,float x,float y,float w,float h,float size,Color c,bool strong=false)
        {
            var t = Rect(p,n,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>(); t.font=strong?bold:regular;
            t.text=text; t.fontSize=size; t.color=c; t.alignment=TextAlignmentOptions.TopLeft; t.raycastTarget=false;
            t.textWrappingMode=TextWrappingModes.NoWrap; return t;
        }
        private static Color Hex(string value) { ColorUtility.TryParseHtmlString("#"+value,out var c); return c; }
    }
}
