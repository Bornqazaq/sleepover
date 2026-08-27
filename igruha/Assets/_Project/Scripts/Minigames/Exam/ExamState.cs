namespace Igruha.Minigames.Exam
{
    /// <summary>Где игрок оказался в момент фиксации.</summary>
    public enum ExamSide : byte
    {
        /// <summary>Ни на одной платформе: зона возврата, проход или зазор между ними.</summary>
        None = 0,
        A = 1,
        B = 2
    }

    /// <summary>
    /// Состояние матча одной структурой. Собрано так намеренно: в фазе 3
    /// это ровно то, что уедет в <c>NetworkVariable</c> целиком, а не
    /// россыпь полей MonoBehaviour, которую пришлось бы синхронизировать
    /// по одному.
    ///
    /// Верного варианта здесь нет и быть не должно — он живёт только
    /// в серверном поле контроллера и не покидает сервер до раскрытия
    /// створок (спека 10.3).
    /// </summary>
    public struct ExamMatchState
    {
        /// <summary>Номер текущего вопроса, с единицы.</summary>
        public int QuestionNumber;

        /// <summary>Всего вопросов в матче. Считается на старте и больше не меняется.</summary>
        public int TotalQuestions;

        /// <summary>Цена текущего вопроса в ОЭ: 1, 2 или 3 по трети матча.</summary>
        public int QuestionValue;

        /// <summary>Кто сейчас за кафедрой.</summary>
        public int HostPlayerId;

        /// <summary>Вопрос состоялся: Ведущий успел напечатать его до конца фазы печати.</summary>
        public bool QuestionPosted;
    }

    /// <summary>
    /// Строка участника. Всё поля — простые типы: структура обязана остаться
    /// unmanaged, иначе её не положить в <c>NetworkList</c> в фазе 3.
    /// </summary>
    public struct ExamEntry
    {
        public int PlayerId;

        /// <summary>Накопленные очки Экзамена. НЕ очки катки: в конце они станут местом.</summary>
        public int Score;

        /// <summary>
        /// Сколько раз угадал верно в одиночку. Второй критерий тайбрейка:
        /// награждает того, кто шёл против толпы.
        /// </summary>
        public int LonelyHits;

        /// <summary>Сколько раз угадал верно вообще. Третий критерий тайбрейка.</summary>
        public int CorrectAnswers;

        /// <summary>
        /// Когда игрок набрал свой текущий счёт, по общим часам. Четвёртый
        /// критерий тайбрейка и единственный, который разводит всегда:
        /// в одну миллисекунду двое начислиться не могут.
        /// </summary>
        public double LastScoredAt;

        /// <summary>Где зафиксирован в текущем вопросе.</summary>
        public ExamSide Side;

        /// <summary>Игрок ещё в матче. Выбывания в «Экзамене» нет — это про дисконнект.</summary>
        public bool Present;
    }
}
