using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Достраивает контроллерам персонажей слой поз «Дырки в стене»: четыре
    /// состояния под клавиши 1–4, вход мгновенный, без анимации перехода.
    ///
    /// <b>Почему отдельным слоем, а не состояниями в основном.</b> Ровно та же
    /// причина, что у слоя ружья (см. <see cref="HunterRifleLayerBuilder"/>):
    /// основной слой заморожен (igruha/CLAUDE.md, раздел 0), там живут присед,
    /// прыжок, нокдаун, удар и танцы. Пока вес слоя ноль, персонаж анимируется
    /// ровно как раньше, байт в байт.
    ///
    /// <b>Почему нельзя было обойтись состояниями по Int из AnyState.</b> Поза
    /// защёлкивается и держится, пока игрок не нажмёт другую цифру. Переход из
    /// AnyState по «Pose == N» срабатывал бы заново каждый кадр и срывал бы
    /// обратно в позу любой удар и прыжок — тот же разбор, из-за которого танцы
    /// входят по триггеру, а не по одному Int (см. BuildEmoteStates). Слой
    /// решает это без состязания переходов: позу показывает вес, а не приоритет.
    ///
    /// <b>Вес слоя гонит игра, а не аниматор.</b> <c>PlayerPoseAbility</c>
    /// поднимает его на время позы и роняет на время нокдауна — сбитый партнёр
    /// обязан быть виден лежащим, иначе провал не читается.
    /// </summary>
    internal static class HoleInWallPoseLayerBuilder
    {
        private const string AnimationsFolder = "Assets/_Project/Art/Animations/";

        /// <summary>Имя слоя. По нему же его находит в рантайме <c>PlayerPoseAbility</c>.</summary>
        internal const string PoseLayerName = "Pose";

        /// <summary>Номер позы, 0 — позы нет. Совпадает с <c>HoleInWallPose</c>.</summary>
        internal const string PoseParameter = "Pose";

        /// <summary>Состояние «позы нет». Пустое: пока игрок не нажал цифру, слою нечего показывать.</summary>
        private const string NoneStateName = "PoseNone";

        /// <summary>Имена состояний поз. Индекс — номер позы минус один.</summary>
        private static readonly string[] StateNames = { "Pose1Candle", "Pose2Titanic", "Pose3Cossack", "Pose4Teapot" };

        /// <summary>
        /// Смена позы мгновенная — спека, раздел 4. Ноль, а не «очень быстро»:
        /// игрок подстраивается под вырез до последнего кадра, и даже сотая доля
        /// секунды блендинга — это кадр, в котором силуэт не тот.
        /// </summary>
        private const float PoseTransitionDuration = 0f;

        private static readonly string[] ControllerPaths =
        {
            AnimationsFolder + "PlayerAnimator.controller",
            AnimationsFolder + "BossAnimator.controller",
            AnimationsFolder + "ShlangaAnimator.controller",
            AnimationsFolder + "FatAnimator.controller",
            AnimationsFolder + "MyBoyAnimator.controller",
            AnimationsFolder + "GirlAnimator.controller",
            AnimationsFolder + "MilezAnimator.controller",
            AnimationsFolder + "AzaAnimator.controller"
        };

        /// <summary>Имена персонажей в порядке <see cref="ControllerPaths"/>. Нужны, чтобы взять клипы именно этого персонажа.</summary>
        private static readonly string[] CharacterNames =
        {
            "Karlan", "Boss", "Shlanga", "Fat", "MyBoy", "Girl", "Milez", "Aza"
        };

        [MenuItem("Igruha/Player/Add Pose Layer To All Controllers")]
        private static void AddToAll()
        {
            int patched = 0;
            for (int i = 0; i < ControllerPaths.Length; i++)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPaths[i]);
                if (controller == null)
                {
                    Debug.LogError($"HoleInWallPoseLayerBuilder: контроллера нет по пути {ControllerPaths[i]}");
                    continue;
                }

                if (Build(controller, CharacterNames[i]))
                {
                    patched++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"HoleInWallPoseLayerBuilder: слой «{PoseLayerName}» добавлен в контроллеров — {patched}.");
        }

        /// <summary>
        /// Собрать слой поз в контроллере. Идемпотентно: прежний слой с тем же
        /// именем сносится целиком, а не дополняется, — иначе повторный запуск
        /// оставляет два слоя, и второй молча перекрывает первый.
        /// </summary>
        internal static bool Build(AnimatorController controller, string characterName)
        {
            if (controller == null)
            {
                return false;
            }

            AnimationClip[] clips = HoleInWallPoseClipBuilder.LoadOrBuild(characterName);
            if (clips == null)
            {
                Debug.LogError($"HoleInWallPoseLayerBuilder ({characterName}): клипы поз не собрались — слой не добавлен. " +
                               "Пересобрать: Igruha/Player/Rebuild Hole In Wall Pose Clips.");
                return false;
            }

            RemoveExistingLayer(controller);
            EnsurePoseParameter(controller);

            controller.AddLayer(PoseLayerName);

            // controller.layers отдаёт копию массива: правку веса и режима наложения
            // обязательно писать обратно, иначе она никуда не попадёт.
            AnimatorControllerLayer[] layers = controller.layers;
            int index = layers.Length - 1;
            layers[index].defaultWeight = 0f;
            layers[index].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;

            AnimatorStateMachine machine = controller.layers[index].stateMachine;

            AnimatorState none = machine.AddState(NoneStateName, new Vector3(300f, 0f, 0f));
            machine.defaultState = none;
            AddPoseTransition(machine, none, 0);

            for (int i = 0; i < clips.Length; i++)
            {
                AnimatorState state = machine.AddState(StateNames[i], new Vector3(560f, i * 90f, 0f));
                state.motion = clips[i];
                AddPoseTransition(machine, state, i + 1);
            }

            EditorUtility.SetDirty(controller);
            return true;
        }

        /// <summary>
        /// Переход в позу из AnyState по её номеру. <c>canTransitionToSelf</c>
        /// снят: номер держится всё время, пока игрок стоит в позе, и без этого
        /// состояние перезапускалось бы каждый кадр — дрожь позы дёргалась бы
        /// с нулевого кадра и выглядела не дрожью, а миганием.
        /// </summary>
        private static void AddPoseTransition(AnimatorStateMachine machine, AnimatorState target, int poseNumber)
        {
            AnimatorStateTransition transition = machine.AddAnyStateTransition(target);
            transition.hasExitTime = false;
            transition.duration = PoseTransitionDuration;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.Equals, poseNumber, PoseParameter);
        }

        private static void RemoveExistingLayer(AnimatorController controller)
        {
            AnimatorControllerLayer[] layers = controller.layers;
            for (int i = layers.Length - 1; i >= 0; i--)
            {
                if (layers[i].name == PoseLayerName)
                {
                    controller.RemoveLayer(i);
                }
            }
        }

        private static void EnsurePoseParameter(AnimatorController controller)
        {
            AnimatorControllerParameter[] parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == PoseParameter)
                {
                    return;
                }
            }

            controller.AddParameter(PoseParameter, AnimatorControllerParameterType.Int);
        }
    }
}
