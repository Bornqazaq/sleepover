using System;
using UnityEngine;

namespace Igruha.Core.Items
{
    /// <summary>
    /// Числа модели групповой переноски. Отдельной структурой, а не россыпью
    /// полей: мини-игра отдаёт их одним вызовом <c>Configure</c> из своего
    /// конфига, и ни одно значение не приходится дублировать в двух местах.
    ///
    /// Разбор модели — в <see cref="MultiCarryObject"/> и в спеке «Переноски
    /// предмета», раздел 9.1. Отдельного параметра «нестабильность» здесь нет
    /// и быть не должно: и рост нестабильности с числом несущих, и рост её при
    /// нехватке рук выводятся из натяжений и опоры сами.
    /// </summary>
    [Serializable]
    public struct MultiCarrySettings
    {
        [Tooltip("Радиус ручек от оси объекта, м. Он же плечо, на котором считается момент")]
        public float handleRadius;
        [Tooltip("Высота ручек над основанием объекта, м")]
        public float handleHeight;
        [Tooltip("Насколько несущий стоит дальше своей ручки, м. Столько места занимает его собственное тело")]
        public float carrierStandoff;
        [Tooltip("На сколько основание объекта поднято над ступнями несущих, м")]
        public float carryClearance;

        [Tooltip("Потолок скорости объекта, м/с. Не зависит от числа несущих")]
        public float maxObjectSpeed;
        [Tooltip("Потолок скорости несущего, м/с. Ставится на PlayerController, пока он держит ручку")]
        public float carrierSpeedCap;
        [Tooltip("Во что превращается метр натяжения: скорость объекта, м/с на метр")]
        public float pullToSpeed;
        [Tooltip("Натяжение ниже этого не считается вовсе, м. Иначе объект дёргается от каждого шага несущего")]
        public float tensionDeadzone;

        [Tooltip("Растяжение связи, за которым ручку срывает, м")]
        public float breakDistance;
        [Tooltip("Сколько метров в секунду несущий волен уходить наружу на каждый метр оставшегося запаса связи")]
        public float tetherFreeSpeedPerMeter;
        [Tooltip("Какую долю превышения связь отбирает, 0…1. Меньше единицы — упрямый бегун всё-таки дотягивает до срыва")]
        [Range(0f, 1f)]
        public float tetherGrip;

        [Tooltip("Порог наклона, ° — за ним объект считается опрокинутым набок")]
        public float tiltThreshold;
        [Tooltip("Предельный наклон в руках, °. Дальше объект уже не удержать, и модель не должна его переворачивать")]
        public float maxTiltAngle;
        [Tooltip("Во что превращается момент натяжений: угловое ускорение, рад/с² на метр·метр")]
        public float tiltFromTorque;
        [Tooltip("Во что превращается смещение опоры при нехватке рук: угловое ускорение, рад/с² на метр")]
        public float tiltFromSupportLoss;
        [Tooltip("Демпфер наклона, 1/с")]
        public float tiltDamping;
        [Tooltip("Возврат к вертикали, рад/с² на радиан наклона")]
        public float tiltRestoring;

        [Tooltip("Импульс совместного броска на одного бросающего")]
        public float throwImpulsePerCarrier;
        [Tooltip("Доля броска вверх")]
        [Range(0f, 1f)]
        public float throwUpward;

        /// <summary>
        /// Значения по умолчанию — те же, что в спеке «Переноски предмета»,
        /// раздел 8.4. Нужны, чтобы объект, поставленный в сцену без настройки
        /// мини-игрой, всё равно работал, а не стоял с нулями.
        /// </summary>
        public static MultiCarrySettings Default => new MultiCarrySettings
        {
            handleRadius = 0.5f,
            handleHeight = 1.2f,
            carrierStandoff = 0.45f,
            carryClearance = 0.15f,

            maxObjectSpeed = 2.2f,
            carrierSpeedCap = 2.9f,
            pullToSpeed = 12f,
            tensionDeadzone = 0.12f,

            breakDistance = 1.8f,
            tetherFreeSpeedPerMeter = 1.3f,
            tetherGrip = 0.85f,

            tiltThreshold = 45f,
            maxTiltAngle = 75f,
            tiltFromTorque = 10f,
            tiltFromSupportLoss = 19f,
            tiltDamping = 3.2f,
            tiltRestoring = 9f,

            throwImpulsePerCarrier = 7f,
            throwUpward = 0.45f
        };
    }
}
