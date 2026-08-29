# Arsmi Games — Unity SDK

Everything a Unity WebGL game needs to talk to the platform. Verified against
`packages/sdk/unity/GameHubBridge.cs` on 2026-07-22.

- Package: `com.arsmi.gamehub` 4.0.0 · Unity 2021.3+ · wire protocol 2
- Install: `https://github.com/Arsmi-17/ArsmiGames_SDK.git?path=/Packages/com.arsmi.gamehub`

Dropping `?path=` is what produces "Repository not found". It is a UPM git URL, not a
`.unitypackage`.

## Setup

Add one `GameHubBridge` component to a GameObject in your first scene, or let your own
bootstrapper create it — `Awake` marks it `DontDestroyOnLoad` and introduces the game to the
platform. Everything reaches it through `GameHubBridge.Instance`, which is null when the game
runs outside the platform.

```csharp
var hub = GameHubBridge.Instance;
if (hub == null) { /* Editor, or opened outside the platform. Run standalone. */ }
```

**The build must ship the SDK.** `gamehub-sdk.js` has to be in the WebGL template, because
`GameHubBridge.jslib` only wires itself up if `window.GameHubSDK` already exists. Without it
every call from C# is a silent no-op — nothing errors, nothing arrives, and there is no clue
why. The package's WebGL template includes it; if you use your own template, copy it in.

## Two rules that are not optional

The platform will not publish a game that breaks either, and it can check both.

**Honour the volume button.** Subscribe to `OnMuteChanged` and actually silence your audio.
Subscribing is also how the platform knows you handled it — the `.jslib` receives the message
whether or not your C# does anything with it, so from outside every Unity build looks
compliant. Only your subscription tells the truth.

**Handle being resized.** Subscribe to `OnFullscreenChanged`. If your layout already follows
the viewport every frame there may be nothing to do, but subscribe anyway.

```csharp
void Start() {
    var hub = GameHubBridge.Instance;
    if (hub == null) return;
    hub.OnMuteChanged += (muted, fromPlatform) => { if (fromPlatform) SetAudioMuted(muted); };
    hub.OnFullscreenChanged += fullscreen => Relayout();
}
```

`OnMuteChanged` fires with `fromPlatform: false` when your own `SetMuted()` echoes back. Acting
on that echo latches the platform's override on, and the player's own music toggle then never
brings the music back — check the flag.

## Saving progress

The full contract, including why each rule exists, is in
[platform_saving_data_instruction.md](../../../platform_saving_data_instruction.md). The short
version:

| Rule | What it means in C# |
|---|---|
| R1 | Mirror a complete, self-consistent snapshot. Counters and what they count go together. |
| R2 | Write nothing until `DataReady`. The SDK now drops earlier writes and logs why. |
| R3 | Do not create a save for a player with no progress. |
| R4 | When the save arrives, adopt it whole and discard your local copy. |
| R5 | Conflicts are resolved by the platform, one whole map wins. Never merge by key. |

Requires the game to be published with **Save progress → "saves data locally and mirrors it to
Arsmi Games"**. In any other mode every write here does nothing.

```csharp
bool   DataReady           // true once the player's save has arrived
string SaveMode            // "no" | "sdk" | "backend"
bool   LoggedIn
string SaveUpdatedAt       // ISO-8601 of the last accepted write, or null

event Action         OnDataChanged   // the save arrived, or the platform replaced it
event Action<string> OnDataError

string GetItem(string key, string fallback = null)
int    GetInt(string key, int fallback = 0)
float  GetFloat(string key, float fallback = 0f)
bool   GetBool(string key, bool fallback = false)
bool   HasItem(string key)
IEnumerable<string> Keys

void SetItem/SetInt/SetFloat/SetBool(string key, ...)
void RemoveItem(string key)
void ClearData()
void FlushData()           // rarely needed; hidden/closed tabs flush automatically
```

Reads come from an in-memory copy, so `GetItem` in `Update()` never crosses into JavaScript.
Writes are debounced and forced out when the tab is hidden or closed.

**`OnDataChanged` fires more than once** — when the save first arrives, when a guest signs in
and their progress is adopted, and when another device turns out to be ahead. Make your handler
safe to run twice.

### The startup order that matters

Do not read your own `PlayerPrefs` on frame one. On a browser the player has never used, local
storage is empty and a new browser is indistinguishable from a new player — so a game that boots
from local state starts fresh and then flushes that fresh state over a real account save.

