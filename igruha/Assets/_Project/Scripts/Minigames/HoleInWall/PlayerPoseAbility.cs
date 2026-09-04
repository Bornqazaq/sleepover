using System;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Четыре позы игрока: клавиши 1–4, смена мгновенная, ходить в позе можно
    /// (спека, раздел 4). Единственное действие игрока в этой игре и
    /// единственное, что решает исход.
    ///
    /// Вешается контроллером на аватар в начале раунда и снимается в конце —
    /// префабы персонажей замороженные, и добавлять в них компонент нельзя
    /// (igruha/CLAUDE.md, раздел 🔒 0).
    /// </summary>
    /// <remarks>
    /// <b>Поза защёлкивается, а не держится клавишей.</b> Нажал — стоишь в позе,
    /// пока не нажал другую или пока не тронулся с места.
    ///
    /// <b>Поза — это стойка, а не режим передвижения.</b> Слой <c>Pose</c>
    /// перекрывает локомоцию целиком, и до 04.09 идущий в позе персонаж ехал
    /// по полу не перебирая ногами. Оставлять так — значит показывать в главной
    /// игре скольжение; поэтому <b>собственный ввод движения позу снимает</b>,
    /// и дальше человек идёт и бежит своими анимациями. Дошёл, нажал цифру —
    /// снова стоит.
    ///
    /// ⚠️ <b>Снимает позу ввод, а не движение.</b> В этой игре персонажа двигают
    /// три чужие силы, и ни одна не имеет права ронять позу: воронка выреза
    /// (<see cref="WallFunnel"/>) специально подталкивает вбок в последние
    /// полсекунды и работает только пока поза совпала с вырезом; трос
    /// (<c>Core/Player/PlayerTether</c>) тянет постоянно, и пара живёт
    /// на натянутом; страховка от застревания (<c>Core/Player/StuckDetector</c>)
    /// телепортирует. Поэтому читается <c>PlayerInputReader.MoveInput</c>,
    /// а не скорость тела.
    ///
    /// <b>Сеть.</b> Важное состояние меняет ровно один метод —
    /// <see cref="SetPose"/>, — и зовёт его не ввод, а мини-игра: ввод уходит
    /// намерением в <see cref="HoleInWallMinigame.SubmitPoseIntent"/>, сервер
    /// проверяет диапазон и рассылает подтверждённую позу. Способность
    /// компонентом <c>NetworkBehaviour</c> быть не может — её навешивают
    /// на уже заспавненный аватар в начале раунда, а <c>NetworkObject</c>
    /// набирает свои <c>NetworkBehaviour</c> при спавне и позже не добирает.
    /// Отсюда и маршрут через мини-игру, у которой сетевая половина есть.
    /// </remarks>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerPoseAbility : MonoBehaviour
    {
        /// <summary>
        /// Имя слоя поз в Animator Controller. Слой строит
        /// <c>Editor/HoleInWallPoseLayerBuilder</c>; строка продублирована здесь,
        /// потому что Editor-сборка недоступна из рантайма.
        /// </summary>
        private const string PoseLayerName = "Pose";

        /// <summary>Вес слоя поз, когда игрок в позе. Слой перекрывает основной целиком: поза — это весь силуэт, а не только руки.</summary>
        private const float PoseLayerWeight = 1f;

        /// <summary>Номер позы для Animator. Тот же параметр, что заводит билдер слоя.</summary>
        private static readonly int PoseParameterHash = Animator.StringToHash("Pose");

        /// <summary>Слоя поз в контроллере нет — у этого персонажа его просто не собрали.</summary>
        private const int NoLayer = -1;

        /// <summary>
        /// Мёртвая зона хода на случай, когда у мотора нет конфига. Совпадает
        /// со значением <c>CharacterConfig.inputDeadzone</c>: без конфига
        /// персонажа не бывает, но молча считать любой шум ходом хуже.
        /// </summary>
        private const float DefaultInputDeadzone = 0.15f;

        /// <summary>Поза сменилась. Визуалу, звуку и строке статуса.</summary>
        public event Action<HoleInWallPose> PoseChanged;

        private HoleInWallMinigame owner;
        private int playerId = -1;
        private PlayerController motor;
        private PlayerInputReader reader;

        /// <summary>
        /// Сетевой объект аватара. По нему видно, ведёт ли этого персонажа
        /// сама эта машина: чужую копию нельзя ни двигать, ни читать за неё ввод.
        /// </summary>
        private NetworkObject body;

        /// <summary>
        /// Аниматор модели. Позу отыгрывает только он: настоящие клипы поз
        /// приехали в арт-фазе (спека 9.5), и заглушки каркаса — полупрозрачная
        /// плашка вокруг персонажа и цветная иконка над головой — сняты
        /// 04.09 по просьбе геймдизайнера. Своей картинки у способности
        /// больше нет вовсе.
        /// </summary>
        private Animator animator;

        /// <summary>Индекс слоя поз. Ищется один раз: поиск идёт по строке.</summary>
        private int poseLayer = NoLayer;

        /// <summary>Поза, в которой игрок стоит прямо сейчас. <see cref="HoleInWallPose.None"/> — ещё ни одной не нажал.</summary>
        public HoleInWallPose CurrentPose { get; private set; } = HoleInWallPose.None;

        /// <summary>
        /// Нажатая цифра, которая ещё не стала позой: игрок нажал на бегу.
        ///
        /// Защёлка нужна из-за человеческого порядка нажатий. К своему месту
        /// бегут, и цифру жмут в ту же десятую долю секунды, что отпускают WASD,
        /// — сплошь и рядом раньше. Без защёлки такое нажатие пропадало бы
        /// молча, и клавиша выглядела бы сломанной; с ней персонаж встаёт
        /// в позу ровно в тот кадр, в который остановился.
        /// </summary>
        private HoleInWallPose pendingPose = HoleInWallPose.None;

        /// <summary>Номер позы под клавишей в прошлом кадре. Им ловится фронт нажатия, а не удержание.</summary>
        private int previousPoseRequest;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            reader = GetComponent<PlayerInputReader>();
            body = GetComponent<NetworkObject>();

            // Аниматор живёт на модели — она ребёнок аватара, а не он сам.
            animator = GetComponentInChildren<Animator>(true);
            poseLayer = animator != null ? animator.GetLayerIndex(PoseLayerName) : NoLayer;
        }

        private void OnEnable()
        {
            // Ctrl-присед выключен на всё время раунда: иначе в позу 3 ведут
            // два разных пути и «какая сейчас поза» получает два источника
            // правды (спека, раздел 4).
            motor.CrouchInputSuppressed = true;
        }

        private void OnDisable()
        {
            // ⚠️ Снимаем всё, что навесили: персонаж переезжает в хаб живым
            // NetworkObject, и незакрытая роль уедет вместе с ним. У «Ангелов»
            // так уехала блокировка движения Водящего.
            motor.CrouchInputSuppressed = false;
            motor.SetCrouched(false);
            motor.ForceStand();

            CurrentPose = HoleInWallPose.None;
            pendingPose = HoleInWallPose.None;
            previousPoseRequest = 0;
            ApplyPoseToAnimator();
        }

        /// <summary>
        /// Указать, кому уходят намерения. Зовётся сразу после навешивания
        /// компонента.
        /// </summary>
        public void Configure(HoleInWallMinigame game, int id)
        {
            owner = game;
            playerId = id;
        }

        /// <summary>
        /// Намерение игрока встать в позу. Само по себе оно ничего не меняет:
        /// решение принимает мини-игра, а в сетевой катке — её сервер.
        ///
        /// <see cref="HoleInWallPose.None"/> здесь — законное значение: это
        /// намерение <b>снять</b> позу, и уходит оно тем же маршрутом. Отдельного
        /// маршрута под сброс не заводим — разбор в
        /// <see cref="HoleInWallMinigame.ApplyPose"/>.
        /// </summary>
        public void RequestPose(HoleInWallPose pose)
        {
            if ((int)pose > HoleInWallConfig.PoseCount)
            {
                return;
            }

            // Уже стоим в этой позе — просить нечего. Заодно это гасит и
            // повтор: ридер отдаёт номер каждый кадр, пока клавишу держат,
            // а снятие позы ходом просится каждый кадр, пока идёт ввод.
            // Без проверки любой из двух давал бы шестьдесят пакетов в секунду
            // вместо одного на смену.
            if (CurrentPose == pose)
            {
                return;
            }

            if (owner == null)
            {
                // Способность живёт без мини-игры только в редакторских
                // проверках самой способности. Играть в это нельзя, но и падать
                // незачем.
                SetPose(pose);
                return;
            }

            owner.SubmitPoseIntent(playerId, pose);
        }

        /// <summary>
        /// Единственная точка, меняющая позу. В сетевой катке её зовёт только
        /// мини-игра — с подтверждённым сервером значением.
        /// </summary>
        public void SetPose(HoleInWallPose pose)
        {
            if (CurrentPose == pose)
            {
                return;
            }

            CurrentPose = pose;

            // Присед — единственная поза, у которой уже есть настоящий клип и
            // сжатие капсулы. Своего приседа не заводим: спека 8.3 считает
            // высоту выреза именно по нему (абсолютные 0.8 м).
            motor.SetCrouched(pose == HoleInWallPose.Crouch);

            ApplyPoseToAnimator();
            PoseChanged?.Invoke(pose);
        }

        /// <summary>
        /// Отдать позу аниматору: номер в параметр, вес — слою.
        ///
        /// Зовётся на КАЖДОЙ машине, а не только у владельца: <see cref="SetPose"/>
        /// приходит всем подтверждённым с сервера, а <c>CharacterAnimatorDriver</c>
        /// у чужих копий выключен и позу за нас не покажет.
        /// </summary>
        private void ApplyPoseToAnimator()
        {
            if (animator == null || poseLayer == NoLayer)
            {
                return;
            }

            animator.SetInteger(PoseParameterHash, (int)CurrentPose);
            animator.SetLayerWeight(poseLayer, PoseLayerVisible ? PoseLayerWeight : 0f);
        }

        /// <summary>
        /// Показывать ли позу прямо сейчас. Сбитого с ног показываем лежащим:
        /// провал в этой игре парный, и партнёр обязан видеть, что случилось,
        /// а не читать позу на теле, которое уже летит в воду.
        /// </summary>
        private bool PoseLayerVisible => CurrentPose != HoleInWallPose.None && !motor.IsKnockedDown;

        private void Update()
        {
            // Нокдаун начинается и кончается не по нашему событию, поэтому вес
            // слоя сверяется каждый кадр. SetLayerWeight с тем же значением
            // ничего не стоит и не аллоцирует.
            ApplyPoseToAnimator();


            // 🔴 Ввод читается только у персонажа, которого ведёт эта машина.
            // У чужой копии ридер отобран, но погонщик болванок умеет подать
            // в него значение напрямую — и тогда эта копия попросила бы позу,
            // а сервер, берущий отправителя из пакета, поставил бы её НАМ.
            // То есть болванка играла бы за живого человека его единственным
            // действием.
            if (reader == null || !WorldAuthority.DrivenHere(body))
            {
                return;
            }

            ReadPoseInput();
        }

        /// <summary>
        /// Разобрать ввод хозяина персонажа: цифра ставит позу, ход её снимает.
        /// </summary>
        private void ReadPoseInput()
        {
            // Сбитый с ног ничего не выбирает: и защёлку сносим, чтобы
            // сметённый стеной не вставал из воды в позу, нажатую до удара.
            if (motor.IsKnockedDown)
            {
                pendingPose = HoleInWallPose.None;
                return;
            }

            int requested = reader.PoseRequest;

            // Фронт нажатия, а не удержание. Раньше повтор гасился сравнением
            // с текущей позой, но теперь ход её снимает — и зажатая цифра
            // ставила бы позу заново каждый кадр, отменяя ход.
            if (requested > 0 && requested != previousPoseRequest)
            {
                pendingPose = (HoleInWallPose)requested;
            }

            previousPoseRequest = requested;

            if (Walking)
            {
                // Идёт сам — позы нет. Просьба уходит одна: RequestPose
                // отсеивает повтор по текущей позе.
                RequestPose(HoleInWallPose.None);
                return;
            }

            if (pendingPose == HoleInWallPose.None)
            {
                return;
            }

            RequestPose(pendingPose);
            pendingPose = HoleInWallPose.None;
        }

        /// <summary>
        /// Хозяин персонажа сам даёт ход.
        ///
        /// ⚠️ Именно ввод, а не скорость тела: воронка выреза, трос и страховка
        /// от застревания двигают персонажа не спрашивая, и по скорости поза
        /// слетала бы от них — то есть воронка, придуманная помогать, роняла бы
        /// позу ровно в тот момент, ради которого она есть.
        ///
        /// Порог — общая мёртвая зона персонажа: та же, по которой
        /// <c>PlayerController</c> отбрасывает дрожь стика, и та же, по которой
        /// застревание отличает «упёрся» от «стою».
        /// </summary>
        private bool Walking
        {
            get
            {
                float deadzone = motor.Config != null ? motor.Config.InputDeadzone : DefaultInputDeadzone;
                return reader.MoveInput.sqrMagnitude > deadzone * deadzone;
            }
        }
    }
}
