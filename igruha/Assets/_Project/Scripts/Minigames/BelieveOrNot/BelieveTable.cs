using UnityEngine;
using Igruha.Core.UI;

namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>
    /// Стол на двоих: два места, две коробки, две точки камеры и два пузыря
    /// для реплик. Держит всю геометрию кона в одном месте, чтобы контроллер
    /// не разыскивал объекты по сцене.
    ///
    /// Места нумеруются 0 и 1 и стоят строго напротив друг друга. Кто из двоих
    /// Знающий, стол не знает и знать не должен — это правило игры, а не
    /// свойство мебели.
    /// </summary>
    public sealed class BelieveTable : MonoBehaviour
    {
        /// <summary>Мест за столом ровно два. Формат игры — дуэль, и это не параметр.</summary>
        public const int SeatCount = 2;

        [Tooltip("Точки посадки, строго напротив друг друга. Ровно две")]
        [SerializeField] private Transform[] seatAnchors = new Transform[SeatCount];

        [Tooltip("Куда встаёт фиксированная камера сидящего на этом месте. Ровно две")]
        [SerializeField] private Transform[] seatCameraAnchors = new Transform[SeatCount];

        [Tooltip("Куда смотрит камера сидящего: голова оппонента. Ровно две")]
        [SerializeField] private Transform[] seatLookTargets = new Transform[SeatCount];

        [Tooltip("Пузыри реплик над местами. Ровно два")]
        [SerializeField] private SpeechBubble[] seatBubbles = new SpeechBubble[SeatCount];

        [Tooltip("Коробки. Ровно две, и они обязаны быть неразличимы")]
        [SerializeField] private BelieveBox[] boxes = new BelieveBox[SeatCount];

        [Tooltip("Единственная фиксированная камера сцены. Игра переставляет её к нужному месту")]
        [SerializeField] private Transform fixedCameraRig;

        public Transform FixedCameraRig => fixedCameraRig;

        public BelieveBox GetBox(int index) => IsValidSeat(index) ? boxes[index] : null;

        public Transform GetSeatAnchor(int index) => IsValidSeat(index) ? seatAnchors[index] : null;

        public Transform GetCameraAnchor(int index) => IsValidSeat(index) ? seatCameraAnchors[index] : null;

        public Transform GetLookTarget(int index) => IsValidSeat(index) ? seatLookTargets[index] : null;

        public SpeechBubble GetBubble(int index) => IsValidSeat(index) ? seatBubbles[index] : null;

        /// <summary>
        /// Где стоит коробка, приписанная месту. Признак «чья коробка» —
        /// это место на столе, а не сама коробка: их не различить.
        /// </summary>
        public Vector3 GetBoxPosition(int seatIndex)
        {
            Transform anchor = GetSeatAnchor(seatIndex);
            if (anchor == null)
            {
                return transform.position;
            }

            Vector3 toSeat = anchor.position - transform.position;
            toSeat.y = 0f;

            if (toSeat.sqrMagnitude < 0.0001f)
            {
                return transform.position;
            }

            return transform.position + toSeat.normalized * boxOffset + Vector3.up * boxHeight;
        }

        [Header("Раскладка коробок")]
        [Tooltip("Смещение коробки от центра стола к своему владельцу, метры. Ставит билдер из конфига")]
        [SerializeField] private float boxOffset = 0.72f;

        [Tooltip("Высота центра коробки над центром стола, метры. Ставит билдер из конфига")]
        [SerializeField] private float boxHeight = 1.01f;

        /// <summary>Задать раскладку из конфига — зовёт билдер сцены.</summary>
        public void ConfigureLayout(float offsetMeters, float heightMeters)
        {
            boxOffset = offsetMeters;
            boxHeight = heightMeters;
        }

        /// <summary>Спрятать оба пузыря: кон кончился или матч закрылся.</summary>
        public void HideBubbles()
        {
            for (int i = 0; i < SeatCount; i++)
            {
                seatBubbles[i]?.Hide();
            }
        }

        private static bool IsValidSeat(int index) => index >= 0 && index < SeatCount;

        private void OnValidate()
        {
            EnsureLength(ref seatAnchors, nameof(seatAnchors));
            EnsureLength(ref seatCameraAnchors, nameof(seatCameraAnchors));
            EnsureLength(ref seatLookTargets, nameof(seatLookTargets));
            EnsureLength(ref seatBubbles, nameof(seatBubbles));
            EnsureLength(ref boxes, nameof(boxes));
        }

        private void EnsureLength<T>(ref T[] array, string fieldName)
        {
            if (array != null && array.Length == SeatCount)
            {
                return;
            }

            Debug.LogWarning($"{name}: у поля {fieldName} должно быть ровно {SeatCount} элемента — стол на двоих", this);
            System.Array.Resize(ref array, SeatCount);
        }
    }
}
