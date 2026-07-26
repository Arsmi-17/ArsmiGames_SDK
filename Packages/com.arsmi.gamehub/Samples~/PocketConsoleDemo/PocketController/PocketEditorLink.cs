// PocketEditorLink.cs — drop this anywhere in your Unity project, once.
//
// It connects the Editor to the local Pocket Console harness so a real phone press runs your
// real OnPocketInput handler in play mode, with no WebGL build in the loop.
//
// It is wrapped in UNITY_EDITOR and does nothing in a build: in a build the platform delivers
// pocket input through GameHubBridge.jslib and SendMessage, which is the path this only
// imitates. WebGL forbids sockets; the Editor does not.
//
// Honest limit #1: outbound PocketReady/PocketSchema only Debug.Log in the Editor
// (GameHubBridge.cs), so the harness cannot learn maxPlayers or your control schema from the
// game here — it reads both from pocket.controller.json. That is why `pocket-console check`
// still requires one real WebGL run before it will write a shippable archive.
//
// PocketState is the exception, since SDK 4.3.0: the Bridge raises OnPocketStateEnvelope when
// there is no .jslib to send through, and this link forwards it, so your phone really does change
// screen in play mode. Requires package 4.3.0 or newer — on an older one the event does not exist
// and this file will not compile.
//
// Honest limit #2 — read this before you set GameSubscribesToPocketInput below:
// GameHubBridge.OnPocketInput is a C# event, and outside the class that declares it, C# only
// lets you += or -= it — it does not let this script null-test it (`Bridge.OnPocketInput !=
// null` from here would not compile). So this link CANNOT ask the Bridge, from the outside,
// whether your game actually subscribed. GameSubscribesToPocketInput below is you telling it,
// not the Editor proving it — which is exactly why it defaults to false and why
// `pocket-console check` refuses to accept a tier-2 "wired: true" as evidence: it demands
// tier-3 presses (a real WebGL build) and fails immediately with "No evidence from the
// production path" when there are none. A mis-ticked box here cannot manufacture a pass.
#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Arsmi.GameHub.PocketHarness
{
    public class PocketEditorLink : MonoBehaviour
    {
        [Tooltip("The URL printed by `npm run pocket:dev`. Use your machine's address, not localhost, if Unity runs elsewhere.")]
        public string HarnessUrl = "ws://localhost:4310/unity";

        [Tooltip("The GameHubBridge in your scene. Left empty, the link finds it.")]
        public GameHubBridge Bridge;

        [Tooltip(
            "Tick this once you have wired Bridge.OnPocketInput += yourHandler in your own code. " +
            "The Editor cannot check this for you from outside the Bridge (OnPocketInput is a C# " +
            "event; only += and -= are legal from another class), so this is a self-report, not " +
            "proof. It only affects what this harness's 'wiring' page shows you during iteration. " +
            "`pocket-console check` does not trust it either way — it requires a real tier-3 " +
            "(WebGL) run before it will call anything shippable."
        )]
        public bool GameSubscribesToPocketInput = false;

        private ClientWebSocket _socket;
        private CancellationTokenSource _cancel;

        // Unity's API is main-thread only, so the socket thread parks frames here and Update
        // drains them. Calling Bridge directly from the receive loop would throw.
        private readonly ConcurrentQueue<string> _inbound = new ConcurrentQueue<string>();

        // Outbound screen changes, parked the same way for the opposite reason: the Bridge raises
        // OnPocketStateEnvelope on the main thread, and awaiting a socket send from inside that
        // handler would block a frame. Update drains this.
        private readonly ConcurrentQueue<string> _outbound = new ConcurrentQueue<string>();

        private static PocketEditorLink _instance;

        /// <summary>
        /// Attaches itself, so "drop this in once" is literally true.
        /// </summary>
        /// <remarks>
        /// Without this the file compiles, nothing ever adds the component, and a developer sits
        /// in play mode waiting for a phone press that cannot arrive — with no error anywhere,
        /// because a MonoBehaviour nobody attached is not an error. That is exactly what happened
        /// on the first real attempt.
        ///
        /// AfterSceneLoad, so GameHubBridge already exists: games bootstrap it BeforeSceneLoad.
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var host = new GameObject("Pocket Editor Link");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<PocketEditorLink>();
        }

        private void Awake()
        {
            // A hand-placed copy plus the bootstrapped one would open two sockets and deliver every
            // press twice, which reads as the phone double-firing.
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            _instance = this;
        }

        private void Start()
        {
            // FindObjectOfType is obsolete from 2023.1 onward (FindFirstObjectByType replaces
            // it), but FindFirstObjectByType doesn't exist on older Editors — this template
            // ships to unknown Unity versions, so both must compile clean.
#if UNITY_2023_1_OR_NEWER
            if (Bridge == null) Bridge = FindFirstObjectByType<GameHubBridge>();
#else
            if (Bridge == null) Bridge = FindObjectOfType<GameHubBridge>();
#endif
            if (Bridge == null)
            {
                Debug.LogError("[PocketEditorLink] No GameHubBridge in the scene; nothing to deliver input to.");
                return;
            }
            // Outside WebGL the Bridge has no .jslib to push a screen through, so PocketState ends
            // at a Debug.Log unless something carries the envelope out. This is that something:
            // without it, presses reach your game in play mode but the phone never changes screen,
            // which reads as the state channel being broken rather than as WebGL-only.
            Bridge.OnPocketStateEnvelope += HandlePocketStateEnvelope;

            _cancel = new CancellationTokenSource();
            Connect();
        }

        /// <summary>
        /// Raised on the main thread by the Bridge. Queued rather than sent here, because sending
        /// means awaiting a socket and this is inside the game's own call stack.
        /// </summary>
        private bool _loggedFirstScreen;

        private void HandlePocketStateEnvelope(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            // Once, so the Console proves the C# half did its job. Without it, "the phone did not
            // change screen" has two indistinguishable causes — the game never asked, or this link
            // never forwarded — and the Bridge's own log cannot tell them apart because
            // OnPocketStateEnvelope is invoked with `?.` and says nothing when nobody is listening.
            if (!_loggedFirstScreen)
            {
                _loggedFirstScreen = true;
                Debug.Log($"[PocketEditorLink] forwarding screen changes to the harness. First: {json}");
            }
            // The harness reads `type` to route this; the rest of the envelope is passed through
            // exactly as the Bridge built it, so the Editor path and the WebGL path carry an
            // identical payload.
            _outbound.Enqueue("{\"type\":\"state\",\"envelope\":" + json + "}");
        }

        private async void Connect()
        {
            try
            {
                _socket = new ClientWebSocket();
                await _socket.ConnectAsync(new Uri(HarnessUrl), _cancel.Token);
                Debug.Log($"[PocketEditorLink] Connected to {HarnessUrl}. Press a button on the phone.");

                // See "Honest limit #2" above: this is what YOU set on the inspector, not
                // something read back from the Bridge — the event cannot be null-tested from
                // out here. Tier 3 (a real WebGL run) is what proves wiring honestly.
                var wired = GameSubscribesToPocketInput ? "true" : "false";
                await SendAsync("{\"type\":\"wiring\",\"wired\":{\"pocket\":" + wired + "}}");
                await SendAsync("{\"type\":\"ready\"}");

                var buffer = new byte[8192];
                while (_socket.State == WebSocketState.Open && !_cancel.IsCancellationRequested)
                {
                    var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancel.Token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    _inbound.Enqueue(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
            }
            catch (OperationCanceledException) { /* leaving play mode */ }
            catch (Exception err)
            {
                Debug.LogWarning($"[PocketEditorLink] {err.Message}. Is `npm run pocket:dev` running?");
            }
        }

        private async System.Threading.Tasks.Task SendAsync(string json)
        {
            if (_socket == null || _socket.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cancel.Token);
        }

        private void Update()
        {
            while (_inbound.TryDequeue(out var raw))
            {
                // The harness sends {"method":"OnGameHubPocketInput","json":"<payload>"} —
                // the same method name and the same STRING payload SendMessage delivers in a
                // build, so your handler cannot tell the difference.
                try
                {
                    var frame = JsonUtility.FromJson<Frame>(raw);
                    if (frame == null || string.IsNullOrEmpty(frame.method)) continue;
                    switch (frame.method)
                    {
                        case "OnGameHubPocketInput": Bridge.OnGameHubPocketInput(frame.json); break;
                        case "OnGameHubPocketPlayerJoined": Bridge.OnGameHubPocketPlayerJoined(frame.json); break;
                        case "OnGameHubPocketPlayerReconnected": Bridge.OnGameHubPocketPlayerReconnected(frame.json); break;
                        case "OnGameHubPocketPlayerLeft": Bridge.OnGameHubPocketPlayerLeft(frame.json); break;
                    }
                }
                catch (Exception err)
                {
                    Debug.LogWarning($"[PocketEditorLink] Bad frame: {err.Message}");
                }
            }

            // Screen changes out. Fire-and-forget on purpose: Update must not await, and a screen
            // that fails to send is not worth stalling a frame over — the next PushScreen resends.
            while (_outbound.TryDequeue(out var outgoing))
            {
                _ = SendAsync(outgoing);
            }
        }

        private void OnDestroy()
        {
            // Before cancelling the socket: the Bridge outlives this component (it is
            // DontDestroyOnLoad), so a handler left subscribed would queue into a dead link for
            // the rest of the session and leak this object with it.
            if (Bridge != null) Bridge.OnPocketStateEnvelope -= HandlePocketStateEnvelope;
            if (_instance == this) _instance = null;

            try { _cancel?.Cancel(); } catch { }
            try { _socket?.Dispose(); } catch { }
        }

        [Serializable]
        private class Frame
        {
            public string method;
            public string json;
        }
    }
}
#endif
