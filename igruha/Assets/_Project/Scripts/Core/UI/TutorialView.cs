using System;
using System.Collections.Generic;
using System.Text;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Igruha.Core.UI
{
    /// <summary>Общий игровой HUD разминки: арена остаётся главным содержимым экрана.</summary>
    public sealed class TutorialView : MonoBehaviour
    {
        private const float Width=1920f, Height=1080f, EntranceSeconds=.28f;
        private const int PlayerSlots=8, StepSlots=3, HintSlots=3;
        private static readonly Color Ink=new Color(.075f,.12f,.16f,1f);
        private static readonly Color Paper=new Color(1f,.96f,.87f,.97f);
        private static readonly Color Gold=new Color(1f,.77f,.23f,1f);
        private static readonly Color Coral=new Color(1f,.40f,.27f,1f);
        private static readonly Color Mint=new Color(.40f,.91f,.75f,1f);
        private static readonly Color Muted=new Color(.66f,.75f,.78f,1f);
        private static readonly Color[] PlayerColors={Coral,Mint,Gold,new Color(.61f,.60f,1f),
            new Color(.42f,.77f,1f),new Color(1f,.57f,.76f),new Color(.79f,.88f,.38f),new Color(.90f,.67f,.44f)};
        private TMP_FontAsset font;
        private RectTransform layout, content, brief, details, completedCard;
        private CanvasGroup entrance;
        private TMP_Text title, category, objective, controls, countLabel, readyLabel, rulesLabel;
        private readonly TMP_Text[] steps=new TMP_Text[StepSlots];
        private readonly TMP_Text[] quickHints=new TMP_Text[HintSlots];
        private readonly RectTransform[] playerTiles=new RectTransform[PlayerSlots];
        private readonly TMP_Text[] playerNames=new TMP_Text[PlayerSlots];
        private readonly TMP_Text[] playerStates=new TMP_Text[PlayerSlots];
        private readonly TutorialPanel[] playerDots=new TutorialPanel[PlayerSlots];
        private readonly List<ControlHint> parsedHints=new List<ControlHint>();
        private Button readyButton, retryButton;
        private TutorialPanel readyFace;
        private bool complete, expanded;
        private float appearedAt;

        public void Build(TMP_FontAsset textFont, Action ready, Action toggleRules, Action retry)
        {
            font=textFont;
            var canvas=gameObject.AddComponent<Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder=500;
            var scaler=gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(Width,Height);
            scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<GraphicRaycaster>();
            layout=Rect("TutorialLayout",transform,0,0,Width,Height);
            layout.anchorMin=layout.anchorMax=layout.pivot=new Vector2(.5f,.5f);
            layout.anchoredPosition=Vector2.zero;
            content=layout;
            entrance=layout.gameObject.AddComponent<CanvasGroup>();

            var badge=Panel("PracticeBadge",44,30,206,48,Coral,16);
            badge.rectTransform.localEulerAngles=new Vector3(0,0,2f);
            Text("PracticeLabel","РАЗМИНКА",62,37,170,38,25,Ink,true);
            Panel("NoScorePill",264,34,590,42,Ink,18);
            Text("NoScore","Можно ошибаться. Очки не считаются.",280,40,555,34,21,Paper);
            Text("TitleShadow","",47,94,1050,82,48,Ink,true);
            title=Text("Title","",44,89,1050,82,48,Color.white,true);
            category=Text("Category","",46,150,850,38,21,Gold);

            brief=Group("BriefRules");
            content=brief;
            Card("RulesCard",44,210,388,350,Paper);
            Text("RulesHeading","Как победить",68,232,340,44,28,Ink,true);
            for(int i=0;i<StepSlots;i++)
            {
                Panel("StepDot"+i,68,302+i*78,34,34,i==0?Coral:i==1?Gold:Mint,17);
                Text("StepNumber"+i,(i+1).ToString(),68,303+i*78,34,32,20,Ink,true,TextAlignmentOptions.Center);
                steps[i]=Text("Step"+i,"",116,294+i*78,289,71,24,Ink);
            }
            content=layout;
            Button rules=MakeButton("Rules",44,580,388,56,Ink,toggleRules);
            rulesLabel=Text("RulesLabel","F1   Все правила",62,590,352,42,23,Paper,true,TextAlignmentOptions.Center);

            for(int i=0;i<HintSlots;i++)
            {
                Card("QuickHint"+i,44+i*612,814,596,62,Ink);
                quickHints[i]=Text("QuickHintLabel"+i,"",62+i*612,827,560,40,23,Paper,false,TextAlignmentOptions.Center);
            }

            Card("ReadyTray",44,916,1832,126,new Color(Ink.r,Ink.g,Ink.b,.97f));
            countLabel=Text("ReadyCount","0 / 0 готовы",68,934,248,42,30,Paper,true);
            Text("WaitForEveryone","Начнём вместе",68,980,248,32,20,Muted);
            for(int i=0;i<PlayerSlots;i++)
            {
                playerTiles[i]=Rect("Player"+i,layout,340+i*136,930,126,98);
                content=playerTiles[i];
                Panel("Avatar",43,0,40,40,PlayerColors[i],20);
                Text("PlayerNumber",(i+1).ToString(),43,3,40,32,22,Ink,true,TextAlignmentOptions.Center);
                playerDots[i]=Panel("ReadyDot",73,27,15,15,Muted,8);
                playerNames[i]=Text("Name","",0,44,126,34,20,Paper,false,TextAlignmentOptions.Center);
                playerNames[i].richText=false;
                playerStates[i]=Text("State","",0,75,126,22,15,Muted,false,TextAlignmentOptions.Center);
            }
            content=layout;
            readyButton=MakeButton("Ready",1478,942,366,70,Gold,ready);
            readyFace=readyButton.GetComponent<TutorialPanel>();
            readyLabel=Text("ReadyLabel","F2   Я готов!",1493,956,336,44,29,Ink,true,TextAlignmentOptions.Center);

            details=Group("DetailedRules");
            content=details;
            Card("DetailsCard",44,210,650,574,Paper);
            Text("DetailsHeading","Правила игры",68,229,602,42,28,Ink,true);
            objective=ScrollText("Objective",68,287,600,198,24);
            Text("ControlsHeading","Управление",68,510,602,40,26,Ink,true);
            controls=ScrollText("Controls",68,557,600,197,23);
            content=layout;

            completedCard=Group("PracticeComplete");
            content=completedCard;
            Card("CompleteCard",670,362,644,270,Paper);
            Text("CompleteKicker","ПРОБНЫЙ ЗАХОД ЗАКОНЧИЛСЯ",702,387,580,35,19,Ink,true,TextAlignmentOptions.Center);
            Text("CompleteTitle","Ещё один заход?",702,435,580,62,40,Ink,true,TextAlignmentOptions.Center);
            Text("CompleteHint","Повтори тренировку или нажми «Я готов».",702,497,580,38,22,Ink,false,TextAlignmentOptions.Center);
            retryButton=MakeButton("Retry",827,551,330,57,Mint,retry);
            Text("RetryLabel","Ещё попытка",847,562,290,37,25,Ink,true,TextAlignmentOptions.Center);
            content=layout;
            SetExpanded(false);
        }

        public void Show(MinigameDefinition definition)
        {
            gameObject.SetActive(true);
            complete=false;
            appearedAt=Time.unscaledTime;
            title.text=definition!=null?definition.DisplayName:"Мини-игра";
            layout.Find("TitleShadow").GetComponent<TMP_Text>().text=title.text;
            category.text=definition==null?"":definition.Category==MinigameCategory.Team?"ИГРАЕМ КОМАНДОЙ":
                definition.Category==MinigameCategory.Asymmetric?"У КАЖДОГО СВОЯ РОЛЬ":"КАЖДЫЙ ЗА СЕБЯ";
            for(int i=0;i<StepSlots;i++) steps[i].text=definition!=null && i<definition.TutorialSteps.Length
                ?definition.TutorialSteps[i]:i==0?"Попробуй управление.":i==1?"Посмотри правила по F1.":"Освоился? Нажми F2.";
            for(int i=0;i<HintSlots;i++)quickHints[i].text=definition!=null && i<definition.TutorialQuickHints.Length
                ?definition.TutorialQuickHints[i]:i==0?"WASD — движение":i==1?"F1 — все правила":"F2 — я готов";
            objective.text=definition!=null?definition.Objective:"Освойся на арене перед началом игры.";
            ControlHintParser.Parse(definition!=null?definition.ControlHints:Array.Empty<string>(),parsedHints);
            var text=new StringBuilder();
            for(int i=0;i<parsedHints.Count;i++)
            {
                if(i>0)text.Append("\n\n");
                if(!string.IsNullOrEmpty(parsedHints[i].Key))text.Append(parsedHints[i].Key).Append(" — ");
                text.Append(parsedHints[i].Action);
            }
            controls.text=text.ToString();
            SetExpanded(false);
        }

        public void SetReadiness(IReadOnlyList<SessionPlayer> players,IReadOnlyList<TutorialParticipant> participants,int localId)
        {
            int readyCount=0;
            bool localReady=false,participates=false;
            for(int i=0;i<PlayerSlots;i++)
            {
                bool show=i<participants.Count;
                playerTiles[i].gameObject.SetActive(show);
                if(!show)continue;
                var entry=participants[i];
                if(entry.Ready)readyCount++;
                if(entry.PlayerId==localId){localReady=entry.Ready;participates=true;}
                string playerName="Игрок "+(i+1);
                for(int j=0;j<players.Count;j++)if(players[j].Id==entry.PlayerId){playerName=players[j].DisplayName;break;}
                playerNames[i].text=entry.PlayerId==localId?playerName+" · вы":playerName;
                playerStates[i].text=entry.Ready?"ГОТОВ":"ПРОБУЕТ";
                playerStates[i].color=entry.Ready?Mint:Muted;
                playerDots[i].color=entry.Ready?Mint:Muted;
            }
            countLabel.text=readyCount+" / "+participants.Count+" готовы";
            readyButton.interactable=participates;
            retryButton.interactable=participates;
            readyFace.color=localReady?Mint:Gold;
            readyLabel.text=localReady?"F2   Готов! Отменить":participates?"F2   Я готов!":"Наблюдаешь";
        }

        public void SetExpanded(bool value)
        {
            expanded=value;
            brief.gameObject.SetActive(!value);
            details.gameObject.SetActive(value);
            rulesLabel.text=value?"F1   Свернуть правила":"F1   Все правила";
            completedCard.gameObject.SetActive(complete && !value);
            if(EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(null);
        }

        public void SetPracticeComplete()
        {
            complete=true;
            completedCard.gameObject.SetActive(!expanded);
        }

        private void Update()
        {
            float progress=Mathf.Clamp01((Time.unscaledTime-appearedAt)/EntranceSeconds);
            if(progress>=1f && entrance.alpha>=1f)return;
            float ease=1f-Mathf.Pow(1f-progress,3f);
            entrance.alpha=ease;
            layout.localScale=Vector3.one*Mathf.Lerp(.97f,1f,ease);
        }

        private RectTransform Group(string name) => Rect(name,layout,0,0,Width,Height);
        private static RectTransform Rect(string name,Transform parent,float x,float y,float width,float height)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=new Vector2(x,-y);
            rect.sizeDelta=new Vector2(width,height);
            return rect;
        }
        private TutorialPanel Panel(string name,float x,float y,float width,float height,Color color,float radius=20)
        {
            var rect=Rect(name,content,x,y,width,height);
            var panel=rect.gameObject.AddComponent<TutorialPanel>();
            panel.Radius=radius;
            panel.color=color;
            panel.raycastTarget=false;
            return panel;
        }
        private void Card(string name,float x,float y,float width,float height,Color color)
        {
            Panel(name+"Shadow",x,y+6,width,height,new Color(0,0,0,.25f));
            Panel(name,x,y,width,height,color);
        }
        private TMP_Text Text(string name,string value,float x,float y,float width,float height,float size,Color color,
            bool bold=false,TextAlignmentOptions alignment=TextAlignmentOptions.TopLeft)
        {
            var rect=Rect(name,content,x,y,width,height);
            var text=rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font=font; text.text=value; text.fontSize=size; text.color=color;
            text.fontStyle=bold?FontStyles.Bold:FontStyles.Normal;
            text.alignment=alignment;text.raycastTarget=false;
            text.overflowMode=TextOverflowModes.Ellipsis;
            return text;
        }
        private Button MakeButton(string name,float x,float y,float width,float height,Color color,Action action)
        {
            Panel(name+"Shadow",x,y+5,width,height,new Color(0,0,0,.3f));
            var face=Panel(name,x,y,width,height,color);
            face.raycastTarget=true;
            var button=face.gameObject.AddComponent<Button>();
            button.targetGraphic=face;
            button.onClick.AddListener(()=>action());
            return button;
        }
        private TMP_Text ScrollText(string name,float x,float y,float width,float height,float size)
        {
            var viewport=Rect(name+"Viewport",content,x,y,width,height);
            var surface=viewport.gameObject.AddComponent<Image>();surface.color=new Color(0,0,0,.01f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll=viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport=viewport;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=32f;
            var text=Text(name,"",0,0,width-18,height,size,Ink);
            text.rectTransform.SetParent(viewport,false);
            text.rectTransform.anchorMax=new Vector2(1,1);
            text.rectTransform.sizeDelta=new Vector2(-18,0);
            text.overflowMode=TextOverflowModes.Overflow;
            text.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            scroll.content=text.rectTransform;
            var rail=Rect(name+"Scroll",viewport,width-7,0,7,height);
            var background=rail.gameObject.AddComponent<Image>();background.color=new Color(Ink.r,Ink.g,Ink.b,.12f);
            var scrollbar=rail.gameObject.AddComponent<Scrollbar>();
            var thumb=Rect("Thumb",rail,0,0,7,height);
            thumb.anchorMin=Vector2.zero;thumb.anchorMax=Vector2.one;thumb.offsetMin=thumb.offsetMax=Vector2.zero;
            var handle=thumb.gameObject.AddComponent<Image>();handle.color=Coral;
            scrollbar.handleRect=thumb;scrollbar.targetGraphic=handle;scrollbar.direction=Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar=scrollbar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
            return text;
        }
    }
}
