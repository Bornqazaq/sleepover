#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.Traps;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>Explicit editor-only regression. Add in Play Mode; never auto-runs.</summary>
    public sealed class DuckHuntInteractionCheck : MonoBehaviour
    {
        private Keyboard keyboard;
        private PlayerController actor;
        private PlayerInputReader input;
        private int doorClosures;
        private bool failed;

        private IEnumerator Start()
        {
            var mg=FindFirstObjectByType<DuckHuntMinigame>();
            var setup=new SerializedObject(mg);setup.FindProperty("localPlayerIsHunter").boolValue=false;setup.FindProperty("driveDummyDucks").boolValue=false;setup.ApplyModifiedPropertiesWithoutUndo();
            typeof(DuckHuntMinigame).GetMethod("AssignRoles",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(mg,null);
            actor=SessionScoreboard.Current?.LocalPlayer?.Avatar;
            if(actor==null){Fail("No local player");yield break;}
            input=actor.GetComponent<PlayerInputReader>();input.EngageAutopilot();
            foreach(var bot in FindObjectsByType<DebugPlayerBot>(FindObjectsSortMode.None))bot.enabled=false;
            yield return new WaitForSeconds(1);
            yield return Slope(false);
            yield return Slope(true);
            typeof(PlayerInputReader).GetProperty("Autopilot").SetValue(input,false);
            input.SetSuspended(false);
            keyboard=Keyboard.current;
            if(keyboard==null){Fail("No keyboard available");yield break;}
            InputSystem.EnableDevice(keyboard);
            var gameView=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            EditorWindow.GetWindow(gameView).Focus();
            var door=FindFirstObjectByType<DoorTrap>();
            door.ClosedChanged+=OnDoor;
            yield return TapLever("_Arena/Tower/Floor_3/Lever_Door");
            if(doorClosures!=1)Fail("50ms E tap did not close door once: "+doorClosures);
            // Keep E down across the ready transition: it must not retrigger.
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.E));
            yield return new WaitForSeconds(11);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            yield return null;
            if(doorClosures!=1)Fail("Holding E retriggered the door");
            else Debug.Log("DH_INTERACTION PASS tap and no repeat across cooldown");
            door.ClosedChanged-=OnDoor;
            yield return TapLever("_Arena/Tower/Floor_4/Lever_Collapse");
            if(!FindFirstObjectByType<CollapsingFloorTrap>().IsSprung)Fail("Collapse did not open on E tap");
            yield return new WaitForSeconds(4);
            if(FindFirstObjectByType<CollapsingFloorTrap>().IsSprung)Fail("Collapse did not restore");
            Debug.Log("DH_INTERACTION complete failed="+failed);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());keyboard=null;
        }

        private IEnumerator Slope(bool down)
        {
            input.DriveMove(Vector2.zero);
            actor.TeleportTo(new Vector3(30.24f,down?2.6f:1.2f,down?3.1f:7.3f),Quaternion.identity);
            yield return new WaitForSeconds(.6f);
            float start=Time.time;
            while((down?actor.Position.z<7.3f:actor.Position.z>3.1f)&&Time.time-start<8)
            {
                input.DriveMove(actor.WorldToMoveInput(down?Vector3.forward:Vector3.back));
                yield return new WaitForFixedUpdate();
            }
            input.DriveMove(Vector2.zero);
            float elapsed=Time.time-start;
            if(elapsed>=8)Fail("Slope traversal stalled");
            Debug.Log("DH_INTERACTION slope "+(down?"DOWN":"UP")+" seconds="+elapsed.ToString("F3"));
        }

        private IEnumerator TapLever(string path)
        {
            var lever=GameObject.Find(path);var pedestal=lever.transform.Find("Pedestal");var handle=lever.transform.Find("Handle");
            actor.TeleportTo(new Vector3(pedestal.position.x,pedestal.position.y-.35f,pedestal.position.z+1.0f),Quaternion.identity);
            yield return new WaitForSeconds(.8f);
            if(GameObject.Find("Press E to use lever")==null)Fail("Proximity prompt missing "+path);
            else Debug.Log("DH_INTERACTION prompt visible "+path);
            ScreenCapture.CaptureScreenshot(Application.dataPath+"/../Captures/DuckHunt/"+(path.Contains("Door")?"lever-door-gameview.png":"lever-collapse-gameview.png"));
            Quaternion before=handle.localRotation;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.E));
            yield return new WaitForSeconds(.05f);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            yield return new WaitForSeconds(.40f);
            float angle=Quaternion.Angle(before,handle.localRotation);
            if(angle<55)Fail("Lever did not physically turn "+angle);
            else Debug.Log("DH_INTERACTION handle travel="+angle.ToString("F1"));
        }

        private void OnDoor(bool closed){if(closed)doorClosures++;}
        private void Fail(string text){failed=true;Debug.LogError("DH_INTERACTION FAIL "+text);}
        private void OnDestroy(){if(keyboard!=null)InputSystem.QueueStateEvent(keyboard,new KeyboardState());}
    }
}
#endif
