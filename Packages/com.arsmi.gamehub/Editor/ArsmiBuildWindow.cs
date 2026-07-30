using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArsmiGames.EditorTools
{
    /// <summary>
    /// The window that opens before a WebGL build: which scenes go in, and what is wrong with the
    /// settings, in one place.
    ///
    /// It exists because the old flow asked one question (orientation) in a modal dialog and took
    /// the scene list silently from Build Settings — a window most people never open. A build with
    /// the wrong scene at index 0 succeeds, uploads, and starts on the wrong screen, and nothing
    /// anywhere says why. Scene 0 is the one thing about a WebGL build that cannot be fixed after
    /// the fact, so it is the one thing worth showing before the folder picker.
    ///
    /// The checks below duplicate what ArsmiWebGLBuildProcessor enforces at build time on purpose.
    /// The processor is the authority — it runs for Unity's own Build button too, and cannot be
    /// skipped — but it runs after you have committed to a build and chosen a folder. Showing the
    /// same facts here means they are answered while you are still deciding, not reported in the
    /// Console afterwards.
    /// </summary>
    public sealed class ArsmiBuildWindow : EditorWindow
    {
        private Vector2 scroll;
        private bool checksExpanded = true;

        /// <summary>Set once per window so the "turned it on for you" notice does not flicker.</summary>
        private bool fallbackWasOff;

        public static void Open()
        {
            var window = GetWindow<ArsmiBuildWindow>(utility: false, title: "Arsmi Build", focus: true);
            window.minSize = new Vector2(460f, 420f);
            window.Prepare();
            window.Show();
        }

        /// <summary>
        /// Fix what the platform requires outright, and remember what had to be fixed.
        ///
        /// Decompression fallback is not a preference. The platform serves a build as static files
        /// and sets no Content-Encoding, so a compressed build without the fallback decompressor
        /// does not load at all — a blank canvas and a console error about an invalid header. There
        /// is no case where a game shipping here wants it off, which is why this turns it on rather
        /// than asking. The notice stays on screen so the change is never silent.
        /// </summary>
        private void Prepare()
        {
            fallbackWasOff = !PlayerSettings.WebGL.decompressionFallback;
            if (!fallbackWasOff) return;

            PlayerSettings.WebGL.decompressionFallback = true;
            Debug.Log("[Arsmi] WebGL decompression fallback was off — turned on. The platform serves " +
                      "builds as static files, so a compressed build cannot load without it.");
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawScenes();
            EditorGUILayout.Space(8f);
            DrawChecks();
            EditorGUILayout.Space(8f);
            DrawOrientation();
            EditorGUILayout.Space(12f);
            DrawBuildButton();

            EditorGUILayout.EndScrollView();
        }

        // --- scenes ---------------------------------------------------------------------------

        private void DrawScenes()
        {
            EditorGUILayout.LabelField("Scenes in this build", EditorStyles.boldLabel);

            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Build Settings has no scenes at all. A build would produce an empty player.",
                    MessageType.Error);
            }

            var enabledCount = scenes.Count(s => s.enabled);
            var firstEnabled = scenes.FindIndex(s => s.enabled);

            for (var i = 0; i < scenes.Count; i++)
            {
                var scene = scenes[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    var enabled = EditorGUILayout.Toggle(scene.enabled, GUILayout.Width(18f));
                    if (enabled != scene.enabled)
                    {
                        scenes[i] = new EditorBuildSettingsScene(scene.path, enabled);
                        Commit(scenes);
                        return; // the list we are iterating is now stale
                    }

                    // Named rather than numbered: Unity's own list numbers every row including the
                    // disabled ones, so the row labelled 0 is not always the scene that loads first.
                    // That mismatch is exactly what puts the wrong screen at startup.
                    var isFirst = i == firstEnabled;
                    var label = Path.GetFileNameWithoutExtension(scene.path);
                    var style = isFirst ? EditorStyles.boldLabel : EditorStyles.label;
                    EditorGUILayout.LabelField(new GUIContent(label, scene.path), style);

                    if (isFirst)
                    {
                        EditorGUILayout.LabelField("loads first", EditorStyles.miniLabel, GUILayout.Width(66f));
                    }
                    else if (scene.enabled)
                    {
                        if (GUILayout.Button(new GUIContent("Make first", "Move this scene above the others"), EditorStyles.miniButton, GUILayout.Width(72f)))
                        {
                            scenes.RemoveAt(i);
                            scenes.Insert(0, scene);
                            Commit(scenes);
                            return;
                        }
                    }
                    else
                    {
                        GUILayout.Space(76f);
                    }

                    using (new EditorGUI.DisabledScope(i == 0))
                    {
                        if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(22f)))
                        {
                            scenes.RemoveAt(i);
                            scenes.Insert(i - 1, scene);
                            Commit(scenes);
                            return;
                        }
                    }

                    using (new EditorGUI.DisabledScope(i == scenes.Count - 1))
                    {
                        if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(22f)))
                        {
                            scenes.RemoveAt(i);
                            scenes.Insert(i + 1, scene);
                            Commit(scenes);
                            return;
                        }
                    }

                    if (GUILayout.Button(new GUIContent("−", "Remove from Build Settings"), EditorStyles.miniButtonRight, GUILayout.Width(22f)))
                    {
                        scenes.RemoveAt(i);
                        Commit(scenes);
                        return;
                    }
                }

                // A row that points at a file that is no longer there builds into a hard failure
                // with a path and no explanation, so say it here where it can still be removed.
                if (!File.Exists(scene.path))
                {
                    EditorGUILayout.HelpBox($"Missing on disk: {scene.path}", MessageType.Error);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add open scene", GUILayout.Width(120f))) AddOpenScene(scenes);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"{enabledCount} of {scenes.Count} enabled", EditorStyles.miniLabel, GUILayout.Width(120f));
            }

            if (scenes.Count > 0 && enabledCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "Every scene is unticked, so the build would be empty. Tick at least one.",
                    MessageType.Error);
            }
        }

        private static void Commit(List<EditorBuildSettingsScene> scenes)
        {
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void AddOpenScene(List<EditorBuildSettingsScene> scenes)
        {
            var active = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path))
            {
                EditorUtility.DisplayDialog("Arsmi Games",
                    "The open scene has never been saved, so it has no path to add. Save it first.", "OK");
                return;
            }

            if (scenes.Any(s => s.path == active.path))
            {
                EditorUtility.DisplayDialog("Arsmi Games", $"{active.name} is already in the list.", "OK");
                return;
            }

            scenes.Add(new EditorBuildSettingsScene(active.path, true));
            Commit(scenes);
        }

        // --- pre-build checks -----------------------------------------------------------------

        private void DrawChecks()
        {
            checksExpanded = EditorGUILayout.Foldout(checksExpanded, "Settings the platform needs", true);
            if (!checksExpanded) return;

            if (fallbackWasOff)
            {
                EditorGUILayout.HelpBox(
                    "Decompression fallback was off — this window turned it on. Without it a compressed " +
                    "build does not load on the platform at all, because builds are served as static " +
                    "files with no Content-Encoding header.",
                    MessageType.Info);
            }

            Check(
                "Decompression fallback",
                PlayerSettings.WebGL.decompressionFallback,
                "On.",
                "Off — a compressed build will not load on the platform.",
                () => PlayerSettings.WebGL.decompressionFallback = true);

            Check(
                "WebGL template",
                PlayerSettings.WebGL.template == ArsmiWebGLBuildProcessor.Template,
                "ArsmiGames.",
                $"'{PlayerSettings.WebGL.template}' — the platform SDK is only loaded by the ArsmiGames template.",
                () => PlayerSettings.WebGL.template = ArsmiWebGLBuildProcessor.Template);

            Check(
                "Run in background",
                PlayerSettings.runInBackground,
                "On.",
                "Off — the game freezes whenever the player clicks platform UI outside the canvas.",
                () => PlayerSettings.runInBackground = true);

            // Not enforced at build time, unlike the three above: a Gzip or uncompressed build is
            // bigger but works, so this is advice rather than a requirement.
            var brotli = PlayerSettings.WebGL.compressionFormat == WebGLCompressionFormat.Brotli;
            Check(
                "Compression",
                brotli,
                "Brotli.",
                $"{PlayerSettings.WebGL.compressionFormat} — Brotli is typically 15-20% smaller, and the " +
                "fallback decompressor above makes it safe to serve statically.",
                () => PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli,
                warningOnly: true);
        }

        /// <summary>One check: a green line, or a red line with the button that fixes it.</summary>
        private static void Check(string label, bool ok, string okText, string badText, System.Action fix, bool warningOnly = false)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(ok ? "✔" : (warningOnly ? "!" : "✘")), GUILayout.Width(16f));
                EditorGUILayout.LabelField(label, GUILayout.Width(150f));
                EditorGUILayout.LabelField(ok ? okText : badText, EditorStyles.wordWrappedMiniLabel);
                if (!ok && GUILayout.Button("Fix", EditorStyles.miniButton, GUILayout.Width(40f))) fix();
            }
        }

        // --- orientation ----------------------------------------------------------------------

        private void DrawOrientation()
        {
            EditorGUILayout.LabelField("Orientation", EditorStyles.boldLabel);

            var current = ArsmiWebGLBuildProcessor.ChosenOrientation();
            var index = current == Orientation.Portrait ? 1 : 0;
            var next = GUILayout.Toolbar(index, new[] { "Landscape", "Portrait" });
            if (next != index)
            {
                var chosen = next == 1 ? Orientation.Portrait : Orientation.Landscape;
                EditorPrefs.SetString(ArsmiWebGLBuildProcessor.OrientationKey, chosen.ToString());
            }

            EditorGUILayout.LabelField(
                "Written into index.html; the platform sizes the frame around the game to match.",
                EditorStyles.wordWrappedMiniLabel);
        }

        // --- build ----------------------------------------------------------------------------

        private void DrawBuildButton()
        {
            var enabled = EditorBuildSettings.scenes.Any(s => s.enabled);
            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (GUILayout.Button("Build WebGL…", GUILayout.Height(32f)))
                {
                    // Closed first: the build blocks the Editor for minutes, and a window left
                    // painting over it repaints with a half-built project's settings.
                    Close();
                    ArsmiBuild.RunInteractiveBuild();
                }
            }

            if (!enabled)
            {
                EditorGUILayout.LabelField("Tick at least one scene to build.", EditorStyles.miniLabel);
            }
        }
    }
}
