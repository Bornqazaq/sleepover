using System.Collections;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Core.UI;
using Igruha.Minigames.CarryItem;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>Exercises the real phase clock and replicated water presentation on both peers.</summary>
    public sealed class CarryHudProbe : MonoBehaviour
    {
        private CarryItemMinigame game;
        private CarryWaterHud hud;
        private bool failed;
        private string screenshots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.TryGetValue("--carry-hud-check", out _)) return;
            var go = new GameObject(nameof(CarryHudProbe)); DontDestroyOnLoad(go);
            go.AddComponent<CarryHudProbe>();
        }

        private IEnumerator Start()
        {
            LaunchArguments.TryGetValue("--carry-hud-screenshots", out screenshots);
            float timeout = Time.realtimeSinceStartup + 90;
            bool practiceReady = false, sawCountdown = false, countdownCaptured = false;
            float countdownBegan = 0;
            int heldClockSamples = 0;
            while (Time.realtimeSinceStartup < timeout)
            {
                var net = NetworkManager.Singleton;
                var selection = CharacterSelection.Current;
                if (net != null && net.IsConnectedClient && selection != null && !selection.HasChosen &&
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Hub")
                { selection.ReportReady(); selection.Choose((int)net.LocalClientId); }
                game = MinigameControllerBase.Current as CarryItemMinigame;
                if (game != null && game.Phase == MinigamePhase.Practice && !practiceReady)
                {
                    yield return new WaitForSeconds(2);
                    Capture("practice"); practiceReady = true;
                    game.ToggleTutorialReady();
                }
                if (game != null && game.Phase == MinigamePhase.Round)
                {
                    game.TryGetRoundTime(out float remaining, out float duration);
                    if (game.StartCountdownActive)
                    {
                        if (!sawCountdown) { countdownBegan = Time.time; sawCountdown = true; }
                        if (!countdownCaptured && Time.time - countdownBegan > .5f)
                        { countdownCaptured = true; Capture("countdown"); }
                        if (duration > 0)
                        {
                            heldClockSamples++;
                            // A remote countdown starts on receipt of the phase, up to one tick later.
                            Check(duration - remaining < (NetworkManager.Singleton.IsServer ? .05f : .4f), "clock held during countdown");
                        }
                    }
                    else if (sawCountdown) break;
                }
                yield return null;
            }
            Check(game != null && sawCountdown && heldClockSamples > 20, "observed full countdown on this peer");
            if (failed) yield break;
            hud = game.GetComponentInChildren<CarryWaterHud>();
            Check(hud != null && game.GetComponentsInChildren<CarryWaterHud>().Length == 1, "one water HUD");
            CheckNoLegacyHud();
            yield return new WaitForSeconds(.7f);
            game.TryGetRoundTime(out float after, out float total);
            Check(total - after > .25f && total - after < 1.4f, "clock begins at GO, elapsed=" + (total - after));
            if (NetworkManager.Singleton.IsServer)
                foreach (TeamSide side in new[] { TeamSide.A, TeamSide.B })
                {
                    game.TapOf(side).enabled = false; game.TankOf(side).enabled = false;
                    game.CartOf(side).SetFilling(false); game.CartOf(side).SetPouring(false);
                }
            string[] names = { "full", "half", "empty", "filling", "pouring", "spill" };
            for (int stage = 0; stage < names.Length; stage++)
            {
                yield return At(3 + stage * 3);
                if (NetworkManager.Singleton.IsServer)
                    foreach (TeamSide side in new[] { TeamSide.A, TeamSide.B })
                    {
                        var cart = game.CartOf(side);
                        int value = stage == 0 ? game.Config.CartCapacity : stage == 2 ? 0 : game.Config.CartCapacity / 2;
                        cart.ChangeWater(value - cart.Water, WaterLossReason.Filled);
                        cart.SetFilling(stage == 3); cart.SetPouring(stage == 4);
                        if (stage == 5) cart.ChangeWater(-5, WaterLossReason.Tilt);
                    }
                yield return At(3.4f + stage * 3);
                var local = SessionScoreboard.Current.LocalPlayer;
                var own = game.CartForPlayer(local.Id);
                Check(hud.WaterVisible && Mathf.Abs(hud.DisplayedWaterFraction - own.Load) < .001f, "replicated " + names[stage]);
                string expected = stage == 2 ? "БАК ПУСТ" : stage == 3 ? "НАПОЛНЯЕТСЯ" : stage == 4 ? "ДОСТАВЛЯЕМ" : stage == 5 ? "ВОДА УХОДИТ!" : null;
                if (expected != null)
                    Check(hud.transform.Find("Your water/Water state").GetComponent<TMP_Text>().text == expected, "context " + names[stage]);
                CheckLabelsFit();
                Capture(names[stage]);
                // GUI capture needs one rendered frame before moving to another stage.
                yield return null;
            }
            yield return At(22);
            if (NetworkManager.Singleton.IsServer)
            {
                int slotA = 0, slotB = 0;
                foreach (var player in SessionScoreboard.Current.Players)
                {
                    TeamSide side = game.TeamOfPlayer(player.Id);
                    var carry = game.CartOf(side).Carry;
                    int slot = side == TeamSide.A ? slotA++ : slotB++;
                    player.Avatar.RequestTeleport(carry.StationOf(slot) + Vector3.up * .04f, carry.transform.rotation);
                }
            }
            yield return At(22.6f);
            if (NetworkManager.Singleton.IsServer)
                foreach (var player in SessionScoreboard.Current.Players)
                    Check(game.CartOf(game.TeamOfPlayer(player.Id)).Carry.TryGrab(player.Avatar), "crew grabs cart");
            yield return At(23.1f);
            Check(game.CoordinationHud.Visible && game.CoordinationHud.MemberCount >= 1, "coordination visible while holding");
            var waterRect = (RectTransform)hud.transform.Find("Your water");
            var coordinationRect = (RectTransform)game.CoordinationHud.transform.Find("Coordination");
            Check(!ScreenRect(waterRect).Overlaps(ScreenRect(coordinationRect)), "water and crew do not overlap");
            CheckLabelsFit(); Capture("holding");
            yield return new WaitForSeconds(.4f);
            if (!string.IsNullOrEmpty(screenshots))
            {
                Screen.SetResolution(1920, 1200, false);
                yield return new WaitForSeconds(1);
                CheckLabelsFit(); Capture("16x10");
                yield return new WaitForSeconds(.4f);
            }
            CheckNoLegacyHud();
            if (!failed) Debug.Log("CARRY_HUD PASS countdownSamples=" + heldClockSamples + " waterStates=6");
        }

        private static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private IEnumerator At(float elapsed)
        {
            while (game != null && game.Phase == MinigamePhase.Round)
            {
                game.TryGetRoundTime(out float remaining, out float total);
                if (total - remaining >= elapsed) yield break;
                yield return null;
            }
            Check(false, "round ended unexpectedly");
        }

        private void CheckNoLegacyHud()
        {
            foreach (var root in game.gameObject.scene.GetRootGameObjects())
                foreach (var bar in root.GetComponentsInChildren<TeamProgressBar>(true))
                    Check(!bar.gameObject.activeInHierarchy, "legacy progress hidden");
            // RoundHud lives on the scene canvas, so inspect the hierarchy once per checkpoint.
            foreach (var root in game.gameObject.scene.GetRootGameObjects())
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                    if (text.name == "StatusText") Check(!text.gameObject.activeInHierarchy, "old status hidden");
        }

        private void CheckLabelsFit()
        {
            Canvas.ForceUpdateCanvases();
            foreach (var label in hud.GetComponentsInChildren<TMP_Text>())
            {
                label.ForceMeshUpdate();
                Check(!label.isTextOverflowing, "text fits: " + label.name + "=" + label.text);
            }
        }

        private void Capture(string stage)
        {
            if (!string.IsNullOrEmpty(screenshots)) ScreenCapture.CaptureScreenshot(screenshots + "-" + stage + ".png");
        }

        private void Check(bool condition, string message)
        {
            if (condition) return;
            failed = true; Debug.LogError("CARRY_HUD FAIL " + message);
        }
    }
}
