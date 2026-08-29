# Changelog

All notable changes to this package are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [4.5.6] - 2026-08-29

### Added

- **Auto, a third orientation in Build WebGL…** — for a game that genuinely plays either way up.
  The canvas takes the shape of the window instead of a fixed one, so a phone held upright plays
  the game portrait and the same phone turned plays it landscape.

  The platform has accepted `auto` as a game's declared orientation for some time. Until now
  nothing could build one: the window offered Landscape and Portrait only, and whichever you
  picked was stamped into `index.html` and locked the canvas to that shape. A developer who
  declared their game auto at upload still shipped a build that could not adapt.

  It is one media query in the WebGL template. The browser re-evaluates it on rotation by
  itself, the canvas relocks through the same `min()` the other two shapes use, and Unity
  matches its framebuffer to the new rendered size exactly as it already does — so there is
  nothing to run at play time and nothing that can fail to run.

  ```
  -arsmiOrientation auto      # and in CI
  ```

### Notes

- **Landscape is still the default.** A default that changed under you would reshape your next
  build without your asking.
- Only pick Auto if your game really does lay out both ways. A landscape-only game built as Auto
  is squeezed into the portrait frame rather than letterboxed inside it — worse than the black
  bars the lock would have given it.
- `gamehub:unity:ready` reports `auto` for such a build. That event is the build answering for
  what it was made for, so it names what it is rather than a shape it does not have.
- `ChosenOrientation()` now parses the stored preference instead of comparing it to one name.
  The old form mapped anything that was not `Portrait` to `Landscape`, so a third shape would
  have silently built the second one.

## [4.5.5] - 2026-08-29

### Added

- **Your game can now be told it is on the platform.** Until now it could not find out. The
  JavaScript bridge subscribes to every platform message on your behalf whether a host is there
  or not, and `OnContext` arrives either way carrying a locally guessed context — so from C# a
  build inside the player page and a build opened from a `file://` URL looked identical.

  ```csharp
  GameHubBridge.Instance.OnConnection += c => {
    if (c.Connected) StartOnlineRun(c.GameId);
    else             StartOfflineRun();
  };
  ```

  `GameHubConnection` carries `Connected`, a `Reason` when it is false, and — when it is true —
  `SessionId`, `GameId`, `Slug`, `Role`, `Preview`, `PlatformVersion` and `Protocol`. Also
  readable as `GameHubBridge.Instance.Connection`, with `IsConnected` as the shorthand.

- **`ConnectionKnown`**, because "not yet" and "no" are different answers. `IsConnected` is false
  before the handshake lands and false for ever off-platform; a game that treats the first as the
  second shows its offline screen for a moment on every single load. `OnConnection` does not fire
  until there is something true to say.

- **A definite no.** If no platform answers within a second and a half, the event fires with
  `Connected = false` and `Reason = "standalone"`. A signal that only ever fired on success would
  leave a genuinely offline game waiting for ever. A host that answers after that still connects,
  and your handler is called again.

- **An answer in the Editor**, `Reason = "editor"`, raised by the package itself. There is no
  JavaScript bridge in the Editor, so without this a game testing its own offline path would wait
  on a callback that could never arrive.

### Notes

- Subscribing after the answer has arrived fires immediately. That is the normal case, not an
  edge one: `Awake()` starts the JavaScript bridge, which is answered at once when the handshake
  has already happened, so the acknowledgment routinely reaches C# before any game's `Start()`
  runs. An event that only fired on change would be missed by nearly every game.
- Nothing new crosses the frame boundary — the wire protocol is still 2, and this is the SDK
  reporting a handshake it already had. Subscribing claims no capability and does not affect the
  publish gate.

## [4.5.4] - 2026-08-16

### Added

- **The platform now tells your game what device it is running on.** A WebGL build runs in an
  iframe the platform sized, so `Screen.width` measures the frame and not the device — a desktop
  browser at a narrow window and a real phone look identical from inside a build. The platform
  detects the device and sends it at handshake.

  ```csharp
  GameHubBridge.Instance.OnDevice += device => {
    if (device.Touch) ShowTouchControls();
    else              ShowKeyboardHints();
  };
  ```

  `GameHubDevice` carries `Type` (`"mobile"`, `"tablet"` or `"desktop"`), the input flags
  `Touch` / `Keyboard` / `Mouse` / `Gamepad`, and `Source`. Also readable at any time as
  `GameHubBridge.Instance.Device`.

  `Type` and the input flags are deliberately separate: an iPad with a keyboard case is a tablet
  that types, and a touchscreen laptop is a desktop that taps. Gate features on `Type`, choose
  controls on the flags.

  `Keyboard` is the best signal available rather than a certainty — nothing can tell a build
  whether a keyboard is attached. `Gamepad` is a snapshot taken at handshake; use Unity's own
  input system for a pad connected later. Neither `Type` nor the flags change during a session.

