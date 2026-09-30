using System;
using UnityEngine;

namespace Igruha.Core.Items
{
    /// <summary>Как объект превращает натяжение несущих в движение.</summary>
    public enum MultiCarryMotion : byte
    {
        /// <summary>Несомый груз: висит на руках, скорость равна натяжению, высота идёт за ступнями.</summary>
        Carried,

        /// <summary>Катящийся груз: стоит на колёсах под гравитацией, разгоняется, тормозит и докатывается.</summary>
        Rolling
    }

    /// <summary>Где у объекта ручки.</summary>
    public enum MultiCarryHandleLayout : byte
    {
        /// <summary>Равномерно по окружности вокруг вертикальной оси. Бутыль, ящик, носилки.</summary>
        Ring,

        /// <summary>
        /// Поручни тележки в системе кузова: один — сзади по центру, двое — сзади
        /// слева и справа, трое — двое сзади и один тянет спереди, четверо — двое
        /// сзади и двое спереди. Поручни едут вместе с курсом кузова.
        /// </summary>
        Cart
    }

    /// <summary>
    /// Числа модели групповой переноски. Отдельной структурой, а не россыпью
    /// полей: мини-игра отдаёт их одним вызовом <c>Configure</c> из своего
    /// конфига, и ни одно значение не приходится дублировать в двух местах.
    ///
    /// Разбор модели — в <see cref="MultiCarryObject"/> и в спеке «Переноски
    /// предмета», раздел 9.1. Отдельного параметра «нестабильность» здесь нет
    /// и быть не должно: и рост нестабильности с числом несущих, и рост её при
    /// нехватке рук выводятся из натяжений и опоры сами.
    ///
    /// Режим качения и раскладка поручней тележки добавлены редизайном
    /// «Переноски» v2. По умолчанию — несомый груз с ручками по кольцу: всё,
    /// что было собрано до редизайна, ведёт себя как прежде.
    /// </summary>
    [Serializable]
    public struct MultiCarrySettings
    {
        [Tooltip("Несут на руках или катят на колёсах")]
        public MultiCarryMotion motion;
        [Tooltip("Ручки по кольцу или поручни тележки в системе кузова")]
        public MultiCarryHandleLayout handleLayout;

        [Tooltip("Радиус ручек от оси объекта, м. Он же плечо, на котором считается момент. Только для кольца")]
        public float handleRadius;
        [Tooltip("Высота ручек над основанием объекта, м")]
        public float handleHeight;
        [Tooltip("Насколько несущий стоит дальше своей ручки, м. Столько места занимает его собственное тело")]
        public float carrierStandoff;
        [Tooltip("На сколько основание объекта поднято над ступнями несущих, м. Только для несомого груза")]
        public float carryClearance;

        [Tooltip("Поручни тележки: на сколько задние отстоят назад от центра кузова, м")]
        public float cartHandleBack;
        [Tooltip("Поручни тележки: на сколько передние отстоят вперёд от центра кузова, м")]
        public float cartHandleFront;
        [Tooltip("Поручни тележки: на сколько парные поручни разведены вбок от оси, м")]
        public float cartHandleSide;

        [Tooltip("Потолок скорости объекта, м/с. Не зависит от числа несущих. Только для несомого груза")]
        public float maxObjectSpeed;
        [Tooltip("Потолок скорости несущего, м/с. Ставится на PlayerController, пока он держит ручку")]
        public float carrierSpeedCap;
        [Tooltip("Во что превращается метр натяжения: скорость объекта, м/с на метр")]
        public float pullToSpeed;
        [Tooltip("Натяжение ниже этого не считается вовсе, м. Иначе объект дёргается от каждого шага несущего")]
        public float tensionDeadzone;

        [Tooltip("Качение: потолок скорости пустого объекта, м/с")]
        public float maxSpeedEmpty;
        [Tooltip("Качение: потолок скорости полного объекта, м/с")]
        public float maxSpeedFull;
        [Tooltip("Качение: разгон пустого объекта к целевой скорости, м/с²")]
        public float accelerationEmpty;
        [Tooltip("Качение: разгон полного объекта, м/с²")]
        public float accelerationFull;
        [Tooltip("Качение: торможение без рук, м/с². Отпущенный объект докатывается и встаёт")]
        public float rollingDeceleration;
        [Tooltip("Качение: скорость доворота кузова к направлению хода, °/с")]
        public float turnRate;

