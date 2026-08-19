using System;
using UnityEditor;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Текстуры персонажей приезжают из Tripo/glTF-экспорта пачкой по общей
    /// конвенции имён — base_color(Имя), metallic_roughness(Имя), normal(Имя).
    /// Две из трёх карт хранят не цвет, а данные, и импортировать их как обычную
    /// картинку нельзя: normal без типа Normal Map читается как RGB-цвет
    /// (освещение ломается), а metallic/roughness с гамма-коррекцией sRGB даёт
    /// заведомо неверные значения металличности и гладкости.
    ///
    /// Это — не разовая правка галочек в инспекторе, а автопостпроцессор: Unity
    /// зовёт его на КАЖДОМ импорте и реимпорте, поэтому настройки переживают
    /// реимпорт и работают для будущих персонажей сами, без правки кода —
    /// достаточно положить файлы по той же конвенции имён.
    /// См. парный CharacterModelCameraLightStripper — он делает то же для FBX.
    /// </summary>
    internal sealed class CharacterTextureImportSetup : AssetPostprocessor
    {
        private const string BaseColorPrefix = "base_color(";
        private const string MetallicRoughnessPrefix = "metallic_roughness(";
        private const string NormalPrefix = "normal(";

        /// <summary>
        /// Суффикс карты, которую CharacterPrefabBuilder пересобирает из
        /// glTF-упаковки в ожидаемую URP раскладку каналов. Тоже данные, не цвет.
        /// </summary>
        internal const string MetallicSmoothnessSuffix = "_MetallicSmoothness";

        private void OnPreprocessTexture()
        {
            if (assetImporter is not TextureImporter importer)
            {
                return;
            }

            string fileName = System.IO.Path.GetFileName(assetPath);

            if (StartsWith(fileName, NormalPrefix))
            {
                importer.textureType = TextureImporterType.NormalMap;
                return;
            }

            if (StartsWith(fileName, MetallicRoughnessPrefix) ||
                System.IO.Path.GetFileNameWithoutExtension(assetPath).EndsWith(MetallicSmoothnessSuffix, StringComparison.Ordinal))
            {
                // Карта данных, а не цвета: гамма-коррекция здесь исказила бы
                // и металличность, и гладкость.
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                return;
            }

            if (StartsWith(fileName, BaseColorPrefix))
            {
                // Единственная из трёх, которая действительно цвет.
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
            }
        }

        private static bool StartsWith(string fileName, string prefix)
        {
            return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
