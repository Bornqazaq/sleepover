using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Igruha.Minigames.CansOrder;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    public static class CircusOctoberPolish
    {
        [MenuItem("Igruha/Цирк/Плейтест октября — свет и правила")]
        public static void ApplyBoth()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and stop Play first.");
            string opened=EditorSceneManager.GetActiveScene().path;
            const string settings="Assets/_Project/Settings/Gameplay/Minigames/";
            Set(settings+"CircusBearConfig.asset","firstAttackDelay",0);
            Set(settings+"StopwatchConfig.asset","bearFirstAttackDelay",0);
            var config=new SerializedObject(AssetDatabase.LoadAssetAtPath<ScriptableObject>(settings+"CansOrderConfig.asset"));
            config.FindProperty("bottomChances").intValue=2;config.ApplyModifiedPropertiesWithoutUndo();
            var definition=new SerializedObject(AssetDatabase.LoadAssetAtPath<ScriptableObject>(settings+"CansOrder.asset"));
            definition.FindProperty("objective").stringValue="Угадай порядок банок за 120 секунд. После каждого хода клетка игрока с наименьшим числом совпадений опускается на один уровень. При равенстве опускаются все с худшим результатом. Внизу — два дополнительных шанса: только два новых худших результата откроют люк. Собранная расстановка спасает клетку. Конец времени сам по себе люк не открывает.";
            Strings(definition,"tutorialSteps",new[]{"МЕНЯЙ БАНКИ • подтверди порядок и узнай число совпадений.","ХУДШИЙ РЕЗУЛЬТАТ ХОДА • клетка опускается на один уровень.","ДВА ШАНСА ВНИЗУ • два новых проигранных хода откроют люк. Собери всё и спаси клетку."});
            Strings(definition,"tutorialQuickHints",new[]{"Худший результат — вниз; при равенстве опускаются все худшие.","В нижней клетке два дополнительных шанса.","Enter — подтвердить порядок банок."});
            definition.ApplyModifiedPropertiesWithoutUndo();
            try
            {
                foreach(string name in new[]{"Stopwatch","CansOrder"})
                {
                    var scene=EditorSceneManager.OpenScene("Assets/_Project/Scenes/Minigames/"+name+".unity");
                    CircusNightBuilder.ApplyNaturalLighting();
                    var cans=Object.FindFirstObjectByType<CansOrderMinigame>();
                    if(cans!=null)
                    {
                        var so=new SerializedObject(cans);so.FindProperty("shelfCameraDistance").floatValue=1.2f;
                        so.FindProperty("shelfCameraHeight").floatValue=.5f;so.ApplyModifiedPropertiesWithoutUndo();
                        var hud=Object.FindFirstObjectByType<CansOrderLocalHud>();
                        var ui=new SerializedObject(hud);
                        var history=(TMPro.TMP_Text)ui.FindProperty("lastCircleLabel").objectReferenceValue;
                        history.rectTransform.sizeDelta=new Vector2(620,310);history.fontSize=25;
                        var reveal=(TMPro.TMP_Text)ui.FindProperty("revealLabel").objectReferenceValue;
                        reveal.rectTransform.sizeDelta=new Vector2(1100,340);
                    }
                    EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
                }
                AssetDatabase.SaveAssets();
            }
            finally { if(!string.IsNullOrEmpty(opened))EditorSceneManager.OpenScene(opened); }
        }
        private static void Set(string path,string field,float value)
        {
            var so=new SerializedObject(AssetDatabase.LoadAssetAtPath<ScriptableObject>(path));
            so.FindProperty(field).floatValue=value;so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void Strings(SerializedObject so,string field,string[] values)
        {
            var array=so.FindProperty(field);array.arraySize=values.Length;
            for(int i=0;i<values.Length;i++)array.GetArrayElementAtIndex(i).stringValue=values[i];
        }
    }
}
