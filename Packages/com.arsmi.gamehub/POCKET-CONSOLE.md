# Pocket Console

A player's phone becomes the controller for a game running on a desktop screen. They scan a code,
their phone shows **your** buttons, and their presses arrive in your game.

Your game ships two things: the game, and a **controller** — a folder of plain HTML, CSS and JS that
renders the pad. There is no build step and no framework.

```
phone (your controller HTML)  ──press──▶  realtime server  ──▶  platform  ──▶  your game
                              ◀──screen──                                     PocketState(...)
```

---

## 1. Desktop only

Pocket Console appears when the browser matches:

```
(min-width: 1024px) and (pointer: fine)
```

Width alone cannot tell a landscape tablet from a laptop; `pointer: fine` can. On a phone or tablet
the feature is **absent** — not merely hidden. The code is behind a dynamic import, so a phone never
downloads it: the player is already holding the screen, and there is no second device to join with.

Nothing is required from you for this. Do not add your own device checks.

---

## 2. The controller

One folder, one manifest.

```
MyController/
  pocket.controller.json
  controller/
    index.html
    controller.css
    controller.js
```

Scaffold it — this generates all four files from the manifest, and never overwrites one you have
edited:

```
npm run pocket:init "--" --project=path/to/MyController --unity
```

> **PowerShell:** the `"--"` must be quoted exactly like that. A bare `--` is swallowed by
> PowerShell 5.1 and your arguments never reach the script. Also note `npm run` sets the working
> directory to the repo root, **not** the folder you typed the command in — so always pass
> `--project`.

### pocket.controller.json

```json
{
  "title": "Cat Slide",
  "slug": "cat-slide",
  "maxPlayers": 4,
  "entry": "controller/index.html",
  "screens": [
    { "id": "menu", "default": true },
    { "id": "level-select" },
    { "id": "playing" }
  ],
  "controls": [
    { "id": "start", "label": "Play",  "group": "actions", "screen": "menu" },
    { "id": "left",  "label": "◀",     "group": "dpad",    "screen": ["level-select", "playing"] },
    { "id": "right", "label": "▶",     "group": "dpad",    "screen": ["level-select", "playing"] }
  ]
}
```

- `maxPlayers: 1` is single-player. There is no separate single-player mode — one seat is a room of
  one.
- `screen` on a control takes a string **or an array**. Omit it and the control appears on every
  screen.
- Exactly one screen may be `default`. It is what a phone shows before your game has spoken.
- Screen ids match `^[a-z0-9][a-z0-9_-]*$`. They are yours; no name is reserved and nothing in the
  protocol understands any of them.

### Declared screens, dynamic payload

Every screen ships **in your markup**, as a `[data-screen]` section:

```html
<section class="screen" data-screen="menu">
  <button class="btn" type="button" data-control="start">Play</button>
</section>
<section class="screen" data-screen="playing" hidden>
  <button class="btn" type="button" data-control="left">◀</button>
</section>
```

Your game only ever names *which* screen is active, and may attach opaque `data` alongside. It never
sends a layout.

That is a deliberate limit. A game that pushed markup could not be checked before it ran, and the
publish gate — which reads your manifest and refuses a controller that disagrees with your game —
would have nothing to read. Declared screens are what make the controller reviewable.

---

## 3. The game

### Unity

```csharp
void Start()
{
    var hub = GameHubBridge.Instance;

    hub.OnPocketInput += HandlePocketInput;   // BEFORE PocketReady — see below
    hub.PocketReady(4);                       // seats. Always explicit.
    hub.PocketState("menu");                  // where everybody starts
}
```

**Subscribe before `PocketReady`.** The bridge reports pocket support by testing whether
`OnPocketInput` has a subscriber. The other order announces your game as not supporting the thing it
supports, and the publish gate then refuses it.

**Pass the seat count explicitly.** The realtime server only ever *widens* a session's seat count, so
one defaulted call leaves the room bigger than the game for the rest of the session.

### Reading a press

The control is **nested**:

```json
{ "playerSlot": 1, "sequence": 4, "input": { "control": "left", "pressed": true } }
```

Read `control` off the top level and you get `null`, every press hits your guard, and the press still
acks as handled — so your logs look perfect and the game does nothing. This is the single most common
integration bug.

```csharp
[Serializable] class Envelope { public int playerSlot; public Body input; }
[Serializable] class Body { public string control; public bool pressed; }

void HandlePocketInput(string json)
{
    var envelope = JsonUtility.FromJson<Envelope>(json);
    var body = envelope?.input;
    if (body == null || !body.pressed || string.IsNullOrEmpty(body.control)) return;

    switch (body.control) { /* ... */ }
}
```

