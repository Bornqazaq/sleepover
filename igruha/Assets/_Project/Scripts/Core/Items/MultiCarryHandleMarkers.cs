using UnityEngine;

namespace Igruha.Core.Items
{
    /// <summary>
    /// Показ ручек: где браться и занято ли место.
    ///
    /// Зачем. Ручки живут не в модели, а в расчёте
    /// (<see cref="MultiCarryObject"/>): их число равно составу команды,
    /// а направления пересчитываются от этого же числа. Пока ручек не видно,
    /// игрок не знает ни куда встать, ни осталось ли свободное место, — на
    /// плейтесте это читается как «бутыль не берётся», хотя берётся она
    /// исправно, просто с другой стороны.
    ///
    /// Маркеры лежат в префабе готовыми, лишние выключаются. Ничего не
    /// создаётся на ходу: тару выдают шесть раз за раунд на каждую команду,
    /// и <c>Instantiate</c> на каждую был бы мусором на ровном месте.
    /// </summary>
    [RequireComponent(typeof(MultiCarryObject))]
    public sealed class MultiCarryHandleMarkers : MonoBehaviour
    {
        [Tooltip("Заготовки маркеров, по одной на слот. Лишние выключаются сами")]
        [SerializeField] private Transform[] markers = new Transform[MultiCarryObject.MaxHandles];

        [Tooltip("Свободная ручка — за неё можно взяться")]
        [SerializeField] private Color freeColor = new Color(0.35f, 0.95f, 0.4f);

        [Tooltip("Ручка занята")]
        [SerializeField] private Color takenColor = new Color(0.26f, 0.28f, 0.31f);

        private MultiCarryObject carry;
        private Renderer[][] renderers;
        private bool[] shownTaken;
        private bool[] shownActive;
        private MaterialPropertyBlock block;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            carry = GetComponent<MultiCarryObject>();

            renderers = new Renderer[markers.Length][];
            shownTaken = new bool[markers.Length];
            shownActive = new bool[markers.Length];
            block = new MaterialPropertyBlock();

            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] == null)
                {
                    continue;
                }

                // Красится вся держалка целиком, вместе с креплением: половина
                // зелёная, половина белая читается как поломка, а не как ручка.
                renderers[i] = markers[i].GetComponentsInChildren<Renderer>(true);
                shownActive[i] = markers[i].gameObject.activeSelf;
                shownTaken[i] = false;
                Paint(i, freeColor);
            }
        }

        /// <summary>
        /// Именно <c>LateUpdate</c>: у авторитета объект досчитывается физикой,
        /// у остальных его довозит <c>NetworkTransform</c> в обычном кадре.
        /// Раньше — и маркер отставал бы от бутыли на кадр, что на бегу видно.
        /// </summary>
        private void LateUpdate()
        {
            int count = carry.HandleCount;

            for (int i = 0; i < markers.Length; i++)
            {
                Transform marker = markers[i];
                if (marker == null)
                {
                    continue;
                }

                bool used = i < count;
                if (shownActive[i] != used)
                {
                    marker.gameObject.SetActive(used);
                    shownActive[i] = used;
                }

                if (!used)
                {
                    continue;
                }

                marker.position = carry.HandleAnchor(i);

                // Разворот наружу — по той же оси, по которой ручка отстоит от
                // объекта. Без него держалка с креплением смотрела бы в стену
                // на трёх слотах из четырёх: направления ручек считаются от
                // числа несущих и на модели не закреплены.
                Vector3 outward = carry.StationOf(i) - marker.position;
                outward.y = 0f;
                if (outward.sqrMagnitude > 0.0001f)
                {
                    marker.rotation = Quaternion.LookRotation(outward.normalized, Vector3.up);
                }

                bool taken = carry.CarrierAt(i) != null;
                if (taken != shownTaken[i])
                {
                    shownTaken[i] = taken;
                    Paint(i, taken ? takenColor : freeColor);
                }
            }
        }

        /// <summary>
        /// Через <c>MaterialPropertyBlock</c>, а не через <c>material</c>:
        /// обращение к материалу создаёт копию на каждый маркер, а их до
        /// четырёх на каждой живой бутыли.
        /// </summary>
        private void Paint(int index, Color color)
        {
            Renderer[] targets = renderers[index];
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                Renderer target = targets[i];
                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                target.SetPropertyBlock(block);
            }
        }
    }
}
