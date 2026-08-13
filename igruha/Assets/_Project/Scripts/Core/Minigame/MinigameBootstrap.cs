using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.Spawning;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Точка входа сцены мини-игры. В локальном режиме сама спавнит толпу,
    /// в сетевом — только ждёт ростер: персонажей создаёт сервер при
    /// подключении. Дальше настраивает камеру на своего игрока и стартует
    /// мини-игру; фазы и очки уже дело контроллера и табло сессии.
    /// </summary>
    public sealed class MinigameBootstrap : MonoBehaviour
    {
        [SerializeField] private PlayerSpawner playerSpawner;
        [SerializeField] private MinigameControllerBase minigame;
        [SerializeField] private MinigameCameraController cameraController;
        [Tooltip("Сколько секунд ждать ростер и аватары сетевой сессии")]
        [SerializeField] private float networkRosterTimeout = 15f;

        private void Start()
        {
            StartCoroutine(Boot());
        }

        private IEnumerator Boot()
        {
            if (minigame == null)
            {
                Debug.LogError($"{name}: MinigameBootstrap не настроен (minigame)", this);
                yield break;
            }

            var bridge = minigame.GetComponent<IMinigameNetworkBridge>();
            bool networked = bridge != null && bridge.IsNetworkSession;

            IReadOnlyList<SessionPlayer> players;
            if (networked)
            {
                yield return WaitForNetworkRoster();

                if (SessionScoreboard.Current == null)
                {
                    Debug.LogError($"{name}: сетевая сессия не отдала табло — мини-игра не стартует", this);
                    yield break;
                }

                players = SessionScoreboard.Current.Players;
            }
            else
            {
                if (playerSpawner == null)
                {
                    Debug.LogError($"{name}: MinigameBootstrap не настроен (playerSpawner)", this);
                    yield break;
                }

                players = playerSpawner.SpawnPlayers();
            }

            if (players.Count == 0)
            {
                Debug.LogError($"{name}: нет игроков — мини-игра не стартует", this);
                yield break;
            }

            FocusCamera(players);
            minigame.StartMinigame(players);
        }

        /// <summary>
        /// Ждём, пока сервер пришлёт ростер и заспавнит персонажей: до этого
        /// у участников нет аватаров, и роли раздать некому.
        /// </summary>
        private IEnumerator WaitForNetworkRoster()
        {
            float deadline = Time.realtimeSinceStartup + networkRosterTimeout;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (RosterReady())
                {
                    yield break;
                }

                yield return null;
            }

            Debug.LogWarning($"{name}: ростер сессии не собрался за {networkRosterTimeout:F0} с — стартуем с тем, что есть", this);
        }

        private static bool RosterReady()
        {
            ISessionScoreboard scoreboard = SessionScoreboard.Current;
            if (scoreboard == null || scoreboard.Players.Count == 0)
            {
                return false;
            }

            IReadOnlyList<SessionPlayer> players = scoreboard.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Avatar == null)
                {
                    return false;
                }
            }

            return true;
        }

        private void FocusCamera(IReadOnlyList<SessionPlayer> players)
        {
            if (cameraController == null || minigame.Definition == null)
            {
                return;
            }

            SessionPlayer local = SessionScoreboard.Current?.LocalPlayer;
            PlayerController focus = local?.Avatar != null ? local.Avatar : players[0].Avatar;
            if (focus == null)
            {
                return;
            }

            cameraController.Apply(minigame.Definition.CameraMode, focus.transform);
        }
    }
}
