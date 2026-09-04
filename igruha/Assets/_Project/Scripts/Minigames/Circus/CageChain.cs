using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>
    /// Подвес клетки: вытравливает цепь по мере того, как клетка опускается.
    ///
    /// <b>Зачем это вообще появилось.</b> В блокауте цепь была цилиндром
    /// фиксированной длины, посчитанной для верхней ступени. Клетка при этом
    /// ездит вниз на 2.16 м за ошибку, до 6.48 м всего, — и цепь оставалась
    /// висеть на месте. На сером цилиндре это никто не заметил; на настоящей
    /// цепи из звеньев разрыв между её концом и крышей клетки виден сразу
    /// и отменяет главный сигнал игры. Спека 3.4 говорит «подвес — цепь
    /// к ферме», а клетка на оборванной цепи не висит, а левитирует.
    ///
    /// <b>Своего состояния и своих RPC здесь нет.</b> Компонент читает
    /// положение клетки, которое сервер уже синхронизировал через
    /// <c>NetworkTransform</c>, и только пересобирает вид. Поэтому цепь
    /// одинакова у всех сама собой, без единого сетевого вызова.
    ///
    /// Звенья создаются билдером на <b>максимальную</b> длину — ту, что нужна
    /// клетке на нижней ступени. Дальше лишние просто выключаются: включать
    /// и выключать готовые дешевле, чем создавать их в игровом цикле.
    /// </summary>
    /// <remarks>
    /// <c>ExecuteAlways</c> здесь ради арт-фазы: цепь обязана быть правильной
    /// и в редакторе, иначе приёмочный кадр с клетками на разной высоте врёт —
    /// на нём цепи остались бы висеть от верхней ступени. Присваивания идут
    /// только при реальном изменении, поэтому сцена не помечается грязной
    /// на каждом кадре.
    /// </remarks>
    [ExecuteAlways]
    public sealed class CageChain : MonoBehaviour
    {
        [Tooltip("Звенья цепи сверху вниз. Заполняет билдер арены")]
        [SerializeField] private Transform[] links;

        [Tooltip("Крыша клетки — точка, до которой цепь обязана доставать")]
        [SerializeField] private Transform target;

        [Tooltip("Натуральная длина одного звена, м")]
        [SerializeField] private float segmentLength = 2.293f;

        /// <summary>Короче этого звено не показываем: сплюснутое в блин, оно читается мусором.</summary>
        private const float MinVisibleFraction = 0.12f;

        private float topY;

        private void Awake()
        {
            topY = transform.position.y;
        }

        private void LateUpdate()
        {
            Apply();
        }

        /// <summary>
        /// Пересчитать вытравку прямо сейчас.
        ///
        /// Публичный, потому что на него опирается билдер арены: цепь обязана
        /// быть правильной в <b>сохранённой</b> сцене, а не только в игре.
        /// Полагаться на <c>ExecuteAlways</c> для этого нельзя — цикл
        /// редактора не тикает, пока окно Unity не в фокусе, и сцена
        /// сохранялась бы с цепями во всю длину, свисающими в яму.
        /// </summary>
        public void Apply()
        {
            if (links == null || links.Length == 0 || target == null)
            {
                return;
            }

            // В редакторе Awake может не успеть отработать до первого кадра
            // ExecuteAlways, а точка крепления неподвижна — берём её здесь.
            if (!Application.isPlaying)
            {
                topY = transform.position.y;
            }

            float length = Mathf.Max(0f, topY - target.position.y);
            float placed = 0f;

            for (int i = 0; i < links.Length; i++)
            {
                Transform link = links[i];
                if (link == null)
                {
                    continue;
                }

                float remaining = length - placed;
                if (remaining <= segmentLength * MinVisibleFraction)
                {
                    SetVisible(link, false);
                    continue;
                }

                float fraction = Mathf.Min(1f, remaining / segmentLength);
                SetVisible(link, true);
                Place(link, -placed, fraction);
                placed += segmentLength * fraction;
            }
        }

        /// <summary>
        /// Присваивать только при реальном изменении. Без этой проверки
        /// <c>ExecuteAlways</c> метил бы сцену грязной каждый кадр редактора.
        /// </summary>
        private static void SetVisible(Transform link, bool visible)
        {
            if (link.gameObject.activeSelf != visible)
            {
                link.gameObject.SetActive(visible);
            }
        }

        private static void Place(Transform link, float y, float fraction)
        {
            var position = new Vector3(0f, y, 0f);
            if ((link.localPosition - position).sqrMagnitude > 1e-8f)
            {
                link.localPosition = position;
            }

            var scale = new Vector3(1f, fraction, 1f);
            if ((link.localScale - scale).sqrMagnitude > 1e-8f)
            {
                link.localScale = scale;
            }
        }
    }
}
