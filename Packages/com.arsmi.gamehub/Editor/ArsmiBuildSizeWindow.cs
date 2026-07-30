using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ArsmiGames.EditorTools
{
    /// <summary>
    /// Where the build's size went, and what can be done about it.
    ///
    /// Three questions, in the order they are worth asking:
    ///
    ///   1. What did the last build actually spend its bytes on? (measured, from the BuildReport)
    ///   2. What is heavy in the project? (file size on disk — a proxy, and a lying one, but it
    ///      works before you have ever built)
    ///   3. What is in the project that no scene in the build reaches?
    ///
    /// The measured answer comes first deliberately. Sorting a project by file size is the obvious
    /// thing to do and it is wrong often enough to waste an afternoon: a 30 MB PSD can pack to
    /// 200 KB, while a 900 KB PNG imported uncompressed can pack to 16 MB. Only the build knows.
    /// </summary>
    public sealed class ArsmiBuildSizeWindow : EditorWindow
    {
        private enum Tab { LastBuild, Heavy, Unreferenced, Shrink }

        private Tab tab = Tab.LastBuild;
        private Vector2 scroll;

        private ArsmiBuildSizeData lastBuild;

        private List<ArsmiSizeEntry> heavy;
        private List<string> unreferenced;
        private bool includeScriptsInScan;
        private long unreferencedBytes;

        [MenuItem("Arsmi Games/Build Size Report", priority = 41)]
        public static void Open()
        {
            var window = GetWindow<ArsmiBuildSizeWindow>(utility: false, title: "Arsmi Build Size", focus: true);
            window.minSize = new Vector2(560f, 460f);
            window.lastBuild = ArsmiBuildSizeRecord.Load();
            window.Show();
        }

        private void OnGUI()
        {
            tab = (Tab)GUILayout.Toolbar((int)tab, new[] { "Last build", "Heavy files", "Unreferenced", "Shrink" });
            EditorGUILayout.Space(6f);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            switch (tab)
            {
                case Tab.LastBuild: DrawLastBuild(); break;
                case Tab.Heavy: DrawHeavy(); break;
                case Tab.Unreferenced: DrawUnreferenced(); break;
                case Tab.Shrink: DrawShrink(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        // --- 1. measured --------------------------------------------------------------------

        private void DrawLastBuild()
        {
            if (lastBuild == null) lastBuild = ArsmiBuildSizeRecord.Load();

            if (lastBuild == null)
            {
                EditorGUILayout.HelpBox(
                    "No build has been measured yet.\n\n" +
                    "Build once — Arsmi Games → Build WebGL…, or Unity's own Build button, either works — " +
                    "and the sizes below are recorded automatically. Until then the other tabs still work, " +
                    "but they measure files on disk rather than what a build packs.",
                    MessageType.Info);
                if (GUILayout.Button("Refresh", GUILayout.Width(80f))) lastBuild = ArsmiBuildSizeRecord.Load();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Total build: {ArsmiBuildSizeRecord.Bytes(lastBuild.totalBytes)}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(60f))) lastBuild = ArsmiBuildSizeRecord.Load();
            }
            EditorGUILayout.LabelField($"{lastBuild.builtAtUtc} UTC  ·  {lastBuild.assetCount} assets  ·  {lastBuild.outputPath}", EditorStyles.miniLabel);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("By category", EditorStyles.boldLabel);
            var largestCategory = lastBuild.categories.Count > 0 ? Math.Max(1L, lastBuild.categories[0].bytes) : 1L;
            foreach (var category in lastBuild.categories)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(category.category, GUILayout.Width(150f));
                    // A bar, because the ratios are the point — one category is usually most of the
                    // build and a column of numbers hides that.
                    var rect = GUILayoutUtility.GetRect(60f, 14f, GUILayout.ExpandWidth(true));
                    EditorGUI.ProgressBar(rect, category.bytes / (float)largestCategory, ArsmiBuildSizeRecord.Bytes(category.bytes));
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Biggest assets in the build", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Packed size — what this asset cost the player, after import settings and compression.",
                EditorStyles.wordWrappedMiniLabel);

            foreach (var entry in lastBuild.biggest.Take(60))
            {
                DrawAssetRow(entry.path, entry.bytes);
            }
        }

        // --- 2. on disk ---------------------------------------------------------------------

        private void DrawHeavy()
        {
            EditorGUILayout.HelpBox(
                "File size on disk, not build size. Useful before your first build, and for finding source " +
                "art that was never meant to ship — but an asset here may pack to almost nothing, and an " +
                "asset that is small here may pack to far more. The Last build tab is the truth.",
                MessageType.Info);

            if (GUILayout.Button("Scan project", GUILayout.Width(110f))) ScanHeavy();

            if (heavy == null) return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"{heavy.Count} largest files", EditorStyles.boldLabel);
            foreach (var entry in heavy) DrawAssetRow(entry.path, entry.bytes);
        }

        private void ScanHeavy()
        {
            var results = new List<ArsmiSizeEntry>();
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;

                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists) continue;
                    results.Add(new ArsmiSizeEntry { path = path, bytes = info.Length, category = ArsmiBuildSizeRecord.CategoryOf(path) });
                }
                catch
                {
                    // A file the OS will not stat is not worth failing a scan over.
                }
            }

            heavy = results.OrderByDescending(entry => entry.bytes).Take(60).ToList();
        }

        // --- 3. unreferenced ----------------------------------------------------------------

        private void DrawUnreferenced()
        {
            EditorGUILayout.HelpBox(
                "Assets that nothing in the build reaches: not used by a scene ticked in Build Settings, not " +
                "in a Resources folder, and not a preloaded asset.\n\n" +
                "Read this as a list to review, never as a list to delete blindly. It cannot see " +
                "Resources.Load called with a name built at runtime, Addressables, asset bundles, or anything " +
                "a native plugin opens. Scenes you have not ticked count as unreferenced, which is correct for " +
                "build size and wrong if you simply forgot to tick one.",
                MessageType.Warning);

            includeScriptsInScan = EditorGUILayout.ToggleLeft(
                new GUIContent("Include scripts",
                    "Off by default: scripts compile into the build whether or not a scene references them, " +
                    "so an 'unreferenced' script is usually a false positive."),
                includeScriptsInScan);

            if (GUILayout.Button("Scan project", GUILayout.Width(110f))) ScanUnreferenced();

            if (unreferenced == null) return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                $"{unreferenced.Count} unreferenced  ·  {ArsmiBuildSizeRecord.Bytes(unreferencedBytes)} on disk",
                EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select all in Project", GUILayout.Width(160f)))
                {
                    Selection.objects = unreferenced
                        .Select(AssetDatabase.LoadMainAssetAtPath)
                        .Where(asset => asset != null)
                        .ToArray();
                }

                if (GUILayout.Button("Delete all…", GUILayout.Width(90f))) DeleteUnreferenced();
            }

            foreach (var path in unreferenced.Take(200))
            {
                long size = 0;
                try { size = new FileInfo(path).Length; } catch { /* reported as 0 */ }
                DrawAssetRow(path, size);
            }

            if (unreferenced.Count > 200)
            {
                EditorGUILayout.LabelField($"…and {unreferenced.Count - 200} more. Select all to see them in the Project window.", EditorStyles.miniLabel);
            }
        }

        private void ScanUnreferenced()
        {
            // The roots: everything the player can reach without a scene reference.
            var roots = new List<string>();
            roots.AddRange(EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path));

            var all = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                .Where(path => !AssetDatabase.IsValidFolder(path))
                .ToArray();

            // A Resources folder ships whole, referenced or not — that is what it is for.
            roots.AddRange(all.Where(IsUnderResources));

            foreach (var preloaded in PlayerSettings.GetPreloadedAssets())
            {
                if (preloaded == null) continue;
                var path = AssetDatabase.GetAssetPath(preloaded);
                if (!string.IsNullOrEmpty(path)) roots.Add(path);
            }

            var used = new HashSet<string>(AssetDatabase.GetDependencies(roots.Distinct().ToArray(), recursive: true),
                StringComparer.OrdinalIgnoreCase);

            var results = new List<string>();
            long total = 0;
            foreach (var path in all)
            {
                if (used.Contains(path)) continue;
                if (IsUnderResources(path)) continue;
                if (IsEditorOnly(path)) continue;
                if (!includeScriptsInScan && IsCode(path)) continue;

                results.Add(path);
                try { total += new FileInfo(path).Length; } catch { /* counted as 0 */ }
            }

            unreferenced = results.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
            unreferencedBytes = total;
        }

        private static bool IsUnderResources(string path) =>
            path.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Editor-only assets never enter a player, so calling them unreferenced is noise.
        /// </summary>
        /// <remarks>
        /// WebGLTemplates is included because the template is not referenced by any scene — Unity
        /// copies it in by name from Player Settings. Listing it would invite deleting the one
        /// directory that makes the game able to talk to the platform at all.
        /// </remarks>
        private static bool IsEditorOnly(string path) =>
            path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.StartsWith("Assets/Editor/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Assets/WebGLTemplates", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase);

        private static bool IsCode(string path) =>
            path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

        private void DeleteUnreferenced()
        {
            if (unreferenced == null || unreferenced.Count == 0) return;

            // Two sentences and a count, because this is the one irreversible thing in the window
            // and the list it works from is explicitly a best guess.
            var confirmed = EditorUtility.DisplayDialog(
                "Delete unreferenced assets",
                $"Permanently delete {unreferenced.Count} files ({ArsmiBuildSizeRecord.Bytes(unreferencedBytes)})?\n\n" +
                "This list is a guess. It cannot see Addressables, asset bundles, or Resources.Load with a " +
                "name built at runtime, and it treats scenes you have not ticked in Build Settings as unused.\n\n" +
                "If this project is not committed to version control, there is no way back.",
                "Delete", "Cancel");

            if (!confirmed) return;

            var failed = new List<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in unreferenced)
                {
                    if (!AssetDatabase.DeleteAsset(path)) failed.Add(path);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[Arsmi] Deleted {unreferenced.Count - failed.Count} unreferenced asset(s).");
            foreach (var path in failed) Debug.LogWarning($"[Arsmi] Could not delete {path}.");

            unreferenced = null;
            unreferencedBytes = 0;
        }

        // --- 4. shrink ----------------------------------------------------------------------

        private void DrawShrink()
        {
            EditorGUILayout.LabelField("Player settings", EditorStyles.boldLabel);

            Setting(
                "Strip engine code",
                PlayerSettings.stripEngineCode,
                "On — unused engine modules are left out.",
                "Off. This is usually the single largest saving available, often several MB.",
                () => PlayerSettings.stripEngineCode = true);

            Setting(
                "Brotli compression",
                PlayerSettings.WebGL.compressionFormat == WebGLCompressionFormat.Brotli,
                "On.",
                $"{PlayerSettings.WebGL.compressionFormat} — Brotli is typically 15-20% smaller than Gzip.",
                () => PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                $"Exception support: {PlayerSettings.WebGL.exceptionSupport}. 'None' is the smallest and the " +
                "fastest, but a crash in the wild then gives you no stack trace at all — a deliberate trade, " +
                "so this window will not make it for you.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Bulk import settings", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These act on what is selected in the Project window right now, so you choose the scope. " +
                "Import settings are stored in .meta files — if those are in version control, every change " +
                "here is revertible.",
                MessageType.Info);

            var selected = Selection.objects
                .Select(AssetDatabase.GetAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct()
                .ToArray();

            EditorGUILayout.LabelField($"{selected.Length} asset(s) selected", EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(selected.Length == 0))
            {
                if (GUILayout.Button("Textures: cap at 1024 and enable crunch")) CapTextures(selected, 1024);
                if (GUILayout.Button("Textures: cap at 512 and enable crunch")) CapTextures(selected, 512);
                if (GUILayout.Button("Audio: compress, and stream anything over 30 s")) CompressAudio(selected);
            }
        }

        private static void Setting(string label, bool ok, string okText, string badText, Action fix)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(ok ? "✔" : "!", GUILayout.Width(16f));
                EditorGUILayout.LabelField(label, GUILayout.Width(140f));
                EditorGUILayout.LabelField(ok ? okText : badText, EditorStyles.wordWrappedMiniLabel);
                if (!ok && GUILayout.Button("Fix", EditorStyles.miniButton, GUILayout.Width(40f))) fix();
            }
        }

        private static void CapTextures(IEnumerable<string> paths, int maxSize)
        {
            var changed = 0;
            foreach (var path in paths)
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                // Only ever downward. Raising a texture that was deliberately capped lower would be
                // a size increase from a button labelled as a saving.
                if (importer.maxTextureSize <= maxSize && importer.crunchedCompression) continue;

                if (importer.maxTextureSize > maxSize) importer.maxTextureSize = maxSize;
                importer.crunchedCompression = true;
                importer.compressionQuality = 50;
                importer.SaveAndReimport();
                changed++;
            }

            Debug.Log($"[Arsmi] Capped {changed} texture(s) at {maxSize} px with crunch compression.");
        }

        private static void CompressAudio(IEnumerable<string> paths)
        {
            var changed = 0;
            foreach (var path in paths)
            {
                if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) continue;

                var settings = importer.defaultSampleSettings;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;

                // Long clips stream rather than sit decompressed in memory. Loaded from the clip
                // itself, because "long" is about the audio, not the file size on disk.
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                settings.loadType = clip != null && clip.length > 30f
                    ? AudioClipLoadType.Streaming
                    : AudioClipLoadType.CompressedInMemory;

                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
                changed++;
            }

            Debug.Log($"[Arsmi] Re-imported {changed} audio clip(s) as Vorbis.");
        }

        // --- shared -------------------------------------------------------------------------

        private static void DrawAssetRow(string path, long bytes)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(ArsmiBuildSizeRecord.Bytes(bytes), GUILayout.Width(70f));
                EditorGUILayout.LabelField(new GUIContent(Path.GetFileName(path), path), GUILayout.MinWidth(120f));
                if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(52f)))
                {
                    var asset = AssetDatabase.LoadMainAssetAtPath(path);
                    if (asset != null)
                    {
                        Selection.activeObject = asset;
                        EditorGUIUtility.PingObject(asset);
                    }
                    else
                    {
                        // Recorded by a build, deleted since. Saying so beats a button that
                        // silently does nothing.
                        Debug.LogWarning($"[Arsmi] {path} is no longer in the project.");
                    }
                }
            }
        }
    }
}
