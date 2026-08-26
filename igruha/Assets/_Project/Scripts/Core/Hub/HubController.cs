using System.Collections;
using Unity.Netcode;
using UnityEngine;
using TMPro;
using Igruha.Core.Interaction;
using Igruha.Core.Minigame;
using Igruha.Core.Session;

namespace Igruha.Core.Hub
{
    /// <summary>
    /// UI-логика хаба: подсказка взаимодействия и автопрогон.
    ///
    /// Выбор игры сюда больше не входит — он целиком на экране приставки
    /// (<see cref="ConsoleMenu"/>). Раньше здесь жило окно подтверждения у
    /// якоря: подошёл к предмету, нажал E, подтвердил Enter. Предметы-якори
    /// отменены, вход один — телевизор, и подтверждение теперь часть самого
    /// экрана, а не отдельная панель поверх хаба.
    /// </summary>
    public sealed class HubController : MonoBehaviour
    {
        [SerializeField] private MinigameLoader loader;
        [SerializeField] private MinigameCatalog catalog;
        [SerializeField] private TMP_Text promptText;

        [Header("Автопрогон")]
        [Tooltip("Сколько секунд ждать состав перед автозапуском по --autostart")]
        [SerializeField] private float autostartRosterTimeout = 300f;
        [Tooltip("Сколько секунд состав не должен меняться, чтобы считать его собравшимся")]
        [SerializeField] private float autostartSettleSeconds = 3f;

        private PlayerInteractor interactor;

        /// <summary>
        /// Сколько игр очередь автопрогона уже отдала. Статическое, потому что
        /// хаб между мини-играми грузится заново и этот компонент создаётся
        /// новый: без памяти процесс запускал бы первую игру по кругу и цепочка
        /// хаб → игра → хаб → вторая игра не проверялась бы никогда.
        /// </summary>
        private static int autostartCursor;

        /// <summary>Как часто писать в лог, сколько народу уже собралось.</summary>
        private const float AutostartReportSeconds = 10f;

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
        /// Взять игру из очереди <c>--autostart</c> и запустить её самому, не
        /// дожидаясь, пока человек подойдёт к телевизору и включит приставку.
        ///
        /// Нужно ради стенда из восьми процессов: нажать кнопку может только
        /// человек, а проверять надо в том числе то, что человеку не показать —
        /// ротацию на восьмерых, деление очков, дисконнекты. Путь при этом
        /// остаётся настоящим: та же <see cref="MinigameLoader"/>, что и у
        /// приставки, — минуется только экран выбора.
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
                Debug.Log($"{name}: очередь автопрогона кончилась — остаёмся в хабе");
                yield break;
            }

            string wanted = queue[autostartCursor];
            MinigameDefinition game = FindGame(wanted);
            if (game == null)
            {
                Debug.LogError($"{name}: автопрогон не нашёл в каталоге игру со сценой '{wanted}' — " +
                               "проверь MinigameCatalog и имя сцены в MinigameDefinition", this);
                yield break;
            }

            int required = Mathf.Max(1, LaunchArguments.WaitPlayers);
            yield return WaitForRoster(required, wanted);

            // Состав собрался — но персонажи ещё доезжают и падают на пол.
            // Стартовать в этот момент значит раздать роли тем, кто ещё летит.
            yield return new WaitForSeconds(autostartSettleSeconds);

            autostartCursor++;
            int joined = SessionScoreboard.Current != null ? SessionScoreboard.Current.Players.Count : 0;
            Debug.Log($"{name}: автопрогон {autostartCursor}/{queue.Length}: запускаю '{wanted}' на {joined} игроков");
            loader?.Load(game);
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
                    Debug.Log($"{name}: жду состав для '{wanted}': {count}/{required}");
                }

                yield return null;
            }

            Debug.LogWarning($"{name}: состав не собрался за {autostartRosterTimeout:F0} с — " +
                             "стартую с тем, что есть");
        }

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

        /// <summary>Игра по имени сцены — так же, как её зовут в Build Settings.</summary>
        private MinigameDefinition FindGame(string sceneName)
        {
            if (catalog == null)
            {
                return null;
            }

            int index = catalog.IndexOfScene(sceneName);
            return catalog.IsPlayable(index) ? catalog.Get(index) : null;
        }

        private void Update()
        {
            UpdatePrompt();
        }

        /// <summary>
        /// Подсказка взаимодействия. Пока включена приставка, её не показываем:
        /// экран занимает весь кадр, а персонаж всё равно заморожен.
        /// </summary>
        private void UpdatePrompt()
        {
            if (promptText == null)
            {
                return;
            }

            bool menuOpen = ConsoleMenu.Active != null && ConsoleMenu.Active.IsOpen;
            IInteractable target = !menuOpen && interactor != null ? interactor.CurrentInteractable : null;
            string text = target != null ? target.InteractionPrompt : string.Empty;

            if (promptText.text != text)
            {
                promptText.text = text;
            }
        }
    }
}
