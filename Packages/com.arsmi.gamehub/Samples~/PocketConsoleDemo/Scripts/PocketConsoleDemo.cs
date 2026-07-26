using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ArsmiGames.Samples.PocketConsole
{
    /// <summary>
    /// Pocket Console, with no game underneath it.
    ///
    /// There is nothing to win here on purpose. What this sample shows is the only thing a game has
    /// to get right to support a phone as a controller, which is a conversation in two directions:
    ///
    ///   phone -> game   a press arrives as OnPocketInput and moves that seat's dot
    ///   game  -> phone  PocketState / PocketSeatState put a phone on one of its declared screens
    ///
    /// and the part most integrations get wrong: those are not the same thing as each other, and
    /// the second one is per seat. PocketState moves everybody. PocketSeatState moves one player
    /// and leaves the rest alone — which is what "seat 2 finished, seats 1 and 3 are still going"
    /// requires, and there is no mode flag anywhere that says which kind of game this is.
    ///
    /// Four screens, walked by four seats:
    ///
    ///   lobby     every seat. `start` from any phone moves EVERY seat to play.
    ///   play      every seat. The d-pad moves that seat's own dot. `done` moves ONLY that seat.
    ///   waiting   one seat at a time. `back` returns just that seat to play.
    ///   over      every seat, once the last present seat is done. `again` returns to lobby.
    ///
    /// So both addressing modes are exercised by pressing buttons, and the screen each phone is on
    /// is drawn on this screen beside its seat colour — if a phone and its card disagree, the
    /// channel is broken and you can see it without reading a log.
    ///
    /// The scene holds one object with this component. Camera, canvas, bridge and every panel are
    /// built here at startup, the same arrangement the Kids Quiz sample uses, so the scene file
    /// stays one reviewable object rather than a few hundred hand-wired RectTransforms.
    ///
    /// Its controller is beside this folder in PocketController/. Run them together with:
    ///   npm run pocket:dev "--" --project=&lt;sample folder&gt;/PocketController --unity-editor
    /// then press Play — see this sample's README.md.
    /// </summary>
    public class PocketConsoleDemo : MonoBehaviour
    {
        /// <summary>
        /// Seats this demo offers. Passed to PocketReady explicitly, never defaulted: the realtime
        /// server only ever widens a session's seat count, so one defaulted call would leave the
        /// room bigger than the game for the rest of the session.
        /// </summary>
        private const int SeatCount = 4;

        /// <summary>Cells per side of a seat's dot grid.</summary>
        private const int GridSize = 5;

        // Must match PocketController/pocket.controller.json exactly. A screen this game names but
        // the controller does not declare leaves the phone where it is — and `pocket-console check`
        // refuses to publish the build, in both directions: named-but-undeclared, and
        // declared-but-never-shown.
        private const string ScreenLobby = "lobby";
        private const string ScreenPlay = "play";
        private const string ScreenWaiting = "waiting";
        private const string ScreenOver = "over";

        /// <summary>
        /// Seat colours, matching SLOT_COLORS in packages/sdk/pocket/manifest.ts and therefore the
        /// colour the controller paints itself. That agreement is the whole point: a player finds
        /// themselves on a shared screen by matching their phone's colour, so these two lists
        /// drifting apart is a real bug even though nothing throws.
        /// </summary>
        private static readonly Color[] SeatColors =
        {
            new Color32(0xff, 0x4d, 0x4d, 0xff),
            new Color32(0xff, 0xa2, 0x33, 0xff),
            new Color32(0xff, 0xd6, 0x33, 0xff),
            new Color32(0x5a, 0xd4, 0x69, 0xff),
            new Color32(0x3b, 0xc9, 0xdb, 0xff),
            new Color32(0x5c, 0x7c, 0xfa, 0xff),
            new Color32(0xb1, 0x97, 0xfc, 0xff),
            new Color32(0xff, 0x8c, 0xc8, 0xff),
        };

        private sealed class Seat
        {
            public bool Present;
            public string Name = "";
            public string Screen = ScreenLobby;
            public Vector2Int Dot = new Vector2Int(GridSize / 2, GridSize / 2);

            public Image Card;
            public Text Heading;
            public Text ScreenLabel;
            public Image[] Cells;
        }

        private readonly Seat[] seats = new Seat[SeatCount];
        private readonly string[] logLines = new string[9];
        private int logCount;

        private Text logText;
        private Text bannerText;
        private GameHubBridge hub;

        private void Awake()
        {
            for (int i = 0; i < seats.Length; i++) seats[i] = new Seat();

            EnsureCamera();
            EnsureEventSystem();
            BuildUi();

            if (GameHubBridge.Instance == null) new GameObject("GameHubBridge").AddComponent<GameHubBridge>();
        }

        private void Start()
        {
            hub = GameHubBridge.Instance;
            if (hub == null)
            {
                Banner("No GameHubBridge — nothing to talk to.");
                return;
            }

            // Subscribing IS the declaration. The bridge reports pocket support by testing whether
            // OnPocketInput has a subscriber, so this has to happen before PocketReady rather than
            // after it, or the game announces itself as not supporting the thing it supports.
            hub.OnPocketInput += HandleInput;
            hub.OnPocketPlayerJoined += HandleSeatArrived;
            hub.OnPocketPlayerReconnected += HandleSeatArrived;
            hub.OnPocketPlayerLeft += HandleSeatLeft;

            hub.PocketReady(SeatCount);

            // The opening screen, stated rather than assumed: the controller marks one screen
            // `default` so a phone has something to show before the game speaks, but the game must
            // still say so — otherwise a phone joining later replays nothing and shows the default
            // while this game believes everyone is somewhere else.
            PushAll(ScreenLobby);
            Banner("Scan the code on the platform, then press Play on a phone.");
        }

        private void OnDestroy()
        {
            if (hub == null) return;
            hub.OnPocketInput -= HandleInput;
            hub.OnPocketPlayerJoined -= HandleSeatArrived;
            hub.OnPocketPlayerReconnected -= HandleSeatArrived;
            hub.OnPocketPlayerLeft -= HandleSeatLeft;
        }

        // --- inbound ----------------------------------------------------------------------------

        /// <summary>
        /// A press.
        ///
        /// Presses only, never releases: every control here is a discrete command, so acting on
        /// both edges would run each one twice. The controller still sends releases — the publish
        /// gate requires press/release symmetry — they are simply not commands.
        /// </summary>
        private void HandleInput(string json)
        {
            InputEnvelope envelope = Parse<InputEnvelope>(json);
            if (envelope == null) return;

            // The payload is NESTED: {"playerSlot":1,"sequence":4,"input":{"control":"up",...}}.
            // apps/realtime-server wraps the controller's own payload under `input` before the
            // platform forwards it, so reading `control` off the top level silently yields null and
            // every press hits the guard below — while the press still acks handled, so the logs
            // look perfect and the game does nothing. The flat fallback is not speculation either:
            // the mobile console accepts a controller that posts its payload flat.
            InputBody body = envelope.input != null && !string.IsNullOrEmpty(envelope.input.control)
                ? envelope.input
                : Parse<InputBody>(json);

            if (body == null || !body.pressed || string.IsNullOrEmpty(body.control)) return;

            int slot = envelope.playerSlot;
            Seat seat = SeatFor(slot);
            if (seat == null) return;

            // A press is the first proof a seat exists: player_joined can be missed (the game may
            // have been loading when it fired) but a press cannot be, because it came from them.
            if (!seat.Present)
            {
                seat.Present = true;
                if (string.IsNullOrEmpty(seat.Name)) seat.Name = $"Player {slot}";
            }

            Log($"P{slot} {body.control}");

            switch (body.control)
            {
                case "start":
                    // Any phone may start, and it moves EVERY seat — one press, all seats.
                    if (seat.Screen == ScreenLobby) PushAll(ScreenPlay);
                    return;

                case "up": MoveDot(seat, 0, 1); return;
                case "down": MoveDot(seat, 0, -1); return;
                case "left": MoveDot(seat, -1, 0); return;
                case "right": MoveDot(seat, 1, 0); return;

                case "done":
                    // ONE seat. The others stay on play and keep moving — this is the line that
                    // makes a race possible, and it is the same protocol a puzzle uses.
                    if (seat.Screen != ScreenPlay) return;
                    PushSeat(slot, ScreenWaiting);
                    if (EveryPresentSeatIs(ScreenWaiting)) PushAll(ScreenOver);
                    return;

                case "back":
                    if (seat.Screen == ScreenWaiting) PushSeat(slot, ScreenPlay);
                    return;

                case "again":
                    if (seat.Screen != ScreenOver) return;
                    foreach (Seat other in seats) other.Dot = new Vector2Int(GridSize / 2, GridSize / 2);
                    PushAll(ScreenLobby);
                    Redraw();
                    return;

                default:
                    // controller_ready and back_to_browse arrive here too. Both belong to the
                    // platform, which has already acted on them.
                    return;
            }
        }

        private void HandleSeatArrived(string json)
        {
            SeatEvent seat = Parse<SeatEvent>(json);
            Seat target = seat == null ? null : SeatFor(seat.playerSlot);
            if (target == null) return;

            target.Present = true;
            target.Name = string.IsNullOrEmpty(seat.displayName) ? $"Player {seat.playerSlot}" : seat.displayName;
            Log($"P{seat.playerSlot} joined — {target.Name}");

            // Nothing is pushed to them here. The realtime server retains each seat's screen and
            // replays it inside the frame that announces the seat, so a phone arriving mid-round
            // lands on the right screen before this game hears about it. Pushing again would be a
            // second source of truth for the same fact.
            Redraw();
        }

        private void HandleSeatLeft(string json)
        {
            SeatEvent seat = Parse<SeatEvent>(json);
            Seat target = seat == null ? null : SeatFor(seat.playerSlot);
            if (target == null) return;

            Log($"P{seat.playerSlot} left");
            target.Present = false;
            target.Screen = ScreenLobby;

            // The seat that just left might have been the last one still playing, and the round
            // would otherwise wait forever for a phone that is gone.
            if (AnyPresentSeat() && EveryPresentSeatIs(ScreenWaiting)) PushAll(ScreenOver);
            Redraw();
        }

        // --- outbound ---------------------------------------------------------------------------

        /// <summary>Every seat, including seats nobody has taken yet — they get it on arrival.</summary>
        private void PushAll(string screen)
        {
            foreach (Seat seat in seats) seat.Screen = screen;
            hub?.PocketState(screen);
            Log($"-> all seats: {screen}");
            Redraw();
        }

        /// <summary>One seat. The others are not touched, and are not told.</summary>
        private void PushSeat(int slot, string screen)
        {
            Seat seat = SeatFor(slot);
            if (seat == null) return;

            seat.Screen = screen;
            hub?.PocketSeatState(slot, screen);
            Log($"-> seat {slot}: {screen}");
            Redraw();
        }

        private void MoveDot(Seat seat, int dx, int dy)
        {
            if (seat.Screen != ScreenPlay) return;
            seat.Dot = new Vector2Int(
                Mathf.Clamp(seat.Dot.x + dx, 0, GridSize - 1),
                Mathf.Clamp(seat.Dot.y + dy, 0, GridSize - 1));
            Redraw();
        }

        private Seat SeatFor(int slot) => slot >= 1 && slot <= seats.Length ? seats[slot - 1] : null;

        private bool AnyPresentSeat()
        {
            foreach (Seat seat in seats) if (seat.Present) return true;
            return false;
        }

        private bool EveryPresentSeatIs(string screen)
        {
            bool any = false;
            foreach (Seat seat in seats)
            {
                if (!seat.Present) continue;
                any = true;
                if (seat.Screen != screen) return false;
            }

            return any;
        }

        private static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch { return null; }
        }

        // --- the screen ---------------------------------------------------------------------------

        private void Redraw()
        {
            for (int i = 0; i < seats.Length; i++)
            {
                Seat seat = seats[i];
                if (seat.Card == null) continue;

                Color color = SeatColors[i % SeatColors.Length];
                // Present seats are their colour; empty ones are the same hue, nearly transparent,
                // so the row does not reflow when a player joins.
                seat.Card.color = seat.Present ? new Color(color.r, color.g, color.b, 0.16f) : new Color(1f, 1f, 1f, 0.04f);
                seat.Heading.color = seat.Present ? color : new Color(1f, 1f, 1f, 0.3f);
                seat.Heading.text = seat.Present ? $"{i + 1}. {seat.Name}" : $"{i + 1}. empty";
                seat.ScreenLabel.text = seat.Present ? seat.Screen : "—";

                for (int cell = 0; cell < seat.Cells.Length; cell++)
                {
                    int x = cell % GridSize;
                    int y = GridSize - 1 - (cell / GridSize); // row 0 is the top of the grid
                    bool lit = seat.Present && seat.Screen == ScreenPlay && seat.Dot.x == x && seat.Dot.y == y;
                    seat.Cells[cell].color = lit ? color : new Color(1f, 1f, 1f, 0.07f);
                }
            }
        }

        private void Log(string line)
        {
            for (int i = logLines.Length - 1; i > 0; i--) logLines[i] = logLines[i - 1];
            logLines[0] = line;
            logCount = Mathf.Min(logCount + 1, logLines.Length);

            if (logText == null) return;
            string text = "";
            for (int i = 0; i < logCount; i++) text += logLines[i] + "\n";
            logText.text = text;
        }

        private void Banner(string text)
        {
            if (bannerText != null) bannerText.text = text;
        }

        private static void EnsureCamera()
        {
            if (Camera.main != null) return;
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(0x11, 0x12, 0x16, 0xff);
        }

        private static void EnsureEventSystem()
        {
#if UNITY_2023_1_OR_NEWER
            if (FindFirstObjectByType<EventSystem>() != null) return;
#else
            if (FindObjectOfType<EventSystem>() != null) return;
#endif
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        /// <summary>
        /// 1920x1080 with a 0.5 match, everything anchored rather than placed at fixed pixels, so
        /// this holds up in a phone-shaped frame, at 21:9 and fullscreen — a Pocket Console game is
        /// shown on a desktop screen whose shape nobody controls.
        /// </summary>
        private void BuildUi()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            Text title = AddText(canvasGo.transform, "Title", "Pocket Console — channel demo", 54, TextAnchor.UpperLeft);
            Stretch(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(64f, -132f), new Vector2(-64f, -48f));

            bannerText = AddText(canvasGo.transform, "Banner", "", 30, TextAnchor.UpperLeft);
            bannerText.color = new Color(1f, 1f, 1f, 0.62f);
            Stretch(bannerText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(64f, -184f), new Vector2(-64f, -136f));

            BuildSeatRow(canvasGo.transform);

            Text logHeading = AddText(canvasGo.transform, "LogHeading", "everything crossing the channel", 24, TextAnchor.LowerLeft);
            logHeading.color = new Color(1f, 1f, 1f, 0.4f);
            Stretch(logHeading.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(64f, 296f), new Vector2(-64f, 330f));

            logText = AddText(canvasGo.transform, "Log", "", 26, TextAnchor.LowerLeft);
            logText.color = new Color(1f, 1f, 1f, 0.78f);
            Stretch(logText.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(64f, 48f), new Vector2(-64f, 292f));

            Redraw();
        }

        private void BuildSeatRow(Transform parent)
        {
            var row = new GameObject("Seats", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            Stretch(row.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(64f, 356f), new Vector2(-64f, -200f));

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 24f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            for (int i = 0; i < seats.Length; i++) BuildSeatCard(row.transform, seats[i], i);
        }

        private void BuildSeatCard(Transform parent, Seat seat, int index)
        {
            var card = new GameObject($"Seat{index + 1}", typeof(RectTransform));
            card.transform.SetParent(parent, false);
            seat.Card = card.AddComponent<Image>();

            seat.Heading = AddText(card.transform, "Heading", "", 30, TextAnchor.UpperCenter);
            Stretch(seat.Heading.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -56f), new Vector2(-16f, -16f));

            seat.ScreenLabel = AddText(card.transform, "Screen", "", 26, TextAnchor.UpperCenter);
            seat.ScreenLabel.color = new Color(1f, 1f, 1f, 0.66f);
            Stretch(seat.ScreenLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -96f), new Vector2(-16f, -58f));

            var grid = new GameObject("Grid", typeof(RectTransform));
            grid.transform.SetParent(card.transform, false);
            // Square, and centred: an aspect-fitted grid keeps the cells square whatever the card
            // ends up being, which a stretched GridLayoutGroup would not.
            RectTransform gridRect = grid.GetComponent<RectTransform>();
            gridRect.anchorMin = new Vector2(0.5f, 0f);
            gridRect.anchorMax = new Vector2(0.5f, 0f);
            gridRect.pivot = new Vector2(0.5f, 0f);
            gridRect.anchoredPosition = new Vector2(0f, 24f);
            gridRect.sizeDelta = new Vector2(240f, 240f);

            var gridLayout = grid.AddComponent<GridLayoutGroup>();
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = GridSize;
            gridLayout.spacing = new Vector2(6f, 6f);
            gridLayout.cellSize = new Vector2(42f, 42f);

            seat.Cells = new Image[GridSize * GridSize];
            for (int cell = 0; cell < seat.Cells.Length; cell++)
            {
                var dot = new GameObject($"Cell{cell}", typeof(RectTransform));
                dot.transform.SetParent(grid.transform, false);
                seat.Cells[cell] = dot.AddComponent<Image>();
            }
        }

        private static Text AddText(Transform parent, string name, string content, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Text text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            // The built-in font, because a sample must not ship a font asset and must not render
            // nothing on a project that has none.
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null) text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return text;
        }

        private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        // --- what arrives on the wire ------------------------------------------------------------

        [Serializable]
        private sealed class InputEnvelope
        {
            public int playerSlot;
            public int sequence;
            public InputBody input;
        }

        [Serializable]
        private sealed class InputBody
        {
            public string control;
            public bool pressed;
        }

        [Serializable]
        private sealed class SeatEvent
        {
            public int playerSlot;
            public string displayName;
        }
    }
}
