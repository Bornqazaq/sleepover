using System.Linq;
using Igruha.Core.Minigame;
using Igruha.Core.UI;
using Igruha.Minigames.CansOrder;
using Igruha.Minigames.Circus;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class CircusInterfacePresentationTests
    {
        [Test] public void StopwatchCannotReactivateItsRoundClockOnRestart()
        {
            var scene=EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Minigames/Stopwatch.unity");
            try
            {
                var hud=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<RoundHud>(true)).Single();
                var fields=new SerializedObject(hud);
                Assert.That(fields.FindProperty("timerText").objectReferenceValue,Is.Null);
                Assert.That(fields.FindProperty("timerPlate").objectReferenceValue,Is.Null);
                hud.Bind(null);hud.SetTimerPlateVisible(true);
                var timers=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<TMP_Text>(true))
                    .Where(t=>t.name=="TimerText" || t.name=="TimerCaption");
                Assert.That(timers.All(t=>!t.isActiveAndEnabled),Is.True,"A new round must not reveal the hidden measurement through the global clock.");
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }

        [TestCase(2)] [TestCase(8)]
        public void StopwatchScreenShowsStaticTargetAndRevealsTimesOnlyWithResults(int players)
        {
            var root=new GameObject("_CircusScreenBoard",typeof(RectTransform),typeof(Canvas));
            try
            {
                var view=root.AddComponent<CircusBoardView>();
                var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/UI/Fonts/UI_Sans_Medium SDF.asset");
                var palette=AssetDatabase.LoadAssetAtPath<CansOrderConfig>("Assets/_Project/Settings/Gameplay/Minigames/CansOrderConfig.asset");
                view.Construct(new[]{(RectTransform)root.transform},font,new Sprite[0],palette);
                view.StopwatchTask("ОТМЕРЬ РОВНО",5,2);
                view.Begin(players,0,5,false);
                for(int i=0;i<players;i++)view.StopwatchRow(i,i,"Игрок "+i,"4.91",0,3,true,false);
                Assert.That(root.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="5.0 с"),Is.True);
                Assert.That(root.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="4.91"),Is.False);
                Assert.That(root.GetComponentsInChildren<CircusUiGraphic>().Any(g=>g.Kind==CircusUiGraphic.Sign.Ring),Is.False);
                view.Begin(players,0,5,true);
                for(int i=0;i<players;i++)view.StopwatchRow(i,i,"Игрок "+i,"4.91",0,3,true,false);
                Assert.That(root.GetComponentsInChildren<TMP_Text>().Count(t=>t.text=="4.91"),Is.EqualTo(players));
                Assert.That(view.VisibleRows,Is.EqualTo(players));
            }
            finally{Object.DestroyImmediate(root);}
        }
    }
}
