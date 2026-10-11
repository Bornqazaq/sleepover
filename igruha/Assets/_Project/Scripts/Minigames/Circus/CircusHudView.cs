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
        [SerializeField] private GameObject layer,dock,controls,waiting,result,history,danger,stopwatchKey;
        [SerializeField] private TMP_Text seconds,clockCaption,action,waitTitle,waitSubtitle,resultScore,resultTitle,resultNote,historyScore,dangerTitle,dangerNote;
        [SerializeField] private CircusUiGraphic ring,waitIcon,resultIcon,dangerIcon;
        [SerializeField] private CircusUiGraphic[] resultCans,historyCans,lives;
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
            var d=Card(root,"ActionDock",0,112,1020,144,Wine);d.anchorMin=d.anchorMax=new Vector2(.5f,0);dock=d.gameObject;
            ring=Icon(d,"TurnClock",CircusUiGraphic.Sign.Ring,-410,0,92,92,Gold);
            seconds=Text(d,"Seconds",font,"12",-410,2,69,41,32,Cream);seconds.fontStyle=FontStyles.Bold;
            clockCaption=Text(d,"ClockCaption",font,"НА ХОД",-410,-25,78,18,12,Gold);
            stopwatchKey=Key(d,font,"E",-410,6,72).gameObject;Show(stopwatchKey,false);
            var buttons=Rect(d,"Controls",35,0,810,110);controls=buttons.gameObject;
            action=Text(buttons,"Action",font,"ВЫБЕРИ БАНКУ",0,42,730,29,24,Cream);action.fontStyle=FontStyles.Bold;
            Icon(buttons,"Mouse",CircusUiGraphic.Sign.Mouse,-310,-9,36,45,Cream);
            Text(buttons,"Or",font,"/",-273,-10,20,30,18,Muted);Key(buttons,font,"E",-234,-9,44);
            Text(buttons,"SelectLabel",font,"ЛКМ / ЗАЖМИ E",-272,-44,200,22,17,Cream);
            Icon(buttons,"Swap",CircusUiGraphic.Sign.Swap,-35,-7,42,40,Gold);
            Text(buttons,"SwapLabel",font,"ВЫБЕРИ ДВЕ",-35,-44,190,22,17,Cream);
            Key(buttons,font,"Enter",235,-9,88);
            Text(buttons,"ConfirmLabel",font,"ПОДТВЕРДИТЬ",235,-44,200,22,17,Cream);
            var w=Rect(d,"Waiting",35,0,785,100);waiting=w.gameObject;
            waitIcon=Icon(w,"State",CircusUiGraphic.Sign.Check,-255,0,62,62,Mint);
            waitTitle=Text(w,"Title",font,"ПРИНЯТО",50,14,500,33,29,Cream);waitTitle.fontStyle=FontStyles.Bold;
            waitSubtitle=Text(w,"Note",font,"Ждём остальных",50,-24,500,25,18,Muted);
            var risk=Card(root,"CageTicket",178,-86,300,112,Wine);risk.anchorMin=risk.anchorMax=new Vector2(0,1);danger=risk.gameObject;
            dangerIcon=Icon(risk,"Cage",CircusUiGraphic.Sign.Cage,-109,11,45,45,Gold);
            dangerTitle=Text(risk,"Title",font,"УРОВЕНЬ 3",31,30,205,28,21,Cream,TextAlignmentOptions.MidlineLeft);
            lives=new CircusUiGraphic[3];for(int i=0;i<3;i++)lives[i]=Icon(risk,"Life_"+i,CircusUiGraphic.Sign.Shield,-48+i*37,-2,25,27,Gold);
            dangerNote=Text(risk,"Note",font,"Худший ход — вниз",8,-35,270,21,15,Muted);
            var h=Card(root,"PreviousTurn",178,-220,300,132,Ink);h.anchorMin=h.anchorMax=new Vector2(0,1);history=h.gameObject;
            Text(h,"Caption",font,"ПРОШЛЫЙ ХОД",-26,45,234,22,15,Muted,TextAlignmentOptions.MidlineLeft);
            historyScore=Text(h,"Score",font,"2/5",100,44,65,25,22,Gold);
            historyCans=new CircusUiGraphic[5];for(int i=0;i<5;i++)historyCans[i]=Icon(h,"Can_"+i,CircusUiGraphic.Sign.Can,-110+i*55,-10,44,61,Cream);
            var rr=Card(root,"ResultTicket",0,-236,814,206,Ink);result=rr.gameObject;
            resultTitle=Text(rr,"Title",font,"ТВОЙ РЕЗУЛЬТАТ",-57,74,630,34,25,Cream);resultTitle.fontStyle=FontStyles.Bold;
            resultCans=new CircusUiGraphic[5];for(int i=0;i<5;i++)resultCans[i]=Icon(rr,"Can_"+i,CircusUiGraphic.Sign.Can,-307+i*75,4,62,83,Cream);
            resultScore=Text(rr,"Matches",font,"2/5",235,15,225,78,65,Gold);resultScore.fontStyle=FontStyles.Bold;
            resultIcon=Icon(rr,"Outcome",CircusUiGraphic.Sign.Down,-371,-70,27,31,Red);
            resultNote=Text(rr,"Note",font,"НА УРОВЕНЬ НИЖЕ",0,-70,715,32,21,Gold);
            Hide();
        }
        public void Hide(){Show(layer,false);Invalidate();}
        public void RenderCans(MinigameStageState stage,CanShelf shelf,CansOrderMinigame game)
        {
            if(stage==null || game==null || stage.Stage==0 || stage.Stage>4){Hide();return;}
            Show(layer,true);byte current=stage.Stage;bool placing=current==2,revealing=current==3;
            Show(stopwatchKey,false);Show(seconds.gameObject,true);
            bool live=placing && shelf!=null && shelf.Active;
            Show(controls,live);Show(waiting,!live);Show(result,revealing);Show(dock,!revealing);
            int left=Mathf.CeilToInt(stage.StageRemaining);
            if(left!=lastSeconds){lastSeconds=left;seconds.SetText("{0}",left);}
            ring.Amount=stage.StageRemaining/Mathf.Max(.01f,stage.StageDuration);ring.color=placing && left<=3?Red:Gold;
            Set(clockCaption,placing?"НА ХОД":current==1?"СТАРТ":"ПАУЗА");
            if(live)Set(action,shelf.CursorOnConfirm?"ПОДТВЕРДИ РАССТАНОВКУ":shelf.HasMarkedCan?"ВЫБЕРИ ВТОРУЮ БАНКУ":"ВЫБЕРИ БАНКУ");
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
                    for(int i=0;i<lives.Length;i++){Show(lives[i].gameObject,i<(level>0?levels:chanceLimit));lives[i].color=i<(level>0?level:entry.BottomChances)?Gold:new Color(.4f,.25f,.25f,.5f);}
                    Set(historyScore,confirmed?matches+"/"+game.Round.CanCount:"—");Fill(historyCans,order);
                    if(revealing)
                    {
                        Set(resultTitle,solved?"ПОРЯДОК СОБРАН!":confirmed?"ТВОЙ РЕЗУЛЬТАТ":"ХОД НЕ ПОДТВЕРЖДЁН");
                        Set(resultScore,solved?"":confirmed?matches+"/"+game.Round.CanCount:"—");Fill(resultCans,order);
                        string note=solved?"КЛЕТКА В БЕЗОПАСНОСТИ":entry.Penalty==CansOrderPenalty.Dropped?"ЛЮК ОТКРЫВАЕТСЯ":entry.Penalty==CansOrderPenalty.LastChance?"ОСТАЛСЯ ОДИН ШАНС":entry.Penalty==CansOrderPenalty.Descended?"НА УРОВЕНЬ НИЖЕ":"КЛЕТКА ОСТАЁТСЯ";
                        Set(resultNote,note);resultIcon.Kind=solved?CircusUiGraphic.Sign.Shield:entry.Penalty==CansOrderPenalty.None?CircusUiGraphic.Sign.Check:CircusUiGraphic.Sign.Down;
                        resultIcon.color=solved || entry.Penalty==CansOrderPenalty.None?Mint:Red;
                    }
                    Set(waitTitle,placing?(solved?"СОБРАНО!":"ПРИНЯТО"):current==1?"ПРИГОТОВЬСЯ":"ФИНАЛ");
                    Set(waitSubtitle,placing?(solved?"Твоя клетка в безопасности":"Ждём остальных"):current==1?"Найди порядок пяти банок":"Смотри на арену");
                    waitIcon.Kind=placing?CircusUiGraphic.Sign.Check:CircusUiGraphic.Sign.Hourglass;
                }
            }
        }
        public void RenderStopwatch(CageButton button)
        {
            if(button==null || !button.WindowOpen){Hide();return;}
            Show(layer,true);Show(dock,true);Show(controls,false);Show(waiting,true);Show(result,false);Show(history,false);Show(danger,false);
            // Static states only: a pulse or progress arc would reveal the hidden clock.
            bool running=button.State==CageButton.ButtonState.Running,stopped=button.State==CageButton.ButtonState.Stopped;
            Show(ring.gameObject,false);Show(stopwatchKey,!stopped);Show(seconds.gameObject,stopped);Set(seconds,"OK");
            Set(clockCaption,stopped?"ПРИНЯТО":running?"ОТПУСТИ":"ЗАЖМИ");
            waitIcon.Kind=stopped?CircusUiGraphic.Sign.Check:running?CircusUiGraphic.Sign.Hourglass:CircusUiGraphic.Sign.Down;waitIcon.color=running?Red:stopped?Mint:Gold;
            Set(waitTitle,stopped?"ГОТОВО":running?"ДЕРЖИ РИТМ":"НАЖМИ И ДЕРЖИ");
            Set(waitSubtitle,stopped?"Ждём остальных":running?"Отпусти E в нужный момент":"E  ·  цель на табло");
        }
        private void Fill(CircusUiGraphic[] cells,IReadOnlyList<int> ids)
        {
            for(int i=0;i<cells.Length;i++)
            {Show(cells[i].gameObject,i<ids.Count);if(i<ids.Count){cells[i].Symbol=ids[i];cells[i].color=palette.GetCanKind(ids[i]).color;}}
        }
    }
}
