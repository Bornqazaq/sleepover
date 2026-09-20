using System;
using UnityEngine;

namespace Igruha.Core.Audio
{
    /// <summary>
    /// Какая тема играет в какой сцене. Один ассет на всю игру.
    ///
    /// Ключ — имя сцены, а не ссылка на мини-игру: так тема цепляется к хабу и
    /// к чужим сценам тоже, а <see cref="MusicPlayer"/> обходится без знания о
    /// том, что такое мини-игра. Главное следствие — <b>ни одна сцена не
    /// правится ради музыки</b>: сцену Duck Hunt ведёт напарник, а темы всё
    /// равно расставлены.
    ///
    /// Сцена без записи — это тишина, а не ошибка: часть игр живёт своим
    /// звуковым слоем (гул цеха, шум шатра), и подкладывать туда тему нельзя
    /// без решения владельца игры.
    /// </summary>
    [CreateAssetMenu(fileName = "MusicLibrary", menuName = "Igruha/Music Library")]
    public sealed class MusicLibrary : ScriptableObject
    {
        /// <summary>Тема одной сцены.</summary>
        [Serializable]
        public struct Entry
        {
            [Tooltip("Имя сцены как в Build Settings: Hub, Stopwatch, BelieveOrNot")]
            public string Scene;

            [Tooltip("Трек из _Project/Audio/Music/")]
            public AudioClip Clip;

            [Tooltip("Громкость темы относительно общей громкости музыки — баланс между треками")]
            [Range(0f, 1f)] public float Volume;
        }

        [Tooltip("Темы по сценам. Собирается пунктом меню Igruha/Арт/Собрать библиотеку музыки")]
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        /// <summary>Записи как есть — для редакторного сборщика и проверок.</summary>
        public Entry[] Entries => entries;

        /// <summary>Тема сцены. Ложь — для этой сцены темы нет, и это нормально.</summary>
        public bool TryGet(string scene, out AudioClip clip, out float volume)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (!string.Equals(entries[i].Scene, scene, StringComparison.OrdinalIgnoreCase)) continue;
                if (entries[i].Clip == null) break;

                clip = entries[i].Clip;
                volume = Mathf.Clamp01(entries[i].Volume <= 0f ? 1f : entries[i].Volume);
                return true;
            }

            clip = null;
            volume = 0f;
            return false;
        }

#if UNITY_EDITOR
        /// <summary>Заполнить библиотеку. Зовёт только редакторный сборщик.</summary>
        public void SetEntries(Entry[] value) => entries = value ?? Array.Empty<Entry>();
#endif
    }
}