```csharp
IEnumerator Start() {
    var hub = GameHubBridge.Instance;
    if (hub == null) { BootLocal(); yield break; }

    hub.OnDataChanged += Apply;
    if (hub.DataReady) { Apply(); yield break; }

    // Never wait forever: offline, or outside the platform, the save never comes and the game
    // still has to become playable.
    float deadline = Time.realtimeSinceStartup + 5f;
    while (!hub.DataReady && Time.realtimeSinceStartup < deadline) yield return null;
    if (!hub.DataReady) BootLocal();
}

void Apply() {
    var hub = GameHubBridge.Instance;
    foreach (var key in new List<string>(hub.Keys)) PlayerPrefs.SetString(key, hub.GetItem(key));
    PlayerPrefs.Save();
    BootLocal();
}
```

## Am I on the platform? (4.5.5)

```csharp
struct GameHubConnection {
    bool   Connected;
    string Reason;            // why not: "standalone" (no host answered) or "editor"
    string SessionId, GameId, Slug, Role;
    bool   Preview;
    string PlatformVersion;   // the SDK the platform serving you is on; null if it did not say
    int    Protocol;          // the wire protocol, which is what decides compatibility
}

GameHubConnection Connection            // the whole answer
bool IsConnected                        // Connection.Connected
bool ConnectionKnown                    // whether the question has been answered at all
event Action<GameHubConnection> OnConnection
```

```csharp
void Start()
{
    GameHubBridge.Instance.OnConnection += c =>
    {
        if (c.Connected) StartOnlineRun(c.GameId);
        else             StartOfflineRun();
    };
}
```

`OnConnection` fires once there is an **answer**, and again if it changes. Subscribing after the
answer has already arrived fires it immediately — which is the normal case here, not an edge one:
`Awake()` starts the JavaScript bridge, which is answered at once when the handshake has already
happened, so the acknowledgment routinely reaches C# before any game's `Start()` runs. An event
that only fired on change would be missed by nearly every game.

- **Do not read `IsConnected` on the first frame.** It is false before the handshake and false for
  ever off-platform. `ConnectionKnown` separates the two; `OnConnection` waits for you.
- **It answers in the Editor too**, with `Connected = false, Reason = "editor"`. There is no
  JavaScript bridge there, so without that a game testing its offline path would wait on a
  callback that cannot arrive.
- **It is not `OnContext`.** That arrives whether or not a platform is there, carrying a locally
  guessed context — from C# a game inside the player page and a game opened from a `file://` URL
  looked identical, which is the gap this closes.

Subscribing claims nothing: this is not one of the wiring checks the publish gate reads.

## Which way the frame is

There is no typed C# accessor for this yet. The orientation arrives on the context, which
`OnContext` already delivers as raw JSON — so a build compiled against any 4.x package hears a
rotation without being rebuilt:

```csharp
GameHubBridge.Instance.OnContext += json =>
{
    // json carries "orientation": "portrait" | "landscape"
    if (json.Contains("\"orientation\":\"portrait\"")) LayOutPortrait();
    else                                              LayOutLandscape();
};
```

On a phone this is **how the player is holding it**, and `OnContext` fires again each time they
turn it. It is not the orientation you uploaded the game as: the platform used to lock the
phone to that value and no longer does, so the frame fills the screen whichever way the device
is held and the game adapts.

**Your build has to be willing to adapt too.** *Arsmi Games → Build WebGL…* stamps an
orientation into `index.html` and the template locks the canvas to that shape, so a build made
as Landscape stays 16:9 inside a portrait frame with black bars above and below — correct, but
fixed. Pick **Auto** (4.5.6) instead and the canvas takes the shape of the window, so the game
is portrait on an upright phone and landscape on a turned one. Only choose it if your game
genuinely lays out both ways; a landscape-only game built as Auto gets squeezed rather than
letterboxed.

## Identity

```csharp
string PlayerId      // pseudonymous, stable for this player in THIS game, not comparable across games
string Username, DisplayName, AvatarPath
string Email         // null unless the per-game email opt-in was granted AND you save to your own backend
bool   EmailShared   // which of those two a null Email means
event Action OnUserChanged
void RequestUserState(string game = "")
void RequestLogin(string reason = "game")
```

Key your own backend records on `PlayerId`. Never build login on `Email`: it is null for most
games and for every guest, and it is not yours to assume.

## Device (4.5.4)

```csharp
struct GameHubDevice { string Type; bool Touch, Keyboard, Mouse, Gamepad; string Source; }

GameHubDevice Device                     // Type is null until the first context arrives
event Action<GameHubDevice> OnDevice     // fires once at handshake
void DeclareDeviceSupport(params string[] types)
```

```csharp
void Start()
{
    GameHubBridge.Instance.OnDevice += device =>
    {
        if (device.Touch) ShowTouchControls();
        else              ShowKeyboardHints();
    };
}
```

All of it is optional. A game that ignores it behaves exactly as before and is treated as
supporting every device.

`Type` is `"mobile"`, `"tablet"` or `"desktop"`. **Ask the platform rather than sniffing:** a WebGL
build runs in an iframe the platform sized, so `Screen.width` measures the frame and not the
device.

