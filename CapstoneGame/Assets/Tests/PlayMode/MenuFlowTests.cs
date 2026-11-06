using System.Collections;
using System.Threading.Tasks;
using CapstoneGame.Auth;
using CapstoneGame.Online;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Play Mode checks for the screen flow: Start Menu -> Game -> Game Over -> Game or Start Menu.
/// Each menu button is pressed and the test checks which scene opens, that the Game Over screen
/// shows the result, and that every button is big enough to tap.
/// </summary>
public class MenuFlowTests
{
    private const string HighScoreKey = "HighScore";
    private bool hadHighScore;
    private int savedHighScore;
    private string savedQueue;

    [SetUp]
    public void SaveHighScore()
    {
        // The tests set high scores; keep the real one on this machine.
        hadHighScore = PlayerPrefs.HasKey(HighScoreKey);
        savedHighScore = PlayerPrefs.GetInt(HighScoreKey, 0);
        PlayerPrefs.DeleteKey(HighScoreKey);

        // Test games must never reach the real leaderboard, even if this machine is signed in: use a
        // not-configured login, a clean score queue, and an in-memory on-phone top 10.
        AuthServices.Override(SupabaseAuth.NotConfigured());
        savedQueue = PlayerPrefs.GetString(PlayerPrefsScoreQueueStore.Key, null);
        PlayerPrefs.DeleteKey(PlayerPrefsScoreQueueStore.Key);
        LocalScores.Override(new LocalScoreBoard(new MemoryLocalScoreStore()));
    }

    [TearDown]
    public void RestoreHighScore()
    {
        if (hadHighScore) PlayerPrefs.SetInt(HighScoreKey, savedHighScore);
        else PlayerPrefs.DeleteKey(HighScoreKey);

        if (string.IsNullOrEmpty(savedQueue)) PlayerPrefs.DeleteKey(PlayerPrefsScoreQueueStore.Key);
        else PlayerPrefs.SetString(PlayerPrefsScoreQueueStore.Key, savedQueue);
        PlayerPrefs.Save();

        AuthServices.Reset();
        LocalScores.Reset();
    }

    [UnityTest]
    public IEnumerator GameOverSendsScoreForSignedInPlayer()
    {
        var http = new FakeHttpTransport();
        var sessions = new MemorySessionStore(
            FakeHttpTransport.Session(System.DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), accessToken: "player-token"));
        var auth = new SupabaseAuth("https://test-project.supabase.co", "test-anon-key", http, sessions);
        AuthServices.Override(auth);
        yield return null;
        Task<AuthResult> restore = auth.RestoreSession();
        yield return new WaitUntil(() => restore.IsCompleted);
        http.Respond(201, "{\"status\":\"saved\"}");

        yield return Load(GameFlow.GameScene);
        GameFlow.ShowGameOver(1234, 3);
        yield return WaitForScene(GameFlow.GameOverScene);
        float end = Time.realtimeSinceStartup + 3f;
        while (http.Requests.Count == 0 && Time.realtimeSinceStartup < end) yield return null;

        Assert.AreEqual(1, http.Requests.Count, "Game over did not send the score.");
        StringAssert.EndsWith("/functions/v1/submit-score", http.Last.Url);
        StringAssert.Contains("\"score\":1234", http.Last.Body);
        StringAssert.Contains("\"wave\":3", http.Last.Body);
        Assert.AreEqual("Bearer player-token", http.Last.Headers["Authorization"]);
        Assert.AreEqual(0, ScoreServices.Submitter.PendingCount, "Sent game should leave the queue.");

