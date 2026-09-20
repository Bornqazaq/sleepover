using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Igruha.Core.Audio;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Приёмка музыки: переносит треки из папки поставки <c>igruha/музыка</c> в
    /// проект, выставляет импорт и собирает <see cref="MusicLibrary"/>.
    ///
    /// Сделано отдельным проходом по той же причине, что и сборщик звука:
    /// между «трек приехал» и «трек играет» не должно быть ручного шага. Папка
    /// поставки названа по-русски и по игре («для хаба», «верю не верю»), а
    /// сцены в проекте — латиницей, поэтому соответствие задано таблицей ниже.
    /// Папка, которой в таблице нет, не молчит: о ней пишется предупреждение,
    /// иначе трек тихо не доедет до игры — ровно так уже терялся звук.
    ///
    /// Сами файлы поставки остаются на месте и в репозиторий не идут: в проект
    /// кладётся копия под латинским именем, потому что имена ассетов с
    /// кириллицей ломают Addressables и пути сборки.
    /// </summary>
    internal static class MusicLibraryBuilder
    {
        /// <summary>Папка поставки рядом с Assets.</summary>
        private const string DeliveryFolder = "музыка";

        /// <summary>Куда кладутся треки в проекте.</summary>
        private const string MusicRoot = "Assets/_Project/Audio/Music";

        /// <summary>Библиотека лежит в Resources: сцены у музыки нет, ссылаться на неё неоткуда.</summary>
        private const string LibraryFolder = "Assets/_Project/Resources";

        private const string LibraryPath = LibraryFolder + "/MusicLibrary.asset";

        /// <summary>Папка поставки → сцена Build Settings.</summary>
        private static readonly Dictionary<string, string> SceneByFolder = new Dictionary<string, string>
        {
            { "для хаба", "Hub" },
            { "секундомер", "Stopwatch" },
            { "дак хант", "DuckHunt" },
            { "переноска предмета", "CarryItem" },
            { "верю не верю", "BelieveOrNot" },
            { "плачущие ангелы", "CryingAngels" },
            { "дырка в стене", "HoleInWall" },
            { "экзамен", "Exam" },
            { "рейс на память", "MemoryRun" },
            { "порядок банок", "CansOrder" },
            { "заражение", "Infection" }
        };

        [MenuItem("Igruha/Арт/Принять музыку и собрать библиотеку")]
        internal static void BuildAll()
        {
            string delivery = Path.GetFullPath(Path.Combine(Application.dataPath, "..", DeliveryFolder));
            if (!Directory.Exists(delivery))
            {
                Debug.LogError($"[Музыка] Папки поставки нет: {delivery}");
                return;
            }

            Directory.CreateDirectory(MusicRoot);
            Directory.CreateDirectory(LibraryFolder);

            var entries = new List<MusicLibrary.Entry>();
            var imported = new List<string>();

            foreach (string folder in Directory.GetDirectories(delivery))
            {
                string folderName = Path.GetFileName(folder);
                if (!SceneByFolder.TryGetValue(folderName.ToLowerInvariant(), out string scene))
                {
                    Debug.LogWarning($"[Музыка] Папка «{folderName}» не сопоставлена сцене — трек не доехал до игры. " +
                                     "Добавить строку в MusicLibraryBuilder.SceneByFolder.");
                    continue;
                }

                string[] tracks = Directory.GetFiles(folder, "*.wav");
                if (tracks.Length == 0)
                {
                    Debug.LogWarning($"[Музыка] В папке «{folderName}» нет ни одного .wav");
                    continue;
                }

                if (tracks.Length > 1)
                {
                    Debug.LogWarning($"[Музыка] В папке «{folderName}» треков {tracks.Length}, беру первый: " +
                                     Path.GetFileName(tracks[0]));
                }

                string assetPath = $"{MusicRoot}/MUS_{scene}.wav";
                File.Copy(tracks[0], assetPath, true);
                imported.Add(assetPath);

                entries.Add(new MusicLibrary.Entry { Scene = scene, Clip = null, Volume = 1f });
            }

            AssetDatabase.Refresh();

            for (int i = 0; i < imported.Count; i++) ApplyImportSettings(imported[i]);

            // Клипы подхватываются после импорта: до Refresh ссылки взять неоткуда.
            for (int i = 0; i < entries.Count; i++)
            {
                MusicLibrary.Entry entry = entries[i];
                entry.Clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{MusicRoot}/MUS_{entry.Scene}.wav");
                entries[i] = entry;

                if (entry.Clip == null) Debug.LogError($"[Музыка] Не импортировался трек сцены {entry.Scene}");
            }

            MusicLibrary library = AssetDatabase.LoadAssetAtPath<MusicLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<MusicLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            library.SetEntries(entries.ToArray());
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Музыка] Собрано тем: {entries.Count} → {LibraryPath}");
        }

        /// <summary>
        /// Музыка грузится потоком и сжимается: трек на три минуты в памяти
        /// несжатым — это под тридцать мегабайт на каждую сцену.
        /// </summary>
        private static void ApplyImportSettings(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
            if (importer == null)
            {
                Debug.LogError($"[Музыка] Не нашёл импортёр: {assetPath}");
                return;
            }

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            settings.preloadAudioData = false;

            importer.defaultSampleSettings = settings;
            importer.forceToMono = false;
            importer.loadInBackground = true;

            importer.SaveAndReimport();
        }
    }
}
