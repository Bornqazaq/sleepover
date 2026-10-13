using System.Collections.Generic;
using Igruha.Core.Minigame;
using Igruha.Minigames.CansOrder;
using Igruha.Minigames.Stopwatch;
using TMPro;
using UnityEngine;
using static Igruha.Minigames.Circus.CircusUiLayout;

namespace Igruha.Minigames.Circus
{
    /// <summary>Contextual key caps, a labelled turn clock and local result tickets.</summary>
    public sealed class CircusHudView : MonoBehaviour
    {
        [SerializeField] private GameObject layer,dock,controls,waiting,result,history,danger,stopwatchKey,stopwatchDock;
        [SerializeField] private TMP_Text seconds,clockCaption,action,waitTitle,waitSubtitle,resultScore,resultTitle,resultNote,historyScore,dangerTitle,dangerNote,stopTitle,stopNote;
        [SerializeField] private CircusUiGraphic ring,waitIcon,resultIcon,dangerIcon;
        [SerializeField] private CircusUiGraphic[] resultCans,historyCans,lives,resultPanels;
        [SerializeField] private CansOrderConfig palette;
        private readonly List<int> order=new List<int>(8);
        private int lastStage=-1,lastSeconds=-1;
        private string lastWait;
        public bool IsShowingResult=>result!=null && result.activeSelf;
        public void Invalidate(){lastStage=-1;lastWait=null;}

