# Pocket Console — channel demo

A phone as a controller, with **no game underneath it**. Nothing to win: what this sample shows is
the only thing a game has to get right to support Pocket Console, which is a conversation in two
directions.

| direction | call | what you see |
| --- | --- | --- |
| phone → game | `OnPocketInput` | the d-pad moves **that seat's** dot |
| game → phone | `PocketState(screen)` | **every** phone changes screen at once |
| game → phone | `PocketSeatState(slot, screen)` | **one** phone changes; the others carry on |

That last row is the part integrations get wrong. There is no mode flag anywhere saying whether a
game is "everyone together" or "each player separately" — a game addresses every seat or one seat,
press by press, and the same protocol carries a puzzle and a race.

## The four screens

```
lobby ──start (any phone, moves EVERYONE)──▶ play ──done (moves ONLY that phone)──▶ waiting
  ▲                                          ▲                                        │
  └──────────── again ◀── over ◀── the last present seat pressed done ◀────────────────┘
                                                     back (that phone only) ───────────┘
```

Each seat's card on the game screen shows its colour, the screen its phone is on, and its dot. If a
phone and its card disagree, the channel is broken and you can see it without reading a log.

## Run it

**In the Editor, with a real phone, no WebGL build** — three things, in this order:

1. `PocketController/PocketEditorLink.cs` is already in this sample. It attaches itself; you do not
   place it. *If your project already has a copy from `pocket-console init --unity`, delete one —
   two copies of the same class will not compile.*
2. Start the harness against this sample's controller, from the repo root:
   ```
   npm run pocket:dev "--" --project=<this folder>/PocketController --unity-editor
   ```
   In PowerShell the `"--"` **must** be quoted exactly like that; a bare `--` is swallowed and the
   arguments never reach the script.
3. Press **Play** in Unity, then open the printed `phone` URL on your phone and enter the code.

The harness prints `screen -> all seats: play` when the game moves a phone, and the phone logs
`screen -> play` when it arrives. Those two lines are opposite ends of the chain — if the first
appears without the second, the problem is between them, not in your game.

**On the platform**: build for WebGL and upload as normal. Pocket Console appears on desktop only —
it is hidden on phones and tablets, where the player is already holding the screen.

## Reading the code

`Scripts/PocketConsoleDemo.cs` is the whole game side, and three details in it are load-bearing:

- **Subscribe to `OnPocketInput` before calling `PocketReady`.** The bridge reports pocket support by
  testing whether the event has a subscriber, so the other order announces the game as not
  supporting the thing it supports.
- **The input payload is nested**: `{"playerSlot":1,"input":{"control":"up","pressed":true}}`. Read
  `control` off the top level and you get `null`, every press is ignored, and the press still acks
  as handled — so the logs look perfect and the game does nothing.
- **Presses only, never releases.** Each control is a discrete command; acting on both edges runs
  everything twice. The controller still sends releases because the publish gate requires
  press/release symmetry — they are simply not commands.

`PocketController/` is the controller: `pocket.controller.json` declares four screens and eight
controls, and `controller/` is plain HTML, CSS and JS with no build step. Every screen ships in the
markup as a `[data-screen]` section and the game only names which one is active — the game never
sends a layout, which is what lets the publish gate check the controller before it ever runs.

Change a control, and the game must change with it: a screen this game names but the controller does
not declare leaves the phone where it is, and `pocket-console check` refuses to publish the build in
either direction — named-but-undeclared, and declared-but-never-shown.
