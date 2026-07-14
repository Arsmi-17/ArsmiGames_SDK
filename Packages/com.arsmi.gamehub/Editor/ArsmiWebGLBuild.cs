using System;
using System.IO;
using System.Linq;
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
    public class ArsmiWebGLBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public const string Template = "PROJECT:ArsmiGames";
        public const string SdkFile = "gamehub-sdk.js";

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
                    "The game would load, look fine, and be unable to reach the platform — no saves, no achievements, " +
                    "no leaderboards, and no error to tell you why.\n" +
                    "Set Player Settings → WebGL → Resolution and Presentation → WebGL Template to 'ArsmiGames' and build again.");
            }

            // The bundled copy is what makes the build work off the platform's origin —
            // on a static host the absolute /sdk/ path resolves against THAT host and
            // 404s. Without this file the game is mute everywhere except the platform,
            // which is the hardest version of this bug to spot.
            if (!File.Exists(Path.Combine(output, SdkFile)))
            {
                throw new BuildFailedException(
                    $"[Arsmi] {SdkFile} was not copied into the build.\n" +
                    "It must sit next to index.html in the template. Without it the game can only reach the " +
                    "platform when the platform itself is serving it, and sits there silently doing nothing on " +
                    "any other host — a static host, Cloudflare Pages, file://.\n" +
                    "Run Arsmi Games → Reinstall WebGL template and build again.");
            }

            Debug.Log($"[Arsmi] WebGL build verified: SDK loads (platform copy, with the bundled one as fallback). → {output}");
        }
    }

    public static class ArsmiBuild
    {
        private const string LastPathKey = "Arsmi.LastBuildPath";

        [MenuItem("Arsmi Games/Build WebGL… %#b", priority = 0)]
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
        /// -arsmiOutput &lt;folder&gt;
        /// </summary>
        public static void BuildFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            var output = "";
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-arsmiOutput") output = args[i + 1];
            }
            if (string.IsNullOrEmpty(output)) output = Path.Combine("Builds", "WebGL");

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
