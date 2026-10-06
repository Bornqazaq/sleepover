using System;
using System.Collections;
using System.Linq;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.UI;
using Igruha.Minigames.CryingAngels;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Tests
{
    internal static class AngelsRoleRespawnCheck
    {
        public static IEnumerator Run(Action<string> fail)
        {
            CryingAngelsMinigame game = null;
            float deadline = Time.realtimeSinceStartup + 100;
            while (Time.realtimeSinceStartup < deadline)
            {
                game = MinigameControllerBase.Current as CryingAngelsMinigame;
                if (game != null && game.Phase == MinigamePhase.Practice && game.Keeper != null) break;
                yield return null;
            }
            if (game == null || game.Keeper == null) { fail("angels practice timeout"); yield break; }
            yield return new WaitForSeconds(1);
            var local = SessionScoreboard.Current.LocalPlayer.Avatar;
            bool keeper = local == game.Keeper;
            var tutorial = UnityEngine.Object.FindFirstObjectByType<TutorialScreen>();
            var view = UnityEngine.Object.FindFirstObjectByType<TutorialView>();
            string text = string.Join("\n", view.GetComponentsInChildren<TMP_Text>(true).Select(t => t.text));
            string expected = keeper ? "ВАША РОЛЬ — ВОДЯЩИЙ" : "ВАША РОЛЬ — БЕГУЩИЙ";
            string wrong = keeper ? "ВАША РОЛЬ — БЕГУЩИЙ" : "ВАША РОЛЬ — ВОДЯЩИЙ";
            if (!tutorial.IsVisible || !text.Contains(expected) || text.Contains(wrong)) fail("wrong local role instructions: " + expected);
            // Expanded rules stay open when a role snapshot is reapplied.
            tutorial.TogglePractice();
            if (!NetworkManager.Singleton.IsServer) game.ApplyNetworkKeeper();
            if (!tutorial.RulesExpanded) fail("role refresh collapsed rules");
            tutorial.TogglePractice();
            game.ToggleTutorialReady();
            while (Time.realtimeSinceStartup < deadline && (game == null || game.Phase != MinigamePhase.Round || game.StartCountdownActive))
            { game = MinigameControllerBase.Current as CryingAngelsMinigame; yield return null; }
            if (game == null || game.Phase != MinigamePhase.Round) { fail("angels round timeout"); yield break; }
            var runners = SessionScoreboard.Current.Players.Where(p => p.Avatar != game.Keeper).Select(p => p.Avatar).ToArray();
            var last = runners.Select(p => p.Position).ToArray();
            // Force the real petrification state; regular server Tick resolves the respawn,
            // and every remote owner must receive the chosen teleport.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                float at = 3 + attempt * 4;
                while (Elapsed(game) < at) yield return null;
                if (NetworkManager.Singleton.IsServer)
                    foreach (var runner in runners) runner.GetComponent<RunnerState>().Tick(true, 100);
                while (Elapsed(game) < at + 2.5f) yield return null;
                for (int i = 0; i < runners.Length; i++)
                {
                    Vector3 point = runners[i].Position;
                    if (new Vector2(point.x, point.z).magnitude < 20 || Vector3.Distance(last[i], point) < 2)
                        fail("runner did not return to a different perimeter point: " + runners[i].name + " " + point);
                    if (runners[i].GetComponent<RunnerState>().Current != RunnerState.Phase.Free)
                        fail("runner remained locked after respawn");
                    for (int j = 0; j < i; j++)
                        if (Vector2.Distance(new Vector2(point.x, point.z), new Vector2(runners[j].Position.x, runners[j].Position.z)) < 1)
                            fail("simultaneous respawns selected the same point");
                    last[i] = point;
                }
                Debug.Log("PLAYTEST_CHECK angels random respawn=" + attempt + " points=" + string.Join(";", last.Select(p => p.ToString())));
            }
            Debug.Log("PLAYTEST_CHECK angels role=" + expected + " respawns=4 runners=" + runners.Length);
        }

        private static float Elapsed(MinigameControllerBase game)
        { game.TryGetRoundTime(out float left, out float total); return total - left; }
    }
}