        [Tooltip("Качение от среднего растяжения связей вместо среднего ввода")]
        public bool rollingTensionDrive;
        [Tooltip("Потолок пустого/полного объекта по числу занятых ручек: 1, 2, 3, 4")]
        public Vector4 speedByHandsEmpty;
        public Vector4 speedByHandsFull;
        [Tooltip("Разгон пустого/полного объекта по числу занятых ручек: 1, 2, 3, 4")]
        public Vector4 accelerationByHandsEmpty;
        public Vector4 accelerationByHandsFull;
        [Tooltip("Мягкость связи с несущим, 1/с. Оставляет возможность отстать от стоянки")]
        public float rollingTetherGain;
        [Tooltip("Боковая устойчивость шага у поручня, 1/с; продольное отставание остаётся свободным")]
        public float rollingLateralGain;

        public float RollingSpeed(int hands, float fill) => rollingTensionDrive
            ? Mathf.Lerp(speedByHandsEmpty[Mathf.Clamp(hands - 1, 0, 3)],
                speedByHandsFull[Mathf.Clamp(hands - 1, 0, 3)], Mathf.Clamp01(fill))
            : Mathf.Lerp(maxSpeedEmpty, maxSpeedFull, Mathf.Clamp01(fill));

        public float RollingAcceleration(int hands, float fill) => rollingTensionDrive
            ? Mathf.Lerp(accelerationByHandsEmpty[Mathf.Clamp(hands - 1, 0, 3)],
                accelerationByHandsFull[Mathf.Clamp(hands - 1, 0, 3)], Mathf.Clamp01(fill))
            : Mathf.Lerp(accelerationEmpty, accelerationFull, Mathf.Clamp01(fill));

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
        [Tooltip("Во что превращается момент натяжений: угловое ускорение, рад/с² на метр·метр. Только для несомого груза")]
        public float tiltFromTorque;
        [Tooltip("Во что превращается смещение опоры при нехватке рук: угловое ускорение, рад/с² на метр. Только для несомого груза")]
        public float tiltFromSupportLoss;
        [Tooltip("Качение: во что превращается рывок кузова — изменение его скорости — у полного объекта: крен, рад/с на м/с. Ровная тяга на колёсах крена не даёт")]
        public float sloshPerDeltaSpeed;
        [Tooltip("Демпфер наклона, 1/с")]
        public float tiltDamping;
        [Tooltip("Возврат к вертикали, рад/с² на радиан наклона")]
        public float tiltRestoring;

        [Tooltip("Импульс совместного броска (или толчка тележки) на одного бросающего")]
        public float throwImpulsePerCarrier;
        [Tooltip("Доля броска вверх. Катящийся объект её не использует")]
        [Range(0f, 1f)]
        public float throwUpward;

        /// <summary>
        /// Значения по умолчанию — те же, что в спеке «Переноски предмета» v1,
        /// раздел 8.4: несомый груз с ручками по кольцу. Нужны, чтобы объект,
        /// поставленный в сцену без настройки мини-игрой, всё равно работал,
        /// а не стоял с нулями. Числа качения заполнены разумными значениями
        /// тележки v2, но не действуют, пока режим — <see cref="MultiCarryMotion.Carried"/>.
        /// </summary>
        public static MultiCarrySettings Default => new MultiCarrySettings
        {
            motion = MultiCarryMotion.Carried,
            handleLayout = MultiCarryHandleLayout.Ring,

            handleRadius = 0.5f,
            handleHeight = 1.2f,
            carrierStandoff = 0.45f,
            carryClearance = 0.15f,

            cartHandleBack = 0.65f,
            cartHandleFront = 0.65f,
            cartHandleSide = 0.45f,

            maxObjectSpeed = 2.2f,
            carrierSpeedCap = 2.9f,
            pullToSpeed = 12f,
            tensionDeadzone = 0.12f,

            maxSpeedEmpty = 2.8f,
            maxSpeedFull = 2f,
            accelerationEmpty = 4f,
            accelerationFull = 2f,
            rollingDeceleration = 1.5f,
            turnRate = 180f,

            breakDistance = 1.8f,
            tetherFreeSpeedPerMeter = 1.3f,
            tetherGrip = 0.85f,

            tiltThreshold = 45f,
            maxTiltAngle = 75f,
            tiltFromTorque = 10f,
            tiltFromSupportLoss = 19f,
            sloshPerDeltaSpeed = 1.3f,
            tiltDamping = 3.2f,
            tiltRestoring = 9f,

            throwImpulsePerCarrier = 7f,
            throwUpward = 0.45f
        };
    }
}
