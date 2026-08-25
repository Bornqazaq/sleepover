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
        [Tooltip("Крышка. Вращается вокруг своей локальной оси X")]
        [SerializeField] private Transform lid;

        [Tooltip("Карточка с галочкой. Включается только в момент раскрытия")]
        [SerializeField] private GameObject winCard;

        [Tooltip("Карточка с крестом. Включается только в момент раскрытия")]
        [SerializeField] private GameObject loseCard;

        [Tooltip("Отблеск из щели: единственное, что видит зал в фазе показа")]
        [SerializeField] private GameObject peekGlow;

        [Tooltip("Облачко в лицо проигравшему. Косметика: персонажа не двигает")]
        [SerializeField] private ParticleSystem gagPuff;

        private Quaternion closedRotation;
        private Coroutine lidRoutine;
        private Coroutine moveRoutine;

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

            SetCardsVisible(false);
            SetGlow(false);
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
        /// Раскрыть коробку: крышка уходит на 90°, карточка появляется.
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
            RotateLid(-90f, seconds);
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

            lidRoutine = StartCoroutine(LidRoutine(closedRotation * Quaternion.Euler(angle, 0f, 0f), seconds));
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
        }
    }
}
