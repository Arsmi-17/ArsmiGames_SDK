# Changelog

All notable changes to this package are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [4.5.0] - 2026-07-27

### Added

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
