using Igruha.Core.UI;
using Igruha.Minigames.Circus;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Igruha.Minigames.Circus.CircusUiLayout;

namespace Igruha.EditorTools
{
    /// <summary>Scene-local typography and result styling; other games retain their existing theme.</summary>
    internal static class CircusInterfaceSkin
    {
        internal static void Apply(Transform canvas,bool cans)
        {
            foreach(var root in canvas.gameObject.scene.GetRootGameObjects())
                foreach(var text in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    // Restrict this pass to UI and scoreboard text, never world decoration.
                    if(text.GetComponentInParent<CircusHudView>()==null && text.GetComponentInParent<CircusBoardView>()==null &&
                        (canvas.Find("_CircusScreenBoard")==null || !text.transform.IsChildOf(canvas.Find("_CircusScreenBoard"))))continue;
                    bool bold=(text.fontStyle&FontStyles.Bold)!=0 || text.name=="Title" || text.name=="Score" || text.name=="Seconds";
                    text.font=bold?UiFonts.SansBold:UiFonts.SansMedium;
                    text.fontStyle&=~FontStyles.Bold;
                }
            var hud=canvas.GetComponentInParent<RoundHud>() ?? canvas.GetComponent<RoundHud>();
            if(hud==null)hud=Object.FindFirstObjectByType<RoundHud>(FindObjectsInactive.Include);
            var data=new SerializedObject(hud);
            var timer=data.FindProperty("timerPlate").objectReferenceValue as GameObject;
            if(timer==null)timer=canvas.Find("_HudPlates/TimerPlate")?.gameObject;
            if(!cans)
            {
                // Disconnect both references: RoundHud.Bind otherwise reactivates the clock each round.
                if(timer!=null)timer.SetActive(false);
                var text=data.FindProperty("timerText").objectReferenceValue as TMP_Text;
                if(text!=null)text.gameObject.SetActive(false);
                data.FindProperty("timerText").objectReferenceValue=null;
                data.FindProperty("timerPlate").objectReferenceValue=null;
            }
            else if(timer!=null)
            {
                var rect=(RectTransform)timer.transform;rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one;
                rect.anchoredPosition=new Vector2(-24,-24);rect.sizeDelta=new Vector2(164,64);
                var image=timer.GetComponent<Image>();if(image!=null)image.color=Wine;
                var timerText=data.FindProperty("timerText").objectReferenceValue as TMP_Text;
                if(timerText!=null){timerText.font=UiFonts.Numbers;timerText.fontSize=30;timerText.rectTransform.anchoredPosition=new Vector2(0,-8);timerText.rectTransform.sizeDelta=new Vector2(144,38);}
                var caption=timer.transform.Find("TimerCaption")?.GetComponent<TMP_Text>();
                if(caption!=null){caption.text="РАУНД";caption.color=Muted;caption.fontSize=11;caption.rectTransform.anchoredPosition=new Vector2(0,18);caption.rectTransform.sizeDelta=new Vector2(144,18);}
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            var tutorial=Object.FindFirstObjectByType<TutorialScreen>(FindObjectsInactive.Include);
            if(tutorial!=null){var t=new SerializedObject(tutorial);t.FindProperty("compactDarkStyle").boolValue=true;t.ApplyModifiedPropertiesWithoutUndo();}
            var panel=data.FindProperty("resultsPanel").objectReferenceValue as GameObject;
            if(panel!=null)StyleResults(panel);
            foreach(var graphic in canvas.GetComponentsInChildren<Graphic>(true))
                if(PrefabUtility.IsPartOfPrefabInstance(graphic))PrefabUtility.RecordPrefabInstancePropertyModifications(graphic);
        }

        private static void StyleResults(GameObject panel)
        {
            var card=panel.transform.Find("ResultsCard");if(card==null)return;
            var scrim=panel.transform.Find("Scrim")?.GetComponent<Image>();if(scrim!=null)scrim.color=new Color(.015f,.025f,.045f,.72f);
            card.GetComponent<Image>().color=Ink;
            foreach(var text in card.GetComponentsInChildren<TMP_Text>(true))
            {
                text.font=text.name=="Title" || text.name=="PlayerName" || text.name=="Points"?UiFonts.SansBold:UiFonts.SansMedium;
                text.color=text.name=="Subtitle" || text.name=="Transition" || text.name=="Detail" || text.name.Contains("Header")?Muted:Cream;
            }
            var accent=card.Find("Accent")?.GetComponent<Image>();if(accent!=null)accent.color=Gold;
            var title=card.Find("Title")?.GetComponent<TMP_Text>();if(title!=null)title.fontSize=42;
            var eyebrow=card.Find("Eyebrow")?.GetComponent<TMP_Text>();if(eyebrow!=null)eyebrow.color=Gold;
            foreach(var row in card.GetComponentsInChildren<ResultRow>(true))
            {
                var data=new SerializedObject(row);
                data.FindProperty("rowColor").colorValue=Wine;
                data.FindProperty("winnerColor").colorValue=new Color(.075f,.23f,.24f);
                data.FindProperty("textColor").colorValue=Cream;
                data.ApplyModifiedPropertiesWithoutUndo();
                var mark=row.transform.Find("LocalMark")?.GetComponent<Image>();if(mark!=null)mark.color=new Color(.22f,.22f,.40f);
                var place=row.transform.Find("Place")?.GetComponent<TMP_Text>();if(place!=null){place.font=UiFonts.Numbers;place.color=Gold;}
                var points=row.transform.Find("Points")?.GetComponent<TMP_Text>();if(points!=null)points.color=Gold;
            }
            var button=card.Find("RestartButton")?.GetComponent<Image>();if(button!=null)button.color=Gold;
            var label=card.Find("RestartButton/Label")?.GetComponent<TMP_Text>();if(label!=null){label.color=Ink;label.font=UiFonts.SansBold;}
        }
    }
}
