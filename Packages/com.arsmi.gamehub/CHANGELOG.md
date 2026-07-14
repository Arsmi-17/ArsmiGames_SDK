# Changelog

All notable changes to this package are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
