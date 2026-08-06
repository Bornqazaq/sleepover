using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Igruha.EditorTools
{
    internal static class PlayerAnimatorControllerBuilder
    {
        private const string ControllerPath = "Assets/_Project/Art/Animations/PlayerAnimator.controller";
        private const string IdleClipModelPath = "Assets/_Project/Art/Animations/Karlan@Happy Idle.fbx";
        private const string RunClipModelPath = "Assets/_Project/Art/Animations/Karlan@Fast Run.fbx";
        private const string JumpClipModelPath = "Assets/_Project/Art/Animations/Karlan@Jumping.fbx";
        private const string PreferredClipName = "mixamo.com";

        private const string SpeedParameter = "Speed";
        private const string JumpParameter = "Jump";
        private const float RunThreshold = 0.1f;
        private const float TransitionDuration = 0.15f;
        private const float JumpExitTime = 0.9f;

        [MenuItem("Igruha/Player/Build Player Animator Controller")]
        private static void Build()
        {
            AnimationClip idleClip = LoadClip(IdleClipModelPath);
            AnimationClip runClip = LoadClip(RunClipModelPath);
            AnimationClip jumpClip = LoadClip(JumpClipModelPath);

            if (idleClip == null || runClip == null || jumpClip == null)
            {
                Debug.LogError("PlayerAnimatorControllerBuilder: не удалось найти один или несколько клипов анимации. Проверьте пути в Art/Animations.");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath);
            }

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter(SpeedParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(JumpParameter, AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

            AnimatorState idleState = stateMachine.AddState("Idle", new Vector3(300f, 0f, 0f));
            idleState.motion = idleClip;

            AnimatorState runState = stateMachine.AddState("Run", new Vector3(300f, 150f, 0f));
            runState.motion = runClip;

            AnimatorState jumpState = stateMachine.AddState("Jump", new Vector3(550f, 75f, 0f));
            jumpState.motion = jumpClip;

            stateMachine.defaultState = idleState;

            AnimatorStateTransition idleToRun = idleState.AddTransition(runState);
            idleToRun.hasExitTime = false;
            idleToRun.duration = TransitionDuration;
            idleToRun.AddCondition(AnimatorConditionMode.Greater, RunThreshold, SpeedParameter);

            AnimatorStateTransition runToIdle = runState.AddTransition(idleState);
            runToIdle.hasExitTime = false;
            runToIdle.duration = TransitionDuration;
            runToIdle.AddCondition(AnimatorConditionMode.Less, RunThreshold, SpeedParameter);

            AnimatorStateTransition anyToJump = stateMachine.AddAnyStateTransition(jumpState);
            anyToJump.hasExitTime = false;
            anyToJump.duration = TransitionDuration;
            anyToJump.canTransitionToSelf = false;
            anyToJump.AddCondition(AnimatorConditionMode.If, 0f, JumpParameter);

            AnimatorStateTransition jumpToIdle = jumpState.AddTransition(idleState);
            jumpToIdle.hasExitTime = true;
            jumpToIdle.exitTime = JumpExitTime;
            jumpToIdle.duration = TransitionDuration;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"PlayerAnimatorControllerBuilder: контроллер создан — {ControllerPath}");
        }

        private static AnimationClip LoadClip(string modelPath)
        {
            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
            AnimationClip fallback = null;

            foreach (Object subAsset in subAssets)
            {
                if (subAsset is not AnimationClip clip || clip.name.StartsWith("__preview__"))
                {
                    continue;
                }

                if (clip.name == PreferredClipName)
                {
                    return clip;
                }

                fallback ??= clip;
            }

            return fallback;
        }
    }
}
