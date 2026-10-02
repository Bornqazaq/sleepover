using System.Text;
using Igruha.Core.Minigame;
using TMPro;
using UnityEngine;

namespace Igruha.Minigames.BelieveOrNot
{
    public sealed partial class BelieveOrNotMinigame
    {
        private readonly BelievePredictions predictions = new BelievePredictions();
        private BelievePredictionPanel predictionPanel;
        private BelievePredictionResults predictionResults;
        private bool predictionBoxesOpened;
        private int predictionShownRound;
        private int localPrediction = -1;
        private int predictionSentRound;
        private const int PredictionNameLength = 12;

        public int PredictionRound => match.RoundNumber;

        public BelievePredictionResults PredictionResults => predictionResults;

        private void InitializePredictions()
        {
            var root = new GameObject("SpectatorPredictions", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            predictionPanel = root.AddComponent<BelievePredictionPanel>();
            TMP_Text sample = seatHud != null ? seatHud.GetComponentInChildren<TMP_Text>(true) : null;
            predictionPanel.Initialize(sample != null ? sample.font : TMP_Settings.defaultFontAsset);
            predictionPanel.Picked += SubmitPrediction;
        }

        private void ResetPredictions()
        {
            predictions.Reset(match.RoundNumber, match.Seat0PlayerId, match.Seat1PlayerId);
            predictionResults = default;
            predictionBoxesOpened = false;
            predictionShownRound = 0;
            localPrediction = -1;
            predictionSentRound = 0;
            predictionPanel?.Close();
            if (HasAuthority) network?.PublishPredictions(default);
        }

        private void OpenPredictions()
        {
            if (SeatOf(LocalPlayerId) >= 0 || IndexOf(LocalPlayerId) < 0) return;
            predictionPanel?.Open(match.RoundNumber, match.Seat0PlayerId, ShortPredictionName(match.Seat0PlayerId),
                match.Seat1PlayerId, ShortPredictionName(match.Seat1PlayerId));
            if (localPrediction >= 0) predictionPanel?.Confirm(ShortPredictionName(localPrediction));
        }

        public void SubmitPrediction(int round, int winnerId)
        {
            if (PauseScreenBlocked || Stage != BelieveStage.Persuasion) return;
            predictionSentRound = round;
            if (HasAuthority)
            {
                HandlePrediction(LocalPlayerId, round, winnerId);
                ApplyPredictionReceipt(round, predictions.ChoiceOf(LocalPlayerId));
            }
            else network?.SubmitPrediction(round, winnerId);
        }

        private bool PauseScreenBlocked => Igruha.Core.UI.PauseScreen.Current?.IsPaused == true;

        /// <summary>Вызывается сервером с настоящим отправителем RPC, никогда с присланным ID игрока.</summary>
        public bool HandlePrediction(int playerId, int round, int winnerId)
        {
            if (!HasAuthority) return false;
            bool accepting = Phase.IsGameplay() && Stage == BelieveStage.Persuasion &&
                             stageState.Running && stageState.StageRemaining > 0f &&
                             !match.Resolved && !match.Cancelled && match.Decision == Decision.None;
            return predictions.TryPick(round, playerId, winnerId, accepting, entries);
        }

        public int PredictionOf(int playerId) => HasAuthority ? predictions.ChoiceOf(playerId) : -1;

        public void ApplyPredictionReceipt(int round, int winnerId)
        {
            if (round != match.RoundNumber || Stage != BelieveStage.Persuasion || match.Cancelled) return;
            if (winnerId >= 0)
            {
                localPrediction = winnerId;
                predictionPanel?.Confirm(ShortPredictionName(winnerId));
            }
            else predictionPanel?.Lock();
        }

        private void PredictionBoxesOpened(int winnerId)
        {
            predictionBoxesOpened = true;
            if (HasAuthority)
            {
                predictionResults = predictions.Reveal(winnerId);
                network?.PublishPredictions(predictionResults);
            }
            TryShowPredictionResults();
        }

        public void ApplyPredictionResults(in BelievePredictionResults state)
        {
            if (HasAuthority) return;
            predictionResults = state;
            TryShowPredictionResults();
        }

        private void TryShowPredictionResults()
        {
            if (!predictionBoxesOpened || predictionResults.Round != match.RoundNumber || match.Cancelled ||
                predictionShownRound == match.RoundNumber || CountPresent() <= BelieveTable.SeatCount ||
                (Stage != BelieveStage.Reveal && Stage != BelieveStage.Reaction)) return;
            predictionShownRound = match.RoundNumber;
            var text = new StringBuilder();
            int correct = 0;
            for (int i = 0; i < predictionResults.Picks.Length; i++)
            {
                BelievePrediction pick = predictionResults.Picks[i];
                bool hit = pick.WinnerId == predictionResults.WinnerId;
                if (hit) correct++;
                if (i > 0) text.Append('\n');
                text.Append(hit ? "+ " : "- ").Append(ShortPredictionName(pick.PlayerId))
                    .Append(" → ").Append(ShortPredictionName(pick.WinnerId));
            }
            int count = predictionResults.Picks.Length;
            predictionPanel?.ShowResults(count == 0 ? "ПРОГНОЗЫ ЗРИТЕЛЕЙ" : $"УГАДАЛИ: {correct} ИЗ {count}",
                count == 0 ? "В этом коне прогнозов не было" : text.ToString(), count);
        }

        private string ShortPredictionName(int playerId)
        {
            string playerName = NameOf(playerId).Replace('\n', ' ').Replace('\r', ' ');
            return playerName.Length <= PredictionNameLength ? playerName : playerName.Substring(0, PredictionNameLength - 1) + "…";
        }
    }
}
