# Arsmi Games — GameHub SDK

Everything a Unity WebGL game needs to talk to the Arsmi Games platform, plus a demo that
exercises all of it.

## Install

**Window → Package Manager → + → Install package from git URL…**

```
https://github.com/Arsmi-17/ArsmiGames_SDK.git?path=/Packages/com.arsmi.gamehub
```

Or add it to your project's `Packages/manifest.json` directly:

```json
{
  "dependencies": {
    "com.arsmi.gamehub": "https://github.com/Arsmi-17/ArsmiGames_SDK.git?path=/Packages/com.arsmi.gamehub"
  }
}
```

`?path=` is relative to the repo root and points at the folder holding `package.json`. Unity
clones the whole repo and takes only that folder.

**Pin a version** — append a tag *after* the path:
`…?path=/Packages/com.arsmi.gamehub#v1.0.0`. Without one you track the default branch, and
someone else's push silently changes your next build.

### "Repository not found"

Unity clones the **remote**, not your working copy, so this means git could not see anything
at that URL. Two very different failures wear the same coat:

1. **Nothing is pushed yet.** Committing locally is not enough.
2. **The repo is private.** GitHub answers an unauthenticated client with *"Repository not
   found"* rather than *403*, so a private repo is indistinguishable from a missing one.
   Unity shells out to `git`, so it needs credentials `git` can find on its own: a
   credential helper holding a PAT, or an SSH url
   (`git@github.com:Arsmi-17/ArsmiGames_SDK.git?path=/Packages/com.arsmi.gamehub`).

On load the package copies its WebGL template into `Assets/WebGLTemplates/ArsmiGames`. That
folder is **generated** — the copy inside the package is the source of truth, and edits to
the one in `Assets/` are overwritten. (Unity only discovers WebGL templates under `Assets/`,
which is the only reason it is copied at all.)

## Try the demo

**Arsmi Games → Import Kids Quiz sample.** It imports the sample, puts its scene at index 0
in Build Settings, and opens it. Then **Arsmi Games → Build WebGL…** and upload the folder.

The Package Manager's own *Samples → Import* button works too, but it stops after copying
the files: the scene is not added to Build Settings, so a build straight afterwards produces
an empty player.

The demo has two halves:

- **Left — the kids quiz.** A real (small) game that saves its progress. The save-mode
  buttons switch between the three published options against the same game code.
- **Right — the SDK console.** A button for every call, and a live log of what crossed the
  bridge in both directions. In a WebGL build there is no debugger to attach, so this log is
  usually the fastest way to find out why something misbehaved.

In the Editor the bridge has no platform to talk to, so every call logs to the Console
instead of crossing over. That is expected — the real test is the WebGL build running inside
the platform, or in the **SDK Assessment** page in admin.

## Building

**Arsmi Games → Build WebGL…** (`Ctrl+Alt+B`). It asks **Portrait or Landscape**, then you
pick a folder. Upload it. There is nothing to edit afterwards.

