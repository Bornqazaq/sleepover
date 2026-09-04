using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Достраивает контроллерам персонажей слой сидячей позы «Верю / не верю»:
    /// одно состояние с клипом этого персонажа, вес по умолчанию ноль.
    ///
    /// <b>Почему отдельным слоем, а не состоянием в основном.</b> Основной слой
    /// заморожен (igruha/CLAUDE.md, раздел 0) — там присед, прыжок, нокдаун,
    /// удар и танцы. Пока вес слоя ноль, персонаж анимируется ровно как раньше,
    /// байт в байт. Тот же приём уже применён к слою ружья Охотника и к слою
    /// поз «Дырки в стене».
    ///
    /// <b>Почему свой слой, а не пятая поза в слое «Pose».</b> Слой поз
    /// «Дырки» ведёт <c>PlayerPoseAbility</c>, который живёт в её мини-игре и
    /// завязан на её типы и конфиг: пятая поза притащила бы «Дырку» в «Верю».
    /// Два перекрывающих слоя друг другу не мешают: одновременно они не
    /// включаются никогда — сцены разные, и вес поднимает только та игра,
    /// которая сейчас идёт.
    ///
    /// <b>Параметра нет.</b> Состояние одно, и включает его вес слоя, а не
    /// условие перехода: <c>CharacterAnimatorDriver.SetSitting</c> поднимает
    /// вес при рассадке и роняет при выходе из-за стола.
    /// </summary>
    internal static class BelieveOrNotSitLayerBuilder
    {
        private const string AnimationsFolder = "Assets/_Project/Art/Animations/";

        /// <summary>Имя слоя. По нему же его находит в рантайме <c>CharacterAnimatorDriver</c>.</summary>
        internal const string SitLayerName = "Sit";

        private const string SitStateName = "Sit";

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

        /// <summary>Имена персонажей в порядке <see cref="ControllerPaths"/>: у каждого свой клип посадки.</summary>
        private static readonly string[] CharacterNames =
        {
            "Karlan", "Boss", "Shlanga", "Fat", "MyBoy", "Girl", "Milez", "Aza"
        };

        [MenuItem("Igruha/Player/Add Sit Layer To All Controllers")]
        internal static void AddToAll()
        {
            int patched = 0;
            for (int i = 0; i < ControllerPaths.Length; i++)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPaths[i]);
                if (controller == null)
                {
                    Debug.LogError($"BelieveOrNotSitLayerBuilder: контроллера нет по пути {ControllerPaths[i]}");
                    continue;
                }

                if (Build(controller, CharacterNames[i]))
                {
                    patched++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"BelieveOrNotSitLayerBuilder: слой «{SitLayerName}» добавлен в контроллеров — {patched}.");
        }

        /// <summary>
        /// Собрать слой в контроллере. Идемпотентно: прежний слой с тем же
        /// именем сносится целиком, а не дополняется, — иначе повторный запуск
        /// оставляет два слоя, и второй молча перекрывает первый.
        /// </summary>
        internal static bool Build(AnimatorController controller, string characterName)
        {
            if (controller == null)
            {
                return false;
            }

            AnimationClip clip = BelieveOrNotSitClipBuilder.LoadOrBuild(characterName);
            if (clip == null)
            {
                Debug.LogError($"BelieveOrNotSitLayerBuilder ({characterName}): клип посадки не собрался — слой не добавлен. " +
                               "Пересобрать: Igruha/Player/Rebuild Believe Or Not Sit Clip.");
                return false;
            }

            RemoveExistingLayer(controller);
            controller.AddLayer(SitLayerName);

            // controller.layers отдаёт копию массива: правку веса и режима
            // наложения обязательно писать обратно, иначе она никуда не попадёт.
            AnimatorControllerLayer[] layers = controller.layers;
            int index = layers.Length - 1;
            layers[index].defaultWeight = 0f;
            layers[index].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;

            AnimatorStateMachine machine = controller.layers[index].stateMachine;
            AnimatorState state = machine.AddState(SitStateName, new Vector3(320f, 0f, 0f));
            state.motion = clip;
            machine.defaultState = state;

            EditorUtility.SetDirty(controller);
            return true;
        }

        private static void RemoveExistingLayer(AnimatorController controller)
        {
            AnimatorControllerLayer[] layers = controller.layers;
            for (int i = layers.Length - 1; i >= 0; i--)
            {
                if (layers[i].name == SitLayerName)
                {
                    controller.RemoveLayer(i);
                }
            }
        }
    }
}
