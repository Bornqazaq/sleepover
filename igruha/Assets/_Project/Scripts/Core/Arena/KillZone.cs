using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Core.Arena
{
    /// <summary>
    /// Зона смерти/падения. Игрок попал → комичное падение уже произошло
    /// (он летел), дальше по режиму: респаун или только событие (выбывание
    /// решают правила мини-игры через onPlayerEntered).
    /// </summary>
    /// <remarks>
    /// <b>Одного триггера мало, и это не перестраховка.</b> Персонажами чужих
    /// машин на сервере двигает <c>ClientNetworkTransform</c>: он переставляет
    /// трансформ раз в сетевой такт, а не ведёт тело физикой. Падающий
    /// разгоняется, шаг между тактами перерастает толщину коробки, и
    /// <c>OnTriggerEnter</c> у сервера не поднимается вовсе — при том, что у
    /// самого владельца всё выглядит нормально. Клиент проваливается мимо зоны
    /// и летит дальше: замерено на «Переноске» (IGR-536), Y уходил ниже −4000,
    /// пока игрок продолжал бегать по X и Z.
    ///
    /// Поэтому та же проверка делается ещё и по высоте: игрок, оказавшийся
    /// внутри следа зоны по горизонтали и ниже её верхней грани, считается
    /// упавшим. Столб вниз, а не коробка — провалиться сквозь него нельзя ни
    /// на какой скорости.
    ///
    /// Двойного срабатывания нет: и триггер, и высота ведут в одно место, а
    /// защёлка отпускает игрока только когда он снова выше зоны.
    /// </remarks>
    [RequireComponent(typeof(Collider))]
    public sealed class KillZone : MonoBehaviour
    {
        public enum ZoneMode
        {
            /// <summary>Вернуть игрока в его точку респауна.</summary>
            Respawn,
            /// <summary>Только событие — правила мини-игры решают сами (выбывание и т.п.).</summary>
            EventOnly
        }

        [SerializeField] private ZoneMode mode = ZoneMode.Respawn;
        [Tooltip("Событие для правил мини-игры: кто попал в зону")]
        [SerializeField] private UnityEvent<PlayerController> onPlayerEntered;

        [Tooltip("Ловить упавших ещё и по высоте, не только триггером. Выключать незачем: " +
                 "быстро падающий клиент сквозь триггер проходит насквозь")]
        [SerializeField] private bool guardByHeight = true;

        /// <summary>След зоны по горизонтали и её верхняя грань — в мировых координатах.</summary>
        private Bounds footprint;

        /// <summary>Кого зона уже засчитала. Отпускает, когда игрок снова выше неё.</summary>
        private readonly HashSet<PlayerController> caught = new HashSet<PlayerController>();

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void Awake()
        {
            footprint = GetComponent<Collider>().bounds;
        }

        /// <summary>
        /// Страховка по высоте. В физическом такте, потому что смотрит на
        /// положение тел, и только у авторитета — как и триггер.
        /// </summary>
        private void FixedUpdate()
        {
            if (!guardByHeight) return;

            IReadOnlyList<SessionPlayer> players = SessionScoreboard.Current?.Players;
            if (players == null) return;

            for (int i = 0; i < players.Count; i++)
            {
                PlayerController player = players[i]?.Avatar;
                if (player == null) continue;

                if (!IsBelow(player.Position))
                {
                    caught.Remove(player);
                    continue;
                }

                // Разбор жалоб «клиент улетел вниз и не вернулся»: строка пишется
                // только когда сработала именно страховка, то есть триггер игрока
                // пропустил. Одна на падение — дальше держит защёлка.
                if (!caught.Contains(player) && player.HasWorldAuthority)
                {
                    Debug.Log($"🕳️ {player.name} провалился мимо триггера «{name}» на Y={player.Position.y:F1} — возвращаю", this);
                }

                Handle(player);
            }
        }

        /// <summary>
        /// Игрок в следе зоны по горизонтали и ниже её верхней грани, то есть
        /// либо внутри неё, либо уже под ней.
        /// </summary>
        private bool IsBelow(Vector3 point)
            => point.y < footprint.max.y
               && point.x >= footprint.min.x && point.x <= footprint.max.x
               && point.z >= footprint.min.z && point.z <= footprint.max.z;

        private void OnTriggerEnter(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null) Handle(player);
        }

        /// <summary>
        /// Единственная точка исхода: и триггер, и высота приходят сюда.
        ///
        /// Проверка авторитета здесь, а не у каждого входа: зона срабатывает на
        /// каждой машине матча, а факт смерти — исход, который решает сервер.
        /// Без неё правила мини-игры получат событие столько раз, сколько в
        /// матче игроков.
        /// </summary>
        private void Handle(PlayerController player)
        {
            if (!player.HasWorldAuthority) return;
            if (!caught.Add(player)) return;

            onPlayerEntered?.Invoke(player);

            if (mode == ZoneMode.Respawn &&
                player.TryGetComponent(out PlayerRespawner respawner))
            {
                respawner.Respawn();
            }
        }
    }
}
