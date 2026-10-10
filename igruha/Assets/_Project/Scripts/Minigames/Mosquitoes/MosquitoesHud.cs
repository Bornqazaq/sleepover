using TMPro;
using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    /// <summary>Role and phase-specific information, drawn from replicated state.</summary>
    public static class MosquitoesHud
    {
        private const string Summary = "СОН {0:0.0} / {1:0} с     КОМАРЫ {2:0}\n";
        private const string FlightControls = "КОМАР   WASD — полёт   Space / Ctrl — высота   Shift — тихо   ЛКМ — держать укус\n";

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
                    return sleep.RecentBites > 0 ? Summary + "ТЕБЯ КУСАЮТ • {3:0} / {4:0} • E — проснуться" : Summary + "СПИШЬ • E — проснуться и включить свет";
                }
                if (sleep.Phase == GiantPhase.Waking) return Summary + "ПРОСЫПАЕШЬСЯ";
                if (sleep.Phase == GiantPhase.LyingDown) return Summary + "УКЛАДЫВАЕШЬСЯ";
                value = sleep.ImmunityRemaining;
                if (value > 0) return sleep.LampOn ? Summary + "СВЕТ ВКЛЮЧЁН • СОН ЧЕРЕЗ {3:0.0} с" : Summary + "ТЕМНО • E у лампы — свет • СОН ЧЕРЕЗ {3:0.0} с";
                return sleep.LampOn ? Summary + "СВЕТ ВКЛЮЧЁН • E у кровати — спать" : Summary + "ТЕМНО • E у лампы — свет • E у кровати — спать";
            }
            if (own == null) return Summary + "ГОТОВИМСЯ К ПОЛЁТУ";
            if (!own.IsAlive) return eaten ? Summary + "СТРЕКОЗА ПОЙМАЛА ТЕБЯ • наблюдаешь за командой" : Summary + "ТЕБЯ ПРИХЛОПНУЛИ • наблюдаешь за командой";
            if (!own.HasEntered) { value = game.EntryRemaining; return Summary + "ВЛЕТИ В ОКНО • {3:0.0} с"; }
            if (own.IsAttached) { value = own.BiteFraction * 100; return Summary + "УКУС {3:0}% • держи ЛКМ"; }
            if (own.BiteCooldown > 0) { value = own.BiteCooldown; return Summary + "УКУС ЧЕРЕЗ {3:0.0} с • отлети в укрытие"; }
            if (sleep.Phase == GiantPhase.Waking || sleep.Phase == GiantPhase.Awake) return Summary + "ЧЕЛОВЕК ПРОСНУЛСЯ • прячься от шлепка";
            if (sleep.Phase == GiantPhase.LyingDown || sleep.ImmunityRemaining > 0) return Summary + "ДОЖДИСЬ, ПОКА ЧЕЛОВЕК УСНЁТ";
            if (game.AliveCount == 1) return Summary + "ТЫ ПОСЛЕДНИЙ • один долгий укус разбудит человека";
            if (sleep.RecentBites > 0) { value = sleep.BiteWindowRemaining; return Summary + "ЕЩЁ ОДИН УКУС! • осталось {3:0.0} с"; }
            return Vector3.Distance(own.Position, game.BiteAnchor(own.Id)) <= game.Config.BiteDistance
                ? Summary + "ДЕРЖИ ЛКМ — УКУСИТЬ" : Summary + "ПОДЛЕТИ К ГРУДИ • два укуса будят человека";
        }
        public static string Instructions(MosquitoesMinigame game, MosquitoBody own)
        {
            if (game.LocalId == game.GiantId) return "ЧЕЛОВЕК   WASD — ходить   E — кровать / лампа   ЛКМ — шлепок\nНакопи 60 секунд сна или поймай всех комаров. Пробуждение от укусов отнимает 2,5 с сна";
            if (own != null && !own.IsAlive) return "ТВОЯ КОМАНДА ЕЩЁ ИГРАЕТ";
            if (own != null && !own.HasEntered) return FlightControls + "Лети к метке окна. После входа вернуться наружу нельзя";
            return game.AliveCount == 1 ? FlightControls + "Остался один: держи укус 1 секунду. Уходи от руки и дождись следующего сна" :
                FlightControls + "Кусайте вместе: два укуса за 4 секунды будят человека. После укуса отлетай";
        }
    }
}
