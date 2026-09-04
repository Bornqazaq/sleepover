using UnityEditor;
using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Minigame;
using Igruha.Minigames.Exam;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Постановка звука «Экзамена» на арену — подфаза 4.5.
    ///
    /// Между «клипы сгенерированы» и «звук в игре» не должно быть ручного шага:
    /// ровно этот шаг звук не пережил дважды — на Duck Hunt и «Порядке банок»
    /// файлы были нужны, а до игры не доехали. Здесь и проигрыватель,
    /// и привязка ставятся пересборкой арены.
    /// </summary>
    internal static class ExamSfx
    {
        private const string LibraryPath = "Assets/_Project/Audio/Exam/SfxLibrary.asset";

        /// <summary>
        /// Дальность затухания трёхмерных слотов. Зал 28.8 × 25.9 м, и створки
        /// обязаны быть слышны с любой его точки: игрок у задней стены должен
        /// понять, что пол раскрылся, не глядя туда.
        /// </summary>
        private const float Falloff = 34f;

        internal static string Build(GameObject arena, ExamConfig config)
        {
            var minigame = Object.FindFirstObjectByType<ExamMinigame>(FindObjectsInactive.Include);
            if (minigame == null)
            {
                return "⚠️ Звук: ExamMinigame в сцене не найден — вешать нечего";
            }

            var library = AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>(LibraryPath);
            if (library == null)
            {
                return $"⚠️ Звук: библиотеки нет ({LibraryPath}) — собрать «Igruha/Арт/Собрать библиотеку звука»";
            }

            GameObject host = minigame.gameObject;

            var player = host.GetComponent<MinigameAudioPlayer>();
            if (player == null)
            {
                player = host.AddComponent<MinigameAudioPlayer>();
            }

            var playerObject = new SerializedObject(player);
            playerObject.FindProperty("library").objectReferenceValue = library;
            playerObject.FindProperty("maxDistance").floatValue = Falloff;
            playerObject.ApplyModifiedPropertiesWithoutUndo();

            var audio = host.GetComponent<ExamAudio>();
            if (audio == null)
            {
                audio = host.AddComponent<ExamAudio>();
            }

            // Точка провала: середина ямы под платформами. Свист падения звучит
            // оттуда, а не с платформы, — иначе он приходит сверху, из места,
            // которое игрок только что покинул.
            float platformsZ = config.HallDepth * 0.5f - config.PodiumDepth - 2.16f - config.PlatformDepth * 0.5f;
            Transform pit = arena.transform.Find("Effects/Pit");
            if (pit == null)
            {
                var marker = new GameObject("PitSound");
                marker.transform.SetParent(arena.transform, false);
                pit = marker.transform;
            }

            pit.position = new Vector3(0f, -config.PitDepth * 0.5f, platformsZ);

            var so = new SerializedObject(audio);
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("stageState").objectReferenceValue = host.GetComponent<MinigameStageState>();
            so.FindProperty("minigame").objectReferenceValue = minigame;
            so.FindProperty("board").objectReferenceValue = FindIn<ExamBoard>(arena, "BoardCanvas");
            so.FindProperty("platformA").objectReferenceValue = FindIn<ExamAnswerPlatform>(arena, "Platform_A");
            so.FindProperty("platformB").objectReferenceValue = FindIn<ExamAnswerPlatform>(arena, "Platform_B");
            so.FindProperty("pitPoint").objectReferenceValue = pit;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(player);
            EditorUtility.SetDirty(audio);

            int slots = library.Entries != null ? library.Entries.Count : 0;
            return $"🔊 «Экзамен», звук 4.5 — библиотека на {slots} слотов, затухание {Falloff:F0} м" +
                   "\n   новых сетевых событий 0: стадии и створки игра уже показала каждому";
        }

        private static T FindIn<T>(GameObject arena, string childName) where T : Component
        {
            foreach (T candidate in arena.GetComponentsInChildren<T>(true))
            {
                if (candidate.name == childName)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
