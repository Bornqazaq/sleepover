using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Core.Spawning
{
    /// <summary>
    /// Спавнит персонажей по точкам набора и регистрирует их в SessionManager.
    /// Локальный режим: первый — живой игрок, остальные — манекены без ввода
    /// (для теста толчков/ловушек). При переходе на NGO спавн уйдёт на сервер,
    /// точки и роли останутся.
    /// </summary>
    public sealed class PlayerSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private SpawnPointSet spawnPoints;
        [Tooltip("Сколько персонажей спавнить в локальном тесте (2–8)")]
        [Range(1, 8)]
        [SerializeField] private int debugPlayerCount = 2;
        [Tooltip("Роль точек, на которые спавнится основная толпа")]
        [SerializeField] private SpawnRole defaultRole = SpawnRole.Default;

        private readonly List<SessionPlayer> spawned = new List<SessionPlayer>(8);

        public IReadOnlyList<SessionPlayer> SpawnedPlayers => spawned;

        /// <summary>Спавн толпы. Возвращает список участников (первый — живой ввод).</summary>
        public IReadOnlyList<SessionPlayer> SpawnPlayers()
        {
            spawned.Clear();

            if (playerPrefab == null || spawnPoints == null)
            {
                Debug.LogError($"{name}: PlayerSpawner не настроен (prefab/spawnPoints)", this);
                return spawned;
            }

            SessionManager session = SessionManager.Instance;
            session?.ClearPlayers();

            for (int i = 0; i < debugPlayerCount; i++)
            {
                SpawnPoint point = spawnPoints.GetPoint(defaultRole, i);
                if (point == null)
                {
                    break;
                }

                GameObject instance = Instantiate(playerPrefab, point.transform.position, point.transform.rotation);
                instance.name = $"Player_{i + 1}";

                bool isHuman = i == 0;
                if (!isHuman && instance.TryGetComponent(out PlayerInputReader reader))
                {
                    reader.enabled = false;
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

            return spawned;
        }
    }
}
