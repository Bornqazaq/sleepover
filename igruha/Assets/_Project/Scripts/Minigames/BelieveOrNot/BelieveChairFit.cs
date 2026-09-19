using UnityEngine;

namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>
    /// Подгоняет кресло под того, кто в нём сидит: корпус с сиденьем, спинкой
    /// и подлокотниками поднимается целиком, ножки тянутся от пола до корпуса,
    /// и всё кресло придвигается к сидящему.
    ///
    /// <b>Почему кресло, а не поза.</b> Сидячая поза у каждого из восьми
    /// ставит ступни на пол, и при разной длине ног таз оказывается на
    /// разной высоте — от 0.27 м у Fat до 0.48 м у MyBoy и Shlanga. Одно
    /// неподвижное сиденье при таком разбросе либо прорезает бёдра коротким
    /// ногам, либо оставляет длинные висеть в воздухе. Голени поза держит
    /// под бёдрами, поэтому край сиденья обязан стоять за икрами, и без
    /// сдвига к сидящему худой персонаж оказывается на самом краю, а между
    /// его спиной и спинкой — пустота. Позы выверены под ступни на полу и
    /// камеру у лица, поэтому подстраивается мебель.
    ///
    /// Чисто визуальный компонент: без коллайдеров и без сети. Кто сидит,
    /// знает каждая машина, и подгонку она ставит себе сама.
    /// </summary>
    public sealed class BelieveChairFit : MonoBehaviour
    {
        /// <summary>Самые короткие ножки как доля модели: ниже корпус не опускается, иначе лёг бы на пол.</summary>
        public const float MinLegScale = 0.25f;

        [Tooltip("Корпус: сиденье, спинка, подлокотники. Поднимается целиком")]
        [SerializeField] private Transform body;

        [Tooltip("Ножки. Их начало координат на полу, растягиваются только по высоте")]
        [SerializeField] private Transform legs;

        [Tooltip("Высота ножек модели от пола до низа корпуса без подъёма, метры")]
        [SerializeField] private float legHeight = 0.23f;

        /// <summary>Во сколько раз вытянуть ножки под подъём <paramref name="lift"/>.</summary>
        public static float LegScale(float lift, float legHeight) =>
            Mathf.Max(MinLegScale, (legHeight + lift) / legHeight);

        /// <summary>На сколько реально поднимется корпус: подъём, срезанный по самым коротким ножкам.</summary>
        public static float BodyRise(float lift, float legHeight) =>
            legHeight * (LegScale(lift, legHeight) - 1f);

        /// <summary>Подключить части кресла. Зовёт билдер сцены.</summary>
        public void Configure(Transform chairBody, Transform chairLegs, float chairLegHeight)
        {
            body = chairBody;
            legs = chairLegs;
            legHeight = chairLegHeight;
        }

        /// <summary>
        /// Поднять кресло на <paramref name="lift"/> метров и придвинуть к
        /// сидящему на <paramref name="forward"/> метров. Отрицательный подъём
        /// опускает корпус для коротких ног.
        /// </summary>
        public void Fit(float lift, float forward)
        {
            if (body == null || legs == null || legHeight <= 0f)
            {
                return;
            }

            body.localPosition = new Vector3(0f, BodyRise(lift, legHeight), forward);
            legs.localPosition = new Vector3(0f, 0f, forward);
            legs.localScale = new Vector3(1f, LegScale(lift, legHeight), 1f);
        }
    }
}
