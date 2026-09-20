using UnityEditor;
using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Player;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Ставит звук персонажа на базовый префаб — фаза 5.
    ///
    /// Ставится ровно на <c>Player.prefab</c>, потому что восемь персонажей —
    /// его варианты: добавленное здесь приезжает всем восьмерым и остаётся
    /// одним местом правки. Тот же приём, которым на базовый префаб поставлено
    /// колесо эмоций (<c>CharacterPrefabBuilder</c>).
    ///
    /// <b>Что именно трогается в замороженном префабе.</b> Только добавляются три
    /// компонента звука. Габариты капсулы, рост, аниматор, клипы, ростер и привязки
    /// клавиш не читаются и не пишутся — раздел 0 правил проекта остаётся в силе.
    ///
    /// Операция повторима: второй запуск ничего не дублирует, а недостающие
    /// ссылки доставляет. Это важнее, чем кажется: префаб пересобирается
    /// билдером персонажей, и звук должен возвращаться одной командой.
    /// </summary>
    internal static class CharacterAudioBuilder
    {
        private const string BasePrefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";
        private const string CharacterLibraryPath = "Assets/_Project/Audio/Core/Character/SfxLibrary.asset";

        /// <summary>
        /// Голосов на персонажа. Одновременно звучащих у одного тела мало: шаг,
        /// приземление и полученный удар. Шестнадцать, как у арены, здесь были бы
        /// восемью персонажами по шестнадцать источников на пустом месте.
        /// </summary>
        private const int CharacterVoices = 6;

        /// <summary>
        /// Дальше этого шаги соседа не слышны, м. Меньше, чем у арены: шаг —
        /// звук ближнего круга, и на сорока метрах восемь бегущих сливаются в гул.
        /// </summary>
        private const float CharacterHearingRange = 22f;

        [MenuItem("Igruha/Арт/Поставить звук персонажу")]
        internal static void Build()
        {
            var library = AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>(CharacterLibraryPath);
            if (library == null)
            {
                Debug.LogError($"[Звук] Нет библиотеки персонажа: {CharacterLibraryPath}. "
                               + "Сперва Igruha/Арт/Собрать все библиотеки звука.");
                return;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(BasePrefabPath);
            try
            {
                var controller = contents.GetComponent<PlayerController>();
                if (controller == null)
                {
                    Debug.LogError($"[Звук] На {BasePrefabPath} нет PlayerController — это не префаб персонажа.");
                    return;
                }

                MinigameAudioPlayer player = Ensure<MinigameAudioPlayer>(contents);
                var playerFields = new SerializedObject(player);
                playerFields.FindProperty("library").objectReferenceValue = library;
                playerFields.FindProperty("voices").intValue = CharacterVoices;
                playerFields.FindProperty("maxDistance").floatValue = CharacterHearingRange;
                playerFields.ApplyModifiedPropertiesWithoutUndo();

                Link(Ensure<CharacterAudio>(contents), player, controller);
                Link(Ensure<CharacterFootsteps>(contents), player, controller);

                PrefabUtility.SaveAsPrefabAsset(contents, BasePrefabPath);
                Debug.Log($"[Звук] Звук персонажа поставлен на {BasePrefabPath}: "
                          + $"проигрыватель, шаги, события контроллера. Слотов в библиотеке: {library.Entries.Count}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static T Ensure<T>(GameObject target) where T : Component
        {
            T existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }

        /// <summary>Связывает компонент звука с проигрывателем и контроллером того же тела.</summary>
        private static void Link(Component component, MinigameAudioPlayer player, PlayerController controller)
        {
            var fields = new SerializedObject(component);
            fields.FindProperty("audioPlayer").objectReferenceValue = player;
            fields.FindProperty("controller").objectReferenceValue = controller;
            fields.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
