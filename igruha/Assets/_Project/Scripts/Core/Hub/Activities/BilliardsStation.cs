using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>
    /// Бильярд в хабе: прицелился A/D, ударил кием по ЛКМ, забил — ударил
    /// ещё. Ни счёта, ни партии, ни табло — решение геймдизайнера: забава
    /// «ударил, забил», а не матч. Спека 9.2 в <c>docs/hub-activities.md</c>
    /// ещё упоминала очки; для хаба они сняты тем же порядком, что табло
    /// боулинга.
    ///
    /// Направление задаёт станция (yaw от A/D), не камера: орбита третьего
    /// лица почти никогда не смотрит вдоль стола.
    /// </summary>
    public sealed class BilliardsStation : HubActivityStation
    {
        [Header("Стол")]
        [SerializeField] private BilliardsBall cueBall;
        [SerializeField] private BilliardsBall[] objectBalls;
        [SerializeField] private BilliardsPocket[] pockets;
        [SerializeField] private Transform aimLine;

        [Header("Удар")]
        [Tooltip("Скорость битка при нулевой силе, м/с")]
        [SerializeField] private float minSpeed = 1.2f;

        [Tooltip("Скорость битка при полной силе, м/с")]
        [SerializeField] private float maxSpeed = 6.5f;

        [Tooltip("Сколько секунд после удара биток не сталкивается с бьющим, с")]
        [SerializeField] private float releaseGraceSeconds = 0.6f;

        [Header("Прицел")]
        [Tooltip("Скорость поворота прицела A/D, градусы в секунду")]
        [SerializeField] private float aimTurnSpeed = 95f;

        [Tooltip("Предел отклонения от метки, градусы. Дальше — удар в себя")]
        [SerializeField] private float aimYawLimit = 80f;

        [Tooltip("Длина линии прицела, м")]
        [SerializeField] private float aimLineLength = 1.4f;

        [Header("Заход")]
        [Tooltip("Сколько ждать, если шары ещё катятся, с")]
        [SerializeField] private float settleTimeout = 4f;

        [Tooltip("Пауза после остановки перед следующим ударом, с")]
        [SerializeField] private float resultSeconds = 0.8f;

        private Collider cueCollider;
        private Collider graceCollider;
        private float graceTimer;

        private float aimYaw;
        private float waitTimer;
        private float resultTimer;
        private bool waitingForSettle;
        private bool showingResult;

        protected override void Start()
        {
            base.Start();

            if (pockets != null)
            {
                for (int i = 0; i < pockets.Length; i++)
                {
                    if (pockets[i] != null)
                    {
                        pockets[i].Bind(this);
                    }
                }
            }

            if (HasAuthority)
            {
                ResetActivity();
            }

            SetAimLineVisible(false);
        }

        private void LateUpdate()
        {
            // Линия прицела — чисто локальная: её включает OnLocalAiming у
            // занявшего, а после удара фаза уходит из Occupied и UpdateAiming
            // больше не зовётся. Без этого хвоста линия оставалась бы висеть.
            if (aimLine != null && Phase != HubActivityPhase.Occupied && aimLine.gameObject.activeSelf)
            {
                aimLine.gameObject.SetActive(false);
            }
        }

        protected override void OnTaken(PlayerController player)
        {
            aimYaw = 0f;
            ResetActivity();
            SetAimLineVisible(true);
            UpdateAimVisual(player);
        }

        /// <summary>
        /// A/D крутит прицел. Move свободен: MovementLocked глушит ходьбу,
        /// камеру не трогаем — она заморожена.
        /// </summary>
        protected override void OnLocalAiming(PlayerController player, PlayerInputReader input)
        {
            if (input == null || player == null)
            {
                return;
            }

            float steer = input.MoveInput.x;
            if (Mathf.Abs(steer) > 0.05f)
            {
                aimYaw += steer * aimTurnSpeed * Time.deltaTime;
                aimYaw = Mathf.Clamp(aimYaw, -aimYawLimit, aimYawLimit);
            }

            SetAimLineVisible(true);
            Vector3 direction = AimDirection();
            player.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            UpdateAimVisual(player);
        }

        protected override Vector3 AimDirection()
        {
            Vector3 forward = base.AimDirection();
            return Quaternion.Euler(0f, aimYaw, 0f) * forward;
        }

        protected override void Launch(Vector3 direction, float power)
        {
            if (cueBall == null || cueBall.IsPocketed)
            {
                return;
            }

            SuspendPlayerCollision();
            SetAimLineVisible(false);

            cueBall.Strike(direction, Mathf.Lerp(minSpeed, maxSpeed, power));

            waitingForSettle = true;
            waitTimer = 0f;
        }

        /// <summary>
        /// Луза позвала. Счёт не ведём: просто убираем шар. Биток вернётся
        /// после остановки стола.
        /// </summary>
        public void NotifyPocketed(BilliardsBall ball)
        {
            if (!HasAuthority || ball == null || ball.IsPocketed)
            {
                return;
            }

            ball.Pocket();
        }

        private void SuspendPlayerCollision()
        {
            RestorePlayerCollision();

            PlayerController player = ResolveOccupantBody();
            if (player == null || cueBall == null)
            {
                return;
            }

            if (cueCollider == null)
            {
                cueCollider = cueBall.GetComponent<Collider>();
            }

            Collider body = player.GetComponent<Collider>();
            if (cueCollider == null || body == null)
            {
                return;
            }

            Physics.IgnoreCollision(cueCollider, body, true);
            graceCollider = body;
            graceTimer = releaseGraceSeconds;
        }

        private void RestorePlayerCollision()
        {
            if (graceCollider != null && cueCollider != null)
            {
                Physics.IgnoreCollision(cueCollider, graceCollider, false);
            }

            graceCollider = null;
            graceTimer = 0f;
        }

        protected override void ResetActivity()
        {
            RestorePlayerCollision();

            waitingForSettle = false;
            showingResult = false;
            waitTimer = 0f;
            resultTimer = 0f;

            if (objectBalls != null)
            {
                for (int i = 0; i < objectBalls.Length; i++)
                {
                    if (objectBalls[i] != null)
                    {
                        objectBalls[i].ResetBall();
                    }
                }
            }

            if (cueBall != null)
            {
                cueBall.ResetBall();
            }

            SetAimLineVisible(Occupant != NoOccupant);
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

        private void WaitForSettle()
        {
            waitTimer += Time.deltaTime;

            if (waitTimer < settleTimeout && !EverythingResting())
            {
                return;
            }

            waitingForSettle = false;
            showingResult = true;
            resultTimer = 0f;
        }

        private bool EverythingResting()
        {
            // Фора: сразу после удара тела ещё «спят» формально.
            if (waitTimer < 0.45f)
            {
                return false;
            }

            if (cueBall != null && !cueBall.IsResting)
            {
                return false;
            }

            if (objectBalls == null)
            {
                return true;
            }

            for (int i = 0; i < objectBalls.Length; i++)
            {
                if (objectBalls[i] != null && !objectBalls[i].IsResting)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Удар кончился. Биток возвращаем всегда (в том числе из лузы).
        /// Остальные шары поднимаются разом, когда забиты все — иначе
        /// стол обнулялся бы после каждого забитого.
        /// </summary>
        private void HoldResult()
        {
            resultTimer += Time.deltaTime;
            if (resultTimer < resultSeconds)
            {
                return;
            }

            showingResult = false;

            if (AllObjectBallsPocketed())
            {
                ResetActivity();
            }
            else if (cueBall != null)
            {
                cueBall.ResetBall();
            }

            if (Occupant != NoOccupant)
            {
                SetPhase(HubActivityPhase.Occupied);
                SetAimLineVisible(true);
            }
        }

        private bool AllObjectBallsPocketed()
        {
            if (objectBalls == null || objectBalls.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < objectBalls.Length; i++)
            {
                if (objectBalls[i] != null && !objectBalls[i].IsPocketed)
                {
                    return false;
                }
            }

            return true;
        }

        private void UpdateAimVisual(PlayerController player)
        {
            if (aimLine == null || cueBall == null || cueBall.IsPocketed)
            {
                return;
            }

            Vector3 direction = AimDirection();
            Vector3 from = cueBall.transform.position;
            aimLine.position = from;
            aimLine.rotation = Quaternion.LookRotation(direction, Vector3.up);
            aimLine.localScale = new Vector3(0.03f, 0.03f, aimLineLength);
            // Центр цилиндра — середина отрезка: сдвигаем вперёд на полдлины.
            aimLine.position = from + direction * (aimLineLength * 0.5f);
        }

        private void SetAimLineVisible(bool visible)
        {
            if (aimLine != null)
            {
                aimLine.gameObject.SetActive(visible);
            }
        }
    }
}
