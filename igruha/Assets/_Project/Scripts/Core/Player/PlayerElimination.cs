using System;
using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Окончательное выбывание: одна жизнь, респавна нет. Попадание — нокдаун,
    /// отлёт импульсом от точки удара, через заданное время тело пропадает.
    ///
    /// Нужен всем играм, где смерть окончательна: Уткам Duck Hunt и нокауту
    /// медведем в «Секундомере».
    ///
    /// Рагдолла в проекте нет и не будет — отлёт собран на готовой связке
    /// <see cref="PlayerController.Knockdown"/> и <see cref="PlayerController.ApplyImpulse"/>,
    /// которая сама выбирает клип падения по стороне удара.
    ///
    /// <see cref="Eliminate"/> — единственная точка входа. В сетевой фазе решение
    /// принимает сервер, а сам отлёт проигрывается локально на каждой машине:
    /// поза мёртвого тела ни на что не влияет, синхронизировать её незачем.
    ///
    /// Объект персонажа намеренно остаётся включённым: на нём висят сетевые
    /// компоненты, которые нельзя выключать вместе с телом. Гасятся рендереры,
    /// коллайдеры и ввод, а кто ещё жив — ведёт мини-игра своим списком.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerElimination : MonoBehaviour
    {
        [Tooltip("Сколько тело летит и лежит на виду, прежде чем исчезнуть, с")]
        [SerializeField] private float flightDuration = 1.5f;
        [Tooltip("Слой, на который уходит тело на время отлёта. В матрице столкновений он не должен пересекаться со слоем живых игроков — иначе труп сносит тех, кто ещё бежит, и перекрывает проходы")]
        [SerializeField] private string eliminatedLayerName = "Eliminated";

        /// <summary>Игрок выбыл. Аргумент — точка попадания: мини-игра фиксирует по ней место гибели.</summary>
        public event Action<Vector3> Eliminated;

        /// <summary>Тело отлетало своё и скрылось. Момент, когда игрок уходит в наблюдатели.</summary>
        public event Action Hidden;

        private readonly List<Renderer> renderers = new List<Renderer>(8);
        private readonly List<Collider> colliders = new List<Collider>(4);
        private readonly List<int> originalLayers = new List<int>(4);

        private PlayerController controller;
        private PlayerInputReader inputReader;
        private Rigidbody body;
        private float flightTimer;
        private int eliminatedLayer = -1;

        /// <summary>Игрок выбыл из раунда — насовсем, до конца мини-игры.</summary>
        public bool IsEliminated { get; private set; }

        /// <summary>Тело уже скрыто. Между попаданием и этим моментом проходит время отлёта.</summary>
        public bool IsHidden { get; private set; }

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            inputReader = GetComponent<PlayerInputReader>();
            body = GetComponent<Rigidbody>();

            GetComponentsInChildren(true, renderers);
            GetComponentsInChildren(true, colliders);

            ResolveEliminatedLayer();
        }

        private void ResolveEliminatedLayer()
        {
            if (string.IsNullOrEmpty(eliminatedLayerName))
            {
                return;
            }

            eliminatedLayer = LayerMask.NameToLayer(eliminatedLayerName);
            if (eliminatedLayer < 0)
            {
                Debug.LogWarning(
                    $"{name}: слоя «{eliminatedLayerName}» нет в проекте — мёртвое тело будет сталкиваться с живыми и перекрывать им проход. " +
                    "Завести слой и снять его пересечение со слоем игроков в матрице столкновений.", this);
            }
        }

        /// <summary>
        /// Убить игрока. Точка попадания и направление импульса приходят снаружи:
        /// в сетевой фазе их присылает сервер вместе с фактом смерти.
        ///
        /// Повторные вызовы игнорируются — умереть можно один раз.
        /// </summary>
        public void Eliminate(Vector3 hitPoint, Vector3 impulse)
        {
            if (IsEliminated)
            {
                return;
            }

            IsEliminated = true;
            flightTimer = flightDuration;

            // Сторона падения — как при обычном толчке: летит против взгляда,
            // значит прилетело в лицо. Нокдаун ставится явно, до импульса:
            // слабый выстрел в упор иначе не уронил бы тело вовсе.
            Vector3 flat = new Vector3(impulse.x, 0f, impulse.z).normalized;
            KnockdownType type = Vector3.Dot(flat, controller.Facing) < 0f
                ? KnockdownType.FlyBack
                : KnockdownType.FallForward;

            controller.Knockdown(type);
            controller.ApplyImpulse(impulse, type);

            MoveToEliminatedLayer();
            DisableInput();

            Eliminated?.Invoke(hitPoint);
        }

        private void Update()
        {
            if (!IsEliminated || IsHidden)
            {
                return;
            }

            flightTimer -= Time.deltaTime;
            if (flightTimer <= 0f)
            {
                HideBody();
            }
        }

        private void HideBody()
        {
            IsHidden = true;

            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = false;
                }
            }

            for (int i = 0; i < colliders.Count; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = false;
                }
            }

            // Тело замирает там, где упало: без этого выключенные коллайдеры
            // отпускают его, и труп бесконечно летит вниз сквозь башню.
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            Hidden?.Invoke();
        }

        private void MoveToEliminatedLayer()
        {
            if (eliminatedLayer < 0)
            {
                return;
            }

            originalLayers.Clear();
            for (int i = 0; i < colliders.Count; i++)
            {
                if (colliders[i] == null)
                {
                    originalLayers.Add(0);
                    continue;
                }

                originalLayers.Add(colliders[i].gameObject.layer);
                colliders[i].gameObject.layer = eliminatedLayer;
            }
        }

        private void DisableInput()
        {
            // Манекенам и чужим сетевым копиям ввод и так не принадлежит —
            // трогаем только тот ридер, который реально управляет этой копией.
            if (inputReader != null && inputReader.LocallyControlled)
            {
                inputReader.enabled = false;
            }
        }

        /// <summary>
        /// Вернуть тело в игру: экран результатов, где обязаны стоять все, и
        /// новый раунд. Возвращает и слой, и рендереры, и физику.
        /// </summary>
        public void Restore()
        {
            if (!IsEliminated)
            {
                return;
            }

            IsEliminated = false;
            IsHidden = false;
            flightTimer = 0f;

            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = true;
                }
            }

            for (int i = 0; i < colliders.Count; i++)
            {
                if (colliders[i] == null)
                {
                    continue;
                }

                colliders[i].enabled = true;
                if (i < originalLayers.Count)
                {
                    colliders[i].gameObject.layer = originalLayers[i];
                }
            }

            if (body != null)
            {
                body.isKinematic = false;
            }
        }
    }
}
