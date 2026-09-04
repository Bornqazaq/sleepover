using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Session;

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
    /// <b>Отсюда же и способ рисовать провис.</b> Верёвка провисает и качается
    /// цепочкой Верле по <see cref="ropeSegments"/> точкам — без коллайдеров,
    /// без Rigidbody и без суставов. Цепочка на суставах выглядела бы так же,
    /// но цеплялась бы за стену, то есть нарушала бы правило абзацем выше.
    /// Симуляция чисто визуальная: на игроков она не действует никак, силу
    /// по-прежнему считает <see cref="FixedUpdate"/> по двум позициям.
    /// Поэтому её не нужно синхронизировать — у каждой машины своя, и разойтись
    /// им нечем, кроме кадра качания.
    ///
    /// <b>Длина считается честно, тяга — по горизонтали.</b> Два разных
    /// вопроса, и раньше на оба отвечала одна проекция.
    ///
    /// <i>Натянут ли трос</i> — это настоящее расстояние между кистями, с
    /// вертикалью. До 04.09 вертикаль выбрасывалась и отсюда, и напарник,
    /// свалившийся с платформы <b>вниз</b>, не создавал натяжения вовсе:
    /// падение на 2.88 м до воды считалось нулём, и трос начинал тянуть только
    /// когда сносом набегало 4.32 м <b>по горизонтали</b> — через 0.4–0.6 с
    /// после падения. На прогоне это читалось как «трос не тянет» и «большая
    /// задержка», и читалось верно.
    ///
    /// <i>Куда тянет</i> — по-прежнему только вбок. Вертикальная тяга утягивала
    /// бы стоящего партнёра вниз сквозь пол, а поднятого ловушкой — вверх.
    /// Игра горизонтальная, и рывок в ней тоже: стоящего сдёргивает к краю,
    /// а не вбивает в платформу.
    ///
    /// <b>Сеть: каждая машина тянет своего.</b> Трос — не событие, а
    /// непрерывная сила, и уходит она не в счёт, а в курс, поэтому её вправе
    /// применять сам владелец персонажа. Через сервер это было бы полсотни
    /// пакетов в секунду на каждую пару: <c>ApplyWorldImpulse</c> у авторитета
    /// шлёт владельцу отдельное сообщение на каждый такт физики. Отсюда
    /// <see cref="PlayerController.ApplyImpulse"/> напрямую — и только своему.
    /// Тот же приём, что у струи в <c>Core/Traps/PushZone</c> и у тяги
    /// в <c>Core/Items/MultiCarryObject</c>.
    ///
    /// Решать здесь нечего: натяжение — чистая функция от двух позиций,
    /// а обе видны каждой машине через сетевой транспорт персонажа. Обе
    /// половины силы прикладываются, просто разными машинами: свою — каждая.
    /// </remarks>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class PlayerTether : MonoBehaviour
    {
        [Tooltip("Толщина верёвки, м")]
        [SerializeField] private float ropeWidth = 0.07f;
        [SerializeField] private Color ropeColor = new Color(0.85f, 0.72f, 0.45f);

        [Header("Провис")]
        [Tooltip("Из скольких точек состоит верёвка. Больше — плавнее дуга, дороже LateUpdate")]
        [SerializeField] private int ropeSegments = 20;
        [Tooltip("Затухание качания за шаг: 1 — верёвка не успокаивается никогда, 0 — висит мёртвой дугой")]
        [Range(0f, 1f)]
        [SerializeField] private float ropeDamping = 0.92f;
        [Tooltip("Сколько раз за шаг подтягивать звенья к длине. Мало — верёвка тянется как резина")]
        [SerializeField] private int ropeIterations = 14;

        /// <summary>
        /// Шаг симуляции верёвки, с. Фиксированный, а не <c>deltaTime</c>:
        /// на просадке кадра переменный шаг раздувает Верле и верёвка
        /// взрывается — классические грабли этого метода.
        /// </summary>
        private const float RopeStep = 1f / 60f;

        /// <summary>Больше шагов за кадр не догоняем: после долгой паузы верёвка просто встаёт по месту.</summary>
        private const int RopeMaxStepsPerFrame = 3;

        private LineRenderer rope;
        private PlayerController first;
        private PlayerController second;
        /// <summary>За что верёвка держится у каждого — кисть, а не корень тела.</summary>
        private Transform firstAnchor;
        private Transform secondAnchor;

        private Rigidbody firstBody;
        private Rigidbody secondBody;

        /// <summary>
        /// Сетевые объекты связанных. Спрашиваются каждый такт физики, поэтому
        /// кэшируются на <see cref="Bind"/>, а не берутся <c>GetComponent</c>
        /// в цикле.
        /// </summary>
        private NetworkObject firstNet;
        private NetworkObject secondNet;

        private float maxLength = 4.32f;
        private float rampDistance = 0.72f;
        private float maxPullAcceleration = 25f;
        private float hardLimit = 1.08f;

        /// <summary>Точки верёвки в мире. Переиспользуются каждый кадр — в <c>LateUpdate</c> не аллоцируем.</summary>
        private Vector3[] ropePoints;

        /// <summary>Те же точки шагом раньше: в Верле скорость хранится разностью, а не полем.</summary>
        private Vector3[] ropePrevious;

        private float ropeStepDebt;

        /// <summary>Трос натянут: пара разошлась дальше предельной длины и её тянет назад.</summary>
        public bool IsTaut { get; private set; }

        /// <summary>Связаны ли двое прямо сейчас.</summary>
        public bool Bound => first != null && second != null;

        private void Awake()
        {
            ropeSegments = Mathf.Max(2, ropeSegments);
            ropePoints = new Vector3[ropeSegments];
            ropePrevious = new Vector3[ropeSegments];

            rope = GetComponent<LineRenderer>();
            rope.positionCount = ropeSegments;
            rope.useWorldSpace = true;
            rope.startWidth = ropeWidth;
            rope.endWidth = ropeWidth;
            rope.textureMode = LineTextureMode.Tile;

            // Скругление: без него провисшая верёвка на изгибах читается
            // гранёной лентой, а не верёвкой.
            rope.numCapVertices = 4;
            rope.numCornerVertices = 4;

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
            firstNet = a != null ? a.GetComponent<NetworkObject>() : null;
            secondNet = b != null ? b.GetComponent<NetworkObject>() : null;

            firstAnchor = ResolveAnchor(a);
            secondAnchor = ResolveAnchor(b);

            IsTaut = false;
            rope.enabled = Bound;

            if (Bound)
            {
                // Иначе первый кадр верёвка тянется из точки, где висела
                // с прошлого раунда, и хлещет через всю арену.
                ResetRope(firstAnchor.position, secondAnchor.position);
            }
        }

        /// <summary>
        /// Где верёвка держится за персонажа.
        ///
        /// <b>Кисть, а не точка камеры.</b> Раньше концы висели на
        /// <c>CameraTarget</c> — это цель наведения камеры на высоте груди,
        /// но в центре тела и ни к чему не привязанная визуально. Верёвка
        /// выходила из воздуха рядом с персонажем, и на приёмке это прочли
        /// ровно так: «прикреплён просто в пустоту».
        ///
        /// Кость ищется через <c>Animator</c>, а не задаётся ссылкой
        /// в инспекторе: префабы персонажей заморожены (igruha/CLAUDE.md, 0),
        /// и добавить в них поле нельзя. Гуманоидный аватар отдаёт кисть сам,
        /// причём у каждого персонажа свою и с его собственным ростом.
        ///
        /// Если аватар не гуманоидный — остаётся прежняя точка: верёвка
        /// из центра груди хуже кисти, но лучше отсутствующей.
        /// </summary>
        private static Transform ResolveAnchor(PlayerController player)
        {
            if (player == null)
            {
                return null;
            }

            Animator animator = player.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (hand != null)
                {
                    return hand;
                }
            }

            return player.CameraTarget;
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
            firstNet = null;
            secondNet = null;
            firstAnchor = null;
            secondAnchor = null;
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

            // Натяжение считается по настоящему расстоянию, вместе с высотой:
            // упавший в воду висит на тросе ровно так же, как отбежавший вбок.
            float distance = offset.magnitude;

            float overstretch = distance - maxLength;
            IsTaut = overstretch > 0f;
            if (!IsTaut)
            {
                return;
            }

            // А тянет трос только вбок — см. разбор в шапке. Пара, оказавшаяся
            // строго друг над другом, тяги не получает: тянуть по горизонтали
            // некуда, и рывок сводился бы к делению на ноль.
            var horizontal = new Vector3(offset.x, 0f, offset.z);
            float reach = horizontal.magnitude;
            if (reach < 0.0001f)
            {
                return;
            }

            Vector3 direction = horizontal / reach;

            // Сила нарастает линейно на первом метре перетяга и дальше держит
            // потолок: у самой границы трос почти не мешает, а на полном
            // растяжении сдёргивает с точки — в этом вся комедия.
            float strength = Mathf.Clamp01(overstretch / rampDistance) * maxPullAcceleration;
            float impulsePerTick = strength * Time.fixedDeltaTime;

            // Только своего: чужое тело ведёт его машина, и всё, что мы ему
            // напишем, тут же перетрёт сетевой транспорт. Вторую половину
            // натяжения приложит машина напарника — по тем же двум позициям.
            bool driveFirst = WorldAuthority.DrivenHere(firstNet);
            bool driveSecond = WorldAuthority.DrivenHere(secondNet);

            if (driveFirst)
            {
                first.ApplyImpulse(direction * (impulsePerTick * firstBody.mass));
            }

            if (driveSecond)
            {
                second.ApplyImpulse(-direction * (impulsePerTick * secondBody.mass));
            }

            ApplyHardLimit(direction, distance, driveFirst, driveSecond);
        }

        /// <summary>
        /// Жёсткий предел: дальше него разойтись нельзя физически. Гасим
        /// расходящуюся составляющую скорости и подтягиваем обоих поровну.
        ///
        /// Одной силы мало: партнёр, которого несёт импульс ловушки или удара,
        /// проскакивает любое притяжение за пару тактов, и трос «рвётся» на
        /// вид, хотя рваться ему нельзя.
        /// </summary>
        /// <param name="direction">Горизонтальное направление тяги</param>
        /// <param name="distance">Настоящее расстояние между телами, с высотой</param>
        private void ApplyHardLimit(Vector3 direction, float distance, bool driveFirst, bool driveSecond)
        {
            float excess = distance - (maxLength + hardLimit);
            if (excess <= 0f)
            {
                return;
            }

            float separation = Vector3.Dot(secondBody.linearVelocity - firstBody.linearVelocity, direction);
            Vector3 correction = separation > 0f ? direction * (separation * 0.5f) : Vector3.zero;
            Vector3 pull = direction * (excess * 0.5f);

            // Своё тело правится напрямую, чужое не трогается вовсе: запись
            // в его Rigidbody не доедет ни до кого и только подерётся
            // с интерполяцией сетевого транспорта.
            if (driveFirst)
            {
                firstBody.linearVelocity += correction;
                firstBody.position += pull;
            }

            if (driveSecond)
            {
                secondBody.linearVelocity -= correction;
                secondBody.position -= pull;
            }
        }

        // ========== ВЕРЁВКА: ПРОВИС И КАЧАНИЕ ==========

        private void LateUpdate()
        {
            if (!Bound)
            {
                return;
            }

            Vector3 head = firstAnchor.position;
            Vector3 tail = secondAnchor.position;

            // Телепорт — это возврат из воды и расстановка пары по местам.
            // Догонять его симуляцией нельзя: цепочка растянута через всю арену
            // и, распрямляясь, хлещет так, что читается сбоем рендера.
            if (Teleported(head, tail))
            {
                ResetRope(head, tail);
            }
            else
            {
                SimulateRope(head, tail);
            }

            rope.SetPositions(ropePoints);
        }

        /// <summary>
        /// Концы уехали дальше, чем верёвка может дотянуться. Это не движение,
        /// а перестановка: <c>RequestTeleport</c> при возврате из воды или
        /// расстановка пары в начале стены.
        /// </summary>
        private bool Teleported(Vector3 head, Vector3 tail)
        {
            float reach = maxLength + hardLimit;
            return (head - ropePoints[0]).sqrMagnitude > reach * reach
                || (tail - ropePoints[ropeSegments - 1]).sqrMagnitude > reach * reach;
        }

        /// <summary>Разложить верёвку прямой между концами и погасить качание.</summary>
        private void ResetRope(Vector3 head, Vector3 tail)
        {
            for (int i = 0; i < ropeSegments; i++)
            {
                Vector3 point = Vector3.Lerp(head, tail, i / (float)(ropeSegments - 1));
                ropePoints[i] = point;
                ropePrevious[i] = point;
            }

            ropeStepDebt = 0f;
        }

        /// <summary>
        /// Шаг Верле плюс подтяжка звеньев. Звено умеет только <b>сокращаться</b>:
        /// верёвка не сопротивляется сближению, поэтому на близкой паре она
        /// провисает сама, а на разошедшейся выпрямляется в струну — ровно тот
        /// признак, по которому игрок читает, что трос уже держит.
        /// </summary>
        private void SimulateRope(Vector3 head, Vector3 tail)
        {
            ropeStepDebt = Mathf.Min(ropeStepDebt + Time.deltaTime, RopeStep * RopeMaxStepsPerFrame);

            float restLength = maxLength / (ropeSegments - 1);
            Vector3 gravityStep = Physics.gravity * (RopeStep * RopeStep);

            // Провисшая верёвка обязана лечь на ту же опору, на которой стоят
            // двое, а не утонуть в платформе. Опора берётся у самого низкого
            // из пары: пока оба на платформе — это её пол, а как только одного
            // сметает в воду, пол уезжает вместе с ним и верёвка повисает.
            float floorY = Mathf.Min(first.Position.y, second.Position.y) + ropeWidth;

            while (ropeStepDebt >= RopeStep)
            {
                ropeStepDebt -= RopeStep;

                for (int i = 1; i < ropeSegments - 1; i++)
                {
                    Vector3 current = ropePoints[i];
                    ropePoints[i] = current + (current - ropePrevious[i]) * ropeDamping + gravityStep;
                    ropePrevious[i] = current;
                }

                for (int pass = 0; pass < ropeIterations; pass++)
                {
                    ropePoints[0] = head;
                    ropePoints[ropeSegments - 1] = tail;

                    for (int i = 0; i < ropeSegments - 1; i++)
                    {
                        Vector3 delta = ropePoints[i + 1] - ropePoints[i];
                        float distance = delta.magnitude;
                        if (distance <= restLength || distance < 0.0001f)
                        {
                            continue;
                        }

                        Vector3 shift = delta * ((distance - restLength) / distance * 0.5f);
                        ropePoints[i] += shift;
                        ropePoints[i + 1] -= shift;
                    }

                    for (int i = 1; i < ropeSegments - 1; i++)
                    {
                        if (ropePoints[i].y < floorY)
                        {
                            ropePoints[i].y = floorY;
                        }
                    }
                }

                ropePoints[0] = head;
                ropePoints[ropeSegments - 1] = tail;
            }
        }
    }
}
