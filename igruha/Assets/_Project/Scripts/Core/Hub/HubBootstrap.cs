using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Interaction;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.Spawning;
using Igruha.Core.UI;

namespace Igruha.Core.Hub
{
    /// <summary>
    /// Запуск хаба: экран выбора персонажа, затем спавн 2–8 персонажей
    /// у лестницы, настройка камеры и привязка UI подсказок к локальному игроку.
    /// </summary>
    public sealed class HubBootstrap : MonoBehaviour
    {
        [SerializeField] private PlayerSpawner playerSpawner;
        [SerializeField] private HubController hubController;
        [SerializeField] private MinigameCameraController cameraController;
        [SerializeField] private CharacterSelectScreen characterSelect;
        [SerializeField] private EmoteWheel emoteWheel;
        [SerializeField] private CharacterRoster roster;

        private void Start()
        {
            if (playerSpawner == null || characterSelect == null || roster == null)
            {
                Debug.LogError($"{name}: HubBootstrap не настроен (playerSpawner/characterSelect/roster)", this);
                return;
            }

            characterSelect.Show(roster, OnCharacterChosen);
        }

        private void OnCharacterChosen(CharacterDefinition chosen)
        {
            if (chosen == null)
            {
                Debug.LogError($"{name}: персонаж не выбран — спавн отменён", this);
                return;
            }

            IReadOnlyList<SessionPlayer> players = playerSpawner.SpawnPlayers(chosen);
            if (players.Count == 0)
            {
                return;
            }

            // Живой игрок — всегда первый в списке (см. PlayerSpawner).
            PlayerController localPlayer = players[0].Avatar;
            if (localPlayer == null)
            {
                Debug.LogError($"{name}: у префаба «{chosen.DisplayName}» нет PlayerController — камере не за кого цепляться", this);
                return;
            }

            Transform localAvatar = localPlayer.transform;

            if (cameraController != null)
            {
                // CameraTarget — точка на уровне груди, а не корень капсулы: персонажи
                // разного роста иначе кадрируются по-разному (см. PlayerController.CameraTarget).
                cameraController.Apply(CameraMode.ThirdPerson, localPlayer.CameraTarget);
            }
            else
            {
                Debug.LogError($"{name}: не назначен cameraController — камера останется на месте вместо выбранного персонажа", this);
            }

            if (hubController != null && localAvatar.TryGetComponent(out PlayerInteractor interactor))
            {
                hubController.BindLocalPlayer(interactor);
            }

            if (emoteWheel != null && localAvatar.TryGetComponent(out PlayerEmoteAbility emotes))
            {
                emoteWheel.BindLocalPlayer(emotes);
            }
        }
    }
}
