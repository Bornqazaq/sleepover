using System;
using UnityEngine;

namespace Igruha.Core.Traps
{
    /// <summary>
    /// Участок пола, который на время исчезает по кнопке. Кто на нём стоял —
    /// падает вниз под обычной гравитацией: специального переноса не нужно,
    /// достаточно убрать опору.
    ///
    /// В Duck Hunt это самая жёсткая из трёх ловушек — откат примерно на этаж.
    /// Та же конструкция стоит в плане «Камер-ловушек» (провал пола, обрушение потолка).
    ///
    /// Состояние — один флаг, меняется только через <see cref="SetOpen"/>:
    /// в сетевой фазе он становится NetworkVariable, а метод — тем, что
    /// применяет реплицированное состояние.
    /// </summary>
    public sealed class CollapsingFloorTrap : TrapBase
    {
        [Header("Участок пола")]
        [Tooltip("Коллайдеры, которые пропадают. Это и есть опора — без них персонаж проваливается сам")]
        [SerializeField] private Collider[] floorColliders;
        [Tooltip("Что перестаёт рисоваться вместе с опорой, чтобы на месте участка была видимая дыра")]
        [SerializeField] private Renderer[] floorRenderers;

        [Header("Срабатывание")]
        [Tooltip("Сколько секунд участок остаётся провалившимся")]
        [SerializeField] private float openDuration = 3f;

        /// <summary>Пол исчез (true) или вернулся (false) — для звука и VFX.</summary>
        public event Action<bool> OpenChanged;

        private float openTimer;

        /// <summary>Участок сейчас провален.</summary>
        public bool IsOpen { get; private set; }

        protected override void OnActivated() => SetOpen(true);

        /// <summary>
        /// Убрать или вернуть опору. Единственная точка смены состояния —
        /// сюда же придёт решение сервера в сетевой фазе.
        /// </summary>
        public void SetOpen(bool open)
        {
            if (IsOpen == open)
            {
                return;
            }

            IsOpen = open;
            openTimer = open ? openDuration : 0f;
            ApplyState(open);
            OpenChanged?.Invoke(open);
        }

        protected override void Update()
        {
            base.Update();

            if (!IsOpen)
            {
                return;
            }

            openTimer -= Time.deltaTime;
            if (openTimer <= 0f)
            {
                SetOpen(false);
            }
        }

        private void ApplyState(bool open)
        {
            for (int i = 0; i < floorColliders.Length; i++)
            {
                if (floorColliders[i] != null)
                {
                    floorColliders[i].enabled = !open;
                }
            }

            for (int i = 0; i < floorRenderers.Length; i++)
            {
                if (floorRenderers[i] != null)
                {
                    floorRenderers[i].enabled = !open;
                }
            }
        }

        /// <summary>Вернуть пол на место. Для старта раунда.</summary>
        public override void ResetTrap()
        {
            base.ResetTrap();
            SetOpen(false);
        }
    }
}
