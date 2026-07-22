# Updating an existing Unity game to GameHub SDK 4.0.0

For a game already integrated against `com.arsmi.gamehub` 3.x. Wire protocol 1 → 2.

**A 3.x build does not talk to a protocol-2 platform.** The handshake succeeds and the game looks
connected, then every requirement check reports the game as implementing nothing. Update and
rebuild — there is no compatibility mode, by design.

## 1. Update the package

Package Manager → the `com.arsmi.gamehub` entry → Update, or re-add the git URL:

```
https://github.com/Arsmi-17/ArsmiGames_SDK.git?path=/Packages/com.arsmi.gamehub
```

Dropping `?path=` is what produces "Repository not found".

**If you imported the samples**, delete `Assets/Samples/Arsmi Games GameHub SDK/3.x/` and
re-import. Unity compiles the imported *copy*, not the package, so an old copy keeps compiling
silently against a new package.

Confirm `Assets/WebGLTemplates/ArsmiGames/gamehub-sdk.js` (or whichever template you build from)
came along — a build takes the SDK sitting in its template folder, not the one in the package.

## 2. What breaks

Only raw event names. **If you use the C# API — `OnMuteChanged`, `OnFullscreenChanged`,
`SetInt`, `WalletGet`, `ShowRewardedAd` — nothing in your code changes.**

If you call `Emit(...)` or subscribe by string:

| Old | New |
|---|---|
| `set_mute` | `gamehub:audio:set` |
| `audio_muted` | `gamehub:audio:changed` |
| `set_fullscreen` | `gamehub:screen:set` |
| `fullscreen_request` | `gamehub:screen:request` |
| `request_fullscreen` | deleted — use `gamehub:screen:request` |
| `gamehub:login:request` | deleted — use `gamehub:auth:login` |

## 3. What will now break at runtime if your save code is ordered wrongly

**Writes made before the player's save arrives are dropped**, with a warning naming the rule.
This is new in 4.0.0 and it is the change most likely to affect you.

It exists because on a browser the player has never used, every local value reads as "new" — so a
game that boots from `PlayerPrefs` and writes immediately overwrites a real account save with a
blank one. That is not hypothetical; it replaced a player's progress with zeroes.

Your boot must become:

```csharp
IEnumerator Start() {
    var hub = GameHubBridge.Instance;
    if (hub == null) { BootFromLocal(); yield break; }   // Editor, or outside the platform

    hub.OnDataChanged += ApplyCloudSave;                 // fires again later — see below
    if (hub.DataReady) { ApplyCloudSave(); yield break; }

    float deadline = Time.realtimeSinceStartup + 5f;     // never wait for ever
    while (!hub.DataReady && Time.realtimeSinceStartup < deadline) yield return null;
    if (!hub.DataReady) BootFromLocal();
}

void ApplyCloudSave() {
    var hub = GameHubBridge.Instance;
    if (hub.HasItem("YourGame.SaveVersion")) {           // a real save exists on the account
        YourProgress.DeleteLocalKeys();                  // R4: adopt it WHOLE
        foreach (var key in new List<string>(hub.Keys)) PlayerPrefs.SetString(key, hub.GetItem(key));
        PlayerPrefs.Save();
    }
    BootFromLocal();
}
```

`OnDataChanged` fires **more than once** — when the save first arrives, when a guest signs in and
their progress is adopted, and when another device turns out to be ahead. Make the handler safe
to run twice.

## 4. The save contract

Full text in `SAVE-CONTRACT.md`, shipped alongside this file. The two the SDK cannot enforce for
you:

- **R3 — never create a save for a player with no progress.** Do not write "zero levels, zero
  stars" to establish a save. An empty account save exists, counts, and then outranks a real one.
  Guard your seeding: if local progress is blank, write nothing.
- **R1 — write complete, self-consistent snapshots.** Counters and the per-level keys they count
  must move together. The platform never merges two saves key-by-key, precisely because a mixed
  map describes a player who never existed.

## 5. What you gain

Previously these messages reached C# and stopped at a `Debug.Log`; they are real events now:

```csharp
OnPocketInput, OnPocketPlayerJoined, OnPocketPlayerReconnected, OnPocketPlayerLeft
OnChallengeStart, OnChallengeLeaderboard, OnChallengeEnd
OnLeaderboardSharing, OnContext
```

And a casino API for admin-registered casino games: `CasinoRound`, `CasinoSeed`,
`CasinoRotateSeed`, `OnCasinoResult`.

## 6. Verify before you publish

1. Build, open in the platform's **SDK Assessment**, and confirm all five: SDK integration,
   Fullscreen, Mute, Save data, SDK version.
2. Console clean — no "called before the player's save arrived" warnings.
3. Play, refresh, progress survives.
4. Play in one browser; **sign in on a second browser** — progress is there. This is the
   acceptance test; steps 2 and 3 pass on a build that never reads its save back.
5. A brand-new account sees a genuinely fresh game.

If Save data fails with "it never used the save API", your game has not called `GetItem`/`SetItem`
and has not subscribed `OnDataChanged`. The platform reports what your C# actually did, not what
the SDK could have done for you.
