using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>
    /// Боулинг в хабе: катишь шар, кегли падают, шар возвращается, катишь
    /// снова. Ни счёта, ни табло, ни захода из двух бросков — решение
    /// геймдизайнера от 21.09: забава должна быть «кинул и сбил», а не партией.
    ///
    /// Кегли остаются лежать там, где упали, и поднимаются все разом, когда
    /// собьёшь последнюю. Физику считает только авторитет, остальные видят
    /// реплику.
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
        [Tooltip("Сколько ждать перед подсчётом, если кегли всё ещё шевелятся, с")]
        [SerializeField] private float settleTimeout = 3f;

        [Tooltip("Пауза после того, как всё встало, перед возвратом шара, с")]
        [SerializeField] private float resultSeconds = 1.2f;

        private Collider ballCollider;
        private Collider graceCollider;
        private float graceTimer;

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
            FinishThrow();
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
                if (pins[i] != null && !pins[i].IsResting)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Бросок кончился. Считать нечего — сбитые кегли просто остаются
        /// лежать. Проверяем только одно: не пора ли поднять их все.
        /// </summary>
        private void FinishThrow()
        {
            showingResult = true;
            resultTimer = 0f;
        }

        /// <summary>
        /// Вернуть шар в руки. Кегли поднимаются только когда сбиты все —
        /// иначе человек, сбивший девять, лишился бы последней попытки.
        /// </summary>
        private void HoldResult()
        {
            resultTimer += Time.deltaTime;
            if (resultTimer < resultSeconds)
            {
                return;
            }

            showingResult = false;

            if (AllPinsKnocked())
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

        private bool AllPinsKnocked()
        {
            if (pins == null)
            {
                return true;
            }

            for (int i = 0; i < pins.Length; i++)
            {
                if (pins[i] != null && !pins[i].IsKnocked)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
