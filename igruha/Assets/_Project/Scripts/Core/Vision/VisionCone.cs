using System;
using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Core.Vision
{
    /// <summary>
    /// «Кто кого видит»: горизонтальный конус обзора с проверкой перекрытий.
    /// Общий компонент — луч Водящего в «Плачущих ангелах», камеры наблюдения,
    /// поиск в «Прятках». Сам компонент ничего не решает: он только сообщает,
    /// что цель вошла в конус или вышла из него, и не знает о мини-играх.
    ///
    /// Проверка идёт в горизонтальной плоскости: наклон источника вверх-вниз
    /// на результат не влияет. Иначе у ног источника получается мёртвая зона,
    /// в которой присевшая цель невидима, и правило перестаёт читаться.
    /// </summary>
    public sealed class VisionCone : MonoBehaviour
    {
        [Tooltip("Откуда смотрим (глаз, фонарь). Пусто — трансформ самого компонента")]
        [SerializeField] private Transform origin;
        [Tooltip("Полный угол конуса по горизонтали, °")]
        [Range(1f, 360f)]
        [SerializeField] private float coneAngle = 35f;
        [Tooltip("Дальность обзора по горизонтали, м")]
        [SerializeField] private float range = 14.4f;
        [Tooltip("Что перекрывает обзор: укрытия, стены. Слой самих целей сюда не включать")]
        [SerializeField] private LayerMask blockers;
        [Tooltip("Насколько ниже макушки цели бьёт второй луч, м — чтобы не цеплять кромку коллайдера")]
        [SerializeField] private float headMargin = 0.1f;

        /// <summary>Цель вошла в конус и видна.</summary>
        public event Action<Collider> TargetEntered;

        /// <summary>Цель перестала быть видимой: вышла, спряталась или исчезла из списка.</summary>
        public event Action<Collider> TargetExited;

        public Transform Origin => origin != null ? origin : transform;
        public float ConeAngle => coneAngle;
        public float Range => range;

        private readonly HashSet<Collider> visible = new HashSet<Collider>();
        private readonly HashSet<Collider> present = new HashSet<Collider>();
        private readonly List<Collider> pendingExit = new List<Collider>(8);

        public bool IsVisible(Collider target) => target != null && visible.Contains(target);

        /// <summary>
        /// Пересчитать видимость всего списка и разослать события входа/выхода.
        /// Буферы переиспользуются — вызов не аллоцирует и годится для FixedUpdate.
        /// </summary>
        public void Evaluate(IReadOnlyList<Collider> targets)
        {
            if (targets == null)
            {
                return;
            }

            present.Clear();
            pendingExit.Clear();

            for (int i = 0; i < targets.Count; i++)
            {
                Collider target = targets[i];
                if (target == null)
                {
                    continue;
                }

                present.Add(target);

                bool seen = CanSee(target);
                bool wasSeen = visible.Contains(target);

                if (seen && !wasSeen)
                {
                    visible.Add(target);
                    TargetEntered?.Invoke(target);
                }
                else if (!seen && wasSeen)
                {
                    pendingExit.Add(target);
                }
            }

            // Цель исчезла из списка (выбыла, дисконнект) — считаем, что вышла из конуса,
            // иначе потребитель останется с зависшим «вижу» и будет держать эффект вечно.
            foreach (Collider tracked in visible)
            {
                if (!present.Contains(tracked))
                {
                    pendingExit.Add(tracked);
                }
            }

            for (int i = 0; i < pendingExit.Count; i++)
            {
                visible.Remove(pendingExit[i]);
                TargetExited?.Invoke(pendingExit[i]);
            }

            pendingExit.Clear();
        }

        /// <summary>Видна ли цель прямо сейчас. Состояние конуса не меняет.</summary>
        public bool CanSee(Collider target)
        {
            if (target == null)
            {
                return false;
            }

            Transform eye = Origin;
            Vector3 eyePosition = eye.position;
            Bounds bounds = target.bounds;
            Vector3 center = bounds.center;

            // Угол берётся по центру капсулы, а не по её краю: на границе конуса
            // край дребезжит и цель мигает «вижу — не вижу» каждый тик.
            Vector3 toTarget = center - eyePosition;
            toTarget.y = 0f;

            float distanceSqr = toTarget.sqrMagnitude;
            if (distanceSqr > range * range || distanceSqr < 0.0001f)
            {
                return false;
            }

            Vector3 forward = eye.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            if (Vector3.Angle(forward, toTarget) > coneAngle * 0.5f)
            {
                return false;
            }

            // Два луча ради правила «за низким укрытием стоя видно голову, присев — нет»:
            // у стоящего макушка торчит над укрытием, у присевшего обе точки за ним.
            Vector3 head = new Vector3(center.x, bounds.max.y - headMargin, center.z);
            return HasLineOfSight(eyePosition, center) || HasLineOfSight(eyePosition, head);
        }

        private bool HasLineOfSight(Vector3 eyePosition, Vector3 point)
        {
            Vector3 delta = point - eyePosition;
            float distance = delta.magnitude;
            if (distance < 0.0001f)
            {
                return true;
            }

            return !Physics.Raycast(eyePosition, delta / distance, distance, blockers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Забыть всё, что видели: смена раунда, выключение фонаря.</summary>
        public void ResetVisibility()
        {
            pendingExit.Clear();
            foreach (Collider tracked in visible)
            {
                pendingExit.Add(tracked);
            }

            visible.Clear();

            for (int i = 0; i < pendingExit.Count; i++)
            {
                TargetExited?.Invoke(pendingExit[i]);
            }

            pendingExit.Clear();
        }

#if UNITY_EDITOR
        private const int GizmoArcSegments = 16;

        /// <summary>
        /// Гизмо конуса и лучей к видимым целям — по нему проверяется,
        /// что на арене нет мёртвых зон при полном обороте источника.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            Transform eye = Origin;
            Vector3 eyePosition = eye.position;

            Vector3 forward = eye.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return;
            }

            forward.Normalize();
            float halfAngle = coneAngle * 0.5f;

            Gizmos.color = Color.yellow;
            Vector3 previous = eyePosition + Quaternion.Euler(0f, -halfAngle, 0f) * forward * range;
            Gizmos.DrawLine(eyePosition, previous);

            for (int i = 1; i <= GizmoArcSegments; i++)
            {
                float angle = -halfAngle + coneAngle * i / GizmoArcSegments;
                Vector3 point = eyePosition + Quaternion.Euler(0f, angle, 0f) * forward * range;
                Gizmos.DrawLine(previous, point);
                previous = point;
            }

            Gizmos.DrawLine(eyePosition, previous);

            Gizmos.color = Color.red;
            foreach (Collider tracked in visible)
            {
                if (tracked == null)
                {
                    continue;
                }

                Bounds bounds = tracked.bounds;
                Gizmos.DrawLine(eyePosition, bounds.center);
                Gizmos.DrawLine(eyePosition, new Vector3(bounds.center.x, bounds.max.y - headMargin, bounds.center.z));
            }
        }
#endif
    }
}
