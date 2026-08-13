using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Interaction;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.Spawning;

namespace Igruha.Core.Hub
{
    /// <summary>
    /// Запуск хаба: в одиночку спавнит персонажей, в сети ждёт ростер
    /// (персонажей уже создал сервер) и вешает камеру на своего игрока.
    /// </summary>
    public sealed class HubBootstrap : MonoBehaviour
    {
        [SerializeField] private PlayerSpawner playerSpawner;
        [SerializeField] private HubController hubController;
        [SerializeField] private MinigameCameraController cameraController;
        [SerializeField] private float networkRosterTimeout = 15f;

        private void Start()
        {
            StartCoroutine(Boot());
        }

        private IEnumerator Boot()
        {
            bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            IReadOnlyList<SessionPlayer> players;

            if (networked)
            {
                yield return WaitForNetworkRoster();
                ISessionScoreboard scoreboard = SessionScoreboard.Current;
                if (scoreboard == null || scoreboard.Players.Count == 0)
                {
                    Debug.LogError($"{name}: сетевая сессия не отдала табло — хаб не стартует", this);
                    yield break;
                }

                players = scoreboard.Players;
            }
            else
            {
                if (playerSpawner == null)
                {
                    Debug.LogError($"{name}: HubBootstrap без PlayerSpawner", this);
                    yield break;
                }

                players = playerSpawner.SpawnPlayers();
            }

            if (players.Count == 0)
            {
                yield break;
            }

            BindLocalPlayer(players);
        }

        private IEnumerator WaitForNetworkRoster()
        {
            float deadline = Time.realtimeSinceStartup + networkRosterTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                ISessionScoreboard scoreboard = SessionScoreboard.Current;
                if (scoreboard != null && scoreboard.Players.Count > 0 && AllHaveAvatars(scoreboard.Players))
                {
                    yield break;
                }

                yield return null;
            }

            Debug.LogWarning($"{name}: ростер не собрался за {networkRosterTimeout:F0} с — стартуем с тем, что есть", this);
        }

        private static bool AllHaveAvatars(IReadOnlyList<SessionPlayer> players)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Avatar == null)
                {
                    return false;
                }
            }

            return true;
        }

        private void BindLocalPlayer(IReadOnlyList<SessionPlayer> players)
        {
            SessionPlayer local = SessionScoreboard.Current?.LocalPlayer;
            PlayerController avatar = local?.Avatar != null ? local.Avatar : players[0].Avatar;
            if (avatar == null)
            {
                return;
            }

            cameraController?.Apply(CameraMode.ThirdPerson, avatar.transform);

            if (hubController != null && avatar.TryGetComponent(out PlayerInteractor interactor))
            {
                hubController.BindLocalPlayer(interactor);
            }
        }
    }
}
