using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Нерастяжимый трос между двумя игроками: ближе предельной длины не мешает
    /// вовсе, дальше — тянет обоих навстречу друг другу, а сверх жёсткого
    /// предела просто не пускает.
    ///
    /// Общий на проект: связка двоих понадобится и «Переноске предмета»,
    /// и «Крокодилу». Родственник <c>Core/Items/MultiCarryObject</c>, но там
    /// связь идёт через предмет, а здесь напрямую между игроками.
    ///
    /// Живёт отдельным объектом, а не компонентом на персонаже: префабы
    /// персонажей замороженные, а мини-игра должна уметь снять трос одним
    /// <c>Destroy</c> в конце раунда.
    /// </summary>
    /// <remarks>
    /// <b>Трос ни с чем не сталкивается — это требование, а не упрощение.</b>
    /// В момент, когда пара «Дырки в стене» проходит стену правильно, отрезок
    /// между игроками идёт ровно через перемычку между вырезами, то есть
    /// сквозь сплошную стену. Физический трос зацепился бы там и убивал бы
    /// каждый успешный проход. Трос — это визуальная верёвка плюс сила,
    /// и ничего больше.
    ///
    /// <b>Считается по горизонтали.</b> Вертикаль в длину не входит и в
    /// направление тяги тоже: иначе сбитый партнёр, летящий в воду, утягивал бы
    /// второго вниз сквозь пол, а поднятый ловушкой — вверх. Игра горизонтальная,
    /// и трос в ней тоже.
    ///
    /// <b>Сеть.</b> Силу прикладывает <see cref="PlayerController.ApplyImpulse"/>
    /// напрямую, а не <c>ApplyWorldImpulse</c>: в фазе 3 натяжение считает
    /// сервер целиком (тикет 18.18), и гонять каждый такт через relay незачем.
    /// </remarks>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class PlayerTether : MonoBehaviour
    {
        [Tooltip("Толщина верёвки, м")]
        [SerializeField] private float ropeWidth = 0.07f;
        [SerializeField] private Color ropeColor = new Color(0.85f, 0.72f, 0.45f);

        private LineRenderer rope;
        private PlayerController first;
        private PlayerController second;
        private Rigidbody firstBody;
        private Rigidbody secondBody;

        private float maxLength = 4.32f;
        private float rampDistance = 0.72f;
        private float maxPullAcceleration = 25f;
        private float hardLimit = 1.08f;

        /// <summary>Трос натянут: пара разошлась дальше предельной длины и её тянет назад.</summary>
        public bool IsTaut { get; private set; }

        /// <summary>Связаны ли двое прямо сейчас.</summary>
        public bool Bound => first != null && second != null;

        private void Awake()
        {
            rope = GetComponent<LineRenderer>();
            rope.positionCount = 2;
            rope.useWorldSpace = true;
            rope.startWidth = ropeWidth;
            rope.endWidth = ropeWidth;
            rope.textureMode = LineTextureMode.Tile;
            rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rope.receiveShadows = false;
            rope.enabled = false;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
            {
                rope.sharedMaterial = new Material(shader) { color = ropeColor };
            }
            else
            {
                Debug.LogError("Шейдер 'Universal Render Pipeline/Lit' не найден — трос будет фиолетовым в сборке", this);
            }
        }

        /// <summary>
        /// Числа троса. Отдельно от <see cref="Bind"/>: длина и жёсткость —
        /// настройка мини-игры и живут в её конфиге, а пара меняется от раунда
        /// к раунду.
        /// </summary>
        /// <param name="length">Предельная длина, м. Ближе неё троса как будто нет</param>
        /// <param name="ramp">На каком перетяге (м) притяжение выходит на полную силу</param>
        /// <param name="pullAcceleration">Максимальное ускорение притяжения, м/с²</param>
        /// <param name="limitBeyondLength">Жёсткий предел сверх длины, м. Дальше позиция стопорится</param>
        public void Configure(float length, float ramp, float pullAcceleration, float limitBeyondLength)
        {
            maxLength = Mathf.Max(0.01f, length);
            rampDistance = Mathf.Max(0.01f, ramp);
            maxPullAcceleration = Mathf.Max(0f, pullAcceleration);
            hardLimit = Mathf.Max(0f, limitBeyondLength);
        }

        /// <summary>Связать двоих. Повторный вызов перевешивает трос на новую пару.</summary>
        public void Bind(PlayerController a, PlayerController b)
        {
            first = a;
            second = b;
            firstBody = a != null ? a.GetComponent<Rigidbody>() : null;
            secondBody = b != null ? b.GetComponent<Rigidbody>() : null;

            IsTaut = false;
            rope.enabled = Bound;
        }

        /// <summary>
        /// Развязать. Обязателен в конце раунда: иначе двоих таскает друг за
        /// другом в хабе — персонаж переезжает между сценами живым.
        /// </summary>
        public void Release()
        {
            first = null;
            second = null;
            firstBody = null;
            secondBody = null;
            IsTaut = false;
            rope.enabled = false;
        }

        private void FixedUpdate()
        {
            if (!Bound || firstBody == null || secondBody == null)
            {
                IsTaut = false;
                return;
            }

            // 🔴 Позиции берутся у Rigidbody, а не у Transform. Transform
            // отстаёт от физики на кадр после TeleportTo: в тот кадр, когда
            // мини-игра расставляет пару по местам, трос видел бы её ещё
            // на точках спавна — то есть в десяти метрах друг от друга —
            // и жёсткий предел растащил бы обоих со свежих мест на 2.7 м
            // в стороны. Замерено на прогоне 31.08.
            Vector3 offset = secondBody.position - firstBody.position;
            offset.y = 0f;

            float distance = offset.magnitude;
            if (distance < 0.0001f)
            {
                IsTaut = false;
                return;
            }

            float overstretch = distance - maxLength;
            IsTaut = overstretch > 0f;
            if (!IsTaut)
            {
                return;
            }

            Vector3 direction = offset / distance;

            // Сила нарастает линейно на первом метре перетяга и дальше держит
            // потолок: у самой границы трос почти не мешает, а на полном
            // растяжении сдёргивает с точки — в этом вся комедия.
            float strength = Mathf.Clamp01(overstretch / rampDistance) * maxPullAcceleration;
            float impulsePerTick = strength * Time.fixedDeltaTime;

            first.ApplyImpulse(direction * (impulsePerTick * firstBody.mass));
            second.ApplyImpulse(-direction * (impulsePerTick * secondBody.mass));

            ApplyHardLimit(direction, distance);
        }

        /// <summary>
        /// Жёсткий предел: дальше него разойтись нельзя физически. Гасим
        /// расходящуюся составляющую скорости и подтягиваем обоих поровну.
        ///
        /// Одной силы мало: партнёр, которого несёт импульс ловушки или удара,
        /// проскакивает любое притяжение за пару тактов, и трос «рвётся» на
        /// вид, хотя рваться ему нельзя.
        /// </summary>
        private void ApplyHardLimit(Vector3 direction, float distance)
        {
            float excess = distance - (maxLength + hardLimit);
            if (excess <= 0f)
            {
                return;
            }

            float separation = Vector3.Dot(secondBody.linearVelocity - firstBody.linearVelocity, direction);
            if (separation > 0f)
            {
                Vector3 correction = direction * (separation * 0.5f);
                firstBody.linearVelocity += correction;
                secondBody.linearVelocity -= correction;
            }

            Vector3 pull = direction * (excess * 0.5f);
            firstBody.position += pull;
            secondBody.position -= pull;
        }

        private void LateUpdate()
        {
            if (!Bound)
            {
                return;
            }

            // Верёвка идёт от груди к груди: CameraTarget стоит именно там и
            // уже выставлен по росту каждого персонажа.
            rope.SetPosition(0, first.CameraTarget.position);
            rope.SetPosition(1, second.CameraTarget.position);
        }
    }
}
