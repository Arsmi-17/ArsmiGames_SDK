# Arsmi Games — Unity SDK

The Unity project where the **GameHub SDK package** is developed, and the Kids Quiz demo that
exercises every call in it.

If you only want to *use* the SDK in your own game, you do not need this repo — install the
package:

```
https://github.com/Arsmi-17/ArsmiGames_SDK.git?path=/Packages/com.arsmi.gamehub
```

**Window → Package Manager → + → Install package from git URL…**

Or in your project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.arsmi.gamehub": "https://github.com/Arsmi-17/ArsmiGames_SDK.git?path=/Packages/com.arsmi.gamehub"
  }
}
```

Pin a version by appending a tag — `…?path=/Packages/com.arsmi.gamehub#v1.0.0`. Without one
you track the default branch, and someone else's push silently changes your next build.

> **"Repository not found"** means git could not see anything at that URL — and two very
> different failures wear the same coat. Either nothing is pushed yet, or the repo is
> **private**: GitHub answers an unauthenticated client with *"Repository not found"* rather
> than *403*, so a private repo is indistinguishable from a missing one. Unity shells out to
> `git`, so it needs credentials `git` can find on its own — a credential helper holding a
> PAT, or an SSH url (`git@github.com:Arsmi-17/ArsmiGames_SDK.git?path=/Packages/…`).

## What is in here

```
Packages/com.arsmi.gamehub/     THE PACKAGE — this is the product
  Runtime/
    GameHubBridge.cs            the bridge: every platform call and callback
    ArsmiSave.cs                one save API over the three published save modes
    ArsmiBackendClient.cs       example "your own backend" client (Supabase over REST)
    Plugins/WebGL/
      GameHubBridge.jslib       the JS side of the bridge; WebGL builds only
  Editor/
    ArsmiWebGLBuild.cs          the build tool, and the checks that make a broken build fail
    ArsmiTemplateInstaller.cs   installs the WebGL template into Assets/ (see below)
    ArsmiSamples.cs             Arsmi Games → Import Kids Quiz sample
    ArsmiSetup.cs               Arsmi Games → Import TextMeshPro Essentials
    WebGLTemplate~/             the template source: index.html + gamehub-sdk.js
  Samples~/KidsQuizDemo/        the demo. Not imported until you ask for it.

Assets/                          the host project. Nearly empty on purpose.
```

The package is **embedded** in this project rather than consumed from a git url. That is
deliberate: you edit it with the demo scene open and the Editor recompiling, instead of
editing files in `Library/PackageCache` and losing them on the next resolve.

## Running the demo

**Arsmi Games → Import Kids Quiz sample.** It imports the sample, puts its scene at index 0
in Build Settings, and opens it.

The Package Manager's own *Samples → Import* button works too, but it stops after copying the
files — the scene is not added to Build Settings, so a build straight afterwards produces an
empty player. The menu item does that last mile.

If the demo's text renders as *nothing at all*, TextMeshPro's Essential Resources are missing:
**Arsmi Games → Import TextMeshPro Essentials**.

In the Editor the bridge has no platform to talk to, so every call logs to the Console instead
of crossing over. That is expected. The real test is a WebGL build running inside the platform,
or pointed at **SDK Assessment** in admin.

## Building

**Arsmi Games → Build WebGL…** (`Ctrl+Shift+B`). Pick a folder. Upload it. **There is nothing
to edit afterwards** — that is the whole point of the tool.

The build is also *checked*. Two callbacks run around every WebGL build, including one started
from `File → Build Settings`, so the guarantee does not depend on which button you press:

- **Before:** installs the WebGL template if it is missing, then forces the template, the
  decompression fallback (the platform serves static files, so a Brotli build will not load
  without it), and Run In Background (or the game freezes the moment the player clicks any
  platform UI outside the canvas).
- **After:** reads the `index.html` that was **actually produced** and fails the build if the
  SDK is not in it, or if `gamehub-sdk.js` was not copied next to it.

### Why that last check exists

Unity's stock template does not load the platform SDK. When it is missing, the game still
builds, still loads, still renders, and **cannot reach the platform at all** — no saves, no
achievements, no leaderboards, and *no error*, in either direction. It is a failure with no
symptom except silence, and it is very easy to ship.

So it is caught at build time instead of in production.

### Why the template is copied into `Assets/`

Unity only discovers WebGL templates in `Assets/WebGLTemplates/`, and a package **cannot**
provide one — there is no package equivalent and no setting that points a build at one. So the
template ships inside the package under `Editor/WebGLTemplate~/` and is copied into `Assets/`
on load.

`Assets/WebGLTemplates/ArsmiGames/` is therefore **generated**. The copy in the package is the
source of truth; edits to the one in `Assets/` get overwritten.

The alternative was a line in the README telling people to copy a folder after installing the
package. That is a step people forget, and forgetting it produces exactly the silent failure
above.

## `gamehub-sdk.js` — the one seam

The template bundles a copy of `gamehub-sdk.js`, so a build works when hosted **anywhere** —
not just on the platform's own origin, where the absolute `/sdk/…` path resolves.

That copy has to match the platform's. **Nothing enforces it across repos.** The platform repo
has `npm run sdk:check`, which keeps its own copies honest, but it cannot see this one.

When the SDK changes on the platform, copy it into
`Packages/com.arsmi.gamehub/Editor/WebGLTemplate~/gamehub-sdk.js`. A stale copy does not error
— the game just quietly disagrees with the host about what the messages mean.

## Releasing the package

The package is developed here and consumed from this same repo by git url, so a release is
just a push and a tag:

```bash
# bump "version" in Packages/com.arsmi.gamehub/package.json and add a CHANGELOG entry
git commit -am "gamehub 1.1.0"
git tag v1.1.0
git push --follow-tags
```

Bump the version and write the changelog entry **in the same commit as the change**, not at tag
time. An untagged consumer tracks the default branch and picks the change up whether or not you
remembered to write it down.

## See also

- **[Web SDK reference](https://github.com/Arsmi-17/ArsmiGames_WebSDK)** — the same Kids Quiz
  in plain HTML/JS. Put the two side by side; the platform contract is identical, only the
  language changes.
- **SDK Assessment** in the platform's admin/dashboard — points at your build and shows the
  leaderboard, achievements, wallet and save it actually produced, plus the silent failures
  (a manifest the importer would drop, a score sent to a board you never declared).
- `Packages/com.arsmi.gamehub/README.md` — the full API, and what each call is for.
