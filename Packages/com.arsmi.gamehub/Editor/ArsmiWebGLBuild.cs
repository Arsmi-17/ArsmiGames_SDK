using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ArsmiGames.EditorTools
{
    /// <summary>
    /// Makes a platform-ready WebGL build, and makes a broken one impossible.
    ///
    /// The failure this exists to prevent: Unity's stock template does not load
    /// /sdk/gamehub-sdk.js, so GameHubBridge.jslib never wires itself up, every call
    /// from C# becomes a silent no-op in both directions, and *nothing errors*. The
    /// game just sits there, mute, with an empty log — which tells you nothing about
    /// why. Editing index.html by hand after each build is not a fix; it is a step
    /// someone eventually forgets, and then ships.
    ///
    /// So this hooks the build itself:
    ///
    ///   before — force the settings a hosted game needs (template above all).
    ///   after  — read the index.html that was actually produced and fail the build
    ///            if the SDK is not in it.
    ///
    /// Both run for *any* WebGL build, including File → Build Settings. The menu item
    /// below is a convenience, not the enforcement.
    /// </summary>
    /// <summary>Which way up the game is meant to be played.</summary>
    public enum Orientation
    {
        Landscape,
        Portrait,
    }

    public class ArsmiWebGLBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public const string Template = "PROJECT:ArsmiGames";
        public const string SdkFile = "gamehub-sdk.js";

        /// <summary>The orientation the next build should be stamped with. Set by the menu.</summary>
        public const string OrientationKey = "Arsmi.BuildOrientation";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL) return;

            // The template lives in the package and is copied into Assets/WebGLTemplates on
            // load, because Unity will not look for it anywhere else. Re-assert that here:
            // this is the last moment we can, and a build without it is a mute game.
            ArsmiTemplateInstaller.Install(force: false);

            if (PlayerSettings.WebGL.template != Template)
            {
                Debug.Log($"[Arsmi] WebGL template was '{PlayerSettings.WebGL.template}' — forcing '{Template}'.");
                PlayerSettings.WebGL.template = Template;
            }

            // The platform serves the build as static files and does not set
            // Content-Encoding, so without the fallback decompressor a Brotli build
            // simply fails to load in the browser.
            if (!PlayerSettings.WebGL.decompressionFallback)
            {
                Debug.Log("[Arsmi] Enabling WebGL decompression fallback (the platform serves static files).");
                PlayerSettings.WebGL.decompressionFallback = true;
            }

            // The game lives in an iframe with platform chrome around it. Without this,
            // clicking anything outside the canvas — the fullscreen button, a menu —
            // takes focus off the canvas and Unity stops rendering until you click back.
            if (!PlayerSettings.runInBackground)
            {
                Debug.Log("[Arsmi] Enabling Run In Background (the game must not freeze when the player clicks platform UI).");
                PlayerSettings.runInBackground = true;
            }

            if (!ArsmiTemplateInstaller.IsInstalled())
            {
                throw new BuildFailedException(
                    "[Arsmi] The ArsmiGames WebGL template is missing from Assets/WebGLTemplates, and could not be " +
                    "installed from the package. That template is what loads the platform SDK; without it the game " +
                    "cannot talk to Arsmi Games at all.\n" +
                    "Try Arsmi Games → Reinstall WebGL template, and check the Console for why the copy failed.");
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL) return;

            var output = report.summary.outputPath;
            var index = Path.Combine(output, "index.html");

            if (!File.Exists(index))
            {
                throw new BuildFailedException($"[Arsmi] No index.html in the build output ({output}).");
            }

            // The whole point. Verify against the file that was actually written, not
            // against the template we hoped was used.
            var html = File.ReadAllText(index);
            if (!html.Contains(SdkFile))
            {
                throw new BuildFailedException(
                    $"[Arsmi] The build's index.html does not load {SdkFile}.\n" +
                    "The game would load, look fine, and be unable to reach the platform — no saves, " +
                    "no leaderboards, and no error to tell you why.\n" +
                    "Set Player Settings → WebGL → Resolution and Presentation → WebGL Template to 'ArsmiGames' and build again.");
            }

            // The bundled copy is what makes the build work off the platform's origin —
            // on a static host the absolute /sdk/ path resolves against THAT host and
            // 404s. Without this file the game is mute everywhere except the platform,
            // which is the hardest version of this bug to spot.
            var bundled = Path.Combine(output, SdkFile);
            if (!File.Exists(bundled))
            {
                throw new BuildFailedException(
                    $"[Arsmi] {SdkFile} was not copied into the build.\n" +
                    "It must sit next to index.html in the template. Without it the game can only reach the " +
                    "platform when the platform itself is serving it, and sits there silently doing nothing on " +
                    "any other host — a static host, Cloudflare Pages, file://.\n" +
                    "Run Arsmi Games → Reinstall WebGL template and build again.");
            }

            VerifySdkIsCurrent(bundled);
            StampOrientation(index);

            Debug.Log($"[Arsmi] WebGL build verified: SDK {SdkVersion(File.ReadAllText(bundled))}, " +
                      $"{ChosenOrientation().ToString().ToLowerInvariant()}. → {output}");
        }

        /// <summary>
        /// The bundled SDK must be the package's SDK, byte for byte.
        ///
        /// "The file is there" was the old check, and a stale file passes it. That is not a
        /// hypothetical: a game shipped a gamehub-sdk.js four hundred lines shorter than the
        /// current one, from before the platform could ask a game what it implements. It
        /// answered the handshake, so it looked connected. It could not answer anything else,
        /// so every publish requirement failed, and the developer was sent looking for a bug
        /// in code that was working perfectly.
        ///
        /// An SDK is a protocol. A stale one does not error — it disagrees, silently. So the
        /// build refuses to produce one.
        /// </summary>
        private static void VerifySdkIsCurrent(string bundled)
        {
            var canonical = ArsmiTemplateInstaller.PackageFile(SdkFile);
            if (canonical == null)
            {
                // Cannot prove it either way. Say so rather than passing in silence — a check
                // that quietly does nothing is worse than no check, because it is trusted.
                Debug.LogWarning(
                    $"[Arsmi] Could not find {SdkFile} inside the package, so the build could not verify " +
                    "that the SDK it bundled is the current one. Reinstall com.arsmi.gamehub.");
                return;
            }

            if (SameContent(canonical, bundled)) return;

            throw new BuildFailedException(
                $"[Arsmi] The {SdkFile} in this build is not the one in the package.\n\n" +
                $"  build:   {SdkVersion(File.ReadAllText(bundled))}  ({bundled})\n" +
                $"  package: {SdkVersion(File.ReadAllText(canonical))}  ({canonical})\n\n" +
                "The SDK is the protocol the platform speaks. An out-of-date copy still answers the " +
                "handshake, so the game looks connected — it just cannot answer the questions the " +
                "platform asks before it will publish anything, and none of that errors.\n\n" +
                "This is almost always an edited or left-over copy in Assets/WebGLTemplates/ArsmiGames/. " +
                "Run Arsmi Games → Reinstall WebGL template and build again.");
        }

        /// <summary>Writes the chosen orientation into the built index.html.</summary>
        private static void StampOrientation(string index)
        {
            var want = ChosenOrientation().ToString().ToLowerInvariant();
            var html = File.ReadAllText(index);

            // The template ships data-orientation="landscape" as its default, so there is
            // always something to replace. If there is not, the project is using a template
            // that is not ours, and the SDK checks above would already have failed.
            var stamped = Regex.Replace(
                html,
                "data-orientation=\"[^\"]*\"",
                $"data-orientation=\"{want}\"",
                RegexOptions.IgnoreCase);

            if (stamped == html && !html.Contains($"data-orientation=\"{want}\""))
            {
                Debug.LogWarning(
                    "[Arsmi] index.html has no data-orientation attribute to stamp — the platform will fall " +
                    "back to the orientation on the game record. Reinstall the WebGL template to fix this.");
                return;
            }

            File.WriteAllText(index, stamped);
        }

        /// <summary>What the menu (or -arsmiOrientation) last asked for. Landscape if never asked.</summary>
        public static Orientation ChosenOrientation()
        {
            return EditorPrefs.GetString(OrientationKey, nameof(Orientation.Landscape)) == nameof(Orientation.Portrait)
                ? Orientation.Portrait
                : Orientation.Landscape;
        }

        /// <summary>The SDK's self-reported version, for error messages. "unknown" if absent.</summary>
        private static string SdkVersion(string js)
        {
            var found = Regex.Match(js, "var SDK_VERSION = \"([^\"]+)\"");
            // An SDK old enough to have no version constant is exactly the one worth naming.
            return found.Success ? $"v{found.Groups[1].Value}" : "an unversioned build (pre-1.0)";
        }

        private static bool SameContent(string a, string b)
        {
            var left = File.ReadAllBytes(a);
            var right = File.ReadAllBytes(b);
            if (left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i]) return false;
            }
            return true;
        }
    }

    public static class ArsmiBuild
    {
        private const string LastPathKey = "Arsmi.LastBuildPath";

        // %&b — Ctrl+Alt+B. (% = Ctrl, & = Alt, # = Shift.) It was Ctrl+Shift+B, which Unity
        // itself uses for Build Settings on some layouts, and which several IDEs take.
        [MenuItem("Arsmi Games/Build WebGL… %&b", priority = 0)]
        public static void BuildWebGL()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog("Arsmi Games",
                    "No scenes are enabled in Build Settings, so the build would be empty.", "OK");
                return;
            }

            // Asked before the folder picker, so the question is answered while you are still
            // thinking about the build rather than after you have committed to a location.
            //
            // Unity's dialog gives us two buttons and a cancel. The default (the "ok" button)
            // is whichever was chosen last, so building the same game repeatedly is one Enter.
            var last = ArsmiWebGLBuildProcessor.ChosenOrientation();
            var portraitWasLast = last == Orientation.Portrait;

            var answer = EditorUtility.DisplayDialogComplex(
                "Build WebGL",
                "Which way up is this game played?\n\n" +
                "It is written into index.html, and the platform sizes the frame around the game to match. " +
                "Getting it wrong does not break the build — the game just runs in a frame the wrong shape.\n\n" +
                $"Last build: {last.ToString().ToLowerInvariant()}.",
                portraitWasLast ? "Portrait" : "Landscape",   // ok      — repeat last
                "Cancel",                                      // cancel
                portraitWasLast ? "Landscape" : "Portrait");   // alt     — the other one

            if (answer == 1) return; // cancel

            var orientation = answer == 0
                ? last
                : (portraitWasLast ? Orientation.Landscape : Orientation.Portrait);

            EditorPrefs.SetString(ArsmiWebGLBuildProcessor.OrientationKey, orientation.ToString());

            var previous = EditorPrefs.GetString(LastPathKey, "");
            var suggestedName = string.IsNullOrEmpty(previous)
                ? SanitizeName(PlayerSettings.productName)
                : Path.GetFileName(previous);
            var startIn = string.IsNullOrEmpty(previous) ? "" : Path.GetDirectoryName(previous);

            var output = EditorUtility.SaveFolderPanel("Build WebGL to…", startIn ?? "", suggestedName);
            if (string.IsNullOrEmpty(output)) return;

            EditorPrefs.SetString(LastPathKey, output);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };

            // The pre/post processors above do the enforcing and the verifying; if either
            // is unhappy the build fails rather than quietly producing a mute game.
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                var megabytes = summary.totalSize / (1024f * 1024f);
                Debug.Log($"[Arsmi] Build succeeded — {megabytes:0.0} MB in {summary.totalTime.TotalSeconds:0} s.");
                EditorUtility.RevealInFinder(Path.Combine(output, "index.html"));
            }
            else
            {
                Debug.LogError($"[Arsmi] Build {summary.result}. See the errors above.");
            }
        }

        /// <summary>
        /// For CI: Unity.exe -quit -batchmode -executeMethod ArsmiGames.EditorTools.ArsmiBuild.BuildFromCommandLine
        /// -arsmiOutput &lt;folder&gt; [-arsmiOrientation portrait|landscape]
        /// </summary>
        public static void BuildFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            var output = "";
            var orientation = "";
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-arsmiOutput") output = args[i + 1];
                if (args[i] == "-arsmiOrientation") orientation = args[i + 1];
            }
            if (string.IsNullOrEmpty(output)) output = Path.Combine("Builds", "WebGL");

            // Batch mode has no one to ask, so it takes the flag — and when the flag is absent
            // it inherits whatever the last interactive build chose, which on a CI machine is
            // landscape (the default). An unrecognised value is a typo, and a typo that
            // silently builds the wrong shape is worth stopping for.
            if (!string.IsNullOrEmpty(orientation))
            {
                if (!Enum.TryParse<Orientation>(orientation, ignoreCase: true, out var parsed))
                {
                    Debug.LogError($"[Arsmi] -arsmiOrientation must be 'portrait' or 'landscape', not '{orientation}'.");
                    EditorApplication.Exit(1);
                    return;
                }
                EditorPrefs.SetString(ArsmiWebGLBuildProcessor.OrientationKey, parsed.ToString());
            }

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
            });

            // Batch mode ignores thrown exceptions for the exit code unless we say so.
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        private static string SanitizeName(string name)
        {
            var clean = new string((name ?? "game").Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
            return clean.Trim('-');
        }
    }
}
