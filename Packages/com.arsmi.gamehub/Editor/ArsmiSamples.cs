using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ArsmiGames.EditorTools
{
    /// <summary>
    /// Imports a package sample and makes it runnable in one step.
    ///
    /// The Package Manager window can already import a sample, but it drops the files in
    /// Assets/Samples/… and stops there: the scene is not in Build Settings, so the next
    /// thing you do — build it — produces an empty player. This does the last mile.
    /// </summary>
    public static class ArsmiSamples
    {
        private const string PackageName = "com.arsmi.gamehub";

        [MenuItem("Arsmi Games/Import Kids Quiz sample", priority = 20)]
        public static void ImportKidsQuiz()
        {
            Import("Kids Quiz Demo", "ArsmiSdkDemo.unity");
        }

        [MenuItem("Arsmi Games/Import Pocket Console sample", priority = 21)]
        public static void ImportPocketConsole()
        {
            if (!Import("Pocket Console Demo", "ArsmiPocketConsoleDemo.unity")) return;

            // The one thing the scene cannot tell you, because the other half of this sample is not
            // a Unity asset at all: the controller is HTML beside it, and it is run by the harness,
            // not by Unity. Without this line a developer presses Play, nothing happens, and there
            // is nothing on screen or in the Console suggesting a second process was expected.
            Debug.Log(
                "[Arsmi] Pocket Console sample imported. Its phone controller is the PocketController " +
                "folder beside the scene — start it from your repo root with:\n" +
                "  npm run pocket:dev \"--\" --project=<imported sample>/PocketController --unity-editor\n" +
                "then press Play. Read the sample's README.md first; it is short.");
        }

        /// <summary>
        /// Import one sample by its manifest displayName and open its scene.
        ///
        /// Both arguments come from package.json's `samples` block and the scene file inside it. A
        /// typo in either is reported to the developer rather than logged, because an import that
        /// silently half-succeeded is what sends someone looking for a bug in their own game.
        /// </summary>
        private static bool Import(string sampleName, string sceneFile)
        {
            var sample = Sample
                .FindByPackage(PackageName, string.Empty)
                .FirstOrDefault(s => s.displayName == sampleName);

            if (sample.Equals(default(Sample)))
            {
                EditorUtility.DisplayDialog("Arsmi Games",
                    $"Could not find the \"{sampleName}\" sample in {PackageName}.", "OK");
                return false;
            }

            if (!sample.isImported && !sample.Import(Sample.ImportOptions.OverridePreviousImports))
            {
                EditorUtility.DisplayDialog("Arsmi Games", "The sample failed to import. See the Console.", "OK");
                return false;
            }

            AssetDatabase.Refresh();

            var scene = FindScene(sceneFile);
            if (scene == null)
            {
                EditorUtility.DisplayDialog("Arsmi Games",
                    $"The sample imported, but {sceneFile} was not found in it.", "OK");
                return false;
            }

            AddSceneFirst(scene);
            EditorSceneManager.OpenScene(scene);

            Debug.Log($"[Arsmi] {sampleName} imported and set as scene 0 → {scene}");
            return true;
        }

        private static string FindScene(string sceneFile)
        {
            return AssetDatabase
                .FindAssets($"{Path.GetFileNameWithoutExtension(sceneFile)} t:Scene")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileName(p) == sceneFile);
        }

        /// <summary>Puts the demo at index 0. A WebGL build starts at scene 0, so a demo
        /// sitting at index 3 behind the project's own scenes would never be the thing that
        /// runs — and the build would look broken for no visible reason.</summary>
        private static void AddSceneFirst(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == scenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
