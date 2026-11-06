using CapstoneGame.Online;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Moves between the three screens of the game: Start Menu, Game, and Game Over.
///
/// It also carries the result of the last game over to the Game Over screen and keeps the best
/// score on this device. When the GameManager, lives, and scoring are added, the game ends by calling
/// <see cref="ShowGameOver"/> with the final score and wave.
/// </summary>
public static class GameFlow
{
    // Scene names as they appear in File > Build Profiles. The tests use these too.
    public const string StartMenuScene = "StartMenu";
    public const string GameScene = "Game";
    public const string GameOverScene = "GameOver";

    private const string HighScoreKey = "HighScore";

    /// <summary>Score of the game that just ended.</summary>
    public static int LastScore { get; private set; }

    /// <summary>Wave the player reached in the game that just ended.</summary>
    public static int LastWave { get; private set; }

    /// <summary>True when the game that just ended set a new best score.</summary>
    public static bool LastWasNewHighScore { get; private set; }

    /// <summary>Best score saved on this device.</summary>
    public static int HighScore => PlayerPrefs.GetInt(HighScoreKey, 0);

    public static void StartGame() => Load(GameScene);

    public static void ToStartMenu() => Load(StartMenuScene);

    /// <summary>Ends the current game, saves a new best score, and shows the Game Over screen.</summary>
    public static void ShowGameOver(int score, int wave)
    {
        LastScore = Mathf.Max(0, score);
        LastWave = Mathf.Max(1, wave);
        LastWasNewHighScore = LastScore > HighScore;

        if (LastWasNewHighScore)
        {
            PlayerPrefs.SetInt(HighScoreKey, LastScore);
            PlayerPrefs.Save();
        }

        // Send the game to the online leaderboard (signed-in players only; kept for later when offline).
        // Called from the Game scene, so the time since it loaded is the game's length.
        ScoreServices.Submitter.Record(LastScore, LastWave, Time.timeSinceLevelLoad);

        Load(GameOverScene);
    }

    private static void Load(string scene)
    {
        Time.timeScale = 1f; // in case the game was paused
        SceneManager.LoadScene(scene);
    }
}
