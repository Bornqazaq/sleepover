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
    /// <b>Почему только файлы с префиксом <c>SFX_</c>.</b> Префикс носят файлы
    /// поставки, и правила импорта взяты из её паспорта — для чужого файла они
    /// были бы догадкой. Рядом ещё лежит синтезированная питоном подложка
    /// «Заражения» (<c>Wind.wav</c>, <c>Splat.wav</c>), сделанная не по этому
    /// паспорту: сводить её в моно задним числом значило бы менять звук
    /// работающей мини-игры мимо её владельца.
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
