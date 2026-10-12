using System;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Session;

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
    /// в сетевой катке его реплицирует мини-игра, а метод — то, чем присланное
    /// состояние применяется. Опору убирает каждая машина у себя: падает игрок
    /// на своей, и пол под ним должен пропасть именно там.
    /// </summary>
    public sealed class CollapsingFloorTrap : TrapBase
    {
        [Header("Участок пола")]
        [Tooltip("Коллайдеры, которые пропадают. Это и есть опора — без них персонаж проваливается сам")]
        [SerializeField] private Collider[] floorColliders;
        [Tooltip("Что перестаёт рисоваться вместе с опорой, чтобы на месте участка была видимая дыра")]
        [SerializeField] private Renderer[] floorRenderers;
        [Tooltip("Независимые секции пола. Если заполнено, секции выключаются целиком вместе со всем декоративным оформлением")]
        [SerializeField] private GameObject[] floorSections = Array.Empty<GameObject>();
        [Tooltip("Сигнальные полосы секций. Горят во время предупреждения и гаснут перед падением своей секции")]
        [SerializeField] private GameObject[] warningObjects = Array.Empty<GameObject>();

        [Header("Срабатывание")]
        [Tooltip("Сколько секунд участок остаётся провалившимся")]
        [SerializeField] private float openDuration = 3f;
        [Tooltip("Сколько секунд пол предупреждает игрока перед первым провалом. Ноль сохраняет старое мгновенное поведение")]
        [SerializeField] private float warningDuration;
        [Tooltip("Задержка между соседними секциями. Ноль открывает весь пол одновременно")]
        [SerializeField] private float sectionDelay;

        /// <summary>Пол исчез (true) или вернулся (false) — для звука и VFX.</summary>
        public event Action<bool> OpenChanged;

        private double cycleStartedAt;
        private bool cycleActive;
        private bool anySectionOpen;

        /// <summary>Участок сейчас провален.</summary>
        public bool IsOpen => anySectionOpen;

        public override bool IsSprung => cycleActive;

        public override void ApplySprung(bool sprung)
        {
            if (!sprung)
            {
                EndCycle();
                return;
            }

            // Событие с точным временем обычно приходит тем же сетевым тиком.
            // Маска остаётся самодостаточной для позднего подключения: если RPC
            // уже прошёл, начинаем цикл с текущей общей отметки.
            if (!cycleActive)
            {
                BeginCycle(NetworkClock.Now);
            }
        }

        protected override void OnActivated() => BeginCycle(NetworkClock.Now);

        protected override void OnRemoteFiredAt(double firedAt) => BeginCycle(firedAt);

        /// <summary>
        /// Убрать или вернуть опору. Единственная точка смены состояния —
        /// сюда же придёт решение сервера в сетевой катке.
        /// </summary>
        public void SetOpen(bool open)
        {
            if (open)
            {
                BeginCycle(NetworkClock.Now - warningDuration -
                    Mathf.Max(0, SectionCount - 1) * sectionDelay);
                return;
            }

            EndCycle();
        }

        protected override void Update()
        {
            base.Update();

            if (!cycleActive)
            {
                return;
            }

            double elapsed = Math.Max(0d, NetworkClock.Now - cycleStartedAt);
            ApplyTimeline((float)elapsed);

            // Визуальное и физическое состояние на каждой машине считается от
            // одних часов. Только авторитет завершает сетевое состояние: клиент
            // ждёт снятия маски и не принимает игровое решение самостоятельно.
            if (WorldAuthority.HasAuthority && elapsed >= TotalDuration)
            {
                EndCycle();
            }
        }

        private int SectionCount => floorSections != null && floorSections.Length > 0
            ? floorSections.Length
            : Mathf.Max(floorColliders?.Length ?? 0, floorRenderers?.Length ?? 0);

        private float TotalDuration => warningDuration +
            Mathf.Max(0, SectionCount - 1) * sectionDelay + openDuration;

        private void BeginCycle(double startedAt)
        {
            cycleStartedAt = startedAt;
            cycleActive = true;
            ApplyTimeline((float)Math.Max(0d, NetworkClock.Now - cycleStartedAt));
        }

        private void EndCycle()
        {
            bool wasOpen = anySectionOpen;
            cycleActive = false;
            anySectionOpen = false;
            ApplyAllClosed();
            SetWarnings(false);
            if (wasOpen)
            {
                OpenChanged?.Invoke(false);
            }
        }

        private void ApplyTimeline(float elapsed)
        {
            bool wasOpen = anySectionOpen;
            anySectionOpen = false;
            int count = SectionCount;

            for (int i = 0; i < count; i++)
            {
                float opensAt = warningDuration + i * sectionDelay;
                bool open = elapsed >= opensAt && elapsed < TotalDuration;
                bool warning = elapsed < opensAt;
                ApplySection(i, open);
                ApplyWarning(i, warning);
                anySectionOpen |= open;
            }

            if (wasOpen != anySectionOpen)
            {
                OpenChanged?.Invoke(anySectionOpen);
            }
        }

        private void ApplySection(int index, bool open)
        {
            if (floorSections != null && floorSections.Length > 0)
            {
                if (index < floorSections.Length && floorSections[index] != null)
                {
                    floorSections[index].SetActive(!open);
                }
                return;
            }

            if (floorColliders != null && index < floorColliders.Length && floorColliders[index] != null)
            {
                floorColliders[index].enabled = !open;
            }
            if (floorRenderers != null && index < floorRenderers.Length && floorRenderers[index] != null)
            {
                floorRenderers[index].enabled = !open;
            }
        }

        private void ApplyWarning(int index, bool visible)
        {
            if (warningObjects != null && index < warningObjects.Length && warningObjects[index] != null)
            {
                warningObjects[index].SetActive(cycleActive && visible);
            }
        }

        private void ApplyAllClosed()
        {
            int count = SectionCount;
            for (int i = 0; i < count; i++)
            {
                ApplySection(i, false);
            }
        }

        private void SetWarnings(bool visible)
        {
            if (warningObjects == null)
            {
                return;
            }
            for (int i = 0; i < warningObjects.Length; i++)
            {
                if (warningObjects[i] != null)
                {
                    warningObjects[i].SetActive(visible);
                }
            }
        }

        /// <summary>Вернуть пол на место. Для старта раунда.</summary>
        public override void ResetTrap()
        {
            base.ResetTrap();
            EndCycle();
        }
    }
}
