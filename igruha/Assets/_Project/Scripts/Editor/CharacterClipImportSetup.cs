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
            // У Fat, MyBoy и Aza исходники названы не по общей конвенции (idle/run) —
            // суффиксы буквальные.
            "Neutral Idle", "Running",
            "Old Man Idle", "Goofy Running",
            "Happy Idle",
            // Ходьба в приседе крутится, пока зажат Ctrl, — тоже зацикленный клип.
            "Crouch Walk Forward",
            // Исходник той же ходьбы после пересборки 20.08 называется иначе,
            // и без этой строки прогон настроек импорта снимал ему loopTime:
            // сам присед от этого не ломается (в контроллере лежит собранная
            // копия CrouchWalkForward.anim), а вот следующая пересборка клипа
            // из этого исходника выдала бы незацикленную ходьбу.
            "Sneaking Forward",
            // Сравнение в HashSet регистрозависимое, а у Girl и Milez бег назван
            // "running" со строчной — без отдельной записи он не зациклится.
            "running"
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

        [MenuItem("Igruha/Player/Setup Girl Import Settings")]
        private static void SetupGirlMenu()
        {
            SetupGirl();
        }

        /// <summary>Точка входа для сборщика персонажа целиком (CharacterPrefabBuilder.CreateGirl).</summary>
        internal static void SetupGirl()
        {
            Setup("Girl");
        }

        [MenuItem("Igruha/Player/Setup Milez Import Settings")]
        private static void SetupMilezMenu()
        {
            SetupMilez();
        }

        /// <summary>Точка входа для сборщика персонажа целиком (CharacterPrefabBuilder.CreateMilez).</summary>
        internal static void SetupMilez()
        {
            Setup("Milez");
        }

        [MenuItem("Igruha/Player/Setup Aza Import Settings")]
        private static void SetupAzaMenu()
        {
            SetupAza();
        }

        /// <summary>Точка входа для сборщика персонажа целиком (CharacterPrefabBuilder.CreateAza).</summary>
        internal static void SetupAza()
        {
            Setup("Aza");
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

        /// <summary>
        /// Клипы, у которых корпус в исходнике развёрнут относительно корня: Mixamo
        /// экспортирует присед с телом вполоборота, и персонаж, стоящий лицом на
        /// 12 часов, визуально смотрит куда-то на 10. Лечится переносом поворота
        /// корня в позу (Bake Into Pose) с отсчётом от ориентации тела: тогда
        /// «вперёд» у модели совпадает с «вперёд» у трансформа.
        /// Остальным клипам это не нужно — у них ориентация авторская и верная,
        /// а лишний бейк только смазал бы её.
        /// </summary>
        private static readonly HashSet<string> BakeRootRotationClipSuffixes = new HashSet<string>
        {
            "Crouch Walk Forward"
        };

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

            if (BakeRootRotationClipSuffixes.Contains(clipName))
            {
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = false;
            }

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
