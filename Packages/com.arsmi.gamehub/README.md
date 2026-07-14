# Arsmi Games — GameHub SDK

Everything a Unity WebGL game needs to talk to the Arsmi Games platform, plus a demo that
exercises all of it.

## Install

**Window → Package Manager → + → Install package from git URL…**

```
https://github.com/Arsmi-17/com.arsmi.gamehub.git
```

Or add it to your project's `Packages/manifest.json` directly:

```json
{
  "dependencies": {
    "com.arsmi.gamehub": "https://github.com/Arsmi-17/com.arsmi.gamehub.git"
  }
}
```

`package.json` sits at that repo's root, so no `?path=` is needed.

**Pin a version** — append a tag: `…com.arsmi.gamehub.git#v1.0.0`. Without one you track the
default branch, and someone else's push silently changes your next build.

### "Repository not found"

Unity clones the **remote**, not your working copy, so this means git could not see anything
at that URL. Two very different failures wear the same coat:

1. **Nothing is pushed yet.** Committing locally is not enough — the package repo has to
   exist and have the commit in it.
2. **The repo is private.** GitHub answers an unauthenticated client with *"Repository not
   found"* rather than *403*, so a private repo is indistinguishable from a missing one.
   Unity shells out to `git`, so it needs credentials `git` can find on its own: a
   credential helper holding a PAT, or an SSH url
   (`git@github.com:Arsmi-17/com.arsmi.gamehub.git`).

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

**Arsmi Games → Build WebGL…** (`Ctrl+Shift+B`). Pick a folder. Upload it. There is nothing
to edit afterwards.

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
and cannot reach the platform at all. No saves, no achievements, no leaderboards, and *no
error*, in either direction. It is a failure with no symptom except silence, so it is caught
at build time instead.

For CI:

```
Unity.exe -quit -batchmode -projectPath . \
  -executeMethod ArsmiGames.EditorTools.ArsmiBuild.BuildFromCommandLine \
  -arsmiOutput Builds/WebGL
```

## Adding it to your game

Drop a `GameHubBridge` component on a GameObject in your first scene (or let something create
it, as the sample's `DemoBootstrap` does). Its `Awake` is what introduces the game to the
platform, so it must exist before anything asks it a question.

```csharp
var hub = GameHubBridge.Instance;
```

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

Do not hand over what the player is buying until `OnWalletChanged` fires. Earning is not in
here on purpose: coins come from rewarded ads and achievement claims, which the platform
grants after it has seen the thing happen.

`WalletSet` still exists but is `[Obsolete]` — it writes an absolute balance and is trusted
as-is, so a game can mint currency with it.

**Never put currency in a save.** Save data lives on the player's machine and is trusted as
written — the player can edit it. Purchases and anything else worth cheating for go through
the wallet, which is server-authoritative.

## Rewarded ads

The ad is a **platform overlay**, drawn over your game. Your game does not render it, does not
time it, and does not decide whether it was watched — the reward is real currency, so that call
stays outside the iframe. Your game asks, pauses, and waits.

```csharp
hub.OnAdStarted  += PauseGame;          // the platform mutes the frame; it does not pause you
hub.OnAdFinished += (rewarded, balance) => {
    ResumeGame();
    if (rewarded) GiveHint();           // ONLY here
};

hub.ShowRewardedAd("quiz-hint");
```

`rewarded: false` means the player skipped it or it failed. Grant nothing.

## Mute

Two-way, and both directions matter.

```csharp
hub.OnMuteChanged += (muted, fromPlatform) => AudioListener.pause = muted;
hub.SetMuted(true);   // the game muted itself; the platform's volume icon follows
```

You must honour `OnMuteChanged` when `fromPlatform` is true, or the platform's volume button
does nothing. The bridge drops no-op updates, so this cannot loop.

## Achievements and leaderboards

```csharp
hub.AchievementProgress("quiz_correct", 1);
hub.LeaderboardScore(120, "quiz_score", "Quiz score");
```

Two rules that bite, because both fail *silently*:

- A manifest entry is dropped by the platform's importer unless it has
  `shareWithPlatform: true` and `rewardFlux > 0`. No error, it simply never exists.
- A leaderboard submit only replaces your score when it **beats** the stored one, for that
  board's sort direction. A game that assumes every submit overwrites will disagree with the
  platform.

The **SDK Assessment** page in admin checks a manifest against those rules and lists what
would be thrown away.

## TextMeshPro

The sample needs TMP's Essential Resources. If its labels render as nothing at all, run
**Arsmi Games → Import TextMeshPro Essentials**.

---

## Releasing (maintainers)

The package is developed **inside** the Unity SDK project, as an embedded package at
`Packages/com.arsmi.gamehub/`. That is deliberate: you edit it with the demo scene open and
the Editor recompiling, rather than editing files in `Library/PackageCache` and losing them
on the next resolve.

It is *consumed* from a package-only repo, where `package.json` is at the root — so
installing it does not drag down `Assets/`, `ProjectSettings/`, or the WebGL builds.

Publish with `git subtree split`, the same way the platform vendors its realtime server.
From the **Unity SDK project repo**:

```bash
# once
git remote add pkg https://github.com/Arsmi-17/com.arsmi.gamehub.git

# every release
git subtree push --prefix=Packages/com.arsmi.gamehub pkg main
```

`subtree push` rewrites the package folder's history so that `package.json` lands at the
repo root, which is what makes the plain (no `?path=`) install url work. The commits stay
descended from the ones in the SDK project, so it fast-forwards each time rather than
forcing.

Then tag the release **in the package repo**, so consumers can pin to it:

```bash
git -C <package-repo-clone> tag v1.0.0 && git push --tags
```

Bump `version` in `package.json` and add a `CHANGELOG.md` entry in the same commit as the
change, not at tag time — an untagged consumer tracks the default branch and will pick the
change up whether or not you remembered to write it down.
