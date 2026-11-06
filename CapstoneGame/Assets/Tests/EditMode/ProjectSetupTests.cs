using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Fast Edit Mode checks that catch broken project setup before anything runs:
/// missing or broken build scenes, missing scripts, and pixel art that would import blurry.
/// </summary>
public class ProjectSetupTests
{
    [Test]
    public void EveryBuildSceneExists()
    {
        var problems = new StringBuilder();
        int enabled = 0;

        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            if (!s.enabled) continue;
            enabled++;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(s.path) == null)
                problems.AppendLine($"  {s.path} is in the build list but the file is missing.");
        }

        Assert.That(enabled, Is.GreaterThan(0), "No scenes are enabled in the build list.");
        Assert.IsTrue(problems.Length == 0, "Build list problems:\n" + problems);
    }

    [Test]
    public void EveryScreenIsInTheBuildInPlayOrder()
    {
        var names = new System.Collections.Generic.List<string>();
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            if (s.enabled) names.Add(System.IO.Path.GetFileNameWithoutExtension(s.path));

        foreach (string scene in new[] { GameFlow.StartMenuScene, GameFlow.GameScene, GameFlow.GameOverScene })
            Assert.Contains(scene, names, $"{scene} is not in the build list, so the game can't open it.");

        // A Login screen may come first; after it the flow is Start Menu, Game, Game Over.
        Assert.That(names.IndexOf(GameFlow.StartMenuScene), Is.LessThan(names.IndexOf(GameFlow.GameScene)),
            "The Start Menu must come before the Game scene in the build list.");
        Assert.That(names.IndexOf(GameFlow.GameScene), Is.LessThan(names.IndexOf(GameFlow.GameOverScene)),
            "The Game scene must come before Game Over in the build list.");
        Assert.That(names.IndexOf(GameFlow.StartMenuScene), Is.LessThanOrEqualTo(1),
            "The Start Menu must be the first scene, or the second after a Login scene.");
    }

    [Test]
    public void BuildScenesHaveNoMissingScripts()
    {
        var problems = new StringBuilder();
        Scene original = SceneManager.GetActiveScene();

        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            if (!s.enabled || AssetDatabase.LoadAssetAtPath<SceneAsset>(s.path) == null) continue;

            Scene scene = SceneManager.GetSceneByPath(s.path);
            bool openedHere = !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(s.path, OpenSceneMode.Additive);

            try
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                            problems.AppendLine($"  {s.path}: {t.name}");
            }
            finally
            {
                if (openedHere && scene != original) EditorSceneManager.CloseScene(scene, true);
            }
        }

        Assert.IsTrue(problems.Length == 0, "GameObjects with missing scripts:\n" + problems);
    }

    [Test]
    public void PixelArtImportsSharp()
    {
        var problems = new StringBuilder();

        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;

            if (importer.filterMode != FilterMode.Point)
                problems.AppendLine($"  {path}: Filter Mode should be Point (no filter).");
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                problems.AppendLine($"  {path}: Compression should be None.");
            if (importer.mipmapEnabled)
                problems.AppendLine($"  {path}: Generate Mip Maps should be off.");
        }

        Assert.IsTrue(problems.Length == 0, "Pixel art import problems:\n" + problems);
    }
}