`Type` and the input flags are two different facts on purpose — an iPad with a keyboard case is a
tablet that types, a touchscreen laptop is a desktop that taps. Gate features on `Type`, choose
controls on the flags. `Keyboard` is the best available signal rather than a certainty; `Gamepad`
is a snapshot taken at handshake, so use Unity's own input system for a pad connected later.

`Source` is `"platform"` when the host told us and `"local"` when the SDK guessed with no host
present. In a real WebGL build served by the platform it is always `"platform"`.

Neither `Type` nor the flags change during a session. For rotation and resize, use
`OnFullscreenChanged` and Unity's own screen metrics.

`DeclareDeviceSupport("desktop")` says which devices your game is built for. It only ever
restricts your own game. The platform reads it during upload preview and pre-fills the
**Supported devices** field; players on other devices see a note above the play button and
**are not blocked**. Declaring nothing means every device, is completely normal, and has no
effect on whether your game can be published.

## Flux Coins

```csharp
int    FluxCoins          // -1 until the first wallet reply, NOT 0
string WalletCurrency
event Action<int>    OnWalletChanged
event Action<string> OnWalletError
void WalletGet(string currency = "flux", double rate = 1, string game = "")
void WalletSpend(int amount, string reason = "game")
```

**A game can never add Flux Coins.** There is no method, and the SDK refuses the underlying
events even if you send them by hand. Flux is bought from the platform or granted by it. Read
with `WalletGet`, take with `WalletSpend`, and wait for the reply before handing over whatever
was bought — the server checks the balance and the spend can fail.

## Rewarded ads

```csharp
bool AdShowing
event Action       OnAdStarted
event Action<bool> OnAdFinished   // true = watched to the end
void ShowRewardedAd(string placement = "game")
```

The ad is a platform overlay. Your game asks, pauses itself, and waits. It pays out in *your*
game's currency — the skin, the extra life — granted by your code when `OnAdFinished` is true.
It does not pay Flux.

## Leaderboards

```csharp
void LeaderboardDefine(string metricKey = "score", string metricLabel = "Score", string sortDirection = "desc")
void LeaderboardScore(double score, string metricKey = "score", ..., string metadataJson = "{}")
void LeaderboardDefineJson(string json)
void LeaderboardScoreJson(string json)
```

There is no way to read entries back. The platform renders the board.

## Pocket Console, Challenge, sharing, context

Phones as controllers, head-to-head challenges, and the rest. Until package 4.0.0 these
messages reached C# and stopped at a `Debug.Log` — the feature was advertised and no Unity game
could act on it. They are real events now.

> **Pocket Console is more than the calls below.** Your game also ships a *controller* — a folder of
> plain HTML that renders the pad — and it is tested with a local harness and checked by a publish
> gate before it can ship. All of that is in **[POCKET-CONSOLE.md](POCKET-CONSOLE.md)**, beside this
> file. There is also a runnable sample with no game in it: **Arsmi Games ▸ Import Pocket Console
> sample**. Start there; this section is the C# surface only.

```csharp
event Action<string> OnPocketInput, OnPocketPlayerJoined, OnPocketPlayerReconnected, OnPocketPlayerLeft;
event Action<string> OnChallengeStart, OnChallengeLeaderboard, OnChallengeEnd;
event Action<string> OnLeaderboardSharing;
event Action<string> OnContext;

void PocketReady(int maxPlayers, string layout = "dpad-buttons", string schemaJson = "{}");
void PocketSchema(string json);
void PocketState(string screen, string dataJson = "{}");             // every seat
void PocketSeatState(int slot, string screen, string dataJson = "{}"); // one seat
void ChallengeReady(int maxPlayers, string mode = "ranked", bool ranked = true);
void ChallengeState(string json);
void ChallengeResult(string json);
```

The payloads are raw JSON strings, deliberately. They carry game-defined shapes, and
`JsonUtility` cannot deserialise the dictionaries most of them contain — parse with whatever
your game already uses.

The `.jslib` subscribes to `gamehub:pocket:input` on every game's behalf, whether or not the
C# does anything with it, so the platform cannot tell "supports Pocket Console" from "ignores
it" by watching JavaScript. Only `OnPocketInput` proves it: a game must subscribe to it, and the
publish gate will not accept a game's Pocket Console support until it does — see below.

### Reading a press

`OnPocketInput` hands you a JSON string, and **the control is nested**. This is the real payload,
captured from a live session:

```json
{ "type": "pocket_input", "playerSlot": 1, "sequence": 4,
  "input": { "control": "start", "pressed": true, "buttons": { "start": true } } }
```

So:

```csharp
[Serializable] class Envelope { public int playerSlot; public Body input; }
[Serializable] class Body     { public string control; public bool pressed; }

hub.OnPocketInput += json => {
    var press = JsonUtility.FromJson<Envelope>(json);
    if (press?.input == null || !press.input.pressed) return;   // releases are not commands
    Act(press.input.control, press.playerSlot);
};
```

