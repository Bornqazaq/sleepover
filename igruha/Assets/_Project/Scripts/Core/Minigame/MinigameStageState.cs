using System;
using UnityEngine;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Подраунды внутри мини-игры. <see cref="MinigameControllerBase"/> знает
    /// только про фазы Tutorial / Round / Results; играм, у которых внутри
    /// раунда идёт своя последовательность (подраунд из нескольких стадий),
    /// нужен ещё один уровень — вот он.
    ///
    /// Что такое стадия, Core не знает: идентификатор — byte, его смысл
    /// определяет игра. Core отвечает за одно — чтобы момент смены стадии был
    /// одинаковым на всех машинах.
    ///
    /// Сеть: переход объявляет авторитет, остальные применяют присланное
    /// состояние. Правило то же, что у <see cref="MinigameControllerBase"/>:
    /// моста нет — авторитет локальный, и всё работает в одиночной сцене.
    ///
    /// Конец стадии хранится как **момент времени**, а не как остаток. Остаток
    /// пришлось бы досылать каждый кадр, а момент достаточно объявить один раз:
    /// дальше каждая машина сама считает от общих часов.
    /// </summary>
    public sealed class MinigameStageState : MonoBehaviour
    {
        /// <summary>Стадии нет — раунд не начат либо уже кончился.</summary>
        public const byte NoStage = 0;

        /// <summary>Начался подраунд. Приходит раньше <see cref="StageStarted"/>.</summary>
        public event Action<int> SubroundStarted;

        /// <summary>Началась стадия.</summary>
        public event Action<byte> StageStarted;

        /// <summary>
        /// Стадия отыграла своё время. Только у авторитета: что делать дальше,
        /// решают правила игры — Core последовательность не знает.
        /// </summary>
        public event Action<byte> StageElapsed;

        private IMinigameNetworkBridge bridge;
        private bool elapsedRaised = true;

        /// <summary>Номер подраунда, с 1. Ноль — подраунды ещё не начинались.</summary>
        public int Subround { get; private set; }

        /// <summary>Текущая стадия. Смысл значения определяет игра.</summary>
        public byte Stage { get; private set; } = NoStage;

        /// <summary>Момент конца стадии по общим часам.</summary>
        public double StageEndTime { get; private set; }

        /// <summary>Длительность текущей стадии, с — для отрисовки полосы прогресса.</summary>
        public float StageDuration { get; private set; }

        public bool Running => Stage != NoStage;

        /// <summary>Сервер сетевой катки либо единственная машина локального теста.</summary>
        public bool HasAuthority => bridge == null || bridge.HasAuthority;

        /// <summary>Сколько секунд осталось до конца стадии. Ноль, если стадия уже вышла.</summary>
        public float StageRemaining
        {
            get
            {
                if (!Running)
                {
                    return 0f;
                }

                return Mathf.Max(0f, (float)(StageEndTime - NetworkClock.Now));
            }
        }

        private void Awake()
        {
            bridge = GetComponent<IMinigameNetworkBridge>();
        }

        /// <summary>
        /// Начать подраунд с его первой стадии. Отдельный метод, а не пара
        /// вызовов, чтобы подписчик гарантированно получил сначала смену
        /// подраунда и только потом смену стадии.
        /// </summary>
        public void BeginSubround(int subround, byte firstStage, float duration)
        {
            if (!HasAuthority)
            {
                return;
            }

            Subround = subround;
            SubroundStarted?.Invoke(Subround);
            EnterStage(firstStage, duration);
        }

        /// <summary>Перевести последовательность на следующую стадию.</summary>
        public void EnterStage(byte stage, float duration)
        {
            if (!HasAuthority)
            {
                return;
            }

            ApplyStage(stage, NetworkClock.Now + Mathf.Max(0f, duration), Mathf.Max(0f, duration));
        }

        /// <summary>
        /// Закончить стадию прямо сейчас, не дожидаясь её времени. Нужно там,
        /// где стадию закрывает не таймер, а событие: фаза отмера «Секундомера»
        /// кончается, как только все живые нажали «стоп».
        /// </summary>
        public void EndStageNow()
        {
            if (!HasAuthority || !Running || elapsedRaised)
            {
                return;
            }

            StageEndTime = NetworkClock.Now;
            RaiseElapsed();
        }

        /// <summary>Остановить последовательность: раунд кончился.</summary>
        public void StopSequence()
        {
            Stage = NoStage;
            StageDuration = 0f;
            StageEndTime = 0d;
            elapsedRaised = true;
        }

        /// <summary>
        /// Применить состояние, присланное авторитетом. Момент конца приходит
        /// в тех же общих часах, поэтому пересчитывать его под локальное время
        /// не нужно — и остаток совпадает с серверным без поправки на пинг.
        /// </summary>
        public void ApplyState(int subround, byte stage, double stageEndTime, float duration)
        {
            if (HasAuthority)
            {
                return;
            }

            if (subround != Subround)
            {
                Subround = subround;
                SubroundStarted?.Invoke(Subround);
            }

            ApplyStage(stage, stageEndTime, duration);
        }

        private void ApplyStage(byte stage, double endTime, float duration)
        {
            StageEndTime = endTime;
            StageDuration = duration;

            bool changed = Stage != stage;
            Stage = stage;
            // Флаг снимается и при повторе той же стадии: подряд идущие
            // одинаковые стадии — законный случай (например, два спуска клетки),
            // и второй обязан снова доиграть до конца.
            elapsedRaised = stage == NoStage;

            if (changed || stage != NoStage)
            {
                StageStarted?.Invoke(stage);
            }
        }

        private void Update()
        {
            if (!HasAuthority || !Running || elapsedRaised)
            {
                return;
            }

            if (NetworkClock.Now >= StageEndTime)
            {
                RaiseElapsed();
            }
        }

        private void RaiseElapsed()
        {
            // Флаг ставится ДО события: правила игры внутри обработчика позовут
            // EnterStage, и без этого стадия успела бы отстреляться дважды.
            elapsedRaised = true;
            StageElapsed?.Invoke(Stage);
        }
    }
}
