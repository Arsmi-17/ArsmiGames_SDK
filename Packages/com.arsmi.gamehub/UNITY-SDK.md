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

```csharp
event Action<string> OnPocketInput, OnPocketPlayerJoined, OnPocketPlayerReconnected, OnPocketPlayerLeft;
event Action<string> OnChallengeStart, OnChallengeLeaderboard, OnChallengeEnd;
event Action<string> OnLeaderboardSharing;
event Action<string> OnContext;

void PocketReady(int maxPlayers, string layout = "dpad-buttons", string schemaJson = "{}");
void PocketSchema(string json);
void ChallengeReady(int maxPlayers, string mode = "ranked", bool ranked = true);
void ChallengeState(string json);
void ChallengeResult(string json);
```

The payloads are raw JSON strings, deliberately. They carry game-defined shapes, and
`JsonUtility` cannot deserialise the dictionaries most of them contain — parse with whatever
your game already uses.

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

The platform reports what you wired up one frame into the first scene. If you subscribe later
than that, call `ReportWiring()` yourself afterwards or you will be assessed as not handling
what you actually handle.
