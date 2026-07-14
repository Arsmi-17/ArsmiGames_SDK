using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ArsmiGames.EditorTools
{
    /// <summary>
    /// One-time project setup.
    ///
    /// TextMeshPro ships its font assets and shaders in a .unitypackage that is *not*
    /// imported until someone opens a TMP menu and clicks through a dialog. Until that
    /// happens every TMP_Text in the project renders nothing at all, which looks exactly
    /// like a broken layout. Importing it here means a fresh clone builds correctly
    /// without anyone knowing that.
    /// </summary>
    public static class ArsmiSetup
    {
        [MenuItem("Arsmi Games/Import TextMeshPro Essentials", priority = 20)]
        public static void ImportTmpEssentials()
        {
            if (Directory.Exists("Assets/TextMesh Pro"))
            {
                Debug.Log("[Arsmi] TextMeshPro essentials are already imported.");
                return;
            }

            var package = Directory
                .GetDirectories("Library/PackageCache")
                .Select(dir => Path.Combine(dir, "Package Resources", "TMP Essential Resources.unitypackage"))
                .FirstOrDefault(File.Exists);

            if (package == null)
            {
                Debug.LogError("[Arsmi] Could not find 'TMP Essential Resources.unitypackage' in the package cache.");
                return;
            }

            Debug.Log($"[Arsmi] Importing TextMeshPro essentials from {package}");
            AssetDatabase.ImportPackage(package, false);
            AssetDatabase.Refresh();
        }
    }
}
