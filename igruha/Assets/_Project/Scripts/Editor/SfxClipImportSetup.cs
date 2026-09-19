using System.IO;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Ставит настройки импорта клипам поставки звука — фаза 5.
    ///
    /// Три требования паспорта поставки, каждое со своей ценой при нарушении:
    ///
    /// <list type="bullet">
    /// <item><b>Force To Mono</b> — стерео-клип Unity не спатиализует вовсе. Шаг
    /// соседа звучал бы ровно по центру головы независимо от того, где сосед.</item>
    /// <item><b>Streaming для лупов</b> — тридцатисекундный фон зала в
    /// Decompress On Load разворачивается в память целиком при загрузке сцены.</item>
    /// <item><b>Decompress On Load для коротких</b> — шаг, наоборот, обязан
    /// начаться в тот же кадр, в который случилось событие.</item>
    /// </list>
    ///
    /// Это автопостпроцессор, а не разовая правка галочек: Unity зовёт его на
    /// каждом импорте и реимпорте, поэтому настройки переживают переустановку
    /// пакета, смену ветки и следующую поставку — новые файлы встают правильно
    /// сами. См. парные <see cref="CharacterTextureImportSetup"/> и
    /// <see cref="CharacterClipImportSetup"/>.
    ///
    /// <b>Почему только файлы с префиксом <c>SFX_</c>.</b> Рядом в тех же папках
    /// лежат клипы прежней генерации fal.ai — стерео и под своими именами
    /// (<c>splash.wav</c>, <c>mine_blast.wav</c>). Они уже приняты на слух в своих
    /// играх, и сводить их в моно задним числом — это менять звук работающих
    /// мини-игр мимо их владельца. Префикс поставки отделяет одно от другого.
    /// </summary>
    internal sealed class SfxClipImportSetup : AssetPostprocessor
    {
        /// <summary>Папка, ниже которой живёт весь звук проекта.</summary>
        private const string AudioRoot = "Assets/_Project/Audio/";

        /// <summary>Префикс имени у файлов поставки. См. пояснение в описании класса.</summary>
        private const string DeliveryPrefix = "SFX_";

        /// <summary>Часть имени, по которой слот опознаётся как зацикленный: <c>..._Loop_01.wav</c>.</summary>
        private const string LoopMarker = "_Loop";

        /// <summary>
        /// Интерфейс звучит ровно по центру у всех и от позиции не зависит,
        /// так что сводить его в моно незачем — стерео тут пригодится будущим
        /// стингерам заставки и результатов.
        /// </summary>
        private const string InterfaceFolder = AudioRoot + "Core/UI/";

        private void OnPreprocessAudio()
        {
            if (assetImporter is not AudioImporter importer) return;

            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(AudioRoot)) return;

            string fileName = Path.GetFileNameWithoutExtension(path);
            if (!fileName.StartsWith(DeliveryPrefix)) return;

            importer.forceToMono = !path.StartsWith(InterfaceFolder);

            bool looping = fileName.Contains(LoopMarker);
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = looping
                ? AudioClipLoadType.Streaming
                : AudioClipLoadType.DecompressOnLoad;

            // Луп грузится фоном и данные заранее не держит: он длинный и начинается
            // не в кадре события, а вместе со сценой. Короткому наоборот нужны
            // готовые данные — иначе первый шаг в раунде опоздает на подгрузку.
            settings.preloadAudioData = !looping;
            importer.defaultSampleSettings = settings;

            importer.loadInBackground = looping;
        }
    }
}
