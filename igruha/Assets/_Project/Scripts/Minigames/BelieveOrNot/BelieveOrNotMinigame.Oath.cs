using System;
using Igruha.Core.Minigame;
using TMPro;
using UnityEngine;

namespace Igruha.Minigames.BelieveOrNot
{
    public sealed partial class BelieveOrNotMinigame
    {
        private BelieveDuelHud duelHud;
        private int announcedOathRound;
        private int autoplayOathRound;
        private bool oathRevealed;
        private bool oathTruth;

        public BelieveOath Oath => match.Oath;
        public bool OathRevealed => oathRevealed;
        public bool OathTruth => oathRevealed && oathTruth;
        public event Action OathCommitted;
        public event Action LidsOpening;
        public event Action<bool> CardsRevealed;
        public event Action DuelEnded;

        private void InitializeOath()
        {
            var root = new GameObject("DuelHud", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            duelHud = root.AddComponent<BelieveDuelHud>();
            TMP_Text sample = seatHud != null ? seatHud.GetComponentInChildren<TMP_Text>(true) : null;
            var plate = seatHud != null ? seatHud.GetComponentInChildren<UnityEngine.UI.Image>(true) : null;
            var status = Hud != null ? Hud.transform.Find("_HudPlates/StatusPlate") as RectTransform : null;
            duelHud.Initialize(sample != null ? sample.font : TMP_Settings.defaultFontAsset,
                plate != null ? plate.sprite : null, status);
            duelHud.Picked += SubmitOath;
        }

        public void SubmitOath(BelieveOath oath)
        {
            if (PauseScreenBlocked || Stage != BelieveStage.Oath) return;
            if (HasAuthority) HandleOath(LocalPlayerId, match.RoundNumber, oath);
            else network?.SubmitOath(match.RoundNumber, oath);
        }

        public bool HandleOath(int playerId, int round, BelieveOath oath)
        {
            int index = IndexOf(playerId);
            if (!HasAuthority || !Phase.IsGameplay() || Stage != BelieveStage.Oath ||
                stageState == null || !stageState.Running || stageState.StageRemaining <= 0f ||
                round != match.RoundNumber || playerId != match.KnowerPlayerId ||
                index < 0 || !entries[index].Present || match.Oath != BelieveOath.Pending ||
                RoundInterrupted || match.Resolved || !BelieveOathRules.IsClaim(oath)) return false;

            match.Oath = oath;
            PublishMatch();
            stageState.EndStageNow();
            return true;
        }

        private void BeginOathView()
        {
            int seat = SeatOf(match.KnowerPlayerId);
            if (seat >= 0) table.GetBox(seat)?.ClosePeekCrack(config.LidOpenSeconds);
            peekView?.Close();
            ShowLocalRole();
        }

        private void RefreshOathView()
        {
            if (duelHud == null) return;
            bool visible = Stage == BelieveStage.Oath || Stage == BelieveStage.Persuasion ||
                           Stage == BelieveStage.Reveal || Stage == BelieveStage.Reaction;
            duelHud.ShowOath(visible && !match.Cancelled, Stage == BelieveStage.Oath &&
                match.Oath == BelieveOath.Pending && LocalPlayerId == match.KnowerPlayerId,
                ShortPredictionName(match.KnowerPlayerId), match.Oath, oathRevealed, oathTruth);
            if (BelieveOathRules.IsClaim(match.Oath) && announcedOathRound != match.RoundNumber &&
                (Stage == BelieveStage.Persuasion || Stage == BelieveStage.Oath))
            {
                announcedOathRound = match.RoundNumber;
                OathCommitted?.Invoke();
            }
        }

        private void RevealOath(BelieveCard seat0Card)
        {
            oathRevealed = BelieveOathRules.IsClaim(match.Oath);
            bool knowerHadWin = (seat0Card == BelieveCard.Win ? 0 : 1) == SeatOf(match.KnowerPlayerId);
            oathTruth = BelieveOathRules.IsTrue(match.Oath, knowerHadWin);
            RefreshOathView();
        }

        private void DriveOathAutoplay()
        {
            if (Stage != BelieveStage.Oath || LocalPlayerId != match.KnowerPlayerId ||
                autoplayOathRound == match.RoundNumber || stageState.StageRemaining > config.OathSeconds - 1f) return;
            autoplayOathRound = match.RoundNumber;
            SubmitOath(UnityEngine.Random.value < .5f ? BelieveOath.Mine : BelieveOath.Yours);
        }
    }
}
