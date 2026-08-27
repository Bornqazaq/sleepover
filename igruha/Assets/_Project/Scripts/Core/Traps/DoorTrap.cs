using System;
using UnityEngine;
using Igruha.Core.Session;

namespace Igruha.Core.Traps
{
    /// <summary>
    /// Дверь, которая захлопывается по нажатию.
    /// Тот, кто подбегал к проходу, вынужден ждать снаружи — в Duck Hunt на
    /// простреливаемом месте. Та же конструкция нужна «Камерам-ловушкам».
    ///
    /// Каждое нажатие переключает створку: закрыта — откроется, открыта —
    /// закроется. Выдержка при этом не обязательна: с нулевой дверь стоит
    /// закрытой, пока её не откроют тем же рычагом, и открывать-закрывать
    /// можно сколько угодно раз подряд.
    ///
    /// Состояние — один флаг «закрыта», и меняется он только через
    /// <see cref="SetClosed"/>. В сетевой катке флаг реплицирует мини-игра,
    /// а этот метод — то, чем присланное состояние применяется на клиенте.
    /// </summary>
    public sealed class DoorTrap : TrapBase
    {
        [Header("Створка")]
        [Tooltip("Что двигается. Коллайдер створки и есть то, что перекрывает проход")]
        [SerializeField] private Transform doorBody;
        [Tooltip("Локальная позиция створки в открытом виде — обычно убрана в пол или потолок")]
        [SerializeField] private Vector3 openLocalPosition;
        [Tooltip("Локальная позиция створки в закрытом виде — перекрывает проём")]
        [SerializeField] private Vector3 closedLocalPosition;
        [Tooltip("За сколько секунд створка проходит путь между открытым и закрытым положением")]
        [SerializeField] private float moveDuration = 0.25f;

        [Header("Срабатывание")]
        [Tooltip("Сколько дверь держится закрытой, с. Ноль — стоит закрытой, пока не откроют нажатием")]
        [SerializeField] private float closedDuration = 5f;

        /// <summary>Дверь закрылась (true) или открылась (false) — для звука и VFX.</summary>
        public event Action<bool> ClosedChanged;

        private float closedTimer;
        private float blend;

        /// <summary>Дверь сейчас закрыта (в том числе пока едет закрываться).</summary>
        public bool IsClosed { get; private set; }

        public override bool IsSprung => IsClosed;

        public override void ApplySprung(bool sprung) => SetClosed(sprung);

        private void Start()
        {
            // Стартовое положение выставляем в Start, а не в Awake: створку
            // могли двигать руками в сцене, и до первого кадра её надо привести
            // ровно к «открыто», иначе проём остаётся наполовину перекрытым.
            blend = 0f;
            ApplyBlend();
        }

        /// <summary>
        /// Нажатие переключает створку, а не закрывает её. Иначе дверь,
        /// оставленную закрытой, нечем открыть обратно: с нулевой выдержкой
        /// она так и стояла бы до конца раунда.
        /// </summary>
        protected override void OnActivated() => SetClosed(!IsClosed);

        /// <summary>
        /// Открыть или закрыть дверь. Единственная точка смены состояния:
        /// сюда же придёт решение сервера в сетевой катке.
        /// </summary>
        public void SetClosed(bool closed)
        {
            if (IsClosed == closed)
            {
                return;
            }

            IsClosed = closed;
            closedTimer = closed ? closedDuration : 0f;
            ClosedChanged?.Invoke(closed);
        }

        protected override void Update()
        {
            base.Update();

            // Открывает дверь обратно тот же, кто её закрыл. На клиенте отсчёт
            // не идёт: он открыл бы створку по своему таймеру, а сервер — по
            // своему, и на разнице в полпинга Утка успевала бы пройти проём,
            // которого на сервере ещё нет. Нулевая выдержка отсчёт отключает
            // целиком — дверь ждёт следующего нажатия.
            if (IsClosed && closedDuration > 0f && WorldAuthority.HasAuthority)
            {
                closedTimer -= Time.deltaTime;
                if (closedTimer <= 0f)
                {
                    SetClosed(false);
                }
            }

            float step = moveDuration > 0f ? Time.deltaTime / moveDuration : 1f;
            float target = IsClosed ? 1f : 0f;
            if (Mathf.Approximately(blend, target))
            {
                return;
            }

            blend = Mathf.MoveTowards(blend, target, step);
            ApplyBlend();
        }

        private void ApplyBlend()
        {
            if (doorBody == null)
            {
                return;
            }

            doorBody.localPosition = Vector3.Lerp(openLocalPosition, closedLocalPosition, blend);
        }

        /// <summary>Вернуть дверь в исходное открытое состояние. Для старта раунда.</summary>
        public override void ResetTrap()
        {
            base.ResetTrap();
            SetClosed(false);
            blend = 0f;
            ApplyBlend();
        }

        private void OnDrawGizmosSelected()
        {
            if (doorBody == null)
            {
                return;
            }

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(openLocalPosition, 0.15f);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(closedLocalPosition, 0.15f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(openLocalPosition, closedLocalPosition);
        }
    }
}
