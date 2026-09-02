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
    /// и папки готовых клипов — подфаза 4.5.
    ///
    /// Существует, чтобы между «звуки сгенерированы» и «звуки в игре» не было ручного шага.
    /// Дюжину клипов можно перетащить в инспектор мышью, и ровно этот шаг звук раз за разом
    /// не переживал: на Duck Hunt и «Порядке банок» файлы были нужны, а до игры не доехали.
    /// Здесь связь слот → клип задаётся именем файла: клип слота <c>splash</c> — это
    /// <c>splash.wav</c> в папке игры, и разойтись им негде.
    ///
    /// Варианты приёмки (<c>splash_v1..v3</c>) лежат в подпапке <c>Probe/</c> и сборщиком
    /// не берутся: победитель кладётся в папку игры под именем слота.
    /// </summary>
    internal static class SfxLibraryBuilder
    {
        /// <summary>Громкость слота, если манифест её не задал. Клипы нормализованы, так что единица — законное значение.</summary>
        private const float DefaultVolume = 1f;

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
            public bool loop;
            public bool spatial;
            public float volume;
        }

        [MenuItem("Igruha/Арт/Собрать библиотеку звука")]
        private static void Build()
        {
            string startDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "art"));
            string manifestPath = EditorUtility.OpenFilePanel("Манифест звука мини-игры", startDir, "json");
            if (string.IsNullOrEmpty(manifestPath)) return;

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
                Debug.LogError("[Звук] В манифесте нет ни одного слота.");
                return;
            }

            if (string.IsNullOrEmpty(manifest.output_folder))
            {
                Debug.LogError("[Звук] В манифесте не задан output_folder — неизвестно, где искать клипы.");
                return;
            }

            var entries = new List<MinigameSfxLibrary.Entry>(manifest.slots.Length);
            var missing = new List<string>();

            foreach (Slot slot in manifest.slots)
            {
                if (string.IsNullOrEmpty(slot.id)) continue;

                string clipPath = $"{manifest.output_folder}/{slot.id}.wav";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                if (clip == null) missing.Add($"{slot.id} → {clipPath}");

                entries.Add(new MinigameSfxLibrary.Entry
                {
                    Id = slot.id,
                    Clip = clip,
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
                      + $"Слотов: {entries.Count}, клипов на месте: {entries.Count - missing.Count}");

            if (missing.Count > 0)
                Debug.LogWarning("[Звук] Клипы не найдены (слот останется немым):\n" + string.Join("\n", missing));

            Selection.activeObject = library;
        }
    }
}
