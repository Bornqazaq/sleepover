using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Igruha.Core.Audio;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Озвучивает интерфейс открытой сцены — фаза 5.
    ///
    /// Один проход вместо ручной расстановки: экраны проекта собираются
    /// редакторными билдерами (<c>CharacterSelectBuilder</c>, <c>PauseMenuView</c>
    /// и прочие), кнопок в сцене десятки, а пересборка экрана сносит всё, что
    /// поставлено мышью. Проход повторим: второй запуск ничего не дублирует,
    /// поэтому его можно гонять после каждой пересборки интерфейса.
    ///
    /// Что делает: заводит в сцене объект звука интерфейса с библиотекой
    /// <c>Core/UI</c> и вешает <see cref="UiButtonSound"/> на каждую кнопку.
    /// Чего не делает: не различает подтверждение и отказ — «Назад» и серые
    /// кнопки получают обычный щелчок, и слот им меняют осознанно, по экрану.
    /// </summary>
    internal static class UiSoundPass
    {
        private const string UiLibraryPath = "Assets/_Project/Audio/Core/UI/SfxLibrary.asset";
        private const string HostName = "UiAudio";

        /// <summary>Голосов интерфейсу. Щелчки короткие и в очередь не выстраиваются — хватает четырёх.</summary>
        private const int UiVoices = 4;

        [MenuItem("Igruha/Арт/Озвучить интерфейс сцены")]
        internal static void Run()
        {
            var library = AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>(UiLibraryPath);
            if (library == null)
            {
                Debug.LogError($"[Звук] Нет библиотеки интерфейса: {UiLibraryPath}. "
                               + "Сперва Igruha/Арт/Собрать все библиотеки звука.");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            EnsureHost(scene, library);

            int added = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Button button in root.GetComponentsInChildren<Button>(includeInactive: true))
                {
                    if (button.GetComponent<UiButtonSound>() != null) continue;
                    Undo.AddComponent<UiButtonSound>(button.gameObject);
                    added++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Звук] Интерфейс сцены «{scene.name}» озвучен: кнопок добавлено {added}. "
                      + "Сцену сохранить вручную.");
        }

        /// <summary>Объект звука интерфейса: один на сцену, с проигрывателем и библиотекой Core/UI.</summary>
        private static void EnsureHost(Scene scene, MinigameSfxLibrary library)
        {
            UiAudio existing = FindHost(scene);
            GameObject host = existing != null ? existing.gameObject : new GameObject(HostName);
            if (existing == null) Undo.RegisterCreatedObjectUndo(host, "Звук интерфейса");

            MinigameAudioPlayer player = host.GetComponent<MinigameAudioPlayer>()
                                         ?? Undo.AddComponent<MinigameAudioPlayer>(host);

            var fields = new SerializedObject(player);
            fields.FindProperty("library").objectReferenceValue = library;
            fields.FindProperty("voices").intValue = UiVoices;
            fields.ApplyModifiedPropertiesWithoutUndo();

            if (host.GetComponent<UiAudio>() == null) Undo.AddComponent<UiAudio>(host);
        }

        private static UiAudio FindHost(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                UiAudio found = root.GetComponentInChildren<UiAudio>(includeInactive: true);
                if (found != null) return found;
            }

            return null;
        }
    }
}
