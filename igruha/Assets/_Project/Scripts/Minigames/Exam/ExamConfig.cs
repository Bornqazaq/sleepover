using UnityEngine;

namespace Igruha.Minigames.Exam
{
    /// <summary>
    /// Все числа «Экзамена» одним ассетом: геймдизайнер крутит их без
    /// программиста. Спека прямо предупреждает, что фаза печати — первое,
    /// что придётся резать при затянутости, и делаться это должно
    /// в инспекторе, а не правкой кода.
    ///
    /// Размеры арены живут здесь же, потому что по ним строится сцена
    /// (см. <c>ExamArenaBuilder</c>): один источник истины и для геометрии,
    /// и для правил.
    /// </summary>
    [CreateAssetMenu(menuName = "Igruha/Exam Config", fileName = "ExamConfig")]
    public sealed class ExamConfig : ScriptableObject
    {
        [Header("Длительности фаз, секунды")]
        [Tooltip("Печать вопроса. ПЕРВОЕ, что резать при затянутости матча: 30→20 даёт минус треть")]
        [SerializeField] private float typingSeconds = 30f;
        [Tooltip("Показ вопроса: прочитать до начала беготни")]
        [SerializeField] private float revealQuestionSeconds = 3f;
        [Tooltip("Выбор платформы")]
        [SerializeField] private float choiceSeconds = 8f;
        [Tooltip("Нагнетание: движение и толчки ещё разрешены")]
        [SerializeField] private float tensionSeconds = 3f;
        [Tooltip("Респавн провалившихся")]
        [SerializeField] private float respawnSeconds = 2f;

        [Header("Створки пола")]
        [SerializeField] private float hatchOpenSeconds = 0.5f;
        [SerializeField] private float hatchHeldSeconds = 3f;
        [SerializeField] private float hatchCloseSeconds = 0.5f;

        [Header("Ввод вопроса")]
        [SerializeField] private int questionMaxLength = 80;
        [SerializeField] private int optionMaxLength = 30;

        [Header("Очки Экзамена (ОЭ)")]
        [Tooltip("Сверху тому, кто угадал верно в одиночку")]
        [SerializeField] private int lonelyBonus = 1;
        [Tooltip("Разница между платформами, при которой Ведущий берёт полную цену вопроса")]
        [SerializeField] private int splitFullThreshold = 1;
        [Tooltip("Разница, при которой Ведущий берёт половину цены. Больше неё — ноль")]
        [SerializeField] private int splitHalfThreshold = 2;

        [Header("Число вопросов по составу на старте")]
        [Tooltip("Сколько вопросов приходится на каждого игрока. Индекс 0 — лобби из 2, дальше по возрастанию до 8")]
        [SerializeField] private int[] questionsPerPlayer = { 4, 3, 2, 1, 1, 1, 1 };

        [Header("Страховки")]
        [Tooltip("Жёсткий предел матча: места расставляются по накопленным ОЭ")]
        [SerializeField] private float matchTimeoutSeconds = 600f;
        [Tooltip("Через сколько секунд упора в геометрию игрока вернуть в зону возврата")]
        [SerializeField] private float stuckTeleportSeconds = 3f;

        [Header("Арена, в ширинах персонажа (1 ШП = 0.72 м)")]
        //
        // ⚠️ Зал ужат на треть 08.09 и это правка по замечанию геймдизайнера,
        // а не подгонка чисел. Было 40 × 36 ШП (28.80 × 25.92 м) при потолке
        // 8 ШП: 746 м² пола на восьмерых, из которых игра занимала меньше
        // трети. С уровня глаз это читалось не классом, а ангаром — персонаж
        // в кадре становился фигуркой, парты у стен игрушечными, а между
        // зоной возврата и платформами лежала полоса голого пола шириной
        // в пять метров, по которой просто бежали.
        //
        // Стало 27 × 28 ШП (19.44 × 20.16 м), 392 м² — ровно вдвое меньше.
        // Раскладка по глубине сходится нацело: кафедра 4 + проход 3 +
        // площадка 6 + разбег 5 + зона возврата 4 + запас камеры 6 = 28 ШП.
        // По ширине: две площадки по 8 + зазор 2 = 18, и по 4.5 ШП (3.24 м)
        // остаётся с боков — там встают ряды парт, а не пустой пол.
        [SerializeField] private float unitsPerWidth = 0.72f;
        [Tooltip("27 = 18 ШП под площадки + по 4.5 ШП боковых нефов под ряды парт")]
        [SerializeField] private float hallWidth = 27f;
        [Tooltip("28 = 4 кафедра + 3 проход + 6 площадка + 5 разбег + 4 возврат + 6 запас камеры")]
        [SerializeField] private float hallDepth = 28f;
        [Tooltip("6.5 ШП = 4.68 м. На 8 ШП зал читался ратушей: потолок выше доски вдвое")]
        [SerializeField] private float ceilingHeight = 6.5f;
        [Tooltip("8 × 6 ШП = 24.9 м² — семеро помещаются с запасом на толкучку, но без пустоты")]
        [SerializeField] private float platformWidth = 8f;
        [SerializeField] private float platformDepth = 6f;
        [Tooltip("Зазор между платформами. НЕ пропасть: перекрыт невидимым полом")]
        [SerializeField] private float platformGap = 2f;
        [SerializeField] private float podiumWidth = 8f;
        [SerializeField] private float podiumDepth = 4f;
        [SerializeField] private float podiumHeight = 1f;
        [SerializeField] private float returnZoneWidth = 18f;
        [SerializeField] private float returnZoneDepth = 4f;
        [Tooltip("От зоны возврата до ближайшей платформы")]
        [SerializeField] private float returnToPlatformGap = 5f;
        [Tooltip("Запас за зоной возврата: камере нужно ~4.5 м позади персонажа")]
        [SerializeField] private float cameraSlackDepth = 6f;
        [Tooltip("Глубина ямы под платформами")]
        [SerializeField] private float pitDepth = 10f;

