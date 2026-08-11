using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Собирает Animator Controller персонажа и синхронизирует длительности
    /// нокдауна в CharacterConfig с фактической длиной клипов, чтобы управление
    /// возвращалось ровно в тот момент, когда персонаж встал.
    /// </summary>
    internal static class PlayerAnimatorControllerBuilder
    {
        private const string ControllerPath = "Assets/_Project/Art/Animations/PlayerAnimator.controller";
        private const string ConfigPath = "Assets/_Project/Settings/Gameplay/CharacterConfig.asset";
        private const string AnimationsFolder = "Assets/_Project/Art/Animations/";

        private const string IdleClip = "Karlan@Happy Idle.fbx";
        private const string RunClip = "Karlan@Fast Run.fbx";
        private const string JumpClip = "Karlan(Fbx without color)@Unarmed Jump.fbx";
        private const string PunchClip = "Karlan(Fbx without color)@Cross Punch.fbx";
        private const string FallForwardClip = "Karlan(Fbx without color)@Falling Forward Death.fbx";
        private const string FlyBackClip = "Karlan(Fbx without color)@Flying Back Death.fbx";
        private const string StandUpForwardClip = "Karlan(Fbx without color)@Stand Up From Forward.fbx";
        private const string StandUpBackClip = "Karlan(Fbx without color)@Standing Up From Back.fbx";

        private const string SpeedParameter = "Speed";
        private const string JumpParameter = "Jump";
        private const string PunchParameter = "Punch";
        private const string KnockdownFrontParameter = "KnockdownFront";
        private const string KnockdownBackParameter = "KnockdownBack";

        private const float RunThreshold = 0.1f;
        private const float TransitionDuration = 0.12f;
        private const float KnockdownTransitionDuration = 0.06f;

        // Клипы Mixamo длинные для party-game: ускоряем, чтобы падение читалось,
        // но не отбирало управление на четыре секунды.
        private const float JumpSpeed = 1.8f;
        private const float PunchSpeed = 1.2f;
        private const float KnockdownSpeed = 1.5f;

        [MenuItem("Igruha/Player/Build Player Animator Controller")]
        private static void Build()
        {
            AnimationClip idle = LoadClip(IdleClip);
            AnimationClip run = LoadClip(RunClip);
            AnimationClip jump = LoadClip(JumpClip);
            AnimationClip punch = LoadClip(PunchClip);
            AnimationClip fallForward = LoadClip(FallForwardClip);
            AnimationClip flyBack = LoadClip(FlyBackClip);
            AnimationClip standUpForward = LoadClip(StandUpForwardClip);
            AnimationClip standUpBack = LoadClip(StandUpBackClip);

            if (idle == null || run == null || jump == null || punch == null ||
                fallForward == null || flyBack == null || standUpForward == null || standUpBack == null)
            {
                Debug.LogError("PlayerAnimatorControllerBuilder: не найден один из клипов — проверь имена файлов в Art/Animations.");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath);
            }

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
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

            AddTriggerTransition(machine, jumpState, JumpParameter, TransitionDuration);
            AddExitTransition(jumpState, idleState, 0.85f, TransitionDuration);

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

            AssetDatabase.SaveAssets();

            float frontDuration = (flyBack.length + standUpBack.length) / KnockdownSpeed;
            float backDuration = (fallForward.length + standUpForward.length) / KnockdownSpeed;
            SyncConfigDurations(frontDuration, backDuration);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"PlayerAnimatorControllerBuilder: контроллер собран. " +
                      $"Нокдаун в лицо {frontDuration:F2}с, со спины {backDuration:F2}с — записаны в CharacterConfig.");
        }

        private static void SyncConfigDurations(float frontDuration, float backDuration)
        {
            CharacterConfig config = AssetDatabase.LoadAssetAtPath<CharacterConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogWarning($"PlayerAnimatorControllerBuilder: не найден {ConfigPath} — тайминги нокдауна не обновлены.");
                return;
            }

            var serialized = new SerializedObject(config);
            serialized.FindProperty("knockdownFrontDuration").floatValue = frontDuration;
            serialized.FindProperty("knockdownBackDuration").floatValue = backDuration;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
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
