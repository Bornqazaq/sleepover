using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Igruha.Core.Player;

namespace Igruha.Core.Arena
{
    /// <summary>
    /// Скат: попал на него — едешь вниз, и управление на это не влияет.
    /// Горка «Заражения», жёлоб, наклонная труба.
    ///
    /// <b>Почему не <see cref="PlayerController.MovementLocked"/>:</b> блокировка
    /// гасит горизонтальную скорость, то есть ровно то, ради чего скат нужен.
    /// Поэтому едущему каждый такт задаётся скорость вдоль ската, а ввод просто
    /// перетирается — камера при этом остаётся живой, и падение видно.
    ///
    /// <b>Почему порядок исполнения задан явно:</b> скорость назначает
    /// <see cref="PlayerController"/> в своём <c>FixedUpdate</c>, и наш такт
    /// обязан идти после него — иначе ввод перетирает скат, а не наоборот.
    ///
    /// Подъём снизу этим же и закрывается: вошедший с нижнего края получает ту
    /// же скорость вниз, сколько бы он ни жал вперёд. Односторонний путь
    /// выходит из физики, а не из невидимой стены.
    ///
    /// По сети не синхронизируется: скорость задаётся от геометрии, одинаковой
    /// у всех, а чужие копии персонажей не трогаются вовсе — их везёт владелец.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [DefaultExecutionOrder(200)]
    public sealed class SlideSurface : MonoBehaviour
    {
        [Tooltip("Куда едет скат. Пусто — локальное «вперёд» самого объекта")]
        [SerializeField] private Transform downhill;

        [Tooltip("Начальная скорость, м/с: с неё съезжает тот, кто ступил на скат стоя")]
        [SerializeField] private float entrySpeed = 3f;

        [Tooltip("Разгон вдоль ската, м/с²")]
        [SerializeField] private float acceleration = 12f;

        [Tooltip("Предельная скорость съезда, м/с")]
        [SerializeField] private float maxSpeed = 11f;

        [Tooltip("Добавка скорости на выходе, м/с: выносит с конца ската вперёд, а не роняет на месте")]
        [SerializeField] private float exitBoost = 2f;

        private readonly List<Rider> riders = new List<Rider>(8);

        private sealed class Rider
        {
            public PlayerController Player;
            public Rigidbody Body;
            public NetworkObject Net;
            public float Speed;
        }

        /// <summary>Направление съезда, мировое, по горизонтали.</summary>
        public Vector3 Downhill
        {
            get
            {
                Transform source = downhill != null ? downhill : transform;
                Vector3 flat = source.forward;
                flat.y = 0f;
                return flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;
            }
        }

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnDisable()
        {
            // Скат выключили посреди съезда (конец раунда, выгрузка сцены) —
            // никто не должен уехать в хаб с чужой скоростью в ногах.
            riders.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null || IndexOf(player) >= 0)
            {
                return;
            }

            if (!player.TryGetComponent(out Rigidbody body))
            {
                return;
            }

            player.TryGetComponent(out NetworkObject net);
            riders.Add(new Rider
            {
                Player = player,
                Body = body,
                Net = net,
                Speed = entrySpeed
            });
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null)
            {
                return;
            }

            int index = IndexOf(player);
            if (index < 0)
            {
                return;
            }

            Rider rider = riders[index];
            riders.RemoveAt(index);

            if (!CanDrive(rider))
            {
                return;
            }

            Vector3 velocity = rider.Body.linearVelocity;
            Vector3 push = Downhill * exitBoost;
            rider.Body.linearVelocity = new Vector3(velocity.x + push.x, velocity.y, velocity.z + push.z);
        }

        private void FixedUpdate()
        {
            if (riders.Count == 0)
            {
                return;
            }

            Vector3 direction = Downhill;

            for (int i = riders.Count - 1; i >= 0; i--)
            {
                Rider rider = riders[i];
                if (rider.Player == null || rider.Body == null)
                {
                    riders.RemoveAt(i);
                    continue;
                }

                if (!CanDrive(rider))
                {
                    continue;
                }

                // Сбитого качелями или толчком скат не отнимает у нокдауна:
                // он и так катится вниз телом, а перебивать клип падения
                // управляемой скоростью — значит терять само падение.
                if (rider.Player.IsKnockedDown)
                {
                    continue;
                }

                rider.Speed = Mathf.Min(maxSpeed, rider.Speed + acceleration * Time.fixedDeltaTime);
                Vector3 velocity = rider.Body.linearVelocity;
                Vector3 flat = direction * rider.Speed;
                rider.Body.linearVelocity = new Vector3(flat.x, velocity.y, flat.z);
            }
        }

        /// <summary>Чужую копию персонажа не трогаем: её скорость решает владелец.</summary>
        private static bool CanDrive(Rider rider) =>
            rider.Net == null || !rider.Net.IsSpawned || rider.Net.IsOwner;

        private int IndexOf(PlayerController player)
        {
            for (int i = 0; i < riders.Count; i++)
            {
                if (riders[i].Player == player)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
