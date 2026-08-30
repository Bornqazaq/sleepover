using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Достраивает контроллерам персонажей слой ружья — стойку Охотника Duck Hunt
    /// и выстрел по ЛКМ.
    ///
    /// <b>Почему отдельным слоем, а не состояниями в основном.</b> Основной слой
    /// заморожен (igruha/CLAUDE.md, раздел 0): там живут присед, прыжок, нокдаун
    /// и танцы, и каждый из них уже ломался чужой правкой с добрыми намерениями.
    /// Слой поверх не трогает ни одного существующего состояния и перехода:
    /// пока его вес ноль, персонаж анимируется ровно как раньше, байт в байт.
    /// Ружьё достаётся ровно тому, кому выпала роль, и ровно на время роли.
    ///
    /// <b>Почему клип один на всех восьмерых.</b> Ровно та же причина, что
    /// у приседа и танцев (см. PlayerAnimatorControllerBuilder): все FBX проекта
    /// импортируются как Humanoid, а Humanoid-клип живёт в абстрактном скелете
    /// и ретаргетится Unity на любой аватар. Личная копия каждому — это ещё
    /// семь файлов по 90 МБ в LFS ради одного и того же движения.
    ///
    /// <b>Почему в этом файле, а не только в билдере.</b> Билдер контроллера
    /// удаляет ассет и создаёт заново, меняя GUID, — после него рвутся ссылки
    /// на контроллер в префабах персонажей (разбор в STATE.md за 15.08).
    /// Пункт меню ниже дописывает слой в существующие контроллеры на месте,
    /// GUID не трогая. Тот же слой строит и билдер, поэтому пересборка
    /// контроллера ружьё не теряет.
    /// </summary>
    internal static class HunterRifleLayerBuilder
    {
        private const string AnimationsFolder = "Assets/_Project/Art/Animations/";

        /// <summary>Клип выстрела из ружья. Один на всех — ретаргетится Humanoid'ом.</summary>
        private const string RifleClipFile = "Aza@Firing Rifle.fbx";

        /// <summary>Имя слоя. По нему же его находит в рантайме CharacterAnimatorDriver.</summary>
        internal const string RifleLayerName = "Rifle";

        /// <summary>Триггер выстрела.</summary>
        internal const string FireParameter = "Fire";

        private const string AimStateName = "RifleAim";
        private const string FireStateName = "RifleFire";

        /// <summary>
        /// Ноль — не «очень медленно», а остановленное время состояния: клип
        /// замирает на кадре, с которого вошли, то есть на нулевом. Тем же
        /// приёмом стоит идл приседа. Это и есть та самая статичная стойка
        /// с ружьём, пока ЛКМ не нажата.
        /// </summary>
        private const float AimSpeed = 0f;

        /// <summary>
        /// Вход в выстрел почти мгновенный: анимация обязана совпасть с
        /// хлопком и вспышкой, а не догонять их через десятую долю секунды.
        /// </summary>
        private const float FireTransitionDuration = 0.02f;

        /// <summary>Возврат в стойку мягче: ствол опускается, а не щёлкает обратно.</summary>
        private const float ReturnTransitionDuration = 0.08f;

        /// <summary>Хвост клипа обрезаем: последние кадры — это уже опускание ствола.</summary>
        private const float FireExitTime = 0.95f;

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

        [MenuItem("Igruha/Player/Add Rifle Layer To All Controllers")]
        private static void AddToAll()
        {
            AnimationClip clip = LoadRifleClip();
            if (clip == null)
            {
                return;
            }

            int patched = 0;
            for (int i = 0; i < ControllerPaths.Length; i++)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPaths[i]);
                if (controller == null)
                {
                    Debug.LogError($"HunterRifleLayerBuilder: контроллера нет по пути {ControllerPaths[i]}");
                    continue;
                }

                Build(controller, clip);
                patched++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"HunterRifleLayerBuilder: слой «{RifleLayerName}» добавлен в контроллеров — {patched}.");
        }

        /// <summary>
        /// Собрать слой ружья в контроллере. Идемпотентно: прежний слой с тем же
        /// именем сносится целиком, а не дополняется, — иначе повторный запуск
        /// оставляет два слоя, и второй молча перекрывает первый.
        /// </summary>
        internal static void Build(AnimatorController controller, AnimationClip clip)
        {
            if (controller == null || clip == null)
            {
                return;
            }

            RemoveExistingLayer(controller);
            EnsureFireParameter(controller);

            controller.AddLayer(RifleLayerName);

            // controller.layers отдаёт копию массива: правку веса и режима
            // наложения обязательно писать обратно, иначе она никуда не попадёт.
            AnimatorControllerLayer[] layers = controller.layers;
            int index = layers.Length - 1;
            layers[index].defaultWeight = 0f;
            layers[index].blendingMode = AnimatorLayerBlendingMode.Override;

            // Без IK Pass Unity не вызывает OnAnimatorIK, и RifleGripIk молча
            // ничего не делает: кисти остаются там, куда их поставил клип, а
            // ружьё живёт отдельной жизнью. Один клип ретаргетится на восемь
            // разных пропорций, поэтому руки к ружью приводит только IK.
            layers[index].iKPass = true;
            controller.layers = layers;

            AnimatorStateMachine machine = controller.layers[index].stateMachine;

            AnimatorState aim = machine.AddState(AimStateName, new Vector3(300f, 0f, 0f));
            aim.motion = clip;
            aim.speed = AimSpeed;

            AnimatorState fire = machine.AddState(FireStateName, new Vector3(560f, 0f, 0f));
            fire.motion = clip;
            fire.speed = 1f;

            machine.defaultState = aim;

            AnimatorStateTransition toFire = aim.AddTransition(fire);
            toFire.hasExitTime = false;
            toFire.duration = FireTransitionDuration;
            toFire.AddCondition(AnimatorConditionMode.If, 0f, FireParameter);

            AnimatorStateTransition toAim = fire.AddTransition(aim);
            toAim.hasExitTime = true;
            toAim.exitTime = FireExitTime;
            toAim.duration = ReturnTransitionDuration;

            EditorUtility.SetDirty(controller);
        }

        /// <summary>Клип выстрела. Имя такта внутри FBX не важно — берём первый настоящий клип, как это делает билдер.</summary>
        internal static AnimationClip LoadRifleClip()
        {
            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(AnimationsFolder + RifleClipFile);
            foreach (Object subAsset in subAssets)
            {
                if (subAsset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    return clip;
                }
            }

            Debug.LogError($"HunterRifleLayerBuilder: в {RifleClipFile} нет клипа анимации. " +
                           "Проверь, что файл импортирован и стоит Humanoid " +
                           "(Igruha/Player/Setup Aza Import Settings).");
            return null;
        }

        private static void RemoveExistingLayer(AnimatorController controller)
        {
            AnimatorControllerLayer[] layers = controller.layers;
            for (int i = layers.Length - 1; i >= 0; i--)
            {
                if (layers[i].name == RifleLayerName)
                {
                    controller.RemoveLayer(i);
                }
            }
        }

        private static void EnsureFireParameter(AnimatorController controller)
        {
            AnimatorControllerParameter[] parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == FireParameter)
                {
                    return;
                }
            }

            controller.AddParameter(FireParameter, AnimatorControllerParameterType.Trigger);
        }
    }
}
