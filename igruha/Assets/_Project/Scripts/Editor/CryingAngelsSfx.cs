using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Minigames.CryingAngels;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Постановка звука «Плачущих ангелов» — фаза 5.
    ///
    /// Заводит проигрыватель арены с библиотекой игры и общим слоем персонажа,
    /// вешает <see cref="CryingAngelsAudio"/> и проставляет ему ссылки на сцену
    /// и скример.
    ///
    /// <b>Звук живёт на MinigameManager, а не под <c>_Arena</c>.</b> Арену
    /// пересобирает билдер, снося корень целиком, и поставленный под ним звук
    /// исчез бы на первой же пересборке — молча, как исчезали ссылки на клипы
    /// у медведя. Контроллер раунда не пересоздаётся никогда.
    ///
    /// Проход повторим: второй запуск ничего не дублирует и доставляет
    /// недостающие ссылки.
    /// </summary>
    internal static class CryingAngelsSfx
    {
        private const string LibraryPath = "Assets/_Project/Audio/CryingAngels/SfxLibrary.asset";
        private const string CharacterLibraryPath = "Assets/_Project/Audio/Core/Character/SfxLibrary.asset";
        private const string HostName = "Audio";

        [MenuItem("Igruha/Звук/Поставить звук «Плачущих ангелов»")]
        internal static void Build()
        {
            var library = AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>(LibraryPath);
            if (library == null)
            {
                Debug.LogError($"[Звук] Нет библиотеки: {LibraryPath}. Сперва «Igruha/Арт/Собрать все библиотеки звука».");
                return;
            }

            var game = Object.FindFirstObjectByType<CryingAngelsMinigame>(FindObjectsInactive.Include);
            if (game == null)
            {
                Debug.LogError("[Звук] В сцене нет CryingAngelsMinigame — открой Scenes/Minigames/CryingAngels.unity.");
                return;
            }

            Transform host = SfxHost.Ensure(game.transform, HostName);
            MinigameAudioPlayer player = SfxHost.EnsurePlayer(host.gameObject, library, CharacterLibraryPath);

            CryingAngelsAudio audio = SfxHost.Ensure<CryingAngelsAudio>(host.gameObject);
            var serialized = new SerializedObject(audio);
            serialized.FindProperty("audioPlayer").objectReferenceValue = player;
            serialized.FindProperty("game").objectReferenceValue = game;
            serialized.FindProperty("config").objectReferenceValue = game.Config;
            serialized.FindProperty("library").objectReferenceValue = library;
            serialized.FindProperty("screamer").objectReferenceValue =
                Object.FindFirstObjectByType<KeeperScreamer>(FindObjectsInactive.Include);
            serialized.FindProperty("caughtFeedback").objectReferenceValue =
                Object.FindFirstObjectByType<BeamCaughtFeedback>(FindObjectsInactive.Include);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
            Debug.Log($"[Звук] «Плачущие ангелы» озвучены: {SfxHost.Describe(library)}");
        }
    }
}
