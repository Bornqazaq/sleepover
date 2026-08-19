using UnityEditor;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Собирает префаб-вариант персонажа поверх базового Player.prefab: свой
    /// визуал-ребёнок с Animator Controller, капсула под рост и ссылки в
    /// CharacterAnimatorDriver/PlayerController.
    /// Рост — единственный физический параметр, который отличает персонажей
    /// (геймплейные величины остаются общими в CharacterConfig). Каждый персонаж
    /// задаёт его одним числом в метрах в своём [MenuItem]; вся арифметика —
    /// общая и от персонажа не зависит, поэтому добавление нового не трогает
    /// код билдера, только добавляет ему одноимённый CreateX().
    /// </summary>
    internal static class CharacterPrefabBuilder
    {
        private const string BasePrefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";

        // Karlan живёт прямо в базовом Player.prefab (не вариант), но проходит
        // тот же расчёт роста/капсулы — см. ResizeKarlan.
        private const string KarlanIdleClipPath = "Assets/_Project/Art/Animations/Karlan@Happy Idle.fbx";
        private const float KarlanHeightMeters = 1.65f;

        private const string BossPrefabPath = "Assets/_Project/Prefabs/Player/Boss.prefab";
        private const string BossModelPath = "Assets/_Project/Art/Animations/Boss@defaultRunning.fbx";
        private const string BossIdleClipPath = "Assets/_Project/Art/Animations/Boss@idle.fbx";
        private const string BossControllerPath = "Assets/_Project/Art/Animations/BossAnimator.controller";
        private const float BossHeightMeters = 1.85f;
        private static readonly string[] BossEmotes = System.Array.Empty<string>();

        private const string ShlangaPrefabPath = "Assets/_Project/Prefabs/Player/Shlanga.prefab";
        // Визуал берётся из анимационного FBX, а не из Art/Models/Shlanga.fbx:
        // там голый меш от Tripo без скелета, Humanoid-аватар из него не собирается.
        private const string ShlangaModelPath = "Assets/_Project/Art/Animations/Shlanga@idle.fbx";
        private const string ShlangaIdleClipPath = ShlangaModelPath;
        private const string ShlangaControllerPath = "Assets/_Project/Art/Animations/ShlangaAnimator.controller";
        private const float ShlangaHeightMeters = 2.00f;

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
        private const float FatHeightMeters = 1.80f;

        /// <summary>Танцев у Fat нет — колесо эмоций у него просто не откроется.</summary>
        private static readonly string[] FatEmotes = System.Array.Empty<string>();

        private const string MyBoyPrefabPath = "Assets/_Project/Prefabs/Player/MyBoy.prefab";
        // См. комментарий у ShlangaModelPath — та же схема: визуал из анимационного FBX.
        private const string MyBoyModelPath = "Assets/_Project/Art/Animations/MyBoy@Old Man Idle.fbx";
        private const string MyBoyIdleClipPath = MyBoyModelPath;
        private const string MyBoyControllerPath = "Assets/_Project/Art/Animations/MyBoyAnimator.controller";
        private const float MyBoyHeightMeters = 1.75f;

        /// <summary>Танцев у MyBoy нет — колесо эмоций у него просто не откроется.</summary>
        private static readonly string[] MyBoyEmotes = System.Array.Empty<string>();

        private const string GirlPrefabPath = "Assets/_Project/Prefabs/Player/Girl.prefab";
        // См. комментарий у ShlangaModelPath — та же схема: визуал из анимационного
        // FBX. У Girl отдельной модели в Art/Models нет вовсе, только клипы.
        private const string GirlModelPath = "Assets/_Project/Art/Animations/Girl@idle.fbx";
        private const string GirlIdleClipPath = GirlModelPath;
        private const string GirlControllerPath = "Assets/_Project/Art/Animations/GirlAnimator.controller";
        private const float GirlHeightMeters = 1.70f;

        /// <summary>Танцев у Girl нет — колесо эмоций у неё просто не откроется.</summary>
        private static readonly string[] GirlEmotes = System.Array.Empty<string>();

        private const string MilezPrefabPath = "Assets/_Project/Prefabs/Player/Milez.prefab";
        // См. комментарий у ShlangaModelPath — та же схема: визуал из анимационного
        // FBX. У Milez, как и у Girl, отдельной модели в Art/Models нет, только клипы.
        private const string MilezModelPath = "Assets/_Project/Art/Animations/Milez@idle.fbx";
        private const string MilezIdleClipPath = MilezModelPath;
        private const string MilezControllerPath = "Assets/_Project/Art/Animations/MilezAnimator.controller";
        private const float MilezHeightMeters = 1.75f;

        /// <summary>Танцев у Milez нет — колесо эмоций у него просто не откроется.</summary>
        private static readonly string[] MilezEmotes = System.Array.Empty<string>();

        private const string AzaPrefabPath = "Assets/_Project/Prefabs/Player/Aza.prefab";
        // См. комментарий у ShlangaModelPath — та же схема: визуал из анимационного
        // FBX. Art/Models/Aza.fbx для префаба не годится по той же причине, что и
        // модель Шланги: там меш без ригa, Humanoid-аватар из него не собрать.
        private const string AzaModelPath = "Assets/_Project/Art/Animations/Aza@Happy Idle.fbx";
        private const string AzaIdleClipPath = AzaModelPath;
        private const string AzaControllerPath = "Assets/_Project/Art/Animations/AzaAnimator.controller";
        private const float AzaHeightMeters = 1.72f;

        /// <summary>Танцев у Aza нет — колесо эмоций у него просто не откроется.</summary>
        private static readonly string[] AzaEmotes = System.Array.Empty<string>();

        /// <summary>
        /// Высота точки привязки камеры — доля от роста персонажа (грудь/плечи),
        /// не корень капсулы: при разнице в рост камера иначе кадрирует
        /// низких и высоких персонажей, если целится в один и тот же уровень
        /// (капсулу-пивот на земле). Общая для всех — рост уже учтён через height.
        /// </summary>
        private const float CameraTargetHeightRatio = 0.85f;

        // --- Текстуры ---------------------------------------------------------
        // Меш приходит из анимационных FBX (Mixamo/Tripo), а они текстуру не
        // отдают — материал остаётся серым, пока карты не назначены вручную.
        // Поддерживаются две конвенции имён, обе работают для нового персонажа
        // без правки кода — достаточно положить файлы и прогнать CreateX():
        //   • полный набор PBR: base_color(Имя).<ext>, metallic_roughness(Имя).<ext>,
        //     normal(Имя).<ext> — так экспортирует Tripo/glTF (Girl);
        //   • одна карта: Textures/{Имя}.<ext> — старые персонажи (Fat, MyBoy).
        private const string TexturesFolder = "Assets/_Project/Art/Textures/";
        private static readonly string[] TextureExtensions = { ".jpg", ".jpeg", ".png" };
        // Куда кладём материалы-подмены (см. RemapMaterialTextures) — по одному
        // на персонажа, переиспользуется при каждой пересборке.
        private const string MaterialsFolder = "Assets/_Project/Art/Materials/";

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MetallicGlossMapId = Shader.PropertyToID("_MetallicGlossMap");
        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int SmoothnessTextureChannelId = Shader.PropertyToID("_SmoothnessTextureChannel");

        [MenuItem("Igruha/Player/Resize Karlan (Base Prefab)")]
        private static void ResizeKarlan()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(BasePrefabPath);
            try
            {
                Transform visual = FindVisualRoot(contents);
                if (visual == null)
                {
                    Debug.LogError("CharacterPrefabBuilder (Karlan): у Player.prefab не найден визуал с Animator.");
                    return;
                }

                if (!ApplyHeightAndCapsule("Karlan", visual, contents, KarlanIdleClipPath, KarlanHeightMeters, out float scale))
                {
                    return;
                }

                BindCameraTarget(contents, EnsureCameraTarget(contents, KarlanHeightMeters));

                // Karlan собирается не через Create(), поэтому ссылки драйвера ему
                // надо переприсвоить здесь же — иначе он единственный останется
                // со сжатием модели вместо клипа приседа. Заодно это чинит значение
                // и всем вариантам: они наследуют его от базового префаба.
                BindAnimatorDriver(contents, visual.GetComponent<Animator>(), visual, hasCrouchAnimation: true);

                PrefabUtility.SaveAsPrefabAsset(contents, BasePrefabPath);
                Debug.Log($"CharacterPrefabBuilder (Karlan): рост {KarlanHeightMeters:F2} м, масштаб модели {scale:F3}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        [MenuItem("Igruha/Player/Create Boss Prefab")]
        private static void CreateBoss()
        {
            EnsureEmoteAbilityOnBasePrefab();

            if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(BossControllerPath) == null)
            {
                PlayerAnimatorControllerBuilder.BuildBossController();
            }

            if (!Create("Boss", BossPrefabPath, BossModelPath, BossIdleClipPath, BossControllerPath, BossHeightMeters, BossEmotes, hasCrouchAnimation: true))
            {
                return;
            }

            PlayerAnimatorControllerBuilder.BuildBossController();
        }

        [MenuItem("Igruha/Player/Create Shlanga Prefab")]
        private static void CreateShlanga()
        {
            EnsureEmoteAbilityOnBasePrefab();

            if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ShlangaControllerPath) == null)
            {
                PlayerAnimatorControllerBuilder.BuildShlangaController();
            }

            if (!Create("Shlanga", ShlangaPrefabPath, ShlangaModelPath, ShlangaIdleClipPath, ShlangaControllerPath, ShlangaHeightMeters, ShlangaEmotes, hasCrouchAnimation: true))
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

            if (!Create("Fat", FatPrefabPath, FatModelPath, FatIdleClipPath, FatControllerPath, FatHeightMeters, FatEmotes, hasCrouchAnimation: true))
            {
                return;
            }

            // Второй прогон билдера — уже по существующему префабу: см. комментарий в CreateShlanga.
            PlayerAnimatorControllerBuilder.BuildFatController();
        }

        [MenuItem("Igruha/Player/Create MyBoy Prefab")]
        private static void CreateMyBoy()
        {
            EnsureEmoteAbilityOnBasePrefab();

            if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(MyBoyControllerPath) == null)
            {
                PlayerAnimatorControllerBuilder.BuildMyBoyController();
            }

            if (!Create("MyBoy", MyBoyPrefabPath, MyBoyModelPath, MyBoyIdleClipPath, MyBoyControllerPath, MyBoyHeightMeters, MyBoyEmotes, hasCrouchAnimation: true))
            {
                return;
            }

            // Второй прогон билдера — уже по существующему префабу: см. комментарий в CreateShlanga.
            PlayerAnimatorControllerBuilder.BuildMyBoyController();
        }

        [MenuItem("Igruha/Player/Create Girl Prefab")]
        internal static void CreateGirl()
        {
            // Клипы Girl приехали из Mixamo как Generic: без Humanoid-рига у модели
            // не будет Animator и префаб не собрать. Поэтому импорт настраивается
            // здесь же — сборка персонажа остаётся одним действием, а не двумя.
            CharacterClipImportSetup.SetupGirl();

            EnsureEmoteAbilityOnBasePrefab();

            if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(GirlControllerPath) == null)
            {
                PlayerAnimatorControllerBuilder.BuildGirlController();
            }

            if (!Create("Girl", GirlPrefabPath, GirlModelPath, GirlIdleClipPath, GirlControllerPath, GirlHeightMeters, GirlEmotes, hasCrouchAnimation: true))
            {
                return;
            }

            // Второй прогон билдера — уже по существующему префабу: см. комментарий в CreateShlanga.
            PlayerAnimatorControllerBuilder.BuildGirlController();
        }

        [MenuItem("Igruha/Player/Create Milez Prefab")]
        internal static void CreateMilez()
        {
            // Клипы приехали из Mixamo как Generic — см. комментарий в CreateGirl.
            CharacterClipImportSetup.SetupMilez();

            EnsureEmoteAbilityOnBasePrefab();

            if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(MilezControllerPath) == null)
            {
                PlayerAnimatorControllerBuilder.BuildMilezController();
            }

            if (!Create("Milez", MilezPrefabPath, MilezModelPath, MilezIdleClipPath, MilezControllerPath, MilezHeightMeters, MilezEmotes, hasCrouchAnimation: true))
            {
                return;
            }

            // Второй прогон билдера — уже по существующему префабу: см. комментарий в CreateShlanga.
            PlayerAnimatorControllerBuilder.BuildMilezController();
        }

        [MenuItem("Igruha/Player/Create Aza Prefab")]
        internal static void CreateAza()
        {
            // Клипы приехали из Mixamo как Generic — см. комментарий в CreateGirl.
            CharacterClipImportSetup.SetupAza();

            EnsureEmoteAbilityOnBasePrefab();

            if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AzaControllerPath) == null)
            {
                PlayerAnimatorControllerBuilder.BuildAzaController();
            }

            if (!Create("Aza", AzaPrefabPath, AzaModelPath, AzaIdleClipPath, AzaControllerPath, AzaHeightMeters, AzaEmotes, hasCrouchAnimation: true))
            {
                return;
            }

            // Второй прогон билдера — уже по существующему префабу: см. комментарий в CreateShlanga.
            PlayerAnimatorControllerBuilder.BuildAzaController();
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
            float targetHeightMeters,
            string[] emoteNames,
            bool hasCrouchAnimation = false)
        {
            GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);

            if (basePrefab == null || model == null || controller == null)
            {
                Debug.LogError($"CharacterPrefabBuilder ({characterName}): нет базового префаба, модели или контроллера — проверь пути.");
                return false;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            try
            {
                RemoveInheritedVisual(instance);

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, instance.transform);

                if (!ApplyHeightAndCapsule(characterName, visual.transform, instance, idleClipPath, targetHeightMeters, out float scale))
                {
                    return false;
                }

                Animator animator = visual.GetComponent<Animator>();
                if (animator == null)
                {
                    Debug.LogError($"CharacterPrefabBuilder ({characterName}): у модели нет Animator — риг не Humanoid? Прогони Setup Import Settings.");
                    return false;
                }

                animator.runtimeAnimatorController = controller;
                // Движение считает Rigidbody, а не корневая кость клипа.
                animator.applyRootMotion = false;

                BindCameraTarget(instance, EnsureCameraTarget(instance, targetHeightMeters));
                BindAnimatorDriver(instance, animator, visual.transform, hasCrouchAnimation);
                BindNetworkAnimator(characterName, instance, animator);
                ApplyEmoteNames(instance, emoteNames);

                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                Debug.Log($"CharacterPrefabBuilder ({characterName}): префаб собран. Рост {targetHeightMeters:F2} м, масштаб модели {scale:F3}.");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Общая для всех персонажей арифметика: измеряет натуральные (до
        /// масштабирования) bounds модели в позе Idle, считает по ним коэффициент
        /// масштаба под целевой рост, применяет его к визуалу и пересчитывает
        /// капсулу персонажа. Единая точка, откуда «рост» превращается
        /// в конкретные Transform/Collider значения — что для варианта,
        /// что для базового Player.prefab (Karlan).
        /// </summary>
        private static bool ApplyHeightAndCapsule(
            string characterName,
            Transform visual,
            GameObject capsuleOwner,
            string idleClipPath,
            float targetHeightMeters,
            out float scale)
        {
            scale = 0f;

            // Сбрасываем текущий transform визуала — bounds должны быть
            // натуральными (масштаб модели из FBX/предыдущей сборки не должен
            // влиять на измерение), дальше эти же значения перезапишутся расчётом.
            visual.localScale = Vector3.one;
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;

            if (!TryMeasureIdleBounds(visual.gameObject, capsuleOwner.transform, idleClipPath, out Bounds native))
            {
                Debug.LogError($"CharacterPrefabBuilder ({characterName}): не удалось измерить bounds модели в Idle — рост не посчитать.");
                return false;
            }

            if (native.size.y <= 0f)
            {
                Debug.LogError($"CharacterPrefabBuilder ({characterName}): у модели нулевой рост в Idle bounds — проверь риг/клип.");
                return false;
            }

            scale = targetHeightMeters / native.size.y;
            visual.localScale = Vector3.one * scale;
            // Ступни — на дне капсулы: капсула стоит пивотом на земле (center.y = height/2,
            // низ капсулы = локальный Y0 владельца), поэтому визуал сдвигаем так, чтобы его
            // самая нижняя точка (native.min.y после масштаба) легла ровно на Y0.
            visual.localPosition = new Vector3(0f, -native.min.y * scale, 0f);

            float radius = Mathf.Min(native.size.x, native.size.z) * 0.5f * scale;

            CapsuleCollider capsule = capsuleOwner.GetComponent<CapsuleCollider>();
            if (capsule == null)
            {
                Debug.LogError($"CharacterPrefabBuilder ({characterName}): на префабе нет CapsuleCollider.");
                return false;
            }

            if (targetHeightMeters < radius * 2f)
            {
                Debug.LogWarning($"CharacterPrefabBuilder ({characterName}): радиус капсулы ({radius:F3}) больше половины роста — Unity сама подожмёт высоту капсулы под 2×radius.");
            }

            capsule.height = targetHeightMeters;
            capsule.radius = radius;
            capsule.center = new Vector3(0f, targetHeightMeters * 0.5f, 0f);

            ApplyCharacterTexture(characterName, visual);

            return true;
        }

        /// <summary>
        /// Один набор карт персонажа. Любая из карт может отсутствовать: тогда
        /// соответствующий слот материала просто не трогается.
        /// </summary>
        private readonly struct CharacterTextureSet
        {
            public readonly Texture2D BaseMap;
            public readonly Texture2D MetallicSmoothness;
            public readonly Texture2D Normal;

            public CharacterTextureSet(Texture2D baseMap, Texture2D metallicSmoothness, Texture2D normal)
            {
                BaseMap = baseMap;
                MetallicSmoothness = metallicSmoothness;
                Normal = normal;
            }

            public bool HasAny => BaseMap != null || MetallicSmoothness != null || Normal != null;

            /// <summary>Есть ли что-то сверх Base Map — карты, которых у персонажей старой конвенции не было.</summary>
            public bool HasDataMaps => MetallicSmoothness != null || Normal != null;
        }

        /// <summary>
        /// Назначает карты материалам визуала. Ничего не делает, если материал уже
        /// текстурирован и добавить к нему нечего (Boss/Shlanga получили свою
        /// единственную текстуру автоматически при импорте FBX — Unity сама находит
        /// в проекте файл с именем, на которое ссылается материал внутри FBX), и
        /// просто предупреждает в консоль, если подходящих файлов не нашлось —
        /// чтобы тонущая в общем логе серая текстура не осталась незамеченной.
        /// </summary>
        private static void ApplyCharacterTexture(string characterName, Transform visual)
        {
            SkinnedMeshRenderer[] renderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            CharacterTextureSet textures = default;
            bool searched = false;
            // Один и тот же материал может стоять на нескольких рендерерах, а каждый
            // remap тянет за собой SaveAndReimport модели — обрабатываем по разу.
            var processed = new System.Collections.Generic.HashSet<string>();

            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Material material = renderer.sharedMaterial;
                if (material == null || !processed.Add(material.name))
                {
                    continue;
                }

                if (!searched)
                {
                    textures = FindCharacterTextures(characterName);
                    searched = true;

                    if (!textures.HasAny)
                    {
                        Debug.LogWarning($"CharacterPrefabBuilder ({characterName}): в {TexturesFolder} нет ни base_color({characterName}).jpg/.png, ни {characterName}.jpg/.png — материалы остаются без карт.");
                        return;
                    }
                }

                // Материал уже с Base Map, а больше положить нечего: это персонаж
                // старой конвенции — переприсваивать то же самое незачем.
                if (material.mainTexture != null && !textures.HasDataMaps)
                {
                    continue;
                }

                RemapMaterialTextures(characterName, material, textures);
            }
        }

        /// <summary>
        /// Ищет карты персонажа по обеим конвенциям имён (см. TexturesFolder).
        /// metallic_roughness не отдаётся движку как есть — сначала пересобирается
        /// в раскладку каналов, которую ждёт URP (см. BuildMetallicSmoothnessMap).
        /// </summary>
        private static CharacterTextureSet FindCharacterTextures(string characterName)
        {
            Texture2D baseMap = FindTexture($"base_color({characterName})")
                                ?? FindTexture(characterName);
            Texture2D normal = FindTexture($"normal({characterName})");
            Texture2D metallicRoughness = FindTexture($"metallic_roughness({characterName})");

            Texture2D metallicSmoothness = metallicRoughness == null
                ? null
                : BuildMetallicSmoothnessMap(characterName, metallicRoughness);

            return new CharacterTextureSet(baseMap, metallicSmoothness, normal);
        }

        /// <summary>
        /// Пересобирает glTF-карту metallic_roughness в ту раскладку каналов,
        /// которую читает URP Lit. Раскладки не совпадают, и это не мелочь:
        ///   glTF: R — occlusion, G — roughness, B — metallic;
        ///   URP:  R — metallic,  A — smoothness.
        /// Отдать исходник напрямую нельзя — URP прочитал бы белый occlusion (R=255)
        /// как «металл на 100%» и покрыл персонажа хромом, а гладкость взял бы
        /// из отсутствующей у JPG альфы, то есть как сплошное зеркало.
        /// Поэтому рядом с исходником лежит производная карта: R = metallic (из B),
        /// A = smoothness (из 1 − roughness). Исходник при этом остаётся нетронутым.
        /// Читаем файл напрямую, а не через GetPixels() импортированной текстуры:
        /// импорт сжимает её в DXT и портит именно те значения, которые нужны точно.
        /// </summary>
        private static Texture2D BuildMetallicSmoothnessMap(string characterName, Texture2D metallicRoughness)
        {
            string sourcePath = AssetDatabase.GetAssetPath(metallicRoughness);
            byte[] sourceBytes = System.IO.File.ReadAllBytes(sourcePath);

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!source.LoadImage(sourceBytes))
                {
                    Debug.LogError($"CharacterPrefabBuilder ({characterName}): не читается {sourcePath} — карта металличности не собрана.");
                    return null;
                }

                Color[] pixels = source.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    float metallic = pixels[i].b;
                    float smoothness = 1f - pixels[i].g;
                    pixels[i] = new Color(metallic, metallic, metallic, smoothness);
                }

                var repacked = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
                try
                {
                    repacked.SetPixels(pixels);
                    repacked.Apply();

                    // PNG, а не JPG: альфа несёт гладкость, а JPG её не хранит вовсе.
                    string outputPath = TexturesFolder + characterName + CharacterTextureImportSetup.MetallicSmoothnessSuffix + ".png";
                    System.IO.File.WriteAllBytes(outputPath, repacked.EncodeToPNG());
                    AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
                }
                finally
                {
                    Object.DestroyImmediate(repacked);
                }
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        /// <summary>
        /// Персонажный материал — embedded sub-asset внутри FBX (Mixamo/Tripo
        /// текстур не отдают, поэтому материал изначально пустой). Прямая правка
        /// такого sub-asset'а (SetDirty+SaveAssets) не переживает следующий
        /// реимпорт: FBX импортируется через ImportViaMaterialDescription,
        /// который при каждом реимпорте (а сборка Animator Controller
        /// переимпортирует свой префаб на каждый прогон — см.
        /// PlayerAnimatorControllerBuilder.SyncPrefabDurations) пересобирает
        /// материалы заново и стирает любую прямую правку.
        /// Единственный официальный способ, переживающий реимпорт, — remap
        /// ModelImporter: заводим отдельный .mat с нужными картами и подменяем
        /// им embedded-материал через AddRemap. Ровно так Unity сама сохраняет
        /// текстуры Boss/Shlanga между реимпортами — та же механика, только
        /// explicit вместо auto-search по имени файла.
        /// </summary>
        private static void RemapMaterialTextures(string characterName, Material embeddedMaterial, CharacterTextureSet textures)
        {
            string modelPath = AssetDatabase.GetAssetPath(embeddedMaterial);
            if (AssetImporter.GetAtPath(modelPath) is not ModelImporter importer)
            {
                Debug.LogError($"CharacterPrefabBuilder ({characterName}): {modelPath} — не ModelImporter, карты не назначить.");
                return;
            }

            string externalMaterialPath = MaterialsFolder + characterName + ".mat";
            Material externalMaterial = AssetDatabase.LoadAssetAtPath<Material>(externalMaterialPath);
            if (externalMaterial == null)
            {
                System.IO.Directory.CreateDirectory(MaterialsFolder);
                externalMaterial = new Material(embeddedMaterial.shader) { name = characterName };
                AssetDatabase.CreateAsset(externalMaterial, externalMaterialPath);
            }

            externalMaterial.CopyPropertiesFromMaterial(embeddedMaterial);
            ApplyMapsToMaterial(externalMaterial, textures);
            EditorUtility.SetDirty(externalMaterial);
            AssetDatabase.SaveAssets();

            var identifier = new AssetImporter.SourceAssetIdentifier(typeof(Material), embeddedMaterial.name);
            importer.AddRemap(identifier, externalMaterial);
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Раскладывает карты по слотам URP Lit. Ключевые слова включаются явно:
        /// без них шейдер собирается в вариант без соответствующей карты, и
        /// назначенная текстура просто не читается.
        /// </summary>
        private static void ApplyMapsToMaterial(Material material, CharacterTextureSet textures)
        {
            if (textures.BaseMap != null)
            {
                material.mainTexture = textures.BaseMap;
                if (material.HasProperty(BaseMapId))
                {
                    material.SetTexture(BaseMapId, textures.BaseMap);
                }
            }

            if (textures.Normal != null && material.HasProperty(BumpMapId))
            {
                material.SetTexture(BumpMapId, textures.Normal);
                material.EnableKeyword("_NORMALMAP");
            }

            if (textures.MetallicSmoothness != null && material.HasProperty(MetallicGlossMapId))
            {
                material.SetTexture(MetallicGlossMapId, textures.MetallicSmoothness);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");

                // Гладкость читаем из альфы карты металличности, а не из альфы
                // Base Map: base_color — JPG без альфы, оттуда пришла бы сплошная
                // единица, то есть зеркало вместо кожи и ткани.
                material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                if (material.HasProperty(SmoothnessTextureChannelId))
                {
                    material.SetFloat(SmoothnessTextureChannelId, 0f);
                }

                // Множитель поверх карты — карта уже несёт готовые значения.
                if (material.HasProperty(SmoothnessId))
                {
                    material.SetFloat(SmoothnessId, 1f);
                }
            }
        }

        private static Texture2D FindTexture(string fileNameWithoutExtension)
        {
            foreach (string extension in TextureExtensions)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturesFolder + fileNameWithoutExtension + extension);
                if (texture != null)
                {
                    return texture;
                }
            }

            return null;
        }

        /// <summary>
        /// Точка привязки Cinemachine — на уровне груди текущего персонажа,
        /// а не в его пивоте на земле (иначе Karlan и Shlanga кадрируются по-разному
        /// просто из-за разницы в росте). Ребёнок общий для базового префаба и всех
        /// вариантов: вариант лишь переопределяет его локальную позицию под свой рост,
        /// как и капсулу.
        /// </summary>
        private static Transform EnsureCameraTarget(GameObject owner, float targetHeightMeters)
        {
            Transform target = owner.transform.Find("CameraTarget");
            if (target == null)
            {
                var go = new GameObject("CameraTarget");
                target = go.transform;
                target.SetParent(owner.transform, false);
            }

            target.localPosition = new Vector3(0f, targetHeightMeters * CameraTargetHeightRatio, 0f);
            target.localRotation = Quaternion.identity;
            target.localScale = Vector3.one;
            return target;
        }

        private static void BindCameraTarget(GameObject instance, Transform cameraTarget)
        {
            var serialized = new SerializedObject(instance.GetComponent<PlayerController>());
            serialized.FindProperty("cameraTarget").objectReferenceValue = cameraTarget;
            serialized.ApplyModifiedPropertiesWithoutUndo();
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
        /// Натуральные (до масштабирования) bounds модели в позе покоя — считаются
        /// по факту запечённой геометрии SkinnedMeshRenderer, а не по костям и не по
        /// Renderer.bounds: последний может быть не пересчитан сразу после
        /// SampleAnimation (кэш bounds обновляется рендером, а не сэмплированием),
        /// и тогда меряется устаревшая T-поза вместо Idle. BakeMesh — единственный
        /// способ получить актуальную геометрию синхронно, без ожидания кадра.
        /// Bounds считаются в локальном пространстве relativeTo (владельца капсулы),
        /// а не в мировом: у Player.prefab корень сам по себе не в нуле мира
        /// (сохранённая позиция ассета), и мировые координаты тогда тащат в bounds
        /// этот произвольный оффсет вместо реального размера модели.
        /// </summary>
        private static bool TryMeasureIdleBounds(GameObject root, Transform relativeTo, string idleClipPath, out Bounds bounds)
        {
            bounds = default;

            AnimationClip idle = LoadClip(idleClipPath);
            if (idle == null)
            {
                Debug.LogError($"CharacterPrefabBuilder: не найден клип покоя {idleClipPath} — рост не посчитать.");
                return false;
            }

            idle.SampleAnimation(root, 0f);

            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogError($"CharacterPrefabBuilder: у {root.name} нет SkinnedMeshRenderer — рост не посчитать.");
                return false;
            }

            bool first = true;
            Mesh baked = new Mesh();
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                renderer.BakeMesh(baked, true);
                Vector3[] vertices = baked.vertices;
                Matrix4x4 localToWorld = renderer.transform.localToWorldMatrix;

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 world = localToWorld.MultiplyPoint3x4(vertices[i]);
                    Vector3 local = relativeTo.InverseTransformPoint(world);
                    if (first)
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(local);
                    }
                }
            }

            Object.DestroyImmediate(baked);
            return true;
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

        /// <summary>
        /// Сжатие модели по высоте — заглушка приседания для персонажа без клипа.
        /// Тому, у кого клип есть, её надо снять: иначе присед отыгрывается дважды,
        /// и настоящая анимация приседа ещё и сплющивается сверху.
        /// </summary>
        private static void BindAnimatorDriver(GameObject instance, Animator animator, Transform visualRoot, bool hasCrouchAnimation)
        {
            var serialized = new SerializedObject(instance.GetComponent<CharacterAnimatorDriver>());
            serialized.FindProperty("animator").objectReferenceValue = animator;
            serialized.FindProperty("visualRoot").objectReferenceValue = visualRoot;
            serialized.FindProperty("squashVisualOnCrouch").boolValue = !hasCrouchAnimation;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Сетевая репликация анимаций берёт Animator из своего поля, а не через
        /// GetComponent: визуал базового префаба в варианте удаляется вместе со
        /// своим Animator, и ссылка обнуляется. Без перепривязки персонаж падает
        /// в рантайме на UnassignedReferenceException ещё до первого кадра.
        /// </summary>
        private static void BindNetworkAnimator(string characterName, GameObject instance, Animator animator)
        {
            var networkAnimator = instance.GetComponent<Igruha.Networking.OwnerNetworkAnimator>();
            if (networkAnimator == null)
            {
                Debug.LogError($"CharacterPrefabBuilder ({characterName}): на префабе нет OwnerNetworkAnimator — анимации не будут реплицироваться по сети.");
                return;
            }

            var serialized = new SerializedObject(networkAnimator);
            serialized.FindProperty("m_Animator").objectReferenceValue = animator;
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
    }
}
