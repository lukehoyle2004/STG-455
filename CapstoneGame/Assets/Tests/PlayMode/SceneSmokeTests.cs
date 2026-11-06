using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// FR5 smoke test: loads EVERY scene in the build list and lets it run.
///
/// A scene fails if it can't load, has a missing script, has no camera, or logs any error or exception
/// while running (the Test Framework fails a test on any unexpected error log). New scenes (Title,
/// Game Over, ...) are covered automatically as soon as they are added to File > Build Profiles.
/// </summary>
public class SceneSmokeTests
{
    /// <summary>How long each scene runs before it passes.</summary>
    private const float RunSeconds = 2f;

    private static IEnumerable<string> BuildScenes()
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            yield return SceneUtility.GetScenePathByBuildIndex(i);
    }

    [Test]
    public void BuildListHasAtLeastOneScene()
    {
        Assert.That(SceneManager.sceneCountInBuildSettings, Is.GreaterThan(0),
            "No scenes are in the build list (File > Build Profiles).");
    }

    [UnityTest]
    public IEnumerator SceneLoadsAndRunsWithoutErrors([ValueSource(nameof(BuildScenes))] string scenePath)
    {
        AsyncOperation load = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
        Assert.IsNotNull(load, $"Could not start loading {scenePath}.");
        yield return load;

        Scene scene = SceneManager.GetSceneByPath(scenePath);
        Assert.IsTrue(scene.isLoaded, $"{scenePath} did not load.");

        AssertNoMissingScripts(scene);
        Assert.IsNotNull(Camera.main, $"{scenePath} has no camera tagged MainCamera.");

        // Let the scene run so Start/Update code executes. Any error logged here fails the test.
        int startFrame = Time.frameCount;
        float end = Time.realtimeSinceStartup + RunSeconds;
        while (Time.realtimeSinceStartup < end) yield return null;

        Assert.That(Time.frameCount, Is.GreaterThan(startFrame), $"{scenePath} never advanced a frame.");
    }

    private static void AssertNoMissingScripts(Scene scene)
    {
        var missing = new StringBuilder();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                foreach (Component c in t.GetComponents<Component>())
                    if (c == null) missing.AppendLine($"  {GetPath(t)}");
            }
        }

        Assert.IsTrue(missing.Length == 0, $"{scene.path} has missing scripts on:\n{missing}");
    }

    private static string GetPath(Transform t) => t.parent == null ? t.name : GetPath(t.parent) + "/" + t.name;
}
