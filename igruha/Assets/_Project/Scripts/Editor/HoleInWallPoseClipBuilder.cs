using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Собирает четыре клипа поз «Дырки в стене» — «Свечку», «Титаник», «Казачок»
    /// и «Чайник» — прямо из мышечного пространства Humanoid, без исходных FBX.
    ///
    /// <b>Почему генерируем, а не скачиваем.</b> Поза здесь — это ОДИН КАДР, а не
    /// движение: силуэт игрока обязан точно попадать в вырез на стене, иначе игра
    /// не читается. Мокап дышит и плывёт, а однокадровая поза в мышцах попадает
    /// ровно туда, куда её поставили. Точный прецедент в проекте —
    /// <see cref="SharedCrouchClipBuilder"/>.
    ///
    /// <b>Почему клип на каждого персонажа, а не один на восьмерых.</b> Мышцы у
    /// всех поз общие — таблица ниже одна, и это по-прежнему одна поза на всех.
    /// Персональна только высота тела (<c>RootT.y</c>): она хранится нормированной
    /// по <c>humanScale</c>, а пропорции у ростера разные, и одно общее число
    /// сажает персонажа мимо пола. Замер: «Казачок» требует <c>RootT.y</c> от
    /// 0.516 у Шланги до 0.691 у Босса — общее значение утопило бы одного в полу
    /// на 10 см, а другого подвесило бы на столько же. Правило «не восемь копий»
    /// (STATE.md, раздел 2a) про 90-мегабайтные FBX с моделью внутри; здесь это
    /// тридцать два генерируемых YAML-файла общим весом около трёх мегабайт,
    /// которые git жмёт как текст, а не как бинарь.
    ///
    /// <b>Замороженного не трогаем.</b> Клипы новые, кладутся в отдельную папку,
    /// в аниматоры попадают отдельным слоем — см. <see cref="HoleInWallPoseLayerBuilder"/>.
    /// </summary>
    internal static class HoleInWallPoseClipBuilder
    {
        /// <summary>Папка для генерируемых клипов. Отдельная, чтобы их нельзя было спутать с купленными.</summary>
        private const string OutputFolder = "Assets/_Project/Art/Animations/Poses";

        private const string PlayerPrefabFolder = "Assets/_Project/Prefabs/Player/";

        /// <summary>Имена файлов персонажей. Karlan живёт в Player.prefab — это исторический корень ростера.</summary>
        private static readonly string[] PrefabNames =
        {
            "Player", "Aza", "Boss", "Fat", "Girl", "Milez", "MyBoy", "Shlanga"
        };

        /// <summary>Имена персонажей в том же порядке. Совпадают с <c>CharacterAnimationSet.CharacterName</c>.</summary>
        private static readonly string[] CharacterNames =
        {
            "Karlan", "Aza", "Boss", "Fat", "Girl", "Milez", "MyBoy", "Shlanga"
        };

        /// <summary>Хвост имени клипа по номеру позы. Индекс — поза минус один.</summary>
        private static readonly string[] PoseFileSuffix =
        {
            "Pose1_Candle", "Pose2_Titanic", "Pose3_Cossack", "Pose4_Teapot"
        };

        /// <summary>Русские названия для логов и разговора с геймдизайнером.</summary>
        private static readonly string[] PoseTitles =
        {
            "Свечка", "Титаник", "Казачок", "Чайник"
        };

        /// <summary>Число поз. То же, что <c>HoleInWallConfig.PoseCount</c>, но Core сюда не тянем — это Editor-сборка.</summary>
        internal const int PoseCount = 4;

        /// <summary>
        /// Длина клипа. Поза статична, но клип зациклен и внутри цикла тело еле
        /// заметно подрагивает — иначе персонаж в позе читается манекеном.
        /// </summary>
        private const float LoopSeconds = 1.1f;

        /// <summary>Сколько ключей на цикл дрожи. Пяти хватает, чтобы синус не выродился в пилу.</summary>
        private const int ShimmerKeys = 5;

        /// <summary>
        /// Амплитуда дрожи в мышечных единицах. 0.03 — это доли градуса на позвонке
        /// и около двух сантиметров на кисти: тело «еле держится», но в вырез
        /// по-прежнему влезает с запасом контура 0.22 м.
        /// </summary>
        private const float ShimmerAmplitude = 0.03f;

        /// <summary>
        /// Дрожь всего тела по высоте, в тех же нормированных единицах, что <c>RootT</c>.
        /// Примерно четыре миллиметра — ниже порога, на котором это мешает попаданию.
        /// </summary>
        private const float ShimmerRootAmplitude = 0.004f;

        /// <summary>
        /// Мышца на пределе (|значение| ≈ 1) не дрожит: половину периода её всё равно
        /// срезал бы кламп, и вместо дрожи выходил бы рывок.
        /// </summary>
        private const float ShimmerSkipThreshold = 0.97f;

        /// <summary>Мышцы, которые дрожат, и фаза каждой. Фазы разные, иначе тело качается целиком, как маятник.</summary>
        private static readonly (string Muscle, float Phase)[] ShimmerMuscles =
        {
            ("Spine Left-Right", 0.00f),
            ("Spine Front-Back", 0.37f),
            ("Head Tilt Left-Right", 0.50f),
            ("Left Arm Down-Up", 0.61f),
            ("Right Arm Down-Up", 0.11f),
            ("Left Lower Leg Stretch", 0.83f),
            ("Right Lower Leg Stretch", 0.29f)
        };

        /// <summary>Шаг прореживания вершин при замере габарита. Меш персонажа — около миллиона вершин.</summary>
        private const int MeasureVertexStride = 11;

        /// <summary>Сколько раз уточняется посадка на пол. Сдвиг тела по высоте линеен, так что двух проходов хватает.</summary>
        private const int GroundSolveIterations = 3;

        // ================= ПОЗЫ =================
        //
        // Значения — мышцы Humanoid в диапазоне −1…1. Ноль у Unity это не Т-поза,
        // а расслабленная стойка: руки опущены, локти согнуты примерно на 90°,
        // колени чуть согнуты. Отсюда неочевидные на вид числа: чтобы просто
        // выпрямить руку, нужен «Forearm Stretch» = 1.
        //
        // Пределы у всех восьми аватаров дефолтные (проверено: useDefaultValues
        // у каждой кости), поэтому одни и те же числа дают одну и ту же позу.

        /// <summary>
        /// 1 «Свечка» — узкая и высокая. На носках, руки вверх ладонями вместе,
        /// ноги сведены, тело в струну. Шутка в том, что в эту дырку отчаянно
        /// пытается протиснуться толстяк.
        /// </summary>
        private static float[] Candle() => new PoseBuilder()
            .Sym("Arm Down-Up", 1f)
            .Sym("Shoulder Down-Up", 1f)
            .Sym("Forearm Stretch", 1f)
            .Sym("Arm Front-Back", -0.3f)
            .Sym("Upper Leg Front-Back", 0.45f)
            .Sym("Lower Leg Stretch", 0.75f)
            .Sym("Upper Leg In-Out", -0.15f)
            .Sym("Foot Up-Down", 1f)
            .Set("Spine Front-Back", 0.1f)
            .Set("Head Nod Down-Up", 0.25f)
            .Muscles;

        /// <summary>
        /// 2 «Титаник» — широкая. Руки в стороны, голова запрокинута, грудь вперёд.
        /// Летит. На него едет стена.
        /// </summary>
        private static float[] Titanic() => new PoseBuilder()
            .Sym("Arm Down-Up", 0.64f)
            .Sym("Forearm Stretch", 1f)
            .Sym("Arm Front-Back", 0.2f)
            .Set("Neck Nod Down-Up", 0.7f)
            .Set("Head Nod Down-Up", 0.8f)
            .Set("Spine Front-Back", 0.35f)
            .Set("Chest Front-Back", 0.35f)
            .Sym("Upper Leg In-Out", 0.12f)
            .Sym("Upper Leg Front-Back", 0.4f)
            .Sym("Lower Leg Stretch", 0.6f)
            .Muscles;

        /// <summary>
        /// 3 «Казачок» — низкая и компактная. Вприсядку, колени врозь, руки накрест
        /// на груди. Это НЕ штатный присед на Ctrl: тот сжимает капсулу до 0.8 м и
        /// живёт своей жизнью, а здесь отдельный клип на отдельном слое.
        /// </summary>
        private static float[] Cossack() => new PoseBuilder()
            .Sym("Upper Leg Front-Back", -1f)
            .Sym("Lower Leg Stretch", -1f)
            .Sym("Upper Leg In-Out", 0.85f)
            .Sym("Foot Up-Down", 0.6f)
            .Set("Spine Front-Back", -0.7f)
            .Set("Chest Front-Back", -0.5f)
            .Set("Neck Nod Down-Up", -0.3f)
            .Sym("Arm Down-Up", -0.2f)
            .Sym("Arm Front-Back", -0.9f)
            .Sym("Forearm Stretch", -1f)
            .Sym("Arm Twist In-Out", 0.8f)
            .Muscles;

        /// <summary>
        /// 4 «Чайник» — асимметричная. Правая рука в бок ручкой (локоть наружу,
        /// кисть на поясе — замкнутый треугольник), левая уходит носиком вверх
        /// по диагонали.
        ///
        /// Носик именно диагональю, а не дугой над головой: у ростера мультяшные
        /// пропорции, голова занимает почти четверть роста, и кисть физически
        /// не заводится над макушкой — замер показал, что рука дотягивается ровно
        /// до её верха. Диагональ читается так же однозначно и заодно совпадает
        /// с запасным вариантом геймдизайнера («Траволта»).
        /// </summary>
        private static float[] Teapot() => new PoseBuilder()
            .Set("Left Arm Down-Up", 0.8f)
            .Set("Left Forearm Stretch", 1f)
            .Set("Left Arm Front-Back", -0.15f)
            .Set("Right Arm Down-Up", -0.09f)
            .Set("Right Arm Front-Back", 0.32f)
            .Set("Right Arm Twist In-Out", -0.71f)
            .Set("Right Forearm Stretch", -0.15f)
            .Set("Spine Left-Right", 0.25f)
            .Set("Head Tilt Left-Right", 0.25f)
            .Sym("Upper Leg Front-Back", 0.4f)
            .Sym("Lower Leg Stretch", 0.6f)
            .Muscles;

        private static float[] MusclesOf(int poseIndex)
        {
            switch (poseIndex)
            {
                case 0: return Candle();
                case 1: return Titanic();
                case 2: return Cossack();
                default: return Teapot();
            }
        }

        /// <summary>
        /// Путь клипа позы конкретного персонажа. По нему клип берут билдеры
        /// контроллеров; имя персонажа — то же, что в <c>CharacterAnimationSet</c>.
        /// </summary>
        internal static string ClipPath(string characterName, int poseIndex) =>
            $"{OutputFolder}/{characterName}_{PoseFileSuffix[poseIndex]}.anim";

        /// <summary>
        /// Загрузить четыре клипа поз персонажа, собрав их, если их ещё нет
        /// (свежий клон репозитория). Возвращает null, если собрать не удалось, —
        /// тогда слой поз просто не строится, а остальной контроллер цел.
        /// </summary>
        internal static AnimationClip[] LoadOrBuild(string characterName)
        {
            var clips = new AnimationClip[PoseCount];
            bool complete = true;
            for (int i = 0; i < PoseCount; i++)
            {
                clips[i] = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(characterName, i));
                complete &= clips[i] != null;
            }

            if (complete)
            {
                return clips;
            }

            if (!BuildCharacter(characterName))
            {
                return null;
            }

            for (int i = 0; i < PoseCount; i++)
            {
                clips[i] = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(characterName, i));
                if (clips[i] == null)
                {
                    return null;
                }
            }

            return clips;
        }

        [MenuItem("Igruha/Player/Rebuild Hole In Wall Pose Clips")]
        internal static void BuildAll()
        {
            EnsureFolder();

            var report = new System.Text.StringBuilder();
            report.AppendLine("HoleInWallPoseClipBuilder: габариты поз, м (высота × ширина, x от…до)");

            int built = 0;
            for (int c = 0; c < CharacterNames.Length; c++)
            {
                if (BuildOne(PrefabNames[c], CharacterNames[c], report))
                {
                    built++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"HoleInWallPoseClipBuilder: собрано персонажей — {built} из {CharacterNames.Length}, клипов — {built * PoseCount}.\n{report}");
        }

        /// <summary>Собрать клипы одного персонажа. Возвращает false, если префаб не найден или не Humanoid.</summary>
        private static bool BuildCharacter(string characterName)
        {
            int index = System.Array.IndexOf(CharacterNames, characterName);
            if (index < 0)
            {
                Debug.LogError($"HoleInWallPoseClipBuilder: персонаж «{characterName}» не в ростере — клипы поз собрать не из чего.");
                return false;
            }

            EnsureFolder();
            var report = new System.Text.StringBuilder();
            bool ok = BuildOne(PrefabNames[index], characterName, report);
            if (ok)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"HoleInWallPoseClipBuilder ({characterName}): клипы поз собраны.\n{report}");
            }

            return ok;
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Art/Animations", "Poses");
            }
        }

        private static bool BuildOne(string prefabName, string characterName, System.Text.StringBuilder report)
        {
            string prefabPath = PlayerPrefabFolder + prefabName + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"HoleInWallPoseClipBuilder: нет префаба {prefabPath}.");
                return false;
            }

            // Персонажа поднимаем в превью-сцену: он нужен живым, чтобы посчитать
            // мышцы через HumanPoseHandler и обмерить кожу, но открытую сцену
            // геймдизайнера при этом трогать нельзя.
            UnityEngine.SceneManagement.Scene preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);

            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                var animator = instance.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.avatar == null || !animator.isHuman)
                {
                    Debug.LogError($"HoleInWallPoseClipBuilder ({characterName}): в префабе нет Humanoid-аватара.");
                    return false;
                }

                var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (skin == null || skin.sharedMesh == null)
                {
                    Debug.LogError($"HoleInWallPoseClipBuilder ({characterName}): в префабе нет скиннед-меша — обмерить позу нечем.");
                    return false;
                }

                var measurer = new PoseMeasurer(animator, skin);
                for (int p = 0; p < PoseCount; p++)
                {
                    float[] muscles = MusclesOf(p);
                    PoseBounds bounds = measurer.PlaceOnGround(muscles);
                    WriteClip(ClipPath(characterName, p), $"{characterName}_{PoseFileSuffix[p]}", muscles, bounds.RootHeight);

                    report.AppendLine(
                        $"  {characterName,-8} {PoseTitles[p],-9} " +
                        $"H={bounds.Height:F3} W={bounds.Width:F3} " +
                        $"x[{bounds.MinX:F2}…{bounds.MaxX:F2}] RootT.y={bounds.RootHeight:F4}");
                }

                return true;
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        /// <summary>
        /// Записать клип позы: все 95 мышц, четыре компоненты поворота тела и три
        /// его смещения.
        ///
        /// Пишем ВСЕ мышцы, а не только ненулевые. Слой поз перекрывает основной,
        /// и мышца, для которой в клипе нет кривой, осталась бы от бега или удара:
        /// поза приезжала бы разной в зависимости от того, что игрок делал секунду
        /// назад.
        /// </summary>
        private static void WriteClip(string path, string clipName, float[] muscles, float rootHeight)
        {
            var clip = new AnimationClip { name = clipName };

            string[] names = HumanTrait.MuscleName;
            for (int i = 0; i < muscles.Length; i++)
            {
                string binding = MuscleBinding(names[i]);
                float phase;
                bool shimmers = ShimmerPhase(names[i], muscles[i], out phase);
                SetCurve(clip, binding, muscles[i], shimmers ? ShimmerAmplitude : 0f, phase);
            }

            // Тело стоит ровно по центру капсулы и смотрит вперёд: разворачивать
            // персонажа — дело PlayerController, а не клипа.
            SetCurve(clip, "RootT.x", 0f, 0f, 0f);
            SetCurve(clip, "RootT.z", 0f, 0f, 0f);
            SetCurve(clip, "RootQ.x", 0f, 0f, 0f);
            SetCurve(clip, "RootQ.y", 0f, 0f, 0f);
            SetCurve(clip, "RootQ.z", 0f, 0f, 0f);
            SetCurve(clip, "RootQ.w", 1f, 0f, 0f);
            SetCurve(clip, "RootT.y", rootHeight, ShimmerRootAmplitude, 0.17f);

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0f;
            settings.stopTime = LoopSeconds;
            settings.loopTime = true;
            settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(clip, path);
            }
            else
            {
                // Перезаписываем содержимое, а не создаём новый ассет: на клип уже
                // ссылается слой поз в контроллере, и новый GUID порвал бы ссылку.
                EditorUtility.CopySerialized(clip, existing);
                Object.DestroyImmediate(clip);
            }
        }

        /// <summary>
        /// Дрожит ли эта мышца и с какой фазой. Мышца, выкрученная до предела,
        /// не дрожит — её колебание всё равно срезал бы кламп.
        /// </summary>
        private static bool ShimmerPhase(string muscleName, float value, out float phase)
        {
            phase = 0f;
            if (Mathf.Abs(value) > ShimmerSkipThreshold)
            {
                return false;
            }

            for (int i = 0; i < ShimmerMuscles.Length; i++)
            {
                if (ShimmerMuscles[i].Muscle == muscleName)
                {
                    phase = ShimmerMuscles[i].Phase;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Положить кривую: либо две одинаковые точки на концах цикла (поза стоит),
        /// либо синус малой амплитуды (поза еле заметно дрожит).
        /// </summary>
        private static void SetCurve(AnimationClip clip, string binding, float value, float amplitude, float phase)
        {
            AnimationCurve curve;
            if (amplitude <= 0f)
            {
                // Один ключ, а не два по краям цикла: кривая от этого не меняется,
                // а файл клипа худеет вдвое — мышц в Humanoid девяносто пять, и
                // каждый лишний ключ множится на них и на восемь персонажей.
                // Длину клипу задают кривые дрожи, они есть в каждой позе.
                curve = new AnimationCurve(new Keyframe(0f, value));
            }
            else
            {
                curve = new AnimationCurve();
                for (int i = 0; i < ShimmerKeys; i++)
                {
                    float t = LoopSeconds * i / (ShimmerKeys - 1);
                    float wave = Mathf.Sin((t / LoopSeconds + phase) * 2f * Mathf.PI);

                    // Кламп −1…1 здесь НЕЛЬЗЯ, хотя мышцы живут именно в этом
                    // диапазоне: через тот же метод пишется RootT.y — высота тела
                    // в долях роста, и она законно больше единицы у всех поз,
                    // кроме «Казачка». Кламп срезал её до 1.0 и втапливал
                    // персонажа в пол на 13 см. Мышцы ограничены раньше,
                    // в PoseBuilder.Set, а дрожь в диапазон укладывает
                    // ShimmerSkipThreshold.
                    curve.AddKey(t, value + amplitude * wave);
                }

                for (int i = 0; i < curve.length; i++)
                {
                    curve.SmoothTangents(i, 0f);
                }
            }

            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), binding), curve);
        }

        /// <summary>
        /// Имя мышцы → имя кривой в клипе. Для тела они совпадают, а пальцы
        /// названы по-разному: «Left Index 2 Stretched» в таблице мышц лежит
        /// в клипе как «LeftHand.Index.2 Stretched».
        /// </summary>
        private static string MuscleBinding(string muscleName)
        {
            string side;
            if (muscleName.StartsWith("Left "))
            {
                side = "Left";
            }
            else if (muscleName.StartsWith("Right "))
            {
                side = "Right";
            }
            else
            {
                return muscleName;
            }

            string[] parts = muscleName.Substring(side.Length + 1).Split(' ');
            if (parts.Length < 2 || System.Array.IndexOf(FingerNames, parts[0]) < 0)
            {
                return muscleName;
            }

            // «Thumb Spread» → «LeftHand.Thumb.Spread»; «Thumb 2 Stretched» → «LeftHand.Thumb.2 Stretched».
            return parts[1] == "Spread"
                ? $"{side}Hand.{parts[0]}.Spread"
                : $"{side}Hand.{parts[0]}.{parts[1]} Stretched";
        }

        private static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };

        /// <summary>Габарит позы, посчитанный по коже конкретного персонажа.</summary>
        private readonly struct PoseBounds
        {
            public readonly float RootHeight;
            public readonly float Height;
            public readonly float MinX;
            public readonly float MaxX;

            public PoseBounds(float rootHeight, float height, float minX, float maxX)
            {
                RootHeight = rootHeight;
                Height = height;
                MinX = minX;
                MaxX = maxX;
            }

            public float Width => MaxX - MinX;
        }

        /// <summary>
        /// Ставит позу на конкретного персонажа, сажает её на пол и обмеряет.
        ///
        /// Меряем по КОЖЕ, а не по костям: в вырез лезет модель, а не скелет,
        /// и у широких персонажей мясо выходит за кости на десятки сантиметров.
        /// Скиннинг считаем сами — <c>BakeMesh</c> внутри одного кадра редактора
        /// отдаёт кэш предыдущей позы и врёт.
        /// </summary>
        private sealed class PoseMeasurer
        {
            private readonly Vector3[] vertices;
            private readonly BoneWeight[] weights;
            private readonly Matrix4x4[] bindPoses;
            private readonly Transform[] bones;
            private readonly Matrix4x4[] boneMatrices;
            private readonly HumanPoseHandler handler;

            /// <summary>Метры мира на единицу <c>RootT</c>: тело хранится нормированным по росту аватара.</summary>
            private readonly float rootToWorld;

            public PoseMeasurer(Animator animator, SkinnedMeshRenderer skin)
            {
                Mesh mesh = skin.sharedMesh;
                vertices = mesh.vertices;
                weights = mesh.boneWeights;
                bindPoses = mesh.bindposes;
                bones = skin.bones;
                boneMatrices = new Matrix4x4[bones.Length];
                handler = new HumanPoseHandler(animator.avatar, animator.transform);
                rootToWorld = animator.humanScale * animator.transform.localScale.y;
            }

            public PoseBounds PlaceOnGround(float[] muscles)
            {
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                pose.bodyRotation = Quaternion.identity;
                pose.bodyPosition = new Vector3(0f, 0.95f, 0f);
                System.Array.Copy(muscles, pose.muscles, muscles.Length);

                float minY = 0f, maxY = 0f, minX = 0f, maxX = 0f;
                for (int i = 0; i < GroundSolveIterations; i++)
                {
                    handler.SetHumanPose(ref pose);
                    Measure(out minY, out maxY, out minX, out maxX);
                    if (i < GroundSolveIterations - 1)
                    {
                        pose.bodyPosition = new Vector3(0f, pose.bodyPosition.y - minY / rootToWorld, 0f);
                    }
                }

                return new PoseBounds(pose.bodyPosition.y, maxY, minX, maxX);
            }

            private void Measure(out float minY, out float maxY, out float minX, out float maxX)
            {
                for (int b = 0; b < bones.Length; b++)
                {
                    boneMatrices[b] = bones[b].localToWorldMatrix * bindPoses[b];
                }

                minY = float.MaxValue;
                maxY = float.MinValue;
                minX = float.MaxValue;
                maxX = float.MinValue;

                for (int v = 0; v < vertices.Length; v += MeasureVertexStride)
                {
                    BoneWeight w = weights[v];
                    Vector3 local = vertices[v];
                    Vector3 world = boneMatrices[w.boneIndex0].MultiplyPoint3x4(local) * w.weight0;
                    if (w.weight1 > 0f)
                    {
                        world += boneMatrices[w.boneIndex1].MultiplyPoint3x4(local) * w.weight1;
                    }

                    if (w.weight2 > 0f)
                    {
                        world += boneMatrices[w.boneIndex2].MultiplyPoint3x4(local) * w.weight2;
                    }

                    if (w.weight3 > 0f)
                    {
                        world += boneMatrices[w.boneIndex3].MultiplyPoint3x4(local) * w.weight3;
                    }

                    if (world.y < minY) minY = world.y;
                    if (world.y > maxY) maxY = world.y;
                    if (world.x < minX) minX = world.x;
                    if (world.x > maxX) maxX = world.x;
                }
            }
        }

        /// <summary>
        /// Набирает мышцы позы по именам. Sym ставит одно значение обеим сторонам:
        /// мышцы Humanoid зеркальны по определению, поэтому симметричная поза —
        /// это буквально одинаковые числа слева и справа.
        /// </summary>
        private sealed class PoseBuilder
        {
            private readonly float[] muscles = new float[HumanTrait.MuscleCount];
            private static readonly Dictionary<string, int> Index = BuildIndex();

            public float[] Muscles => muscles;

            public PoseBuilder Set(string muscleName, float value)
            {
                int index;
                if (!Index.TryGetValue(muscleName, out index))
                {
                    Debug.LogError($"HoleInWallPoseClipBuilder: мышцы «{muscleName}» нет в Humanoid — поза собрана не полностью.");
                    return this;
                }

                muscles[index] = Mathf.Clamp(value, -1f, 1f);
                return this;
            }

            public PoseBuilder Sym(string muscleSuffix, float value) =>
                Set("Left " + muscleSuffix, value).Set("Right " + muscleSuffix, value);

            private static Dictionary<string, int> BuildIndex()
            {
                string[] names = HumanTrait.MuscleName;
                var map = new Dictionary<string, int>(names.Length);
                for (int i = 0; i < names.Length; i++)
                {
                    map[names[i]] = i;
                }

                return map;
            }
        }
    }
}
