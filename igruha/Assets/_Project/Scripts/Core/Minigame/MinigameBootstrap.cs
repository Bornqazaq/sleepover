using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Player;
using Igruha.Core.Scenes;
using Igruha.Core.Session;
using Igruha.Core.Spawning;
using Igruha.Core.UI;

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
        /// <summary>
        /// Меньше двух участников сетевая мини-игра не ждёт, но и не начинает:
        /// начатая в одиночестве, она раздаёт роли на одного и доигрывает пустой
        /// раунд, пока остальные ещё подключаются. Порог именно здесь, а не в
        /// <c>MinigameDefinition.MinPlayers</c>: там указан состав, под который
        /// игра задумана (у «Ангелов» — трое), а это техническая нижняя граница,
        /// ниже которой сеть бессмысленна. Кого пускать в матч — дело лобби (EPIC 3).
        /// </summary>
        private const int MinNetworkPlayers = 2;

        [SerializeField] private PlayerSpawner playerSpawner;
        [SerializeField] private MinigameControllerBase minigame;
        [SerializeField] private MinigameCameraController cameraController;
        [Tooltip("Колесо эмоций сцены. Пусто — Tab в этой мини-игре работать не будет")]
        [SerializeField] private EmoteWheel emoteWheel;
        [Tooltip("Сколько секунд ждать ростер и аватары сетевой сессии")]
        [SerializeField] private float networkRosterTimeout = 180f;

        private bool networkReady;
        private readonly List<SessionPlayer> networkPlayers = new List<SessionPlayer>();

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

                if (!networkReady) yield break;
                if (SessionScoreboard.Current == null)
                {
                    Debug.LogError($"{name}: сетевая сессия не отдала табло — мини-игра не стартует", this);
                    yield break;
                }

                players = networkPlayers;
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

            BindLocalPlayer(players);
            minigame.BindTutorialCamera(cameraController);
            minigame.StartMinigame(players);
        }

        // Ни таймаут, ни временно неполный ростер не разрешают начать раунд.
        // Подтверждение относится к NetworkObject этой загрузки, поэтому старый
        // RPC не может открыть следующую тренировку той же сцены.
        private IEnumerator WaitForNetworkRoster()
        {
            var readiness = minigame.GetComponent<IMinigameSceneReadiness>();
            if (readiness == null)
            {
                Debug.LogError($"{name}: отсутствует сетевой шлюз готовности", this);
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + networkRosterTimeout;
            bool reported = false;
            Debug.Log($"[SceneReady] {gameObject.scene.name}: waiting for placement and all peers");
            while (Time.realtimeSinceStartup < deadline)
            {
                var manager = NetworkManager.Singleton;
                if (manager == null || !manager.IsListening || manager.ShutdownInProgress) yield break;
                if (readiness.PlacementComplete && CollectReadyRoster(readiness.ParticipantIds))
                {
                    if (!reported)
                    {
                        BindLocalPlayer(networkPlayers);
                        readiness.ReportLocalReady();
                        reported = true;
                    }
                    if (readiness.CanStart)
                    {
                        networkReady = true;
                        Debug.Log($"[SceneReady] {gameObject.scene.name}: START participants={networkPlayers.Count}");
                        yield break;
                    }
                }
                yield return null;
            }

            readiness.AbortPreparation("Не удалось дождаться загрузки игроков. Подключитесь к хосту заново.");
        }

        private bool CollectReadyRoster(IReadOnlyList<int> ids)
        {
            networkPlayers.Clear();
            var scoreboard = SessionScoreboard.Current;
            if (scoreboard == null || ids.Count < MinNetworkPlayers) return false;
            for (int i = 0; i < ids.Count; i++)
            {
                SessionPlayer found = null;
                foreach (var player in scoreboard.Players)
                    if (player.Id == ids[i]) { found = player; break; }
                if (found == null || found.Avatar == null) return false;
                networkPlayers.Add(found);
            }
            return true;
        }

        /// <summary>
        /// Навести камеру и колесо эмоций на персонажа этой машины.
        ///
        /// В сети берём только своего: откат к <c>players[0]</c> уводил камеру на
        /// аватар хоста, и клиент оказывался зрителем чужой игры. Тот же откат уже
        /// чинили в хабе — здесь он жил своей копией.
        /// </summary>
        private void BindLocalPlayer(IReadOnlyList<SessionPlayer> players)
        {
            bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            SessionPlayer local = SessionScoreboard.Current?.LocalPlayer;
            PlayerController focus = local?.Avatar;

            if (focus == null)
            {
                if (networked)
                {
                    // Тела нет — значит, подключился посреди матча. Это не
                    // ошибка: персонажа он выберет в хабе, а пока смотрит за
                    // остальными. Чужой аватар всё равно не подставляем.
                    Debug.Log($"{name}: 👀 своего персонажа в составе нет — досматриваю матч со стороны");
                    minigame.BeginViewing();
                    return;
                }

                focus = players[0].Avatar;
            }

            if (focus == null)
            {
                return;
            }

            if (cameraController != null && minigame.Definition != null)
            {
                cameraController.Apply(minigame.Definition.CameraMode, focus.transform);
            }

            BindEmoteWheel(focus);
        }

        /// <summary>
        /// Привязать колесо насмешек к локальному игроку. Без этого Tab жмётся,
        /// но колесо не знает, чьи эмоции показывать.
        ///
        /// Ссылка из инспектора — не единственный путь намеренно. Насмешки
        /// обязаны работать в каждой мини-игре, а не только там, где поле
        /// заполнили руками: в Duck Hunt его не заполнили, и танцы там молча
        /// не работали. Поэтому пустая ссылка — не ошибка, а отсутствие колеса
        /// в сцене — ошибка, и она говорит о себе вслух.
        /// </summary>
        private void BindEmoteWheel(PlayerController focus)
        {
            EmoteWheel wheel = emoteWheel != null ? emoteWheel : EmoteWheel.Current;

            if (wheel == null)
            {
                Debug.LogWarning(
                    $"{name}: в сцене нет колеса эмоций — Tab ничего не откроет. " +
                    "Собери его пунктом меню Igruha/UI/Build Emote Wheel", this);
                return;
            }

            if (focus.TryGetComponent(out PlayerEmoteAbility emotes))
            {
                wheel.BindLocalPlayer(emotes);
            }
        }
    }
}
