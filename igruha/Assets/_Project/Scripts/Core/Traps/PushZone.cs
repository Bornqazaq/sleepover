using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Core.Traps
{
    /// <summary>
    /// Постоянная зона выталкивания: струя воды из прорванной трубы, вентиляция,
    /// поток воздуха. Не роняет и не отнимает ничего — только сбивает курс, и
    /// этого достаточно, чтобы несущие разошлись и уронили то, что тащат.
    ///
    /// Толкает непрерывно, малыми импульсами за такт физики, а не одним ударом.
    /// Разница не косметическая: одиночный импульс той же величины перевалил бы
    /// порог нокдауна в <c>CharacterConfig</c> и ронял бы каждого вошедшего, а
    /// зона обязана именно сбивать, а не валить.
    ///
    /// Обычные тела в зоне тоже сносит: брошенная бутыль, кирпич, тачка.
    ///
    /// <b>Сеть: каждая машина толкает своего.</b> Зона не событие, а
    /// непрерывная сила, и уходит она не в счёт, а в курс — поэтому её вправе
    /// применять сам владелец персонажа. Через сервер это было бы полсотни
    /// пакетов в секунду на каждого стоящего в струе: <c>ApplyWorldImpulse</c>
    /// у авторитета шлёт владельцу отдельное сообщение на каждый такт физики.
    /// Тот же приём, что у тяги в <c>MultiCarryObject</c>.
    ///
    /// Обычные тела остаются за сервером: их физику считает он один, и
    /// остальным они приезжают <c>NetworkTransform</c>.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PushZone : MonoBehaviour
    {
        [Tooltip("Куда толкает. Пусто — вдоль forward этого объекта")]
        [SerializeField] private Transform direction;
        [Tooltip("Сила выталкивания, Н. Для струи трубы — CharacterConfig.pushForce × 0.6")]
        [SerializeField] private float force = 8.4f;
        [Tooltip("Толкать ли обычные тела: брошенную тару, кирпичи, тачку")]
        [SerializeField] private bool affectsRigidbodies = true;

        /// <summary>
        /// Персонаж в зоне вместе со своим сетевым объектом. Пара, а не два
        /// списка: <c>NetworkObject</c> спрашивают каждый такт физики, и звать
        /// ради него <c>GetComponent</c> в цикле нельзя.
        /// </summary>
        private struct Occupant
        {
            public PlayerController Player;
            public NetworkObject Net;

            /// <summary>Ведёт ли персонажа эта машина. Вне сети — всегда: мотор здесь же.</summary>
            public bool LocallyOwned => Net == null || !Net.IsSpawned || Net.IsOwner;
        }

        private readonly List<Occupant> players = new List<Occupant>(8);
        private readonly List<Rigidbody> bodies = new List<Rigidbody>(8);

        /// <summary>Сила выталкивания, Н. Задаётся мини-игрой из её конфига.</summary>
        public float Force
        {
            get => force;
            set => force = Mathf.Max(0f, value);
        }

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnDisable()
        {
            players.Clear();
            bodies.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                if (IndexOf(player) < 0)
                {
                    players.Add(new Occupant
                    {
                        Player = player,
                        Net = player.GetComponent<NetworkObject>()
                    });
                }

                return;
            }

            if (!affectsRigidbodies)
            {
                return;
            }

            Rigidbody rigid = other.attachedRigidbody;
            if (rigid != null && !rigid.isKinematic && !bodies.Contains(rigid))
            {
                bodies.Add(rigid);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                int index = IndexOf(player);
                if (index >= 0)
                {
                    players.RemoveAt(index);
                }

                return;
            }

            Rigidbody rigid = other.attachedRigidbody;
            if (rigid != null)
            {
                bodies.Remove(rigid);
            }
        }

        private int IndexOf(PlayerController player)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Player == player)
                {
                    return i;
                }
            }

            return -1;
        }

        private void FixedUpdate()
        {
            if (players.Count == 0 && bodies.Count == 0)
            {
                return;
            }

            Vector3 push = (direction != null ? direction.forward : transform.forward).normalized *
                           (force * Time.fixedDeltaTime);

            for (int i = players.Count - 1; i >= 0; i--)
            {
                Occupant occupant = players[i];
                if (occupant.Player == null)
                {
                    players.RemoveAt(i);
                    continue;
                }

                // Только своего: чужого ведёт его машина, и толчок отсюда всё
                // равно был бы перетёрт. ApplyImpulse, а не ApplyWorldImpulse, —
                // решать здесь нечего, сила одинакова у всех и по геометрии
                // зоны видна каждому.
                if (occupant.LocallyOwned)
                {
                    occupant.Player.ApplyImpulse(push);
                }
            }

            // Обычные тела считает сервер: их физика целиком на нём.
            if (!WorldAuthority.HasAuthority)
            {
                return;
            }

            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                if (bodies[i] == null)
                {
                    bodies.RemoveAt(i);
                    continue;
                }

                bodies[i].AddForce(push, ForceMode.Impulse);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 arrow = (direction != null ? direction.forward : transform.forward).normalized;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, transform.position + arrow * 2f);
        }
    }
}
