using System;
using System.Collections.Generic;

namespace Igruha.Minigames.MemoryRun
{
    /// <summary>
    /// Что известно про одного игрока к концу раунда. Из этих трёх чисел
    /// целиком считаются места (см. <see cref="MemoryRunRanking"/>).
    /// </summary>
    /// <remarks>
    /// Структура, а не поля по разным <c>MonoBehaviour</c>, — намеренно:
    /// в фазе 3 она уезжает в <c>NetworkList</c> как есть, без разбора
    /// на части. Правило конвейера «состояние раунда мигрирует, а не
    /// переписывается» держится именно на этом.
    /// </remarks>
    [Serializable]
    public struct MemoryRunProgress
    {
        public int PlayerId;

        /// <summary>
        /// Сколько шагов пройдено в лучшей попытке за всю игру. Ноль — игрок
        /// ни разу не встал на первую плиту.
        ///
        /// Именно рекорд, а не результат последней попытки: смерть в этой игре
        /// не откатывает достигнутое, она стоит очереди и попытки.
        /// </summary>
        public int BestStep;

        /// <summary>Когда рекорд был поставлен. Тайбрейк при равном шаге.</summary>
        public double BestStepTime;

        /// <summary>Сколько раз погиб. Взрыв, пропасть и истёкший таймер хода — одинаково.</summary>
        public int Deaths;

        /// <summary>Дошёл ли до выходной двери.</summary>
        public bool Finished;

        /// <summary>Каким по счёту дошёл, с 1. Ноль — не дошёл.</summary>
        public int ArrivalOrder;
    }

    /// <summary>
    /// Состояние раунда «Рейса на память»: прогресс, смерти и порядок прибытия
    /// на всех участников.
    ///
    /// Все изменения идут через методы этого класса — <see cref="RegisterReach"/>,
    /// <see cref="RegisterDeath"/>, <see cref="RegisterFinish"/>. Ни одного
    /// публичного сеттера: в фазе 3 ровно эти три метода уйдут за <c>IsServer</c>,
    /// и больше менять состояние будет неоткуда.
    /// </summary>
    public sealed class MemoryRunState
    {
        private readonly List<MemoryRunProgress> records = new List<MemoryRunProgress>(8);
        private readonly Dictionary<int, int> indexById = new Dictionary<int, int>(8);

        /// <summary>Игрок погиб — для звука, счётчика и реакции HUD.</summary>
        public event Action<int> PlayerDied;

        /// <summary>Игрок улучшил рекорд: id и номер достигнутого шага.</summary>
        public event Action<int, int> ProgressAdvanced;

        /// <summary>Игрок дошёл до двери: id и место по прибытию.</summary>
        public event Action<int, int> PlayerFinished;

        public IReadOnlyList<MemoryRunProgress> Records => records;

        /// <summary>Сколько игроков уже дошло до двери.</summary>
        public int FinishedCount { get; private set; }

        public void Reset(IReadOnlyList<int> playerIds)
        {
            records.Clear();
            indexById.Clear();
            FinishedCount = 0;

            if (playerIds == null)
            {
                return;
            }

            for (int i = 0; i < playerIds.Count; i++)
            {
                indexById[playerIds[i]] = records.Count;
                records.Add(new MemoryRunProgress { PlayerId = playerIds[i] });
            }
        }

        public bool TryGet(int playerId, out MemoryRunProgress progress)
        {
            if (indexById.TryGetValue(playerId, out int index))
            {
                progress = records[index];
                return true;
            }

            progress = default;
            return false;
        }

        /// <summary>
        /// Игрок встал на плиту шага <paramref name="step"/> (с нуля).
        /// Рекорд обновляется только вверх — иначе новая неудачная попытка
        /// затирала бы достижение прошлой, и место считалось бы по последнему
        /// провалу вместо лучшего результата.
        /// </summary>
        public void RegisterReach(int playerId, int step, double time)
        {
            if (!indexById.TryGetValue(playerId, out int index))
            {
                return;
            }

            MemoryRunProgress progress = records[index];
            int reached = step + 1;
            if (reached <= progress.BestStep)
            {
                return;
            }

            progress.BestStep = reached;
            progress.BestStepTime = time;
            records[index] = progress;

            ProgressAdvanced?.Invoke(playerId, reached);
        }

        /// <summary>Смерть любого рода. Возвращает, исчерпан ли лимит попыток.</summary>
        public bool RegisterDeath(int playerId, int deathLimit)
        {
            if (!indexById.TryGetValue(playerId, out int index))
            {
                return false;
            }

            MemoryRunProgress progress = records[index];
            progress.Deaths++;
            records[index] = progress;

            PlayerDied?.Invoke(playerId);
            return progress.Deaths >= deathLimit;
        }

        /// <summary>Игрок дошёл до двери. Порядок прибытия и есть его место.</summary>
        public void RegisterFinish(int playerId, double time)
        {
            if (!indexById.TryGetValue(playerId, out int index))
            {
                return;
            }

            MemoryRunProgress progress = records[index];
            if (progress.Finished)
            {
                return;
            }

            FinishedCount++;
            progress.Finished = true;
            progress.ArrivalOrder = FinishedCount;
            progress.BestStepTime = time;
            records[index] = progress;

            PlayerFinished?.Invoke(playerId, progress.ArrivalOrder);
        }

        /// <summary>
        /// Принять запись, посчитанную сервером. Зовётся только на машине без
        /// авторитета — это зеркало, а не второй источник истины.
        ///
        /// Отдельным методом, а не публичным сеттером: три метода выше остаются
        /// единственным способом что-то <i>посчитать</i>, и по ним видно, что
        /// считает их только сервер.
        ///
        /// <see cref="FinishedCount"/> здесь не восстанавливается намеренно:
        /// порядок прибытия уже приехал готовым в <c>ArrivalOrder</c>, а
        /// раздавать его — дело сервера, и клиенту счётчик не нужен ни на что.
        /// </summary>
        public void ApplyReplicated(in MemoryRunProgress record)
        {
            if (indexById.TryGetValue(record.PlayerId, out int index))
            {
                records[index] = record;
                return;
            }

            indexById[record.PlayerId] = records.Count;
            records.Add(record);
        }

        /// <summary>
        /// Участник ушёл из матча. Запись остаётся: спека требует, чтобы
        /// ушедший сохранял достигнутое и получал место по лучшему результату.
        /// </summary>
        public int DeathsOf(int playerId) =>
            indexById.TryGetValue(playerId, out int index) ? records[index].Deaths : 0;
    }
}
