using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Minigames.Circus;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Opt-in destructive Play Mode review. Exit Play to restore the scene after capture.</summary>
    public static class CircusMascotReview
    {
        public static void Start(string folder)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter circus Play Mode first.");
            var game = Object.FindFirstObjectByType<MinigameControllerBase>();
            game.StopAllCoroutines(); game.enabled = false;
            foreach (var timer in Object.FindObjectsByType<RoundTimer>(FindObjectsSortMode.None)) timer.StopTimer();
            game.GetComponent<MinigameStageState>().enabled = false;
            foreach (var bot in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (bot.GetType().Name.Contains("DebugBot")) bot.enabled = false;
            game.StartCoroutine(Capture(folder));
        }

        private static IEnumerator Capture(string folder)
        {
            Directory.CreateDirectory(folder);
            var cage = Object.FindObjectsByType<CageStation>(FindObjectsSortMode.None).OrderBy(c => c.name).First();
            var reaction = cage.GetComponent<CircusCageMascot>();
            var old = cage.Occupant; cage.ReleaseOccupant(); if (old != null) old.gameObject.SetActive(false);
            cage.Configure(AssetDatabase.LoadAssetAtPath<CircusArenaConfig>("Assets/_Project/Settings/Gameplay/Minigames/CircusArenaConfig.asset"), 3);
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            var cameraObject = new GameObject("Mascot review camera"); var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.fieldOfView = 45; camera.nearClipPlane = .05f;
            var report = new StringBuilder(); int index = 0;
            foreach (var character in roster.Characters)
            {
                cage.CloseDoors(); cage.SnapToLevel(3);
                var avatar = Object.Instantiate(character.Prefab, cage.transform.TransformPoint(new Vector3(0, .22f, -.4f)), cage.transform.rotation);
                var player = avatar.GetComponent<PlayerController>();
                player.GetComponent<PlayerInputReader>().EngageAutopilot(); cage.SetOccupant(player);
                yield return new WaitForSeconds(.3f);
                cage.DescendTo(2, 4);
                float until = Time.time + 3.8f; bool captured = false; float blinkMin = 100, blinkMax = 0, minDot = 1;
                while (Time.time < until)
                {
                    if (reaction.IsPresenting)
                    {
                        blinkMin = Mathf.Min(blinkMin, reaction.Actor.BlinkWeight); blinkMax = Mathf.Max(blinkMax, reaction.Actor.BlinkWeight);
                        if (reaction.Elapsed > 1 && reaction.Elapsed < 3)
                            minDot = Mathf.Min(minDot, Vector3.Dot(reaction.Actor.PointDirection, (reaction.Anger.HeadPosition - reaction.Actor.PointOrigin).normalized));
                        if (!captured && reaction.Elapsed > 1.8f)
                        {
                            captured = true;
                            Vector3 head = reaction.Anger.HeadPosition;
                            camera.transform.position = head + player.transform.forward * 2.4f + player.transform.right * .55f + Vector3.up * .1f;
                            camera.transform.LookAt(head); camera.fieldOfView = 31;
                            Save(camera, Path.Combine(folder, character.DisplayName + "-anger.png"), 700, 700);
                            camera.fieldOfView = 48;
                            camera.transform.position = cage.transform.TransformPoint(new Vector3(4.8f, 2.8f, 5.5f));
                            camera.transform.LookAt(cage.transform.TransformPoint(new Vector3(.7f, 1f, .3f)));
                            Save(camera, Path.Combine(folder, character.DisplayName + "-cage.png"), 1280, 800);
                            report.AppendLine(character.DisplayName + ": overlays=" + reaction.Anger.OverlayCount + " steam=" + reaction.Anger.ParticleCount +
                                " headOffset=" + (head - player.Position).ToString("F3") + " heat=" + reaction.Anger.Heat);
                        }
                    }
                    yield return null;
                }
                report.AppendLine("  blink=" + blinkMin + ".." + blinkMax + " pointDot=" + minDot);
                cage.ReleaseOccupant();
                yield return null;
                if (reaction.IsPresenting || reaction.Anger.OverlayCount != 0 || reaction.Anger.ParticleCount != 0)
                    throw new InvalidOperationException("Mascot cleanup failed: " + character.DisplayName);
                Object.Destroy(avatar); index++;
            }
            Object.Destroy(cameraObject); File.WriteAllText(Path.Combine(folder, "review.txt"), report.ToString());
            Debug.Log("CIRCUS_MASCOT_REVIEW complete " + index + " → " + folder);
        }

        private static void Save(Camera camera, string path, int width, int height)
        {
            var previous = RenderTexture.active; var target = RenderTexture.GetTemporary(width, height, 24);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.Destroy(texture); }
        }
    }
}
