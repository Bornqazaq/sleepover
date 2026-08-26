using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Minigames.MemoryRun
{
    /// <summary>
    /// Барьер очереди: сплошная стена, сквозь которую проходит только тот,
    /// чей сейчас ход.
    ///
    /// Нужен не для порядка, а чтобы информация копилась честно. Пусти всех
    /// на плиты — и первый же желающий столкнёт идущего в пропасть до того,
    /// как тот прыгнет, а шаг так и останется неизвестным. Вся игра стоит на
    /// том, что каждый пройденный шаг достаётся зрителям бесплатно.
    ///
    /// <b>Лежит на слое <c>PlayerBarrier</c></b> — это слой сплошной геометрии
    /// из маски препятствий камеры, то есть камера сквозь барьер не пройдёт
    /// (igruha/CLAUDE.md, 2a).
    /// </summary>
    /// <remarks>
    /// Проход открывается через <c>Physics.IgnoreCollision</c>, а не выключением
    /// коллайдера: выключи его целиком — и стена перестанет держать остальных
    /// семерых ровно в тот момент, когда держать нужнее всего.
    ///
    /// В сетевой фазе решение «кому открыт проход» принимает сервер, а клиент
    /// только применяет присланное. Точка входа для этого одна — <see cref="OpenFor"/>.
    /// </remarks>
    public sealed class TurnGate : MonoBehaviour
    {
        [Tooltip("Коллайдер стены. Пусто — возьмётся с этого же объекта")]
        [SerializeField] private Collider barrier;

        private readonly List<Collider> openedFor = new List<Collider>(4);

        private void Awake()
        {
            if (barrier == null)
            {
                barrier = GetComponent<Collider>();
            }
        }

        /// <summary>
        /// Открыть проход одному игроку и закрыть всем остальным.
        /// Единственная точка смены состояния барьера.
        /// </summary>
        public void OpenFor(PlayerController player)
        {
            CloseForAll();

            if (player == null || barrier == null)
            {
                return;
            }

            foreach (Collider collider in player.GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger)
                {
                    continue;
                }

                Physics.IgnoreCollision(barrier, collider, true);
                openedFor.Add(collider);
            }
        }

        /// <summary>
        /// Вернуть стену в исходное состояние. Звать обязательно в конце раунда:
        /// персонаж переезжает между сценами живым, и незакрытое исключение
        /// коллизии уедет вместе с ним.
        /// </summary>
        public void CloseForAll()
        {
            if (barrier == null)
            {
                openedFor.Clear();
                return;
            }

            for (int i = 0; i < openedFor.Count; i++)
            {
                if (openedFor[i] != null)
                {
                    Physics.IgnoreCollision(barrier, openedFor[i], false);
                }
            }

            openedFor.Clear();
        }

        private void OnDisable() => CloseForAll();
    }
}
