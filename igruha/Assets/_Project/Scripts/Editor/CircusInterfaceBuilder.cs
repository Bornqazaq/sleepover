using System;
using System.Linq;
using Igruha.Core.Player;
using Igruha.Core.UI;
using Igruha.Minigames.CansOrder;
using Igruha.Minigames.Circus;
using Igruha.Minigames.Stopwatch;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.EditorTools
{
    /// <summary>Idempotent scene-owned circus UI. Shared HUD, input and camera code stay untouched.</summary>
    internal static class CircusInterfaceBuilder
    {
        [MenuItem("Igruha/Цирк/Оформить табло и подсказки — обе сцены")]
        internal static void ApplyBoth()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Leave Play Mode first.");
            var original = EditorSceneManager.GetActiveScene();
            if (original.isDirty) throw new InvalidOperationException("Save the scene first.");
            string path = original.path;
            try
            {
                foreach (string scene in new[] { "CansOrder", "Stopwatch" })
                {
                    EditorSceneManager.OpenScene("Assets/_Project/Scenes/Minigames/" + scene + ".unity");
                    ApplyActive();
                    EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                }
            }
            finally { if (!string.IsNullOrEmpty(path)) EditorSceneManager.OpenScene(path); }
        }

        internal static void ApplyActive()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != "CansOrder" && scene.name != "Stopwatch") return;
            bool cans = scene.name == "CansOrder";
            if (cans) CircusUiWiring.ApplyCansOrder(); else CircusUiWiring.ApplyStopwatch();
            var board = GameObject.Find("_Arena/Scoreboard");
            var canvas = GameObject.Find("_UI/Canvas").transform;
            var config = AssetDatabase.LoadAssetAtPath<CansOrderConfig>("Assets/_Project/Settings/Gameplay/Minigames/CansOrderConfig.asset");
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            var portraits = roster.Characters.Select(c => AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/_Project/Art/UI/CharacterSelect/Portraits/" + c.DisplayName + "_Face.png")).ToArray();
            if (portraits.Any(p => p == null)) throw new InvalidOperationException("A roster portrait is missing.");
            var screens = board.GetComponentsInChildren<WorldScoreboardFace>(true).Select(f => (RectTransform)f.transform).ToList();
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            foreach (var screen in screens)
            {
                Remove(screen, "CircusFace");
                foreach (var graphic in screen.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
            }
            Remove(canvas, "_CircusScreenBoard");
            if (!cans)
            {
                // Preserve the existing screen copy of Stopwatch's board, including its target.
                var legacy = canvas.Find("StopwatchHud");
                if (legacy != null) foreach (var graphic in legacy.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
                var screen = CircusUiLayout.Rect(canvas, "_CircusScreenBoard", 316, -145, 576, 230);
                screen.anchorMin = screen.anchorMax = new Vector2(0, 1);
                screens.Add(screen);
            }
            var presentation = board.GetComponent<CircusBoardView>() ?? board.AddComponent<CircusBoardView>();
            presentation.Construct(screens.ToArray(), font, portraits, config);
            presentation.Task(cans ? "ПОРЯДОК БАНОК" : "СЕКУНДОМЕР", "ЦИРК БРУНО", cans);
            var arrangement = board.transform.Find("ArrangementPanel");
            if (arrangement != null) arrangement.gameObject.SetActive(false);
            Component controller = cans ? (Component)UnityEngine.Object.FindFirstObjectByType<CanOrderBoard>(FindObjectsInactive.Include)
                : UnityEngine.Object.FindFirstObjectByType<StopwatchScoreboard>(FindObjectsInactive.Include);
            Bind(controller, "presentation", presentation);

            Remove(canvas, "_CircusHudPresentation");
            var root = CircusUiLayout.Rect(canvas, "_CircusHudPresentation", 0, 0, 0, 0);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            var hud = root.gameObject.AddComponent<CircusHudView>();
            hud.Construct(root, font, config);
            Component local = cans ? (Component)UnityEngine.Object.FindFirstObjectByType<CansOrderLocalHud>(FindObjectsInactive.Include)
                : UnityEngine.Object.FindFirstObjectByType<StopwatchLocalHud>(FindObjectsInactive.Include);
            Bind(local, "presentation", hud);
            foreach (var graphic in local.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
            foreach (string name in new[] { "ShelfPromptLabel", "ShelfControlsLabel", "RevealCardLabel", "LastCircleLabel" })
            {
                var legacy = canvas.Find(name);
                if (legacy != null) foreach (var graphic in legacy.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
            }
            canvas.Find("_HudOverlay")?.SetAsLastSibling();
            var timer = canvas.Find("_HudPlates/TimerPlate") as RectTransform;
            if (timer != null)
            {
                timer.anchorMin = timer.anchorMax = new Vector2(1, 1);
                timer.anchoredPosition = new Vector2(-142, -30);
                var background = timer.GetComponent<Image>();
                if (background != null) background.color = CircusUiLayout.Wine;
                var caption = timer.Find("TimerCaption")?.GetComponent<TMP_Text>();
                if (caption != null) caption.color = CircusUiLayout.Gold;
            }
            EditorUtility.SetDirty(presentation);
            EditorUtility.SetDirty(hud);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void Remove(Transform parent, string name)
        { var old = parent.Find(name); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject); }

        private static void Bind(Component target, string field, UnityEngine.Object value)
        {
            if (target == null) throw new InvalidOperationException("Missing UI controller for " + field);
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
