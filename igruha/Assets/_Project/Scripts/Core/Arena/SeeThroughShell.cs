using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Arena
{
    /// <summary>
    /// Оболочка, сквозь которую становится видно, когда внутрь кто-то зашёл:
    /// труба, короб, кабина. Пока внутри пусто — обычная непрозрачная стенка;
    /// зашёл игрок — стенка гаснет до силуэта.
    ///
    /// Проектный стандарт: <b>никто не должен врезаться в невидимое</b>. Прятки
    /// внутри укрытия обязаны читаться снаружи хотя бы силуэтом, иначе погоня
    /// превращается в угадайку, а игрок — в жертву чужой геометрии.
    ///
    /// Прозрачность локальная и на состояние игры не влияет: это вид, а не
    /// правило, и по сети её гонять незачем. Материал меняется на всю оболочку
    /// сразу — пофрагментное «окно вокруг игрока» стоило бы шейдера ради того,
    /// что и так читается.
    ///
    /// Триггер-зона и видимая оболочка — разные объекты: коллайдер трубы
    /// сплошной (сквозь стенку не пройти), а зона занимает её просвет.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class SeeThroughShell : MonoBehaviour
    {
        [Tooltip("Что гасим. Пусто — рендереры этого объекта и его детей")]
        [SerializeField] private Renderer[] shell = System.Array.Empty<Renderer>();

        [Tooltip("Прозрачный материал стенки: подставляется, пока внутри есть игрок")]
        [SerializeField] private Material transparentMaterial;

        [Tooltip("Сколько секунд занимает переход. Мгновенное переключение читается как моргание")]
        [SerializeField] private float fadeSeconds = 0.15f;

        private readonly List<PlayerController> inside = new List<PlayerController>(8);
        private Material[] solidMaterials;
        private float blend;

        /// <summary>Внутри кто-то есть. Звук и арт берут состояние отсюда, а не считают заново.</summary>
        public bool Occupied => inside.Count > 0;

        private void Awake()
        {
            if (shell.Length == 0)
            {
                shell = GetComponentsInChildren<Renderer>();
            }

            solidMaterials = new Material[shell.Length];
            for (int i = 0; i < shell.Length; i++)
            {
                solidMaterials[i] = shell[i] != null ? shell[i].sharedMaterial : null;
            }
        }

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnDisable()
        {
            inside.Clear();
            blend = 0f;
            Apply(false);
        }

        private void OnTriggerEnter(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null || inside.Contains(player))
            {
                return;
            }

            inside.Add(player);
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null)
            {
                return;
            }

            inside.Remove(player);
        }

        private void Update()
        {
            // Игрок мог выйти вместе со сценой или умереть внутри — тогда
            // OnTriggerExit не придёт вовсе, и труба осталась бы прозрачной.
            for (int i = inside.Count - 1; i >= 0; i--)
            {
                if (inside[i] == null)
                {
                    inside.RemoveAt(i);
                }
            }

            float target = Occupied ? 1f : 0f;
            float step = fadeSeconds > 0f ? Time.deltaTime / fadeSeconds : 1f;
            float next = Mathf.MoveTowards(blend, target, step);
            if (Mathf.Approximately(next, blend))
            {
                return;
            }

            bool wasTransparent = blend > 0.5f;
            blend = next;
            bool isTransparent = blend > 0.5f;
            if (wasTransparent != isTransparent)
            {
                Apply(isTransparent);
            }
        }

        private void Apply(bool transparent)
        {
            if (shell == null || solidMaterials == null)
            {
                return;
            }

            for (int i = 0; i < shell.Length; i++)
            {
                if (shell[i] == null)
                {
                    continue;
                }

                Material material = transparent && transparentMaterial != null
                    ? transparentMaterial
                    : solidMaterials[i];

                if (material != null)
                {
                    shell[i].sharedMaterial = material;
                }
            }
        }
    }
}
