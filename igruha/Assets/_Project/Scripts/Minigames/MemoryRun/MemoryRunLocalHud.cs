using System.Text;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.UI;

namespace Igruha.Minigames.MemoryRun
{
    /// <summary>
    /// Строка состояния «Рейса на память»: чей ход, сколько у него осталось,
    /// кто следующий и сколько попыток осталось лично у тебя.
    ///
    /// <b>Отдельный компонент, а не пара строк в контроллере.</b> Контроллер
    /// в фазе 3 живёт под <c>IsServer</c>, и всё, что он рисует, у клиентов
    /// не появится вовсе. HUD только читает состояние, поэтому одинаково
    /// работает и у сервера, и у клиента.
    /// </summary>
    /// <remarks>
    /// <b>Чего здесь нет намеренно:</b> подсветки безопасных плит, истории
    /// пройденного и номера текущего шага. Сбитый счёт шагов — часть игры,
    /// а не неудобство, которое надо исправить интерфейсом.
    ///
    /// Чужие счётчики попыток тоже не показываются: своё видит каждый, чужое
    /// не видит никто.
    /// </remarks>
    public sealed class MemoryRunLocalHud : MonoBehaviour
    {
        /// <summary>Сколько следующих участников показывать в очереди.</summary>
        private const int QueuePreview = 2;

        [SerializeField] private MemoryRunMinigame game;
        [SerializeField] private RoundHud hud;

        private readonly StringBuilder builder = new StringBuilder(128);
        private int shownSeconds = -1;
        private int shownWalker = TurnQueue.NoPlayer;
        private int shownDeaths = -1;
        private int localPlayerId = TurnQueue.NoPlayer;

        private void Update()
        {
            if (game == null || hud == null)
            {
                return;
            }

            if (game.Phase != MinigamePhase.Round)
            {
                return;
            }

            int walkerId = game.CurrentWalkerId;
            if (walkerId == TurnQueue.NoPlayer)
            {
                return;
            }

            if (localPlayerId == TurnQueue.NoPlayer)
            {
                localPlayerId = FindLocalPlayerId();
            }

            int seconds = game.TurnArmed ? Mathf.CeilToInt(game.TurnSecondsLeft) : 0;
            int deaths = localPlayerId != TurnQueue.NoPlayer ? game.DeathsOf(localPlayerId) : 0;

            // Строка пересобирается только когда изменилась. Собирать её каждый
            // кадр значит аллоцировать строку шестьдесят раз в секунду на ровном
            // месте — а это как раз то, чего правила проекта не разрешают.
            if (seconds == shownSeconds && walkerId == shownWalker && deaths == shownDeaths)
            {
                return;
            }

            shownSeconds = seconds;
            shownWalker = walkerId;
            shownDeaths = deaths;

            builder.Clear();
            builder.Append("Ход: ").Append(game.DisplayNameOf(walkerId));

            if (game.TurnArmed)
            {
                builder.Append(" — ").Append(seconds).Append(" с");
            }

            AppendQueue(walkerId);

            if (localPlayerId != TurnQueue.NoPlayer)
            {
                builder.Append("\nПопыток: ").Append(deaths).Append(" / ").Append(game.DeathLimit);
            }

            hud.ShowStatus(builder.ToString());
        }

        /// <summary>Кто ходит следующим. Очередь объявлена всем, скрывать нечего.</summary>
        private void AppendQueue(int walkerId)
        {
            var order = game.TurnOrder;
            if (order == null || order.Count <= 1)
            {
                return;
            }

            int current = -1;
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i] == walkerId)
                {
                    current = i;
                    break;
                }
            }

            if (current < 0)
            {
                return;
            }

            int shown = 0;
            for (int offset = 1; offset < order.Count && shown < QueuePreview; offset++)
            {
                int id = order[(current + offset) % order.Count];
                builder.Append(shown == 0 ? "\nДальше: " : ", ").Append(game.DisplayNameOf(id));
                shown++;
            }
        }

        /// <summary>
        /// Кто из участников — эта машина. Ищется один раз и кэшируется:
        /// персонажи спавнятся раньше, чем начинается раунд, и меняться
        /// владелец уже не будет.
        /// </summary>
        private int FindLocalPlayerId()
        {
            var order = game.TurnOrder;
            if (order == null)
            {
                return TurnQueue.NoPlayer;
            }

            foreach (PlayerInputReader reader in
                     FindObjectsByType<PlayerInputReader>(FindObjectsSortMode.None))
            {
                if (!reader.LocallyControlled)
                {
                    continue;
                }

                var avatar = reader.GetComponent<PlayerController>();
                if (avatar == null)
                {
                    continue;
                }

                for (int i = 0; i < order.Count; i++)
                {
                    if (ReferenceEquals(game.AvatarOf(order[i]), avatar))
                    {
                        return order[i];
                    }
                }
            }

            return TurnQueue.NoPlayer;
        }
    }
}
