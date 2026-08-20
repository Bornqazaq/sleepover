using UnityEngine.SceneManagement;

namespace Igruha.Core.Scenes
{
    /// <summary>
    /// Превращает имя сцены в её путь из Build Settings — в том виде,
    /// в каком сцену опознаёт NGO.
    /// </summary>
    /// <remarks>
    /// Ловушка, ради которой класс существует: NGO проверяет сцену через
    /// <c>SceneUtility.GetBuildIndexByScenePath</c>, а тот принимает только полный
    /// путь с расширением. На «Hub» или «Stopwatch» он отдаёт −1, и
    /// <c>NetworkSceneManager</c> отбивает загрузку как «сцена не добавлена
    /// в Build Settings» — при том что она там есть. Обычный <c>SceneManager</c>
    /// имена принимает, поэтому в одиночном запуске расхождения не видно:
    /// оно вылезает только по сети.
    /// </remarks>
    public static class BuildSceneCatalog
    {
        /// <summary>
        /// Ищет сцену в списке сборки по имени или по готовому пути.
        /// </summary>
        /// <returns><c>false</c>, если сцены в Build Settings нет.</returns>
        public static bool TryResolvePath(string sceneNameOrPath, out string scenePath)
        {
            scenePath = string.Empty;

            if (string.IsNullOrEmpty(sceneNameOrPath))
            {
                return false;
            }

            int count = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < count; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);

                if (string.Equals(path, sceneNameOrPath, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ExtractName(path), sceneNameOrPath, System.StringComparison.OrdinalIgnoreCase))
                {
                    scenePath = path;
                    return true;
                }
            }

            return false;
        }

        private static string ExtractName(string scenePath)
        {
            int slash = scenePath.LastIndexOf('/');
            int start = slash >= 0 ? slash + 1 : 0;
            int dot = scenePath.LastIndexOf('.');
            int end = dot > start ? dot : scenePath.Length;
            return scenePath.Substring(start, end - start);
        }
    }
}
