using System;
using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Core.Audio
{
    /// <summary>
    /// Звуковая библиотека одной мини-игры: слот события → клип и то, как он звучит.
    /// Общий слой подфазы 4.5 для всех пятнадцати игр, к конкретной игре не привязан.
    ///
    /// Существует, чтобы игровой код называл звук словом, а не тащил ссылку на файл.
    /// <see cref="MinigameAudioPlayer"/> просит «splash», а какой это клип, насколько
    /// громкий и трёхмерный ли он — решает ассет. Отсюда два следствия: подобрать
    /// звук заново можно, не трогая ни строки кода, а сам код читается как список
    /// событий игры, а не как список файлов.
    ///
    /// Идентификаторы слотов — те же, что в <c>docs/art/&lt;игра&gt;-sfx.json</c>.
    /// Библиотеку не набивают руками: её собирает из манифеста и папки клипов
    /// пункт меню <c>Igruha/Арт/Собрать библиотеку звука</c>. Ручное перетаскивание
    /// дюжины клипов в инспектор — ровно тот шаг, на котором звук уезжал в долг.
    /// </summary>
    [CreateAssetMenu(fileName = "SfxLibrary", menuName = "Igruha/Sfx Library")]
    public sealed class MinigameSfxLibrary : ScriptableObject
    {
        /// <summary>Один слот: звук события и его настройки воспроизведения.</summary>
        [Serializable]
        public struct Entry
        {
            [Tooltip("Идентификатор слота — тот же, что в манифесте docs/art/<игра>-sfx.json")]
            public string Id;

            [Tooltip("Клип из _Project/Audio/<Игра>/")]
            public AudioClip Clip;

            [Tooltip("Громкость слота. Клипы нормализованы по пику, так что здесь — баланс между звуками, а не починка уровня")]
            [Range(0f, 1f)] public float Volume;

            [Tooltip("Зациклен: гул, подводный слой, тема раунда. Такие запускаются StartLoop и глушатся StopLoop")]
            public bool Loop;

            [Tooltip("Трёхмерный: слышен из точки события и тише издалека. Выключено — звучит ровно у всех, как джингл или тема")]
            public bool Spatial;
        }

        [Tooltip("Слоты игры. Собираются из манифеста, руками не набиваются")]
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        /// <summary>Разбор по идентификатору. Строится один раз: поиск идёт из кода событий, иногда по несколько раз за кадр.</summary>
        private Dictionary<string, Entry> byId;

        /// <summary>Слоты как есть — для редакторного сборщика и проверок.</summary>
        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>Находит слот по идентификатору. Возвращает false, если такого слота нет.</summary>
        public bool TryGet(string id, out Entry entry)
        {
            if (byId == null) BuildIndex();
            return byId.TryGetValue(id, out entry);
        }

        /// <summary>Заменяет содержимое библиотеки. Только для редакторного сборщика.</summary>
        public void SetEntries(Entry[] value)
        {
            entries = value ?? Array.Empty<Entry>();
            byId = null;
        }

        private void OnEnable() => byId = null;

        private void OnValidate() => byId = null;

        private void BuildIndex()
        {
            byId = new Dictionary<string, Entry>(entries.Length);
            foreach (Entry e in entries)
            {
                if (string.IsNullOrEmpty(e.Id)) continue;
                byId[e.Id] = e;
            }
        }
    }
}
