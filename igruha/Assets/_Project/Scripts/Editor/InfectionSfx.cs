using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Minigames.Infection;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Постановка звука «Заражения» — фаза 5.
    ///
    /// Поставка привезла игре два слота, и оба ложатся в то, что в сцене уже
    /// есть: всплеск краски — в источник всплеска на <see cref="InfectionPaintEffects"/>,
    /// шаг заражённого — в общий слой через <see cref="InfectionPresentation"/>.
    /// Своего проигрывателя арене не заводится: арене пока нечего играть, а
    /// пустой проигрыватель в сцене выглядел бы как готовый звук.
    ///
    /// <b>Капающий след отключается.</b> Он был вторым слоем шага у покрашенных,
    /// и вместе с приехавшим шагом заражённого их стало бы два на один шаг.
    /// Частицы следа при этом остаются: отключается только клип.
    ///
    /// Проход повторим: второй запуск ничего не дублирует.
    /// </summary>
    internal static class InfectionSfx
    {
        private const string LibraryPath = "Assets/_Project/Audio/Infection/SfxLibrary.asset";
        private const string SplatSlot = "SFX_INFC_Infect_Splat";

        [MenuItem("Igruha/Звук/Поставить звук «Заражения»")]
        internal static void Build()
        {
            var library = AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>(LibraryPath);
            if (library == null)
            {
                Debug.LogError($"[Звук] Нет библиотеки: {LibraryPath}. Сперва «Igruha/Арт/Собрать все библиотеки звука».");
                return;
            }

            var presentation = Object.FindFirstObjectByType<InfectionPresentation>(FindObjectsInactive.Include);
            if (presentation == null)
            {
                Debug.LogError("[Звук] В сцене нет InfectionPresentation — открой Scenes/Minigames/Infection.unity.");
                return;
            }

            var serialized = new SerializedObject(presentation);
            serialized.FindProperty("library").objectReferenceValue = library;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var template = serialized.FindProperty("template").objectReferenceValue as InfectionPaintEffects;
            if (template == null)
            {
                Debug.LogWarning("[Звук] У InfectionPresentation не задан шаблон всплеска — всплеск краски останется прежним.");
            }
            else
            {
                var effects = new SerializedObject(template);
                effects.FindProperty("splat").objectReferenceValue = FindClip(library, SplatSlot);
                effects.FindProperty("wetStep").objectReferenceValue = null;
                effects.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(presentation.gameObject.scene);
            Debug.Log($"[Звук] «Заражение» озвучено: {SfxHost.Describe(library)}");
        }

        private static AudioClip FindClip(MinigameSfxLibrary library, string id)
        {
            if (!library.TryGet(id, out MinigameSfxLibrary.Entry entry))
            {
                Debug.LogWarning($"[Звук] В библиотеке «Заражения» нет слота «{id}».");
                return null;
            }

            if (entry.Variants != null && entry.Variants.Length > 0) return entry.Variants[0];
            return entry.Clip;
        }
    }
}
