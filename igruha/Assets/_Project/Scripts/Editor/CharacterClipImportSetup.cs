using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Приводит FBX персонажа к единой схеме импорта проекта: Humanoid-риг
    /// (иначе нет ретаргета и общей схемы с Karlan/Boss) и loopTime у клипов,
    /// которые обязаны крутиться бесконечно — idle, бег и танцы-эмоции.
    /// Мышкой в инспекторе это 17 файлов по десятку галочек, поэтому — скриптом.
    /// </summary>
    internal static class CharacterClipImportSetup
    {
        private const string AnimationsFolder = "Assets/_Project/Art/Animations/";

        /// <summary>Суффиксы файлов вида Имя@суффикс.fbx, чьи клипы должны зацикливаться.</summary>
        private static readonly HashSet<string> LoopingClipSuffixes = new HashSet<string>
        {
            "idle", "run",
            "dance1", "dance2", "dance3", "dance4", "dance5", "dance6", "dance7", "dance8",
            // У Fat и MyBoy исходники названы не по общей конвенции (idle/run) —
            // суффиксы буквальные.
            "Neutral Idle", "Running",
            "Old Man Idle", "Goofy Running"
        };

        [MenuItem("Igruha/Player/Setup Shlanga Import Settings")]
        private static void SetupShlanga()
        {
            Setup("Shlanga");
        }

        [MenuItem("Igruha/Player/Setup Fat Import Settings")]
        private static void SetupFat()
        {
            Setup("Fat");
        }

        [MenuItem("Igruha/Player/Setup MyBoy Import Settings")]
        private static void SetupMyBoy()
        {
            Setup("MyBoy");
        }

        /// <summary>
        /// Обрабатываются только файлы Имя@клип.fbx: меш персонажа приходит
        /// вместе с ними, а отдельная модель из Art/Models не нужна — там голый
        /// меш без скелета, префаб собирается не из него (см. CharacterPrefabBuilder).
        /// </summary>
        private static void Setup(string characterName)
        {
            int processed = 0;

            // Фильтруем сами: '@' в поисковом запросе AssetDatabase не ищется буквально.
            string prefix = characterName + "@";
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { AnimationsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileName(path).StartsWith(prefix) && ApplyToAnimation(path))
                {
                    processed++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"CharacterClipImportSetup ({characterName}): переимпортировано файлов — {processed}.");
        }

        private static bool ApplyToAnimation(string path)
        {
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
            {
                return false;
            }

            string clipName = ClipNameFromPath(path);
            bool shouldLoop = LoopingClipSuffixes.Contains(clipName);

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;

            // Собственные clipAnimations нужны, чтобы задать loopTime: у клипов
            // «по умолчанию» (defaultClipAnimations) настройки не сохраняются.
            ModelImporterClipAnimation[] defaults = importer.defaultClipAnimations;
            if (defaults.Length == 0)
            {
                Debug.LogWarning($"CharacterClipImportSetup: в {path} нет анимации — пропускаю.");
                return false;
            }

            ModelImporterClipAnimation clip = defaults[0];
            clip.name = clipName;
            clip.loopTime = shouldLoop;
            clip.keepOriginalPositionY = true;
            importer.clipAnimations = new[] { clip };

            importer.SaveAndReimport();
            return true;
        }

        /// <summary>«Shlanga@dance1.fbx» → «dance1»: имя клипа = суффикс после '@'.</summary>
        private static string ClipNameFromPath(string path)
        {
            string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            int separator = fileName.IndexOf('@');
            return separator >= 0 ? fileName[(separator + 1)..] : fileName;
        }
    }
}
