using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Полный список персонажей игры (GDD: 8 слотов). Часть слотов может быть
    /// не заполнена (нет префаба) — заготовка под персонажа, который ещё
    /// не готов; UI выбора показывает такие слоты задизейбленными.
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterRoster", menuName = "Igruha/Character Roster")]
    public sealed class CharacterRoster : ScriptableObject
    {
        [SerializeField] private CharacterDefinition[] characters = System.Array.Empty<CharacterDefinition>();

        public IReadOnlyList<CharacterDefinition> Characters => characters;
    }
}