        Assert.AreEqual(1, GameFlow.LastLocalRank);
        Assert.IsFalse(LocalScores.Board.Top[0].guest);
    }

    [UnityTest]
    public IEnumerator GuestGameIsKeptOnThePhone()
    {
        var http = new FakeHttpTransport();
        AuthServices.Override(new SupabaseAuth("https://test-project.supabase.co", "test-anon-key", http, new MemorySessionStore()));

        yield return Load(GameFlow.GameScene);
        GameFlow.ShowGameOver(50, 1);
        yield return WaitForScene(GameFlow.GameOverScene);

        Assert.IsEmpty(http.Requests, "A guest's game must not be sent.");
        Assert.AreEqual(1, ScoreServices.Submitter.GuestPendingCount, "Guest game should wait for a sign-in.");
        Assert.AreEqual(1, GameFlow.LastLocalRank);
        Assert.AreEqual(50, LocalScores.Board.Top[0].score);
        Assert.IsTrue(LocalScores.Board.Top[0].guest);
    }

    [UnityTest]
    public IEnumerator PlayStartsTheGame()
    {
        yield return Load(GameFlow.StartMenuScene);

        Button play = FindButton("Play Button");
        yield return null; // Start() selects Play
        Assert.AreEqual(play.gameObject, EventSystem.current.currentSelectedGameObject,
            "Play is not selected, so keyboard and gamepad players can't start with Enter or A.");

        play.onClick.Invoke();
        yield return WaitForScene(GameFlow.GameScene);
    }

    [UnityTest]
    public IEnumerator GameOverShowsTheResult()
    {
        GameFlow.ShowGameOver(1234, 3);
        yield return WaitForScene(GameFlow.GameOverScene);

        var screen = Object.FindAnyObjectByType<GameOverScreen>();
        Assert.IsNotNull(screen, "Game Over scene has no GameOverScreen.");
        StringAssert.Contains("1234", FindText("Score").text);
        StringAssert.Contains("3", FindText("Wave").text);
        StringAssert.Contains("NEW HIGH SCORE", FindText("High Score").text);
        Assert.AreEqual(1234, GameFlow.HighScore, "The new high score was not saved.");

        // A lower score next time keeps the best one.
        GameFlow.ShowGameOver(10, 1);
        yield return WaitForScene(GameFlow.GameOverScene);
        Assert.AreEqual(1234, GameFlow.HighScore);
        StringAssert.Contains("1234", FindText("High Score").text);
    }

    [UnityTest]
    public IEnumerator GameOverButtonsWaitBeforeResponding()
    {
        GameFlow.ShowGameOver(0, 1);
        yield return WaitForScene(GameFlow.GameOverScene);

        Button again = FindButton("Play Again Button");
        Assert.IsFalse(again.interactable,
            "Play Again responds right away, so a player still holding fire would skip the screen.");

        yield return WaitUntilReady();
        Assert.IsTrue(again.interactable);
        Assert.AreEqual(again.gameObject, EventSystem.current.currentSelectedGameObject,
            "Play Again is not selected for keyboard and gamepad players.");
    }

    [UnityTest]
    public IEnumerator PlayAgainStartsANewGame()
    {
        GameFlow.ShowGameOver(0, 1);
        yield return WaitForScene(GameFlow.GameOverScene);
        yield return WaitUntilReady();

        FindButton("Play Again Button").onClick.Invoke();
        yield return WaitForScene(GameFlow.GameScene);
    }

    [UnityTest]
    public IEnumerator MainMenuReturnsToTheStartMenu()
    {
        GameFlow.ShowGameOver(0, 1);
        yield return WaitForScene(GameFlow.GameOverScene);
        yield return WaitUntilReady();

        FindButton("Main Menu Button").onClick.Invoke();
        yield return WaitForScene(GameFlow.StartMenuScene);
    }

    [UnityTest]
    public IEnumerator MenuButtonsAreOnScreenAndLargeEnough(
        [Values(GameFlow.StartMenuScene, GameFlow.GameOverScene)] string scene)
    {
        yield return Load(scene);
        Canvas.ForceUpdateCanvases();

        // In the Editor, Play Mode can render in the Game view while reporting the Device Simulator's
        // safe area and DPI, which no real device does. Sizes can't be judged then, so skip; CI and
        // a Game-view-only or Simulator-only Editor still run the check.
        Rect safe = Screen.safeArea;
        if (Application.isEditor && (safe.xMax > Screen.width + 1 || safe.yMax > Screen.height + 1))
            Assert.Ignore($"Editor reports a {Screen.width}x{Screen.height} screen but a safe area of {safe}. " +
                          "Close the Device Simulator or the Game view and run again to check layout.");

        // FR7: touch targets at least 44 x 44 points. One point is about 1/160 inch.
        float pixelsPerPoint = Screen.dpi > 0f ? Mathf.Max(1f, Screen.dpi / 160f) : 1f;
        float minPx = 44f * pixelsPerPoint;
        var corners = new Vector3[4];

        Button[] buttons = Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
        Assert.That(buttons.Length, Is.GreaterThan(0), $"{scene} has no buttons.");

        foreach (Button b in buttons)
        {
            if (!b.gameObject.activeInHierarchy) continue;
            ((RectTransform)b.transform).GetWorldCorners(corners);
            Vector2 size = corners[2] - corners[0];

            Assert.That(corners[0].x, Is.GreaterThanOrEqualTo(0f), $"{b.name} is off the left of the screen.");
            Assert.That(corners[0].y, Is.GreaterThanOrEqualTo(0f), $"{b.name} is off the bottom of the screen.");
            Assert.That(corners[2].x, Is.LessThanOrEqualTo(Screen.width), $"{b.name} is off the right of the screen.");
            Assert.That(corners[2].y, Is.LessThanOrEqualTo(Screen.height), $"{b.name} is off the top of the screen.");
            Assert.That(Mathf.Min(size.x, size.y), Is.GreaterThanOrEqualTo(minPx),
                $"{b.name} is {size.x:F0}x{size.y:F0}px; needs at least {minPx:F0}px (44pt).");
        }
    }

    private static IEnumerator Load(string scene)
    {
        yield return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
        yield return null; // let Awake/Start run
    }

    private static IEnumerator WaitForScene(string scene, float timeout = 5f)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (SceneManager.GetActiveScene().name != scene && Time.realtimeSinceStartup < end) yield return null;
        Assert.AreEqual(scene, SceneManager.GetActiveScene().name, $"{scene} did not open.");
        yield return null; // let Awake/Start run
    }

    private static IEnumerator WaitUntilReady(float timeout = 3f)
    {
        var screen = Object.FindAnyObjectByType<GameOverScreen>();
        float end = Time.realtimeSinceStartup + timeout;
        while (!screen.IsReady && Time.realtimeSinceStartup < end) yield return null;
        Assert.IsTrue(screen.IsReady, "Game Over buttons never became active.");
        yield return null; // selection is set right after the buttons unlock
    }

    private static Button FindButton(string name)
    {
        foreach (Button b in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b.name == name) return b;
        Assert.Fail($"No button named \"{name}\" in {SceneManager.GetActiveScene().name}.");
        return null;
    }

    private static Text FindText(string name)
    {
        foreach (Text t in Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == name) return t;
        Assert.Fail($"No text named \"{name}\" in {SceneManager.GetActiveScene().name}.");
        return null;
    }
}
