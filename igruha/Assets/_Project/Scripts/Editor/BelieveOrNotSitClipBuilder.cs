using System.Text;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Собирает восемь клипов сидячей позы «Верю / не верю» — по одному на
    /// персонажа — прямо из мышечного пространства Humanoid, без исходных FBX.
    ///
    /// <b>Почему генерируем, а не скачиваем.</b> Поза — это ОДИН КАДР, и он
    /// обязан сесть на конкретный стул: таз на сиденье, ступни на полу.
    /// Мокап «сидит» на своей мебели и своих пропорциях, и подгонять его под
    /// наш стул дороже, чем поставить позу числами. Тот же вывод сделан на
    /// четырёх позах «Дырки в стене» (STATE.md, 3.53).
    ///
    /// <b>Почему клип на каждого персонажа, а не один на восьмерых.</b> Мышцы
    /// общие — таблица ниже одна. Персональна только высота тела
    /// (<c>RootT.y</c>): она хранится нормированной по <c>humanScale</c>,
    /// а пропорции у ростера разные, и одно общее число сажает кого-то мимо
    /// пола. Файлы генерируемые, текстовые, по три десятка килобайт — правило
    /// «не восемь копий» из STATE 2a про 90-мегабайтные FBX с моделью внутри.
    ///
    /// <b>Замороженного не трогаем.</b> Клипы новые, лежат отдельной папкой,
    /// в аниматоры попадают отдельным слоем — см.
    /// <see cref="BelieveOrNotSitLayerBuilder"/>.
    ///
    /// <b>Про повтор кода.</b> Механика замера повторяет
    /// <see cref="HoleInWallPoseClipBuilder"/>, и это осознанно: тот файл сейчас
    /// на приёмке у геймдизайнера вместе с формами вырезов, выверенными по этим
    /// самым обмерам, и вынимать из него общий кит — значит пересчитывать их
    /// заново ради чужой задачи. Кит выносится отдельной задачей, когда позы
    /// «Дырки» будут приняты.
    /// </summary>
    internal static class BelieveOrNotSitClipBuilder
    {
        /// <summary>Папка генерируемых клипов — общая с позами «Дырки», имена не пересекаются.</summary>
        private const string OutputFolder = "Assets/_Project/Art/Animations/Poses";

        private const string PlayerPrefabFolder = "Assets/_Project/Prefabs/Player/";

        /// <summary>Имена файлов персонажей. Karlan живёт в Player.prefab — исторический корень ростера.</summary>
        private static readonly string[] PrefabNames =
        {
            "Player", "Aza", "Boss", "Fat", "Girl", "Milez", "MyBoy", "Shlanga"
        };

        /// <summary>Имена персонажей в том же порядке.</summary>
        internal static readonly string[] CharacterNames =
        {
            "Karlan", "Aza", "Boss", "Fat", "Girl", "Milez", "MyBoy", "Shlanga"
        };

        /// <summary>
        /// Длина клипа. Поза статична, но клип зациклен и внутри цикла тело еле
        /// заметно дышит: сорок секунд уговоров с неподвижным манекеном в кадре
        /// смотреть невозможно, а камера всё это время висит у лица.
        /// </summary>
        private const float LoopSeconds = 2.4f;

        /// <summary>Ключей на цикл дыхания. Пяти хватает, чтобы синус не выродился в пилу.</summary>
        private const int BreathKeys = 5;

        /// <summary>
        /// Амплитуда дыхания в мышечных единицах: доли градуса на позвонке,
        /// около сантиметра на плече. Сидящего это оживляет, позы не ломает.
        /// </summary>
        private const float BreathAmplitude = 0.035f;

        /// <summary>Мышцы, которые дышат, и фаза каждой — иначе тело качается целиком, как маятник.</summary>
        private static readonly (string Muscle, float Phase)[] BreathMuscles =
        {
            ("Chest Front-Back", 0.00f),
            ("Spine Front-Back", 0.21f),
            ("Head Nod Down-Up", 0.55f),
            ("Head Tilt Left-Right", 0.72f),
            ("Left Shoulder Down-Up", 0.34f),
            ("Right Shoulder Down-Up", 0.84f)
        };

        /// <summary>Мышца на пределе не дышит: половину периода её всё равно срезал бы кламп.</summary>
        private const float BreathSkipThreshold = 0.97f;

        /// <summary>Шаг прореживания вершин при замере. Меш персонажа — около миллиона вершин.</summary>
        private const int MeasureVertexStride = 11;

        /// <summary>Сколько раз уточняется посадка на пол. Сдвиг тела по высоте линеен.</summary>
        private const int GroundSolveIterations = 3;

        // ================= ПОЗА =================
        //
        // Значения — мышцы Humanoid в диапазоне −1…1. Ноль у Unity это не
        // Т-поза, а расслабленная стойка: руки опущены, локти согнуты примерно
        // на 90°, колени чуть согнуты.

        /// <summary>
        /// Сидит за столом: бёдра горизонтально, голени вниз, ступни на полу,
        /// корпус чуть вперёд, предплечья на столешнице.
        ///
        /// Руки подняты перед грудью, а не положены на стол. Замер показал, что
        /// на стол их не положить в принципе: сидящий стоит в 0.67 м от кромки
        /// сукна (так его отодвинули в фазе 2, чтобы стол не выталкивал), а
        /// кисть у ростера достаёт вперёд самое большее на 0.41 м. Руки перед
        /// грудью — это ещё и поза уговоров: сорок секунд человек ими говорит.
        /// Наклон корпуса небольшой — камера смотрит в лицо, и сгорбленный
        /// силуэт читается как «отвернулся».
        ///
        /// <b>Числа ног подобраны замером, а не на глаз.</b> Перебор по сетке
        /// «бедро × голень» на живом аватаре показал, что у ростера короткие
        /// ноги при большой голове: таз садится не выше 0.39 м над полом, а
        /// сильный сгиб голени (−0.55) уводит ступни ПОД таз, и поза читается
        /// не сидением, а присядкой в воздухе. Взята пара −0.50 / 0.00: таз
        /// около 0.39 м, ступни впереди колен, голень почти отвесная.
        /// Отсюда же масштаб стула — сиденье пака 0.45 м пришлось бы
        /// персонажу выше таза.
        /// </summary>
        private static float[] Sitting() => new PoseBuilder()
            .Sym("Upper Leg Front-Back", -0.50f)
            .Sym("Lower Leg Stretch", 0.00f)
            .Sym("Upper Leg In-Out", 0.22f)
            .Sym("Foot Up-Down", 0.25f)
            .Set("Spine Front-Back", 0.18f)
            .Set("Chest Front-Back", 0.12f)
            .Set("Neck Nod Down-Up", -0.1f)
            .Sym("Arm Down-Up", -0.12f)
            .Sym("Arm Front-Back", -0.35f)
            .Sym("Arm Twist In-Out", 0.15f)
            .Sym("Forearm Stretch", 0.25f)
            .Sym("Hand Down-Up", -0.1f)
            .Muscles;

        /// <summary>Путь клипа сидячей позы конкретного персонажа.</summary>
        internal static string ClipPath(string characterName) => $"{OutputFolder}/{characterName}_Sit.anim";

        /// <summary>
        /// Загрузить клип персонажа, собрав весь ростер, если клипа ещё нет
        /// (свежий клон репозитория). Возвращает null, если собрать не удалось, —
        /// тогда слой просто не строится, а остальной контроллер цел.
        /// </summary>
        internal static AnimationClip LoadOrBuild(string characterName)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(characterName));
            if (clip != null)
            {
                return clip;
            }

            BuildAll();
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(characterName));
        }

        [MenuItem("Igruha/Player/Rebuild Believe Or Not Sit Clip")]
        internal static void BuildAll()
        {
            EnsureFolder();

            var report = new StringBuilder();
            report.AppendLine("BelieveOrNotSitClipBuilder: посадка, м");

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
            Debug.Log($"BelieveOrNotSitClipBuilder: собрано клипов — {built} из {CharacterNames.Length}.\n{report}");
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Art/Animations", "Poses");
            }
        }

        private static bool BuildOne(string prefabName, string characterName, StringBuilder report)
        {
            string prefabPath = PlayerPrefabFolder + prefabName + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"BelieveOrNotSitClipBuilder: нет префаба {prefabPath}.");
                return false;
            }

            // Персонажа поднимаем в превью-сцену: он нужен живым, чтобы считать
            // мышцы через HumanPoseHandler и обмерить кожу, а открытую сцену
            // геймдизайнера трогать нельзя.
            UnityEngine.SceneManagement.Scene preview =
                UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);

            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                var animator = instance.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.avatar == null || !animator.isHuman)
                {
                    Debug.LogError($"BelieveOrNotSitClipBuilder ({characterName}): в префабе нет Humanoid-аватара.");
                    return false;
                }

                var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (skin == null || skin.sharedMesh == null)
                {
                    Debug.LogError($"BelieveOrNotSitClipBuilder ({characterName}): нет скиннед-меша — обмерить нечем.");
                    return false;
                }

                var measurer = new SitMeasurer(animator, skin);
                float[] muscles = Sitting();
                SitBounds bounds = measurer.PlaceOnGround(muscles);
                WriteClip(ClipPath(characterName), $"{characterName}_Sit", muscles, bounds.RootHeight);

                report.AppendLine(
                    $"  {characterName,-8} таз={bounds.HipHeight:F3} колени={bounds.KneeHeight:F3} " +
                    $"ступни вперёд={bounds.FootForward:F3} высота={bounds.Height:F3} RootT.y={bounds.RootHeight:F4}");
                return true;
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        /// <summary>
        /// Записать клип: все 95 мышц, поворот тела и его смещение.
        ///
        /// Пишем ВСЕ мышцы, а не только ненулевые. Слой сидения перекрывает
        /// основной, и мышца без кривой осталась бы от бега или удара: поза
        /// приезжала бы разной в зависимости от того, что игрок делал секунду
        /// назад.
        /// </summary>
        private static void WriteClip(string path, string clipName, float[] muscles, float rootHeight)
        {
            var clip = new AnimationClip { name = clipName };

            string[] names = HumanTrait.MuscleName;
            for (int i = 0; i < muscles.Length; i++)
            {
                float phase;
                bool breathes = BreathPhase(names[i], muscles[i], out phase);
                SetCurve(clip, MuscleBinding(names[i]), muscles[i], breathes ? BreathAmplitude : 0f, phase);
            }

            // Тело стоит по центру капсулы и смотрит вперёд: разворачивать
            // персонажа — дело PlayerController, а не клипа.
            SetCurve(clip, "RootT.x", 0f, 0f, 0f);
            SetCurve(clip, "RootT.z", 0f, 0f, 0f);
            SetCurve(clip, "RootQ.x", 0f, 0f, 0f);
            SetCurve(clip, "RootQ.y", 0f, 0f, 0f);
            SetCurve(clip, "RootQ.z", 0f, 0f, 0f);
            SetCurve(clip, "RootQ.w", 1f, 0f, 0f);
            SetCurve(clip, "RootT.y", rootHeight, 0f, 0f);

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
                // Перезаписываем содержимое, а не создаём новый ассет: на клип
                // уже ссылается слой в контроллере, и новый GUID порвал бы ссылку.
                EditorUtility.CopySerialized(clip, existing);
                Object.DestroyImmediate(clip);
            }
        }

        private static bool BreathPhase(string muscleName, float value, out float phase)
        {
            phase = 0f;
            if (Mathf.Abs(value) > BreathSkipThreshold)
            {
                return false;
            }

            for (int i = 0; i < BreathMuscles.Length; i++)
            {
                if (BreathMuscles[i].Muscle == muscleName)
                {
                    phase = BreathMuscles[i].Phase;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Положить кривую: либо один ключ (поза стоит), либо синус малой
        /// амплитуды (поза дышит). Один ключ вместо двух по краям цикла кривую
        /// не меняет, а файл клипа худеет вдвое: мышц девяносто пять, и каждый
        /// лишний ключ множится на них и на восемь персонажей.
        /// </summary>
        private static void SetCurve(AnimationClip clip, string binding, float value, float amplitude, float phase)
        {
            AnimationCurve curve;
            if (amplitude <= 0f)
            {
                curve = AnimationCurve.Constant(0f, LoopSeconds, value);
                curve.keys = new[] { new Keyframe(0f, value) };
            }
            else
            {
                var keys = new Keyframe[BreathKeys];
                for (int i = 0; i < BreathKeys; i++)
                {
                    float t = i / (float)(BreathKeys - 1);
                    float offset = amplitude * Mathf.Sin((t + phase) * Mathf.PI * 2f);
                    keys[i] = new Keyframe(t * LoopSeconds, Mathf.Clamp(value + offset, -1f, 1f));
                }

                curve = new AnimationCurve(keys);
                for (int i = 0; i < BreathKeys; i++)
                {
                    curve.SmoothTangents(i, 0f);
                }
            }

            clip.SetCurve(string.Empty, typeof(Animator), binding, curve);
        }

        /// <summary>
        /// Имя мышцы в имя кривой. У пальцев оно другое:
        /// «Left Thumb Spread» → «LeftHand.Thumb.Spread».
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

            return parts[1] == "Spread"
                ? $"{side}Hand.{parts[0]}.Spread"
                : $"{side}Hand.{parts[0]}.{parts[1]} Stretched";
        }

        private static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };

        /// <summary>Сборщик таблицы мышц по именам: без него поза — массив из 95 чисел.</summary>
        private sealed class PoseBuilder
        {
            public readonly float[] Muscles = new float[HumanTrait.MuscleCount];

            /// <summary>Одна мышца по имени.</summary>
            public PoseBuilder Set(string muscle, float value)
            {
                int index = System.Array.IndexOf(HumanTrait.MuscleName, muscle);
                if (index < 0)
                {
                    Debug.LogError($"BelieveOrNotSitClipBuilder: мышцы «{muscle}» нет в Humanoid.");
                    return this;
                }

                Muscles[index] = value;
                return this;
            }

            /// <summary>Симметрично левой и правой стороне.</summary>
            public PoseBuilder Sym(string muscle, float value)
            {
                Set("Left " + muscle, value);
                Set("Right " + muscle, value);
                return this;
            }
        }

        /// <summary>Обмеры сидящей позы конкретного персонажа.</summary>
        private readonly struct SitBounds
        {
            public readonly float RootHeight;
            public readonly float Height;
            public readonly float HipHeight;
            public readonly float KneeHeight;
            public readonly float FootForward;

            public SitBounds(float rootHeight, float height, float hipHeight, float kneeHeight, float footForward)
            {
                RootHeight = rootHeight;
                Height = height;
                HipHeight = hipHeight;
                KneeHeight = kneeHeight;
                FootForward = footForward;
            }
        }

        /// <summary>
        /// Ставит позу на персонажа, сажает её на пол и обмеряет.
        ///
        /// Меряем по КОЖЕ, а не по костям: на стуле сидит модель, а не скелет.
        /// Скиннинг считаем сами — <c>BakeMesh</c> внутри одного кадра редактора
        /// отдаёт кэш предыдущей позы и врёт.
        /// </summary>
        private sealed class SitMeasurer
        {
            private readonly Vector3[] vertices;
            private readonly BoneWeight[] weights;
            private readonly Matrix4x4[] bindPoses;
            private readonly Transform[] bones;
            private readonly Matrix4x4[] boneMatrices;
            private readonly HumanPoseHandler handler;
            private readonly Animator animator;

            /// <summary>Метры мира на единицу <c>RootT</c>: тело хранится нормированным по росту аватара.</summary>
            private readonly float rootToWorld;

            public SitMeasurer(Animator animator, SkinnedMeshRenderer skin)
            {
                this.animator = animator;
                Mesh mesh = skin.sharedMesh;
                vertices = mesh.vertices;
                weights = mesh.boneWeights;
                bindPoses = mesh.bindposes;
                bones = skin.bones;
                boneMatrices = new Matrix4x4[bones.Length];
                handler = new HumanPoseHandler(animator.avatar, animator.transform);
                rootToWorld = animator.humanScale * animator.transform.localScale.y;
            }

            public SitBounds PlaceOnGround(float[] muscles)
            {
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                pose.bodyRotation = Quaternion.identity;
                pose.bodyPosition = new Vector3(0f, 0.95f, 0f);
                System.Array.Copy(muscles, pose.muscles, muscles.Length);

                float minY = 0f, maxY = 0f;
                for (int i = 0; i < GroundSolveIterations; i++)
                {
                    handler.SetHumanPose(ref pose);
                    Measure(out minY, out maxY);
                    if (i < GroundSolveIterations - 1)
                    {
                        pose.bodyPosition = new Vector3(0f, pose.bodyPosition.y - minY / rootToWorld, 0f);
                    }
                }

                float hips = BoneHeight(HumanBodyBones.Hips);
                float knee = BoneHeight(HumanBodyBones.LeftLowerLeg);
                float foot = BoneForward(HumanBodyBones.LeftFoot);
                return new SitBounds(pose.bodyPosition.y, maxY, hips, knee, foot);
            }

            private float BoneHeight(HumanBodyBones bone)
            {
                Transform t = animator.GetBoneTransform(bone);
                return t == null ? 0f : t.position.y;
            }

            private float BoneForward(HumanBodyBones bone)
            {
                Transform t = animator.GetBoneTransform(bone);
                return t == null ? 0f : t.position.z;
            }

            private void Measure(out float minY, out float maxY)
            {
                for (int b = 0; b < bones.Length; b++)
                {
                    boneMatrices[b] = bones[b].localToWorldMatrix * bindPoses[b];
                }

                minY = float.MaxValue;
                maxY = float.MinValue;
                for (int v = 0; v < vertices.Length; v += MeasureVertexStride)
                {
                    float y = Skin(v).y;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            /// <summary>Одна вершина, продавленная костями в мир.</summary>
            private Vector3 Skin(int vertex)
            {
                BoneWeight w = weights[vertex];
                Vector3 local = vertices[vertex];
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

                return world;
            }
        }
    }
}