        public float TypingSeconds => typingSeconds;
        public float RevealQuestionSeconds => revealQuestionSeconds;
        public float ChoiceSeconds => choiceSeconds;
        public float TensionSeconds => tensionSeconds;
        public float RespawnSeconds => respawnSeconds;

        public float HatchOpenSeconds => hatchOpenSeconds;
        public float HatchHeldSeconds => hatchHeldSeconds;
        public float HatchCloseSeconds => hatchCloseSeconds;

        /// <summary>Вся стадия створок целиком: раскрытие + держим + закрытие.</summary>
        public float HatchStageSeconds => hatchOpenSeconds + hatchHeldSeconds + hatchCloseSeconds;

        public int QuestionMaxLength => questionMaxLength;
        public int OptionMaxLength => optionMaxLength;

        public int LonelyBonus => lonelyBonus;
        public int SplitFullThreshold => splitFullThreshold;
        public int SplitHalfThreshold => splitHalfThreshold;

        public float MatchTimeoutSeconds => matchTimeoutSeconds;
        public float StuckTeleportSeconds => stuckTeleportSeconds;

        public float UnitsPerWidth => unitsPerWidth;
        public float HallWidth => hallWidth * unitsPerWidth;
        public float HallDepth => hallDepth * unitsPerWidth;
        public float CeilingHeight => ceilingHeight * unitsPerWidth;
        public float PlatformWidth => platformWidth * unitsPerWidth;
        public float PlatformDepth => platformDepth * unitsPerWidth;
        public float PlatformGap => platformGap * unitsPerWidth;
        public float PodiumWidth => podiumWidth * unitsPerWidth;
        public float PodiumDepth => podiumDepth * unitsPerWidth;
        public float PodiumHeight => podiumHeight * unitsPerWidth;
        public float ReturnZoneWidth => returnZoneWidth * unitsPerWidth;
        public float ReturnZoneDepth => returnZoneDepth * unitsPerWidth;
        public float ReturnToPlatformGap => returnToPlatformGap * unitsPerWidth;
        public float CameraSlackDepth => cameraSlackDepth * unitsPerWidth;
        public float PitDepth => pitDepth * unitsPerWidth;

        /// <summary>
        /// Сколько вопросов будет в матче. Считается ОДИН раз, на старте:
        /// цена вопроса привязана к его номеру, и если число поплывёт при
        /// выходе игрока, треть вопросов переоценится посреди матча.
        /// </summary>
        public int GetQuestionCount(int playerCount)
        {
            int clamped = Mathf.Clamp(playerCount, 2, 8);
            int index = Mathf.Clamp(clamped - 2, 0, questionsPerPlayer.Length - 1);
            return Mathf.Max(1, questionsPerPlayer[index] * clamped);
        }

        /// <summary>
        /// Цена вопроса растёт по третям матча: 1 / 2 / 3 очка. Назначает
        /// игра, а не Ведущий — иначе он поставит максимум на вопрос, ответ
        /// на который знает только его друг.
        /// </summary>
        public int GetQuestionValue(int questionNumber, int totalQuestions)
        {
            if (totalQuestions <= 0)
            {
                return 1;
            }

            int index = Mathf.Clamp(questionNumber - 1, 0, totalQuestions - 1);
            int third = Mathf.CeilToInt(totalQuestions / 3f);
            if (third <= 0)
            {
                return 1;
            }

            return Mathf.Clamp(index / third + 1, 1, 3);
        }

        /// <summary>
        /// Награда Ведущего за раскол. D — разница в числе Учеников между
        /// платформами; стоящие вне платформ не в счёт. Ровный раскол —
        /// полная цена, очевидный вопрос — ноль.
        /// </summary>
        public int GetHostReward(int difference, int questionValue)
        {
            if (difference <= splitFullThreshold)
            {
                return questionValue;
            }

            if (difference <= splitHalfThreshold)
            {
                return questionValue / 2;
            }

            return 0;
        }
    }
}