Act on **presses only**. Every control is a discrete command, so acting on both edges runs each one
twice. The controller still sends releases — the gate requires press/release symmetry — they are
simply not commands.

### Moving a phone between screens

Two calls, and they are the whole vocabulary:

```csharp
hub.PocketState("ranking");                              // every seat
hub.PocketSeatState(2, "finished", "{\"rank\":1}");      // one seat; the others carry on
```

**There is no mode flag.** A game never declares whether it is "seat-based" — it expresses that by
which call it makes:

| situation | call |
| --- | --- |
| first finisher ends the round for everyone (a puzzle) | `PocketState("over")` |
| the others keep playing after one finishes (a race) | `PocketSeatState(slot, "finished")` |
| a race, properly | per-seat as each crosses, then `PocketState("ranking")` at the end |

These are examples, not categories. Any game combines the two however its rules require.

An all-seats push **clears** per-seat overrides — it is a new baseline. Without that, a seat left on
`finished` from the last round would stay there through the whole of the next one.

### Retention and replay

The realtime server remembers each seat's screen and replays it inside the frame that announces the
seat. So:

- a phone joining mid-round lands on the right screen before your game hears about it
- a player who reloads comes back to the screen they were on, not your default
- a screen set for a seat nobody has taken yet is delivered when they arrive

Do not push a screen in your `player_joined` handler. Replay already did it, and you would be adding
a second source of truth for the same fact.

When a player leaves, seats are **renumbered** — seat 3 becomes seat 2 — and their screens are
carried across with them. Nothing is required from you, but do not cache seat numbers across a leave.

### Seat colours

Seats 1–8 have fixed colours, defined once in `packages/sdk/pocket/manifest.ts` (`SLOT_COLORS`). The
generated controller paints itself in its seat's colour. Use the same colour for that player on your
screen — the border of their name, their piece, their score row. That agreement is how a player finds
themselves on a shared screen, so the two lists drifting apart is a real bug even though nothing
throws.

---

## 4. Running it locally

```
npm run pocket:dev "--" --project=path/to/MyController
```

It prints a `screen` URL for this machine, a `phone` URL plus a QR code for your phone (both must be
on the same network), and the realtime server it is using. Open the phone URL, enter the 6-digit
code, and your controller loads.

Edit any controller file and the phone reloads it — the socket and the seat survive, and the screen
your game last set is re-applied, so you are not thrown back to the default on every save.

### Three tiers

| tier | what is running | how |
| --- | --- | --- |
| 1 | controller only, no game | `pocket:dev` with no game |
| 2 | your game in the **Unity Editor** | `--unity-editor`, with `PocketEditorLink.cs` in the project |
| 3 | a **served build** — the real path | `--game=./Build` or a dev-server URL |

**Only tier 3 counts as evidence for publishing.** Tier 1 has no game, and tier 2 runs through an
Editor-only socket shim that never ships. Accepting either would make the gate a statement about the
harness rather than about your game.

### Tier 2, in detail

`pocket-console init --unity` writes `PocketEditorLink.cs`. Drop it in your project **once** — it
attaches itself, you do not place it on a GameObject. Then run the harness with `--unity-editor` and
press Play. A phone press runs your real `OnPocketInput` in play mode with no WebGL build in the loop,
and `PocketState` really does change your phone's screen.

Two honest limits, both by design:

- Outbound `PocketReady`/`PocketSchema` only reach a `Debug.Log` in the Editor, so the harness reads
  your seat count and controls from `pocket.controller.json` instead of from the game.
- The link's "the game subscribes to pocket input" tickbox is **you telling it**, not the Editor
  proving it. `OnPocketInput` is a C# event, and from outside the declaring class C# permits only
  `+=` and `-=` — nothing can null-test it. The gate does not trust that tickbox either way.

Requires Unity package **4.3.0 or newer**. Older packages do not raise `OnPocketStateEnvelope`, so
screens cannot leave the Editor at all — and the file will not compile.

### The realtime server

`pocket:dev` talks to the **deployed** realtime server by default. `dev` probes its `/health` at
startup and prints what it found:

```
realtime wss://…/ws  (screen channel ok)
```

If it prints `<< NO SCREEN CHANNEL`, that deployment predates the screen protocol: presses will work
and screens will not, silently — the server routes on message type with no default, so an unknown
message is accepted, matched against nothing, and dropped without an error. `dev` prints the two
commands that fix it. See `apps/realtime-server/README.md`; that directory is a mirror and does not
deploy.

---

## 5. Publishing

