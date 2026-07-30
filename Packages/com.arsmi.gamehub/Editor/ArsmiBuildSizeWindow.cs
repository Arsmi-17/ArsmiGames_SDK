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

        /// <summary>Every asset's size on disk. Shared by the Heavy tab and Shrink's quick-select.</summary>
        private List<ArsmiSizeEntry> diskScan;

        private List<Unreferenced> unreferenced;
        private bool includeScriptsInScan;
        private long unreferencedBytes;

        /// <summary>Bytes above which the Shrink tab's quick-select calls an asset heavy.</summary>
        private int heavyThresholdKb = 512;

        /// <summary>
        /// An asset the build does not reach, and the scenes — if any — that do.
        /// </summary>
        /// <remarks>
        /// The scene list is the difference between "nothing uses this" and "a scene you have not
        /// ticked uses this", and those two want opposite actions. Without it the tab reads as the
        /// first and is sometimes the second, which is the one way a Move or Delete button here can
        /// cost someone real work.
        /// </remarks>
        private sealed class Unreferenced
        {
            public string path;
            public long bytes;
            public List<string> usedByScenes = new List<string>();
        }

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
            EnsureDiskScan();
            heavy = diskScan.OrderByDescending(entry => entry.bytes).Take(60).ToList();
        }

        /// <summary>
        /// Every asset with its size on disk, measured once and kept.
        /// </summary>
        /// <remarks>
        /// Full, not the top 60 the Heavy tab shows: the Shrink tab selects everything over a
        /// threshold, and a list truncated for display would silently cap what a Select button can
        /// reach — the sort of limit that looks like the button not working.
        /// </remarks>
        private void EnsureDiskScan()
        {
            if (diskScan != null) return;

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

            diskScan = results;
        }

        private static List<ArsmiSizeEntry> HeavierThan(List<ArsmiSizeEntry> source, long threshold, string category)
        {
            return source
                .Where(entry => entry.bytes >= threshold)
                .Where(entry => category == null || string.Equals(entry.category, category, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.bytes)
                .ToList();
        }

        /// <summary>
        /// A select button that states its own effect: how many, and how much.
        /// </summary>
        /// <remarks>
        /// Disabled at zero rather than hidden, so the answer to "are there any heavy textures?" is
        /// the button itself rather than a control that vanished.
        /// </remarks>
        private static void SelectButton(string label, List<ArsmiSizeEntry> entries)
        {
            var total = entries.Sum(entry => entry.bytes);
            using (new EditorGUI.DisabledScope(entries.Count == 0))
            {
                if (GUILayout.Button($"{label} — {entries.Count}, {ArsmiBuildSizeRecord.Bytes(total)}"))
                {
                    Selection.objects = entries
                        .Select(entry => AssetDatabase.LoadMainAssetAtPath(entry.path))
                        .Where(asset => asset != null)
                        .ToArray();
                }
            }
        }

        private static void Ping(string path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null) { Debug.LogWarning($"[Arsmi] {path} is no longer in the project."); return; }
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
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

            var orphans = unreferenced.Count(entry => entry.usedByScenes.Count == 0);
            var usedElsewhere = unreferenced.Count - orphans;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                $"{unreferenced.Count} unreferenced  ·  {ArsmiBuildSizeRecord.Bytes(unreferencedBytes)} on disk",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"{orphans} used by no scene at all  ·  {usedElsewhere} used only by scenes that are not in the build",
                EditorStyles.miniLabel);

            if (usedElsewhere > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{usedElsewhere} of these are used by a scene in the project that is not ticked in Build " +
                    "Settings. They are dead weight in the build and live assets in the Editor — check the scene " +
                    "named beside each one before moving or deleting it.",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select all", GUILayout.Width(80f))) SelectUnreferenced(all: true);
                if (GUILayout.Button(new GUIContent("Select orphans", "Only the ones no scene uses"), GUILayout.Width(110f)))
                {
                    SelectUnreferenced(all: false);
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Move to Archive…", "Move out of Assets, keeping the folder structure"), GUILayout.Width(130f)))
                {
                    ArchiveUnreferenced();
                }
                if (GUILayout.Button("Delete all…", GUILayout.Width(90f))) DeleteUnreferenced();
            }

            foreach (var entry in unreferenced.Take(200))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(ArsmiBuildSizeRecord.Bytes(entry.bytes), GUILayout.Width(70f));
                    EditorGUILayout.LabelField(new GUIContent(Path.GetFileName(entry.path), entry.path), GUILayout.MinWidth(110f));

                    // The scene, where there is one. This is the column that decides whether a row is
                    // safe to act on, so it sits beside the name rather than in a tooltip.
                    var scenes = entry.usedByScenes.Count == 0
                        ? "no scene"
                        : string.Join(", ", entry.usedByScenes.Take(3)) + (entry.usedByScenes.Count > 3 ? $" +{entry.usedByScenes.Count - 3}" : "");
                    var style = entry.usedByScenes.Count == 0 ? EditorStyles.miniLabel : EditorStyles.whiteMiniLabel;
                    EditorGUILayout.LabelField(new GUIContent(scenes, string.Join("\n", entry.usedByScenes)), style, GUILayout.Width(190f));

                    if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(52f))) Ping(entry.path);
                }
            }

            if (unreferenced.Count > 200)
            {
                EditorGUILayout.LabelField($"…and {unreferenced.Count - 200} more. Select all to see them in the Project window.", EditorStyles.miniLabel);
            }
        }

        private void SelectUnreferenced(bool all)
        {
            Selection.objects = unreferenced
                .Where(entry => all || entry.usedByScenes.Count == 0)
                .Select(entry => AssetDatabase.LoadMainAssetAtPath(entry.path))
                .Where(asset => asset != null)
                .ToArray();
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

            var results = new List<Unreferenced>();
            long total = 0;
            foreach (var path in all)
            {
                if (used.Contains(path)) continue;
                if (IsUnderResources(path)) continue;
                if (IsEditorOnly(path)) continue;
                if (!includeScriptsInScan && IsCode(path)) continue;

                var entry = new Unreferenced { path = path };
                try { entry.bytes = new FileInfo(path).Length; } catch { /* counted as 0 */ }
                total += entry.bytes;
                results.Add(entry);
            }

            AttributeToScenes(results);

            unreferenced = results.OrderBy(entry => entry.path, StringComparer.OrdinalIgnoreCase).ToList();
            unreferencedBytes = total;
        }

        /// <summary>
        /// For each unreferenced asset, which scenes in the PROJECT use it — not just the ones in
        /// the build.
        /// </summary>
        /// <remarks>
        /// The roots of the scan above are deliberately the enabled build scenes, because that is
        /// what decides build size. But it means a scene you have merely not ticked contributes
        /// nothing, and every asset only it uses is reported as unreferenced. That reading is
        /// correct for size and wrong for "is this safe to remove", and the two are one button
        /// apart in this tab.
        ///
        /// Walked per scene rather than in one GetDependencies call because the answer needed is
        /// which scene, not whether any.
        /// </remarks>
        private static void AttributeToScenes(List<Unreferenced> candidates)
        {
            if (candidates.Count == 0) return;

            var byPath = candidates.ToDictionary(entry => entry.path, StringComparer.OrdinalIgnoreCase);
            var inBuild = new HashSet<string>(
                EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path),
                StringComparer.OrdinalIgnoreCase);

            var scenes = AssetDatabase.FindAssets("t:Scene")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                // A scene already in the build cannot be the explanation: everything it touches was
                // in the used set, so nothing it uses reached this list.
                .Where(path => !inBuild.Contains(path))
                .Distinct()
                .ToArray();

            try
            {
                for (var i = 0; i < scenes.Length; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Arsmi — scanning scenes",
                            Path.GetFileNameWithoutExtension(scenes[i]),
                            (i + 1) / (float)scenes.Length))
                    {
                        // Cancelled: the list is still correct about what the build does not reach,
                        // it just has fewer scene names filled in. Partial attribution is better
                        // than none, and the count shown makes the gap visible.
                        break;
                    }

                    var name = Path.GetFileNameWithoutExtension(scenes[i]);
                    foreach (var dependency in AssetDatabase.GetDependencies(scenes[i], recursive: true))
                    {
                        if (byPath.TryGetValue(dependency, out var entry) && !entry.usedByScenes.Contains(name))
                        {
                            entry.usedByScenes.Add(name);
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
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

        /// <summary>Where archived assets go: a sibling of Assets, not a folder inside it.</summary>
        private static string ArchiveRoot =>
            Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? ".", "Archive");

        /// <summary>
        /// Move the list out of Assets, keeping the folder structure.
        ///
        /// Assets/Graphics/Art/cat.png becomes Archive/Graphics/Art/cat.png, so what comes back is
        /// recognisable and can be put back by hand if this window is ever gone.
        ///
        /// Out of Assets rather than into Assets/Archive, which is the tempting version and does
        /// almost nothing: inside Assets an asset is still imported on every project load, still
        /// costs Library space, and still ships if anything reaches it — a Resources folder moved
        /// wholesale would go right on shipping from its new home.
        ///
        /// The .meta moves with the file, and that is the point. The GUID lives in the .meta, so a
        /// reference is not broken by the move, only unresolvable while the file is away — put the
        /// pair back and every reference resolves again. That is what makes this the safe button
        /// and Delete the last resort, given the list is explicitly a guess.
        /// </summary>
        private void ArchiveUnreferenced()
        {
            if (unreferenced == null || unreferenced.Count == 0) return;

            var usedElsewhere = unreferenced.Count(entry => entry.usedByScenes.Count > 0);
            var warning = usedElsewhere > 0
                ? $"\n\n{usedElsewhere} of them are used by a scene that is not in the build. Those scenes will " +
                  "show missing references until you move the files back."
                : "";

            var confirmed = EditorUtility.DisplayDialog(
                "Move to Archive",
                $"Move {unreferenced.Count} files ({ArsmiBuildSizeRecord.Bytes(unreferencedBytes)}) out of Assets?\n\n" +
                $"They go to {ArchiveRoot}, keeping their folder structure, with their .meta files. " +
                "Because the .meta travels too, moving anything back restores every reference to it." + warning,
                "Move", "Cancel");

            if (!confirmed) return;

            var moved = 0;
            var failed = new List<string>();

            foreach (var entry in unreferenced)
            {
                try
                {
                    // Relative to Assets, so Assets/A/B.png lands at Archive/A/B.png.
                    var relative = entry.path.Substring("Assets/".Length);
                    var destination = UniquePath(Path.Combine(ArchiveRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ArchiveRoot);

                    File.Move(entry.path, destination);
                    // Same uniquified stem, or Unity would pair the .meta with nothing on the way back.
                    if (File.Exists(entry.path + ".meta")) File.Move(entry.path + ".meta", destination + ".meta");
                    moved++;
                }
                catch (Exception error)
                {
                    failed.Add($"{entry.path} — {error.Message}");
                }
            }

            AssetDatabase.Refresh();

            Debug.Log($"[Arsmi] Moved {moved} asset(s) to {ArchiveRoot}. Empty folders are left in Assets on " +
                      "purpose — deleting a folder is not something a size report should decide.");
            foreach (var failure in failed) Debug.LogWarning($"[Arsmi] Could not archive {failure}");

            unreferenced = null;
            unreferencedBytes = 0;
        }

        /// <summary>Never overwrite. A second archive run must not silently replace the first.</summary>
        private static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;

            var directory = Path.GetDirectoryName(path) ?? "";
            var stem = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            for (var i = 2; i < 1000; i++)
            {
                var candidate = Path.Combine(directory, $"{stem} ({i}){extension}");
                if (!File.Exists(candidate)) return candidate;
            }

            return Path.Combine(directory, $"{stem} ({Guid.NewGuid():N}){extension}");
        }

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
                "Move to Archive does the same job reversibly. If this project is not in version control, " +
                "there is no way back from this one.",
                "Delete", "Cancel");

            if (!confirmed) return;

            var failed = new List<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var entry in unreferenced)
                {
                    if (!AssetDatabase.DeleteAsset(entry.path)) failed.Add(entry.path);
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
            EditorGUILayout.LabelField("Find the heavy ones", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Selects them in the Project window, which is what the actions below act on.",
                EditorStyles.wordWrappedMiniLabel);

            // Packed size when a build has been measured, file size otherwise — and it says which,
            // because the two disagree often enough that a number with no provenance is a trap.
            var measured = lastBuild != null && lastBuild.biggest.Count > 0;
            var source = measured ? lastBuild.biggest : diskScan;

            if (source == null)
            {
                EditorGUILayout.HelpBox(
                    "No build measured and no file scan yet, so there is nothing to rank.",
                    MessageType.Info);
                // Explicit, not on first paint: this walks every asset path, and a tab that stalls
                // the Editor the moment you click it reads as a hang.
                if (GUILayout.Button("Scan project files", GUILayout.Width(140f))) EnsureDiskScan();
                return;
            }

            EditorGUILayout.LabelField(
                measured ? "Ranked by packed size from the last build." : "Ranked by file size on disk.",
                EditorStyles.miniLabel);

            heavyThresholdKb = EditorGUILayout.IntSlider("Heavier than (KB)", heavyThresholdKb, 64, 8192);

            // Counted before the buttons are drawn so each can say what it will select. A button that
            // turns out to select nothing is indistinguishable from a button that is broken.
            var threshold = heavyThresholdKb * 1024L;
            SelectButton("All heavy assets", HeavierThan(source, threshold, null));
            SelectButton("Heavy textures", HeavierThan(source, threshold, "Textures"));
            SelectButton("Heavy audio", HeavierThan(source, threshold, "Audio"));
            SelectButton("Heavy models", HeavierThan(source, threshold, "Models"));

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
                // Recorded by a build and deleted since is a real case, so Ping says so rather than
                // leaving a button that silently does nothing.
                if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(52f))) Ping(path);
            }
        }
    }
}
