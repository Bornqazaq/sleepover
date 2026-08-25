using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>
    /// Отладочный погонщик болванок для соло-прогона.
    ///
    /// Половину правил этой мини-игры одному игроку проверить нечем: за столом
    /// всегда двое, и если второй — неподвижная кукла, кон никогда не кончится
    /// решением, а только по таймеру. Болванка садится, жмёт кнопку и шлёт
    /// реплики; зрители в это время бегают по залу, чтобы было видно, где они
    /// застревают.
    ///
    /// Это инструмент, а не игровой ИИ: болванка не блефует и не читает
    /// соперника. Все её действия идут через те же точки входа
    /// (<see cref="BelieveOrNotMinigame.HandleDecision"/> и
    /// <see cref="BelieveOrNotMinigame.HandlePhrase"/>), что и действия
    /// человека, — поэтому валидация проверяется заодно.
    /// </summary>
    public sealed class BelieveDebugBot : MonoBehaviour
    {
        [SerializeField] private BelieveOrNotMinigame game;
        [SerializeField] private BelieveTable table;

        [Tooltip("Не жать раньше этой секунды уговоров: иначе кон кончается, не начавшись")]
        [SerializeField] private float minDecisionDelay = 4f;

        [Tooltip("Доля фазы уговоров, после которой болванка уже точно решит")]
        [Range(0.2f, 1f)]
        [SerializeField] private float maxDecisionFraction = 0.85f;

        [Tooltip("Пауза между репликами болванки — сверх серверного кулдауна")]
        [SerializeField] private float phraseInterval = 4f;

        [Tooltip("Заставить болванку решать всегда одинаково. None — как обычно, монеткой. " +
                 "Нужно, чтобы прогнать обмен коробок: на монетке он может не выпасть за весь матч")]
        [SerializeField] private Decision forcedDecision = Decision.None;

        [Tooltip("Радиус блуждания зрителей от центра стола, метры")]
        [SerializeField] private float wanderRadius = 6.5f;

        [Tooltip("Как часто зритель выбирает новую точку")]
        [SerializeField] private float wanderInterval = 5f;

        private readonly List<DebugPlayerBot> spectators = new List<DebugPlayerBot>(8);
        private LayerMask obstacles;
        private byte lastStage = MinigameStageState.NoStage;
        private float decisionAt;
        private float nextPhraseAt;
        private float nextWanderAt;
        private bool decisionSent;

        private void Awake()
        {
            obstacles = LayerMask.GetMask("Ground");
        }

        private void Update()
        {
            if (game == null || table == null)
            {
                return;
            }

            byte stage = game.Stage;
            if (stage != lastStage)
            {
                lastStage = stage;
                OnStageChanged(stage);
            }

            if (stage == BelieveStage.Persuasion)
            {
                DriveSeatedBots();
            }

            DriveSpectators();
        }

        private void OnStageChanged(byte stage)
        {
            if (stage != BelieveStage.Persuasion || game.Config == null)
            {
                return;
            }

            decisionSent = false;
            float window = game.Config.PersuasionSeconds * maxDecisionFraction;
            decisionAt = Time.time + Random.Range(Mathf.Min(minDecisionDelay, window), window);
            nextPhraseAt = Time.time + 1f;
        }

        private void DriveSeatedBots()
        {
            if (IsBot(game.KnowerPlayerId) && Time.time >= nextPhraseAt)
            {
                nextPhraseAt = Time.time + phraseInterval;
                game.HandlePhrase(game.KnowerPlayerId, Random.Range(0, 5));
            }

            if (decisionSent || !IsBot(game.DeciderPlayerId) || Time.time < decisionAt)
            {
                return;
            }

            decisionSent = true;
            Decision decision = forcedDecision != Decision.None
                ? forcedDecision
                : (Random.value < 0.5f ? Decision.Keep : Decision.Swap);

            game.HandleDecision(game.DeciderPlayerId, decision);
        }

        /// <summary>
        /// Разогнать зрителей по залу. Нужно не для красоты: именно так
        /// проверяется, что в свободной зоне негде застрять и что барьер стола
        /// держит тело, но пропускает камеру.
        /// </summary>
        private void DriveSpectators()
        {
            if (Time.time < nextWanderAt)
            {
                return;
            }

            nextWanderAt = Time.time + wanderInterval;
            CollectSpectators();

            for (int i = 0; i < spectators.Count; i++)
            {
                Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(wanderRadius * 0.4f, wanderRadius);
                Vector3 point = table.transform.position + new Vector3(offset.x, 0f, offset.y);
                spectators[i].SetTarget(point);
            }
        }

        /// <summary>
        /// Пересобрать список бегающих. Пересобирается редко (раз в
        /// <c>wanderInterval</c>), потому что состав меняется рассадкой:
        /// севший за стол перестаёт быть зрителем.
        /// </summary>
        private void CollectSpectators()
        {
            spectators.Clear();

            IReadOnlyList<SessionPlayer> players = SessionScoreboard.Current?.Players;
            if (players == null)
            {
                return;
            }

            for (int i = 0; i < players.Count; i++)
            {
                PlayerController avatar = players[i].Avatar;
                if (avatar == null || !IsBot(players[i].Id) || avatar.MovementLocked)
                {
                    continue;
                }

                if (!avatar.TryGetComponent(out DebugPlayerBot bot))
                {
                    bot = avatar.gameObject.AddComponent<DebugPlayerBot>();
                    bot.Configure(obstacles);
                }

                spectators.Add(bot);
            }
        }

        /// <summary>Болванка — тот, у кого отнято локальное управление.</summary>
        private static bool IsBot(int playerId)
        {
            SessionPlayer player = SessionScoreboard.Current?.FindPlayer(playerId);
            PlayerController avatar = player?.Avatar;

            return avatar != null
                   && avatar.TryGetComponent(out PlayerInputReader reader)
                   && !reader.LocallyControlled;
        }
    }
}