        public void Construct(Transform parent,TMP_FontAsset font,CansOrderConfig colors)
        {
            palette=colors;
            var root=Rect(parent,"CircusHud",0,0,0,0);root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;root.offsetMin=root.offsetMax=Vector2.zero;layer=root.gameObject;
            var d=Card(root,"ActionDock",0,62,760,76,Wine);d.anchorMin=d.anchorMax=new Vector2(.5f,0);dock=d.gameObject;
            ring=Icon(d,"TurnClock",CircusUiGraphic.Sign.Ring,-334,0,48,48,Gold);
            seconds=Text(d,"Seconds",font,"12",-334,1,45,35,25,Cream);seconds.fontStyle=FontStyles.Bold;
            clockCaption=Text(d,"ClockCaption",font,"ХОД",-334,-29,65,13,9,Muted);
            var buttons=Rect(d,"Controls",32,0,626,70);controls=buttons.gameObject;
            action=Text(buttons,"Action",font,"Выбери банку",-104,14,342,27,21,Cream,TextAlignmentOptions.MidlineLeft);action.fontStyle=FontStyles.Bold;
            Text(buttons,"SelectLabel",font,"ЛКМ / E  ·  выбрать две банки",-104,-14,342,25,16,Muted,TextAlignmentOptions.MidlineLeft);
            Key(buttons,font,"Enter",170,0,67);
            Text(buttons,"ConfirmLabel",font,"Принять",267,0,109,28,18,Cream);
            var w=Rect(d,"Waiting",34,0,624,70);waiting=w.gameObject;
            waitIcon=Icon(w,"State",CircusUiGraphic.Sign.Check,-255,0,30,30,Mint);
            waitTitle=Text(w,"Title",font,"Принято",36,14,510,29,22,Cream,TextAlignmentOptions.MidlineLeft);waitTitle.fontStyle=FontStyles.Bold;
            waitSubtitle=Text(w,"Note",font,"Ждём остальных",36,-14,510,24,16,Muted,TextAlignmentOptions.MidlineLeft);
            var sw=Card(root,"StopwatchAction",0,62,448,76,Wine);sw.anchorMin=sw.anchorMax=new Vector2(.5f,0);stopwatchDock=sw.gameObject;
            stopwatchKey=Key(sw,font,"E",-177,0,46).gameObject;
            stopTitle=Text(sw,"Title",font,"Зажми и держи",29,14,321,28,22,Cream,TextAlignmentOptions.MidlineLeft);stopTitle.fontStyle=FontStyles.Bold;
            stopNote=Text(sw,"Note",font,"Отпусти в нужный момент",29,-14,321,24,16,Muted,TextAlignmentOptions.MidlineLeft);
            var risk=Card(root,"CageTicket",156,-70,264,92,Wine);risk.anchorMin=risk.anchorMax=new Vector2(0,1);danger=risk.gameObject;
            dangerIcon=Icon(risk,"Cage",CircusUiGraphic.Sign.Cage,-104,10,28,30,Gold);
            dangerTitle=Text(risk,"Title",font,"УРОВЕНЬ 3",21,23,194,23,16,Cream,TextAlignmentOptions.MidlineLeft);dangerTitle.fontStyle=FontStyles.Bold;
            lives=new CircusUiGraphic[3];for(int i=0;i<3;i++)lives[i]=Icon(risk,"Life_"+i,CircusUiGraphic.Sign.Shield,-64+i*29,-3,17,20,Gold);
            dangerNote=Text(risk,"Note",font,"Худший ход — вниз",0,-29,228,21,13,Muted,TextAlignmentOptions.MidlineLeft);
            var h=Card(root,"PreviousTurn",156,-178,264,96,Ink);h.anchorMin=h.anchorMax=new Vector2(0,1);history=h.gameObject;
            Text(h,"Caption",font,"ПРОШЛЫЙ ХОД",-33,28,169,19,12,Muted,TextAlignmentOptions.MidlineLeft);
            historyScore=Text(h,"Score",font,"2/5",89,28,54,25,20,Gold);
            historyCans=new CircusUiGraphic[5];for(int i=0;i<5;i++)historyCans[i]=Icon(h,"Can_"+i,CircusUiGraphic.Sign.Can,-93+i*46,-10,32,43,Cream);
            var rr=Card(root,"ResultTicket",0,92,520,120,Ink);rr.anchorMin=rr.anchorMax=new Vector2(.5f,0);result=rr.gameObject;resultPanels=rr.GetComponentsInChildren<CircusUiGraphic>();
            resultTitle=Text(rr,"Title",font,"ТВОЙ РЕЗУЛЬТАТ",0,38,470,24,17,Cream,TextAlignmentOptions.MidlineLeft);resultTitle.fontStyle=FontStyles.Bold;
            resultCans=new CircusUiGraphic[5];for(int i=0;i<5;i++)resultCans[i]=Icon(rr,"Can_"+i,CircusUiGraphic.Sign.Can,-211+i*44,-4,32,43,Cream);
            resultScore=Text(rr,"Matches",font,"2/5",168,0,130,50,40,Gold);resultScore.fontStyle=FontStyles.Bold;
            resultIcon=Icon(rr,"Outcome",CircusUiGraphic.Sign.Down,-224,-39,16,18,Red);
            resultNote=Text(rr,"Note",font,"НА УРОВЕНЬ НИЖЕ",19,-39,440,22,13,Muted,TextAlignmentOptions.MidlineLeft);
            Hide();
        }
        public void Hide(){Show(layer,false);Invalidate();}
        public void RenderCans(MinigameStageState stage,CanShelf shelf,CansOrderMinigame game)
        {
            if(stage==null || game==null || stage.Stage==0 || stage.Stage>4 || game.IsPractice){Hide();return;}
            Show(layer,true);byte current=stage.Stage;bool placing=current==2,revealing=current==3;
            Show(stopwatchDock,false);Show(ring.gameObject,true);Show(seconds.gameObject,true);
            bool live=placing && shelf!=null && shelf.Active;
            Show(controls,live);Show(waiting,!live);Show(result,revealing);Show(dock,!revealing);
            int left=Mathf.CeilToInt(stage.StageRemaining);
            if(left!=lastSeconds){lastSeconds=left;seconds.SetText("{0}",left);}
            ring.Amount=stage.StageRemaining/Mathf.Max(.01f,stage.StageDuration);ring.color=placing && left<=3?Red:Gold;
            Set(clockCaption,placing?"НА ХОД":current==1?"СТАРТ":"ПАУЗА");
            if(live)Set(action,shelf.CursorOnConfirm?"Подтверди расстановку":shelf.HasMarkedCan?"Выбери вторую банку":"Выбери банку");
            string wait=placing?game.LocalWaitHint():string.Empty;
            if(current!=lastStage || wait!=lastWait)
            {
                lastStage=current;lastWait=wait;
                if(game.TryGetLocalHudState(out var entry,out int levels,out int chanceLimit,order,placing,out bool previous,out bool confirmed,out int matches))
                {
                    bool solved=entry.Solved;
                    int level=Mathf.RoundToInt(entry.HeightFraction*levels);
                    Show(danger,placing && !solved);Show(history,placing && previous && !solved);
                    Set(dangerTitle,level>0?"УРОВЕНЬ "+level:"ПОСЛЕДНИЕ ШАНСЫ");
                    Set(dangerNote,level>0?"Худший ход — вниз":"Худший ход — минус шанс");
                    for(int i=0;i<lives.Length;i++){Show(lives[i].gameObject,i<(level>0?levels:chanceLimit));lives[i].color=i<(level>0?level:entry.BottomChances)?Gold:new Color(.25f,.31f,.40f,.8f);}
                    Set(historyScore,confirmed?matches+"/"+game.Round.CanCount:"—");Fill(historyCans,order);
                    if(revealing)
                    {
                        Set(resultTitle,solved?"ПОРЯДОК СОБРАН!":confirmed?"ТВОЙ РЕЗУЛЬТАТ":"ХОД НЕ ПОДТВЕРЖДЁН");
                        Set(resultScore,solved?"":confirmed?matches+"/"+game.Round.CanCount:"—");Fill(resultCans,order);
                        string note=solved?"КЛЕТКА В БЕЗОПАСНОСТИ":entry.Penalty==CansOrderPenalty.Dropped?"ЛЮК ОТКРЫВАЕТСЯ":entry.Penalty==CansOrderPenalty.LastChance?"ОСТАЛСЯ ОДИН ШАНС":entry.Penalty==CansOrderPenalty.Descended?"НА УРОВЕНЬ НИЖЕ":"КЛЕТКА ОСТАЁТСЯ";
                        Set(resultNote,note);resultIcon.Kind=solved?CircusUiGraphic.Sign.Shield:entry.Penalty==CansOrderPenalty.None?CircusUiGraphic.Sign.Check:CircusUiGraphic.Sign.Down;
                        resultIcon.color=solved || entry.Penalty==CansOrderPenalty.None?Mint:Red;
                        LayoutResult(order.Count>0);
                    }
                    Set(waitTitle,placing?(solved?"СОБРАНО!":"ПРИНЯТО"):current==1?"ПРИГОТОВЬСЯ":"ФИНАЛ");
                    Set(waitSubtitle,placing?(solved?"Твоя клетка в безопасности":"Ждём остальных"):current==1?"Найди порядок пяти банок":"Смотри на арену");
                    waitIcon.Kind=placing?CircusUiGraphic.Sign.Check:CircusUiGraphic.Sign.Hourglass;
                }
            }
        }
        private void LayoutResult(bool hasOrder)
        {
            var root=(RectTransform)result.transform;
            root.sizeDelta=hasOrder?new Vector2(520,120):new Vector2(460,76);
            root.anchoredPosition=new Vector2(0,hasOrder?92:62);
            foreach(var panel in resultPanels)
                panel.rectTransform.sizeDelta=root.sizeDelta+(panel.name=="Shadow"?Vector2.one*2:panel.name=="Surface"?-Vector2.one*2:Vector2.zero);
            Place(resultTitle.rectTransform,hasOrder?0:16,hasOrder?38:13,hasOrder?470:380,36);
            resultTitle.fontSize=hasOrder?17:20;
            Place(resultNote.rectTransform,hasOrder?19:16,hasOrder?-39:-15,hasOrder?440:380,26);
            resultNote.fontSize=hasOrder?13:14;
            Place(resultIcon.rectTransform,hasOrder?-224:-197,hasOrder?-39:0,hasOrder?16:23,hasOrder?18:25);
            Show(resultScore.gameObject,hasOrder);
        }
        public void RenderStopwatch(CageButton button)
        {
            if(button==null || !button.WindowOpen || MinigameControllerBase.Current!=null && MinigameControllerBase.Current.IsPractice){Hide();return;}
            Show(layer,true);Show(dock,false);Show(stopwatchDock,true);Show(result,false);Show(history,false);Show(danger,false);
            // No animation, elapsed number or progress arc while measuring.
            bool running=button.State==CageButton.ButtonState.Running,stopped=button.State==CageButton.ButtonState.Stopped;
            Show(stopwatchKey,!stopped);
            Set(stopTitle,stopped?"Ответ принят":running?"Считай про себя":"Зажми и держи E");
            Set(stopNote,stopped?"Ждём остальных":running?"Отпусти E в нужный момент":"Цель указана на табло");
            stopTitle.color=stopped?Mint:Cream;
        }

        private void Fill(CircusUiGraphic[] cells,IReadOnlyList<int> ids)
        {
            for(int i=0;i<cells.Length;i++)
            {Show(cells[i].gameObject,i<ids.Count);if(i<ids.Count){cells[i].Symbol=ids[i];cells[i].color=palette.GetCanKind(ids[i]).color;}}
        }
    }
}