- **`DeclareDeviceSupport(params string[] types)`** — say which devices your game is built for,
  e.g. `DeclareDeviceSupport("desktop")`. It only ever restricts your own game. The platform reads
  it during upload preview and pre-fills the **Supported devices** field; players on other devices
  see a note above the play button and **are not blocked from playing**. Declaring nothing means
  every device and is what most games should do.

  None of this is required, and none of it affects whether a game can be published.

### Internal

- `ReadJsonObject(json, field)` — the existing JSON readers only ever saw top-level fields, and
  `device` is the first nested object the bridge has had to read.

## [4.5.3] - 2026-08-05

### Fixed

- **Every WebGL build rendered at a third of a phone's resolution.** The template set
  `config.devicePixelRatio`, sniffing the user agent and forcing **1** on anything that looked
  like a phone. That field overrides the browser — Unity's loader reads
  `Module.devicePixelRatio || window.devicePixelRatio || 1`, in that order — so the device's own
  answer was thrown away on every mobile device.

  It is the line Unity ships **commented out** in its own default template, under *"to lower
  canvas resolution on mobile devices to gain some performance"*. An opt-in sacrifice, which this
  template had permanently on for every phone.

  On a DPR 3 phone it meant Unity rendered a 640×360 framebuffer and the browser stretched it
  across 1920×1080 of glass — a third of the detail in each direction, about a sixteenth of the
  pixels the same build gave a desktop, and every edge in the game soft. Desktops were unaffected,
  which is why it looked like a mobile hardware limit rather than a line of template code.

  Now `Math.min(2, Math.max(1, window.devicePixelRatio || 1))`: the device's own ratio, with a
  ceiling so a 4K desktop or a DPR 4 phone does not render sixteen megapixels of a game that does
  not need them, and a floor so a zoomed-out browser reporting less than 1 cannot go below native.
  There is no device list to maintain — the browser already knows, and every phone reports
  differently.

  In practice: a 1600×720 Android at DPR 2 and an iPhone at DPR 3 both go from 1× to 2×, four
  times the pixels. Desktops are unchanged.

- **Touchscreen laptops were treated as phones.** The same check matched
  `(pointer: coarse)`, so a desktop with a touchscreen was forced to 1 as well. The sniff is gone
  entirely; nothing now branches on what kind of device is asking.

  Nothing in a game needs to change for this. The template is reinstalled from the package on the
  next domain reload and the fix ships with the next build — but note that reinstall **overwrites**
  `Assets/WebGLTemplates/ArsmiGames/`, so any local edits to that copy are lost.

## [4.5.2] - 2026-08-04

### Fixed

- **With *Include scripts* on, the *Unreferenced* tab archived scripts the project compiles
  against.** A scene references a MonoBehaviour's `.cs` by GUID, and that is the only script edge
  Unity records. What that script then names — a helper class, an interface, a struct, a static
  utility — is resolved by the compiler, so every one of those files looked unreferenced. Moving
  them stopped the script that *is* in the scene from compiling, which takes the whole project
  down rather than one asset with it.

  This package's own sample was the case exactly: `DemoBootstrap` is the one script on a
  GameObject, and `DemoUI`, `KidsQuiz` and `SdkFunctionPanel` are reached only through the types
  it names. All three were on the move list.

  C# references are now followed. A script counts as used if something references it by GUID, if
  any script already counted as used names a type it declares, or if an Editor script needs it —
  transitively, and including interfaces, attributes by their short name, extension methods by
  their own name, the other halves of a partial class, and a type named only in a string the way
  `AddComponent("Enemy")` names one. Editor scripts seed the walk because they are never archived
  and so have to go on compiling.

  Deliberately generous: keeping a file that is merely mentioned costs a line in a list, and
  missing an edge costs a project that does not build.

