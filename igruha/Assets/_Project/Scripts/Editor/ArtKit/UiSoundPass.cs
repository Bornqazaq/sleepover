using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Igruha.Core.Audio;
using Igruha.Core.UI;

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
    /// <c>Core/UI</c>, вешает <see cref="UiButtonSound"/> на каждую кнопку и
    /// разводит кнопки по трём слотам — подтверждение, отмена, выбор персонажа.
    ///
    /// <b>Разводит по роли, а не по внешнему виду.</b> Карточка персонажа узнаётся
    /// по своему компоненту, а «Назад» и «Выход» — по имени объекта: собственного
    /// признака отмены у кнопки нет, а заводить его на весь проект ради звука
    /// значило бы править каждый экран. Ошибка узнавания здесь стоит одного
    /// неверного щелчка, и слот правится в инспекторе.
    ///
    /// Чего не делает: не озвучивает отказ на серой кнопке — <c>onClick</c> у
    /// выключенной кнопки не поднимается вовсе, и зовёт этот звук сам экран
    /// (<see cref="UiButtonSound.PlayBlocked"/>).
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

            int voiced = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Button button in root.GetComponentsInChildren<Button>(includeInactive: true))
                {
                    UiButtonSound sound = button.GetComponent<UiButtonSound>()
                                          ?? Undo.AddComponent<UiButtonSound>(button.gameObject);

                    var fields = new SerializedObject(sound);
                    fields.FindProperty("clickSlot").stringValue = SlotFor(button);
                    fields.ApplyModifiedPropertiesWithoutUndo();
                    voiced++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Звук] Интерфейс сцены «{scene.name}» озвучен: кнопок {voiced}. "
                      + "Сцену сохранить вручную.");
        }

        /// <summary>Слова, по которым кнопка узнаётся как отмена. Только нижний регистр — сравнение идёт по нему.</summary>
        private static readonly string[] BackWords = { "назад", "back", "выход", "exit", "отмена", "cancel", "закрыть", "close" };

        /// <summary>
        /// Чем звучит нажатие этой кнопки. Карточка персонажа — своим звуком
        /// выбора, кнопка отмены — отменой, всё остальное — обычным щелчком.
        /// </summary>
        private static string SlotFor(Button button)
        {
            if (button.GetComponent<CharacterSlotButton>() != null) return CoreSfx.UiCharSelect;

            string name = button.gameObject.name.ToLowerInvariant();
            foreach (string word in BackWords)
            {
                if (name.Contains(word)) return CoreSfx.UiBack;
            }

            return CoreSfx.UiClick;
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