Reading `control` off the top level yields `null` and every press is silently ignored — while the
platform still reports `handled: true`, because the SDK acked receipt long before your code looked
at it. The symptom is a controller that visibly connects, logs perfect presses, and moves nothing.
It cost a full round of real-phone testing to find, so it is written down here.

Act on `pressed: true` only, unless you genuinely want press-and-hold. A controller sends both
edges — the publish gate requires it — but each press is one command.

### Moving a phone between screens (4.2.0)

A controller declares every screen it can show in its `pocket.controller.json`; your game names
which one is active. Two calls, and they are the whole vocabulary:

```csharp
hub.PocketState("car-select", "{\"cars\":[\"Neon\",\"Rusty\"]}");   // every seat
hub.PocketSeatState(2, "finished", "{\"rank\":1}");                // one seat
```

Screen ids are whatever the controller's manifest declares. There are no reserved names, no
required screens, and no ordering — you may move any seat to any declared screen at any time.
`dataJson` is yours and is passed through untouched; it reaches the controller as the `data` of a
`pocket:screen` event.

You never declare whether your game is "seat-based". You express that by which call you make: a
game whose first finisher ends the round for everyone calls `PocketState`; a game where the others
keep playing calls `PocketSeatState` for the seat that finished. A race wants both — seat-targeted
as players cross the line, then `PocketState("ranking")` once all are done.

`dataJson` is a raw JSON string, not an object, for the same reason every other payload on this
side is: `JsonUtility` cannot serialise the dictionaries these normally carry.

The server keeps each seat's screen and replays it when a phone joins or reconnects, so a player
who reloads mid-round comes back where they were. Setting a screen for a seat nobody has taken yet
is fine — it is delivered when that seat joins.

Do **not** push a screen from your `OnPocketPlayerJoined` handler. Replay has already done it, and a
second push for the same fact is a second source of truth for it.

An all-seats push clears any per-seat overrides, because it is a new baseline. Without that, a seat
left on `"finished"` from the last round would stay there for the whole of the next one.

### In the Editor (4.3.0)

Outside WebGL there is no `.jslib`, so both calls above would end at a `Debug.Log` — presses would
reach your game in play mode and nothing would come back, which reads as a broken channel rather
than as a WebGL-only path. Since 4.3.0 the bridge raises `OnPocketStateEnvelope` in that branch and
the local harness's `PocketEditorLink.cs` forwards it, so your phone really does change screen while
you iterate.

`OnPocketStateEnvelope` exists for that companion, not for your game — the envelope it carries is
what your own `PocketState` call just produced. Ignore it unless you are writing tooling.

Two things the publish gate refuses: a declared screen your game never drives the controller into
(nothing on it was tested), and a screen id your game sends that the manifest does not declare
(the phone would keep its current screen and the player would be stuck).

## Casino

Only for games an admin has registered as casino-class; every other game's round is refused by
the server, which is why this being present for everyone is harmless.

```csharp
event Action<string> OnCasinoResult;   // answers Round, Seed and RotateSeed alike
void CasinoRound(string mode, int bet, string roundKey);
void CasinoSeed();
void CasinoRotateSeed(string clientSeed = null);
```

**You send a bet. You never send a payout.** There is no parameter for an outcome or a
multiplier — not validated away, simply absent. The server owns the paytable and settles in one
transaction; your game renders a result that has already happened.

JavaScript returns a promise and C# cannot await one, so every result arrives through
`OnCasinoResult`. Match it to the round you sent using `roundKey` — which is also an
idempotency key: retry with the same one after a dropped connection and you get the same result
back, not a second spin and not a second charge.

## Still not available

| Feature | Status |
|---|---|
| Achievements | Removed in package 3.0.0. The platform no longer has them, in either SDK. |
| Reading leaderboard entries | No API in either SDK. The platform renders the board. |

## Publishing checklist

1. `OnMuteChanged` subscribed and actually silencing audio.
2. `OnFullscreenChanged` subscribed.
3. If you save: boot gated on `DataReady`, writes after it, complete snapshots.
4. `gamehub-sdk.js` present in the WebGL template.
5. Console clean — no "called before the player's save arrived" warnings.
6. If you support phones as controllers: `OnPocketInput` subscribed. Nothing else proves it —
   the `.jslib` acks `handled:true` for every Unity build regardless.

The platform reports what you wired up one frame into the first scene, as a JSON object with
one boolean per requirement: `mute`, `fullscreen`, `data`, `user`, `wallet`, `ads`,
`leaderboard`, and, since package 4.1.0, `pocket` — true only when `OnPocketInput` has a
subscriber. If you subscribe later than that, call `ReportWiring()` yourself afterwards or you
will be assessed as not handling what you actually handle.
