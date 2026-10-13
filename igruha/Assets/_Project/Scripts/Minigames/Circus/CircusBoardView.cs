using System;
using System.Collections.Generic;
using Igruha.Core.Session;
using Igruha.Core.Minigame;
using Igruha.Minigames.CansOrder;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Igruha.Minigames.Circus.CircusUiLayout;

namespace Igruha.Minigames.Circus
{
    /// <summary>Published results on the arena screens; a compact, static task on the player's screen.</summary>
    public sealed class CircusBoardView : MonoBehaviour
    {
        [Serializable] public sealed class Row
        {
            public RectTransform root;
            public Image portrait;
            public TMP_Text name, score, status, rank;
            public CircusUiGraphic seal, background;
            public CircusUiGraphic[] cans;
        }
        [Serializable] public sealed class Face
        {
            public RectTransform root, surface;
            public CircusUiGraphic[] panels;
            public TMP_Text title, subtitle, target, caption;
            public GameObject idle, cans;
            public bool screenCopy;
            public string ruleTitle, resultTitle, roundLabel;
            public Row[] rows;
        }
        [SerializeField] private Face[] faces;
        [SerializeField] private Sprite[] portraits;
        [SerializeField] private CansOrderConfig palette;
        private int total,featured,featureCursor,otherCursor,canCount=5;
        private bool reveal=true;
        public int VisibleRows { get; private set; }
        public int VisibleArrangements { get; private set; }

