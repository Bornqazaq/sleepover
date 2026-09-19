using UnityEditor;
using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Minigame;
using Igruha.Minigames.Circus;
using Igruha.Minigames.Stopwatch;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Постановка звука цирковой арены — фаза 5.
    ///
    /// Вешает <see cref="MinigameAudioPlayer"/> с библиотекой на арену,
    /// заводит <see cref="CircusAudio"/> (события арены, общие на обе игры)
    /// и, если сцена «Секундомера», <see cref="StopwatchAudio"/> (кнопка
    /// и гонг).
    ///
    /// <b>Ссылки проставляются кодом, а не руками в инспекторе.</b> Ровно
    /// этот шаг звук не переживал дважды: пересборка пересоздаёт клетки
    /// и кнопки, набитые вручную ссылки повисают, и игра немеет молча.
    /// </summary>
    internal static class CircusSfx
    {
        private const string LibraryPath = "Assets/_Project/Audio/Stopwatch/SfxLibrary.asset";

        /// <summary>
        /// Обновить клипы цирковой арены в открытой сцене, ничего не пересобирая.
        ///
        /// Нужен, когда приехала поставка: ссылки на клипы надо перецелить, а
        /// пересборка арены ради этого снесла бы и расставила заново весь
        /// реквизит. Отдельным пунктом ещё и потому, что арену цирка делят две
        /// игры, и открыта может быть любая из двух сцен.
        /// </summary>
        [MenuItem("Igruha/Звук/Обновить клипы цирковой арены")]
        internal static void RefreshClips()
        {
            var library = AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>(LibraryPath);
            if (library == null)
            {
                Debug.LogError($"[Звук] Нет библиотеки: {LibraryPath}. Сперва «Igruha/Арт/Собрать все библиотеки звука».");
                return;
            }

            var bear = Object.FindFirstObjectByType<PitBear>(FindObjectsInactive.Include);
            WireBear(bear, library);

            var director = Object.FindFirstObjectByType<DistractionDirector>(FindObjectsInactive.Include);
            if (director != null)
            {
                var serialized = new SerializedObject(director);
                AssignClip(serialized, library, "tickClip", "tick");
                AssignClip(serialized, library, "roarClip", "bear_roar");
                AssignClip(serialized, library, "crowdClip", "crowd_ambience");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log($"[Звук] Клипы цирковой арены обновлены: медведь {(bear != null ? "есть" : "не найден")}, "
                      + $"отвлекалки {(director != null ? "есть" : "нет")}. {SfxHost.Describe(library)}");
        }

        internal static void Build(Transform arena, CageStation[] cages, PitBear bear)
        {
            var library = AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>(LibraryPath);
            if (library == null)
            {
                Debug.LogWarning("CircusSfx: не найдена " + LibraryPath +
                                 " — собери библиотеку пунктом «Igruha/Арт/Собрать библиотеку звука».");
                return;
            }

            Transform root = EnsureGroup(arena, "Audio");
            MinigameAudioPlayer player = Ensure<MinigameAudioPlayer>(root.gameObject);
            var playerSerialized = new SerializedObject(player);
            playerSerialized.FindProperty("library").objectReferenceValue = library;
            playerSerialized.ApplyModifiedPropertiesWithoutUndo();

            var controller = Object.FindFirstObjectByType<MinigameControllerBase>(FindObjectsInactive.Include);

            CircusAudio arenaAudio = Ensure<CircusAudio>(root.gameObject);
            var arenaSerialized = new SerializedObject(arenaAudio);
            arenaSerialized.FindProperty("audioPlayer").objectReferenceValue = player;
            arenaSerialized.FindProperty("bear").objectReferenceValue = bear;
            arenaSerialized.FindProperty("controller").objectReferenceValue = controller;
            SetArray(arenaSerialized, "cages", cages);
            arenaSerialized.ApplyModifiedPropertiesWithoutUndo();

            WireStopwatch(root, player, library);
            WireBear(bear, library);
        }

        /// <summary>
        /// Клипы медведя — рёв, взмах, удар.
        ///
        /// <see cref="CircusBearFeedback"/> крутит их своим источником и своими
        /// часами удара: взмах приходится на 0.47 с после начала замаха, и вынести
        /// эту привязку в общий проигрыватель значило бы переложить туда же весь
        /// такт атаки. Поэтому здесь не привязка события к слоту, а раздача клипов
        /// из той же библиотеки — чтобы правда о том, чем звучит медведь, осталась
        /// одна, в манифесте.
        ///
        /// Пустое поле оставляется пустым намеренно: шаг медведя поставка не
        /// привезла, и висящая ссылка на удалённый черновик — ровно то, из-за чего
        /// медведь молчал, выглядя озвученным.
        /// </summary>
        private static void WireBear(PitBear bear, MinigameSfxLibrary library)
        {
            if (bear == null) return;

            var feedback = bear.GetComponent<CircusBearFeedback>();
            if (feedback == null) return;

            var serialized = new SerializedObject(feedback);
            AssignClip(serialized, library, "growl", "bear_roar");
            AssignClip(serialized, library, "swipe", "bear_swipe");
            AssignClip(serialized, library, "step", "bear_step");
            AssignClips(serialized, library, "impacts", "bear_impact");
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Своё у «Секундомера»: кнопки, гонг и — главное — настоящие клипы
        /// вместо сгенерированных заглушек в <see cref="DistractionDirector"/>.
        ///
        /// В «Порядке банок» ничего этого нет, поэтому молча выходим:
        /// один и тот же билдер работает в обеих сценах.
        /// </summary>
        private static void WireStopwatch(Transform root, MinigameAudioPlayer player, MinigameSfxLibrary library)
        {
            var game = Object.FindFirstObjectByType<StopwatchMinigame>(FindObjectsInactive.Include);
            if (game == null)
            {
                return;
            }

            var buttons = Object.FindObjectsByType<CageButton>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            StopwatchAudio audio = Ensure<StopwatchAudio>(root.gameObject);
            var serialized = new SerializedObject(audio);
            serialized.FindProperty("audioPlayer").objectReferenceValue = player;
            // Стадии живут на том же объекте, что и контроллер: гонг и дробь
            // приходят оттуда, а не опросом номера подраунда.
            serialized.FindProperty("stageState").objectReferenceValue =
                game.GetComponent<Igruha.Core.Minigame.MinigameStageState>();
            SetArray(serialized, "buttons", buttons);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Отвлекалки 8.12 крутят свои источники сами и до сих пор играли
            // синтезированные заглушки (GenerateTick и соседи). Подменяем их
            // настоящими клипами — сам ритм при этом не трогаем: период тика
            // выбирает сервер один на всех, иначе подраунд перестаёт быть
            // честным.
            var director = Object.FindFirstObjectByType<DistractionDirector>(FindObjectsInactive.Include);
            if (director == null)
            {
                return;
            }

            var directorSerialized = new SerializedObject(director);
            AssignClip(directorSerialized, library, "tickClip", "tick");
            AssignClip(directorSerialized, library, "roarClip", "bear_roar");
            AssignClip(directorSerialized, library, "crowdClip", "crowd_ambience");
            directorSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Кладёт в поле клип слота из библиотеки.
        ///
        /// <b>Из библиотеки, а не из папки по имени слота.</b> Слот и файл — разные
        /// имена: код зовёт <c>bear_roar</c>, а поставка привезла
        /// <c>SFX_CIRCUS_Bear_Roar</c> и положила его в общую папку цирка. Связь
        /// между ними держит манифест, и второе место, где эта связь угадывается
        /// по имени файла, разошлось бы с ним на первой же поставке. Ровно так
        /// медведь и онемел: ссылки указывали на удалённые черновики.
        ///
        /// Немой слот обнуляет поле. Пустое поле — честная тишина, висящая ссылка
        /// на несуществующий файл — тишина, выглядящая как звук.
        /// </summary>
        private static void AssignClip(SerializedObject serialized, MinigameSfxLibrary library, string field, string id)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                return;
            }

            AudioClip[] clips = FindClips(library, id);
            property.objectReferenceValue = clips.Length > 0 ? clips[0] : null;
        }

        /// <summary>То же для поля-пачки: вариации слота едут в массив целиком.</summary>
        private static void AssignClips(SerializedObject serialized, MinigameSfxLibrary library, string field, string id)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                return;
            }

            SetArray(serialized, field, FindClips(library, id));
        }

        /// <summary>Клипы слота: вариации, если они есть, иначе единственный клип. Немой слот — пустой массив.</summary>
        private static AudioClip[] FindClips(MinigameSfxLibrary library, string id)
        {
            if (library == null || !library.TryGet(id, out MinigameSfxLibrary.Entry entry))
            {
                Debug.LogWarning($"CircusSfx: в библиотеке нет слота «{id}» — поле останется пустым.");
                return System.Array.Empty<AudioClip>();
            }

            if (entry.Variants != null && entry.Variants.Length > 0) return entry.Variants;
            return entry.Clip != null ? new[] { entry.Clip } : System.Array.Empty<AudioClip>();
        }

        private static T Ensure<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        private static void SetArray(SerializedObject serialized, string field, Object[] values)
        {
            SerializedProperty array = serialized.FindProperty(field);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static Transform EnsureGroup(Transform parent, string name)
        {
            Transform group = parent.Find(name);
            if (group != null)
            {
                return group;
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            return go.transform;
        }
    }
}
