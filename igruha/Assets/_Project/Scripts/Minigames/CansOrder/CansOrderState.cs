namespace Igruha.Minigames.CansOrder
{
    /// <summary>
    /// Состояние раунда одной структурой. Собрано так намеренно: в фазе 3
    /// это поле целиком уезжает в <c>NetworkVariable</c>, и переписывать
    /// правила не придётся. Разрозненные поля MonoBehaviour пришлось бы
    /// синхронизировать по одному.
    /// </summary>
    public struct CansOrderRoundState
    {
        /// <summary>Номер раунда, с 1.</summary>
        public int Round;

        /// <summary>Номер круга внутри раунда, с 1. Ноль — идёт брифинг.</summary>
        public int Circle;

        /// <summary>Сколько банок в задании этого раунда (таблица 8.2).</summary>
        public int CanCount;

        /// <summary>Сколько игроков выбывает за этот раунд (таблица 6.1).</summary>
        public int Quota;

        /// <summary>Живых в начале раунда. Знаменатель доли высоты клеток (5.5).</summary>
        public int AliveAtStart;

        /// <summary>Сколько уже собрало расстановку в этом раунде.</summary>
        public int SolvedCount;
    }

    /// <summary>
    /// Состояние одного участника — то, что в фазе 3 уедет в <c>NetworkList</c>.
    ///
    /// <b>Число совпадений публикуется только в стадии показа результатов.</b>
    /// До неё оно физически не покидает сервер: это единственный способ
    /// удержать честность против клиента, который читает сетевое состояние
    /// напрямую (спека 10.1).
    /// </summary>
    public struct CansOrderEntry
    {
        public int PlayerId;

        /// <summary>Игрок ещё в матче.</summary>
        public bool Alive;

        /// <summary>Собрал скрытую расстановку и в следующих кругах не участвует.</summary>
        public bool Solved;

        /// <summary>Собрал именно в этом круге — на табло у него «СОБРАЛ».</summary>
        public bool SolvedThisCircle;

        /// <summary>Подтвердил расстановку в этом круге.</summary>
        public bool Confirmed;

        /// <summary>
        /// Совпадений по позициям в этом круге. Значимо только когда
        /// <see cref="Confirmed"/>: неподтвердившему ноль не приписывается —
        /// ноль это полноценная информация, он вычёркивает все позиции сразу
        /// (спека 5.4 и 13, пункт 3).
        /// </summary>
        public int Matches;

        /// <summary>Потрачено попыток за раунд. Круг без подтверждения тоже тратит попытку.</summary>
        public int Attempts;

        /// <summary>Лучшее число совпадений за раунд. Ранжирует при исчерпании потолка кругов (5.6).</summary>
        public int BestMatches;

        /// <summary>Круг, в котором лучшее число достигнуто впервые. Позже — хуже.</summary>
        public int BestCircle;

        /// <summary>Момент подтверждения на общих часах. Решает при равенстве попыток (5.6).</summary>
        public double ConfirmTime;

        /// <summary>Доля высоты клетки 0…1: 0 — нижняя ступень, 1 — верхняя (спека 5.5).</summary>
        public float HeightFraction;
    }
}
