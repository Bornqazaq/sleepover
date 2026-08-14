using System;
using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Один слот ростера персонажей: имя + префаб. Слот без префаба — заглушка
    /// под персонажа, который ещё не готов (см. CharacterRoster).
    /// </summary>
    [Serializable]
    public sealed class CharacterDefinition
    {
        [SerializeField] private string displayName = "???";
        [SerializeField] private GameObject prefab;

        public string DisplayName => displayName;
        public GameObject Prefab => prefab;
        public bool IsAvailable => prefab != null;
    }
}
