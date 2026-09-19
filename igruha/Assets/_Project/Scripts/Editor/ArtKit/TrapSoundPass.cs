using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Igruha.Core.Audio;
using Igruha.Core.Traps;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Озвучивает ловушки открытой сцены — фаза 5.
    ///
    /// Один проход вместо ручной расстановки, по той же причине, что и у звука
    /// интерфейса: ловушки ставит билдер арены, и всё, что повешено на них
    /// мышью, исчезает при первой же пересборке.
    ///
    /// Что делает: заводит в сцене проигрыватель с библиотекой <c>Core/Traps</c>
    /// и вешает <see cref="TrapAudio"/> на каждую ловушку, подбирая ей рычаг
    /// по ссылке самого рычага.
    ///
    /// Чего не делает: не озвучивает пружины и падающие ящики — своих звуков у
    /// них поставка не привезла, и общий рычаг им не подходит. Такая ловушка
    /// остаётся немой осознанно, а не по недосмотру.
    /// </summary>
    internal static class TrapSoundPass
    {
        private const string TrapLibraryPath = "Assets/_Project/Audio/Core/Traps/SfxLibrary.asset";
        private const string HostName = "TrapAudio";

        /// <summary>Голосов ловушкам. Хлопок, провал и рычаг разом — это уже редкость, четырёх хватает.</summary>
        private const int TrapVoices = 4;

        [MenuItem("Igruha/Звук/Озвучить ловушки сцены")]
        internal static void Run()
        {
            var library = AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>(TrapLibraryPath);
            if (library == null)
            {
                Debug.LogError($"[Звук] Нет библиотеки ловушек: {TrapLibraryPath}. "
                               + "Сперва «Igruha/Арт/Собрать все библиотеки звука».");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            var traps = new List<TrapBase>();
            var levers = new List<TrapLever>();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                traps.AddRange(root.GetComponentsInChildren<TrapBase>(includeInactive: true));
                levers.AddRange(root.GetComponentsInChildren<TrapLever>(includeInactive: true));
            }

            // Пружина и падающий ящик озвучиваются не рычагом, дверью и не полом,
            // а своим звуком, которого поставка не привезла. Вешать на них
            // TrapAudio незачем: он молчал бы всеми тремя слотами.
            var voiceable = new List<TrapBase>();
            foreach (TrapBase trap in traps)
            {
                if (trap is DoorTrap || trap is CollapsingFloorTrap || FindLever(levers, trap) != null) voiceable.Add(trap);
            }

            if (voiceable.Count == 0)
            {
                Debug.Log($"[Звук] В сцене «{scene.name}» нечего озвучивать: ловушек {traps.Count}, "
                          + "ни одна не дверь, не провал и не на рычаге.");
                return;
            }

            MinigameAudioPlayer player = EnsureHost(scene, library);

            foreach (TrapBase trap in voiceable)
            {
                TrapAudio audio = trap.GetComponent<TrapAudio>() ?? Undo.AddComponent<TrapAudio>(trap.gameObject);

                var serialized = new SerializedObject(audio);
                serialized.FindProperty("audioPlayer").objectReferenceValue = player;
                serialized.FindProperty("trap").objectReferenceValue = trap;
                serialized.FindProperty("lever").objectReferenceValue = FindLever(levers, trap);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Звук] Ловушки сцены «{scene.name}» озвучены: {voiceable.Count} из {traps.Count}. Сцену сохранить вручную.");
        }

        /// <summary>
        /// Рычаг этой ловушки — по ссылке самого рычага.
        ///
        /// Обратной ссылки у ловушки нет и заводить её ради звука не нужно:
        /// одна ловушка может стоять без рычага вовсе (её дёргает кнопка или
        /// таймер), и тогда пустое поле — правильный ответ.
        /// </summary>
        private static TrapLever FindLever(List<TrapLever> levers, TrapBase trap)
        {
            foreach (TrapLever lever in levers)
            {
                var serialized = new SerializedObject(lever);
                if (serialized.FindProperty("trap").objectReferenceValue == trap) return lever;
            }

            return null;
        }

        /// <summary>Проигрыватель ловушек: один на сцену, с библиотекой Core/Traps.</summary>
        private static MinigameAudioPlayer EnsureHost(Scene scene, MinigameSfxLibrary library)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != HostName) continue;
                return SfxHost.EnsurePlayer(root, library);
            }

            var host = new GameObject(HostName);
            Undo.RegisterCreatedObjectUndo(host, "Звук ловушек");
            MinigameAudioPlayer player = SfxHost.EnsurePlayer(host, library);

            var serialized = new SerializedObject(player);
            serialized.FindProperty("voices").intValue = TrapVoices;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return player;
        }
    }
}
