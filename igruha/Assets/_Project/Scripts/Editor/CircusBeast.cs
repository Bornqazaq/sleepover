using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Igruha.Minigames.Circus;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Зверь в яме: анимированная замена статуе <c>SM_Prop_Bear_Statue_01</c>.
    ///
    /// <b>Почему не медведь.</b> Настоящего медведя нет ни в одном из
    /// тринадцати паков: поиск по слову bear даёт бороды, капкан и плюшевых
    /// мишек, а единственное попадание — витринная фигура без костей. Она и
    /// стояла в яме с 04.09: ездила по опилкам, не переставляя лап. Живых
    /// животных со скелетом у Synty в этих паках нет вовсе — ни одного.
    ///
    /// <b>Что вместо него.</b> Цирковой силач под маской и хвостом оборотня,
    /// перекрашенный в бурый. Он humanoid, и на него ложатся клипы игрока
    /// (см. <see cref="CircusBeastAnimator"/>): зверь ходит, крадётся, бегает,
    /// бьёт лапой и пляшет под клеткой — без единой новой анимации и без
    /// покупок. Для party-game это ещё и точнее по тону: мужик в костюме
    /// зверя, гоняющий тебя по яме, смешнее правильного медведя.
    ///
    /// <b>Логика не тронута.</b> <see cref="PitBear"/> не знает, как зверь
    /// выглядит: он двигает корень и дёргает три параметра аниматора. Ради
    /// этого в нём и заведён <c>Visual</c> — всё, что делает этот класс,
    /// происходит внутри него.
    /// </summary>
    internal static class CircusBeast
    {
        private const string Kids = "Assets/Synty/PolygonKids/Prefabs/Attachments/";

        /// <summary>
        /// Тело. Силач циркового пака: широкие плечи и голый торс, который
        /// после перекраски читается шкурой. Ростом он крупнее прочих Synty —
        /// это же нужно и зверю.
        /// </summary>
        private const string BodyPath =
            "Assets/Synty/PolygonHorrorCarnival/Prefabs/Characters/SM_Chr_Strongman_01.prefab";

        /// <summary>Морда. Маска оборотня — единственная звериная голова в паках, закрывающая лицо целиком.</summary>
        private const string MaskPath = Kids + "Head_Attachments/SM_Chr_Attach_Mask_Werewolf_01.prefab";

        /// <summary>Хвост. Нужен со спины: сверху из клетки зверя видно чаще всего именно сзади.</summary>
        private const string TailPath = Kids + "SM_Chr_Attach_Tail_Werewolf_01.prefab";

        private const string VisualName = "Visual";
        private const string BodyName = "BeastBody";
        private const string MaskName = "BeastMask";
        private const string TailName = "BeastTail";

        /// <summary>
        /// Доля роста, которую занимает морда. Маска сделана на детскую голову
        /// и в родном размере на силача не налезает; подгонка идёт по габариту,
        /// а не множителем, — тогда она переживёт замену модели тела.
        /// </summary>
        private const float MaskHeightRatio = 0.2f;

        /// <summary>Доля роста на длину хвоста.</summary>
        private const float TailSizeRatio = 0.25f;

        /// <summary>
        /// Насколько хвост сдвинут за спину от кости, в долях роста. Хвост
        /// посажен по габариту в точку кости, и без сдвига половина его
        /// оказывается внутри тела.
        /// </summary>
        private const float TailBackRatio = 0.09f;

        /// <summary>
        /// Насколько хвост опущен от нижней кости позвоночника до таза,
        /// в долях роста. Позвоночник у модели начинается выше тазобедренных
        /// суставов, и без спуска хвост рос бы из поясницы.
        /// </summary>
        private const float TailDownRatio = 0.1f;

        /// <summary>Морду поднимает над костью головы, в долях роста: кость сидит в основании черепа.</summary>
        private const float MaskUpRatio = 0.03f;

        /// <summary>
        /// Собрать зверя внутри <paramref name="visual"/>. Возвращает аниматор
        /// тела — его <see cref="PitBear"/> получит в поле <c>animator</c>.
        /// Null означает, что модели пака не нашлись: вызывающий обязан
        /// откатиться на прежний вид, а не оставить яму пустой.
        /// </summary>
        internal static Animator Build(Transform visual, float height)
        {
            if (visual == null)
            {
                return null;
            }

            if (!DressKit.TryLoad(BodyPath, out GameObject bodyPrefab))
            {
                return null;
            }

            AnimatorController controller = CircusBeastAnimator.LoadOrBuild();
            if (controller == null)
            {
                return null;
            }

            var body = (GameObject)PrefabUtility.InstantiatePrefab(bodyPrefab, visual);
            body.name = BodyName;
            body.transform.localPosition = Vector3.zero;

            // Персонажи Synty смотрят в +Z, как и корень медведя: PitBear
            // разворачивает корень через LookRotation, и лишний доворот здесь
            // заставил бы зверя бегать боком. Статуе поворот на 180° был нужен
            // ровно потому, что она смотрела в другую сторону.
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one;
            CircusDress.MarkAsScenery(body, true);

            // Ставим на ноги: пивот у персонажей пака в ступнях, но проверяем
            // габаритом — модель тела здесь заменяемая, и на следующей ничего
            // про её пивот заранее не известно.
            float scale = FitHeight(body, height);
            AlignFeet(body, visual.position);

            Animator animator = body.GetComponent<Animator>();
            if (animator == null)
            {
                animator = body.AddComponent<Animator>();
            }

            animator.runtimeAnimatorController = controller;

            // Корневое движение выключено намеренно: зверя двигает PitBear
            // через transform, и root motion спорил бы с ним — на сервере
            // зверь уезжал бы из ямы, а клиенты видели бы его на месте.
            animator.applyRootMotion = false;

            // 🔴 Красим ДО навески морды и хвоста, а не после. Обе детали
            // садятся на кости, то есть внутрь иерархии тела, и перекраска
            // после навески забирает и их: зверь выходил ровным бурым
            // силуэтом, в котором морда не читалась вовсе.
            DressKit.Repaint(body, CircusPalette.Get(CircusPalette.Tone.BearFur));

            AttachToBone(body, animator, HumanBodyBones.Head, MaskPath, MaskName,
                height * MaskHeightRatio, Vector3.up * (height * MaskUpRatio));
            // Хвост садится на нижнюю кость позвоночника, а не на Hips.
            // 🔴 У рига Synty кость Hips аватара указывает на «Root» — корень
            // всей модели, лежащий на полу (замер: y 0.11 против 1.16 у
            // настоящего таза). Посаженный туда хвост уходил на 19 см под
            // опилки. Позвоночник же стоит там, где ему положено, и вращается
            // вместе с корпусом — то есть хвост ещё и следует за поворотами.
            AttachToBone(body, animator, HumanBodyBones.Spine, TailPath, TailName,
                height * TailSizeRatio,
                -body.transform.forward * (height * TailBackRatio) - Vector3.up * (height * TailDownRatio));

            Debug.Log($"CircusBeast: зверь собран, рост {height:0.00} м, масштаб модели ×{scale:0.00}.");
            return animator;
        }

        /// <summary>
        /// Переодеть зверя в уже открытой сцене, не пересобирая арену.
        ///
        /// 🔴 <b>Ради этого пункт меню и существует.</b> Полная пересборка
        /// <see cref="CircusArenaBuilder"/> пересоздаёт клетки и табло с новыми
        /// <c>fileID</c>, и ссылки контроллеров на них обнуляются молча — обе
        /// игры шатра простояли так без интерфейса с 04.09 (STATE.md, раздел
        /// 3.78). Здесь трогается только содержимое <c>Visual</c> внутри
        /// <c>PitBear</c>: ни одна чужая ссылка не пересоздаётся.
        /// </summary>
        [MenuItem("Igruha/Цирк/Переодеть зверя в яме")]
        internal static void RedressInScene()
        {
            var bear = Object.FindFirstObjectByType<PitBear>(FindObjectsInactive.Include);
            if (bear == null)
            {
                Debug.LogError("CircusBeast: в открытой сцене нет PitBear. Открой Stopwatch.unity или CansOrder.unity.");
                return;
            }

            Transform visual = bear.transform.Find(VisualName);
            if (visual == null)
            {
                var visualGo = new GameObject(VisualName);
                visual = visualGo.transform;
                visual.SetParent(bear.transform, false);
            }

            for (int i = visual.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(visual.GetChild(i).gameObject);
            }

            Animator animator = Build(visual, CircusDress.BearHeight);
            if (animator == null)
            {
                Debug.LogError("CircusBeast: зверь не собрался — Visual остался пустым, верни прежний вид пересборкой арены.");
                return;
            }

            var serialized = new SerializedObject(bear);
            serialized.FindProperty("visualRoot").objectReferenceValue = visual;
            serialized.FindProperty("animator").objectReferenceValue = animator;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(bear.gameObject.scene);
            Debug.Log($"CircusBeast: зверь переодет в сцене {bear.gameObject.scene.name}. Сцену сохранить вручную.");
        }

        /// <summary>
        /// Подгон роста по габариту. Возвращает применённый масштаб — он идёт
        /// в лог, потому что при замене модели тела это первое число, по
        /// которому видно, что новая модель не той величины.
        /// </summary>
        private static float FitHeight(GameObject go, float height)
        {
            if (!TryWorldBounds(go, out Bounds bounds) || bounds.size.y < 0.0001f)
            {
                return 1f;
            }

            float scale = height / bounds.size.y;
            go.transform.localScale = Vector3.one * scale;
            return scale;
        }

        /// <summary>Опустить модель так, чтобы низ габарита лёг в точку.</summary>
        private static void AlignFeet(GameObject go, Vector3 groundPoint)
        {
            if (!TryWorldBounds(go, out Bounds bounds))
            {
                return;
            }

            var anchor = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            go.transform.position += groundPoint - anchor;
        }

        /// <summary>
        /// Навесить деталь на кость humanoid-скелета.
        ///
        /// <b>Посадка по габариту, а не по нулевому локальному положению.</b>
        /// Маска и хвост нарисованы в паке Kids относительно детского корня,
        /// а не относительно кости: обнулив локальную позицию на кости взрослого
        /// силача, мы получили бы маску в животе. Габарит же не врёт независимо
        /// от того, где автор оставил пивот.
        /// </summary>
        private static void AttachToBone(GameObject body, Animator animator, HumanBodyBones bone,
            string prefabPath, string partName, float size, Vector3 offset)
        {
            Transform boneTransform = animator.isHuman ? animator.GetBoneTransform(bone) : null;
            if (boneTransform == null)
            {
                Debug.LogWarning($"CircusBeast: у модели тела нет кости {bone} — деталь {partName} пропущена.");
                return;
            }

            if (!DressKit.TryLoad(prefabPath, out GameObject prefab))
            {
                Debug.LogWarning($"CircusBeast: не нашлась модель {prefabPath} — деталь {partName} пропущена.");
                return;
            }

            var part = (GameObject)PrefabUtility.InstantiatePrefab(prefab, boneTransform);
            part.name = partName;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = Vector3.one;
            CircusDress.MarkAsScenery(part, true);

            if (TryWorldBounds(part, out Bounds bounds))
            {
                float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                if (largest > 0.0001f)
                {
                    part.transform.localScale *= size / largest;
                }
            }

            // Разворачиваем деталь по кости и подтягиваем её центр в точку
            // кости: у маски и хвоста нет ни общего пивота, ни общей оси.
            part.transform.rotation = body.transform.rotation;
            if (TryWorldBounds(part, out Bounds placed))
            {
                part.transform.position += boneTransform.position + offset - placed.center;
            }
        }

        private static bool TryWorldBounds(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds();
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return false;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }
    }
}
