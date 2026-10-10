#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Core.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace Igruha.Minigames.Mosquitoes
{
    /// <summary>Real action-map input in separate development players, without --bot.</summary>
    public sealed class MosquitoInputProbe : MonoBehaviour
    {
        private MosquitoesMinigame game;
        private Keyboard keyboard;
        private Mouse mouse;
        private Camera view;
        private Unity.Cinemachine.CinemachineCamera flightRig;
        private bool navigation, entered, bit, woke, sleptAgain, reported;
        private float interactIn;
        private string captureFolder;
        private bool capturedEntry, capturedBite, capturedWake;
        private float elapsed, stepTime;
        private int step;
        private Vector3 start;
        private bool sampled, finished, readySent;
        private float readyWait;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (LaunchArguments.HasFlag("--mosquito-input-check") || LaunchArguments.TryGetValue("--mosquito-check", out _)) SceneManager.sceneLoaded += Loaded;
        }
        private static void Loaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Hub" || scene.name == "Mosquitoes" && LaunchArguments.HasFlag("--mosquito-input-check"))
                new GameObject("MosquitoInputProbe").AddComponent<MosquitoInputProbe>();
        }
        private void Start()
        {
            game = FindFirstObjectByType<MosquitoesMinigame>();
            if (game == null) return;
            keyboard = InputSystem.AddDevice<Keyboard>("MosquitoProbeKeyboard");
            mouse = InputSystem.AddDevice<Mouse>("MosquitoProbeMouse");
            view = Camera.main;
            flightRig = FindFirstObjectByType<Unity.Cinemachine.CinemachineCamera>();
            navigation = LaunchArguments.TryGetValue("--mosquito-input-check", out var mode) && mode == "navigation";
            if (LaunchArguments.TryGetValue("--mosquito-capture", out captureFolder)) System.IO.Directory.CreateDirectory(captureFolder);
            game.ResultsReported += Results;
        }
        private void Results(MinigameResults results)
        {
            Debug.Log($"MOSQUITO_INPUT RESULTS local={game.LocalId} bodies={game.Bodies.Count}");
        }
        private void Update()
        {
            if (game == null)
            {
                var selection = CharacterSelection.Current;
                if (selection != null && !selection.HasChosen && NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
                {
                    readyWait += Time.unscaledDeltaTime;
                    if (readyWait > 1) { readyWait = 0; selection.Choose((int)NetworkManager.Singleton.LocalClientId % 8); }
                }
                return;
            }
            if (game.AwaitingTutorialReady && !readySent)
            {
                readyWait += Time.unscaledDeltaTime;
                if (readyWait > 1.5f)
                {
                    var tutorial = FindFirstObjectByType<TutorialScreen>();
                    if (tutorial != null && tutorial.IsVisible) { tutorial.RequestReady(); readySent = true; }
                }
            }
            if (game.Phase != MinigamePhase.Round || !game.ControlsAvailable) return;
            elapsed += Time.deltaTime;
            if (navigation) TickNavigation();
            if (elapsed > (navigation ? 25 : 8) && !finished)
            {
                finished = true;
                if (NetworkManager.Singleton.IsServer) game.GetComponent<RoundTimer>().StartTimer(.3f);
            }
            var own = game.FindBody(game.LocalId);
            if (navigation) return;
            if (own == null || !own.IsAlive || step >= 3) return;
            if (!sampled)
            {
                start = own.Position; sampled = true;
                if (step == 0)
                {
                    bool correct = Vector3.Distance(start, game.FlightSpawn(own.Id)) < .3f;
                    Debug.Log($"MOSQUITO_SPAWN {(correct ? "PASS" : "FAIL")} local={game.LocalId} position={start:F2} expected={game.FlightSpawn(own.Id):F2}");
                }
            }
            stepTime += Time.deltaTime;
            InputSystem.QueueStateEvent(keyboard, step == 0 ? new KeyboardState(Key.Space) :
                step == 1 ? new KeyboardState(Key.W) : new KeyboardState());
            if (stepTime < 1.2f) return;
            Vector3 delta = own.Position - start;
            bool passed = step == 0 ? delta.y > 1 : step == 1 ? new Vector2(delta.x, delta.z).magnitude > 1 : own.Velocity.magnitude < .2f;
            Debug.Log($"MOSQUITO_INPUT {(passed ? "PASS" : "FAIL")} local={game.LocalId} step={step} delta={delta:F2} speed={own.Velocity.magnitude:F2} owner={own.IsOwner} kinematic={own.FlightKinematic}");
            Debug.Log("MOSQUITO_INPUT STATE " + own.InputDiagnostic);
            step++; stepTime = 0; sampled = false;
        }
        private void TickNavigation()
        {
            var state = new KeyboardState();
            var mouseState = new MouseState();
            var own = game.FindBody(game.LocalId);
            if (captureFolder != null && own != null)
            {
                if (!capturedEntry && elapsed > .5f) { capturedEntry = true; Capture("entry"); }
                if (!capturedBite && own.IsAttached && own.BiteFraction > .3f) { capturedBite = true; Capture("bite"); }
                if (!capturedWake && woke && game.Sleep.Phase == GiantPhase.Awake) { capturedWake = true; Capture("awake"); }
            }
            woke |= game.Sleep.Phase == GiantPhase.Awake;
            sleptAgain |= woke && game.Sleep.Phase == GiantPhase.Sleeping;
            if (own == null)
            {
                interactIn -= Time.deltaTime;
                if (game.Sleep.CanSleep && interactIn <= 0)
                {
                    interactIn = 1.2f;
                    var giant = SessionScoreboard.Current.LocalPlayer.Avatar;
                    var reader = giant.GetComponent<Igruha.Core.Player.PlayerInputReader>();
                    var interactor = giant.GetComponent<Igruha.Core.Interaction.PlayerInteractor>();
                    Debug.Log($"MOSQUITO_INTERACT local={game.LocalId} pos={giant.Position:F2} reader={reader.enabled}/{reader.Suspended} locked={giant.MovementLocked} target={interactor.CurrentInteractable?.InteractionPrompt}");
                }
                // Interact uses the project's frozen Hold interaction, not a one-frame tap.
                if (game.Sleep.CanSleep && interactIn > .5f) state.Press(Key.E);
            }
            else if (own.IsAlive)
            {
                entered |= own.HasEntered; bit |= own.BiteCooldown > 0;
                Vector3 target = own.HasEntered ? (bit || own.BiteCooldown > 0 || !game.Sleep.CanBite ? new Vector3(0, 1.8f, 0) : game.BiteAnchor(own.Id)) : MosquitoWindowEntry.Approach;
                if (own.HasEntered && Vector2.Distance(new Vector2(own.Position.x, own.Position.z), new Vector2(target.x, target.z)) > .45f)
                    target.y = game.Config.FlightCeiling - .08f;
                Vector3 delta = target - own.Position;
                Vector3 forward = view != null ? view.transform.forward : Vector3.forward; forward.y = 0; forward.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                float x = Vector3.Dot(delta, right), z = Vector3.Dot(delta, forward);
                if (x > .12f) state.Press(Key.D); else if (x < -.12f) state.Press(Key.A);
                if (z > .12f) state.Press(Key.W); else if (z < -.12f) state.Press(Key.S);
                if (delta.y > .10f) state.Press(Key.Space); else if (delta.y < -.10f) state.Press(Key.LeftCtrl);
                if (delta.magnitude < 1.5f) state.Press(Key.LeftShift);
                if (!bit && own.HasEntered && game.Sleep.CanBite && own.BiteCooldown <= 0 && Vector3.Distance(own.Position, game.BiteAnchor(own.Id)) < game.Config.BiteDistance * .95f)
                    mouseState = new MouseState { buttons = 1 };
            }
            InputSystem.QueueStateEvent(keyboard, state); InputSystem.QueueStateEvent(mouse, mouseState);
            if (elapsed > 24 && !reported)
            {
                reported = true;
                bool cameraBound = own == null || flightRig != null && flightRig.Follow == own.CameraTarget && flightRig.LookAt == own.CameraTarget;
                bool passed = (own == null ? woke && sleptAgain : entered && bit) && cameraBound;
                Debug.Log($"MOSQUITO_NAV {(passed ? "PASS" : "FAIL")} local={game.LocalId} entered={entered} bit={bit} woke={woke} sleptAgain={sleptAgain} cameraBound={cameraBound}");
            }
        }
        private void Capture(string label)
        {
            var own = game.FindBody(game.LocalId);
            var cameraController = FindFirstObjectByType<Igruha.Core.CameraSystems.MinigameCameraController>();
            if (own != null && view != null)
                Debug.Log($"MOSQUITO_FRAME {label} camera={view.transform.position:F2} body={own.Position:F2} follow={cameraController.CurrentTarget.position:F2} viewport={view.WorldToViewportPoint(own.Position):F2}");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(captureFolder, "player-" + game.LocalId + "-" + label + ".png"));
        }
        private void OnDestroy()
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (game != null) game.ResultsReported -= Results;
        }
    }
}
#endif
