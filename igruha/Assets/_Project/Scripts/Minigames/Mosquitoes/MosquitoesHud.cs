using TMPro;
using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    /// <summary>Role and phase-specific information, drawn from replicated state.</summary>
    public static class MosquitoesHud
    {
        private const string Summary = "СОН {0:0.0} / {1:0} с     КОМАРЫ {2:0}\n";
        private const string FlightControls = "WASD — полёт    Space / Ctrl — высота    Shift — точно    ЛКМ — укус";

        public static void PaintStatus(TMP_Text text, MosquitoesMinigame game, MosquitoBody own, bool eaten)
        {
            string format = StatusFormat(game, own, eaten, out float value, out float total);
            text.SetText(format, game.Sleep.Sleep, game.Config.SleepTarget, game.AliveCount, value, total);
        }

        private static string StatusFormat(MosquitoesMinigame game, MosquitoBody own, bool eaten, out float value, out float total)
        {
            value = total = 0;
            var sleep = game.Sleep;
            if (game.LocalId == game.GiantId)
            {
                if (sleep.Phase == GiantPhase.Sleeping)
                {
                    value = sleep.RecentBites; total = game.AliveCount == 1 ? 1 : game.Config.BitesToWake;
                    if (sleep.ImmunityRemaining > 0) { value = sleep.ImmunityRemaining; return Summary + "ЗАЩИТА {3:0.0} с • СОН ПОКА НЕ РАСТЁТ"; }
                    return sleep.RecentBites > 0 ? Summary + "ТЕБЯ КУСАЮТ • {3:0} / {4:0} • E — проснуться" : Summary + "СПИШЬ • E — проснуться";
                }
                if (sleep.Phase == GiantPhase.Waking) return Summary + "ПРОСЫПАЕШЬСЯ";
                if (sleep.Phase == GiantPhase.LyingDown) return Summary + "УКЛАДЫВАЕШЬСЯ";
                return sleep.LampOn ? Summary + "СВЕТ ВКЛЮЧЁН • E у кровати — спать" : Summary + "ТЕМНО • E у лампы — свет • E у кровати — спать";
            }
            if (own == null) return Summary + "ГОТОВИМСЯ К ПОЛЁТУ";
            if (!own.IsAlive) return eaten ? Summary + "СТРЕКОЗА ПОЙМАЛА ТЕБЯ • наблюдаешь за командой" : Summary + "ТЕБЯ ПРИХЛОПНУЛИ • наблюдаешь за командой";
            if (!own.HasEntered) { value = game.EntryRemaining; return Summary + "ВЛЕТИ В ОКНО • {3:0.0} с"; }
            if (own.IsAttached) { value = own.BiteFraction * 100; return own.BiteFraction > 0 ? Summary + "УКУС {3:0}% • держи ЛКМ" : Summary + "СБЛИЖЕНИЕ • движение — оторваться"; }
            if (own.BiteCooldown > 0) { value = own.BiteCooldown; return Summary + "УКУС ЧЕРЕЗ {3:0.0} с • отлети в укрытие"; }
            if (sleep.Phase == GiantPhase.Waking || sleep.Phase == GiantPhase.Awake) return Summary + "ЧЕЛОВЕК ПРОСНУЛСЯ • прячься от шлепка";
            if (sleep.Phase == GiantPhase.LyingDown) return Summary + "ЧЕЛОВЕК УКЛАДЫВАЕТСЯ";
            if (sleep.ImmunityRemaining > 0) { value = sleep.ImmunityRemaining; return Summary + "ЗАЩИТА ОТ УКУСОВ • {3:0.0} с"; }
            if (game.BiteAccess(own, own.Position) == MosquitoBiteAccess.Blocked) return Summary + "ПУТЬ ПЕРЕКРЫТ • облети препятствие";
            if (game.BiteAccess(own, own.Position) == MosquitoBiteAccess.Ready) return Summary + "ДЕРЖИ ЛКМ • движение — оторваться";
            if (game.AliveCount == 1) return Summary + "ТЫ ПОСЛЕДНИЙ • укус длиной 1 с";
            if (sleep.RecentBites > 0) { value = sleep.BiteWindowRemaining; return Summary + "ЕЩЁ ОДИН УКУС! • осталось {3:0.0} с"; }
            return Summary + "ПОДЛЕТИ К МЕТКЕ • два укуса будят человека";
        }
        public static string Instructions(MosquitoesMinigame game, MosquitoBody own)
        {
            if (game.LocalId == game.GiantId) return "WASD — ходить    E — кровать / лампа    ЛКМ — шлепок";
            if (own != null && !own.IsAlive) return "ТВОЯ КОМАНДА ЕЩЁ ИГРАЕТ";
            return FlightControls;
        }
    }
}
