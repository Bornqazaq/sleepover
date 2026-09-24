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

        /// <summary>
        /// Сколько ждать события «сцену догрузили все», прежде чем начинать
        /// без него. Потерянное событие не должно стоить раунда.
        /// </summary>
        private const float PlacementGateTimeout = 20f;

        [SerializeField] private PlayerSpawner playerSpawner;
        [SerializeField] private MinigameControllerBase minigame;
        [SerializeField] private MinigameCameraController cameraController;
        [Tooltip("Колесо эмоций сцены. Пусто — Tab в этой мини-игре работать не будет")]
        [SerializeField] private EmoteWheel emoteWheel;
        [Tooltip("Сколько секунд ждать ростер и аватары сетевой сессии")]
        [SerializeField] private float networkRosterTimeout = 180f;
        [Tooltip("Сколько секунд состав не должен меняться, чтобы считать его собравшимся")]
        [SerializeField] private float networkRosterSettleTime = 1f;

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

            BindLocalPlayer(players);
            minigame.BindTutorialCamera(cameraController);
            minigame.StartMinigame(players);
        }

        /// <summary>
        /// Ждём, пока сервер пришлёт ростер и заспавнит персонажей: до этого
        /// у участников нет аватаров, и роли раздать некому.
        ///
        /// Мало дождаться непустого состава — надо дождаться, пока он перестанет
        /// расти. Хост загружает сцену мини-игры сразу, как поднялся сервер, и
        /// в этот момент в ростере он один: без выдержки мини-игра стартует на
        /// одного, раздаёт роли на одного, а подключившийся следом клиент
        /// приезжает в уже идущий раунд, где его нет ни в списке, ни в ролях.
        /// Замерено 16.08 на host + client: у обоих в мини-игре был один
        /// участник при ростере из двух.
        /// </summary>
        private IEnumerator WaitForNetworkRoster()
        {
            float deadline = Time.realtimeSinceStartup + networkRosterTimeout;

            // Сначала шлюз расстановки: сцену обязаны догрузить все, и общая
            // раскладка по точкам спавна обязана пройти ДО того, как игра
            // начнёт раздавать свои места. Иначе общий телепорт прилетает
            // следом и выдёргивает людей из клеток и кресел — разбор в
            // ScenePlacementGate.
            // Ждём шлюз недолго и отдельно от общего срока: событие загрузки
            // приходит за секунды, а если оно потерялось вовсе, лучше начать
            // игру с опозданием, чем не начать её три минуты.
            float gateDeadline = Mathf.Min(deadline, Time.realtimeSinceStartup + PlacementGateTimeout);
            while (!ScenePlacementGate.IsOpen && Time.realtimeSinceStartup < gateDeadline)
            {
                yield return null;
            }

            if (!ScenePlacementGate.IsOpen)
            {
                Debug.LogWarning($"{name}: ⏳ событие загрузки сцены не пришло за {PlacementGateTimeout:F0} с — " +
                                 "начинаю без общей раскладки по точкам спавна", this);
            }

            // Шлюз открыло настоящее сетевое событие — значит в сцене уже все,
            // и состав вырасти больше не может. Выдержка на «устаканивание»
            // здесь только добавила бы секунду свободного падения с точки
            // спавна до места, которое игра отведёт человеку сама.
            float settleTime = ScenePlacementGate.OpenedByNetwork ? 0f : networkRosterSettleTime;
            int settledCount = 0;
            float settledSince = 0f;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (!RosterReady(out int count))
                {
                    settledCount = 0;
                }
                else if (count != settledCount)
                {
                    settledCount = count;
                    settledSince = Time.realtimeSinceStartup;
                }
                else if (Time.realtimeSinceStartup - settledSince >= settleTime)
                {
                    yield break;
                }

                yield return null;
            }

            int joined = SessionScoreboard.Current != null ? SessionScoreboard.Current.Players.Count : 0;
            Debug.LogWarning($"{name}: ⏳ ростер сессии не собрался за {networkRosterTimeout:F0} с — стартуем с тем, " +
                             $"что есть ({joined}). Если участников меньше двух, ролей не будет и фонарь не загорится", this);
        }

        /// <summary>Состав готов: играть есть с кем и у всех уже есть персонажи.</summary>
        private static bool RosterReady(out int count)
        {
            count = 0;

            ISessionScoreboard scoreboard = SessionScoreboard.Current;
            if (scoreboard == null)
            {
                return false;
            }

            IReadOnlyList<SessionPlayer> players = scoreboard.Players;
            if (players.Count < MinNetworkPlayers)
            {
                return false;
            }

            ICharacterSelection selection = CharacterSelection.Current;

            for (int i = 0; i < players.Count; i++)
            {
                // Подключившийся посреди матча тела не получит вовсе — он
                // зритель до возвращения в хаб. Ждать его аватар значит ждать
                // до самого таймаута и держать раунд у всех остальных.
                if (selection != null && !selection.HasCharacter(players[i].Id))
                {
                    continue;
                }

                if (players[i].Avatar == null)
                {
                    return false;
                }
            }

            count = players.Count;
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
