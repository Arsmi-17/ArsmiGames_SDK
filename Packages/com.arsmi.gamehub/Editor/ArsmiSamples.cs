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
    /// Imports the Kids Quiz demo and makes it runnable in one step.
    ///
    /// The Package Manager window can already import a sample, but it drops the files in
    /// Assets/Samples/… and stops there: the scene is not in Build Settings, so the next
    /// thing you do — build it — produces an empty player. This does the last mile.
    /// </summary>
    public static class ArsmiSamples
    {
        private const string PackageName = "com.arsmi.gamehub";
        private const string SampleName = "Kids Quiz Demo";
        private const string SceneFile = "ArsmiSdkDemo.unity";

        [MenuItem("Arsmi Games/Import Kids Quiz sample", priority = 20)]
        public static void ImportKidsQuiz()
        {
            var sample = Sample
                .FindByPackage(PackageName, string.Empty)
                .FirstOrDefault(s => s.displayName == SampleName);

            if (sample.Equals(default(Sample)))
            {
                EditorUtility.DisplayDialog("Arsmi Games",
                    $"Could not find the \"{SampleName}\" sample in {PackageName}.", "OK");
                return;
            }

            if (!sample.isImported && !sample.Import(Sample.ImportOptions.OverridePreviousImports))
            {
                EditorUtility.DisplayDialog("Arsmi Games", "The sample failed to import. See the Console.", "OK");
                return;
            }

            AssetDatabase.Refresh();

            var scene = FindScene();
            if (scene == null)
            {
                EditorUtility.DisplayDialog("Arsmi Games",
                    $"The sample imported, but {SceneFile} was not found in it.", "OK");
                return;
            }

            AddSceneFirst(scene);
            EditorSceneManager.OpenScene(scene);

            Debug.Log($"[Arsmi] Kids Quiz sample imported and set as scene 0 → {scene}");
        }

        private static string FindScene()
        {
            return AssetDatabase
                .FindAssets("ArsmiSdkDemo t:Scene")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileName(p) == SceneFile);
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