        public void Construct(RectTransform[] screens,TMP_FontAsset font,Sprite[] portraitSprites,CansOrderConfig colors)
        {
            portraits=portraitSprites;palette=colors;faces=new Face[screens.Length];
            for(int f=0;f<screens.Length;f++)
            {
                bool overlay=screens[f].name=="_CircusScreenBoard";
                var root=Rect(screens[f],"CircusFace",0,0,overlay?448:552,206);
                if(overlay){root.anchorMin=root.anchorMax=root.pivot=new Vector2(.5f,1);}
                var face=faces[f]=new Face{root=root,screenCopy=overlay};
                face.surface=Card(root,"Display",0,0,overlay?448:552,206,Ink);
                face.panels=face.surface.GetComponentsInChildren<CircusUiGraphic>();
                face.title=Text(root,"Title",font,"ПОРЯДОК БАНОК",0,77,508,29,22,Cream,TextAlignmentOptions.MidlineLeft);face.title.fontStyle=FontStyles.Bold;
                face.subtitle=Text(root,"Subtitle",font,"",0,51,508,18,11,Muted,TextAlignmentOptions.MidlineLeft);
                var idle=Rect(root,"TaskIllustration",0,-22,508,115);face.idle=idle.gameObject;
                var cans=Rect(idle,"CanSymbols",0,0,300,90);face.cans=cans.gameObject;
                Icon(cans,"CanA",CircusUiGraphic.Sign.Can,-84,4,43,59,Gold).Symbol=3;
                Icon(cans,"Swap",CircusUiGraphic.Sign.Swap,0,4,32,32,Muted);
                Icon(cans,"CanB",CircusUiGraphic.Sign.Can,84,4,43,59,Violet).Symbol=3;
                face.target=Text(idle,"Target",font,"",0,4,380,62,44,Gold);face.target.fontStyle=FontStyles.Bold;
                face.caption=Text(idle,"Caption",font,"Выбери · поменяй · подтверди",0,-44,470,23,14,Muted);
                face.rows=new Row[8];
                for(int i=0;i<8;i++)
                {
                    var row=face.rows[i]=new Row();
                    row.root=Rect(root,"Player_"+i,0,0,520,40);
                    row.background=Icon(row.root,"Card",CircusUiGraphic.Sign.Panel,0,0,520,40,Wine);
                    var bg=row.background.rectTransform;bg.anchorMin=Vector2.zero;bg.anchorMax=Vector2.one;bg.offsetMin=bg.offsetMax=Vector2.zero;
                    row.rank=Text(row.root,"Rank",font,(i+1).ToString("00"),-245,0,23,20,10,Muted);
                    row.portrait=Portrait(row.root,-210,0,30);
                    row.name=Text(row.root,"Name",font,"",-130,0,122,25,15,Cream,TextAlignmentOptions.MidlineLeft);row.name.fontStyle=FontStyles.Bold;
                    row.score=Text(row.root,"Score",font,"",220,0,70,30,24,Gold);row.score.fontStyle=FontStyles.Bold;
                    row.status=Text(row.root,"Status",font,"",45,0,182,20,11,Muted);
                    row.seal=Icon(row.root,"Seal",CircusUiGraphic.Sign.Check,205,0,21,21,Mint);
                    row.cans=new CircusUiGraphic[5];
                    for(int c=0;c<5;c++)row.cans[c]=Icon(row.root,"Can_"+c,CircusUiGraphic.Sign.Can,-36+c*37,0,28,34,Cream);
                    row.root.gameObject.SetActive(false);
                }
            }
        }
        public void Header(string title,string subtitle)
        {if(faces==null)return;foreach(var f in faces){Set(f.title,title);Set(f.subtitle,subtitle);}}
        public void Task(string title,string subtitle,bool cans)
        {
            Clear();Header(title,subtitle);
            if(faces==null)return;
            foreach(var f in faces)
            {
                Show(f.idle,true);Show(f.cans,cans);Show(f.target.gameObject,!cans);
                Set(f.caption,cans?"Выбери  ·  поменяй  ·  подтверди":"Считай про себя");
            }
        }
        public void StopwatchTask(string rule,float target,int round)
        {
            Task(rule+" · "+target.ToString("0.0")+" С","РАУНД "+round+"  /  ОСТАВШИЕСЯ ШАНСЫ",false);
            foreach(var f in faces)
            {
                Set(f.target,target.ToString("0.0")+" с");
                if(f.screenCopy)
                {
                    f.ruleTitle=rule;f.resultTitle=rule+" · "+target.ToString("0.0")+" С";f.roundLabel="РАУНД "+round;
                    Set(f.title,rule);Set(f.subtitle,f.roundLabel+"  /  ЦЕЛЬ");
                    LayoutScreen(f,false);
                }
            }
        }
        public void Begin(int count,int arrangements,int cans=5,bool showResults=true)
        {
            canCount=cans;total=count;featured=arrangements;reveal=showResults;
            featureCursor=otherCursor=0;VisibleRows=VisibleArrangements=0;
            if(faces==null)return;
            foreach(var f in faces)
            {
                if(f.screenCopy){LayoutScreen(f,showResults);Show(f.root.gameObject,MinigameControllerBase.Current==null || !MinigameControllerBase.Current.IsPractice);}
                Show(f.idle,f.screenCopy && !showResults);
                foreach(var r in f.rows)Show(r.root.gameObject,false);
            }
        }
        public void CansRow(int index,int playerId,string playerName,IReadOnlyList<int> order,int matches,bool confirmed,bool solved)
        {
            bool hasOrder=order!=null && !solved && confirmed;
            int slot=hasOrder?featureCursor++:otherCursor++;
            foreach(var f in faces)
            {
                Row row=f.rows[index];Arrange(f,row,index,slot,hasOrder);BindPlayer(row,playerId,playerName);
                Set(row.score,solved?"":confirmed?matches+"/"+canCount:"—");row.score.color=Gold;
                Set(row.status,solved?"СОБРАНО":!confirmed?"НЕТ ОТВЕТА":hasOrder?"":"СОВПАДЕНИЙ");
                Show(row.status.gameObject,!confirmed);Show(row.seal.gameObject,solved);
                if(!hasOrder && confirmed){var pos=row.name.rectTransform.anchoredPosition;pos.y=0;row.name.rectTransform.anchoredPosition=pos;}
                row.background.color=solved?new Color(.075f,.21f,.19f):Wine;
                for(int i=0;i<row.cans.Length;i++)
                {
                    Show(row.cans[i].gameObject,hasOrder && i<order.Count);
                    if(hasOrder && i<order.Count){row.cans[i].Kind=CircusUiGraphic.Sign.Can;row.cans[i].Symbol=order[i];row.cans[i].color=palette.GetCanKind(order[i]).color;}
                }
            }
            VisibleRows++;if(hasOrder)VisibleArrangements++;
        }
        public void StopwatchRow(int index,int playerId,string playerName,string value,int errors,int limit,bool alive,bool faulted)
        {
            foreach(var f in faces)
            {
                if(f.screenCopy && !reveal)continue;
                var row=f.rows[index];Arrange(f,row,index,index,false);BindPlayer(row,playerId,playerName);
                Set(row.score,value);row.score.color=faulted?Red:Gold;Set(row.status,alive?"":"ВЫБЫЛ");
                row.background.color=faulted?new Color(.22f,.105f,.15f):Wine;
                Show(row.status.gameObject,!alive);Show(row.seal.gameObject,false);
                for(int c=0;c<row.cans.Length;c++)
                {var icon=row.cans[c];Show(icon.gameObject,alive && c<limit);icon.Kind=CircusUiGraphic.Sign.Shield;icon.color=c<limit-errors?Mint:new Color(.25f,.31f,.4f);}
            }
            VisibleRows++;
        }
        private void LayoutScreen(Face f,bool results)
        {
            if(!string.IsNullOrEmpty(f.ruleTitle)){Set(f.title,results?f.resultTitle:f.ruleTitle);Set(f.subtitle,f.roundLabel+(results?"  /  РЕЗУЛЬТАТЫ":"  /  ЦЕЛЬ"));}
            float height=results?92+Mathf.Min(total,4)*45:112;
            f.root.sizeDelta=new Vector2(448,height);f.surface.sizeDelta=f.root.sizeDelta;
            foreach(var g in f.panels)
                g.rectTransform.sizeDelta=f.root.sizeDelta+(g.name=="Shadow"?Vector2.one*2:g.name=="Surface"?-Vector2.one*2:Vector2.zero);
            Place(f.title.rectTransform,results?0:-62,height*.5f-25,results?408:284,29);
            f.title.fontSize=results?17:16;
            Place(f.subtitle.rectTransform,0,height*.5f-51,408,19);
            Place((RectTransform)f.idle.transform,0,0,408,80);
            Place(f.target.rectTransform,145,14,105,48);f.target.fontSize=32;
            Place(f.caption.rectTransform,0,-34,408,21);f.caption.alignment=TextAlignmentOptions.MidlineLeft;
            FitTextHeight(f.title);FitTextHeight(f.subtitle);FitTextHeight(f.target);FitTextHeight(f.caption);
        }
        private void Arrange(Face f,Row r,int index,int slot,bool hasOrder)
        {
            bool wide=f.screenCopy?total<=4:total<=3;
            float width=f.screenCopy?(wide?416:202):wide?520:featured>0?(hasOrder?320:192):254;
            int n=wide?total:featured>0?(hasOrder?Mathf.Max(featured,3):Mathf.Max(total-featured,5)):4;
            float pitch=f.screenCopy?45:126f/Mathf.Max(1,n),height=Mathf.Min(f.screenCopy?39:56,pitch-4);
            int line=wide?index:featured>0?slot:index%4;
            float x=wide?0:featured>0?(hasOrder?-100:164):(index<4?-width*.5f-5:width*.5f+5);
            float top=f.screenCopy?f.root.sizeDelta.y*.5f-79:34;
            Place(r.root,x,top-pitch*.5f-line*pitch,width,height);Show(r.root.gameObject,true);
            float half=width*.5f;
            Show(r.rank.gameObject,wide);Place(r.rank.rectTransform,-half+18,0,24,20);
            Place(r.portrait.rectTransform,-half+(wide?49:20),0,Mathf.Min(32,height-4),Mathf.Min(32,height-4));
            if(wide)
            {
                Place(r.name.rectTransform,-half+129,0,118,25);r.name.fontSize=f.screenCopy?16:15;
                Place(r.score.rectTransform,half-43,0,77,30);r.score.fontSize=f.screenCopy?24:23;
                Place(r.status.rectTransform,48,0,130,21);r.status.fontSize=11;
                for(int i=0;i<5;i++)Place(r.cans[i].rectTransform,(f.screenCopy?12:-21)+i*(f.screenCopy?22:33),0,f.screenCopy?14:25,Mathf.Min(31,height-5));
            }
            else if(hasOrder)
            {
                Place(r.name.rectTransform,-half+123,11,170,19);r.name.fontSize=13;
                Place(r.score.rectTransform,half-30,0,58,25);r.score.fontSize=22;
                for(int i=0;i<5;i++)Place(r.cans[i].rectTransform,-92+i*34,-7,25,24);
            }
            else
            {
                Place(r.name.rectTransform,-half+85,6,98,18);r.name.fontSize=11;
                Place(r.score.rectTransform,half-30,0,57,25);r.score.fontSize=17;
                Place(r.status.rectTransform,-half+87,-7,103,14);r.status.fontSize=9;
                for(int i=0;i<5;i++)Place(r.cans[i].rectTransform,-half+45+i*15,-9,10,11);
            }
            Place(r.seal.rectTransform,half-26,0,18,18);
            FitTextHeight(r.name);FitTextHeight(r.score);FitTextHeight(r.status);
        }
        private void BindPlayer(Row row,int id,string label)
        {
            Set(row.name,label);var p=SessionScoreboard.Current?.FindPlayer(id);int character=p!=null?p.CharacterIndex:id;
            if(CharacterSelection.Current is Igruha.Networking.CharacterSelectionManager selection)character=selection.CharacterOf(id);
            row.portrait.sprite=portraits!=null && character>=0 && character<portraits.Length?portraits[character]:null;
            row.portrait.enabled=row.portrait.sprite!=null;
        }
        public void Clear()
        {VisibleRows=VisibleArrangements=0;if(faces==null)return;foreach(var f in faces){Show(f.idle,false);foreach(var r in f.rows)if(r.root!=null)Show(r.root.gameObject,false);}}
    }
}
