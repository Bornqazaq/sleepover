using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>
    /// Боулинг в хабе. Занял дорожку — катишь шар, кегли падают, табличка
    /// показывает, сколько сбил за заход. Правила простые и такими задуманы:
    /// ни фреймов, ни страйков, ни партии — из хаба в любой момент выдёргивают
    /// в мини-игру, и длинная партия там никому не нужна.
    ///
    /// Считает только авторитет. Клиент видит результат готовым — иначе у
    /// каждой машины была бы своя правдоподобная картинка упавших кеглей.
    /// </summary>
    public sealed class BowlingStation : HubActivityStation
    {
        [Header("Инвентарь дорожки")]
        [SerializeField] private BowlingBall ball;
        [SerializeField] private BowlingPin[] pins;

        [Tooltip("Откуда катится шар. Обычно совпадает с меткой игрока")]
        [SerializeField] private Transform ballHome;

        [Header("Бросок")]
        [Tooltip("Скорость шара при нулевой силе, м/с — докатывается и сносит одну-две")]
        [SerializeField] private float minSpeed = 2f;

        [Tooltip("Скорость шара при полной силе, м/с")]
        [SerializeField] private float maxSpeed = 7.5f;

        [Tooltip("Сколько секунд после броска шар не сталкивается с бросавшим, с")]
        [SerializeField] private float releaseGraceSeconds = 0.8f;

        [Tooltip("Высота выпуска шара над меткой, м")]
        [SerializeField] private float releaseHeight = 0.35f;

        [Header("Заход")]
        [Tooltip("Сколько бросков в заходе")]
        [SerializeField] private int throwsPerFrame = 2;

        [Tooltip("Сколько ждать перед подсчётом, если кегли всё ещё шевелятся, с")]
        [SerializeField] private float settleTimeout = 3f;

        [Tooltip("Сколько держать результат на табличке перед новым заходом, с")]
        [SerializeField] private float resultSeconds = 1.5f;

        private Collider ballCollider;
        private Collider graceCollider;
        private float graceTimer;

        private int throwIndex;
        private int knockedTotal;
        private float waitTimer;
        private float resultTimer;
        private bool waitingForSettle;
        private bool showingResult;

        protected override void Start()
        {
            base.Start();

            // Дорожка должна выглядеть собранной и до того, как к ней подошли.
            if (HasAuthority)
            {
                ResetActivity();
            }
        }

        protected override void OnTaken(PlayerController player)
        {
            ResetActivity();
        }

        protected override void Launch(Vector3 direction, float power)
        {
            if (ball == null || ballHome == null)
            {
                return;
            }

            SuspendPlayerCollision();

            Vector3 from = ballHome.position + Vector3.up * releaseHeight;
            ball.Roll(from, direction, Mathf.Lerp(minSpeed, maxSpeed, power));

            waitingForSettle = true;
            waitTimer = 0f;
        }

        /// <summary>
        /// На время выпуска шар не сталкивается с бросавшим. Без этого
        /// достаточно шагнуть вперёд, чтобы своей же капсулой отпихнуть шар
        /// вбок в первый же кадр — со стороны это выглядит как кривой бросок.
        /// </summary>
        private void SuspendPlayerCollision()
        {
            RestorePlayerCollision();

            PlayerController player = ResolveOccupantBody();
            if (player == null || ball == null)
            {
                return;
            }

            if (ballCollider == null)
            {
                ballCollider = ball.GetComponent<Collider>();
            }

            Collider body = player.GetComponent<Collider>();
            if (ballCollider == null || body == null)
            {
                return;
            }

            Physics.IgnoreCollision(ballCollider, body, true);
            graceCollider = body;
            graceTimer = releaseGraceSeconds;
        }

        private void RestorePlayerCollision()
        {
            if (graceCollider != null && ballCollider != null)
            {
                Physics.IgnoreCollision(ballCollider, graceCollider, false);
            }

            graceCollider = null;
            graceTimer = 0f;
        }

        /// <summary>
        /// Сброс дорожки: кегли на места, шар на подставку, заход сначала.
        /// Зовётся и при уходе игрока, и при выгрузке хаба в мини-игру.
        /// </summary>
        protected override void ResetActivity()
        {
            RestorePlayerCollision();

            throwIndex = 0;
            knockedTotal = 0;
            waitingForSettle = false;
            showingResult = false;
            waitTimer = 0f;
            resultTimer = 0f;

            if (pins != null)
            {
                for (int i = 0; i < pins.Length; i++)
                {
                    if (pins[i] != null)
                    {
                        pins[i].ResetPin();
                    }
                }
            }

            if (ball != null && ballHome != null)
            {
                ball.ResetTo(ballHome.position + Vector3.up * releaseHeight);
            }
        }

        protected override void OnAuthorityUpdate()
        {
            if (graceTimer > 0f)
            {
                graceTimer -= Time.deltaTime;

                if (graceTimer <= 0f)
                {
                    RestorePlayerCollision();
                }
            }

            if (waitingForSettle)
            {
                WaitForSettle();
                return;
            }

            if (showingResult)
            {
                HoldResult();
            }
        }

        /// <summary>
        /// Ждём, пока всё встанет. Таймаут нужен на случай кегли, которая
        /// катается по дорожке бесконечно, и игрока, который встал на пути
        /// шара и держит его телом.
        /// </summary>
        private void WaitForSettle()
        {
            waitTimer += Time.deltaTime;

            if (waitTimer < settleTimeout && !EverythingResting())
            {
                return;
            }

            waitingForSettle = false;
            CountKnocked();
        }

        private bool EverythingResting()
        {
            // Полсекунды форы: сразу после броска тело ещё не разогналось и
            // формально «покоится», а кегли даже не тронуты.
            if (waitTimer < 0.5f)
            {
                return false;
            }

            if (ball != null && !ball.IsResting)
            {
                return false;
            }

            if (pins == null)
            {
                return true;
            }

            for (int i = 0; i < pins.Length; i++)
            {
                if (pins[i] != null && !pins[i].IsCleared && !pins[i].IsResting)
                {
                    return false;
                }
            }

            return true;
        }

        private void CountKnocked()
        {
            int knocked = 0;

            if (pins != null)
            {
                for (int i = 0; i < pins.Length; i++)
                {
                    BowlingPin pin = pins[i];
                    if (pin == null || pin.IsCleared)
                    {
                        continue;
                    }

                    if (!pin.IsKnocked)
                    {
                        continue;
                    }

                    // Сбитую убираем: второй бросок идёт по тому, что осталось.
                    pin.Clear();
                    knocked++;
                }
            }

            knockedTotal += knocked;
            throwIndex++;

            SetPhase(HubActivityPhase.Counting);
            ReportScore((byte)knockedTotal, ResolveOccupantBody());

            showingResult = true;
            resultTimer = 0f;
        }

        /// <summary>
        /// Подержать результат на табличке и начать следующий бросок — или
        /// новый заход, если броски кончились. Игрока при этом не выгоняем:
        /// он ждёт друзей, пусть катает, пока не надоест.
        /// </summary>
        private void HoldResult()
        {
            resultTimer += Time.deltaTime;
            if (resultTimer < resultSeconds)
            {
                return;
            }

            showingResult = false;

            bool frameOver = throwIndex >= throwsPerFrame || AllPinsCleared();
            if (frameOver)
            {
                ResetActivity();
            }
            else if (ball != null && ballHome != null)
            {
                ball.ResetTo(ballHome.position + Vector3.up * releaseHeight);
            }

            // Станция остаётся занятой: следующий бросок делает тот же игрок.
            if (Occupant != NoOccupant)
            {
                SetPhase(HubActivityPhase.Occupied);
            }
        }

        private bool AllPinsCleared()
        {
            if (pins == null)
            {
                return true;
            }

            for (int i = 0; i < pins.Length; i++)
            {
                if (pins[i] != null && !pins[i].IsCleared)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
