using UnityEditor;
using UnityEngine;
using Igruha.Core.Audio;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Общие шаги постановки звука в сцену — фаза 5.
    ///
    /// Каждая игра ставит звук одинаково: объект-хозяин, проигрыватель с
    /// библиотекой, поверх — общие библиотеки. Отличается только то, на какие
    /// события игра вешается. Эти четыре шага и собраны здесь, чтобы у пяти
    /// проходов не оказалось пяти разных представлений о том, сколько
    /// проигрывателю голосов и как далеко слышно арену.
    ///
    /// Все операции повторимы: второй запуск ничего не дублирует.
    /// </summary>
    internal static class SfxHost
    {
        /// <summary>
        /// Голосов проигрывателю арены. Восемь игроков и их события с запасом:
        /// столько же ставит себе <see cref="MinigameAudioPlayer"/> по умолчанию,
        /// и менять это число под конкретную игру поводов не было.
        /// </summary>
        private const int ArenaVoices = 16;

        /// <summary>Дочерний объект с этим именем: найти или создать.</summary>
        internal static Transform Ensure(Transform parent, string name)
        {
            Transform found = parent.Find(name);
            if (found != null) return found;

            var created = new GameObject(name);
            created.transform.SetParent(parent, worldPositionStays: false);
            created.transform.localPosition = Vector3.zero;
            return created.transform;
        }

        /// <summary>Компонент на объекте: найти или добавить.</summary>
        internal static T Ensure<T>(GameObject host) where T : Component
        {
            T found = host.GetComponent<T>();
            return found != null ? found : host.AddComponent<T>();
        }

        /// <summary>
        /// Проигрыватель арены с библиотекой игры и общими библиотеками поверх.
        ///
        /// Порядок важен: слот ищется сперва в своей библиотеке, и только потом
        /// в общих. Так игра может перебить общий звук своим, не трогая Core.
        /// </summary>
        internal static MinigameAudioPlayer EnsurePlayer(GameObject host, MinigameSfxLibrary library, params string[] extraLibraryPaths)
        {
            MinigameAudioPlayer player = Ensure<MinigameAudioPlayer>(host);

            var serialized = new SerializedObject(player);
            serialized.FindProperty("library").objectReferenceValue = library;
            serialized.FindProperty("voices").intValue = ArenaVoices;

            SerializedProperty extras = serialized.FindProperty("extraLibraries");
            extras.arraySize = 0;
            foreach (string path in extraLibraryPaths)
            {
                var extra = AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>(path);
                if (extra == null)
                {
                    Debug.LogWarning($"[Звук] Общей библиотеки нет: {path}");
                    continue;
                }

                extras.arraySize++;
                extras.GetArrayElementAtIndex(extras.arraySize - 1).objectReferenceValue = extra;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return player;
        }

        /// <summary>
        /// Сводка библиотеки для лога: сколько слотов и сколько из них немых.
        ///
        /// Немой слот ошибкой не считается — он значит «звук ещё не приехал», —
        /// но видеть их число после каждого прохода надо: именно оно говорит,
        /// закрыта фаза по игре или нет.
        /// </summary>
        internal static string Describe(MinigameSfxLibrary library)
        {
            if (library == null) return "библиотеки нет";

            int mute = 0;
            foreach (MinigameSfxLibrary.Entry entry in library.Entries)
            {
                bool hasClip = entry.Clip != null || (entry.Variants != null && entry.Variants.Length > 0);
                if (!hasClip) mute++;
            }

            return $"слотов {library.Entries.Count}, немых {mute}";
        }
    }
}
