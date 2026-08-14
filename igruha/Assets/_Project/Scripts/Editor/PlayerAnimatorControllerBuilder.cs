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
            string[] danceClips = null)
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

        private const string SpeedParameter = "Speed";
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
                standUpForwardClip: "Karlan(Fbx without color)@Stand Up From Forward.fbx"));
        }

        [MenuItem("Igruha/Player/Build Boss Animator Controller")]
        private static void BuildBoss()
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
                runJumpClip: "Boss@jumpRun.fbx"));
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
                }));
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

            bool missingRunJump = !string.IsNullOrEmpty(set.RunJumpClip) && runJump == null;
            if (idle == null || run == null || jump == null || punch == null ||
                fallForward == null || flyBack == null || standUpForward == null || standUpBack == null ||
                missingRunJump)
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

            AddConditionTransition(idleState, runState, AnimatorConditionMode.Greater, RunThreshold, SpeedParameter);
            AddConditionTransition(runState, idleState, AnimatorConditionMode.Less, RunThreshold, SpeedParameter);

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
