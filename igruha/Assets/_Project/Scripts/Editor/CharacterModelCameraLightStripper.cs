using UnityEditor;

namespace Igruha.EditorTools
{
    /// <summary>
    /// FBX персонажей, экспортированные из Blender/Tripo, иногда несут с собой
    /// служебные ноды Camera/Light из вьюпорта экспорта. Раньше с этим боролись
    /// точечно — руками выключали Import Cameras/Import Lights в инспекторе на
    /// каждом файле каждого персонажа, и часть файлов (весь Shlanga, часть Fat)
    /// оставалась непочищенной: лишняя Camera на сцене перехватывала рендер у
    /// CinemachineBrain, ломая вид после выбора именно этого персонажа.
    ///
    /// Это — не точечный фикс, а автопостпроцессор: Unity вызывает его на КАЖДОМ
    /// импорте и реимпорте ЛЮБОГО FBX в проекте, включая ещё не сделанных
    /// персонажей. Отдельно настраивать новый FBX не нужно — оба флага гасятся
    /// сами.
    /// </summary>
    internal sealed class CharacterModelCameraLightStripper : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetImporter is not ModelImporter importer)
            {
                return;
            }

            importer.importCameras = false;
            importer.importLights = false;
        }
    }
}
