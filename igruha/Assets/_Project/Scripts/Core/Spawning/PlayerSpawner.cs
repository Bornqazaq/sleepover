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

            SessionManager session = SessionManager.Instance;
            session?.ClearPlayers();
            var used = new HashSet<int>();

            int playerCount = LocalPartyProfile.PlayerCount > 0 ? LocalPartyProfile.PlayerCount : debugPlayerCount;
            for (int i = 0; i < playerCount; i++)
            {
                SpawnPoint point = spawnPoints.GetSpreadPoint(defaultRole, i, playerCount);
                if (point == null)
                {
                    break;
                }

                bool isHuman = i == 0;
                int chosen = LocalPartyProfile.CharacterOf(i);
                if (chosen < 0 || chosen >= roster.Characters.Count || !roster.Characters[chosen].IsAvailable || used.Contains(chosen))
                {
                    chosen = -1;
                    if (isHuman) for (int k = 0; k < roster.Characters.Count; k++)
                        if (roster.Characters[k] == humanCharacter) chosen = k;
                    if (chosen < 0) for (int k = 0; k < roster.Characters.Count; k++)
                        if (roster.Characters[k].IsAvailable && !used.Contains(k)) { chosen = k; break; }
                }
                if (chosen < 0) break;
                used.Add(chosen);
                GameObject prefabToSpawn = roster.Characters[chosen].Prefab;

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

                LocalPartyProfile.Restore(player);
                player.CharacterIndex = chosen;
                LocalPartyProfile.Save(player);
                spawned.Add(player);
                session?.RegisterPlayer(player);
            }

            SceneCameraGuard.ValidateSingleActiveCameraAndListener();

            return spawned;
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
