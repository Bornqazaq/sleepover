using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Описание клипов одного персонажа для сборки его Animator Controller.
    /// RunJumpClip опционален: если задан, добавляется отдельное состояние JumpRun
    /// (прыжок на бегу W+Space), иначе прыжок всегда играет JumpClip — как у Karlan.
    /// CrouchWalkClip опционален: если задан, добавляются состояния приседа
    /// (CrouchIdle — сидит на месте, CrouchWalk — идёт), иначе присед остаётся
    /// чисто физическим: капсула жмётся, а модель сжимается по высоте
    /// (CharacterAnimatorDriver).
    /// DanceClips опционален: клипы эмоций-насмешек для радиального меню (до восьми).
    /// Пустой массив — персонаж без эмоций (Karlan/Boss, пока им не завезли танцы):
    /// состояния и параметры эмоций тогда в контроллер не добавляются вовсе.
    /// PlayerPrefabPath опционален: если задан и префаб существует, билдер сразу
    /// пишет в него посчитанные длительности нокдауна (PlayerController — они
    /// индивидуальны для каждого персонажа, как и сам Animator Controller).
    /// </summary>
    internal readonly struct CharacterAnimationSet
    {
        public readonly string CharacterName;
        public readonly string ControllerPath;
        public readonly string PlayerPrefabPath;
        public readonly string IdleClip;
        public readonly string RunClip;
        public readonly string JumpClip;
        public readonly string RunJumpClip;
        public readonly string PunchClip;
        public readonly string FlyBackClip;
        public readonly string StandUpBackClip;
        public readonly string FallForwardClip;
        public readonly string StandUpForwardClip;
        public readonly string CrouchWalkClip;
        public readonly string[] DanceClips;

        public CharacterAnimationSet(
            string characterName,
            string controllerPath,
            string playerPrefabPath,
            string idleClip,
            string runClip,
            string jumpClip,
            string punchClip,
            string flyBackClip,
            string standUpBackClip,
            string fallForwardClip,
            string standUpForwardClip,
            string runJumpClip = null,
            string[] danceClips = null,
            string crouchWalkClip = null)
        {
            CharacterName = characterName;
            ControllerPath = controllerPath;
            PlayerPrefabPath = playerPrefabPath;
            IdleClip = idleClip;
            RunClip = runClip;
            JumpClip = jumpClip;
            RunJumpClip = runJumpClip;
            PunchClip = punchClip;
            FlyBackClip = flyBackClip;
            StandUpBackClip = standUpBackClip;
            FallForwardClip = fallForwardClip;
            StandUpForwardClip = standUpForwardClip;
            CrouchWalkClip = crouchWalkClip;
            DanceClips = danceClips ?? System.Array.Empty<string>();
        }
    }

    /// <summary>
    /// Собирает Animator Controller персонажа и синхронизирует длительности
    /// нокдауна на его PlayerController с фактической длиной клипов, чтобы
    /// управление возвращалось ровно в тот момент, когда персонаж встал.
    /// Один переиспользуемый билдер на всех персонажей — см. пункты меню ниже
    /// за конкретными наборами клипов.
    /// </summary>
    internal static class PlayerAnimatorControllerBuilder
    {
        private const string AnimationsFolder = "Assets/_Project/Art/Animations/";

        /// <summary>
        /// Ходьба в приседе — одна на всех. Клип куплен под Aza, но играет на любом
        /// персонаже: все FBX импортируются как Humanoid (CharacterClipImportSetup),
        /// а Humanoid-клип живёт в абстрактном скелете, а не в костях конкретной
        /// модели, и Unity ретаргетит его на чужой аватар сама. Своя копия клипа
        /// каждому персонажу не нужна — это ещё восемь файлов по 90 МБ в LFS
        /// ради одной и той же анимации.
        /// Если на ком-то ретаргет выйдет кривым (крайние пропорции — Шланга в два
        /// метра, широкий Fat), ему подставляется свой клип этой же строкой в его
        /// BuildXController — остальных это не трогает.
        /// </summary>
        private const string SharedCrouchWalkClip = "Aza@Crouch Walk Forward.fbx";

        private const string SpeedParameter = "Speed";
        private const string CrouchParameter = "Crouch";
        private const string JumpParameter = "Jump";
        private const string PunchParameter = "Punch";
        private const string KnockdownFrontParameter = "KnockdownFront";
        private const string KnockdownBackParameter = "KnockdownBack";
        private const string EmoteParameter = "Emote";
        private const string EmotePlayParameter = "EmotePlay";
        private const string EmoteStopParameter = "EmoteStop";

        private const float RunThreshold = 0.1f;
        private const float TransitionDuration = 0.12f;
        private const float KnockdownTransitionDuration = 0.06f;
        // Вход в танец чуть мягче обычного: эмоция включается «напоказ», без рывка.
        private const float EmoteTransitionDuration = 0.15f;

        // Клипы Mixamo/Tripo длинные для party-game: ускоряем, чтобы падение читалось,
        // но не отбирало управление на четыре секунды.
        private const float JumpSpeed = 1.8f;
        private const float PunchSpeed = 1.2f;
        private const float KnockdownSpeed = 1.5f;
        // Ноль — не «очень медленно», а буквально остановленное время состояния:
        // клип замирает на кадре, с которого вошли, то есть на нулевом.
        private const float CrouchIdleSpeed = 0f;

        [MenuItem("Igruha/Player/Build Karlan Animator Controller")]
        private static void BuildKarlan()
        {
            Build(new CharacterAnimationSet(
                characterName: "Karlan",
                controllerPath: "Assets/_Project/Art/Animations/PlayerAnimator.controller",
                playerPrefabPath: "Assets/_Project/Prefabs/Player/Player.prefab",
                idleClip: "Karlan@Happy Idle.fbx",
                runClip: "Karlan@Fast Run.fbx",
                jumpClip: "Karlan(Fbx without color)@Unarmed Jump.fbx",
                punchClip: "Karlan(Fbx without color)@Cross Punch.fbx",
                flyBackClip: "Karlan(Fbx without color)@Flying Back Death.fbx",
                standUpBackClip: "Karlan(Fbx without color)@Standing Up From Back.fbx",
                fallForwardClip: "Karlan(Fbx without color)@Falling Forward Death.fbx",
                standUpForwardClip: "Karlan(Fbx without color)@Stand Up From Forward.fbx",
                crouchWalkClip: SharedCrouchWalkClip));
        }

        [MenuItem("Igruha/Player/Build Boss Animator Controller")]
        internal static void BuildBossController()
        {
            Build(new CharacterAnimationSet(
                characterName: "Boss",
                controllerPath: "Assets/_Project/Art/Animations/BossAnimator.controller",
                playerPrefabPath: "Assets/_Project/Prefabs/Player/Boss.prefab",
                idleClip: "Boss@idle.fbx",
                runClip: "Boss@defaultRunning.fbx",
                jumpClip: "Boss@Jumping.fbx",
                punchClip: "Boss@Jab Cross.fbx",
                flyBackClip: "Boss@fallBack.fbx",
                standUpBackClip: "Boss@kipUp(back).fbx",
                fallForwardClip: "Boss@fallForward.fbx",
                standUpForwardClip: "Boss@standUp.fbx",
                runJumpClip: "Boss@jumpRun.fbx",
                crouchWalkClip: SharedCrouchWalkClip));
        }

        [MenuItem("Igruha/Player/Build Shlanga Animator Controller")]
        internal static void BuildShlangaController()
        {
            Build(new CharacterAnimationSet(
                characterName: "Shlanga",
                controllerPath: "Assets/_Project/Art/Animations/ShlangaAnimator.controller",
                playerPrefabPath: "Assets/_Project/Prefabs/Player/Shlanga.prefab",
                idleClip: "Shlanga@idle.fbx",
                runClip: "Shlanga@run.fbx",
                jumpClip: "Shlanga@jump.fbx",
                punchClip: "Shlanga@cross.fbx",
                flyBackClip: "Shlanga@fall_Back.fbx",
                standUpBackClip: "Shlanga@standUpBack.fbx",
                fallForwardClip: "Shlanga@fall_Forward.fbx",
                standUpForwardClip: "Shlanga@standUpForward.fbx",
                runJumpClip: "Shlanga@runningJump.fbx",
                danceClips: new[]
                {
                    "Shlanga@dance1.fbx",
                    "Shlanga@dance2.fbx",
                    "Shlanga@dance3.fbx",
                    "Shlanga@dance4.fbx",
                    "Shlanga@dance5.fbx",
                    "Shlanga@dance6.fbx",
                    "Shlanga@dance7.fbx",
                    "Shlanga@dance8.fbx"
                },
                crouchWalkClip: SharedCrouchWalkClip));
        }

        [MenuItem("Igruha/Player/Build Fat Animator Controller")]
        internal static void BuildFatController()
        {
            Build(new CharacterAnimationSet(
                characterName: "Fat",
                controllerPath: "Assets/_Project/Art/Animations/FatAnimator.controller",
                playerPrefabPath: "Assets/_Project/Prefabs/Player/Fat.prefab",
                idleClip: "Fat@Neutral Idle.fbx",
                runClip: "Fat@Running.fbx",
                jumpClip: "Fat@Forward Jump.fbx",
                punchClip: "Fat@Cross Punch.fbx",
                flyBackClip: "Fat@Sweep Fall.fbx",
                standUpBackClip: "Fat@Kip Up.fbx",
                fallForwardClip: "Fat@Fall Flat.fbx",
                standUpForwardClip: "Fat@Stand Up Forward.fbx",
                runJumpClip: "Fat@Running Jump.fbx",
                crouchWalkClip: SharedCrouchWalkClip));
        }

        [MenuItem("Igruha/Player/Build MyBoy Animator Controller")]
        internal static void BuildMyBoyController()
        {
            Build(new CharacterAnimationSet(
                characterName: "MyBoy",
                controllerPath: "Assets/_Project/Art/Animations/MyBoyAnimator.controller",
                playerPrefabPath: "Assets/_Project/Prefabs/Player/MyBoy.prefab",
                idleClip: "MyBoy@Old Man Idle.fbx",
                runClip: "MyBoy@Goofy Running.fbx",
                jumpClip: "MyBoy@Jumping.fbx",
                punchClip: "MyBoy@Cross Punch.fbx",
                flyBackClip: "MyBoy@fall_Back.fbx",
                standUpBackClip: "MyBoy@standUpBack.fbx",
                fallForwardClip: "MyBoy@fall_Forward.fbx",
                standUpForwardClip: "MyBoy@standUpForward.fbx",
                runJumpClip: "MyBoy@Running Jump.fbx",
                crouchWalkClip: SharedCrouchWalkClip));
        }

        [MenuItem("Igruha/Player/Build Girl Animator Controller")]
        internal static void BuildGirlController()
        {
            Build(new CharacterAnimationSet(
                characterName: "Girl",
                controllerPath: "Assets/_Project/Art/Animations/GirlAnimator.controller",
                playerPrefabPath: "Assets/_Project/Prefabs/Player/Girl.prefab",
                idleClip: "Girl@idle.fbx",
                runClip: "Girl@running.fbx",
                jumpClip: "Girl@jump.fbx",
                punchClip: "Girl@crossPunch.fbx",
                flyBackClip: "Girl@fallBack.fbx",
                // «Zombie Stand Up» у Mixamo — подъём из положения на спине,
                // то есть парный клип к падению назад (fallBack).
                standUpBackClip: "Girl@Zombie Stand Up.fbx",
                fallForwardClip: "Girl@fallForward.fbx",
                standUpForwardClip: "Girl@standUpForward.fbx",
                runJumpClip: "Girl@runJump.fbx",
                crouchWalkClip: SharedCrouchWalkClip));
        }

        [MenuItem("Igruha/Player/Build Milez Animator Controller")]
        internal static void BuildMilezController()
        {
            Build(new CharacterAnimationSet(
                characterName: "Milez",
                controllerPath: "Assets/_Project/Art/Animations/MilezAnimator.controller",
                playerPrefabPath: "Assets/_Project/Prefabs/Player/Milez.prefab",
                idleClip: "Milez@idle.fbx",
                runClip: "Milez@running.fbx",
                jumpClip: "Milez@jump.fbx",
                punchClip: "Milez@crossPunch.fbx",
                flyBackClip: "Milez@fallBack.fbx",
                standUpBackClip: "Milez@standUpBack.fbx",
                fallForwardClip: "Milez@fallForward.fbx",
                standUpForwardClip: "Milez@standUpForward.fbx",
                runJumpClip: "Milez@runJump.fbx",
                crouchWalkClip: SharedCrouchWalkClip));
        }

        [MenuItem("Igruha/Player/Build Aza Animator Controller")]
        internal static void BuildAzaController()
        {
            // Набор Mixamo у Aza тот же, что у Fat, и раскладывается так же:
            // Sweep Fall — падение назад после подсечки, Kip Up — парный ему
            // подъём рывком со спины; Fall Flat — падение плашмя вперёд,
            // Stand Up — подъём с живота.
            Build(new CharacterAnimationSet(
                characterName: "Aza",
                controllerPath: "Assets/_Project/Art/Animations/AzaAnimator.controller",
                playerPrefabPath: "Assets/_Project/Prefabs/Player/Aza.prefab",
                idleClip: "Aza@Happy Idle.fbx",
                runClip: "Aza@Running.fbx",
                jumpClip: "Aza@Jumping.fbx",
                punchClip: "Aza@Cross Punch.fbx",
                flyBackClip: "Aza@Sweep Fall.fbx",
                standUpBackClip: "Aza@Kip Up.fbx",
                fallForwardClip: "Aza@Fall Flat.fbx",
                standUpForwardClip: "Aza@Stand Up.fbx",
                runJumpClip: "Aza@Running Jump.fbx",
                crouchWalkClip: SharedCrouchWalkClip));
        }

        private static void Build(CharacterAnimationSet set)
        {
            AnimationClip idle = LoadClip(set.IdleClip);
            AnimationClip run = LoadClip(set.RunClip);
            AnimationClip jump = LoadClip(set.JumpClip);
            AnimationClip runJump = string.IsNullOrEmpty(set.RunJumpClip) ? null : LoadClip(set.RunJumpClip);
            AnimationClip punch = LoadClip(set.PunchClip);
            AnimationClip fallForward = LoadClip(set.FallForwardClip);
            AnimationClip flyBack = LoadClip(set.FlyBackClip);
            AnimationClip standUpForward = LoadClip(set.StandUpForwardClip);
            AnimationClip standUpBack = LoadClip(set.StandUpBackClip);
            AnimationClip crouchWalk = string.IsNullOrEmpty(set.CrouchWalkClip) ? null : LoadClip(set.CrouchWalkClip);

            bool missingRunJump = !string.IsNullOrEmpty(set.RunJumpClip) && runJump == null;
            bool missingCrouchWalk = !string.IsNullOrEmpty(set.CrouchWalkClip) && crouchWalk == null;
            if (idle == null || run == null || jump == null || punch == null ||
                fallForward == null || flyBack == null || standUpForward == null || standUpBack == null ||
                missingRunJump || missingCrouchWalk)
            {
                Debug.LogError($"PlayerAnimatorControllerBuilder ({set.CharacterName}): не найден один из клипов — проверь имена файлов в Art/Animations.");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(set.ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(set.ControllerPath);
            }

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(set.ControllerPath);
            controller.AddParameter(SpeedParameter, AnimatorControllerParameterType.Float);
            // Параметр заводится всегда — его гонит CharacterAnimatorDriver независимо
            // от персонажа. Состояние по нему появляется только у того, кому завезли
            // клип приседания (см. BuildLocomotionTransitions); у остальных параметр
            // остаётся без состояний, и это не ошибка.
            controller.AddParameter(CrouchParameter, AnimatorControllerParameterType.Bool);
            controller.AddParameter(JumpParameter, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(PunchParameter, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(KnockdownFrontParameter, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(KnockdownBackParameter, AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimatorState idleState = AddState(machine, "Idle", idle, 1f, new Vector3(300f, 0f, 0f));
            AnimatorState runState = AddState(machine, "Run", run, 1f, new Vector3(300f, 120f, 0f));
            AnimatorState jumpState = AddState(machine, "Jump", jump, JumpSpeed, new Vector3(560f, 60f, 0f));
            AnimatorState punchState = AddState(machine, "Punch", punch, PunchSpeed, new Vector3(560f, 180f, 0f));

            AnimatorState flyBackState = AddState(machine, "FlyBack", flyBack, KnockdownSpeed, new Vector3(560f, 300f, 0f));
            AnimatorState standUpBackState = AddState(machine, "StandUpFromBack", standUpBack, KnockdownSpeed, new Vector3(820f, 300f, 0f));
            AnimatorState fallForwardState = AddState(machine, "FallForward", fallForward, KnockdownSpeed, new Vector3(560f, 420f, 0f));
            AnimatorState standUpForwardState = AddState(machine, "StandUpFromForward", standUpForward, KnockdownSpeed, new Vector3(820f, 420f, 0f));

            machine.defaultState = idleState;

            BuildLocomotionTransitions(machine, idleState, runState, crouchWalk);

            if (runJump != null)
            {
                // Прыжок на бегу (W+Space) и прыжок с места (Space) — разные клипы,
                // ветвление по Speed в момент срабатывания триггера Jump.
                AnimatorState jumpRunState = AddState(machine, "JumpRun", runJump, JumpSpeed, new Vector3(560f, -60f, 0f));

                AnimatorStateTransition toJump = machine.AddAnyStateTransition(jumpState);
                toJump.hasExitTime = false;
                toJump.duration = TransitionDuration;
                toJump.canTransitionToSelf = false;
                toJump.AddCondition(AnimatorConditionMode.If, 0f, JumpParameter);
                toJump.AddCondition(AnimatorConditionMode.Less, RunThreshold, SpeedParameter);

                AnimatorStateTransition toJumpRun = machine.AddAnyStateTransition(jumpRunState);
                toJumpRun.hasExitTime = false;
                toJumpRun.duration = TransitionDuration;
                toJumpRun.canTransitionToSelf = false;
                toJumpRun.AddCondition(AnimatorConditionMode.If, 0f, JumpParameter);
                toJumpRun.AddCondition(AnimatorConditionMode.Greater, RunThreshold, SpeedParameter);

                AddExitTransition(jumpState, idleState, 0.85f, TransitionDuration);
                AddExitTransition(jumpRunState, runState, 0.85f, TransitionDuration);
            }
            else
            {
                AddTriggerTransition(machine, jumpState, JumpParameter, TransitionDuration);
                AddExitTransition(jumpState, idleState, 0.85f, TransitionDuration);
            }

            AddTriggerTransition(machine, punchState, PunchParameter, TransitionDuration);
            AddExitTransition(punchState, idleState, 0.9f, TransitionDuration);

            // Удар в лицо: отлёт назад → подъём со спины.
            AddTriggerTransition(machine, flyBackState, KnockdownFrontParameter, KnockdownTransitionDuration);
            AddExitTransition(flyBackState, standUpBackState, 0.95f, KnockdownTransitionDuration);
            AddExitTransition(standUpBackState, idleState, 0.92f, TransitionDuration);

            // Удар со спины: падение вперёд → подъём с живота.
            AddTriggerTransition(machine, fallForwardState, KnockdownBackParameter, KnockdownTransitionDuration);
            AddExitTransition(fallForwardState, standUpForwardState, 0.95f, KnockdownTransitionDuration);
            AddExitTransition(standUpForwardState, idleState, 0.92f, TransitionDuration);

            BuildEmoteStates(set, controller, machine, idleState, runState);

            AssetDatabase.SaveAssets();

            float frontDuration = (flyBack.length + standUpBack.length) / KnockdownSpeed;
            float backDuration = (fallForward.length + standUpForward.length) / KnockdownSpeed;
            SyncPrefabDurations(set, frontDuration, backDuration);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"PlayerAnimatorControllerBuilder ({set.CharacterName}): контроллер собран. " +
                      $"Нокдаун в лицо {frontDuration:F2}с, со спины {backDuration:F2}с.");
        }

        /// <summary>
        /// Наземное перемещение: покой, бег и — если у персонажа есть клип —
        /// ходьба в приседе.
        ///
        /// Условие «не в приседе» довешивается и на переход Idle→Run: иначе при
        /// движении в приседе годятся сразу два перехода (Speed выше порога и
        /// Crouch взведён), и какой из них сработает, решал бы порядок добавления,
        /// а не смысл. Персонажу без клипа приседа лишнее условие не ставится —
        /// у него бег в приседе и должен оставаться обычным бегом.
        /// </summary>
        private static void BuildLocomotionTransitions(
            AnimatorStateMachine machine,
            AnimatorState idleState,
            AnimatorState runState,
            AnimationClip crouchWalk)
        {
            AddConditionTransition(runState, idleState, AnimatorConditionMode.Less, RunThreshold, SpeedParameter);

            if (crouchWalk == null)
            {
                AddConditionTransition(idleState, runState, AnimatorConditionMode.Greater, RunThreshold, SpeedParameter);
                return;
            }

            // Клип приседа один на оба состояния: сидение на месте — это его
            // нулевой кадр, замороженный скоростью 0. Отдельного клипа «сидит и не
            // двигается» у Mixamo не взято, а гнать ходьбу на месте нельзя —
            // персонаж перебирал бы ногами, стоя под Ctrl.
            AnimatorState crouchIdleState = AddState(machine, "CrouchIdle", crouchWalk, CrouchIdleSpeed, new Vector3(300f, 240f, 0f));
            AnimatorState crouchWalkState = AddState(machine, "CrouchWalk", crouchWalk, 1f, new Vector3(560f, 240f, 0f));

            AnimatorStateTransition toRun = idleState.AddTransition(runState);
            toRun.hasExitTime = false;
            toRun.duration = TransitionDuration;
            toRun.AddCondition(AnimatorConditionMode.Greater, RunThreshold, SpeedParameter);
            toRun.AddCondition(AnimatorConditionMode.IfNot, 0f, CrouchParameter);

            // Вход в присед — из обоих наземных состояний, каждое в свой аналог:
            // стоял — сядет неподвижно, бежал — сразу пойдёт в приседе.
            AddConditionTransition(idleState, crouchIdleState, AnimatorConditionMode.If, 0f, CrouchParameter);
            AddConditionTransition(runState, crouchWalkState, AnimatorConditionMode.If, 0f, CrouchParameter);

            // Ctrl зажат, W нажимают и отпускают — переключение между сидением и
            // ходьбой напрямую, без промежуточной стойки.
            AddCrouchTransition(crouchIdleState, crouchWalkState, AnimatorConditionMode.If, AnimatorConditionMode.Greater);
            AddCrouchTransition(crouchWalkState, crouchIdleState, AnimatorConditionMode.If, AnimatorConditionMode.Less);

            // Выход — туда, где персонаж окажется по скорости. PlayerController
            // держит присед принудительно, пока над головой потолок, так что
            // распрямление здесь всегда законное.
            AddCrouchTransition(crouchIdleState, idleState, AnimatorConditionMode.IfNot, AnimatorConditionMode.Less);
            AddCrouchTransition(crouchWalkState, idleState, AnimatorConditionMode.IfNot, AnimatorConditionMode.Less);
            AddCrouchTransition(crouchWalkState, runState, AnimatorConditionMode.IfNot, AnimatorConditionMode.Greater);
            AddCrouchTransition(crouchIdleState, runState, AnimatorConditionMode.IfNot, AnimatorConditionMode.Greater);
        }

        /// <summary>
        /// Переход, развилка которого держится на паре «присед × скорость». Оба
        /// условия ставятся всегда, поэтому набор переходов состояния взаимно
        /// исключающий — какой сработает, не зависит от порядка добавления.
        /// </summary>
        private static void AddCrouchTransition(
            AnimatorState from,
            AnimatorState to,
            AnimatorConditionMode crouchMode,
            AnimatorConditionMode speedMode)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = TransitionDuration;
            transition.AddCondition(crouchMode, 0f, CrouchParameter);
            transition.AddCondition(speedMode, RunThreshold, SpeedParameter);
        }

        /// <summary>
        /// Танцы-насмешки из радиального меню. Каждый танец — отдельное состояние,
        /// зацикленное клипом (loopTime выставляет CharacterClipImportSetup), поэтому
        /// играет бесконечно, пока игрок не сделает что-то ещё:
        /// побежал (Speed), прыгнул/ударил/получил в лицо (переходы из AnyState
        /// перебивают танец сами) или отменил эмоцию (EmoteStop).
        /// Персонажу без танцев блок не добавляется — включая параметры, чтобы
        /// его контроллер не тащил мёртвые поля.
        /// </summary>
        private static void BuildEmoteStates(
            CharacterAnimationSet set,
            AnimatorController controller,
            AnimatorStateMachine machine,
            AnimatorState idleState,
            AnimatorState runState)
        {
            if (set.DanceClips.Length == 0)
            {
                return;
            }

            controller.AddParameter(EmoteParameter, AnimatorControllerParameterType.Int);
            controller.AddParameter(EmotePlayParameter, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(EmoteStopParameter, AnimatorControllerParameterType.Trigger);

            for (int i = 0; i < set.DanceClips.Length; i++)
            {
                AnimationClip dance = LoadClip(set.DanceClips[i]);
                if (dance == null)
                {
                    Debug.LogError($"PlayerAnimatorControllerBuilder ({set.CharacterName}): не найден клип танца {set.DanceClips[i]}.");
                    continue;
                }

                // Номер эмоции — не индекс: в UI и в коде танцы нумеруются с единицы,
                // а 0 зарезервирован под «эмоция не выбрана».
                int emoteNumber = i + 1;
                AnimatorState danceState = AddState(
                    machine,
                    $"Dance_{emoteNumber}",
                    dance,
                    1f,
                    new Vector3(1100f, i * 90f - 180f, 0f));

                AnimatorStateTransition toDance = machine.AddAnyStateTransition(danceState);
                toDance.hasExitTime = false;
                toDance.duration = EmoteTransitionDuration;
                toDance.canTransitionToSelf = false;
                toDance.AddCondition(AnimatorConditionMode.If, 0f, EmotePlayParameter);
                toDance.AddCondition(AnimatorConditionMode.Equals, emoteNumber, EmoteParameter);

                AnimatorStateTransition toRun = danceState.AddTransition(runState);
                toRun.hasExitTime = false;
                toRun.duration = TransitionDuration;
                toRun.AddCondition(AnimatorConditionMode.Greater, RunThreshold, SpeedParameter);

                AnimatorStateTransition toIdle = danceState.AddTransition(idleState);
                toIdle.hasExitTime = false;
                toIdle.duration = EmoteTransitionDuration;
                toIdle.AddCondition(AnimatorConditionMode.If, 0f, EmoteStopParameter);
            }
        }

        private static void SyncPrefabDurations(CharacterAnimationSet set, float frontDuration, float backDuration)
        {
            if (string.IsNullOrEmpty(set.PlayerPrefabPath))
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(set.PlayerPrefabPath) == null)
            {
                Debug.LogWarning($"PlayerAnimatorControllerBuilder ({set.CharacterName}): не найден префаб {set.PlayerPrefabPath} — тайминги нокдауна не обновлены. Создай префаб и перезапусти сборку.");
                return;
            }

            // PrefabUtility.SavePrefabAsset на GameObject из AssetDatabase.LoadAssetAtPath ненадёжен для
            // вложенных PrefabInstance-оверрайдов (например, Animator.m_Controller на визуальном ребёнке) —
            // он может молча обнулить их. LoadPrefabContents/SaveAsPrefabAsset — единственный путь,
            // который гарантированно сохраняет такие оверрайды нетронутыми.
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(set.PlayerPrefabPath);
            try
            {
                PlayerController controller = prefabContents.GetComponent<PlayerController>();
                if (controller == null)
                {
                    Debug.LogWarning($"PlayerAnimatorControllerBuilder ({set.CharacterName}): на {set.PlayerPrefabPath} нет PlayerController — тайминги нокдауна не обновлены.");
                    return;
                }

                var serialized = new SerializedObject(controller);
                serialized.FindProperty("knockdownFrontDuration").floatValue = frontDuration;
                serialized.FindProperty("knockdownBackDuration").floatValue = backDuration;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(prefabContents, set.PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }
        }

        private static AnimatorState AddState(AnimatorStateMachine machine, string name, AnimationClip clip, float speed, Vector3 position)
        {
            AnimatorState state = machine.AddState(name, position);
            state.motion = clip;
            state.speed = speed;
            return state;
        }

        private static void AddConditionTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, float threshold, string parameter)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = TransitionDuration;
            transition.AddCondition(mode, threshold, parameter);
        }

        private static void AddTriggerTransition(AnimatorStateMachine machine, AnimatorState to, string trigger, float duration)
        {
            AnimatorStateTransition transition = machine.AddAnyStateTransition(to);
            transition.hasExitTime = false;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        }

        private static void AddExitTransition(AnimatorState from, AnimatorState to, float exitTime, float duration)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = true;
            transition.exitTime = exitTime;
            transition.duration = duration;
        }

        private static AnimationClip LoadClip(string fileName)
        {
            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(AnimationsFolder + fileName);
            AnimationClip fallback = null;

            foreach (Object subAsset in subAssets)
            {
                if (subAsset is not AnimationClip clip || clip.name.StartsWith("__preview__"))
                {
                    continue;
                }

                fallback ??= clip;
            }

            if (fallback == null)
            {
                Debug.LogError($"PlayerAnimatorControllerBuilder: в {fileName} нет клипа анимации.");
            }

            return fallback;
        }
    }
}
