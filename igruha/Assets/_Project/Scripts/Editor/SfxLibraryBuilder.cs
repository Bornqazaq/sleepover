using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Audio;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Собирает <see cref="MinigameSfxLibrary"/> из манифеста <c>docs/art/&lt;игра&gt;-sfx.json</c>
    /// и папки готовых клипов — фаза 5.
    ///
    /// Существует, чтобы между «звуки сгенерированы» и «звуки в игре» не было ручного шага.
    /// Дюжину клипов можно перетащить в инспектор мышью, и ровно этот шаг звук раз за разом
    /// не переживал: на Duck Hunt и «Порядке банок» файлы были нужны, а до игры не доехали.
    /// Здесь связь слот → клип задаётся именем файла: клип слота <c>splash</c> — это
    /// <c>splash.wav</c> в папке игры, и разойтись им негде.
    ///
    /// <b>Имя файла можно задать отдельно от имени слота</b> — полем <c>file</c> в манифесте.
    /// Нужно там, где игровой код уже зовёт слот своим именем (<c>mine_blast</c>), а приехавший
    /// клип назван по паспорту поставки (<c>SFX_MEM_Plate_Explosion</c>). Переименовывать
    /// файл поставки нельзя: следующая поставка приедет под тем же именем и разойдётся
    /// с проектом. Переписывать игровой код ради имени файла — тем более.
    ///
    /// <b>Слот может быть пачкой вариаций.</b> Шесть шагов по дереву лежат как
    /// <c>SFX_CHR_Step_Wood_01..06.wav</c> и собираются в один слот: выбор варианта —
    /// дело <see cref="MinigameAudioPlayer"/>, а не манифеста.
    ///
    /// Варианты приёмки (<c>splash_v1..v3</c>) лежат в подпапке <c>Probe/</c> и сборщиком
    /// не берутся: победитель кладётся в папку игры под именем слота.
    /// </summary>
    internal static class SfxLibraryBuilder
    {
        /// <summary>Громкость слота, если манифест её не задал. Клипы нормализованы, так что единица — законное значение.</summary>
        private const float DefaultVolume = 1f;

        /// <summary>Сколько вариаций слота имеет смысл искать. Больше шести поставка не присылала ни разу.</summary>
        private const int MaxVariants = 32;

        [Serializable]
        private sealed class Manifest
        {
            public string game;
            public string output_folder;
            public Slot[] slots;
        }

        [Serializable]
        private sealed class Slot
        {
            public string id;
            public string file;
            public bool loop;
            public bool spatial;
            public float volume;
        }

        [MenuItem("Igruha/Арт/Собрать библиотеку звука")]
        private static void Pick()
        {
            string startDir = ManifestFolder();
            string manifestPath = EditorUtility.OpenFilePanel("Манифест звука мини-игры", startDir, "json");
            if (string.IsNullOrEmpty(manifestPath)) return;

            Build(manifestPath);
        }

        /// <summary>
        /// Пересобирает библиотеки по всем манифестам разом.
        ///
        /// Поставка звука приезжает сразу на несколько игр и на общий слой: 18.09 одна
        /// пачка задела девять папок. Девять раз выбирать файл мышью — тот же ручной
        /// шаг, ради устранения которого написан весь сборщик.
        /// </summary>
        [MenuItem("Igruha/Арт/Собрать все библиотеки звука")]
        internal static void BuildAll()
        {
            string folder = ManifestFolder();
            if (!Directory.Exists(folder))
            {
                Debug.LogError($"[Звук] Папки манифестов нет: {folder}");
                return;
            }

            string[] manifests = Directory.GetFiles(folder, "*-sfx.json");
            if (manifests.Length == 0)
            {
                Debug.LogWarning($"[Звук] В {folder} нет ни одного манифеста *-sfx.json");
                return;
            }

            foreach (string manifest in manifests) Build(manifest);

            Debug.Log($"[Звук] Пересобрано библиотек: {manifests.Length}");
        }

        /// <summary>
        /// Собрать библиотеку по конкретному манифесту.
        ///
        /// Отделено от пункта меню намеренно: выбор файла — модальное окно,
        /// а его нельзя открыть ни из пересборки арены, ни из сессии агента.
        /// Человеку остаётся меню, всему остальному — путь доводом.
        /// </summary>
        internal static void Build(string manifestPath)
        {
            Manifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception e)
            {
                Debug.LogError($"[Звук] Манифест не читается: {e.Message}");
                return;
            }

            if (manifest?.slots == null || manifest.slots.Length == 0)
            {
                Debug.LogError($"[Звук] В манифесте нет ни одного слота: {manifestPath}");
                return;
            }

            if (string.IsNullOrEmpty(manifest.output_folder))
            {
                Debug.LogError($"[Звук] В манифесте не задан output_folder: {manifestPath}");
                return;
            }

            var entries = new List<MinigameSfxLibrary.Entry>(manifest.slots.Length);
            var missing = new List<string>();
            int clips = 0;

            foreach (Slot slot in manifest.slots)
            {
                if (string.IsNullOrEmpty(slot.id)) continue;

                string baseName = string.IsNullOrEmpty(slot.file) ? slot.id : slot.file;
                AudioClip[] variants = LoadVariants(manifest.output_folder, baseName);

                if (variants.Length == 0) missing.Add($"{slot.id} → {manifest.output_folder}/{baseName}.wav");
                else clips += variants.Length;

                entries.Add(new MinigameSfxLibrary.Entry
                {
                    Id = slot.id,
                    Clip = variants.Length > 0 ? variants[0] : null,
                    Variants = variants.Length > 1 ? variants : Array.Empty<AudioClip>(),
                    Volume = slot.volume > 0f ? Mathf.Clamp01(slot.volume) : DefaultVolume,
                    Loop = slot.loop,
                    Spatial = slot.spatial,
                });
            }

            string libraryPath = $"{manifest.output_folder}/SfxLibrary.asset";
            var library = AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>(libraryPath);
            bool created = library == null;

            if (created)
            {
                library = ScriptableObject.CreateInstance<MinigameSfxLibrary>();
                Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Application.dataPath, "..", manifest.output_folder)));
                AssetDatabase.CreateAsset(library, libraryPath);
            }

            library.SetEntries(entries.ToArray());
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Звук] «{manifest.game}»: библиотека {(created ? "создана" : "обновлена")} — {libraryPath}\n"
                      + $"Слотов: {entries.Count}, немых: {missing.Count}, клипов подхвачено: {clips}");

            if (missing.Count > 0)
                Debug.LogWarning("[Звук] Клипы не найдены (слот останется немым):\n" + string.Join("\n", missing));
        }

        /// <summary>
        /// Клипы слота: сперва пачка вариаций <c>&lt;база&gt;_01..NN.wav</c>, затем одиночный
        /// <c>&lt;база&gt;.wav</c>. Порядок важен — поставка присылает даже единственный звук
        /// с номером (<c>SFX_UI_Click_01.wav</c>), и без нумерованной ветки он бы не нашёлся.
        /// </summary>
        private static AudioClip[] LoadVariants(string folder, string baseName)
        {
            var found = new List<AudioClip>(MaxVariants);

            for (int i = 1; i <= MaxVariants; i++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{folder}/{baseName}_{i:00}.wav");
                if (clip == null) break;
                found.Add(clip);
            }

            if (found.Count == 0)
            {
                var single = AssetDatabase.LoadAssetAtPath<AudioClip>($"{folder}/{baseName}.wav");
                if (single != null) found.Add(single);
            }

            return found.ToArray();
        }

        /// <summary>Папка манифестов — <c>docs/art</c> рядом с Unity-проектом, а не внутри него.</summary>
        private static string ManifestFolder()
            => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "art"));
    }
}
