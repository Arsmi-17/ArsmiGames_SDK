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
        /// An asset the build does not reach, and whatever still points at it.
        /// </summary>
        /// <remarks>
        /// The two lists are the difference between "nothing uses this" and "something you can name
        /// uses this", and those two want opposite actions. Without them the tab reads as the first
        /// and is sometimes the second, which is the one way a Move or Delete button here can cost
        /// someone real work — a material assigned to a prefab, or a shader a scene you have not
        /// ticked still draws with, is not spare.
        ///
        /// Anything with an entry in either list is held back from Move and Delete. See
        /// <see cref="IsHeld"/>.
        /// </remarks>
        private sealed class Unreferenced
        {
            public string path;
            public long bytes;
            public List<string> usedByScenes = new List<string>();

            /// <summary>Assets that still reference this one — a prefab, a material, a settings asset.</summary>
            public List<string> referencedBy = new List<string>();

            /// <summary>
            /// Still pointed at by something, so listed as build weight but never moved or deleted.
            /// </summary>
            /// <remarks>
            /// Held even when the thing pointing at it is itself on this list — a dead prefab and
            /// its dead material both being weight does not make it safe to take the material out
            /// from under the prefab in the same click. Archive the prefab, scan again, and the
            /// material comes back with nothing pointing at it and moves on the second pass. Two
            /// passes to clear a chain is the price of never breaking a live reference.
            /// </remarks>
            public bool IsHeld => usedByScenes.Count > 0 || referencedBy.Count > 0;

            /// <summary>The one thing to show in the row: why this is not moving.</summary>
            public string HeldBy =>
                usedByScenes.Count > 0 ? "scene: " + usedByScenes[0]
                : referencedBy.Count > 0 ? Path.GetFileName(referencedBy[0])
                : "nothing";
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
                "Assets that nothing in the build reaches: not used by a scene ticked in Build Settings, not in " +
                "a Resources folder, not preloaded, not named by Project Settings, not in an asset bundle or " +
                "the Addressables catalogue, and not included by a shader that ships.\n\n" +
                "Read this as a list to review, never as a list to delete blindly. It still cannot see " +
                "Resources.Load called with a name built at runtime, a Shader.Find by name, or anything a " +
                "native plugin opens.\n\n" +
                "Move and Delete only ever touch rows nothing points at. Anything still assigned to a scene, a " +
                "prefab, a material or a settings asset is listed as build weight and left where it is.",
                MessageType.Warning);

            includeScriptsInScan = EditorGUILayout.ToggleLeft(
                new GUIContent("Include scripts",
                    "Off by default: scripts compile into the build whether or not a scene references them, " +
                    "so an 'unreferenced' script is usually a false positive."),
                includeScriptsInScan);

            if (GUILayout.Button("Scan project", GUILayout.Width(110f))) ScanUnreferenced();

            if (unreferenced == null) return;

            var movable = Movable().ToList();
            var held = unreferenced.Count - movable.Count;
            var movableBytes = movable.Sum(entry => entry.bytes);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                $"{unreferenced.Count} unreferenced  ·  {ArsmiBuildSizeRecord.Bytes(unreferencedBytes)} on disk",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"{movable.Count} nothing points at ({ArsmiBuildSizeRecord.Bytes(movableBytes)})  ·  {held} held back",
                EditorStyles.miniLabel);

            if (held > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{held} of these are still assigned to something — a scene that is not ticked in Build " +
                    "Settings, a prefab, a material, a settings asset. They are weight in the build and live " +
                    "references in the Editor, so they are listed and never moved or deleted. The row names what " +
                    "holds each one.\n\n" +
                    "Archive what holds them, scan again, and they become movable.",
                    MessageType.Info);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select all", GUILayout.Width(80f))) SelectUnreferenced(all: true);
                if (GUILayout.Button(new GUIContent("Select movable", "Only the ones nothing points at"), GUILayout.Width(110f)))
                {
                    SelectUnreferenced(all: false);
                }
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(movable.Count == 0))
                {
                    if (GUILayout.Button(
                            new GUIContent($"Move {movable.Count} to Archive…", "Move out of Assets, keeping the folder structure"),
                            GUILayout.Width(150f)))
                    {
                        ArchiveUnreferenced();
                    }
                    if (GUILayout.Button($"Delete {movable.Count}…", GUILayout.Width(90f))) DeleteUnreferenced();
                }
            }

            foreach (var entry in unreferenced.Take(200))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(ArsmiBuildSizeRecord.Bytes(entry.bytes), GUILayout.Width(70f));
                    EditorGUILayout.LabelField(new GUIContent(Path.GetFileName(entry.path), entry.path), GUILayout.MinWidth(110f));

                    // What holds the row, where something does. This is the column that decides
                    // whether a row is acted on, so it sits beside the name rather than in a tooltip.
                    var others = entry.usedByScenes.Count + entry.referencedBy.Count - 1;
                    var reason = entry.IsHeld
                        ? entry.HeldBy + (others > 0 ? $" +{others}" : "")
                        : "nothing points at it";
                    var tooltip = entry.IsHeld
                        ? string.Join("\n", entry.usedByScenes.Select(scene => "scene: " + scene).Concat(entry.referencedBy))
                        : "Nothing in the project references this. Move and Delete will take it.";
                    var style = entry.IsHeld ? EditorStyles.whiteMiniLabel : EditorStyles.miniLabel;
                    EditorGUILayout.LabelField(new GUIContent(reason, tooltip), style, GUILayout.Width(190f));

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
            Selection.objects = (all ? unreferenced : Movable().ToList())
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

            roots.AddRange(ProjectSettingsRoots());
            roots.AddRange(AssetBundleRoots());
            // The Addressables catalogue names every addressable asset by GUID, so making the
            // catalogue a root makes the whole addressable tree reachable.
            roots.AddRange(all.Where(path => path.StartsWith("Assets/AddressableAssetsData/", StringComparison.OrdinalIgnoreCase)));

            var used = new HashSet<string>(AssetDatabase.GetDependencies(roots.Distinct().ToArray(), recursive: true),
                StringComparer.OrdinalIgnoreCase);

            AddShaderSources(used, all);

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
            AttributeToAssets(results, all);

            unreferenced = results.OrderBy(entry => entry.path, StringComparer.OrdinalIgnoreCase).ToList();
            unreferencedBytes = total;
        }

        /// <summary>
        /// Assets referenced by Project Settings rather than by anything in Assets.
        /// </summary>
        /// <remarks>
        /// This is the gap that makes the tab dangerous without it. A URP project keeps its render
        /// pipeline asset, its renderer, its global settings and its default volume profile in
        /// Assets, and the only thing pointing at them is Graphics Settings — no scene does, so a
        /// scan rooted at scenes calls every one of them unreferenced and offers to move the files
        /// the project cannot render a single frame without. Always-included shaders, preloaded
        /// shader variant collections, the per-quality-level pipeline overrides and the splash
        /// screen logo are all in the same position.
        ///
        /// Walked generically rather than field by field on purpose: every one of those is just an
        /// object reference on a settings object, so iterating the serialised properties catches
        /// the ones a hand-written list would miss and keeps catching them when Unity adds more.
        /// </remarks>
        private static IEnumerable<string> ProjectSettingsRoots()
        {
            var settings = new List<UnityEngine.Object>();

            // Qualified rather than a using, which would drop the whole of UnityEngine.Rendering
            // into a file that already has UnityEngine and UnityEditor in scope.
            try { settings.Add(UnityEngine.Rendering.GraphicsSettings.GetGraphicsSettings()); }
            catch { /* no graphics settings */ }
            try { settings.Add(QualitySettings.GetQualitySettings()); } catch { /* no quality settings */ }
            // Player Settings has no accessor of its own; it is a settings asset like the others.
            try { settings.AddRange(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")); }
            catch { /* not loadable on this Unity version */ }

            var paths = new List<string>();
            foreach (var asset in settings)
            {
                if (asset == null) continue;

                try
                {
                    var serialized = new SerializedObject(asset);
                    var property = serialized.GetIterator();
                    while (property.Next(enterChildren: true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;

                        var value = property.objectReferenceValue;
                        if (value == null) continue;

                        var path = AssetDatabase.GetAssetPath(value);
                        if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        {
                            paths.Add(path);
                        }
                    }
                }
                catch (Exception error)
                {
                    Debug.LogWarning($"[Arsmi] Could not read {asset.name} for references — assets it points at may be " +
                                     $"listed as unreferenced. {error.Message}");
                }
            }

            return paths;
        }

        /// <summary>
        /// Anything with an AssetBundle name ships in that bundle whether or not a scene wants it.
        /// </summary>
        private static IEnumerable<string> AssetBundleRoots() =>
            AssetDatabase.GetAllAssetBundleNames().SelectMany(AssetDatabase.GetAssetPathsFromAssetBundle);

        /// <summary>
        /// Follow the references shaders make in text, which the asset database does not model.
        /// </summary>
        /// <remarks>
        /// <c>#include "TMPro_Properties.cginc"</c> is a filename in a string, not a GUID, so
        /// <c>GetDependencies</c> on a live shader does not report the include and the include is
        /// reported unreferenced. Move it and the shader stops compiling — the project's text turns
        /// magenta and the cause is a file that is no longer in Assets. Shader Graph has the same
        /// problem from the other end: a Custom Function node stores its HLSL file as a bare GUID
        /// string inside the graph's JSON, which is likewise not a dependency.
        ///
        /// Both are resolved here against files already known to ship, then repeated until nothing
        /// new turns up, because an include may include another one.
        /// </remarks>
        private static void AddShaderSources(HashSet<string> used, string[] all)
        {
            var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in all.Where(IsShaderSource))
            {
                var name = Path.GetFileName(path);
                if (!byName.TryGetValue(name, out var matches)) byName[name] = matches = new List<string>();
                matches.Add(path);
            }

            var pending = new Queue<string>(used.Where(IsShaderSource));
            while (pending.Count > 0)
            {
                var path = pending.Dequeue();

                string text;
                try { text = File.ReadAllText(path); }
                catch { continue; }

                foreach (var found in ReferencedShaderSources(path, text, byName))
                {
                    if (!used.Add(found)) continue;
                    if (IsShaderSource(found)) pending.Enqueue(found);
                }
            }
        }

        /// <summary>Every shader source one shader source names, by path, by filename, or by GUID.</summary>
        private static IEnumerable<string> ReferencedShaderSources(
            string path, string text, Dictionary<string, List<string>> byName)
        {
            var folder = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";

            // Any quoted filename with a shader source extension. Covers #include and
            // #include_with_pragmas without having to model either.
            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(text, "\"([^\"\r\n]+\\.(?:cginc|hlsl|hlslinc|glslinc|shader|compute))\""))
            {
                var quoted = match.Groups[1].Value.Replace('\\', '/');

                // Relative to the including file first, which is what the compiler does.
                var relative = NormalisePath(folder + "/" + quoted);
                if (!string.IsNullOrEmpty(relative) && File.Exists(relative)) { yield return relative; continue; }

                // Then by name anywhere in the project. Two files with one name means both are
                // kept: over-keeping costs a few kilobytes, under-keeping costs a broken shader.
                if (byName.TryGetValue(Path.GetFileName(quoted), out var matches))
                {
                    foreach (var candidate in matches) yield return candidate;
                }
            }

            // Shader Graph's Custom Function nodes, which store the HLSL file as a bare GUID.
            if (!path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".shadersubgraph", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(text, "\\b[0-9a-f]{32}\\b"))
            {
                var found = AssetDatabase.GUIDToAssetPath(match.Value);
                if (!string.IsNullOrEmpty(found) && File.Exists(found)) yield return found;
            }
        }

        /// <summary>Collapse "A/B/../C" to "A/C" so a relative include resolves to a real file.</summary>
        private static string NormalisePath(string path)
        {
            var parts = new List<string>();
            foreach (var part in path.Split('/'))
            {
                if (part == "." || part.Length == 0) continue;
                if (part == ".." && parts.Count > 0) { parts.RemoveAt(parts.Count - 1); continue; }
                parts.Add(part);
            }

            return string.Join("/", parts);
        }

        private static bool IsShaderSource(string path) =>
            path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".cginc", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".hlslinc", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".glslinc", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".compute", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".shadersubgraph", StringComparison.OrdinalIgnoreCase);

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

        /// <summary>
        /// For each candidate, what in the project still points at it.
        /// </summary>
        /// <remarks>
        /// The scene pass above answers "does a scene draw this". This one answers the wider
        /// question the Move button actually needs: does <em>anything</em> — a prefab, a material,
        /// an animator, a ScriptableObject an Editor tool owns — still have this assigned. A
        /// material on a prefab that no scene has yet, a shader on that material, a VFX asset on a
        /// component: all of them are assigned work, and none of them can be told apart from
        /// genuine litter by the build's reachability alone.
        ///
        /// Direct dependencies only, per asset, because what is wanted is the name of the thing
        /// holding the reference — the recursive answer would name the far end of a chain rather
        /// than the link.
        /// </remarks>
        private static void AttributeToAssets(List<Unreferenced> candidates, string[] all)
        {
            if (candidates.Count == 0) return;

            var byPath = candidates.ToDictionary(entry => entry.path, StringComparer.OrdinalIgnoreCase);

            try
            {
                for (var i = 0; i < all.Length; i++)
                {
                    if (i % 64 == 0 && EditorUtility.DisplayCancelableProgressBar(
                            "Arsmi — checking what still points at these",
                            Path.GetFileName(all[i]),
                            (i + 1) / (float)all.Length))
                    {
                        // Cancelled mid-pass, so some referrers are unknown — and an unknown
                        // referrer is exactly what this pass exists to stop. Hold everything.
                        foreach (var entry in candidates)
                        {
                            if (!entry.referencedBy.Contains(Cancelled)) entry.referencedBy.Add(Cancelled);
                        }

                        break;
                    }

                    // An asset trivially depends on itself; that is not a referrer.
                    foreach (var dependency in AssetDatabase.GetDependencies(all[i], recursive: false))
                    {
                        if (string.Equals(dependency, all[i], StringComparison.OrdinalIgnoreCase)) continue;
                        if (byPath.TryGetValue(dependency, out var entry) && !entry.referencedBy.Contains(all[i]))
                        {
                            entry.referencedBy.Add(all[i]);
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>Stand-in referrer for a cancelled reference pass: unknown, so treated as held.</summary>
        private const string Cancelled = "(reference check cancelled)";

        /// <summary>
        /// What Move and Delete are allowed to touch: the entries nothing at all points at.
        /// </summary>
        private IEnumerable<Unreferenced> Movable() =>
            unreferenced == null ? Enumerable.Empty<Unreferenced>() : unreferenced.Where(entry => !entry.IsHeld);

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
            if (unreferenced == null) return;

            var moving = Movable().ToList();
            if (moving.Count == 0) return;

            var held = unreferenced.Count - moving.Count;
            var note = held > 0
                ? $"\n\n{held} more are listed but held back: something in the project still has them assigned. " +
                  "Nothing that is still pointed at is moved."
                : "";

            var confirmed = EditorUtility.DisplayDialog(
                "Move to Archive",
                $"Move {moving.Count} files ({ArsmiBuildSizeRecord.Bytes(moving.Sum(entry => entry.bytes))}) out of Assets?\n\n" +
                $"They go to {ArchiveRoot}, keeping their folder structure, with their .meta files. " +
                "Because the .meta travels too, moving anything back restores every reference to it." + note,
                "Move", "Cancel");

            if (!confirmed) return;

            var moved = 0;
            var failed = new List<string>();

            foreach (var entry in moving)
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
            if (unreferenced == null) return;

            var deleting = Movable().ToList();
            if (deleting.Count == 0) return;

            // Two sentences and a count, because this is the one irreversible thing in the window
            // and the list it works from is explicitly a best guess.
            var confirmed = EditorUtility.DisplayDialog(
                "Delete unreferenced assets",
                $"Permanently delete {deleting.Count} files ({ArsmiBuildSizeRecord.Bytes(deleting.Sum(entry => entry.bytes))})?\n\n" +
                "Nothing in the project points at any of them. That is still a guess about the build: it cannot " +
                "see Resources.Load with a name built at runtime, or an asset a script finds by name.\n\n" +
                "Move to Archive does the same job reversibly. If this project is not in version control, " +
                "there is no way back from this one.",
                "Delete", "Cancel");

            if (!confirmed) return;

            var failed = new List<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var entry in deleting)
                {
                    if (!AssetDatabase.DeleteAsset(entry.path)) failed.Add(entry.path);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[Arsmi] Deleted {deleting.Count - failed.Count} unreferenced asset(s).");
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
