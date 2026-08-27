using System.Collections.Generic;
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

        private readonly List<PlayerController> players = new List<PlayerController>(8);
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
                if (!players.Contains(player))
                {
                    players.Add(player);
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
                players.Remove(player);
                return;
            }

            Rigidbody rigid = other.attachedRigidbody;
            if (rigid != null)
            {
                bodies.Remove(rigid);
            }
        }

        private void FixedUpdate()
        {
            if (players.Count == 0 && bodies.Count == 0)
            {
                return;
            }

            if (!WorldAuthority.HasAuthority)
            {
                return;
            }

            Vector3 push = (direction != null ? direction.forward : transform.forward).normalized *
                           (force * Time.fixedDeltaTime);

            for (int i = players.Count - 1; i >= 0; i--)
            {
                if (players[i] == null)
                {
                    players.RemoveAt(i);
                    continue;
                }

                players[i].ApplyWorldImpulse(push);
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
