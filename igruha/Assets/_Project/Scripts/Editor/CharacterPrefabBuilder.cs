using UnityEditor;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Собирает префаб-вариант персонажа поверх базового Player.prefab —
    /// ровно по схеме Boss.prefab: свой визуал-ребёнок с Animator Controller,
    /// капсула под рост и ссылки в CharacterAnimatorDriver.
    /// Рост задаётся не «на глаз», а множителем от фактического роста Boss:
    /// габариты моделей меряются в редакторе, поэтому масштаб FBX значения не имеет.
    /// </summary>
    internal static class CharacterPrefabBuilder
    {
        private const string BasePrefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";
        private const string ReferencePrefabPath = "Assets/_Project/Prefabs/Player/Boss.prefab";
        private const string ReferenceIdleClipPath = "Assets/_Project/Art/Animations/Boss@idle.fbx";

        private const string ShlangaPrefabPath = "Assets/_Project/Prefabs/Player/Shlanga.prefab";
        // Визуал берётся из анимационного FBX, а не из Art/Models/Shlanga.fbx:
        // там голый меш от Tripo без скелета, Humanoid-аватар из него не собирается.
        // У Boss ровно так же — визуал приходит из Boss@defaultRunning.fbx.
        private const string ShlangaModelPath = "Assets/_Project/Art/Animations/Shlanga@idle.fbx";
        private const string ShlangaIdleClipPath = ShlangaModelPath;
        private const string ShlangaControllerPath = "Assets/_Project/Art/Animations/ShlangaAnimator.controller";

        /// <summary>Шланга — самый высокий в ростере: на 15% выше Boss.</summary>
        private const float ShlangaHeightFactor = 1.15f;

        private static readonly string[] ShlangaEmotes =
        {
            "Танец 1", "Танец 2", "Танец 3", "Танец 4",
            "Танец 5", "Танец 6", "Танец 7", "Танец 8"
        };

        private const string FatPrefabPath = "Assets/_Project/Prefabs/Player/Fat.prefab";
        // См. комментарий у ShlangaModelPath — та же схема: визуал из анимационного FBX.
        private const string FatModelPath = "Assets/_Project/Art/Animations/Fat@Neutral Idle.fbx";
        private const string FatIdleClipPath = FatModelPath;
        private const string FatControllerPath = "Assets/_Project/Art/Animations/FatAnimator.controller";

        /// <summary>Танцев у Fat нет — колесо эмоций у него просто не откроется.</summary>
        private static readonly string[] FatEmotes = System.Array.Empty<string>();

        /// <summary>Fat — того же роста, что и эталонный Boss.</summary>
        private const float FatHeightFactor = 1f;

        [MenuItem("Igruha/Player/Create Shlanga Prefab")]
        private static void CreateShlanga()
        {
            EnsureEmoteAbilityOnBasePrefab();

            if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ShlangaControllerPath) == null)
            {
                PlayerAnimatorControllerBuilder.BuildShlangaController();
            }

            if (!Create("Shlanga", ShlangaPrefabPath, ShlangaModelPath, ShlangaIdleClipPath, ShlangaControllerPath, ShlangaHeightFactor, ShlangaEmotes))
            {
                return;
            }

            // Второй прогон билдера — уже по существующему префабу: он допишет
            // в PlayerController длительности нокдауна по длине клипов Шланги.
            PlayerAnimatorControllerBuilder.BuildShlangaController();
        }

        [MenuItem("Igruha/Player/Create Fat Prefab")]
        private static void CreateFat()
        {
            EnsureEmoteAbilityOnBasePrefab();

            if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(FatControllerPath) == null)
            {
                PlayerAnimatorControllerBuilder.BuildFatController();
            }

            if (!Create("Fat", FatPrefabPath, FatModelPath, FatIdleClipPath, FatControllerPath, FatHeightFactor, FatEmotes))
            {
                return;
            }

            // Второй прогон билдера — уже по существующему префабу: см. комментарий в CreateShlanga.
            PlayerAnimatorControllerBuilder.BuildFatController();
        }

        /// <summary>
        /// Способность эмоций живёт на базовом префабе: колесо должно быть
        /// у всех персонажей, а список танцев — уже дело варианта (у кого их
        /// пока нет, список пуст и колесо просто не открывается).
        /// </summary>
        private static void EnsureEmoteAbilityOnBasePrefab()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(BasePrefabPath);
            try
            {
                if (contents.GetComponent<PlayerEmoteAbility>() != null)
                {
                    return;
                }

                PlayerEmoteAbility ability = contents.AddComponent<PlayerEmoteAbility>();
                var serialized = new SerializedObject(ability);
                serialized.FindProperty("inputReader").objectReferenceValue = contents.GetComponent<PlayerInputReader>();
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(contents, BasePrefabPath);
                Debug.Log("CharacterPrefabBuilder: на Player.prefab добавлен PlayerEmoteAbility.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static bool Create(
            string characterName,
            string prefabPath,
            string modelPath,
            string idleClipPath,
            string controllerPath,
            float heightFactor,
            string[] emoteNames)
        {
            GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);

            if (basePrefab == null || model == null || controller == null)
            {
                Debug.LogError($"CharacterPrefabBuilder ({characterName}): нет базового префаба, модели или контроллера — проверь пути.");
                return false;
            }

            if (!TryReadReference(out float referenceHeight, out float referenceOffsetY, out CapsuleShape referenceCapsule))
            {
                return false;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            try
            {
                RemoveInheritedVisual(instance);

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, instance.transform);
                visual.transform.localRotation = Quaternion.identity;

                float nativeHeight = MeasureIdleHeight(visual, idleClipPath);
                if (nativeHeight <= 0f)
                {
                    Debug.LogError($"CharacterPrefabBuilder ({characterName}): у модели нет рендереров — рост не посчитать.");
                    return false;
                }

                float targetHeight = referenceHeight * heightFactor;
                float scale = targetHeight / nativeHeight;
                visual.transform.localScale = Vector3.one * scale;
                // Смещение визуала относительно капсулы держим таким же по доле роста,
                // как у эталона, иначе персонаж уедет ступнями под пол или над ним.
                visual.transform.localPosition = new Vector3(0f, referenceOffsetY / referenceHeight * targetHeight, 0f);

                Animator animator = visual.GetComponent<Animator>();
                if (animator == null)
                {
                    Debug.LogError($"CharacterPrefabBuilder ({characterName}): у модели нет Animator — риг не Humanoid? Прогони Setup Import Settings.");
                    return false;
                }

                animator.runtimeAnimatorController = controller;
                // Движение считает Rigidbody, а не корневая кость клипа.
                animator.applyRootMotion = false;

                ApplyCapsule(instance, referenceCapsule, heightFactor);
                BindAnimatorDriver(instance, animator, visual.transform);
                ApplyEmoteNames(instance, emoteNames);

                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                Debug.Log($"CharacterPrefabBuilder ({characterName}): префаб собран. Рост {targetHeight:F2} м " +
                          $"(эталон Boss {referenceHeight:F2} м, ×{heightFactor:F2}), масштаб модели {scale:F3}.");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Габариты эталонного персонажа (Boss), от которых считается рост нового.</summary>
        private static bool TryReadReference(out float height, out float offsetY, out CapsuleShape capsule)
        {
            height = 0f;
            offsetY = 0f;
            capsule = default;

            GameObject referencePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ReferencePrefabPath);
            if (referencePrefab == null)
            {
                Debug.LogError($"CharacterPrefabBuilder: не найден эталонный префаб {ReferencePrefabPath}.");
                return false;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(referencePrefab);
            try
            {
                Transform visual = FindVisualRoot(instance);
                if (visual == null)
                {
                    Debug.LogError("CharacterPrefabBuilder: у эталонного префаба не найден визуал с Animator.");
                    return false;
                }

                height = MeasureIdleHeight(visual.gameObject, ReferenceIdleClipPath);
                offsetY = visual.localPosition.y;

                CapsuleCollider collider = instance.GetComponent<CapsuleCollider>();
                capsule = new CapsuleShape(collider.height, collider.radius, collider.center.y);
                return height > 0f;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Визуал базового префаба (модель Karlan) в варианте не нужен — вариант
        /// приносит свою модель. Удаление ребёнка префаб-инстанса Unity пишет как
        /// оверрайд «удалённый объект», ровно как это сделано в Boss.prefab.
        /// </summary>
        private static void RemoveInheritedVisual(GameObject instance)
        {
            Transform visual = FindVisualRoot(instance);
            if (visual != null)
            {
                Object.DestroyImmediate(visual.gameObject);
            }
        }

        private static Transform FindVisualRoot(GameObject instance)
        {
            for (int i = 0; i < instance.transform.childCount; i++)
            {
                Transform child = instance.transform.GetChild(i);
                if (child.GetComponent<Animator>() != null)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>
        /// Рост в позе покоя: клип idle сэмплируется прямо в редакторе, и уже
        /// по нему меряются кости. Ровно этот рост игрок и видит, когда персонажи
        /// стоят рядом. Все прочие метрики врут: Renderer.bounds у этих FBX
        /// раздут по-разному, bind-поза у Boss сутулая, а humanScale считается
        /// по пропорциям T-позы, а не по фактическому росту стоящего персонажа.
        /// </summary>
        private static float MeasureIdleHeight(GameObject root, string idleClipPath)
        {
            AnimationClip idle = LoadClip(idleClipPath);
            if (idle == null)
            {
                Debug.LogError($"CharacterPrefabBuilder: не найден клип покоя {idleClipPath} — рост не посчитать.");
                return 0f;
            }

            idle.SampleAnimation(root, 0f);
            return MeasureHeight(root);
        }

        private static AnimationClip LoadClip(string path)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    return clip;
                }
            }

            return null;
        }

        /// <summary>Расстояние от нижней стопы до головы с учётом текущего масштаба.</summary>
        private static float MeasureHeight(GameObject root)
        {
            Animator animator = root.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogError($"CharacterPrefabBuilder: у {root.name} нет Humanoid-аватара — рост не посчитать.");
                return 0f;
            }

            Transform head = FindHumanBone(animator, HumanBodyBones.Head, "Head");
            Transform leftFoot = FindHumanBone(animator, HumanBodyBones.LeftFoot, "LeftFoot");
            Transform rightFoot = FindHumanBone(animator, HumanBodyBones.RightFoot, "RightFoot");

            if (head == null || leftFoot == null || rightFoot == null)
            {
                Debug.LogError($"CharacterPrefabBuilder: у {root.name} не нашлись кости головы/стоп.");
                return 0f;
            }

            return head.position.y - Mathf.Min(leftFoot.position.y, rightFoot.position.y);
        }

        /// <summary>
        /// Кость по роли Humanoid. GetBoneTransform работает только на
        /// инициализированном Animator, поэтому вне Play-режима подстраховываемся
        /// картой костей из самого аватара.
        /// </summary>
        private static Transform FindHumanBone(Animator animator, HumanBodyBones bone, string humanName)
        {
            Transform direct = animator.GetBoneTransform(bone);
            if (direct != null)
            {
                return direct;
            }

            foreach (HumanBone mapping in animator.avatar.humanDescription.human)
            {
                if (mapping.humanName.Replace(" ", string.Empty) == humanName)
                {
                    return FindByName(animator.transform, mapping.boneName);
                }
            }

            return null;
        }

        private static Transform FindByName(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>())
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static void ApplyCapsule(GameObject instance, CapsuleShape reference, float factor)
        {
            CapsuleCollider capsule = instance.GetComponent<CapsuleCollider>();
            capsule.height = reference.Height * factor;
            capsule.radius = reference.Radius * factor;
            capsule.center = new Vector3(0f, reference.CenterY * factor, 0f);
        }

        private static void BindAnimatorDriver(GameObject instance, Animator animator, Transform visualRoot)
        {
            var serialized = new SerializedObject(instance.GetComponent<CharacterAnimatorDriver>());
            serialized.FindProperty("animator").objectReferenceValue = animator;
            serialized.FindProperty("visualRoot").objectReferenceValue = visualRoot;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ApplyEmoteNames(GameObject instance, string[] emoteNames)
        {
            PlayerEmoteAbility ability = instance.GetComponent<PlayerEmoteAbility>();
            if (ability == null)
            {
                return;
            }

            var serialized = new SerializedObject(ability);
            SerializedProperty names = serialized.FindProperty("emoteNames");
            names.arraySize = emoteNames.Length;
            for (int i = 0; i < emoteNames.Length; i++)
            {
                names.GetArrayElementAtIndex(i).stringValue = emoteNames[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private readonly struct CapsuleShape
        {
            public readonly float Height;
            public readonly float Radius;
            public readonly float CenterY;

            public CapsuleShape(float height, float radius, float centerY)
            {
                Height = height;
                Radius = radius;
                CenterY = centerY;
            }
        }
    }
}