The orientation you pick is written into the build's `index.html`, and the canvas is locked to
it: a portrait build stays portrait on a landscape screen, with black bars down the sides,
rather than stretching to fill. (If your game is not 9:16 / 16:9, change the one pair of
numbers in the template's orientation-lock CSS.)

The build is also *checked*. Two callbacks run around every WebGL build — including one
started from `File → Build Settings`, so the guarantee does not depend on which button you
press:

- **Before:** installs the template if it is missing, then forces the template, the
  decompression fallback (the platform serves static files, so a Brotli build will not load
  without it), and Run In Background (or the game freezes the moment the player clicks any
  platform UI outside the canvas).
- **After:** reads the `index.html` that was actually produced and **fails the build** if the
  SDK is not in it, or if `gamehub-sdk.js` was not copied next to it.

That last check is the point of the whole thing. Unity's stock template does not load the
SDK, and when it is missing the game still builds, still loads, still looks completely fine —
and cannot reach the platform at all. No saves, no leaderboards, and *no
error*, in either direction. It is a failure with no symptom except silence, so it is caught
at build time instead.

For CI (pass `-arsmiOrientation portrait` or `landscape` to match the menu):

```
Unity.exe -quit -batchmode -projectPath . \
  -executeMethod ArsmiGames.EditorTools.ArsmiBuild.BuildFromCommandLine \
  -arsmiOutput Builds/WebGL -arsmiOrientation landscape
```

## Test your integration before you upload

**Arsmi Games → Test SDK Integration** runs the platform's own assessment inside Unity, so you
find out whether a build will publish in seconds — not after a build-and-upload round trip that
comes back rejected.

It is the same verdict the platform reaches on upload, reached the same way. The platform does
not read your code; it *runs* your game and drives the bridge at it — sending a real `set_mute`
and waiting to hear your own handler answer — because a game that ignores the volume button and
one that honours it look identical from the outside. So this tool does the same:

1. Pick what you are **publishing as** (Platform save / Own backend / No save) — the save
   requirement differs for each, exactly as in the upload wizard.
2. Press **Enter Play Mode & Test** (or enter Play Mode yourself and press **Run Test**).
3. It reads what your game actually subscribed to, fires `set_mute` and `set_fullscreen` at the
   live bridge to confirm the handlers run without throwing, and reports.

The report has two parts:

- **Build settings** (no Play Mode needed): WebGL template, decompression fallback, Run In
  Background, and whether the bundled SDK is current.
- **Live protocol**: SDK connected, mute handled, fullscreen handled, save/identity for your
  chosen mode — plus wallet, ads and leaderboard as optional, informational rows.

Each failure says what is wrong and how to fix it, and **Save report…** writes it to a
Markdown file you can keep. A green verdict here means a green verdict on upload.

One thing the Play Mode run cannot see: whether your mute handler actually silenced the audio,
only that it ran without error — same limit the platform has. The [Mute](#mute) section is the
rule the check cannot enforce for you: silence *every* channel.

## Adding it to your game

Drop a `GameHubBridge` component on a GameObject in your first scene (or let something create
it, as the sample's `DemoBootstrap` does). Its `Awake` is what introduces the game to the
platform, so it must exist before anything asks it a question.

```csharp
var hub = GameHubBridge.Instance;
```

## The contract, function by function

Every function is one or both of two directions: the **platform sends** your game something and
expects it honoured, or your **game sends** the platform something. Some checks are required to
publish; the rest you use only if the feature applies.

The platform proves a game handles an inbound message by sending a real one and waiting for the
bridge's **acknowledgement**. For the inbound checks (mute, fullscreen), *subscribing to the
event* is what acks "handled" — a game that never subscribes is reported as not handling it, and
the bridge cannot infer it for you (the `.jslib` subscribes on your behalf, so only your C#
subscription is real evidence). Silence never counts as a pass.

| Function | Publish? | Platform → game (you must) | Game → platform (you may) |
|---|---|---|---|
| **Handshake** | **Required** | `Awake` introduces the game; the bridge replies and reports capabilities. Just have a `GameHubBridge` in the first scene. | — |
| **Mute** | **Required** | `OnMuteChanged` → zero **all** audio (`AudioListener.volume = 0`). Subscribing acks it. | `SetMuted(true)` when your own sliders all reach 0; `false` when any rises. Skip if you have no volume UI. |
| **Fullscreen** | **Required** | `OnFullscreenChanged` → subscribe to ack; re-fit only if you drive layout from code. | `RequestFullscreen()` — **only** if your game already has a fullscreen button. Do not add one. |
| **Identity** | Required for **own-backend** save | `OnUserChanged` → read `hub.PlayerId` and key saves on it. `hub.Email` is null unless your game was granted it — never key on it. | `RequestUserState()` to ask for it. |
| **Save (Platform)** | Required if published **Platform save** | Re-read through `GetInt/GetString` after a change; do not cache stale values. | `SetInt/SetString` (or `ArsmiSave`). **Never store currency in a save** — it is on the player's machine and editable. |
| **Wallet (Flux)** | Only if you sell for Flux | `OnWalletChanged` → update your HUD; `OnWalletError` → show the reason. | `WalletGet()` to read; `WalletSpend(n, reason)` to spend, and wait for `OnWalletChanged` before granting. **A game cannot earn Flux** — `WalletSet` was removed and the platform refuses it. |
| **Rewarded ad** | Optional | `OnAdStarted` → pause; `OnAdFinished(rewarded)` → resume. | `ShowRewardedAd(id)`. Grant **your own** reward only when `rewarded` is true. **An ad pays no Flux.** |
| **Leaderboard** | Optional | Nothing to handle — the platform owns the share UI. | `LeaderboardScore(score, metricKey, …)`; keep `metricKey`/`sortDirection` consistent. |
| **Achievements** | — | — | **Removed.** No manifest, no progress, **no Flux reward.** Track them in your own game and currency. |

The rest of this file is that table in detail, one section per row.

## The three save modes

These are the three answers to *"Does your game save progress?"* in the publish wizard. The
demo lets you switch between them at runtime; a real game picks one.

### 1. Local only

`PlayerPrefs`, nothing else. Progress dies with the browser profile and never reaches another
device. Fine for a game where progress does not matter.

### 2. Platform (local + cloud mirror) — the usual choice

Your game keeps saving locally exactly as it does today. It **also** mirrors the same values
to the player's Arsmi Games account, so their progress follows them to another device.

```csharp
if (hub.DataReady)
    level = hub.GetInt("level", 1);

hub.SetInt("level", 7);   // batched; written about a second later

hub.OnDataChanged += () => { /* re-read your values */ };
```

Reads come from a copy held inside C#, so calling `GetItem` in `Update()` never crosses into
JavaScript.

**`OnDataChanged` is not optional.** It fires when the platform replaces your values — after a
guest signs in and their progress is merged into their account, or when the player's *other
device* turns out to be further ahead. Ignoring it is how progress gets lost.

Guests can play and save without logging in. Their progress is held by the platform and merged
into their account if they sign up; on a conflicting key the account's value wins, so a
throwaway guest session can never clobber a real save.

### 3. Your own backend

The platform stores nothing. It only tells the game **who the player is**:

```csharp
var playerId = hub.PlayerId;   // null for guests
```

`PlayerId` is pseudonymous and **per game**: stable forever for this player in this game, but
two games cannot compare ids to work out they have the same person. Key your own records on
it. Never use the raw platform user id for that.

#### The player's email

`hub.Email` carries the real address — but only if your game was **granted it**, and it is
`null` otherwise:

```csharp
hub.OnUserChanged += () => {
    if (hub.Email != null)          Send(hub.PlayerId, hub.Email);
    else if (hub.EmailShared)       Log("Granted, but this player has no address on file.");
    else                            Send(hub.PlayerId);   // the normal case
};
```

Three things must all be true before an address arrives:

1. **Own-backend save mode** — a game the platform saves for has no backend to send it to.
2. **The per-game grant.** Tick *"This game requires sharing of player email"* in your game's
   options when you submit. It is read at review, so tick it only if your backend genuinely
   needs the address and be ready to say why.
3. **The player is signed in.** Guests have no address.

`EmailShared` tells you which side of that you are on: `false` means the platform withheld it,
`true` with a null `Email` means the grant is in place but this particular player has none.

> **Do not build your login on `Email`.** It can be null forever, for any player, and the grant
> can be withdrawn. `PlayerId` is the identifier that is always there — key on it, and treat an
> address as extra information you may or may not get.

An email address identifies the same person across every game, which is exactly what `PlayerId`
is designed to prevent. That is why it is a per-game decision rather than a flag you can set
yourself, and why most games should never ask for it.

`ArsmiBackendClient` is a worked example against Supabase — swap its two coroutines for your
own endpoints and nothing else in the game changes. To try it, run
`supabase/demo_quiz_backend_schema.sql`, then fill in the Supabase URL and anon key on the
**ArsmiDemo** object in the sample scene.

> The anon key is public by design, and the demo table's RLS policy lets anyone holding it
> read and write any row. That is fine for a quiz score keyed by an id that identifies nobody,
> and **not** fine for anything else. A real backend verifies the player server-side rather
> than letting the browser talk to Postgres directly.

## Wallet

Flux Coins are real currency, so the balance is whatever the **server** says it is — never what
the game says it is. A game can read it, and ask to spend from it.

```csharp
hub.OnWalletChanged += balance => UpdateHud(balance);
hub.OnWalletError   += message => ShowMessage(message);   // "Not enough Flux Coins."

hub.WalletGet();                     // read
hub.WalletSpend(50, "extra-hint");   // the server CAN refuse this
```

Do not hand over what the player is buying until `OnWalletChanged` fires.

**There is no way to add coins, and there will not be one.** `WalletSet` has been removed — it
wrote an absolute balance and was trusted as-is, so any game could mint unlimited currency with
one call. The platform now refuses the message outright.

Flux Coins go up in three places, none of them a game: the player **buys** them, the player
watches **the platform's own ad** from the platform's UI.

If your game has its own currency — coins, gems, lives — that is yours to grant however you
like. It does not convert to Flux.

**Never put currency in a save.** Save data lives on the player's machine and is trusted as
written — the player can edit it. Purchases and anything else worth cheating for go through
the wallet, which is server-authoritative.

## Rewarded ads

The ad is a **platform overlay**, drawn over your game. Your game does not render it, does not
time it, and does not decide whether it was watched — a game cannot be trusted to report that,
so the decision stays outside the iframe. Your game asks, pauses, and waits.

**An ad your game asks for pays no Flux Coins.** It pays whatever *your* game promised — the
hint, the extra life, the skin — and *your* code grants it. There is no balance in the callback,
because no balance moved.

(The platform has its own "watch an ad for Flux" button in its UI. That one is the platform's,
the player starts it deliberately, and it has nothing to do with your game.)

```csharp
hub.OnAdStarted  += PauseGame;          // the platform mutes the frame; it does not pause you
hub.OnAdFinished += rewarded => {
    ResumeGame();
    if (rewarded) GiveHint();           // your reward, your currency, your call
};

hub.ShowRewardedAd("quiz-hint");
```

`rewarded: false` means the player skipped it or it failed. Grant nothing.

## Mute

Two-way, and both directions matter. **Your game does not need a mute button** — the platform
provides one. Your job is only to make its button real, and to keep it honest if you have
volume controls of your own.

### Platform → game: silence *everything*

When the platform mutes you, drop **every** audio channel to zero — music, SFX, ambience,
voice, UI, all of it. The one-liner that cannot miss a channel is the global listener:

```csharp
void Start()
{
    hub.OnMuteChanged += (muted, fromPlatform) => AudioListener.volume = muted ? 0f : 1f;
}
```

`AudioListener.volume` sits below every `AudioSource` in the scene, so zeroing it silences all
of them at once — nothing can slip through. (`AudioListener.pause = muted` also works, but it
*halts* audio rather than muting it; prefer `volume` if you want music to keep playing
inaudibly and resume in place.)

If you route through an **AudioMixer** with exposed parameters, mute by snapping them to
`-80 dB`, and remember every group:

```csharp
hub.OnMuteChanged += (muted, fromPlatform) =>
{
    var db = muted ? -80f : 0f;
    mixer.SetFloat("MusicVol", db);
    mixer.SetFloat("SfxVol",   db);
    mixer.SetFloat("AmbienceVol", db);
    // …every exposed group. Missing one is the bug the mute check catches.
};
```

Subscribing to `OnMuteChanged` is what acks the platform's probe — a game that wires this up
passes the mute check, a game that never subscribes fails it. Silence is not evidence that
mute works.

### Game → platform: report when *you* go silent

If your game has its own volume sliders, **all of them at zero is the player muting the game**,
and the platform's speaker icon should follow. Whenever a slider moves, tell the platform
whether everything is now silent:

```csharp
public void OnVolumeChanged()
{
    bool allSilent = musicVolume == 0f && sfxVolume == 0f && ambienceVolume == 0f;
    hub.SetMuted(allSilent);   // true when every channel is 0, false as soon as one is raised
}
```

The bridge drops no-op updates, so calling this on every slider tick sends nothing redundant
and cannot loop against the platform's own `set_mute`. If your game has **no** volume controls,
skip this direction — honouring `OnMuteChanged` is the only mandatory half.

## Fullscreen

**Your game does not need a fullscreen button** — the platform provides one, and clicking it
resizes the frame around your game. All you have to do is *acknowledge* that you heard it:

```csharp
void Start()
{
    hub.OnFullscreenChanged += fullscreen => { /* acknowledged just by subscribing */ };
}
```

Subscribing is what makes the fullscreen check pass — it proves your game is listening, rather
than the platform resizing a canvas that will never know it changed. A Unity WebGL canvas set
to fill its container re-fits on its own, so most games need nothing in the body. If you drive
layout from code, `OnFullscreenChanged` is where you re-read the screen size.

### If your game *has* its own fullscreen button

Then point it at the platform — do **not** call `Screen.fullScreen` yourself. The build runs
in an iframe, and only the platform can size the frame; doing it from Unity fights the chrome
around you:

```csharp
myFullscreenButton.onClick.AddListener(() => hub.RequestFullscreen());
```

The platform goes fullscreen and sends `set_fullscreen` back, so your `OnFullscreenChanged`
handler runs for both your button and the platform's — they behave identically. **If your game
has no fullscreen button, do not add one for this.** The platform already provides one;
acknowledging `OnFullscreenChanged` is all that is required.

## Leaderboards

Optional — a game with no ranking does not need it. If you use it, `LeaderboardScore` carries
the board's own definition (`metricKey`, `metricLabel`, `sortDirection`) inline, so a single
call both declares the board and submits to it:

```csharp
hub.LeaderboardScore(120, "quiz_score", "Quiz score", "desc");
```

You can also declare a board up front with `hub.LeaderboardDefine("quiz_score", "Quiz score",
"desc")` — useful if you want the board to exist before the first score — but it is not
required, because the score message describes its own board.

**Requirement — keep the key and direction consistent.** Every submit for one board must use
the same `metricKey` and `sortDirection`. A submit is a **best**, not a **set**: the platform
keeps the player's best for that direction (`desc` = higher wins, `asc` = lower/faster wins)
and only replaces it when a new score **beats** it. A game that reads its own last submit back
as "the score" will disagree with the platform, which kept the higher earlier one. A typo in
`metricKey` does not error — it just quietly writes to a different board.

**Acknowledgement.** The submit is outbound; the platform acks it. There is no inbound
leaderboard message a Unity game must handle — the platform owns the "share your score" UI, and
there is nothing to wire on the game side.

**The platform has no achievements.** There is no `AchievementProgress`, no
`AchievementsDefine`, and no manifest to send — the feature was removed. Track achievements
inside your own game and **reward the player in your own currency, never Flux** — they were
never worth any Flux Coins, so for most games this is only a change of where the code lives.

The **SDK Assessment** page in admin replays these rules and lists anything that would be
thrown away.

## TextMeshPro

The sample needs TMP's Essential Resources. If its labels render as nothing at all, run
**Arsmi Games → Import TextMeshPro Essentials**.

---

## Releasing (maintainers)

The package is developed **inside** the Unity SDK project, as an embedded package at
`Packages/com.arsmi.gamehub/`, and consumed from that same repo by git url with `?path=`.
Nothing has to be split out or mirrored — a release is a push and a tag:

```bash
# bump "version" here in package.json, add a CHANGELOG.md entry
git commit -am "gamehub 1.1.0"
git tag v1.1.0
git push --follow-tags
```

Bump the version and write the changelog entry **in the same commit as the change**, not at
tag time. An untagged consumer tracks the default branch and picks the change up whether or
not you remembered to write it down.

Consumers who install without a tag get whatever is on the default branch, so anything you
push is live for them immediately. Tag, and tell people to pin.
