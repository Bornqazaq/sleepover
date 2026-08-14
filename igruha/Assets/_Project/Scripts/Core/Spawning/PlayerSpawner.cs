using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Core.Spawning
{
    /// <summary>
    /// Спавнит персонажей по точкам набора и регистрирует их в SessionManager.
    /// Локальный режим: первый — живой игрок с персонажем, выбранным на экране
    /// выбора, остальные — манекены без ввода со случайным доступным персонажем
    /// из ростера (для теста толчков/ловушек и визуального разнообразия толпы).
    /// При переходе на NGO спавн уйдёт на сервер, точки и роли останутся.
    /// </summary>
    public sealed class PlayerSpawner : MonoBehaviour
    {
        [SerializeField] private CharacterRoster roster;
        [SerializeField] private SpawnPointSet spawnPoints;
        [Tooltip("Сколько персонажей спавнить в локальном тесте (2–8)")]
        [Range(1, 8)]
        [SerializeField] private int debugPlayerCount = 2;
        [Tooltip("Роль точек, на которые спавнится основная толпа")]
        [SerializeField] private SpawnRole defaultRole = SpawnRole.Default;

        private readonly List<SessionPlayer> spawned = new List<SessionPlayer>(8);
        private readonly List<CharacterDefinition> availableCharacters = new List<CharacterDefinition>(8);

        public IReadOnlyList<SessionPlayer> SpawnedPlayers => spawned;

        /// <summary>Спавн с первым доступным персонажем ростера — для точек входа без экрана выбора (например, тестовый запуск мини-игры напрямую).</summary>
        public IReadOnlyList<SessionPlayer> SpawnPlayers() => SpawnPlayers(FindFirstAvailableCharacter());

        /// <summary>Спавн толпы. humanCharacter — выбор с экрана выбора персонажа. Возвращает список участников (первый — живой ввод).</summary>
        public IReadOnlyList<SessionPlayer> SpawnPlayers(CharacterDefinition humanCharacter)
        {
            spawned.Clear();

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                Debug.Log($"{name}: сеть уже запущена — локальный спавн пропущен, персонажей создаёт сервер");
                return spawned;
            }

            if (humanCharacter == null || !humanCharacter.IsAvailable || roster == null || spawnPoints == null)
            {
                Debug.LogError($"{name}: PlayerSpawner не настроен (humanCharacter/roster/spawnPoints)", this);
                return spawned;
            }

            CollectAvailableCharacters();

            SessionManager session = SessionManager.Instance;
            session?.ClearPlayers();

            for (int i = 0; i < debugPlayerCount; i++)
            {
                SpawnPoint point = spawnPoints.GetSpreadPoint(defaultRole, i, debugPlayerCount);
                if (point == null)
                {
                    break;
                }

                bool isHuman = i == 0;
                GameObject prefabToSpawn = isHuman ? humanCharacter.Prefab : PickRandomAvailablePrefab();

                GameObject instance = Instantiate(prefabToSpawn, point.transform.position, point.transform.rotation);
                instance.name = $"Player_{i + 1}";

                if (!isHuman && instance.TryGetComponent(out PlayerInputReader reader))
                {
                    reader.RevokeLocalControl();
                }

                if (instance.TryGetComponent(out PlayerRespawner respawner))
                {
                    respawner.SetRespawnPoint(point.transform);
                }

                var player = new SessionPlayer(i, $"Игрок {i + 1}")
                {
                    Avatar = instance.GetComponent<PlayerController>()
                };

                spawned.Add(player);
                session?.RegisterPlayer(player);
            }

            SceneCameraGuard.ValidateSingleActiveCameraAndListener();

            return spawned;
        }

        /// <summary>Пересобрать список персонажей с назначенным префабом (заглушки ростера пропускаются).</summary>
        private void CollectAvailableCharacters()
        {
            availableCharacters.Clear();
            IReadOnlyList<CharacterDefinition> characters = roster.Characters;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].IsAvailable)
                {
                    availableCharacters.Add(characters[i]);
                }
            }
        }

        private GameObject PickRandomAvailablePrefab()
        {
            return availableCharacters[Random.Range(0, availableCharacters.Count)].Prefab;
        }

        private CharacterDefinition FindFirstAvailableCharacter()
        {
            if (roster == null)
            {
                return null;
            }

            IReadOnlyList<CharacterDefinition> characters = roster.Characters;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].IsAvailable)
                {
                    return characters[i];
                }
            }

            return null;
        }
    }
}
