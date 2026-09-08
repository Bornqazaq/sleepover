using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Igruha.Minigames.HoleInWall;
using Tone = Igruha.EditorTools.HoleInWallPaletteAssets.Tone;
using static Igruha.EditorTools.HoleInWallProps;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Публика «Дырки в стене»: рассадка зала по трибунам, одежда, реквизит
    /// болельщика и привязка оживления.
    ///
    /// <b>Переделана 08.09 по замечанию геймдизайнера «зрители просто стоят
    /// тупо, как истуканы».</b> До этого зал был сорока стальными фигурами
    /// в один ряд на шести скамьях, неподвижными весь раунд. Изменено три
    /// вещи, и каждая отвечает за свою половину замечания:
    ///
    /// <list type="bullet">
    /// <item><b>Их стало много.</b> Публика садится на <i>каждую</i> секцию
    /// трибуны, которую поставил <see cref="HoleInWallStands"/>, — а их теперь
    /// три пояса вместо одного.</item>
    /// <item><b>Они разные.</b> Восемь персонажей пака в двух позах, пять
    /// матовых тонов одежды, разброс роста и разворота — вместо одного
    /// стального силуэта, повторённого сорок раз.</item>
    /// <item><b>Они живые.</b> Группа получает <see cref="HoleInWallCrowd"/>:
    /// зал качается на бите, прокатывает волну, замирает перед ударом стены
    /// и взрывается после вердикта.</item>
    /// </list>
    ///
    /// <b>Реквизит болельщика вложен в самого зрителя, а не лежит рядом.</b>
    /// Огонёк телефона и флажок — дети фигуры, поэтому едут вместе с ней при
    /// каждом подскоке. Первым прогоном 4.3 телефоны стояли отдельными
    /// кубами в группе зала: стоило залу зашевелиться, и сорок огоньков
    /// остались бы висеть в воздухе.
    /// </summary>
    /// <remarks>
    /// <b>Меши, а не персонажи.</b> Зал набран из мешей, снятых
    /// <c>BakeMesh</c> в двух позах: скиннинг четырёхсот персонажей стоил бы
    /// дороже всей остальной сцены, а <c>MeshRenderer</c> с подменой меша
    /// даёт то же самое почти даром. Разбор запекания — в
    /// <see cref="LoadCrowdMeshes"/>.
    ///
    /// <b>Правила павильона держит <c>Strip</c> декора</b> — коллайдеры,
    /// слой и отброс теней снимаются одним проходом в конце. Статическую
    /// пакетную отрисовку зал при этом <b>не</b> получает, и это существенно:
    /// помеченный <c>BatchingStatic</c> зритель вмерзает в общий меш сцены
    /// и перестаёт шевелиться, чего бы ему ни велел
    /// <see cref="HoleInWallCrowd"/>.
    /// </remarks>
    internal static class HoleInWallCrowdBuilder
    {
        private const string CrowdGroup = "Crowd";

        /// <summary>Приставка секции трибуны: по ней ищутся места для публики.</summary>
        private const string BleacherPrefix = "Bleacher_";

        /// <summary>Приставка секции дальнего амфитеатра: он сидит плотнее боковых поясов.</summary>
        private const string AmphiPrefix = "Bleacher_A";

        private const string PackModels = "Assets/Synty/PolygonNightclubs/Models/";
        private const string BakedModels = "Assets/_Project/Art/HoleInWall/PolygonNightclubs/Models/";

        /// <summary>Восемь персонажей одним FBX — из них набирается зал.</summary>
        private const string CrowdModel = "Characters.fbx";

        /// <summary>Путь к запечённым в позе мешам зала.</summary>
        private const string CrowdMeshPath = "Assets/_Project/Art/HoleInWall/HIW_Crowd.asset";

        // ========== РАССАДКА ==========
        //
        // Плотность у поясов разная, и это счёт, а не вкус: дальний амфитеатр
        // стоит прямо в кадре и виден всю игру, боковые пояса — краем глаза
        // при обороте камеры. Лишняя сотня фигур в амфитеатре видна, лишняя
        // сотня по бокам стоит столько же, а не видна почти никогда.

        /// <summary>Рядов и мест в ряду на секции дальнего амфитеатра.</summary>
        private const int AmphiRows = 2;
        private const int AmphiSeats = 3;

        /// <summary>Рядов и мест в ряду на секции бокового пояса.</summary>
        private const int BeltRows = 1;
        private const int BeltSeats = 4;

        /// <summary>
        /// Где стоит задний ряд по высоте секции: доля от её собственной
        /// высоты. Единица — верхняя ступень, ноль — пол под трибуной.
        /// </summary>
        private const float BackRowLevel = 1f;

        /// <summary>Где стоит передний ряд: на средней ступени, ближе к арене.</summary>
        private const float FrontRowLevel = 0.58f;

        /// <summary>Насколько передний ряд вынесен к арене от оси секции, м.</summary>
        private const float FrontRowReach = 0.5f;

        /// <summary>Разброс роста зрителя множителем к модели пака.</summary>
        private const float FanScaleMin = 0.9f;
        private const float FanScaleMax = 1.08f;

        /// <summary>Разброс разворота зрителя от «лицом к арене», градусов.</summary>
        private const float FanYawJitter = 16f;

        // ========== РЕКВИЗИТ БОЛЕЛЬЩИКА ==========

        /// <summary>Доля зрителей, подсвеченных «телефоном». Живой зал снимает на телефоны.</summary>
        private const float PhoneShare = 0.26f;

        /// <summary>Доля зрителей с флажком. Меньше, чем с телефонами: флажок крупнее и виден дальше.</summary>
        private const float FlagShare = 0.14f;

        /// <summary>Размер огонька телефона, м.</summary>
        private const float PhoneSize = 0.11f;

        /// <summary>На сколько огонёк вынесен вперёд и поднят над сиденьем, м.</summary>
        private const float PhoneReach = 0.3f;
        private const float PhoneHeight = 1.5f;

        /// <summary>Флажок болельщика: размер полотнища и высота поднятой руки, м.</summary>
        private const float FlagWidth = 0.55f;
        private const float FlagHeight = 0.38f;
        private const float FlagLift = 2.1f;
        private const float FlagReach = 0.22f;

        /// <summary>Насколько довернуть руку от позы привязки: вниз — почти до конца, вверх — сильнее.</summary>
        private const float ArmDownBlend = 0.82f;
        private const float ArmUpBlend = 0.55f;

        /// <summary>
        /// Рассадить зал по трибунам и оживить его.
        /// </summary>
        /// <returns>Сколько зрителей село — для отчёта и приёмки.</returns>
        internal static int Build(Transform decor, Transform studio, System.Random rng)
        {
            Transform stands = studio.Find("Stands");
            if (stands == null)
            {
                Debug.LogWarning("Трибун в павильоне нет — публику ставить некуда");
                return 0;
            }

            Transform group = Group(decor, CrowdGroup);

            Mesh[] kinds = LoadCrowdMeshes();
            if (kinds.Length == 0)
            {
                Debug.LogWarning("Мешей зала нет — трибуны останутся пустыми");
                return 0;
            }

            int seated = 0;
            for (int i = 0; i < stands.childCount; i++)
            {
                Transform bench = stands.GetChild(i);
                if (!bench.name.StartsWith(BleacherPrefix, System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryWorldBounds(bench.gameObject, out Bounds bounds))
                {
                    continue;
                }

                bool amphi = bench.name.StartsWith(AmphiPrefix, System.StringComparison.Ordinal);
                seated += SeatBench(group, kinds, bounds, amphi, i, rng);
            }

            Wire(group, kinds);
            return seated;
        }

        /// <summary>
        /// Рассадить одну секцию. Ряды идут по глубине скамьи, места — вдоль
        /// её длинной стороны.
        ///
        /// <b>Длинная сторона ищется замером, а не берётся из имени.</b>
        /// Боковые пояса развёрнуты на ±90° и вытянуты по Z, амфитеатр стоит
        /// поперёк и вытянут по X. Одна и та же рассадка обслуживает оба
        /// только потому, что спрашивает у габарита, куда секция длиннее.
        /// </summary>
        private static int SeatBench(Transform group, Mesh[] kinds, Bounds bounds, bool amphi, int bench,
            System.Random rng)
        {
            bool alongZ = bounds.size.z >= bounds.size.x;
            int rows = amphi ? AmphiRows : BeltRows;
            int seats = amphi ? AmphiSeats : BeltSeats;

            // Зритель смотрит на арену. Для бокового пояса это поперёк
            // скамьи в сторону нуля по X, для амфитеатра — вдоль −Z:
            // и то, и другое есть «в сторону середины арены от своего места».
            float faceX = alongZ ? -Mathf.Sign(bounds.center.x) : 0f;
            float faceZ = alongZ ? 0f : -1f;
            float yaw = Mathf.Atan2(faceX, faceZ) * Mathf.Rad2Deg;

            int placed = 0;
            for (int row = 0; row < rows; row++)
            {
                // Передний ряд стоит ниже и ближе к арене — на средней
                // ступени скамьи. Один ряд на верхней ступени читался
                // шеренгой; два уступом читаются трибуной.
                bool front = rows > 1 && row == 0;
                float level = front ? FrontRowLevel : BackRowLevel;
                float y = bounds.min.y + bounds.size.y * level;
                float reach = front ? FrontRowReach : 0f;

                for (int seat = 0; seat < seats; seat++)
                {
                    float along = (seat + 0.5f) / seats;
                    float x = alongZ
                        ? bounds.center.x + faceX * reach
                        : Mathf.Lerp(bounds.min.x, bounds.max.x, along) + Jitter(rng, 0.14f);
                    float z = alongZ
                        ? Mathf.Lerp(bounds.min.z, bounds.max.z, along) + Jitter(rng, 0.14f)
                        : bounds.center.z + faceZ * reach;

                    SpawnFan(group, kinds, new Vector3(x, y, z), yaw + Jitter(rng, FanYawJitter),
                        bench, row * seats + seat, rng);
                    placed++;
                }
            }

            return placed;
        }

        /// <summary>
        /// Поставить одного зрителя с его реквизитом.
        ///
        /// Поза берётся случайной из двух: спокойная и болеющая. Смесь важна
        /// не только для вида — <see cref="HoleInWallCrowd"/> находит вторую
        /// позу того же персонажа по надетой, и зал, поставленный целиком
        /// в одной позе, оживал бы ровно так же.
        /// </summary>
        private static void SpawnFan(Transform parent, Mesh[] kinds, Vector3 at, float yaw, int bench, int seat,
            System.Random rng)
        {
            var fan = new GameObject($"Fan_{bench}_{seat}");
            fan.transform.SetParent(parent, false);
            fan.transform.localPosition = at;
            fan.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            fan.transform.localScale = Vector3.one * Mathf.Lerp(FanScaleMin, FanScaleMax, (float)rng.NextDouble());

            fan.AddComponent<MeshFilter>().sharedMesh = kinds[rng.Next(kinds.Length)];
            fan.AddComponent<MeshRenderer>().sharedMaterial =
                HoleInWallPaletteAssets.Get(HoleInWallPaletteAssets.CrowdWearTone(rng.Next(64)));

            double roll = rng.NextDouble();
            if (roll < PhoneShare)
            {
                Chip(fan.transform, "Phone", Tone.NeonCyan,
                    new Vector3(0f, PhoneHeight, PhoneReach), new Vector3(PhoneSize, PhoneSize, PhoneSize * 0.35f));
            }
            else if (roll < PhoneShare + FlagShare)
            {
                // Флажок поднят над головой и повёрнут полотнищем к арене:
                // с тридцати метров от зрителя видно ровно две вещи — силуэт
                // и то, что он держит.
                Chip(fan.transform, "Flag", HoleInWallPaletteAssets.LaneTone(rng.Next(4)),
                    new Vector3(0f, FlagLift, FlagReach), new Vector3(FlagWidth, FlagHeight, 0.04f));
            }
        }

        /// <summary>
        /// Светящаяся мелочь в руках зрителя: огонёк телефона или флажок.
        /// Вложена в фигуру, поэтому едет вместе с ней при каждом подскоке.
        /// </summary>
        private static void Chip(Transform fan, string chipName, Tone tone, Vector3 offset, Vector3 size)
        {
            GameObject chip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chip.name = chipName;
            chip.transform.SetParent(fan, false);
            chip.transform.localPosition = offset;
            chip.transform.localScale = size;

            Object.DestroyImmediate(chip.GetComponent<Collider>());
            chip.GetComponent<Renderer>().sharedMaterial = HoleInWallPaletteAssets.Get(tone);
        }

        /// <summary>
        /// Повесить на группу оживление и отдать ему таблицу поз и ссылку
        /// на игру.
        ///
        /// Ссылка ищется по сцене здесь, в редакторе, а не в
        /// <c>Awake</c> компонента: поиск объекта в рантайме запрещён
        /// правилами проекта, а построителю сцены он ровно для того и дан.
        /// </summary>
        private static void Wire(Transform group, Mesh[] kinds)
        {
            var crowd = group.gameObject.AddComponent<HoleInWallCrowd>();
            var game = Object.FindFirstObjectByType<HoleInWallMinigame>(FindObjectsInactive.Include);
            if (game == null)
            {
                Debug.LogWarning("В сцене нет HoleInWallMinigame — зал будет качаться, но не реагировать");
            }

            // Пары идут парами в самом ассете: BakeCrowdMeshes кладёт их
            // подряд, «спокоен» и следом «болеет» того же персонажа.
            int pairs = kinds.Length / 2;
            var so = new SerializedObject(crowd);
            so.FindProperty("game").objectReferenceValue = game;

            SerializedProperty wardrobe = so.FindProperty("wardrobe");
            wardrobe.arraySize = pairs;
            for (int i = 0; i < pairs; i++)
            {
                SerializedProperty pair = wardrobe.GetArrayElementAtIndex(i);
                pair.FindPropertyRelative("Idle").objectReferenceValue = kinds[i * 2];
                pair.FindPropertyRelative("Cheer").objectReferenceValue = kinds[i * 2 + 1];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ========== ЗАПЕКАНИЕ ПОЗ ==========

        /// <summary>
        /// Путь к модели: сначала уже перенесённая копия, и только потом пак.
        ///
        /// ⚠️ Иначе каждая пересборка зала заново ломает запекание 4.6:
        /// в сцене снова появляются ссылки на <c>Assets/Synty/**</c>, и
        /// напарник без паков открывает сцену с дырами.
        /// </summary>
        private static string ModelPath(string fileName)
        {
            string baked = BakedModels + fileName;
            return AssetDatabase.LoadAssetAtPath<GameObject>(baked) != null ? baked : PackModels + fileName;
        }

        /// <summary>
        /// Меши зрителей — <b>запечённые в позе</b>, а не в позе привязки.
        ///
        /// ⚠️ Первым прогоном брался готовый <c>sharedMesh</c>, то есть поза
        /// привязки: сорок человек стояли с раскинутыми руками. На приёмке это
        /// прочли сразу — «что за болванки слева справа стоят». T-поза
        /// на трибуне читается не зрителем, а манекеном.
        ///
        /// Здесь скелет разворачивается в сцене, руки опускаются или
        /// поднимаются, и меш снимается <c>BakeMesh</c> уже в этом виде.
        /// Дальше он живёт обычным <c>MeshRenderer</c>, а позу зрителю меняет
        /// подменой меша <see cref="HoleInWallCrowd"/>.
        ///
        /// <b>Поворот считается по направлению руки, а не по оси кости.</b>
        /// У кости своя система координат, и «повернуть на 60° вокруг Z» даёт
        /// у разных ригов разное. Здесь берётся текущее направление
        /// плечо→кисть и доворачивается к нужному: работает независимо
        /// от соглашений рига.
        /// </summary>
        private static Mesh[] LoadCrowdMeshes()
        {
            var existing = AssetDatabase.LoadAllAssetsAtPath(CrowdMeshPath);
            if (existing != null && existing.Length > 0)
            {
                var cached = new List<Mesh>(existing.Length);
                for (int i = 0; i < existing.Length; i++)
                {
                    if (existing[i] is Mesh mesh)
                    {
                        cached.Add(mesh);
                    }
                }

                if (cached.Count > 0)
                {
                    return Order(cached);
                }
            }

            return BakeCrowdMeshes();
        }

        /// <summary>
        /// Выстроить меши парами «спокоен, болеет» по имени.
        ///
        /// ⚠️ <c>LoadAllAssetsAtPath</c> отдаёт подобъекты в порядке, который
        /// нигде не обещан, и после первой же пересборки ассета пары
        /// разъезжаются: зритель получал бы «болеет» от другого персонажа,
        /// то есть менял бы одежду вместе с позой. Порядок восстанавливается
        /// по именам, которые ставит <see cref="BakePose"/>.
        /// </summary>
        private static Mesh[] Order(List<Mesh> meshes)
        {
            var byName = new Dictionary<string, Mesh>(meshes.Count);
            for (int i = 0; i < meshes.Count; i++)
            {
                byName[meshes[i].name] = meshes[i];
            }

            var paired = new List<Mesh>(meshes.Count);
            for (int i = 0; i < meshes.Count; i++)
            {
                string name = meshes[i].name;
                if (!name.EndsWith(IdleSuffix, System.StringComparison.Ordinal))
                {
                    continue;
                }

                string cheerName = name.Substring(0, name.Length - IdleSuffix.Length) + CheerSuffix;
                if (!byName.TryGetValue(cheerName, out Mesh cheer))
                {
                    continue;
                }

                paired.Add(meshes[i]);
                paired.Add(cheer);
            }

            // Ассет мог остаться от старой сборки, где имён с приставками
            // ещё не было: тогда пересобираем его целиком, а не садим зал
            // на пустой список.
            return paired.Count > 0 ? paired.ToArray() : BakeCrowdMeshes();
        }

        private const string IdleSuffix = "_Idle";
        private const string CheerSuffix = "_Cheer";

        /// <summary>
        /// Развернуть персонажей пака, поставить им руки и снять меши.
        /// На каждого — две позы, чтобы зал не читался штампом.
        /// </summary>
        private static Mesh[] BakeCrowdMeshes()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(CrowdModel));
            if (source == null)
            {
                Debug.LogWarning($"Персонажей нет ни в паке, ни в запечённом арте ({CrowdModel}) — зала не будет");
                return System.Array.Empty<Mesh>();
            }

            var template = (GameObject)PrefabUtility.InstantiatePrefab(source);
            var baked = new List<Mesh>(16);

            try
            {
                foreach (SkinnedMeshRenderer skin in template.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin.sharedMesh == null)
                    {
                        continue;
                    }

                    // Две позы на персонажа и строго подряд: пары читаются
                    // порядком, и Wire отдаёт их оживлению как есть.
                    baked.Add(BakePose(skin, Vector3.down, ArmDownBlend));
                    baked.Add(BakePose(skin, Vector3.up, ArmUpBlend));
                }
            }
            finally
            {
                Object.DestroyImmediate(template);
            }

            if (baked.Count == 0)
            {
                return System.Array.Empty<Mesh>();
            }

            AssetDatabase.DeleteAsset(CrowdMeshPath);
            AssetDatabase.CreateAsset(baked[0], CrowdMeshPath);
            for (int i = 1; i < baked.Count; i++)
            {
                AssetDatabase.AddObjectToAsset(baked[i], CrowdMeshPath);
            }

            AssetDatabase.SaveAssets();
            return baked.ToArray();
        }

        /// <summary>
        /// Довернуть обе руки к заданному направлению и снять меш.
        /// </summary>
        /// <param name="towards">Куда тянуть руку: вниз или вверх</param>
        /// <param name="blend">Насколько довернуть, 0…1 от исходного к цели</param>
        private static Mesh BakePose(SkinnedMeshRenderer skin, Vector3 towards, float blend)
        {
            // ⚠️ Поза сбрасывается после каждого снятия. Персонажи пака сидят
            // в одном FBX на общем скелете, поэтому поворот плеча, сделанный
            // для одного, виден всем остальным. Без сброса повороты
            // НАКАПЛИВАЮТСЯ: первый зритель получал верную позу, второй —
            // двойную, а «руки вверх» доворачивались от уже опущенных.
            // На трибуне это вышло половиной зала с одной торчащей рукой.
            Transform left = FindBone(skin, "Shoulder_L");
            Transform right = FindBone(skin, "Shoulder_R");
            Quaternion leftWas = left != null ? left.localRotation : Quaternion.identity;
            Quaternion rightWas = right != null ? right.localRotation : Quaternion.identity;

            SwingArm(left, FindBone(skin, "Hand_L"), towards, blend);
            SwingArm(right, FindBone(skin, "Hand_R"), towards, blend);

            var mesh = new Mesh
            {
                name = skin.sharedMesh.name + (towards == Vector3.up ? CheerSuffix : IdleSuffix)
            };

            skin.BakeMesh(mesh);
            mesh.RecalculateBounds();

            if (left != null)
            {
                left.localRotation = leftWas;
            }

            if (right != null)
            {
                right.localRotation = rightWas;
            }

            return mesh;
        }

        /// <summary>Кость по имени в скелете этого рендерера.</summary>
        private static Transform FindBone(SkinnedMeshRenderer skin, string boneName)
        {
            Transform[] bones = skin.bones;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null && bones[i].name == boneName)
                {
                    return bones[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Повернуть плечо так, чтобы рука пошла к <paramref name="towards"/>.
        /// Ищет кости по имени в массиве самого рендерера: свой скелет
        /// у каждого персонажа, и общего <c>Animator</c> тут не хватило бы.
        /// </summary>
        private static void SwingArm(Transform shoulder, Transform hand, Vector3 towards, float blend)
        {
            if (shoulder == null || hand == null)
            {
                return;
            }

            Vector3 current = hand.position - shoulder.position;
            if (current.sqrMagnitude < 0.0001f)
            {
                return;
            }

            current.Normalize();
            Vector3 target = Vector3.Slerp(current, towards, blend).normalized;
            shoulder.rotation = Quaternion.FromToRotation(current, target) * shoulder.rotation;
        }
    }
}