- **Assets named in a string are no longer invisible.** `Resources.Load("Sprites/coin")`,
  `Shader.Find("Custom/Water")` — a literal matching an asset's file name, or the name a shader
  declares on its own first line, now keeps that asset. The tab had been warning that it could not
  see these; it can see the literal ones, which is most of them. Names built out of pieces at
  runtime are still beyond it, and it still says so.

## [4.5.1] - 2026-08-04

### Fixed

- **The *Unreferenced* tab offered to archive the assets a URP project cannot render without.**
  A URP project keeps its render pipeline asset, its renderer, its global settings and its default
  volume profile in `Assets`, and the only thing pointing at any of them is Project Settings — no
  scene does. The scan rooted at scenes, Resources and preloaded assets, so it called all four
  unreferenced and *Move to Archive* took them out of the project. Always-included shaders,
  preloaded shader variant collections, per-quality-level pipeline overrides and the splash screen
  logo were in the same position.

  Project Settings are now roots. Walked generically over their serialised object references
  rather than field by field, so it catches the ones a hand-written list would miss.

- **A shipping shader's `#include` files were reported unreferenced.**
  `#include "TMPro_Properties.cginc"` is a filename in a string, not a GUID, so the asset database
  does not model the edge and the include looked like litter. Archiving it stopped the shader
  compiling — every piece of text in the project turns magenta, and the cause is a file that is no
  longer in `Assets`. In this repo's own project, five of TextMesh Pro's `.cginc` and `.hlsl`
  files were on the move list while the shader that includes one of them was shipping.

  Include edges are now followed in text, transitively, along with the bare GUID a Shader Graph
  Custom Function node stores for its HLSL file.

- **Asset bundles and Addressables are no longer invisible.** Anything with an AssetBundle name,
  and everything the Addressables catalogue names, now counts as reachable. The tab had said in
  its own warning box that it could not see either.

### Changed

- **Move and Delete only ever touch rows nothing points at.** Anything still assigned to
  something — a scene that is not ticked in Build Settings, a prefab, a material, a settings
  asset — is listed as build weight and left where it is, with the row naming what holds it. This
  is stricter than before deliberately: a material on a prefab no scene has yet is assigned work,
  and reachability from the build cannot tell it apart from litter.

  It holds even when the thing pointing at it is itself on the list. A dead prefab and its dead
  material both being weight does not make it safe to take the material out from under the prefab
  in the same click — archive the prefab, scan again, and the material moves on the second pass.
  Two passes to clear a chain is the price of never breaking a live reference.

  The buttons now say how many files they will take, and *Select orphans* is *Select movable*, for
  the same reason.

## [4.5.0] - 2026-07-27

### Added

- **Move to Archive** in the Build Size Report's *Unreferenced* tab. Moves the listed assets to
  `Archive/`, a sibling of `Assets`, keeping their folder structure —
  `Assets/Graphics/Art/cat.png` becomes `Archive/Graphics/Art/cat.png`.

  Out of `Assets` rather than into a folder inside it, which is the tempting version and achieves
  almost nothing: an asset inside `Assets` is still imported on every project load and can still
  ship. The `.meta` moves with the file, so the GUID survives and moving anything back restores
  every reference to it — which is what makes this the safe button and Delete the last resort,
  given the list is explicitly a guess. Nothing is ever overwritten; a repeated archive gets a
  numbered suffix.

- **Which scenes use an unreferenced asset**, named on the row. The scan roots at scenes ticked in
  Build Settings, because that is what decides build size — so an asset used only by an unticked
  scene appears here, correctly for size and misleadingly for "is this safe to remove". The summary
  now counts orphans and used-elsewhere separately, warns when the second group is non-empty, and
  *Select orphans* picks only the assets no scene references at all. Scene attribution is
  cancellable; cancelling leaves the size verdict intact and only fills in fewer names.

- **Quick-select in *Shrink***: all heavy assets, textures, audio or models above a size you choose.
  Each button states its own count and total before you press it, and is disabled rather than
  hidden at zero, so "are there any heavy textures?" is answered by the button itself. Ranked by
  packed size when a build has been measured and by file size otherwise, and the tab says which.

