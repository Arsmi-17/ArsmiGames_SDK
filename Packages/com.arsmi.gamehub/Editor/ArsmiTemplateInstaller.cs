using System.IO;
using UnityEditor;
using UnityEngine;

// UnityEditor also has a (legacy, unrelated) PackageInfo, so importing
// UnityEditor.PackageManager wholesale makes the name ambiguous. Alias the one we mean.
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace ArsmiGames.EditorTools
{
    /// <summary>
    /// Puts the ArsmiGames WebGL template where Unity can actually see it.
    ///
    /// Unity only discovers WebGL templates in <c>Assets/WebGLTemplates/</c>. A package
    /// cannot provide one — there is no package equivalent, and no setting that points the
    /// build at one. So the template ships inside this package under
    /// <c>Editor/WebGLTemplate~/</c> (the trailing ~ keeps Unity from importing it as
    /// assets, which is what lets us treat it as plain files) and gets copied into the
    /// consuming project on load.
    ///
    /// The alternative was telling every developer to hand-copy a folder after installing
    /// the package. That is a step people forget, and forgetting it produces a game that
    /// builds, loads, renders, and cannot talk to the platform at all, with no error — the
    /// exact failure this whole package exists to make impossible. So it is automatic, and
    /// the build additionally verifies the result rather than trusting this ran.
    /// </summary>
    [InitializeOnLoad]
    public static class ArsmiTemplateInstaller
    {
        public const string TemplateName = "ArsmiGames";
        private const string SourceFolder = "Editor/WebGLTemplate~";

        private static string DestinationFolder =>
            Path.Combine(Application.dataPath, "WebGLTemplates", TemplateName);

        static ArsmiTemplateInstaller()
        {
            // Deferred: the AssetDatabase is not ready to be written to during a static
            // constructor on domain reload.
            EditorApplication.delayCall += () => Install(force: false);
        }

        [MenuItem("Arsmi Games/Reinstall WebGL template", priority = 40)]
        private static void Reinstall()
        {
            Install(force: true);
            EditorUtility.DisplayDialog(
                "Arsmi Games",
                $"The {TemplateName} WebGL template is installed at Assets/WebGLTemplates/{TemplateName}.",
                "OK");
        }

        /// <summary>Copies the template into Assets/ if it is missing or out of date.
        /// Returns true if anything was written.</summary>
        public static bool Install(bool force)
        {
            var source = SourcePath();
            if (source == null)
            {
                Debug.LogError(
                    "[Arsmi] Could not locate the package's WebGL template. The package looks damaged — " +
                    "reinstall com.arsmi.gamehub.");
                return false;
            }

            var wrote = false;
            Directory.CreateDirectory(DestinationFolder);

            foreach (var file in Directory.GetFiles(source))
            {
                var name = Path.GetFileName(file);
                var target = Path.Combine(DestinationFolder, name);

                // Only write when the content differs. Rewriting identical files on every
                // domain reload would churn the AssetDatabase and re-import for nothing.
                if (!force && File.Exists(target) && SameContent(file, target)) continue;

                File.Copy(file, target, overwrite: true);
                wrote = true;
            }

            if (wrote)
            {
                AssetDatabase.Refresh();
                Debug.Log($"[Arsmi] Installed the {TemplateName} WebGL template → Assets/WebGLTemplates/{TemplateName}");
            }
            return wrote;
        }

        /// <summary>True if the template is present in the project and matches the package.</summary>
        public static bool IsInstalled()
        {
            var source = SourcePath();
            if (source == null || !Directory.Exists(DestinationFolder)) return false;

            foreach (var file in Directory.GetFiles(source))
            {
                var target = Path.Combine(DestinationFolder, Path.GetFileName(file));
                if (!File.Exists(target) || !SameContent(file, target)) return false;
            }
            return true;
        }

        /// <summary>
        /// The package's own folder on disk. Resolved through the Package Manager rather
        /// than a hard-coded path, because a package installed from a git url lives in
        /// Library/PackageCache/&lt;name&gt;@&lt;hash&gt;/, not in Packages/.
        /// </summary>
        private static string SourcePath()
        {
            var info = PackageInfo.FindForAssembly(typeof(ArsmiTemplateInstaller).Assembly);
            if (info == null || string.IsNullOrEmpty(info.resolvedPath)) return null;

            var path = Path.Combine(info.resolvedPath, SourceFolder);
            return Directory.Exists(path) ? path : null;
        }

        private static bool SameContent(string a, string b)
        {
            // Byte-compare, not timestamps. A git checkout rewrites mtimes, and a template
            // that silently stops matching the package is the bug we are preventing.
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
}
