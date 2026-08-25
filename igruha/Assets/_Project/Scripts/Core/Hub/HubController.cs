using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using Igruha.Core.Interaction;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Core.Hub
{
    /// <summary>
    /// UI-логика хаба: подсказка у якоря, окно подтверждения и запуск мини-игры.
    /// MVP-переход без кинематографичной камеры (ROADMAP 5.6 — позже).
    /// </summary>
    public sealed class HubController : MonoBehaviour
    {
        [SerializeField] private MinigameLoader loader;
        [SerializeField] private TMP_Text promptText;
        [SerializeField] private GameObject confirmPanel;
        [SerializeField] private TMP_Text confirmText;

        [Header("Автопрогон")]
        [Tooltip("Сколько секунд ждать состав перед автозапуском по --autostart")]
        [SerializeField] private float autostartRosterTimeout = 300f;
        [Tooltip("Сколько секунд состав не должен меняться, чтобы считать его собравшимся")]
        [SerializeField] private float autostartSettleSeconds = 3f;

        private PlayerInteractor interactor;
        private MinigameAnchor[] anchors;
        private MinigameAnchor pendingAnchor;
        private bool confirmOpenedThisFrame;

        /// <summary>
        /// Сколько игр очередь автопрогона уже отдала. Статическое, потому что
        /// хаб между мини-играми грузится заново и этот компонент создаётся
        /// новый: без памяти процесс запускал бы первую игру по кругу и цепочка
        /// хаб → игра → хаб → вторая игра не проверялась бы никогда.
        /// </summary>
        private static int autostartCursor;

        private void Awake()
        {
            anchors = FindObjectsByType<MinigameAnchor>(FindObjectsSortMode.None);
            for (int i = 0; i < anchors.Length; i++)
            {
                anchors[i].Activated += OnAnchorActivated;
            }

            if (confirmPanel != null)
            {
                confirmPanel.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (anchors == null)
            {
                return;
            }

            for (int i = 0; i < anchors.Length; i++)
            {
                if (anchors[i] != null)
                {
                    anchors[i].Activated -= OnAnchorActivated;
                }
            }
        }

        /// <summary>Связать с персонажем локального игрока (вызывает HubBootstrap после спавна).</summary>
        public void BindLocalPlayer(PlayerInteractor localInteractor)
        {
            interactor = localInteractor;
        }

        private void Start()
        {
            if (LaunchArguments.TryGetAutostart(out string[] queue))
            {
                StartCoroutine(Autostart(queue));
            }
        }

        /// <summary>
        /// Взять игру из очереди <c>--autostart</c> и запустить её самому,
        /// не дожидаясь, пока живой человек подойдёт к якорю и нажмёт E.
        ///
        /// Нужно ради стенда из восьми процессов: нажать E может только
        /// человек, а проверять надо в том числе то, что человеку не показать —
        /// ротацию на восьмерых, деление очков, дисконнекты. Путь при этом
        /// остаётся настоящим: та же <see cref="MinigameLoader"/>, что и
        /// у якоря, — минуется только подтверждение в UI.
        /// </summary>
        private IEnumerator Autostart(string[] queue)
        {
            // Сцену рассылает сервер. Клиент, запущенный с тем же аргументом,
            // молча ждёт: иначе он попробовал бы грузить игру сам и получил
            // отказ от NGO в каждой катке.
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsListening && !network.IsServer)
            {
                yield break;
            }

            if (autostartCursor >= queue.Length)
            {
                Debug.Log($"{name}: 🤖 очередь автопрогона кончилась — остаёмся в хабе");
                yield break;
            }

            string wanted = queue[autostartCursor];
            MinigameAnchor anchor = FindAnchor(wanted);
            if (anchor == null)
            {
                Debug.LogError($"{name}: 🤖 автопрогон не нашёл в хабе якорь для '{wanted}' — " +
                               "проверь имя сцены в MinigameDefinition", this);
                yield break;
            }

            int required = Mathf.Max(1, LaunchArguments.WaitPlayers);
            yield return WaitForRoster(required, wanted);

            // Состав собрался — но персонажи ещё доезжают и падают на пол.
            // Стартовать в этот момент значит раздать роли тем, кто ещё летит.
            yield return new WaitForSeconds(autostartSettleSeconds);

            autostartCursor++;
            int joined = SessionScoreboard.Current != null ? SessionScoreboard.Current.Players.Count : 0;
            Debug.Log($"{name}: 🤖 автопрогон {autostartCursor}/{queue.Length}: запускаю '{wanted}' на {joined} игроков");
            StartMinigame(anchor);
        }

        /// <summary>Дождаться, пока в составе наберётся нужное число участников и у всех появятся персонажи.</summary>
        private IEnumerator WaitForRoster(int required, string wanted)
        {
            float deadline = Time.realtimeSinceStartup + autostartRosterTimeout;
            float nextReport = 0f;

            while (Time.realtimeSinceStartup < deadline)
            {
                ISessionScoreboard scoreboard = SessionScoreboard.Current;
                int count = scoreboard != null ? scoreboard.Players.Count : 0;

                if (count >= required && AllHaveAvatars(scoreboard))
                {
                    yield break;
                }

                if (Time.realtimeSinceStartup >= nextReport)
                {
                    nextReport = Time.realtimeSinceStartup + AutostartReportSeconds;
                    Debug.Log($"{name}: 🤖 жду состав для '{wanted}': {count}/{required}");
                }

                yield return null;
            }

            Debug.LogWarning($"{name}: 🤖 состав не собрался за {autostartRosterTimeout:F0} с — " +
                             "стартую с тем, что есть");
        }

        /// <summary>Как часто писать в лог, сколько народу уже собралось.</summary>
        private const float AutostartReportSeconds = 10f;

        private static bool AllHaveAvatars(ISessionScoreboard scoreboard)
        {
            if (scoreboard == null || scoreboard.Players.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < scoreboard.Players.Count; i++)
            {
                if (scoreboard.Players[i].Avatar == null)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Якорь по имени сцены мини-игры — так же, как её зовут в Build Settings.</summary>
        private MinigameAnchor FindAnchor(string sceneName)
        {
            if (anchors == null)
            {
                return null;
            }

            for (int i = 0; i < anchors.Length; i++)
            {
                MinigameAnchor candidate = anchors[i];
                if (candidate != null && candidate.IsPlayable &&
                    string.Equals(candidate.Definition.SceneName, sceneName,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        private void Update()
        {
            if (pendingAnchor != null)
            {
                UpdateConfirmation();
                return;
            }

            UpdatePrompt();
        }

        private void UpdatePrompt()
        {
            if (promptText == null)
            {
                return;
            }

            IInteractable target = interactor != null ? interactor.CurrentInteractable : null;
            string text = target != null ? target.InteractionPrompt : string.Empty;
            if (promptText.text != text)
            {
                promptText.text = text;
            }
        }

        private void OnAnchorActivated(MinigameAnchor anchor, PlayerController player)
        {
            if (pendingAnchor != null)
            {
                return;
            }

            // Взаимодействие исполняет сервер, поэтому сюда прилетают и чужие нажатия.
            // Окно подтверждения — сугубо местный UI: открываем его только на нажатие
            // хозяина этой машины, иначе хосту вылезала бы панель, когда кнопку жмёт
            // кто-то другой, а сам нажавший не видел бы ничего.
            if (!IsLocalPlayer(player))
            {
                return;
            }

            if (!anchor.IsPlayable)
            {
                if (promptText != null)
                {
                    promptText.text = $"{anchor.InteractionPrompt} (игра ещё не готова)";
                }

                return;
            }

            pendingAnchor = anchor;
            confirmOpenedThisFrame = true;

            if (confirmText != null)
            {
                confirmText.text = $"Запустить «{anchor.Definition.DisplayName}»?\n\nEnter — да     Esc — отмена";
            }

            if (confirmPanel != null)
            {
                confirmPanel.SetActive(true);
            }
        }

        /// <summary>
        /// Персонаж этой машины. До привязки (HubBootstrap ещё не отдал игрока)
        /// считаем нажатие своим: одиночные сцены хаба живут без сети и без привязки.
        /// </summary>
        private bool IsLocalPlayer(PlayerController player)
        {
            if (interactor == null)
            {
                return true;
            }

            return player != null && player.gameObject == interactor.gameObject;
        }

        private void UpdateConfirmation()
        {
            // Кадр открытия пропускаем: та же кнопка не должна сразу подтвердить.
            if (confirmOpenedThisFrame)
            {
                confirmOpenedThisFrame = false;
                return;
            }

            Keyboard keyboard = Keyboard.current;
            bool confirmed = keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
            bool cancelled = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                confirmed |= gamepad.buttonSouth.wasPressedThisFrame;
                cancelled |= gamepad.buttonEast.wasPressedThisFrame;
            }

            if (confirmed)
            {
                MinigameAnchor anchor = pendingAnchor;
                CloseConfirmation();
                StartMinigame(anchor);
                return;
            }

            if (cancelled)
            {
                CloseConfirmation();
            }
        }

        private void StartMinigame(MinigameAnchor anchor)
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsListening && !network.IsServer)
            {
                Debug.Log($"{name}: мини-игру запускает только хост");
                return;
            }

            loader?.Load(anchor.Definition);
        }

        private void CloseConfirmation()
        {
            pendingAnchor = null;
            if (confirmPanel != null)
            {
                confirmPanel.SetActive(false);
            }
        }
    }
}
