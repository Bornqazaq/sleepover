using UnityEngine;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Метка над головой того, чей сейчас ход. Живёт в Core: пометить активного
    /// игрока захочет любая пошаговая мини-игра, а таких в пуле ещё будет.
    ///
    /// <b>Показывает только, ГДЕ игрок.</b> Ни номера шага, ни прогресса, ни
    /// подсказок — иначе это уже не читаемость, а подсказка.
    ///
    /// В «Рейсе на память» без метки игра просто не работает: идущий уходит от
    /// зрителей на сорок метров, силуэт на таком расстоянии теряется, и понять,
    /// на какую из трёх плит он встал, нельзя — то есть запоминать нечего.
    /// </summary>
    /// <remarks>
    /// Масштаб компенсирует дистанцию до камеры, поэтому метка читается
    /// одинаково и в двух метрах, и в сорока. Без компенсации она честно
    /// уменьшается перспективой и к последним рядам превращается в точку.
    /// </remarks>
    public sealed class ActivePlayerMarker : MonoBehaviour
    {
        [Tooltip("На сколько метров метка висит над точкой привязки")]
        [SerializeField] private float heightAboveTarget = 2.4f;
        [Tooltip("Размер метки на расстоянии в один метр. Итоговый размер умножается на дистанцию до камеры")]
        [SerializeField] private float sizePerMeter = 0.035f;
        [Tooltip("Ниже этого размер не падает — вблизи метка не должна схлопываться")]
        [SerializeField] private float minSize = 0.35f;
        [Tooltip("Выше этого не растёт — вдали не должна закрывать пол-арены")]
        [SerializeField] private float maxSize = 1.4f;
        [Tooltip("Амплитуда покачивания, м. Ноль — не качается")]
        [SerializeField] private float bobAmplitude = 0.12f;
        [SerializeField] private float bobSpeed = 2.2f;
        [Tooltip("Визуал метки. Пусто — построится из примитива, этого хватает на блокаут")]
        [SerializeField] private Transform visual;

        private Transform target;
        private Camera activeCamera;

        private void Awake()
        {
            if (visual == null)
            {
                visual = BuildFallbackVisual();
            }

            Clear();
        }

        /// <summary>Повесить метку над игроком. Единственная точка смены цели.</summary>
        public void SetTarget(Transform player)
        {
            target = player;
            gameObject.SetActive(player != null);
        }

        /// <summary>Погасить метку: ходить некому либо раунд кончился.</summary>
        public void Clear() => SetTarget(null);

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            float bob = bobAmplitude > 0f ? Mathf.Sin(Time.time * bobSpeed) * bobAmplitude : 0f;
            transform.position = target.position + Vector3.up * (heightAboveTarget + bob);

            // Камера ищется лениво и кэшируется: в мини-играх активная камера
            // меняется на старте раунда и при переходе в зрители.
            if (activeCamera == null || !activeCamera.isActiveAndEnabled)
            {
                activeCamera = Camera.main;
            }

            if (activeCamera == null)
            {
                return;
            }

            Vector3 toCamera = activeCamera.transform.position - transform.position;
            if (toCamera.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }

            float size = Mathf.Clamp(toCamera.magnitude * sizePerMeter, minSize, maxSize);
            transform.localScale = Vector3.one * size;
        }

        /// <summary>
        /// Заглушка на время блокаута: перевёрнутый конус над головой.
        /// В фазе арта на её место приезжает нормальный визуал через
        /// поле <c>visual</c>, и трогать код для этого не придётся.
        /// </summary>
        private Transform BuildFallbackVisual()
        {
            var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cone.name = "MarkerVisual";
            cone.transform.SetParent(transform, false);
            cone.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
            cone.transform.localScale = new Vector3(0.6f, 0.5f, 0.6f);

            Collider blocker = cone.GetComponent<Collider>();
            if (blocker != null)
            {
                Destroy(blocker);
            }

            // ⚠️ CreatePrimitive вешает Default-Material не из URP: в билде метка
            // стала бы фиолетовой, хотя в редакторе выглядит нормально (STATE 3.9).
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Renderer renderer = cone.GetComponent<Renderer>();
            if (shader != null && renderer != null)
            {
                renderer.sharedMaterial = new Material(shader) { color = new Color(1f, 0.85f, 0.2f) };
            }

            return cone.transform;
        }
    }
}
