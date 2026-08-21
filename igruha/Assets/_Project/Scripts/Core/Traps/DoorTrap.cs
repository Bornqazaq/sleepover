using System;
using UnityEngine;

namespace Igruha.Core.Traps
{
    /// <summary>
    /// Дверь, которая захлопывается по кнопке и через заданное время открывается сама.
    /// Тот, кто подбегал к проходу, вынужден ждать снаружи — в Duck Hunt на
    /// простреливаемом месте. Та же конструкция нужна «Камерам-ловушкам».
    ///
    /// Состояние — один флаг «закрыта», и меняется он только через
    /// <see cref="SetClosed"/>. В сетевой фазе флаг становится NetworkVariable,
    /// а этот метод — тем, что применяет реплицированное состояние на клиенте.
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
        [Tooltip("Сколько дверь держится закрытой, с")]
        [SerializeField] private float closedDuration = 5f;

        /// <summary>Дверь закрылась (true) или открылась (false) — для звука и VFX.</summary>
        public event Action<bool> ClosedChanged;

        private float closedTimer;
        private float blend;

        /// <summary>Дверь сейчас закрыта (в том числе пока едет закрываться).</summary>
        public bool IsClosed { get; private set; }

        private void Start()
        {
            // Стартовое положение выставляем в Start, а не в Awake: створку
            // могли двигать руками в сцене, и до первого кадра её надо привести
            // ровно к «открыто», иначе проём остаётся наполовину перекрытым.
            blend = 0f;
            ApplyBlend();
        }

        protected override void OnActivated() => SetClosed(true);

        /// <summary>
        /// Открыть или закрыть дверь. Единственная точка смены состояния:
        /// сюда же придёт решение сервера в сетевой фазе.
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

            if (IsClosed)
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
