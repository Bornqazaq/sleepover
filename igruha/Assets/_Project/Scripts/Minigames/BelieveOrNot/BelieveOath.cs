namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>Публичное обещание. Истинности в сетевом состоянии нет.</summary>
    public enum BelieveOath : byte { Pending, Mine, Yours, Declined }

    public static class BelieveOathRules
    {
        public static bool IsClaim(BelieveOath oath) => oath == BelieveOath.Mine || oath == BelieveOath.Yours;

        // Проверяется исходная коробка Знающего, а не её место ПОСЛЕ обмена.
        public static bool IsTrue(BelieveOath oath, bool knowerOriginallyHadWin) =>
            IsClaim(oath) && (oath == BelieveOath.Mine) == knowerOriginallyHadWin;

        public static string Statement(BelieveOath oath) => oath == BelieveOath.Mine
            ? "«Выигрышная у меня»" : oath == BelieveOath.Yours ? "«Выигрышная у тебя»" : "Без клятвы";
    }
}
