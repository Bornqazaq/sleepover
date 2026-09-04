using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Запекание обеих цирковых сцен одним действием — подфаза 4.6.
    ///
    /// <b>Зачем отдельный пункт, а не «Запечь арт сцены» дважды.</b> Арена
    /// общая: она лежит и в <c>Stopwatch.unity</c>, и в <c>CansOrder.unity</c>.
    /// Запечь одну и забыть вторую — это ровно тот случай, из-за которого
    /// <c>DuckHunt.unity</c> дала 73 ошибки в сборке и была выключена из
    /// билда. Один пункт на обе сцены убирает саму возможность забыть.
    ///
    /// 🔴 <b>Внутри зовётся <see cref="ArtBake.BakeActiveScene"/>, а не пункт
    /// меню.</b> Пункт <c>Tools/Арт/Запечь арт сцены</c> начинается с
    /// <c>EditorUtility.DisplayDialog</c>. Вызванный из кода — в том числе
    /// через MCP, — он открывает модальное окно и <b>блокирует главный поток
    /// Unity</b>: мост перестаёт отвечать на ping, и снаружи это выглядит как
    /// отвалившееся соединение, хотя редактор просто ждёт клика мышью.
    /// Проверено 04.09 на этой самой подфазе. <c>BakeActiveScene</c> написан
    /// ровно для такого вызова и диалога не открывает.
    ///
    /// Здесь диалог всё-таки есть — один на оба запекания, и только когда
    /// пункт нажат человеком. Запуск из кода идёт через
    /// <see cref="BakeBoth"/>, который спрашивать не умеет.
    /// </summary>
    internal static class CircusBake
    {
        private const string StopwatchScene = "Assets/_Project/Scenes/Minigames/Stopwatch.unity";
        private const string CansOrderScene = "Assets/_Project/Scenes/Minigames/CansOrder.unity";

        [MenuItem("Igruha/Цирк/Запечь обе сцены")]
        private static void BakeBothWithPrompt()
        {
            if (!EditorUtility.DisplayDialog(
                    "Запечь обе цирковые сцены",
                    "Перенести использованное из паков в Assets/_Project/Art/ и переписать ссылки " +
                    "в Stopwatch.unity и CansOrder.unity?\n\n" +
                    "Инстансы префабов паков будут распакованы — связь с исходными префабами теряется.",
                    "Запечь обе", "Отмена"))
            {
                return;
            }

            Debug.Log(BakeBoth());
        }

        /// <summary>
        /// Запечь обе сцены без единого диалога. Возвращает отчёт строкой:
        /// вызывающий сам решает, логировать его или разбирать числа.
        /// </summary>
        internal static string BakeBoth()
        {
            string opened = EditorSceneManager.GetActiveScene().path;
            var report = new StringBuilder();
            report.Append("🔥 Запекание цирка — обе сцены");

            report.Append("\n\n").Append(BakeOne(StopwatchScene));
            report.Append("\n\n").Append(BakeOne(CansOrderScene));

            // Возвращаем ту сцену, что была открыта: пересборка после
            // запекания идёт уже по локальным копиям, и подменять человеку
            // сцену под руками незачем.
            if (!string.IsNullOrEmpty(opened))
            {
                EditorSceneManager.OpenScene(opened, OpenSceneMode.Single);
            }

            return report.ToString();
        }

        private static string BakeOne(string scenePath)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            string result = ArtBake.BakeActiveScene();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            return result;
        }
    }
}
