using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Minigames.Stopwatch;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Opt-in Play Mode review: real input path, camera and all eight unchanged roster avatars.</summary>
    public static class StopwatchButtonHandsReview
    {
        public static void Start(string folder)
        {
            var game=Object.FindFirstObjectByType<StopwatchMinigame>();
            if(!EditorApplication.isPlaying || game==null || game.IsPractice || game.LocalButton==null)
                throw new InvalidOperationException("Enter the actual Stopwatch round first.");
            game.StartCoroutine(Capture(game,folder));
        }

        private static IEnumerator Capture(StopwatchMinigame game,string folder)
        {
            Directory.CreateDirectory(folder);
            var stage=game.GetComponent<MinigameStageState>();
            while(stage.Stage!=2)yield return null;
            bool wasEnabled=stage.enabled;stage.enabled=false;
            foreach(var bot in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if(bot.GetType().Name.Contains("DebugBot"))bot.enabled=false;
            foreach(var other in Object.FindObjectsByType<CageButton>(FindObjectsSortMode.None))
                other.OpenWindow(100); // Keep the other contestants unfinished during the review.
            var button=game.LocalButton;
            var original=button.Owner;
            var presentation=game.GetComponent<StopwatchButtonPresentation>();
            var cameras=Object.FindFirstObjectByType<MinigameCameraController>();
            var roster=AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            var report=new StringBuilder("Play Mode: real PlayerInputReader → PlayerInteractor → CageButton.\n");
            var position=original.Position;var rotation=original.transform.rotation;
            GameObject avatar=null;
            presentation.enabled=false;original.gameObject.SetActive(false);
            try
            {
                foreach(var character in roster.Characters)
                {
                    avatar=Object.Instantiate(character.Prefab,position,rotation);
                    var player=avatar.GetComponent<PlayerController>();
                    var input=avatar.GetComponent<PlayerInputReader>();input.EngageAutopilot();
                    button.SetOwner(player);button.OpenWindow(100);
                    cameras.Apply(CameraMode.ThirdPerson,player.transform);
                    presentation.enabled=true;
                    yield return new WaitForSeconds(.9f);
                    Check(presentation.IsActive && player.MovementLocked,character.DisplayName+" enters button view; stage="+stage.Stage);
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder,character.DisplayName+"-idle.png"));
                    yield return new WaitForSeconds(.15f);
                    double start=Time.realtimeSinceStartupAsDouble;
                    input.DriveInteractHold(true);
                    yield return new WaitForSeconds(.9f);
                    Check(button.State==CageButton.ButtonState.Running,character.DisplayName+" input starts measurement");
                    Vector3 surface=button.PressSurface.bounds.center+Vector3.up*button.PressSurface.bounds.extents.y;
                    float error=Vector3.Distance(presentation.Hand.ContactPosition,surface);
                    Check(error<.002f,character.DisplayName+" finger contact: "+error);
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder,character.DisplayName+"-held.png"));
                    yield return new WaitForSeconds(.3f);
                    input.DriveInteractHold(false);
                    double elapsed=Time.realtimeSinceStartupAsDouble-start;
                    yield return new WaitForSeconds(.02f);
                    Check(button.Completed,character.DisplayName+" release stops measurement");
                    Check(Math.Abs(button.Measured-elapsed)<.09,character.DisplayName+" animation does not delay input");
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder,character.DisplayName+"-release.png"));
                    yield return new WaitForSeconds(.9f);
                    Check(!presentation.IsActive && !player.MovementLocked && cameras.CurrentMode==CameraMode.ThirdPerson,
                        character.DisplayName+" camera and movement restored");
                    Check(player.GetComponent<Igruha.Core.Interaction.PlayerInteractor>().ButtonOverride==null,
                        character.DisplayName+" interaction override restored");
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder,character.DisplayName+"-returned.png"));
                    report.AppendLine(character.DisplayName+": PASS; contact="+error.ToString("F6")+
                        " m; input interval="+elapsed.ToString("F3")+" s; measured="+button.Measured.ToString("F3")+" s");
                    yield return new WaitForSeconds(.15f);
                    presentation.enabled=false;Object.Destroy(avatar);avatar=null;
                    yield return null;
                }
                File.WriteAllText(Path.Combine(folder,"review.txt"),report.ToString());
                Debug.Log("STOPWATCH_HAND_REVIEW PASS 8/8 → "+folder);
            }
            finally
            {
                presentation.enabled=false;
                if(avatar!=null)Object.Destroy(avatar);
                original.gameObject.SetActive(true);button.SetOwner(original);button.CloseWindow();
                cameras.Apply(CameraMode.ThirdPerson,original.transform);
                presentation.enabled=true;stage.enabled=wasEnabled;
            }
        }

        private static void Check(bool valid,string message)
        {
            if(!valid)throw new InvalidOperationException("STOPWATCH_HAND_REVIEW FAIL "+message);
        }
    }
}
