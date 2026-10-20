using System;
using System.Linq;
using Igruha.Core.Audio;
using Igruha.Core.CameraSystems;
using Igruha.Minigames.Stopwatch;
using Igruha.Minigames.CansOrder;
using Igruha.Core.Minigame;
using Igruha.Core.UI;
using Igruha.Minigames.Circus;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Wires only the duel presentation, preserving the authored arenas and camera rigs.</summary>
    public static class CircusDuelBuilder
    {
        [MenuItem("Igruha/Цирк/Финальная дуэль — обе сцены")]
        public static void ApplyBoth()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and leave Play Mode first.");
            string original = EditorSceneManager.GetActiveScene().path;
            try
            {
                foreach (string name in new[] { "Stopwatch", "CansOrder" })
                {
                    var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Minigames/" + name + ".unity");
                    ApplyActive();
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally { if (!string.IsNullOrEmpty(original)) EditorSceneManager.OpenScene(original); }
        }

        internal static void ApplyActive()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != "Stopwatch" && scene.name != "CansOrder")
                throw new InvalidOperationException("Duel lighting requires a circus scene.");
            var game = Object.FindFirstObjectByType<MinigameControllerBase>();
            var duel = game.GetComponent<CircusDuelPresentation>() ?? game.gameObject.AddComponent<CircusDuelPresentation>();
            var cameraShow = game.GetComponent<CircusDuelCamera>() ?? game.gameObject.AddComponent<CircusDuelCamera>();
            var practicals = game.GetComponent<CircusDuelPracticalLights>() ?? game.gameObject.AddComponent<CircusDuelPracticalLights>();
            var practicalData = new SerializedObject(practicals);
            SetArray(practicalData, "fixtures", GameObject.Find("_CircusNight").GetComponentsInChildren<Renderer>(true)
                .Where(r => r.sharedMaterials.Any(m => m != null && m.HasProperty("_EmissionColor") &&
                    m.GetColor("_EmissionColor").maxColorComponent > 0)).ToArray());
            practicalData.ApplyModifiedPropertiesWithoutUndo();
            var cameras = Object.FindFirstObjectByType<MinigameCameraController>();
            var cameraData = new SerializedObject(cameras);
            var shotData = new SerializedObject(cameraShow);
            shotData.FindProperty("game").objectReferenceValue = game;
            shotData.FindProperty("stage").objectReferenceValue = game.GetComponent<MinigameStageState>();
            shotData.FindProperty("introStage").intValue = game is StopwatchMinigame ? StopwatchMinigame.StageDuel : CansOrderMinigame.StageDuel;
            shotData.FindProperty("cameras").objectReferenceValue = cameras;
            shotData.FindProperty("view").objectReferenceValue = cameraData.FindProperty("fixedRig").objectReferenceValue;
            SetArray(shotData, "privateShelves", Object.FindObjectsByType<CanShelf>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            shotData.ApplyModifiedPropertiesWithoutUndo();
            var gameData = new SerializedObject(game);
            var cageProperty = gameData.FindProperty("cages");
            var cages = new CageStation[cageProperty.arraySize];
            for (int i = 0; i < cages.Length; i++) cages[i] = (CageStation)cageProperty.GetArrayElementAtIndex(i).objectReferenceValue;
            var spots = Object.FindObjectsByType<CircusSpotlight>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(l => l.GetComponentInParent<CircusSpotlight>() == null).ToArray();
            if (spots.Length < 2 || cages.Length < 2) throw new InvalidOperationException("Missing circus lights or cages.");

            var canvas = GameObject.Find("_UI/Canvas").transform;
            var old = canvas.Find("_DuelTitle");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var oldFrame = canvas.Find("_DuelFrame");
            if (oldFrame != null) Object.DestroyImmediate(oldFrame.gameObject);
            var oldBlackout = canvas.Find("_DuelBlackout");
            if (oldBlackout != null) Object.DestroyImmediate(oldBlackout.gameObject);
            var oldComic = canvas.Find("_DuelComic");
            if (oldComic != null) Object.DestroyImmediate(oldComic.gameObject);
            var comicRoot = FullScreen(canvas, "_DuelComic");
            var comicGroup = comicRoot.gameObject.AddComponent<CanvasGroup>();
            comicGroup.alpha = 0; comicGroup.interactable = false; comicGroup.blocksRaycasts = false;
            var picture = FullScreen(comicRoot, "Artwork").gameObject.AddComponent<RawImage>();
            picture.raycastTarget = false;
            const string materialPath = "Assets/_Project/Art/UI/CircusDuelComic.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Igruha/UI/CircusDuelComic"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            picture.material = material;
            var font = DuelFont();
            var firstName = Nickname(comicRoot, "FirstName", font, new Color(1, .91f, .58f), new Color(1, .28f, .045f));
            var secondName = Nickname(comicRoot, "SecondName", font, new Color(.85f, 1, 1), new Color(.1f, .6f, 1));
            var versus = Nickname(comicRoot, "Versus", font, Color.white, new Color(.85f, .9f, 1));
            versus.text = "VS"; versus.fontSize = 125; versus.enableAutoSizing = false;
            versus.rectTransform.sizeDelta = new Vector2(400, 240);
            versus.rectTransform.anchoredPosition = new Vector2(0, -25);
            versus.rectTransform.localRotation = Quaternion.Euler(0, 0, -8);
            var portraits = game.GetComponent<CircusDuelPortraits>() ?? game.gameObject.AddComponent<CircusDuelPortraits>();
            var comic = comicRoot.gameObject.AddComponent<CircusDuelComicView>();
            var comicData = new SerializedObject(comic);
            comicData.FindProperty("group").objectReferenceValue = comicGroup;
            comicData.FindProperty("picture").objectReferenceValue = picture;
            comicData.FindProperty("firstName").objectReferenceValue = firstName;
            comicData.FindProperty("secondName").objectReferenceValue = secondName;
            comicData.FindProperty("versus").objectReferenceValue = versus;
            comicData.FindProperty("portraits").objectReferenceValue = portraits;
            comicData.ApplyModifiedPropertiesWithoutUndo();

            var audioTransform = game.transform.Find("DuelAudio");
            if (audioTransform == null)
            {
                audioTransform = new GameObject("DuelAudio").transform;
                audioTransform.SetParent(game.transform, false);
            }
            var audio = audioTransform.GetComponent<MinigameAudioPlayer>() ?? audioTransform.gameObject.AddComponent<MinigameAudioPlayer>();
            var audioData = new SerializedObject(audio);
            audioData.FindProperty("library").objectReferenceValue = AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>(
                "Assets/_Project/Audio/Minigames/Circus/SfxLibrary.asset");
            audioData.FindProperty("voices").intValue = 3;
            audioData.ApplyModifiedPropertiesWithoutUndo();
            var so = new SerializedObject(duel);
            SetArray(so, "cages", cages); SetArray(so, "spots", spots); SetArray(so, "houseLights", lights);
            so.FindProperty("practicalLights").objectReferenceValue = practicals;
            so.FindProperty("audioPlayer").objectReferenceValue = audio;
            so.FindProperty("cameraShow").objectReferenceValue = cameraShow;
            so.FindProperty("comicView").objectReferenceValue = comic;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static RectTransform FullScreen(Transform parent, string name)
        {
            var rect = CircusUiLayout.Rect(parent, name, 0, 0, 0, 0);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static TMP_Text Nickname(Transform parent, string name, TMP_FontAsset font, Color top, Color bottom)
        {
            var text = CircusUiLayout.Text(parent, name, font, "", 0, 0, 1200, 200, 112, Color.white);
            text.fontStyle = FontStyles.Italic;
            text.richText = false;
            text.enableAutoSizing = true; text.fontSizeMin = 28; text.fontSizeMax = 112;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.enableVertexGradient = true;
            text.colorGradient = new VertexGradient(top, top, bottom, bottom);
            const string materialPath = "Assets/_Project/Art/UI/CircusDuelNickname.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null)
            {
                mat = new Material(font.material);
                mat.SetFloat("_OutlineWidth", .22f); mat.SetColor("_OutlineColor", new Color(.025f, .012f, .04f, 1));
                mat.EnableKeyword("UNDERLAY_ON");
                mat.SetColor("_UnderlayColor", new Color(.02f,.01f,.03f,1));
                mat.SetFloat("_UnderlayOffsetX", .8f); mat.SetFloat("_UnderlayOffsetY", -.8f);
                mat.SetFloat("_UnderlayDilate", .5f); mat.SetFloat("_UnderlaySoftness", .05f);
                AssetDatabase.CreateAsset(mat, materialPath);
            }
            text.fontSharedMaterial = mat;
            text.raycastTarget = false;
            return text;
        }

        private static TMP_FontAsset DuelFont()
        {
            const string path = "Assets/_Project/Art/UI/Fonts/Circus_Russo SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null) return existing;
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Project/Art/UI/Fonts/RussoOne-Regular.ttf");
            var font = TMP_FontAsset.CreateFontAsset(source, 90, 12, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                2048, 2048, AtlasPopulationMode.Dynamic, false);
            var characters = new System.Text.StringBuilder();
            characters.Append("…—–");
            for (int c = 32; c <= 255; c++) characters.Append((char)c);
            for (int c = 0x400; c <= 0x4ff; c++) if (source.HasCharacter((char)c)) characters.Append((char)c);
            font.TryAddCharacters(characters.ToString(), out _);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { UiFonts.SansBold };
            AssetDatabase.CreateAsset(font, path);
            AssetDatabase.AddObjectToAsset(font.atlasTexture, font);
            AssetDatabase.AddObjectToAsset(font.material, font);
            EditorUtility.SetDirty(font); AssetDatabase.SaveAssets();
            return font;
        }

        private static void SetArray(SerializedObject so, string field, Object[] values)
        {
            var array = so.FindProperty(field); array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
