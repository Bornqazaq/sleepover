using System;
using System.Collections.Generic;
using Igruha.Core.Session;
using Igruha.Minigames.CansOrder;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Igruha.Minigames.Circus.CircusUiLayout;

namespace Igruha.Minigames.Circus
{
    /// <summary>Only displays data already published by the game. The same cards appear on all four faces.</summary>
    public sealed class CircusBoardView : MonoBehaviour
    {
        [Serializable] public sealed class Row
        {
            public RectTransform root;
            public Image portrait;
            public TMP_Text name, score, status;
            public CircusUiGraphic seal;
            public CircusUiGraphic[] cans;
        }
        [Serializable] public sealed class Face
        {
            public TMP_Text title, subtitle;
            public GameObject idle;
            public Row[] rows;
        }
        [SerializeField] private Face[] faces;
        [SerializeField] private Sprite[] portraits;
        [SerializeField] private CansOrderConfig palette;
        private int total,featured,featureCursor,otherCursor,canCount=5;
        public int VisibleRows { get; private set; }
        public int VisibleArrangements { get; private set; }

        public void Construct(RectTransform[] screens,TMP_FontAsset font,Sprite[] portraitSprites,CansOrderConfig colors)
        {
            portraits=portraitSprites;palette=colors;faces=new Face[screens.Length];
            for(int f=0;f<screens.Length;f++)
            {
                var root=Rect(screens[f],"CircusFace",0,0,552,206);
                Card(root,"Velvet",0,0,552,206,Wine);
                var face=faces[f]=new Face();
                face.title=Text(root,"Title",font,"ПОРЯДОК БАНОК",0,81,510,32,25,Cream);face.title.fontStyle=FontStyles.Bold;
                face.subtitle=Text(root,"Subtitle",font,"",0,57,510,18,12,Gold);
                var idle=Rect(root,"TaskIllustration",0,-27,520,135);face.idle=idle.gameObject;
                Icon(idle,"CanA",CircusUiGraphic.Sign.Can,-120,8,58,80,Gold).Symbol=3;
                Icon(idle,"Swap",CircusUiGraphic.Sign.Swap,0,8,51,51,Cream);
                Icon(idle,"CanB",CircusUiGraphic.Sign.Can,120,8,58,80,Gold).Symbol=3;
                Text(idle,"Caption",font,"НАЙДИ СВОЙ РИТМ",0,-46,490,23,17,Cream);
                face.rows=new Row[8];
                for(int i=0;i<8;i++)
                {
                    var row=face.rows[i]=new Row();
                    row.root=Rect(root,"Player_"+i,0,0,528,60);
                    var bg=Icon(row.root,"Card",CircusUiGraphic.Sign.Ticket,0,0,528,60,Ink);
                    bg.rectTransform.anchorMin=Vector2.zero;bg.rectTransform.anchorMax=Vector2.one;bg.rectTransform.offsetMin=Vector2.zero;bg.rectTransform.offsetMax=Vector2.zero;
                    row.portrait=Portrait(row.root,-235,0,43);
                    row.name=Text(row.root,"Name",font,"",-150,0,114,25,16,Cream,TextAlignmentOptions.MidlineLeft);
                    row.name.fontStyle=FontStyles.Bold;
                    row.score=Text(row.root,"Score",font,"",223,4,64,31,25,Gold);row.score.fontStyle=FontStyles.Bold;
                    row.status=Text(row.root,"Status",font,"",0,0,210,20,12,Muted);
                    row.seal=Icon(row.root,"Seal",CircusUiGraphic.Sign.Check,193,0,28,28,Mint);
                    row.cans=new CircusUiGraphic[5];
                    for(int c=0;c<5;c++)row.cans[c]=Icon(row.root,"Can_"+c,CircusUiGraphic.Sign.Can,-47+c*40,0,34,42,Cream);
                    row.root.gameObject.SetActive(false);
                }
            }
        }
        public void Header(string title,string subtitle)
        {if(faces==null)return;foreach(var f in faces){Set(f.title,title);Set(f.subtitle,subtitle);}}
        public void Task(string title,string subtitle,bool cans)
        {
            Clear();Header(title,subtitle);
            foreach(var f in faces)
            {
                Show(f.idle,true);
                var caption=f.idle.GetComponentInChildren<TMP_Text>();Set(caption,cans?"ВЫБЕРИ  ·  ПОМЕНЯЙ  ·  ПОДТВЕРДИ":"ПОЧУВСТВУЙ ВРЕМЯ");
            }
        }
        public void Begin(int count,int arrangements,int cans=5)
        {
            canCount=cans;
            total=count;featured=arrangements;featureCursor=otherCursor=0;VisibleRows=VisibleArrangements=0;
            if(faces==null)return;
            foreach(var f in faces){Show(f.idle,false);foreach(var r in f.rows)Show(r.root.gameObject,false);}
        }
        public void CansRow(int index,int playerId,string playerName,IReadOnlyList<int> order,int matches,bool confirmed,bool solved)
        {
            bool hasOrder=order!=null && !solved && confirmed;
            int slot=hasOrder?featureCursor++:otherCursor++;
            foreach(var f in faces)
            {
                Row row=f.rows[index];Arrange(row,index,slot,hasOrder);BindPlayer(row,playerId,playerName);
                Set(row.score,solved?"":confirmed?matches+"/"+canCount:"—");row.score.color=solved?Mint:Gold;
                Set(row.status,solved?"СОБРАЛ!":!confirmed?"НЕ ПОДТВЕРДИЛ":hasOrder?"":"СОВПАДЕНИЙ");
                Show(row.status.gameObject,!hasOrder);Show(row.seal.gameObject,solved);
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
                var row=f.rows[index];Arrange(row,index,index,false);BindPlayer(row,playerId,playerName);
                Set(row.score,value);row.score.color=faulted?Red:Gold;Set(row.status,alive?"":"ВЫБЫЛ");
                Show(row.status.gameObject,!alive);Show(row.seal.gameObject,false);
                for(int c=0;c<row.cans.Length;c++)
                {var icon=row.cans[c];Show(icon.gameObject,alive && c<limit);icon.Kind=CircusUiGraphic.Sign.Shield;icon.color=c<limit-errors?Mint:new Color(.4f,.25f,.25f,.45f);}
            }
            VisibleRows++;
        }
        private void Arrange(Row r,int index,int slot,bool hasOrder)
        {
            bool wide=total<=3;
            float width=wide?528:featured>0?(hasOrder?318:202):258;
            int n=wide?total:featured>0?(hasOrder?Mathf.Max(featured,3):Mathf.Max(total-featured,5)):4;
            float pitch=136f/Mathf.Max(1,n),height=Mathf.Min(64,pitch-4);
            int line=wide?index:featured>0?slot:index%4;
            float x=wide?0:featured>0?(hasOrder?-105:164):(index<4?-135:135);
            Place(r.root,x,44-pitch*.5f-line*pitch,width,height);Show(r.root.gameObject,true);
            float half=width*.5f;
            Place(r.portrait.rectTransform,-half+23,0,Mathf.Min(40,height-4),Mathf.Min(40,height-4));
            if(wide)
            {
                Place(r.name.rectTransform,-half+102,3,112,24);r.name.fontSize=16;
                Place(r.score.rectTransform,half-37,0,65,30);r.score.fontSize=25;
                Place(r.status.rectTransform,20,0,175,21);r.status.fontSize=13;
                for(int i=0;i<5;i++)Place(r.cans[i].rectTransform,-49+i*39,0,33,Mathf.Min(42,height-4));
            }
            else if(hasOrder)
            {
                Place(r.name.rectTransform,-half+111,12,130,19);r.name.fontSize=14;
                Place(r.score.rectTransform,half-27,0,49,25);r.score.fontSize=22;
                for(int i=0;i<5;i++)Place(r.cans[i].rectTransform,-86+i*36,-8,27,27);
            }
            else
            {
                Place(r.name.rectTransform,-half+92,5,112,19);r.name.fontSize=12;
                Place(r.score.rectTransform,half-28,0,55,26);r.score.fontSize=17;
                Place(r.status.rectTransform,-half+99,-6,122,11);r.status.fontSize=8;
                for(int i=0;i<5;i++)Place(r.cans[i].rectTransform,-half+51+i*14,-10,11,11);
            }
            Place(r.seal.rectTransform,half-32,0,22,22);
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
