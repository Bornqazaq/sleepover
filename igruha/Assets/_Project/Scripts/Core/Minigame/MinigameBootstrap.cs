using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Session;
using Igruha.Core.Spawning;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Точка входа сцены мини-игры (локальный запуск без сети):
    /// спавнит игроков, настраивает камеру, стартует мини-игру и привязывает
    /// её результаты к SessionManager. Позже эту роль возьмёт сетевой флоу.
    /// </summary>
    public sealed class MinigameBootstrap : MonoBehaviour
    {
        [SerializeField] private PlayerSpawner playerSpawner;
        [SerializeField] private MinigameControllerBase minigame;
        [SerializeField] private MinigameCameraController cameraController;

        private void Start()
        {
            if (playerSpawner == null || minigame == null)
            {
                Debug.LogError($"{name}: MinigameBootstrap не настроен", this);
                return;
            }

            var players = playerSpawner.SpawnPlayers();
            if (players.Count == 0)
            {
                return;
            }

            if (cameraController != null && minigame.Definition != null)
            {
                cameraController.Apply(minigame.Definition.CameraMode, players[0].Avatar.transform);
            }

            SessionManager.Instance?.AttachMinigame(minigame);
            minigame.StartMinigame(players);
        }
    }
}
