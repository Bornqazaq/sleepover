#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Igruha.Core.Interaction;
using Igruha.Core.Items;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Igruha.Tests
{
    public sealed class CountdownTestGame : MinigameControllerBase
    {
        protected override void OnRoundStarted() => SetStartCountdownActive(true);
        protected override void CollectResults(MinigameResults results) { }
        public void Release() => SetStartCountdownActive(false);
        public void Lock() => SetStartCountdownActive(true);
    }

    public sealed class CountdownTestInteraction : MonoBehaviour, IInteractable
    {
        public int Uses;
        public string InteractionPrompt => "Test";
        public bool CanInteract(PlayerController player) => true;
        public void Interact(PlayerController player) => Uses++;
    }

    public sealed class StartCountdownTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private PlayerController motor;
        private PlayerInputReader input;
        private CountdownTestGame game;
        private GameObject Create(string name) { var go = new GameObject(name); objects.Add(go); return go; }

        [UnitySetUp]
        public IEnumerator Setup()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); objects.Add(floor);
            floor.layer = LayerMask.NameToLayer("Ground");
            floor.transform.position = new Vector3(900,-.25f,900); floor.transform.localScale = new Vector3(30,.5f,30);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Player.prefab");
            var go = Object.Instantiate(prefab); objects.Add(go);
            motor = go.GetComponent<PlayerController>(); input = go.GetComponent<PlayerInputReader>();
            input.EngageAutopilot(); motor.SetCameraReference(Create("Move reference").transform);
            motor.TeleportTo(new Vector3(900,.1f,900),Quaternion.identity);
            yield return new WaitForSeconds(.3f);
            game = Create("Countdown test").AddComponent<CountdownTestGame>();
            input.DriveMove(Vector2.up); input.DriveJump(); input.DrivePushHold(true); input.DriveInteract();
            game.StartMinigame(new[] { new SessionPlayer(0,"Test") { Avatar = motor } });
        }

        [UnityTest]
        public IEnumerator CountdownRejectsBufferedAndNewActionsThenRestoresControl()
        {
            Assert.That(game.StartCountdownActive, Is.True);
            Assert.That(input.Suspended, Is.True);
            var start = motor.Position;
            int punches = 0;
            var push = motor.GetComponent<PlayerPushAbility>(); push.PunchStarted += () => punches++;
            float until = Time.time + .5f;
            while (Time.time < until)
            {
                input.DriveMove(Vector2.up); input.DriveJump(); input.DrivePushHold(true);
                input.DriveInteract(); input.DriveInteractHold(true); input.DrivePose(2); push.RequestPush();
                Assert.That(input.MoveInput, Is.EqualTo(Vector2.zero));
                Assert.That(input.JumpPressed || input.PushPressed || input.InteractPressed || input.InteractHeld, Is.False);
                Assert.That(input.PoseRequest, Is.Zero);
                yield return null;
            }
            Assert.That(Vector2.Distance(new Vector2(start.x,start.z), new Vector2(motor.Position.x,motor.Position.z)), Is.LessThan(.03f));
            Assert.That(punches, Is.Zero);
            Assert.That(motor.IsGrounded, Is.True);
            game.Release();
            Assert.That(input.Suspended, Is.False);
            Assert.That(input.JumpPressed || input.PushPressed || input.InteractPressed, Is.False);
            input.DriveMove(Vector2.up);
            yield return new WaitForSeconds(.4f);
            Assert.That(Vector3.Distance(start,motor.Position), Is.GreaterThan(.5f));
            input.DriveMove(Vector2.zero); input.DriveJump();
            yield return new WaitForSeconds(.12f);
            Assert.That(motor.IsGrounded, Is.False);
        }

        [UnityTest]
        public IEnumerator CountdownProtectsInteractionPickupAndServerThrowWithoutBreakingDrop()
        {
            var target = Create("Interaction"); target.transform.position = motor.Position + Vector3.forward*.5f;
            target.AddComponent<BoxCollider>(); var action = target.AddComponent<CountdownTestInteraction>();
            var interactor = motor.GetComponent<PlayerInteractor>();
            interactor.ExecuteInteraction(target); Assert.That(action.Uses, Is.Zero);
            var prop = Create("Prop"); prop.transform.position = target.transform.position;
            prop.AddComponent<BoxCollider>(); prop.AddComponent<Rigidbody>(); var item = prop.AddComponent<PickupItem>();
            var carry = motor.GetComponent<PlayerCarryAbility>();
            Assert.That(carry.TryPickup(item), Is.False);
            game.Release(); interactor.ExecuteInteraction(target); Assert.That(action.Uses, Is.EqualTo(1));
            Assert.That(carry.TryPickup(item), Is.True);
            game.Lock(); carry.ServerThrow(true); Assert.That(carry.IsCarrying, Is.True);
            carry.Drop(); Assert.That(carry.IsCarrying, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CountdownDoesNotClearRoleLocksOrLeakIntoTheNextScene()
        {
            input.SetSuspended(true); motor.MovementLocked = true;
            game.Release(); Assert.That(input.Suspended, Is.True); Assert.That(motor.MovementLocked, Is.True);
            input.SetSuspended(false); Assert.That(input.Suspended, Is.False);
            game.Lock(); Object.Destroy(game.gameObject); yield return null;
            Assert.That(input.Suspended, Is.False);
            Assert.That(motor.MovementLocked, Is.True);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (var go in objects) if (go != null) Object.Destroy(go);
            objects.Clear(); yield return null;
        }
    }
}
#endif
