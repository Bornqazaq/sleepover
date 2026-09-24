using System;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Igruha.Tests
{
    /// <summary>Ручной стенд редактора: ввод клавиатуры проходит через настоящие Input Actions и мотор.</summary>
    public static class TutorialMovementCheck
    {
        private const float Seconds=1.2f, MinimumDistance=.1f;
        private static Keyboard keyboard, previousKeyboard;
        private static PlayerInputReader reader;
        private static PlayerController player;
        private static Vector3 origin;
        private static double endsAt;
        private static bool sawInput, details;
        public static string Result { get; private set; }

        public static string Begin(bool detailedRules)
        {
            if(!EditorApplication.isPlaying || MinigameControllerBase.Current==null ||
                MinigameControllerBase.Current.Phase!=MinigamePhase.Practice) return "Open a practice scene first";
            if(keyboard!=null)return "Check already running";
            foreach(var candidate in UnityEngine.Object.FindObjectsByType<PlayerInputReader>(FindObjectsSortMode.None))
                if(candidate.LocallyControlled){reader=candidate;break;}
            if(reader==null)return "No local input";
            player=reader.GetComponent<PlayerController>();
            details=detailedRules;
            foreach(var screen in UnityEngine.Object.FindObjectsByType<TutorialScreen>(FindObjectsSortMode.None))
                if(screen.IsVisible && screen.RulesExpanded!=details)screen.TogglePractice();
            previousKeyboard=Keyboard.current;
            keyboard=InputSystem.AddDevice<Keyboard>("Tutorial movement check");
            keyboard.MakeCurrent();
            origin=player.Position;
            sawInput=false;
            endsAt=EditorApplication.timeSinceStartup+Seconds;
            Result="Running";
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));
            EditorApplication.update+=Tick;
            return Result;
        }

        private static void Tick()
        {
            if(reader!=null && reader.MoveInput.sqrMagnitude>.1f)sawInput=true;
            if(EditorApplication.timeSinceStartup<endsAt && EditorApplication.isPlaying)return;
            float distance=player!=null?Vector3.Distance(origin,player.Position):0f;
            bool passed=reader!=null && reader.enabled && !reader.Suspended && sawInput && distance>MinimumDistance;
            EditorApplication.update-=Tick;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            InputSystem.RemoveDevice(keyboard);
            keyboard=null;
            previousKeyboard?.MakeCurrent();
            Result="TUTORIAL_MOVEMENT_CHECK "+(passed?"PASS":"FAIL")+" detailed="+details+
                " input="+sawInput+" travelled="+distance.ToString("F2")+"m";
            if(passed)Debug.Log(Result);else Debug.LogError(Result);
        }
    }
}