- **A build window** in front of *Arsmi Games ▸ Build WebGL…*, replacing the modal that asked one
  question. It shows the scenes going into the build, marks which one **loads first**, and gives
  you one button to change it.

  Orientation was the only thing the old dialog asked about, and it is the one thing that is
  cheap to get wrong — a frame the wrong shape is obvious and one rebuild away. Scene order is
  the opposite: the build succeeds, uploads, and starts on the wrong screen, and nothing anywhere
  says why. Worth noting the window marks *loads first* rather than numbering rows, because Unity
  numbers disabled rows too, so its `0` is not always the scene that runs.

  Opening the window also turns **decompression fallback** on when it is off, and leaves a notice
  saying it did. The build already forced this, but only after you had committed to a build and
  picked a folder.

- **Arsmi Games ▸ Build Size Report** — where the build's size went, and what to do about it.
  Per-asset packed sizes from the last build, the heaviest files on disk, assets no enabled scene
  reaches, and the settings and bulk import actions that shrink a build.

  Packed size is recorded during the build because that is the only time it exists:
  `BuildReport.packedAssets` lives inside the post-process callback and Unity discards it
  afterwards. It is worth having rather than sorting by file size, which is the obvious approach
  and is wrong often enough to cost an afternoon — a 30 MB PSD can pack to 200 KB, and a 900 KB
  PNG imported uncompressed can pack to 16 MB.

  The unreferenced list is explicitly a guess and the window says so: Addressables, asset bundles
  and `Resources.Load` with a runtime-built name are all invisible to it.

### Changed

- `ArsmiBuild.BuildWebGL` now opens the window; the build itself moved to
  `ArsmiBuild.RunInteractiveBuild`. `BuildFromCommandLine` is untouched, so CI is unaffected.

### Fixed

- `package.json` said `4.3.0` throughout 4.4.0 — the release added the sample and the changelog
  entry but never bumped the manifest, which is the only version Unity actually reads. Anyone who
  updated to get the Pocket Console sample saw no version change and had no way to tell which
  package they had. Corrected here rather than by re-tagging 4.4.0, since that version is already
  pushed.

## [4.4.0] - 2026-07-26

### Added

- A second sample: **Pocket Console Demo**. A phone as a controller with no game underneath it —
  four screens, four seats, and both ways of addressing them. It ships its own HTML controller
  beside the scene, so the pair can be run together without writing either half. Import it from
  *Arsmi Games ▸ Import Pocket Console sample*, then read its `README.md`.

  It exists because every other example of this API is embedded in a game, and the two mistakes that
  cost the most are invisible there: the input payload is nested under `input`, and `OnPocketInput`
  must be subscribed *before* `PocketReady` or the bridge reports the game as not supporting pocket
  input at all. Both are called out in the sample where you meet them.

## [4.3.0] - 2026-07-26

### Fixed

- `PocketState` and `PocketSeatState` now reach a phone from the **Unity Editor**, not only from a
  WebGL build. Outside WebGL there is no `.jslib`, so both calls ended at a `Debug.Log`: presses
  arrived in play mode and nothing came back, which reads as a broken state channel rather than as a
  WebGL-only path. Reported from a real session whose Console showed
  `[GameHubBridge] PocketState {"slot":null,"screen":"menu","data":{}}` while the phone never moved.

### Added

- `GameHubBridge.OnPocketStateEnvelope` — raised in the non-WebGL branch with the envelope exactly as
  the Bridge built it. `PocketEditorLink` subscribes and forwards it to the local harness, so the
  Editor and WebGL paths carry an identical payload and a screen cannot work in one and not the
  other. Games do not subscribe to this; the Editor companion does.

## [4.2.0] - 2026-07-26

### Added

- Games can move a phone between screens. Two calls, and they are the whole vocabulary:

  ```csharp
  hub.PocketState(screen, dataJson);            // every seat
  hub.PocketSeatState(slot, screen, dataJson);  // one seat; the others carry on
  ```

  A game never declares whether it is seat-based — it expresses that by which call it makes. One
  whose first finisher ends the round for everyone calls `PocketState`; one where the others keep
  playing targets the seat that finished. A race needs both, which is why a mode flag would have
  been self-contradictory.

  No new wiring bit: `pocket` already means "handles phone input", and pushing a screen is not
  handling input. Two bits for one capability is how a game ends up reported as supporting Pocket
  Console when it reads nothing.

  A bad seat number is logged, not thrown. A game must not die because it computed a seat wrong,
  and a silent drop would be worse than either.

## [4.1.0] - 2026-07-26

### Fixed

