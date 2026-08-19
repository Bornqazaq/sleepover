using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Окончательное выбывание: одна жизнь, без респавна. Отлёт от удара,
    /// падение, исчезновение тела, дальше игрок смотрит за живыми.
    ///
    /// Нужно и Duck Hunt (смерть Утки), и «Секундомеру» (нокаут медведем),
    /// поэтому живёт в Core.
    ///
    /// Про то, кто сейчас жив, не знает ничего: включить наблюдателя может
    /// только тот, у кого есть состав матча, — правила мини-игры. Отсюда
    /// наружу идут два события, а <see cref="Igruha.Core.CameraSystems.SpectatorCamera"/>
    /// дёргает подписчик.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerElimination : MonoBehaviour
    {
        [Tooltip("Сколько длится отлёт и падение, с")]
        [SerializeField] private float flightDuration = 1.5f;
        [Tooltip("Через сколько секунд после падения исчезает тело, с")]
        [SerializeField] private float bodyHideDelay = 1.5f;
        [Tooltip("Необязательно: корень модели. Пусто — гасятся все рендереры персонажа")]
        [SerializeField] private Transform visualRoot;

        /// <summary>Игрок выбыл. Летит прямо сейчас — тело ещё видно.</summary>
        public event Action<PlayerElimination> Eliminated;

        /// <summary>Тело исчезло. Здесь подписчик включает наблюдателя.</summary>
        public event Action<PlayerElimination> BodyHidden;

        private PlayerController motor;
        private Collider ownCollider;
        private Rigidbody body;
        private readonly List<Collider> ignored = new List<Collider>(8);
        private readonly List<Renderer> visuals = new List<Renderer>(8);
        private Coroutine routine;

        public bool IsEliminated { get; private set; }

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            ownCollider = GetComponent<Collider>();
            body = GetComponent<Rigidbody>();
            CollectRenderers();
        }

        /// <summary>
        /// Убить насмерть. <paramref name="impulse"/> задаёт и силу, и сторону
        /// отлёта: клип падения выбирается по тому, прилетело в лицо или в спину.
        /// </summary>
        public void Eliminate(Vector3 hitPoint, Vector3 impulse)
        {
            if (IsEliminated)
            {
                return;
            }

            IsEliminated = true;

            // В лицо или в спину: тот же выбор, что у толчков и ловушек.
            bool fromFront = Vector3.Dot(impulse.normalized, motor.Facing) < 0f;
            motor.ApplyImpulse(impulse, fromFront ? KnockdownType.FlyBack : KnockdownType.FallForward);

            IgnoreLivingPlayers(true);
            Eliminated?.Invoke(this);

            if (routine != null)
            {
                StopCoroutine(routine);
            }

            routine = StartCoroutine(HideAfterFlight());
        }

        private IEnumerator HideAfterFlight()
        {
            // Пока тело летит и лежит, коллайдер остаётся: он держит труп
            // на земле. С живыми он уже не сталкивается — это сделано
            // отдельно, через IgnoreCollision, ещё в момент смерти.
            yield return new WaitForSeconds(flightDuration + bodyHideDelay);

            // Порядок важен: сначала гасим физику, потом убираем коллайдер.
            // Наоборот тело остаётся без опоры и уходит сквозь пол в минус
            // бесконечность — замерено, улетало на четыре километра вниз.
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            if (ownCollider != null)
            {
                ownCollider.enabled = false;
            }

            SetVisible(false);

            routine = null;
            BodyHidden?.Invoke(this);
        }

        /// <summary>
        /// Гасим рендереры, а не объект. У персонажа меш висит на том же
        /// объекте, что и сетевые компоненты, — SetActive(false) убил бы вместе
        /// с телом и репликацию. Рендереры выключаются независимо от того,
        /// как собрана иерархия конкретной модели.
        /// </summary>
        private void CollectRenderers()
        {
            visuals.Clear();
            Transform source = visualRoot != null ? visualRoot : transform;
            source.GetComponentsInChildren(true, visuals);
        }

        private void SetVisible(bool visible)
        {
            for (int i = 0; i < visuals.Count; i++)
            {
                if (visuals[i] != null)
                {
                    visuals[i].enabled = visible;
                }
            }
        }

        /// <summary>
        /// Вернуть всё как было. Обязателен в конце раунда: персонаж переезжает
        /// между сценами живым, и невидимое тело с выключенным коллайдером
        /// уедет в хаб вместе с ним.
        /// </summary>
        public void Restore()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            IgnoreLivingPlayers(false);

            if (body != null)
            {
                body.isKinematic = false;
            }

            if (ownCollider != null)
            {
                ownCollider.enabled = true;
            }

            SetVisible(true);

            IsEliminated = false;
        }

        /// <summary>
        /// Выбывший не сталкивается с живыми: иначе тело, отлетевшее в яму,
        /// сбивает того, кто там ещё бегает.
        /// </summary>
        private void IgnoreLivingPlayers(bool ignore)
        {
            if (ownCollider == null)
            {
                return;
            }

            if (!ignore)
            {
                for (int i = 0; i < ignored.Count; i++)
                {
                    if (ignored[i] != null && ownCollider != null)
                    {
                        Physics.IgnoreCollision(ownCollider, ignored[i], false);
                    }
                }

                ignored.Clear();
                return;
            }

            var others = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            for (int i = 0; i < others.Length; i++)
            {
                if (others[i] == motor || !others[i].TryGetComponent(out Collider other))
                {
                    continue;
                }

                Physics.IgnoreCollision(ownCollider, other, true);
                ignored.Add(other);
            }
        }
    }
}
