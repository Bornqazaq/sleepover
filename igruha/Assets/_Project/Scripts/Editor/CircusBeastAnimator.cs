using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Аниматор зверя в яме: отдельный контроллер на клипах, которые уже лежат
    /// в проекте.
    ///
    /// <b>Почему отдельный контроллер, а не правка игроцких.</b> Восемь
    /// аниматоров персонажей и все клипы заморожены (igruha/CLAUDE.md,
    /// раздел 🔒 0). Здесь они только читаются: контроллер свой, ссылается на
    /// те же <c>.fbx</c>, ни один игроцкий ассет не открывается на запись.
    ///
    /// <b>Почему клипы человека налезают на зверя.</b> Все 81 клип в
    /// <c>Art/Animations</c> импортированы как Humanoid, и персонажи Synty —
    /// тоже (<c>Characters.fbx</c> пака HorrorCarnival, <c>animationType: 3</c>).
    /// Humanoid-ретаргет переносит движение по пропорциям скелета, а не по
    /// именам костей, поэтому клип, снятый с Босса, идёт на силача из цирка
    /// без единой правки. Ровно этим зверь и дёшев: анимации уже оплачены.
    ///
    /// <b>Три параметра — те же, что ждёт <c>PitBear</c>:</b> <c>Speed</c>
    /// (float, м/с), <c>Strike</c> (триггер удара), <c>Roar</c> (триггер
    /// насмешки под нижней клеткой). Имена совпадают с полями компонента по
    /// умолчанию, поэтому в инспекторе их менять не нужно.
    /// </summary>
    internal static class CircusBeastAnimator
    {
        private const string AnimationsFolder = "Assets/_Project/Art/Animations/";
        private const string CircusFolder = AnimationsFolder + "Circus";
        internal const string ControllerPath = CircusFolder + "/PitBeastAnimator.controller";

        /// <summary>Параметры. Совпадают со значениями по умолчанию в <c>PitBear</c>.</summary>
        private const string SpeedParameter = "Speed";
        private const string StrikeParameter = "Strike";
        private const string RoarParameter = "Roar";

        private const string LocomotionState = "Locomotion";
        private const string StrikeState = "Strike";
        private const string RoarState = "Roar";

        /// <summary>
        /// Клипы зверя. Все пять — точки, где вид зверя меняется одной строкой,
        /// без правки логики: <c>PitBear</c> знает только про скорость и два
        /// триггера.
        ///
        /// Выбор объясняется так:
        /// — <i>стойка</i> сутулая и тяжёлая, а не бодрая: зверь в покое
        ///   переминается, а не позирует;
        /// — <i>шаг</i> крадущийся — это патруль пустой ямы на 2.5 м/с;
        /// — <i>бег</i> быстрый и прямой: погоня на 5.5 м/с должна пугать;
        /// — <i>удар</i> двойкой, а не одиночным кроссом: размашистее читается
        ///   с высоты клетки;
        /// — <i>насмешка</i> — пляска под клеткой. Урона в этом состоянии нет
        ///   (спека 3.5), поэтому и движение выбрано дразнящее, а не боевое.
        /// </summary>
        private const string IdleClip = "MyBoy@Old Man Idle.fbx";
        private const string WalkClip = "Aza@Sneaking Forward.fbx";
        private const string RunClip = "Karlan@Fast Run.fbx";
        private const string StrikeClip = "Boss@Jab Cross.fbx";
        private const string RoarClip = "Shlanga@dance1.fbx";

        /// <summary>
        /// Пороги смешивания — ровно скорости из <c>CircusBearConfig</c>:
        /// патруль 2.5 м/с, погоня 5.5 м/с. Держать их числом здесь нельзя было
        /// бы, если бы они читались в рантайме, но контроллер собирается один
        /// раз в редакторе, а конфиг — ScriptableObject, до которого билдеру
        /// не дотянуться без ссылки на ассет. Разойдутся — зверь просто
        /// начнёт скользить, логика не сломается.
        /// </summary>
        private const float PatrolSpeed = 2.5f;
        private const float ChaseSpeed = 5.5f;

        /// <summary>
        /// Поправка темпа клипов под рост зверя. Клипы сняты с человека 1.8 м,
        /// зверь — 2.4 м: у него шаг длиннее во столько же раз, и при родном
        /// темпе ноги проскальзывают вперёд. Замедление на отношение ростов
        /// убирает основную часть проскальзывания; остаток доводится глазами
        /// на первом плейтесте.
        /// </summary>
        private const float GaitTimeScale = 0.75f;

        /// <summary>Переход в удар и обратно, с. Короткий: замах не должен опаздывать за логикой.</summary>
        private const float StrikeBlend = 0.08f;
        private const float ReturnBlend = 0.15f;

        /// <summary>Доля клипа, после которой одноразовое состояние возвращается в ход.</summary>
        private const float ExitTime = 0.85f;

        [MenuItem("Igruha/Цирк/Пересобрать аниматор зверя")]
        internal static void Rebuild()
        {
            AnimatorController controller = LoadOrBuild();
            if (controller != null)
            {
                Debug.Log($"CircusBeastAnimator: контроллер зверя пересобран — {ControllerPath}");
            }
        }

        /// <summary>
        /// Готовый контроллер: собирается, если его ещё нет, и пересобирается
        /// на месте, если есть.
        ///
        /// <b>Пересборка на месте, а не удаление и создание заново.</b> Удалив
        /// ассет, мы сменили бы ему GUID, и ссылка из сцены обнулилась бы
        /// молча — зверь остался бы стоять без анимаций, а в консоли не было бы
        /// ни строчки. Ровно этот класс поломки стоил шатру интерфейса 04.09
        /// (STATE.md, раздел 3.78).
        /// </summary>
        internal static AnimatorController LoadOrBuild()
        {
            AnimationClip idle = LoadClip(IdleClip);
            AnimationClip walk = LoadClip(WalkClip);
            AnimationClip run = LoadClip(RunClip);
            AnimationClip strike = LoadClip(StrikeClip);
            AnimationClip roar = LoadClip(RoarClip);

            if (idle == null || walk == null || run == null || strike == null || roar == null)
            {
                Debug.LogError("CircusBeastAnimator: не хватает клипов, контроллер не собран.");
                return null;
            }

            if (!AssetDatabase.IsValidFolder(CircusFolder))
            {
                AssetDatabase.CreateFolder(AnimationsFolder.TrimEnd('/'), "Circus");
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }
            else
            {
                Wipe(controller);
            }

            controller.AddParameter(SpeedParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(StrikeParameter, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(RoarParameter, AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimatorState locomotion = machine.AddState(LocomotionState, new Vector3(300f, 0f, 0f));
            locomotion.motion = BuildLocomotion(controller, idle, walk, run);
            machine.defaultState = locomotion;

            AddOneShot(machine, locomotion, StrikeState, strike, StrikeParameter, new Vector3(300f, 120f, 0f));
            AddOneShot(machine, locomotion, RoarState, roar, RoarParameter, new Vector3(300f, 240f, 0f));

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>
        /// Ход зверя одним деревом по скорости: стойка → крадущийся шаг →
        /// бег. Дерево, а не три состояния с переходами, потому что скорость
        /// <c>PitBear</c> меняет плавно (разгон после задержки), и на переходах
        /// зверь дёргался бы между шагом и бегом на каждом пороге.
        /// </summary>
        private static BlendTree BuildLocomotion(AnimatorController controller,
            AnimationClip idle, AnimationClip walk, AnimationClip run)
        {
            var tree = new BlendTree
            {
                name = LocomotionState,
                blendType = BlendTreeType.Simple1D,
                blendParameter = SpeedParameter,
                useAutomaticThresholds = false
            };

            AssetDatabase.AddObjectToAsset(tree, controller);

            tree.AddChild(idle, 0f);
            tree.AddChild(walk, PatrolSpeed);
            tree.AddChild(run, ChaseSpeed);

            // children отдаёт копию массива — поправку темпа обязательно
            // писать обратно, иначе она никуда не попадёт.
            ChildMotion[] children = tree.children;
            children[0].timeScale = 1f;
            children[1].timeScale = GaitTimeScale;
            children[2].timeScale = GaitTimeScale;
            tree.children = children;

            return tree;
        }

        /// <summary>
        /// Одноразовое состояние по триггеру: вход из Any State, выход обратно
        /// в ход по истечении клипа. <c>canTransitionToSelf</c> снят — иначе
        /// повторный триггер в середине удара перезапускал бы замах с нуля.
        /// </summary>
        private static void AddOneShot(AnimatorStateMachine machine, AnimatorState back,
            string stateName, AnimationClip clip, string trigger, Vector3 position)
        {
            AnimatorState state = machine.AddState(stateName, position);
            state.motion = clip;

            AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
            enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            enter.hasExitTime = false;
            enter.duration = StrikeBlend;
            enter.canTransitionToSelf = false;

            AnimatorStateTransition exit = state.AddTransition(back);
            exit.hasExitTime = true;
            exit.exitTime = ExitTime;
            exit.duration = ReturnBlend;
        }

        /// <summary>
        /// Снести содержимое контроллера, сохранив сам ассет. Состояния и
        /// параметры удаляются явно, а осиротевшие деревья смешивания — по
        /// подассетам: <c>RemoveState</c> дерево внутри себя не трогает, и
        /// после третьей пересборки в файле лежало бы три мёртвых дерева.
        /// </summary>
        private static void Wipe(AnimatorController controller)
        {
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            ChildAnimatorState[] states = machine.states;
            for (int i = 0; i < states.Length; i++)
            {
                machine.RemoveState(states[i].state);
            }

            AnimatorControllerParameter[] parameters = controller.parameters;
            for (int i = parameters.Length - 1; i >= 0; i--)
            {
                controller.RemoveParameter(i);
            }

            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(ControllerPath);
            for (int i = 0; i < subAssets.Length; i++)
            {
                if (subAssets[i] is BlendTree tree)
                {
                    Object.DestroyImmediate(tree, true);
                }
            }
        }

        /// <summary>
        /// Клип из <c>.fbx</c>. Тот же приём, что в
        /// <c>PlayerAnimatorControllerBuilder</c>: в файле лежит и модель,
        /// и предпросмотровые клипы, и брать нужно первый настоящий.
        /// </summary>
        private static AnimationClip LoadClip(string fileName)
        {
            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(AnimationsFolder + fileName);
            for (int i = 0; i < subAssets.Length; i++)
            {
                if (subAssets[i] is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    return clip;
                }
            }

            Debug.LogError($"CircusBeastAnimator: в {fileName} нет клипа анимации.");
            return null;
        }
    }
}
