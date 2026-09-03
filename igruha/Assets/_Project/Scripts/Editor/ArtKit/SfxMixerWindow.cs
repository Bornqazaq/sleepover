using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Igruha.Core.Audio;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Пульт звука — крутить громкости слотов ползунками и тут же слушать результат.
    ///
    /// <b>Зачем окно, если громкости лежат в ассете.</b> Инспектор ScriptableObject
    /// даёт числа, но не даёт услышать: чтобы сравнить два уровня, приходилось
    /// править число, собирать библиотеку, входить в плей-мод и играть раунд до
    /// нужного события. Круг занимал минуты, а решение принимается ухом за секунды.
    ///
    /// <b>Слушать можно на фактической громкости слота, а не «как в файле».</b>
    /// Превью редактора громкость не умеет, поэтому клип пересобирается копией
    /// с умноженными сэмплами. Это не приблизительно — это ровно то, что услышит
    /// игрок, если у звука нет затухания по расстоянию.
    ///
    /// <b>Главное здесь — «сыграть вместе».</b> Разбор 03.09 показал, что жалобы
    /// на звук почти никогда не про один слот: рёв публики «не слышно» не потому,
    /// что он тихий (он был самым громким файлом набора), а потому что он один
    /// против четырёх дорожек ударов и всплесков. Одиночное прослушивание такое
    /// не показывает вовсе. Здесь можно отметить несколько слотов и услышать их
    /// разом, как в игре.
    ///
    /// Колонка «уровень» — громкость слота с поправкой на сам клип (RMS + громкость
    /// в дБ). Она отвечает на вопрос, почему два слота с одинаковым ползунком
    /// слышны по-разному: пиковая нормализация выравнивает файлы, а не громкость.
    /// </summary>
    internal sealed class SfxMixerWindow : EditorWindow
    {
        /// <summary>Сколько секунд клипа берём в превью. Тему раунда на минуту слушать целиком незачем.</summary>
        private const float PreviewSeconds = 8f;

        /// <summary>Столько сэмплов хватает, чтобы посчитать уровень: слушаем характер, а не хвост.</summary>
        private const int LevelSampleWindow = 220_500;

        /// <summary>Диапазон шкалы уровня, дБ. Ниже −40 разница на слух уже не читается.</summary>
        private const float LevelFloorDb = -40f;

        /// <summary>Ширина колонок таблицы, px.</summary>
        private const float IdWidth = 170f;
        private const float PlayWidth = 26f;
        private const float ValueWidth = 46f;
        private const float LevelWidth = 130f;
        private const float ResetWidth = 24f;

        [MenuItem("Igruha/Арт/Пульт звука")]
        private static void Open()
        {
            var window = GetWindow<SfxMixerWindow>("Пульт звука");
            window.minSize = new Vector2(640f, 320f);
            window.Show();
        }

        private MinigameSfxLibrary library;

        /// <summary>Громкости как они лежат в ассете на момент открытия. Нужны для «откатить» и для пометки правок.</summary>
        private readonly Dictionary<string, float> saved = new Dictionary<string, float>();

        /// <summary>Отмеченные для совместного прослушивания.</summary>
        private readonly HashSet<string> selected = new HashSet<string>();

        /// <summary>Уровень клипа в дБ. Считается один раз: GetData на минутной теме недёшев.</summary>
        private readonly Dictionary<int, float> levelCache = new Dictionary<int, float>();

        private bool playOnRelease = true;
        private Vector2 scroll;
        private string status = string.Empty;

        private void OnEnable()
        {
            if (library == null) library = FindLibraryForOpenScene();
            CaptureSaved();
        }

        private void OnDisable() => StopPreview();

        private void OnGUI()
        {
            DrawToolbar();

            if (library == null)
            {
                EditorGUILayout.HelpBox(
                    "Выбери библиотеку звука мини-игры — Assets/_Project/Audio/<Игра>/SfxLibrary.asset",
                    MessageType.Info);
                return;
            }

            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Плей-мод: правка ползунка слышна на СЛЕДУЮЩЕМ срабатывании звука в игре. " +
                    "Уже звучащий луп не перестраивается.",
                    MessageType.None);
            }

            DrawHeader();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            IReadOnlyList<MinigameSfxLibrary.Entry> entries = library.Entries;
            for (int i = 0; i < entries.Count; i++) DrawRow(i, entries[i]);
            EditorGUILayout.EndScrollView();

            DrawFooter();
        }

        // ========== ШАПКА ==========

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            var picked = (MinigameSfxLibrary)EditorGUILayout.ObjectField(
                library, typeof(MinigameSfxLibrary), false, GUILayout.Width(220f));
            if (picked != library)
            {
                library = picked;
                selected.Clear();
                levelCache.Clear();
                CaptureSaved();
            }

            GUI.enabled = library != null && selected.Count > 0;
            if (GUILayout.Button($"▶ вместе ({selected.Count})", EditorStyles.toolbarButton, GUILayout.Width(110f)))
            {
                PlayTogether();
            }
            GUI.enabled = true;

            if (GUILayout.Button("■", EditorStyles.toolbarButton, GUILayout.Width(24f))) StopPreview();

            GUILayout.FlexibleSpace();
            playOnRelease = GUILayout.Toggle(
                playOnRelease, "играть, отпустив ползунок", EditorStyles.toolbarButton, GUILayout.Width(180f));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label(string.Empty, GUILayout.Width(18f));
            GUILayout.Label("слот", EditorStyles.miniBoldLabel, GUILayout.Width(IdWidth));
            GUILayout.Label(string.Empty, GUILayout.Width(PlayWidth));
            GUILayout.Label("громкость", EditorStyles.miniBoldLabel);
            GUILayout.Label(string.Empty, GUILayout.Width(ValueWidth));
            GUILayout.Label("уровень с клипом", EditorStyles.miniBoldLabel, GUILayout.Width(LevelWidth));
            GUILayout.Label(string.Empty, GUILayout.Width(ResetWidth));
            EditorGUILayout.EndHorizontal();
        }

        // ========== СТРОКА СЛОТА ==========

        private void DrawRow(int index, MinigameSfxLibrary.Entry entry)
        {
            EditorGUILayout.BeginHorizontal();

            bool mark = selected.Contains(entry.Id);
            bool wantMark = GUILayout.Toggle(mark, GUIContent.none, GUILayout.Width(18f));
            if (wantMark != mark)
            {
                if (wantMark) selected.Add(entry.Id);
                else selected.Remove(entry.Id);
            }

            bool changed = saved.TryGetValue(entry.Id, out float was) && !Mathf.Approximately(was, entry.Volume);
            var label = new GUIContent(
                changed ? $"{entry.Id} ●" : entry.Id,
                changed ? $"было {was:0.00}, стало {entry.Volume:0.00}" : entry.Id);
            GUILayout.Label(label, changed ? EditorStyles.boldLabel : EditorStyles.label, GUILayout.Width(IdWidth));

            GUI.enabled = entry.Clip != null;
            if (GUILayout.Button("▶", GUILayout.Width(PlayWidth))) PlayOne(entry);
            GUI.enabled = true;

            EditorGUI.BeginChangeCheck();
            float volume = GUILayout.HorizontalSlider(entry.Volume, 0f, 1f);
            bool released = EditorGUI.EndChangeCheck() == false
                            && Event.current.type == EventType.MouseUp;

            float typed = EditorGUILayout.FloatField(volume, GUILayout.Width(ValueWidth));
            volume = Mathf.Clamp01(typed);

            if (!Mathf.Approximately(volume, entry.Volume))
            {
                SetVolume(index, volume);
                if (playOnRelease && released) PlayOne(library.Entries[index]);
            }

            DrawLevelBar(entry);

            GUI.enabled = changed;
            if (GUILayout.Button("↩", GUILayout.Width(ResetWidth))) SetVolume(index, was);
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Полоса фактического уровня: RMS клипа плюс громкость слота, в дБ.
        /// Именно она объясняет, почему два слота с одинаковым ползунком слышны
        /// по-разному — файлы выровнены по пику, а слышимость определяет RMS.
        /// </summary>
        private void DrawLevelBar(MinigameSfxLibrary.Entry entry)
        {
            Rect rect = GUILayoutUtility.GetRect(LevelWidth, EditorGUIUtility.singleLineHeight, GUILayout.Width(LevelWidth));

            if (entry.Clip == null || entry.Volume <= 0f)
            {
                EditorGUI.LabelField(rect, "—", EditorStyles.miniLabel);
                return;
            }

            float db = ClipLevelDb(entry.Clip) + 20f * Mathf.Log10(entry.Volume);
            float fill = Mathf.InverseLerp(LevelFloorDb, 0f, db);
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.15f));
            var filled = new Rect(rect.x, rect.y + 2f, rect.width * fill, rect.height - 4f);
            EditorGUI.DrawRect(filled, Color.Lerp(new Color(0.2f, 0.5f, 0.9f), new Color(0.9f, 0.3f, 0.4f), fill));
            EditorGUI.LabelField(rect, $"  {db:0.0} дБ", EditorStyles.miniLabel);
        }

        // ========== НИЗ ==========

        private void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal();

            int changed = CountChanged();
            GUI.enabled = changed > 0;
            if (GUILayout.Button($"Сохранить ({changed})")) Save();
            if (GUILayout.Button("Откатить всё")) RevertAll();
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);
        }

        // ========== ДАННЫЕ ==========

        private void CaptureSaved()
        {
            saved.Clear();
            if (library == null) return;
            foreach (MinigameSfxLibrary.Entry e in library.Entries) saved[e.Id] = e.Volume;
        }

        private int CountChanged()
        {
            if (library == null) return 0;
            int n = 0;
            foreach (MinigameSfxLibrary.Entry e in library.Entries)
                if (saved.TryGetValue(e.Id, out float was) && !Mathf.Approximately(was, e.Volume)) n++;
            return n;
        }

        /// <summary>
        /// Пишет громкость в ассет через SerializedObject: правка структуры внутри
        /// массива иначе не помечается грязной и теряется при перезагрузке домена.
        /// </summary>
        private void SetVolume(int index, float volume)
        {
            var so = new SerializedObject(library);
            SerializedProperty entries = so.FindProperty("entries");
            if (index < 0 || index >= entries.arraySize) return;
            entries.GetArrayElementAtIndex(index).FindPropertyRelative("Volume").floatValue = volume;
            so.ApplyModifiedProperties();
        }

        private void RevertAll()
        {
            IReadOnlyList<MinigameSfxLibrary.Entry> entries = library.Entries;
            for (int i = 0; i < entries.Count; i++)
                if (saved.TryGetValue(entries[i].Id, out float was)) SetVolume(i, was);
            status = "Откатил к сохранённому.";
        }

        /// <summary>Пишет громкости и в ассет, и в манифест — иначе сборка библиотеки вернёт старые числа.</summary>
        private void Save()
        {
            AssetDatabase.SaveAssetIfDirty(library);

            string manifest = FindManifest();
            if (manifest == null)
            {
                status = "Ассет сохранён, но манифест не найден: следующая сборка библиотеки вернёт старые громкости.";
                CaptureSaved();
                return;
            }

            int written = WriteManifestVolumes(manifest);
            CaptureSaved();
            status = $"Сохранено. Ассет и манифест {Path.GetFileName(manifest)} — {written} слотов.";
        }

        // ========== МАНИФЕСТ ==========

        /// <summary>Манифест этой игры: тот из docs/art, чей output_folder указывает на папку библиотеки.</summary>
        private string FindManifest()
        {
            string libraryFolder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(library))?.Replace('\\', '/');
            if (string.IsNullOrEmpty(libraryFolder)) return null;

            string artDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "art"));
            if (!Directory.Exists(artDir)) return null;

            foreach (string path in Directory.GetFiles(artDir, "*.json"))
            {
                string text;
                try { text = File.ReadAllText(path); }
                catch (IOException) { continue; }
                if (text.Contains($"\"{libraryFolder}\"")) return path;
            }

            return null;
        }

        /// <summary>
        /// Меняет в манифесте только числа громкостей, точечной заменой по тексту.
        /// Разбирать и писать JSON целиком нельзя: <c>JsonUtility</c> не знает про
        /// prompt, note и model и вычистил бы их — а манифест это и есть техзадание
        /// на генерацию, ради которого он существует.
        /// </summary>
        private int WriteManifestVolumes(string path)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (IOException) { return 0; }

            int written = 0;
            foreach (MinigameSfxLibrary.Entry entry in library.Entries)
            {
                var pattern = new Regex(
                    "(\"id\"\\s*:\\s*\"" + Regex.Escape(entry.Id) + "\"[\\s\\S]*?\"volume\"\\s*:\\s*)([\\d.]+)");
                Match m = pattern.Match(text);
                if (!m.Success) continue;

                string value = entry.Volume.ToString("0.##", CultureInfo.InvariantCulture);
                text = text.Substring(0, m.Groups[2].Index) + value + text.Substring(m.Groups[2].Index + m.Groups[2].Length);
                written++;
            }

            try { File.WriteAllText(path, text); }
            catch (IOException) { return 0; }

            return written;
        }

        // ========== ПРОСЛУШИВАНИЕ ==========

        private void PlayOne(MinigameSfxLibrary.Entry entry)
        {
            if (entry.Clip == null) return;
            AudioClip scaled = Scale(entry.Clip, entry.Volume, $"preview_{entry.Id}");
            if (scaled != null) Preview(scaled);
        }

        /// <summary>
        /// Складывает отмеченные слоты в один клип и играет разом. Ровно этим
        /// проверяется, не тонет ли редкий звук в частых: по одному они все
        /// слышны, вопрос всегда в сумме.
        /// </summary>
        private void PlayTogether()
        {
            var parts = new List<(float[] data, int channels, int frequency)>();
            int longest = 0;
            int frequency = 0;
            int channels = 1;

            foreach (MinigameSfxLibrary.Entry entry in library.Entries)
            {
                if (!selected.Contains(entry.Id) || entry.Clip == null) continue;
                float[] data = Read(entry.Clip, entry.Volume);
                if (data == null) continue;
                parts.Add((data, entry.Clip.channels, entry.Clip.frequency));
                longest = Mathf.Max(longest, data.Length / entry.Clip.channels);
                frequency = Mathf.Max(frequency, entry.Clip.frequency);
                channels = Mathf.Max(channels, entry.Clip.channels);
            }

            if (parts.Count == 0 || longest == 0)
            {
                status = "Нечего играть: у отмеченных слотов нет читаемых клипов.";
                return;
            }

            var mix = new float[longest * channels];
            foreach ((float[] data, int srcChannels, int _) in parts)
            {
                int frames = data.Length / srcChannels;
                for (int f = 0; f < frames; f++)
                for (int c = 0; c < channels; c++)
                {
                    int src = f * srcChannels + Mathf.Min(c, srcChannels - 1);
                    mix[f * channels + c] += data[src];
                }
            }

            // Клипование на сумме — честный признак того, что вместе эти звуки
            // не помещаются: сообщаем, а не прячем нормализацией.
            int clipped = 0;
            for (int i = 0; i < mix.Length; i++)
            {
                if (mix[i] > 1f || mix[i] < -1f) clipped++;
                mix[i] = Mathf.Clamp(mix[i], -1f, 1f);
            }

            AudioClip together = AudioClip.Create("preview_mix", longest, channels, frequency, false);
            together.SetData(mix, 0);
            Preview(together);

            status = clipped > 0
                ? $"Сыграно вместе: {parts.Count}. ⚠️ Сумма клипует на {clipped} сэмплах — вместе эти звуки не помещаются."
                : $"Сыграно вместе: {parts.Count}, без клиппинга.";
        }

        /// <summary>Копия клипа с умноженными сэмплами: превью редактора громкость задавать не умеет.</summary>
        private static AudioClip Scale(AudioClip clip, float volume, string name)
        {
            float[] data = Read(clip, volume);
            if (data == null) return null;

            int frames = data.Length / clip.channels;
            AudioClip copy = AudioClip.Create(name, frames, clip.channels, clip.frequency, false);
            copy.SetData(data, 0);
            return copy;
        }

        /// <summary>Сэмплы клипа, умноженные на громкость, не длиннее <see cref="PreviewSeconds"/>.</summary>
        private static float[] Read(AudioClip clip, float volume)
        {
            int frames = Mathf.Min(clip.samples, Mathf.RoundToInt(clip.frequency * PreviewSeconds));
            if (frames <= 0) return null;

            var data = new float[frames * clip.channels];
            if (!clip.GetData(data, 0)) return null;

            for (int i = 0; i < data.Length; i++) data[i] *= volume;
            return data;
        }

        private float ClipLevelDb(AudioClip clip)
        {
            int key = clip.GetInstanceID();
            if (levelCache.TryGetValue(key, out float cached)) return cached;

            float db = LevelFloorDb;
            int frames = Mathf.Min(clip.samples, LevelSampleWindow);
            if (frames > 0)
            {
                var data = new float[frames * clip.channels];
                if (clip.GetData(data, 0))
                {
                    double sum = 0d;
                    for (int i = 0; i < data.Length; i++) sum += (double)data[i] * data[i];
                    double rms = Math.Sqrt(sum / data.Length);
                    if (rms > 0d) db = (float)(20d * Math.Log10(rms));
                }
            }

            levelCache[key] = db;
            return db;
        }

        // ========== ПРЕВЬЮ РЕДАКТОРА ==========

        private static MethodInfo playMethod;
        private static MethodInfo stopMethod;

        /// <summary>
        /// Превью живёт в UnityEditor.AudioUtil — тип внутренний, и имена методов
        /// от версии к версии менялись. Поэтому рефлексия с запасными именами,
        /// а не прямой вызов.
        /// </summary>
        private static void Preview(AudioClip clip)
        {
            ResolvePreview();
            if (playMethod == null) return;

            StopPreview();
            ParameterInfo[] args = playMethod.GetParameters();
            object[] call = args.Length >= 3
                ? new object[] { clip, 0, false }
                : new object[] { clip };
            playMethod.Invoke(null, call);
        }

        private static void StopPreview()
        {
            ResolvePreview();
            stopMethod?.Invoke(null, null);
        }

        private static void ResolvePreview()
        {
            if (playMethod != null) return;

            Type type = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            if (type == null) return;

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            playMethod = type.GetMethod("PlayPreviewClip", flags) ?? type.GetMethod("PlayClip", flags);
            stopMethod = type.GetMethod("StopAllPreviewClips", flags) ?? type.GetMethod("StopAllClips", flags);
        }

        // ========== ПОИСК БИБЛИОТЕКИ ==========

        /// <summary>Библиотека проигрывателя из открытой сцены — чтобы окно открывалось уже настроенным.</summary>
        private static MinigameSfxLibrary FindLibraryForOpenScene()
        {
            MinigameAudioPlayer player = FindFirstObjectByType<MinigameAudioPlayer>(FindObjectsInactive.Include);
            if (player != null)
            {
                var so = new SerializedObject(player);
                SerializedProperty prop = so.FindProperty("library");
                if (prop != null && prop.objectReferenceValue is MinigameSfxLibrary fromScene) return fromScene;
            }

            string[] guids = AssetDatabase.FindAssets($"t:{nameof(MinigameSfxLibrary)}");
            return guids.Length == 1
                ? AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;
        }
    }
}
