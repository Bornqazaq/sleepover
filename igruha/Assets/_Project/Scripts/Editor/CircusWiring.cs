using UnityEditor;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Minigames.Circus;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Возврат ссылок контроллеру после пересборки арены.
    ///
    /// 🔴 <b>Зачем это вообще понадобилось.</b> У контроллера обеих игр есть
    /// два поля, указывающих на объекты сцены, которые пересборка пересоздаёт:
    /// массив <c>cages</c> и <c>bear</c>. Ссылки в сцене — это <c>fileID</c>
    /// конкретных объектов; <see cref="CircusArenaBuilder"/> сносит клетки
    /// и медведя и делает новых, с новыми идентификаторами, — и массив
    /// молча превращается в восемь пустых ячеек.
    ///
    /// Поймано 04.09 плей-мод прогоном: игра встала с «клетка 0 не назначена».
    /// До этого поля переживали пересборки только потому, что их каждый раз
    /// набивали в инспекторе руками, и никто не замечал, что это ручная
    /// работа после каждого нажатия пункта меню.
    ///
    /// Это тот же класс поломки, что уже стоил проекту дважды: всё, что не
    /// восстанавливается пересборкой, теряется. Поэтому ссылки ставятся кодом,
    /// а инспектор перестаёт быть местом, где живёт правда.
    ///
    /// <b>Обе игры чинятся одним местом:</b> у <c>StopwatchMinigame</c>
    /// и <c>CansOrderMinigame</c> поля называются одинаково, а искать контроллер
    /// по базовому типу можно без ссылки на конкретную игру — значит билдер
    /// арены остаётся общим и ничего про них не знает.
    /// </summary>
    internal static class CircusWiring
    {
        private const string CagesField = "cages";
        private const string BearField = "bear";

        internal static void Apply(CageStation[] cages, PitBear bear)
        {
            var controller = Object.FindFirstObjectByType<MinigameControllerBase>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("CircusWiring: контроллер мини-игры не найден — клетки и медведь останутся без ссылок.");
                return;
            }

            var serialized = new SerializedObject(controller);
            SerializedProperty cagesProperty = serialized.FindProperty(CagesField);
            SerializedProperty bearProperty = serialized.FindProperty(BearField);

            if (cagesProperty == null && bearProperty == null)
            {
                // Не цирковая игра — молча выходим: билдер общий.
                return;
            }

            int wired = 0;
            if (cagesProperty != null && cagesProperty.isArray)
            {
                cagesProperty.arraySize = cages.Length;
                for (int i = 0; i < cages.Length; i++)
                {
                    cagesProperty.GetArrayElementAtIndex(i).objectReferenceValue = cages[i];
                    if (cages[i] != null)
                    {
                        wired++;
                    }
                }
            }

            if (bearProperty != null)
            {
                bearProperty.objectReferenceValue = bear;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"CircusWiring: контроллеру «{controller.GetType().Name}» возвращены клетки {wired}/{cages.Length}" +
                      $" и медведь {(bear == null ? "— НЕ НАЙДЕН" : "✓")}.", controller);
        }
    }
}
