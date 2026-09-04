using UnityEditor;
using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Minigame;
using Igruha.Minigames.Circus;
using Igruha.Minigames.Stopwatch;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Постановка звука цирковой арены — подфаза 4.5.
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
        private const string ClipFolder = "Assets/_Project/Audio/Stopwatch/";

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

            WireStopwatch(root, player);
        }

        /// <summary>
        /// Своё у «Секундомера»: кнопки, гонг и — главное — настоящие клипы
        /// вместо сгенерированных заглушек в <see cref="DistractionDirector"/>.
        ///
        /// В «Порядке банок» ничего этого нет, поэтому молча выходим:
        /// один и тот же билдер работает в обеих сценах.
        /// </summary>
        private static void WireStopwatch(Transform root, MinigameAudioPlayer player)
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
            AssignClip(directorSerialized, "tickClip", "tick");
            AssignClip(directorSerialized, "roarClip", "bear_roar");
            AssignClip(directorSerialized, "crowdClip", "crowd_ambience");
            directorSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignClip(SerializedObject serialized, string field, string id)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                return;
            }

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipFolder + id + ".wav");
            if (clip == null)
            {
                Debug.LogWarning($"CircusSfx: клип «{id}» не найден, {field} остаётся заглушкой.");
                return;
            }

            property.objectReferenceValue = clip;
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
