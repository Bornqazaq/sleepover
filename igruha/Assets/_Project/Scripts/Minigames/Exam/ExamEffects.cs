using UnityEngine;

namespace Igruha.Minigames.Exam
{
    /// <summary>
    /// Эффекты «Экзамена» — подфаза 4.4. Пыль из-под расходящихся створок,
    /// хлопок при их закрытии и вспышка кромки уцелевшей платформы.
    ///
    /// <b>Своего состояния и своих RPC у эффектов нет.</b> Всё, что нужно,
    /// игра уже показала на каждой машине: створки раскрываются и у хоста,
    /// и у клиента — сервер объявляет, какая платформа неверна, а дальше
    /// <see cref="ExamAnswerPlatform.OpenDoors"/> вызывается локально с обеих
    /// сторон. Значит эффекту достаточно смотреть на
    /// <see cref="ExamAnswerPlatform.DoorsOpen"/> и ловить его перепад.
    /// Отдельного сетевого события ради пыли заводить нельзя: оно приехало бы
    /// вторым пакетом и разъехалось бы с картинкой.
    ///
    /// <b>Опросом, а не подпиской</b> — и это осознанно. У створок есть событие
    /// <c>Released</c>, но оно поднимается в середине хода, когда полотно уже
    /// наклонилось на четверть; пыль нужна в момент, когда шов только пошёл.
    /// Тот же приём применён в «Переноске»: живая тара там тоже находится
    /// опросом, потому что событие приходит не тогда, когда нужно эффекту.
    ///
    /// <b>Кромка уцелевшей платформы подсвечивается через
    /// <see cref="MaterialPropertyBlock"/>.</b> Материалы палитры — общие
    /// ассеты на всю сцену, и правка их в рантайме переписала бы файл на диске
    /// и перекрасила бы заодно вторую платформу.
    /// </summary>
    public sealed class ExamEffects : MonoBehaviour
    {
        /// <summary>Эффекты одной платформы: чем пылит и что подсвечивает.</summary>
        [System.Serializable]
        private struct SideEffects
        {
            [Tooltip("Платформа, за створками которой следим")]
            public ExamAnswerPlatform Platform;

            [Tooltip("Пыль по линии шва: играет, когда половины расходятся")]
            public ParticleSystem SeamDust;

            [Tooltip("Труха, сыплющаяся в яму по периметру проёма")]
            public ParticleSystem RimDust;

            [Tooltip("Хлопок пыли, когда створки встают на место")]
            public ParticleSystem CloseDust;

            [Tooltip("Полосы рамки люка: их кромка вспыхивает, когда платформа оказалась верной")]
            public Renderer[] EdgeBands;
        }

        [SerializeField] private SideEffects sideA;
        [SerializeField] private SideEffects sideB;

        [Tooltip("Сколько секунд горит кромка уцелевшей платформы")]
        [SerializeField] private float edgeFlashSeconds = 2.6f;

        [Tooltip("Во сколько раз ярче обычного светится кромка на вспышке")]
        [SerializeField] private float edgeFlashGain = 3.5f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private bool openA;
        private bool openB;
        private float flashA;
        private float flashB;
        private MaterialPropertyBlock block;

        private void Awake() => block = new MaterialPropertyBlock();

        /// <summary>
        /// Ход эффектов. В <c>Update</c>, а не в <c>FixedUpdate</c>: это
        /// картинка, а не физика, и её шаг обязан совпадать с кадром.
        /// </summary>
        private void Update()
        {
            Track(ref sideA, ref sideB, ref openA, ref flashB);
            Track(ref sideB, ref sideA, ref openB, ref flashA);

            Fade(ref sideA, ref flashA);
            Fade(ref sideB, ref flashB);
        }

        /// <summary>
        /// Поймать перепад створок одной платформы.
        ///
        /// Раскрылась эта — значит верной оказалась соседняя, и вспыхнуть
        /// обязана её кромка. Отдельного «кто прав» эффекту не нужно: игра
        /// раскрывает ровно неверную половину, и обратное следует из этого
        /// однозначно.
        /// </summary>
        private void Track(ref SideEffects side, ref SideEffects other, ref bool wasOpen, ref float otherFlash)
        {
            if (side.Platform == null)
            {
                return;
            }

            bool open = side.Platform.DoorsOpen;
            if (open == wasOpen)
            {
                return;
            }

            wasOpen = open;
            if (open)
            {
                Fire(side.SeamDust);
                Fire(side.RimDust);
                otherFlash = edgeFlashSeconds;
            }
            else
            {
                Fire(side.CloseDust);
            }
        }

        /// <summary>Гасить вспышку кромки. Ноль возвращает полосам их обычное свечение.</summary>
        private void Fade(ref SideEffects side, ref float flash)
        {
            if (side.EdgeBands == null || side.EdgeBands.Length == 0)
            {
                return;
            }

            if (flash <= 0f)
            {
                return;
            }

            flash = Mathf.Max(0f, flash - Time.deltaTime);
            float t = edgeFlashSeconds > 0.001f ? flash / edgeFlashSeconds : 0f;
            float gain = 1f + (edgeFlashGain - 1f) * t * t;

            for (int i = 0; i < side.EdgeBands.Length; i++)
            {
                Renderer band = side.EdgeBands[i];
                if (band == null)
                {
                    continue;
                }

                Color baseEmission = band.sharedMaterial != null && band.sharedMaterial.HasProperty(EmissionColorId)
                    ? band.sharedMaterial.GetColor(EmissionColorId)
                    : Color.black;

                band.GetPropertyBlock(block);
                block.SetColor(EmissionColorId, baseEmission * gain);
                band.SetPropertyBlock(block);
            }
        }

        private static void Fire(ParticleSystem effect)
        {
            if (effect == null)
            {
                return;
            }

            // Сначала остановить, потом запустить. Play на приостановленной
            // системе продолжает её с места, а не начинает заново, и залп
            // не срабатывает вовсе — поймано на прогоне в редакторе.
            effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            effect.Play(true);
        }
    }
}
