using System;
using System.IO;
using Igruha.Core.Audio;
using Igruha.Core.CameraSystems;
using Igruha.Core.Hub.Activities;
using TMPro;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Builds a single cabinet without replacing any existing scene network identities.</summary>
    public static class HubArkanoidBuilder
    {
        private const string RootName = "_HubArkanoid";
        private const string Folder = "Assets/_Project/Art/Hub/Arkanoid";
        private static readonly Color Ink = new Color(.025f, .055f, .085f);
        private static readonly Color Mint = new Color(.35f, 1f, .78f);
        private static TMP_FontAsset font;

        [MenuItem("Igruha/Хаб/Собрать арканоид")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/_Project/Scenes/Hub.unity")
                throw new InvalidOperationException("Open Hub outside Play Mode.");
            Transform corner = GameObject.Find("_HubOriginal/ArcadeCorner")?.transform;
            if (corner == null || corner.childCount != 3) throw new InvalidOperationException("Expected three arcade cabinets.");
            Transform cabinet = corner.GetChild(1);
            Transform root = GameObject.Find(RootName)?.transform;
            if (root != null) throw new InvalidOperationException("Arcade already exists; update its objects without regenerating the NetworkObject identity.");
            root = new GameObject(RootName).transform;
            root.SetPositionAndRotation(cabinet.position, cabinet.rotation);
            foreach (Renderer r in cabinet.GetComponentsInChildren<Renderer>())
                if (r.name == "HO_Glow") r.enabled = false;
            HubCozyMaterials.EnsureFolder(Folder);
            font = MakeFont();

            Transform stand = Child(root, "Stand");
            stand.localPosition = new Vector3(0, .025f, 1.0f);
            stand.localRotation = Quaternion.Euler(0, 180, 0);
            Transform seat = Child(root, "ArkanoidStation");
            seat.SetPositionAndRotation(stand.position, stand.rotation);
            var zone = seat.gameObject.AddComponent<BoxCollider>();
            zone.isTrigger = true; zone.center = Vector3.up * .9f; zone.size = new Vector3(1, 1.8f, 1);
            seat.gameObject.AddComponent<NetworkObject>();
            var station = seat.gameObject.AddComponent<ArkanoidStation>();
            Set(station, "standPoint", stand); Set(station, "activityName", "арканоид");
            Set(station, "takeRadius", 2f); Set(station, "leaveRadius", 2.2f);
            var camera = root.gameObject.AddComponent<ArkanoidCamera>();
            Set(camera, "station", station); Set(station, "localCamera", camera);

            var screen = new GameObject("ArcadeScreen", typeof(RectTransform), typeof(Canvas));
            screen.transform.SetParent(root, false);
            screen.transform.localPosition = new Vector3(0, 1.5f, .164f);
            screen.transform.localRotation = Quaternion.Euler(9, 180, 0);
            screen.transform.localScale = Vector3.one * .001f;
            var screenRect = (RectTransform)screen.transform;
            screenRect.sizeDelta = new Vector2(620, 450);
            var canvas = screen.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            Rect(screenRect, "Glass", Vector2.zero, new Vector2(620, 450), Ink);
            Label(screenRect, "Score", new Vector2(-175, 195), new Vector2(240, 30), 20, "СЧЁТ 00000", Mint, out var score);
            Label(screenRect, "Level", new Vector2(30, 195), new Vector2(130, 30), 20, "LV 1", Color.white, out var level);
            Label(screenRect, "Lives", new Vector2(205, 195), new Vector2(175, 30), 20, "ЖИЗНИ 3", new Color(1,.65f,.4f), out var lives);
            Rect(screenRect, "HeaderRule", new Vector2(0,173), new Vector2(568, 2), Mint);
            var field = (RectTransform)ChildRect(screenRect, "Field");
            field.sizeDelta = new Vector2(540, 336);
            Rect(field, "LeftWall", new Vector2(-263, 0), new Vector2(3, 333), new Color(.16f,.35f,.4f));
            Rect(field, "RightWall", new Vector2(263, 0), new Vector2(3, 333), new Color(.16f,.35f,.4f));
            var colors = new[] {new Color(.3f,.9f,.72f),new Color(.3f,.75f,1),new Color(.75f,.55f,1),new Color(1,.58f,.35f),new Color(1,.8f,.35f)};
            var brickObjects = new GameObject[ArkanoidRules.BrickCount];
            float units = ArkanoidPresentation.PixelsPerUnit;
            for (int i = 0; i < brickObjects.Length; i++)
            {
                var brick = Rect(field, "Brick" + i, ArkanoidRules.BrickCenter(i) * units,
                    new Vector2(ArkanoidRules.BrickWidth, ArkanoidRules.BrickHeight) * units, colors[i / ArkanoidRules.Columns]);
                Rect(brick, "Glint", new Vector2(0, 5.3f), new Vector2(49, 2), new Color(1,1,1,.32f));
                brickObjects[i] = brick.gameObject;
            }
            var paddle = Rect(field, "Paddle", new Vector2(0, ArkanoidRules.PaddleY * units),
                new Vector2(ArkanoidRules.PaddleWidth, ArkanoidRules.PaddleHeight) * units, Mint);
            Rect(paddle, "PaddleTop", new Vector2(0,3), new Vector2(72,2), Color.white);
            var ball = Rect(field, "Ball", new Vector2(0,-117), Vector2.one * (ArkanoidRules.BallRadius * 2 * units), Color.white);
            var panel = Rect(screenRect, "StartPanel", new Vector2(0,-20), new Vector2(420,174), new Color(.018f,.035f,.06f,.97f));
            Rect(panel, "Accent", new Vector2(0,85), new Vector2(420,3), Mint);
            Label(panel, "Title", new Vector2(0,45), new Vector2(400,45), 30, "АРКАНОИД", Color.white, out var title);
            var buttonPanel = Rect(panel, "PlayButton", new Vector2(0,-5), new Vector2(246,45), Mint);
            Label(buttonPanel, "ButtonLabel", Vector2.zero, new Vector2(240,40), 23, "ЗАЖМИ E", Ink, out var button);
            Label(panel, "Hint", new Vector2(0,-55), new Vector2(410,32), 18, "РАЗБЕЙ ВСЕ КИРПИЧИ", Mint, out var hint);
            Label(screenRect, "Controls", new Vector2(0,-196), new Vector2(595,35), 17,
                "МЫШЬ — ПЛАТФОРМА   ·   ЛКМ — МЯЧ   ·   E — ВЫХОД", new Color(.65f,.8f,.83f), out _);
            var presentation = screen.AddComponent<ArkanoidPresentation>();
            Set(presentation, "station", station); Set(presentation, "paddle", paddle); Set(presentation, "ball", ball);
            Set(presentation, "score", score); Set(presentation, "level", level); Set(presentation, "lives", lives);
            Set(presentation, "panel", panel.gameObject); Set(presentation, "title", title);
            Set(presentation, "button", button); Set(presentation, "hint", hint);
            var so = new SerializedObject(presentation); var bricks = so.FindProperty("bricks");
            bricks.arraySize = brickObjects.Length;
            for (int i = 0; i < brickObjects.Length; i++) bricks.GetArrayElementAtIndex(i).objectReferenceValue = brickObjects[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            var rig = Child(root, "ArcadeView").gameObject.AddComponent<CinemachineCamera>();
            Vector3 eye = screen.transform.position - screen.transform.forward * .78f;
            rig.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(screen.transform.position - eye, screen.transform.up));
            rig.Priority.Value = -100; rig.Priority.Enabled = true;
            var lens = rig.Lens; lens.FieldOfView = 38; lens.NearClipPlane = .03f; lens.FarClipPlane = 100;
            rig.Lens = lens; rig.enabled = false;
            Set(camera, "screenRig", rig); Set(camera, "brain", Object.FindFirstObjectByType<CinemachineBrain>());
            Set(camera, "cameraController", Object.FindFirstObjectByType<MinigameCameraController>());
            var sound = screen.AddComponent<MinigameAudioPlayer>();
            SfxLibraryBuilder.Build(Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/art/hub-arkanoid-sfx.json")));
            Set(sound, "library", AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>("Assets/_Project/Audio/Hub/Arkanoid/SfxLibrary.asset"));
            Set(sound, "maxDistance", 10f); Set(presentation, "audioPlayer", sound);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Arkanoid: middle cabinet, world screen, local blend camera, one network station and six spatial sound slots.");
        }

        private static TMP_FontAsset MakeFont()
        {
            string path = Folder + "/ArcadeFont.asset";
            var result = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (result != null) return result;
            result = TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>(
                "Assets/_Project/Art/UI/Fonts/Manrope-Bold.ttf"), 72, 8, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
            result.name = "ArcadeFont";
            result.TryAddCharacters("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZАБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ —·", out string missing);
            if (!string.IsNullOrEmpty(missing)) throw new InvalidOperationException("Missing arcade glyphs: " + missing);
            result.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(result, path);
            foreach (var atlas in result.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, result);
            AssetDatabase.AddObjectToAsset(result.material, result);
            return result;
        }

        private static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); return t;
        }
        private static RectTransform ChildRect(Transform parent, string name)
        {
            var t = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            t.SetParent(parent, false); return t;
        }
        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var rect = ChildRect(parent, name); rect.anchoredPosition = position; rect.sizeDelta = size;
            var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false;
            return rect;
        }
        private static void Label(Transform parent, string name, Vector2 position, Vector2 size, float pointSize,
            string value, Color color, out TMP_Text text)
        {
            var rect = ChildRect(parent, name); rect.anchoredPosition = position; rect.sizeDelta = size;
            text = rect.gameObject.AddComponent<TextMeshProUGUI>(); text.font = font; text.fontSize = pointSize;
            text.text = value; text.color = color; text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap; text.raycastTarget = false;
        }
        private static void Set(Object target, string property, Object value)
        {
            var so = new SerializedObject(target); so.FindProperty(property).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void Set(Object target, string property, string value)
        {
            var so = new SerializedObject(target); so.FindProperty(property).stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void Set(Object target, string property, float value)
        {
            var so = new SerializedObject(target); so.FindProperty(property).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
