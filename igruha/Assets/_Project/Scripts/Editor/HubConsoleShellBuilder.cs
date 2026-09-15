using System;
using Igruha.Core.Hub;
using Igruha.Core.Player;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    public static class HubConsoleShellBuilder
    {
        private static readonly Color Ink = H("101E23"), Panel = H("203439"), Line = H("3D5558"), Cream = H("F4ECD9"), Muted = H("A4B8B5"), Gold = H("F4BE81");
        private static TMP_FontAsset regular, bold;
        private const string PortraitFolder = "Assets/_Project/Art/UI/CharacterSelect/Portraits/";

        public static void Wrap(RectTransform screen, RectTransform library, ConsoleMenu menu)
        {
            regular = HubConsoleAssets.Font(false); bold = HubConsoleAssets.Font(true);
            var root = Rect(screen, "ConsoleShell", 0, 0, 1280, 720);
            var shell = root.gameObject.AddComponent<ConsoleShellView>();
            library.SetParent(root, false);
            var profiles = menu.GetComponent<HubPartyProfiles>();
            if (profiles == null) profiles = menu.gameObject.AddComponent<HubPartyProfiles>();
            profiles.roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            if (profiles.roster == null) throw new InvalidOperationException("CharacterRoster not found");
            shell.profiles = profiles;
            var canvas = screen.GetComponent<Canvas>();
            if (canvas == null) canvas = screen.GetComponentInParent<Canvas>();
            canvas.worldCamera = Camera.main;
            if (canvas.GetComponent<GraphicRaycaster>() == null) canvas.gameObject.AddComponent<GraphicRaycaster>();

            string path = "Assets/_Project/Art/Hub/Console/Home/SleepoverNight.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.maxTextureSize = 2048; importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            var background = AssetDatabase.LoadAssetAtPath<Sprite>(path);

            var home = Rect(root, "Home", 0, 0, 1280, 720); shell.home = home.gameObject;
            var backdrop = Box(home, "SleepoverNight", 0, 0, 1280, 720, Color.white); backdrop.sprite = background;
            // A smooth low-cost gradient keeps the key art visible and the menu readable.
            for (int i = 0; i < 48; i++)
                Box(home, "Shade_" + i, i * 16, 0, 16, 720, new Color(.018f,.055f,.06f, Mathf.Lerp(.90f, 0f, Mathf.Pow(i / 47f, 1.9f))));
            Label(home, "SmallBrand", "ДОБРО ПОЖАЛОВАТЬ ДОМОЙ", 62, 78, 510, 24, 16, Gold, true);
            Label(home, "Title", "SLEEPOVER", 58, 119, 560, 92, 70, Cream, true);
            Label(home, "Tagline", "Друзья рядом. Ночь только начинается.", 63, 213, 490, 38, 23, Cream);
            var full = Button(home, "FullGame", "ПОЛНАЯ ИГРА", 64, 315, 425, 102, Gold, Ink, 27);
            Label(full.transform, "Description", "Все мини-игры · вперемешку · без повторов", 22, 61, 382, 23, 16, Ink);
            ((RectTransform)full.GetComponentInChildren<TMP_Text>().transform).anchoredPosition = new Vector2(22, -17);
            full.GetComponentInChildren<TMP_Text>().alignment = TextAlignmentOptions.Left;
            full.GetComponentInChildren<TMP_Text>().rectTransform.sizeDelta = new Vector2(365, 36);
            var single = Button(home, "SingleGame", "ВЫБРАТЬ МИНИ-ИГРУ", 64, 433, 425, 102, new Color(.08f,.16f,.18f,.96f), Cream, 25);
            Label(single.transform, "Description", "Один раунд любимой игры", 22, 61, 382, 23, 17, Muted);
            single.GetComponentInChildren<TMP_Text>().alignment = TextAlignmentOptions.Left;
            single.GetComponentInChildren<TMP_Text>().rectTransform.anchoredPosition = new Vector2(22, -17);
            single.GetComponentInChildren<TMP_Text>().rectTransform.sizeDelta = new Vector2(382, 36);
            Box(single.transform, "Accent", 0, 0, 3, 102, Muted);
            Label(full.transform, "Arrow", "→", 372, 20, 30, 32, 25, Ink, true);
            shell.fullGame = full; shell.singleGame = single;
            shell.homeCount = Label(home, "Connected", "В КОМНАТЕ  00 / 08", 930, 40, 288, 30, 18, Cream, true);
            shell.homeCount.alignment = TextAlignmentOptions.Right;
            shell.footer = Label(home, "Controls", "", 64, 656, 950, 28, 15, Muted);
            Box(home, "LowerRule", 64, 626, 1152, 1, new Color(.7f,.8f,.8f,.25f));

            var party = Rect(root, "Party", 0, 0, 1280, 720); shell.party = party.gameObject; Paint(party, Ink);
            var art = Box(party, "Backdrop", 0, 0, 1280, 720, Color.white); art.sprite = background;
            Box(party, "Wash", 0, 0, 1280, 720, new Color(.02f,.08f,.09f,.86f));
            Label(party, "Brand", "SLEEPOVER", 48, 28, 230, 38, 28, Cream, true);
            Label(party, "Eyebrow", "ПОЛНАЯ ИГРА", 290, 38, 520, 24, 16, Gold, true);
            Box(party, "Rule", 48, 82, 1184, 1, Line);
            Label(party, "Title", "Вся компания в сборе", 48, 104, 960, 48, 38, Cream, true);
            shell.partyCount = Label(party, "Count", "00 / 08", 1040, 110, 192, 40, 30, Gold, true);
            shell.partyCount.alignment = TextAlignmentOptions.Right;
            shell.seriesInfo = Label(party, "SeriesInfo", "", 48, 158, 1184, 23, 16, Muted);
            shell.participants = new ConsoleParticipantCard[8];
            for (int i = 0; i < 8; i++)
            {
                var r = Rect(party, "Participant_" + i, 48 + (i % 4) * 300, 186 + (i / 4) * 151, 284, 290);
                var c = r.gameObject.AddComponent<ConsoleParticipantCard>();
                c.border = Paint(r, Line);
                var surface = Rect(r, "Surface", 2, 2, 280, 286); Stretch(surface, 2); Paint(surface, Panel);
                c.badge = Label(r, "Badge", "ВАШ ПЕРСОНАЖ", 16, 12, 252, 23, 13, Gold, true);
                c.body = Box(r, "Character", 38, 30, 208, 232, Color.white); c.body.preserveAspect = true;
                c.playerName = Label(r, "PlayerName", "Игрок", 16, 247, 252, 30, 24, Cream, true);
                c.playerName.enableAutoSizing = true; c.playerName.fontSizeMin = 15; c.playerName.fontSizeMax = 24;
                c.playerName.richText = false;
                c.score = Label(r, "Score", "", 198, 33, 72, 25, 13, Muted, true);
                c.empty = Label(r, "Empty", "+\n<size=17>ЖДЁМ ДРУЗЕЙ</size>", 0, 0, 284, 290, 48, Muted);
                Stretch(c.empty.rectTransform, 10); c.empty.alignment = TextAlignmentOptions.Center;
                shell.participants[i] = c;
                if (i > 3) r.gameObject.SetActive(false);
            }
            Box(party, "ProfileDock", 48, 501, 1184, 112, new Color(.1f,.19f,.21f));
            Label(party, "ProfileHeading", "ТВОЁ ИМЯ", 66, 514, 278, 22, 13, Muted, true);
            shell.nameInput = Input(party, 66, 544, 245, 46);
            shell.saveName = Button(party, "SaveName", "OK", 319, 544, 56, 46, Line, Cream, 17);
            Label(party, "Looks", "ТВОЙ ОБЛИК", 411, 514, 330, 22, 13, Muted, true);
            shell.skins = new Button[8]; shell.skinFrames = new Image[8]; shell.portraits = new Sprite[8];
            string[] names = { "Karlan", "Boss", "Shlanga", "Fat", "MyBoy", "Girl", "Milez", "Aza" };
            for (int i = 0; i < 8; i++)
            {
                shell.portraits[i] = AssetDatabase.LoadAssetAtPath<Sprite>(PortraitFolder + names[i] + "_Body.png");
                var b = Button(party, "Look_" + names[i], "", 411 + i * 90, 540, 78, 58, Line, Cream, 14);
                shell.skinFrames[i] = b.targetGraphic as Image;
                var face = Box(b.transform, "Face", 2, 2, 74, 54, Color.white);
                face.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PortraitFolder + names[i] + "_Face.png"); face.preserveAspect = true;
                // Button colour tint applies to the face as well, making occupied characters visibly unavailable.
                b.targetGraphic = face; shell.skins[i] = b;
            }
            shell.status = Label(party, "Status", "", 48, 625, 1184, 24, 16, Muted);
            shell.start = Button(party, "StartSeries", "НАЧАТЬ ИГРУ  →", 927, 661, 305, 44, Gold, Ink, 21);
            Label(party, "StartHint", "Собери друзей — дальше игры сменяются сами", 48, 670, 830, 24, 17, Muted);

            shell.library = library.gameObject;
            shell.back = Button(root, "Back", "←  НАЗАД", 1068, 26, 164, 38, Ink, Cream, 17);
            // Keep the existing library cover count clear of the Back button.
            var page = library.Find("Page").GetComponent<TMP_Text>();
            page.rectTransform.anchoredPosition = new Vector2(854, -28);
            page.rectTransform.sizeDelta = new Vector2(176, 32);
            var launch = library.Find("FeaturedGame/Launch").gameObject.AddComponent<Button>();
            launch.targetGraphic = launch.GetComponent<Image>(); launch.targetGraphic.raycastTarget = true;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(launch.onClick, menu.Launch);
            var so = new SerializedObject(menu);
            so.FindProperty("screenRoot").objectReferenceValue = root.gameObject;
            so.FindProperty("shellView").objectReferenceValue = shell;
            so.ApplyModifiedPropertiesWithoutUndo();
            library.gameObject.SetActive(false); party.gameObject.SetActive(false); shell.back.gameObject.SetActive(false);
            EditorUtility.SetDirty(shell); EditorUtility.SetDirty(profiles);
        }
        private static TMP_InputField Input(Transform parent, float x, float y, float w, float h)
        {
            var r = Rect(parent, "NameInput", x, y, w, h); var image = Paint(r, Ink); image.raycastTarget = true;
            var input = r.gameObject.AddComponent<TMP_InputField>();
            var viewport = Rect(r, "Viewport", 10, 5, w - 20, h - 10); viewport.gameObject.AddComponent<RectMask2D>();
            var text = (TextMeshProUGUI)Label(viewport, "Text", "", 0, 0, w - 20, h - 10, 21, Cream);
            text.alignment = TextAlignmentOptions.MidlineLeft; text.richText = false;
            input.textViewport = viewport; input.textComponent = text; input.targetGraphic = image;
            input.characterLimit = 20; input.lineType = TMP_InputField.LineType.SingleLine; input.richText = false;
            input.customCaretColor = true; input.caretColor = Gold; input.selectionColor = new Color(.6f,.7f,.7f,.4f);
            return input;
        }
        private static Button Button(Transform p, string n, string t, float x, float y, float w, float h, Color bg, Color fg, float size)
        {
            var r = Rect(p,n,x,y,w,h); var image = Paint(r,bg); image.raycastTarget = true;
            var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = image;
            var colors = b.colors; colors.highlightedColor = new Color(1.12f,1.12f,1.12f); colors.selectedColor = new Color(1.16f,1.16f,1.16f);
            colors.pressedColor = new Color(.75f,.85f,.85f); colors.disabledColor = new Color(.35f,.38f,.38f,.7f); colors.fadeDuration = .12f; b.colors = colors;
            var label = Label(r,"Label",t,12,0,w-24,h,size,fg,true); label.alignment = TextAlignmentOptions.Center;
            return b;
        }
        private static void Stretch(RectTransform r, float inset)
        { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.one * inset; r.offsetMax = Vector2.one * -inset; }
        private static RectTransform Rect(Transform p,string n,float x,float y,float w,float h)
        { var r = new GameObject(n,typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(p,false); r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1); r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r; }
        private static Image Paint(RectTransform r,Color c) { var i=r.gameObject.AddComponent<Image>();i.color=c;i.raycastTarget=false;return i; }
        private static Image Box(Transform p,string n,float x,float y,float w,float h,Color c)=>Paint(Rect(p,n,x,y,w,h),c);
        private static TMP_Text Label(Transform p,string n,string t,float x,float y,float w,float h,float size,Color c,bool heavy=false)
        { var l=Rect(p,n,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();l.font=heavy?bold:regular;l.fontSize=size;l.text=t;l.color=c;l.raycastTarget=false;return l; }
        private static Color H(string v)=>HubCozyMaterials.Hex(v);
    }
}
