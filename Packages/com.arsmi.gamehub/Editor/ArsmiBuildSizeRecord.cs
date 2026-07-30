using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ArsmiGames.EditorTools
{
    /// <summary>One asset's contribution to the build, as the build actually packed it.</summary>
    [Serializable]
    public sealed class ArsmiSizeEntry
    {
        public string path;
        public string category;
        public long bytes;
    }

    /// <summary>What the last WebGL build weighed, and what made it that size.</summary>
    [Serializable]
    public sealed class ArsmiBuildSizeData
    {
        public string builtAtUtc = "";
        public string outputPath = "";
        public long totalBytes;
        public int assetCount;
        public List<ArsmiSizeEntry> biggest = new List<ArsmiSizeEntry>();
        public List<ArsmiSizeEntry> categories = new List<ArsmiSizeEntry>();
    }

    /// <summary>
    /// Keeps the one number that matters after a build: how many bytes each asset contributed to
    /// the player.
    ///
    /// It has to be captured during the build. BuildReport.packedAssets exists only inside the
    /// post-process callback — Unity discards it when the build ends, and nothing on disk records
    /// it. (Library/LastBuild.buildreport does, but reading it means copying a binary Unity object
    /// into Assets to load it through AssetDatabase, which leaves debris in the user's project.)
    /// So this writes a small JSON summary instead, from the callback that already runs.
    ///
    /// Packed size is not file size on disk, and the difference is the whole point of measuring. A
    /// 30 MB PSD can pack to 200 KB; a 900 KB PNG imported uncompressed can pack to 16 MB. Sorting
    /// a project by file size finds the former and misses the latter, which is why the report shows
    /// both and says which is which.
    /// </summary>
    public static class ArsmiBuildSizeRecord
    {
        /// <summary>How many assets to keep. Enough to find the problem, small enough to stay a
        /// file you can open and read.</summary>
        private const int KeepBiggest = 400;

        public static string RecordPath =>
            Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? ".", "Library", "arsmi-build-size.json");

        public static void Capture(BuildReport report)
        {
            try
            {
                var perAsset = new Dictionary<string, long>();

                foreach (var packed in report.packedAssets)
                {
                    foreach (var content in packed.contents)
                    {
                        var path = content.sourceAssetPath;
                        // Unity's built-in resources have no source path. They are real bytes in the
                        // build but there is nothing for a developer to act on, so they are left out
                        // rather than listed as a mystery entry.
                        if (string.IsNullOrEmpty(path)) continue;
                        if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                            !path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) continue;

                        perAsset.TryGetValue(path, out var running);
                        // Summed, not assigned: an asset pulled into more than one bundle is counted
                        // once per copy, which is what it costs.
                        perAsset[path] = running + (long)content.packedSize;
                    }
                }

                var data = new ArsmiBuildSizeData
                {
                    builtAtUtc = DateTime.UtcNow.ToString("u"),
                    outputPath = report.summary.outputPath,
                    totalBytes = (long)report.summary.totalSize,
                    assetCount = perAsset.Count,
                    biggest = perAsset
                        .OrderByDescending(pair => pair.Value)
                        .Take(KeepBiggest)
                        .Select(pair => new ArsmiSizeEntry { path = pair.Key, bytes = pair.Value, category = CategoryOf(pair.Key) })
                        .ToList(),
                };

                data.categories = perAsset
                    .GroupBy(pair => CategoryOf(pair.Key))
                    .Select(group => new ArsmiSizeEntry { path = group.Key, category = group.Key, bytes = group.Sum(p => p.Value) })
                    .OrderByDescending(entry => entry.bytes)
                    .ToList();

                File.WriteAllText(RecordPath, JsonUtility.ToJson(data, prettyPrint: true));
            }
            catch (Exception error)
            {
                // A size report is a convenience. Never let it be the reason a good build is
                // reported as failed.
                Debug.LogWarning($"[Arsmi] Could not record the build size report: {error.Message}");
            }
        }

        /// <summary>The last recorded build, or null when none has been made on this machine.</summary>
        public static ArsmiBuildSizeData Load()
        {
            try
            {
                if (!File.Exists(RecordPath)) return null;
                var data = JsonUtility.FromJson<ArsmiBuildSizeData>(File.ReadAllText(RecordPath));
                return data != null && data.biggest != null ? data : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Coarse buckets, by extension.
        /// </summary>
        /// <remarks>
        /// Deliberately not AssetDatabase.GetMainAssetTypeAtPath: that loads and imports every asset
        /// it is asked about, which on a large project turns a report into a multi-minute stall. The
        /// extension is already right for everything a build is made of.
        /// </remarks>
        public static string CategoryOf(string path)
        {
            var extension = Path.GetExtension(path ?? "").ToLowerInvariant();
            switch (extension)
            {
                case ".png": case ".jpg": case ".jpeg": case ".tga": case ".psd":
                case ".gif": case ".bmp": case ".tif": case ".tiff": case ".exr": case ".hdr":
                    return "Textures";
                case ".wav": case ".mp3": case ".ogg": case ".aiff": case ".aif": case ".flac":
                    return "Audio";
                case ".fbx": case ".obj": case ".blend": case ".dae": case ".3ds":
                    return "Models";
                case ".anim": case ".controller": case ".overridecontroller":
                    return "Animation";
                case ".unity":
                    return "Scenes";
                case ".prefab":
                    return "Prefabs";
                case ".mat": case ".shader": case ".shadergraph": case ".shadersubgraph":
                    return "Materials & shaders";
                case ".ttf": case ".otf":
                    return "Fonts";
                case ".cs": case ".dll":
                    return "Code";
                case ".mp4": case ".webm": case ".mov":
                    return "Video";
                default:
                    return "Other";
            }
        }

        /// <summary>"12.4 MB" — sizes are read, not calculated, so they are rounded here once.</summary>
        public static string Bytes(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) return $"{bytes / (1024f * 1024f * 1024f):0.00} GB";
            if (bytes >= 1024L * 1024L) return $"{bytes / (1024f * 1024f):0.0} MB";
            if (bytes >= 1024L) return $"{bytes / 1024f:0} KB";
            return $"{bytes} B";
        }
    }
}