- Pocket input wiring is reported from C#, honestly. The `.jslib` subscribes to pocket input on the
  game's behalf, so **every** Unity build acked `handled: true` whether or not its C# did anything
  with the press — and `ReportWiring` had no `pocket` key at all, so there was no honest signal to
  read instead. A game that ignored every press was indistinguishable from one that handled them.

## [4.0.0] - 2026-07-22

Wire protocol 2. **This release does not talk to a platform on protocol 1, and a game built
against 3.x does not talk to a platform on protocol 2.** Rebuild your game against this package.

### Changed — BREAKING

- Every event is now `gamehub:<domain>:<verb>`. The last four snake_case names are gone:
  `set_mute` → `gamehub:audio:set`, `audio_muted` → `gamehub:audio:changed`,
  `set_fullscreen` → `gamehub:screen:set`, `fullscreen_request` → `gamehub:screen:request`.
  This only affects games that send or subscribe to raw event names through `Emit`/`on`; the C#
  API (`OnMuteChanged`, `RequestFullscreen`, …) is unchanged.
- `request_fullscreen` is deleted. It was a second spelling of `fullscreen_request` that the
  platform accepted for long enough that both ended up in the documentation.
- `gamehub:login:request` is deleted; use `gamehub:auth:login`. `RequestLogin()` is unchanged.

### Added

- Pocket Console, Challenge, leaderboard sharing and context are real C# events at last:
  `OnPocketInput`, `OnPocketPlayerJoined`, `OnPocketPlayerReconnected`, `OnPocketPlayerLeft`,
  `OnChallengeStart`, `OnChallengeLeaderboard`, `OnChallengeEnd`, `OnLeaderboardSharing`,
  `OnContext`. These messages already arrived from the .jslib and went no further than a
  `Debug.Log`, so the features were listed as supported and no Unity game could act on them.
- A casino API: `CasinoRound`, `CasinoSeed`, `CasinoRotateSeed` and `OnCasinoResult`. Unity had
  none at all. You send a bet; the server rolls and settles. There is no way to report an
  outcome, by design.

### Fixed

- Save writes made before the player's save arrives are now dropped with a warning instead of
  being accepted. On a browser the player has never used, everything local reads as "new", so a
  write in that window carried a blank state and landed on top of the real account save still in
  flight. That is not hypothetical — it replaced a player's progress with zeroes.

## [3.1.0] - 2026-07-21

### Added

- `GameHubBridge.Email` and `GameHubBridge.EmailShared`. The platform had been sending the
  player's address in the user-state payload all along; the bridge parsed four fields out of
  that JSON and silently dropped this one, so a Unity game appeared to be denied something the
  JS SDK was already handing out.

  `Email` is null unless the game is in **own-backend** save mode AND holds the per-game
  *"requires sharing of player email"* grant AND the player is signed in. `EmailShared`
  separates "the platform withheld it" from "this player has no address on file".

  **Key your records on `PlayerId`, never on `Email`** — an address can be null forever, and
  the grant can be withdrawn. See "The player's email" in the README.

## [3.0.0] - 2026-07-14

**Achievements are gone from the platform.** Not capped, not deprecated — removed. The tables,
the routes, the profile tab, the admin screens, and the SDK surface.

### Removed

- `AchievementsDefine`, `AchievementProgress`, `AchievementProgressJson` and
  `OnGameHubAchievementsSharing`. The platform refuses `gamehub:achievements:manifest` and
  `gamehub:achievement:progress`, and the JS SDK will not even send them: they never leave your
  iframe, and you get a console error naming the event.

### Migrating

Track achievements **inside your own game**, and reward the player in **your own currency**.

That was already the only thing a game's achievements could do — 2.0.0 forced their `rewardFlux`
to zero, because a game that prices its own rewards in real currency is a game that mints money.
So for most games this is a change of where the code lives, not what it does.

```diff
- GameHubBridge.Instance.AchievementProgress("quiz_correct", 1);
+ _myOwnAchievements.Advance("quiz_correct", 1);   // your save, your currency, your UI
```

Leaderboards are untouched.

## [2.0.0] - 2026-07-14

**A game cannot increase Flux Coins.** Breaking, deliberately, and it will break any game that
was doing so — which is the point.

### Removed

- **`WalletSet` is gone.** It wrote an absolute balance and was trusted as-is, so any game could
  mint unlimited currency with one call. The platform refuses the message now, and the SDK will
  not even send it. Read with `WalletGet`, take with `WalletSpend`. There is no counterpart that
  gives coins, and there will not be one.

