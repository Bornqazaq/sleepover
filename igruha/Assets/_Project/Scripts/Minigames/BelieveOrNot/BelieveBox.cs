using System.Collections;
using UnityEngine;

namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>
    /// Коробка на столе: крышка, содержимое, обмен местами и раскрытие.
    ///
    /// <b>Содержимое здесь — визуал, а не источник истины.</b> Что в какой
    /// коробке, решает и помнит контроллер на сервере; коробка только
    /// показывает то, что ей сказали показать, и делает это ровно в момент
    /// раскрытия. Поэтому карточка выключена всё остальное время: её нельзя
    /// подсмотреть ни через щель, ни камерой сверху, потому что её просто
    /// нет в сцене.
    ///
    /// Две коробки обязаны быть <b>абсолютно неразличимы</b>. Это не косметика:
    /// любая примета учит Решающего запоминать коробку вместо чтения человека,
    /// и мини-игра на этом кончается.
    /// </summary>
    public sealed class BelieveBox : MonoBehaviour
    {
        [Tooltip("Крышка. Вращается вокруг своей локальной оси Z — петля сбоку коробки")]
        [SerializeField] private Transform lid;

        [Tooltip("Карточка с галочкой. Включается только в момент раскрытия")]
        [SerializeField] private GameObject winCard;

        [Tooltip("Карточка с крестом. Включается только в момент раскрытия")]
        [SerializeField] private GameObject loseCard;

        [Tooltip("Отблеск из щели: единственное, что видит зал в фазе показа")]
        [SerializeField] private GameObject peekGlow;

        [Tooltip("Облачко в лицо проигравшему. Косметика: персонажа не двигает")]
        [SerializeField] private ParticleSystem gagPuff;

        [Tooltip("Держатель обеих карточек. На раскрытии поднимается над коробкой и " +
                 "разворачивается к камере")]
        [SerializeField] private Transform cardPivot;

        [Tooltip("На сколько карточка поднимается над коробкой при раскрытии, метры")]
        [SerializeField] private float revealLift = 0.45f;

        [Tooltip("Время подъёма карточки, секунды")]
        [SerializeField] private float cardRiseSeconds = 0.35f;

        /// <summary>
        /// Насколько откидывается крышка. Больше 90° намеренно: ровно на 90°
        /// крышка встаёт вертикально и у ближней коробки закрывает собой всё
        /// содержимое от того, кто на неё смотрит.
        /// </summary>
        private const float RevealLidAngle = 108f;

        private Quaternion closedRotation;
        private Vector3 cardHome;
        private Coroutine lidRoutine;
        private Coroutine moveRoutine;
        private Coroutine cardRoutine;
        private bool cardRaised;
        private Camera view;

        /// <summary>
        /// Что лежит внутри. Заполняется только на стороне авторитета
        /// и до раскрытия наружу не отдаётся.
        /// </summary>
        public BelieveCard Card { get; private set; } = BelieveCard.Unknown;

        /// <summary>Чьё место коробка занимает сейчас. Меняется обменом.</summary>
        public BoxSlot Slot { get; private set; } = BoxSlot.None;

        private void Awake()
        {
            if (lid != null)
            {
                closedRotation = lid.localRotation;
            }

            if (cardPivot != null)
            {
                cardHome = cardPivot.localPosition;
            }

            SetCardsVisible(false);
            SetGlow(false);
        }

        /// <summary>
        /// Поднятая карточка держится лицом к камере.
        ///
        /// Плоская карточка на дне коробки не читается вовсе: сидящий смотрит
        /// на стол почти вдоль неё и видит торец в два сантиметра, а зрителю
        /// её закрывает откинутая крышка. Поэтому исход кона показывает не
        /// содержимое коробки, а поднятый над ней знак, развёрнутый к тому,
        /// кто смотрит.
        /// </summary>
        private void LateUpdate()
        {
            if (!cardRaised || cardPivot == null)
            {
                return;
            }

            if (view == null || !view.isActiveAndEnabled)
            {
                view = Camera.main;
            }

            if (view == null)
            {
                return;
            }

            Vector3 toCamera = view.transform.position - cardPivot.position;
            if (toCamera.sqrMagnitude < 0.0001f)
            {
                return;
            }

            cardPivot.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
        }

        /// <summary>
        /// Подготовить коробку к новому кону: крышка закрыта, карточки скрыты,
        /// содержимое и место назначены заново.
        /// </summary>
        public void Prepare(BelieveCard card, BoxSlot slot, Vector3 position)
        {
            StopRoutines();

            Card = card;
            Slot = slot;
            transform.position = position;

            if (lid != null)
            {
                lid.localRotation = closedRotation;
            }

            if (cardPivot != null)
            {
                cardPivot.localPosition = cardHome;
                cardPivot.localRotation = Quaternion.identity;
            }

            cardRaised = false;
            SetCardsVisible(false);
            SetGlow(false);
        }

        /// <summary>
        /// Приподнять крышку «на щёлку»: из коробки бьёт отблеск, но увидеть
        /// внутри нельзя ничего. Карточку Знающий смотрит панелью крупным
        /// планом — открыть крышку по-настоящему нельзя, потому что вокруг
        /// стола стоят зрители, а «открыть только одному» в общем 3D-мире
        /// не бывает (спека 5.5).
        /// </summary>
        public void OpenPeekCrack(float angle, float seconds)
        {
            SetGlow(true);
            RotateLid(-Mathf.Abs(angle), seconds);
        }

        /// <summary>Опустить крышку обратно после показа.</summary>
        public void ClosePeekCrack(float seconds)
        {
            SetGlow(false);
            RotateLid(0f, seconds);
        }

        /// <summary>
        /// Раскрыть коробку: крышка откидывается вбок, знак исхода поднимается
        /// над коробкой и разворачивается к камере.
        /// Обе коробки обязаны получить этот вызов в один кадр — раскрытие
        /// одновременное, иначе зал успевает прочитать исход по первой.
        ///
        /// Карточка приходит параметром, а не берётся из собственного поля,
        /// намеренно: в фазе 3 у клиента этого поля просто нет — содержимое
        /// коробок не покидает сервер до раскрытия, и клиент узнаёт его
        /// в тот же момент, что и все остальные (спека 10.3).
        /// </summary>
        public void Reveal(BelieveCard card, float seconds)
        {
            Card = card;
            SetGlow(false);
            SetCardsVisible(true);
            RotateLid(-RevealLidAngle, seconds);
            RaiseCard();
        }

        /// <summary>Поднять знак над коробкой. Дальше его держит лицом к камере LateUpdate.</summary>
        private void RaiseCard()
        {
            if (cardPivot == null)
            {
                return;
            }

            cardRaised = true;

            if (cardRoutine != null)
            {
                StopCoroutine(cardRoutine);
            }

            cardRoutine = StartCoroutine(CardRiseRoutine());
        }

        private IEnumerator CardRiseRoutine()
        {
            Vector3 from = cardPivot.localPosition;
            Vector3 to = cardHome + Vector3.up * revealLift;
            float elapsed = 0f;

            while (elapsed < cardRiseSeconds)
            {
                elapsed += Time.deltaTime;
                float t = cardRiseSeconds > 0f ? Mathf.Clamp01(elapsed / cardRiseSeconds) : 1f;
                cardPivot.localPosition = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }

            cardPivot.localPosition = to;
            cardRoutine = null;
        }

        /// <summary>Гэг проигравшей коробки. Ничего не двигает и ни на что не влияет.</summary>
        public void PlayGag()
        {
            if (gagPuff != null)
            {
                gagPuff.Play();
            }
        }

        /// <summary>
        /// Переехать на новое место по дуге. Наглядность здесь важнее скорости:
        /// зрители должны видеть, что именно произошло с коробками.
        /// </summary>
        public void MoveToSlot(BoxSlot slot, Vector3 target, float seconds, float arcHeight)
        {
            Slot = slot;

            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
            }

            moveRoutine = StartCoroutine(MoveRoutine(target, seconds, arcHeight));
        }

        private IEnumerator MoveRoutine(Vector3 target, float seconds, float arcHeight)
        {
            Vector3 from = transform.position;
            float span = Vector3.Distance(from, target);
            float peak = span * Mathf.Max(0f, arcHeight);
            float elapsed = 0f;

            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                float t = seconds > 0f ? Mathf.Clamp01(elapsed / seconds) : 1f;
                float eased = Mathf.SmoothStep(0f, 1f, t);

                Vector3 point = Vector3.Lerp(from, target, eased);
                point.y += Mathf.Sin(eased * Mathf.PI) * peak;
                transform.position = point;

                yield return null;
            }

            transform.position = target;
            moveRoutine = null;
        }

        private void RotateLid(float angle, float seconds)
        {
            if (lid == null)
            {
                return;
            }

            if (lidRoutine != null)
            {
                StopCoroutine(lidRoutine);
            }

            lidRoutine = StartCoroutine(LidRoutine(closedRotation * Quaternion.Euler(0f, 0f, angle), seconds));
        }

        private IEnumerator LidRoutine(Quaternion target, float seconds)
        {
            Quaternion from = lid.localRotation;
            float elapsed = 0f;

            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                float t = seconds > 0f ? Mathf.Clamp01(elapsed / seconds) : 1f;
                lid.localRotation = Quaternion.Slerp(from, target, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }

            lid.localRotation = target;
            lidRoutine = null;
        }

        private void SetCardsVisible(bool visible)
        {
            if (winCard != null)
            {
                winCard.SetActive(visible && Card == BelieveCard.Win);
            }

            if (loseCard != null)
            {
                loseCard.SetActive(visible && Card == BelieveCard.Lose);
            }
        }

        private void SetGlow(bool on)
        {
            if (peekGlow != null)
            {
                peekGlow.SetActive(on);
            }
        }

        private void StopRoutines()
        {
            if (lidRoutine != null)
            {
                StopCoroutine(lidRoutine);
                lidRoutine = null;
            }

            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }

            if (cardRoutine != null)
            {
                StopCoroutine(cardRoutine);
                cardRoutine = null;
            }
        }
    }
}