```
npm run pocket:check "--" --project=path/to/MyController --out=controller.zip
```

`check` reads the evidence `pocket:dev` recorded (`.pocket/tape.json`) and refuses to write an archive
until the run proves the controller works. What it enforces:

- **Tier-3 presses exist at all.** Nothing else is evidence.
- **Every declared control was pressed**, and press/release counts match **per seat**. (Globally was
  not enough: seat 1 sending two downs and seat 2 two ups used to pass because 2 = 2.)
- **The game acked each control** — for web games. For Unity this is waived and *said out loud*,
  because `GameHubBridge.jslib` subscribes on every game's behalf, so every Unity build acks true
  whatever its C# does. Unity's evidence is the wiring bit that only C# can report.
- **The game reported `pocket: true`** and sent `pocket:ready`.
- **Every declared screen was actually displayed** during the run. A screen you declared but never
  drove into is untested, and untested is a failure.
- **The game never asked for an undeclared screen.** On a phone that leaves the wrong pad up and the
  player stuck. Both rules exist because rule 1 alone reports `"car-select" was never displayed` for a
  game that typed `car_select` — true, and silent about the actual cause.
- Duplicate input sequence numbers fail; forward gaps only warn (a phone that sent `launch_game`
  leaves a legitimate hole).

A manifest with **no** screens produces no screen rules at all. Screens are opt-in.

### What ships

The archive is **one self-contained HTML file**. `check` inlines your CSS and JS into the entry
document, so a phone makes one request instead of three or four. The ceiling is 2 MB — a phone
downloads all of it before showing a button.

A `type="module"` script containing `import`/`export` cannot be inlined and is refused rather than
silently broken.

Served controllers are cached hard (`immutable`, one year) **only** when the URL carries a `?v=`
stamp; without one they are `must-revalidate`, because storage paths are overwritable and promising
immutability for a mutable path is a lie that outlives the mistake by a year.

---

## 6. Troubleshooting

| symptom | cause |
| --- | --- |
| Phone shows a pad you do not recognise ("My Game / Jump / Dash") | `--project` was not passed, so the harness served the repo root. `dev` prints the project and title it is serving — read that line. |
| Presses do nothing, but the log says `handled: true` | You read `control` off the top level. It is nested under `input`. |
| Presses work, screens never change | The realtime server predates the screen channel. Check `dev`'s `realtime` line. |
| Screens change in a build but not in the Editor | Unity package older than 4.3.0, or `PocketEditorLink.cs` is missing. |
| The phone changes screen once, then stops | Your game pushes the same screen repeatedly and something upstream deduplicates. Push only on change. |
| A button stays held down in the game | The controller does not release on `pointercancel`/`pointerleave`. Use pointer capture — the generated pad already does. |
| `check` says "No evidence from the production path" | The run was tier 1 or tier 2. Serve a real build. |
| A control the game handles is reported as never pressed | The manifest and the markup disagree. Every `data-control` must exist in `controls`, and vice versa. |
| Editor logs `PocketState …` and nothing happens | Nothing is listening to `OnPocketStateEnvelope`. Restart `pocket:dev` if it predates your SDK. |

---

## 7. Where things live

| what | where |
| --- | --- |
| Protocol definition (single source) | `packages/sdk/protocol/manifest.mjs` |
| Manifest parsing, seat colours | `packages/sdk/pocket/manifest.ts` |
| Publish-gate rules | `packages/sdk/pocket/coverage.ts` |
| Host socket state machine | `packages/sdk/pocket/hostSession.ts` |
| Local harness (`pocket:init` / `dev` / `check`) | `packages/sdk/test-node/` |
| Unity bridge (authored here) | `SDK/Unity/.../Runtime/GameHubBridge.cs` |
| Unity sample, runnable | `SDK/Unity/.../Samples~/PocketConsoleDemo/` |
| Realtime server (a mirror — see its README) | `apps/realtime-server/` |
| The phone app players actually use | `apps/mobile-console/` |

The protocol is defined once and generated everywhere. Edit `packages/sdk/`, then run:

```
npm run sdk:sync     # push the change to every copy
npm run sdk:check    # fail if any copy drifted
```

`GameHubBridge.cs` and `GameHubBridge.jslib` are the exception: they are **authored in the Unity
package** and *pulled* into `packages/sdk/unity/` by the sync. Editing the copy under `packages/sdk/`
will be silently reverted.

---

## 8. Start from the sample

The Unity package ships a runnable sample with no game in it — four screens, four seats, both
addressing modes, and its own HTML controller:

**Arsmi Games ▸ Import Pocket Console sample**

Read its `README.md`. It is the shortest complete example of everything above.
