using UnityEngine;

namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>
    /// Подгоняет кресло под того, кто в нём сидит: корпус с сиденьем, спинкой
    /// и подлокотниками поднимается целиком, ножки тянутся от пола до корпуса.
    ///
    /// <b>Почему кресло, а не поза.</b> Сидячая поза у каждого из восьми
    /// ставит ступни на пол, и при разной длине ног таз оказывается на
    /// разной высоте — от 0.27 м у Fat до 0.48 м у MyBoy и Shlanga. Одно
    /// неподвижное сиденье при таком разбросе либо прорезает бёдра коротким
    /// ногам, либо оставляет длинные висеть в воздухе. Позы выверены под
    /// ступни на полу и камеру у лица, поэтому подстраивается мебель.
    ///
    /// Чисто визуальный компонент: без коллайдеров и без сети. Кто сидит,
    /// знает каждая машина, и подъём она считает себе сама.
    /// </summary>
    public sealed class BelieveChairFit : MonoBehaviour
    {
        [Tooltip("Корпус: сиденье, спинка, подлокотники. Поднимается целиком")]
        [SerializeField] private Transform body;

        [Tooltip("Ножки. Их начало координат на полу, растягиваются только по высоте")]
        [SerializeField] private Transform legs;

        [Tooltip("Высота ножек модели от пола до низа корпуса без подъёма, метры")]
        [SerializeField] private float legHeight = 0.23f;

        [Tooltip("Самые короткие ножки как доля модели: ниже подъём не опускает, иначе корпус ляжет на пол")]
        [SerializeField] private float minLegScale = 0.25f;

        /// <summary>Подключить части кресла. Зовёт билдер сцены.</summary>
        public void Configure(Transform chairBody, Transform chairLegs, float chairLegHeight)
        {
            body = chairBody;
            legs = chairLegs;
            legHeight = chairLegHeight;
        }

        /// <summary>
        /// Поднять кресло на <paramref name="lift"/> метров относительно модели.
        /// Отрицательный подъём опускает корпус для коротких ног.
        /// </summary>
        public void Fit(float lift)
        {
            if (body == null || legs == null || legHeight <= 0f)
            {
                return;
            }

            float legScale = Mathf.Max(minLegScale, (legHeight + lift) / legHeight);
            body.localPosition = Vector3.up * (legHeight * (legScale - 1f));
            legs.localScale = new Vector3(1f, legScale, 1f);
        }
    }
}
