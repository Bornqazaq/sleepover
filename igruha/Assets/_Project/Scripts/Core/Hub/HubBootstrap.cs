using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Interaction;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Core.Spawning;

namespace Igruha.Core.Hub
{
    /// <summary>
    /// Запуск хаба: спавн 2–8 персонажей у лестницы, настройка камеры и
    /// привязка UI подсказок к локальному игроку.
    /// </summary>
    public sealed class HubBootstrap : MonoBehaviour
    {
        [SerializeField] private PlayerSpawner playerSpawner;
        [SerializeField] private HubController hubController;
        [SerializeField] private MinigameCameraController cameraController;

        private void Start()
        {
            if (playerSpawner == null)
            {
                Debug.LogError($"{name}: HubBootstrap без PlayerSpawner", this);
                return;
            }

            IReadOnlyList<SessionPlayer> players = playerSpawner.SpawnPlayers();
            if (players.Count == 0)
            {
                return;
            }

            Transform localAvatar = players[0].Avatar.transform;
            cameraController?.Apply(CameraMode.ThirdPerson, localAvatar);

            if (hubController != null && localAvatar.TryGetComponent(out PlayerInteractor interactor))
            {
                hubController.BindLocalPlayer(interactor);
            }
        }
    }
}