### Changed

- **`OnAdFinished` is now `Action<bool>`, not `Action<bool, int>`.** The `int` was the player's
  new Flux balance, and it was there because a rewarded ad used to pay Flux. It does not.

  An ad **your game asks for** pays out in **your game**: the extra life, the skin, the boss
  level, granted by your own code when `rewarded` is true. It never moves the player's Flux. (The
  platform has its own "watch an ad for Flux" button in its own UI. That one is not yours.)

  A game could previously loop `ShowRewardedAd()` and print money.

- **`rewardFlux` in an achievement manifest is read and thrown away.** A game's achievements are
  worth **0 Flux**. The manifest is written by the game, so `rewardFlux` was a number the game
  chose for itself — define an achievement worth a million against a metric you emit, complete
  it, claim it. Reward your players in your own currency instead.

### Migrating

```diff
- hub.OnAdFinished += (rewarded, balance) => { if (rewarded) GiveHint(); };
+ hub.OnAdFinished += rewarded => { if (rewarded) GiveHint(); };

- hub.WalletSet(newBalance);   // no replacement — a game cannot add coins
+ hub.WalletSpend(50, "hint"); // taking is still fine
```

## [1.1.0] - 2026-07-14

The platform now checks that a game really handles mute and fullscreen before it will publish
it, and a game cannot be published until it does. **Rebuild with this version.** A build made
against 1.0.0 cannot answer the check, and a game that cannot answer is treated as a game that
does not work — which is the point, but it means an old build will be blocked at upload.

### Added

- `OnFullscreenChanged` and `IsFullscreen`. Subscribing is what tells the platform you handle
  fullscreen at all. Previously `OnGameHubFullscreen` was a `Debug.Log` and nothing else.
- Acknowledgements. The platform sends a real `set_mute` and `set_fullscreen` carrying the
  state the game is **already in** — silent to the player — and `GameHubBridge.cs` answers
  whether anything is actually listening. It has to come from C#: the `.jslib` subscribes on
  your game's behalf whether or not your C# does anything with them, so a JavaScript answer
  would report every Unity build ever made as compliant.

### Fixed

- **`engine: "unity"` never took effect.** The WebGL template loads `gamehub-sdk.js` in
  `<head>`, so by the time `GameHubBridge_Init` ran, `window.GameHubBridge` already existed and
  the `create({ engine: "unity" })` guarded by `if (!window.GameHubBridge)` was skipped every
  single time. Every Unity build was running in auto-wiring mode — inferring what the game
  implements from JavaScript subscriptions, which in a Unity build are the `.jslib`'s own. Every
  requirement came back "wired" no matter what the C# did. The false pass was hiding inside the
  mechanism built to prevent it.

## [1.0.0] - 2026-07-14

First release as a UPM package. Previously the SDK lived in `Assets/ArsmiGames/` and had to
be copied between projects by hand.

### Added

- Installable by git URL, with the Kids Quiz demo as an importable sample.
- **Arsmi Games → Import Kids Quiz sample** — imports the sample *and* puts its scene at
  index 0 in Build Settings, which the Package Manager's own Import button does not do.
- The WebGL template now ships inside the package and is copied into
  `Assets/WebGLTemplates/ArsmiGames` on load. Unity only discovers templates under `Assets/`,
  so a package cannot provide one directly.
- Wallet: `WalletSpend(amount, reason)`, `FluxCoins`, `OnWalletChanged`, `OnWalletError`. The
  balance is the server's; a spend can be refused.
- Mute: `IsMuted`, `OnMuteChanged(muted, fromPlatform)`. Previously the platform's volume
  button reached the game and the game did nothing with it.
- `SaveUpdatedAt` — when the platform last accepted a write.

### Changed

- `WalletSet` is `[Obsolete]`. It writes an absolute balance and is trusted as-is, so a game
  can mint currency with it. Use `WalletSpend`.
- The build now also fails if `gamehub-sdk.js` was not copied next to `index.html`. Without
  it a build works when the platform serves it and is mute on every other host — the hardest
  version of this bug to notice.

### Fixed

- The demo's achievement manifest was missing `shareWithPlatform` and `rewardFlux`, so it
  would have imported **zero** achievements on the real platform, silently.
